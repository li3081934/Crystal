#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
render-8dir.py —— Blender 无头渲染：把 3D 模型渲成 Mir2 需要的「8 方向 × N 帧」序列帧。

这是官方美术管线的复刻（WeMade 2001：3D 制作 → 8 方向预渲染 → 降色手修 → .wil/.lib）。
社区（LOMCN）十几年来的做法与我们一致：
  · 正交相机 + 固定仰角（约 30°~35°），绕 Z 轴每 45° 渲染一次 = 8 个方向
  · 光照固定在**世界空间**（不跟模型转），这样 8 个方向的高光方向在屏幕上是恒定的
  · 透明背景 PNG，逐帧输出；阴影单独出一遍，回到 Node 侧做 50% 棋盘抖动
  · 偏移/画布不对齐是最大的坑 —— 本仓库用「照抄参考 Lib」的办法绕开（sheet-to-lib.mjs）

用法（headless）：
  blender -b --python Web/tools/render-8dir.py -- \
      --config docs/art/petboon/3d/rig-078.json \
      --blend  my_rig.blend            # 或者 --import model.glb / model.fbx
      --out    docs/art/petboon/3d/render-078 \
      [--only stand,walk] [--skip-existing] [--res 512] [--samples 16]
      [--elev 33] [--yaw0 180] [--yawstep -45] [--ortho auto|1.9]
      [--engine auto|eevee|workbench] [--shadow ellipse|none] [--shadow-scale 0.55]
      [--list]

自检（不需要任何模型，内置一个方块骷髅 + 6 个动作，用来验证整条管线）：
  blender -b --python Web/tools/render-8dir.py -- --selftest --out /tmp/selftest-078

方向约定（与 export-sheet.mjs 的行序一致）：
  行 0 = 上(背对镜头)  1 = 右上  2 = 右  3 = 右下
  行 4 = 下(正面朝镜头) 5 = 左下  6 = 左  7 = 左上
  yaw(d) = yaw0 + d * yawstep（度，绕世界 Z 轴）。模型默认朝 -Y 时 yaw0=180 表示背对镜头。
  用 --list 或看输出的 8 行总览图核对；行 4 必须是正面。

输出：
  <out>/a{action:02d}-d{dir}-f{frame}.png          角色（RGBA，透明背景）
  <out>/a{action:02d}-d{dir}-f{frame}-shadow.png   阴影（RGBA，纯黑；--shadow none 时无）
  <out>/render-meta.json                           本次渲染的全部参数，供 frames-to-sheet.mjs 使用
"""

import bpy        # noqa: F401  (Blender 内置)
import bmesh      # noqa: F401
import json
import math
import os
import sys
from mathutils import Vector, Euler

# ---------------------------------------------------------------- 参数解析

def parse_argv():
    argv = sys.argv
    argv = argv[argv.index("--") + 1:] if "--" in argv else []
    opt = {
        "config": None, "blend": None, "import": None, "out": None,
        "only": None, "skip_existing": False, "list": False, "selftest": False,
        # res/samples 默认 None = 交给配置文件决定；两边都没有时用 512 / 32
        "res": None, "samples": None,
        "elev": None, "yaw0": None, "yawstep": None,
        "ortho": "auto", "engine": "auto", "shadow": None, "shadow_scale": None,
        "fit_action": None, "fit_height": 0.72,
        "sun": None, "ambient": None, "fill": None,
    }
    i = 0
    while i < len(argv):
        a = argv[i]
        nxt = (lambda: argv[i + 1]) if i + 1 < len(argv) else (lambda: None)
        if a == "--config": opt["config"] = nxt(); i += 2
        elif a == "--blend": opt["blend"] = nxt(); i += 2
        elif a == "--import": opt["import"] = nxt(); i += 2
        elif a == "--out": opt["out"] = nxt(); i += 2
        elif a == "--only": opt["only"] = nxt(); i += 2
        elif a == "--res": opt["res"] = nxt(); i += 2
        elif a == "--samples": opt["samples"] = int(nxt()); i += 2
        elif a == "--elev": opt["elev"] = float(nxt()); i += 2
        elif a == "--yaw0": opt["yaw0"] = float(nxt()); i += 2
        elif a == "--yawstep": opt["yawstep"] = float(nxt()); i += 2
        elif a == "--ortho": opt["ortho"] = nxt(); i += 2
        elif a == "--engine": opt["engine"] = nxt(); i += 2
        elif a == "--shadow": opt["shadow"] = nxt(); i += 2
        elif a == "--shadow-scale": opt["shadow_scale"] = float(nxt()); i += 2
        elif a == "--sun": opt["sun"] = float(nxt()); i += 2
        elif a == "--ambient": opt["ambient"] = float(nxt()); i += 2
        elif a == "--fill": opt["fill"] = float(nxt()); i += 2
        elif a == "--fit-action": opt["fit_action"] = nxt(); i += 2
        elif a == "--fit-height": opt["fit_height"] = float(nxt()); i += 2
        elif a == "--skip-existing": opt["skip_existing"] = True; i += 1
        elif a == "--list": opt["list"] = True; i += 1
        elif a == "--selftest": opt["selftest"] = True; i += 1
        else:
            die("未知参数: %s" % a)
    return opt


def die(msg):
    sys.stderr.write("[render-8dir] ERROR: %s\n" % msg)
    sys.exit(2)


def log(msg):
    print("[render-8dir] %s" % msg, flush=True)


# ---------------------------------------------------------------- 场景装载

def load_scene(opt):
    if opt["blend"]:
        bpy.ops.wm.open_mainfile(filepath=os.path.abspath(opt["blend"]))
        log("打开 blend: %s" % opt["blend"])
    elif opt["import"]:
        path = os.path.abspath(opt["import"])
        ext = os.path.splitext(path)[1].lower()
        if ext in (".glb", ".gltf"):
            bpy.ops.import_scene.gltf(filepath=path)
        elif ext == ".fbx":
            bpy.ops.import_scene.fbx(filepath=path)
        elif ext == ".obj":
            bpy.ops.import_scene.obj(filepath=path)
        else:
            die("不支持的模型格式: %s" % ext)
        log("导入模型: %s" % path)
    elif not opt["selftest"]:
        die("需要 --blend / --import 之一（或用 --selftest 跑内置自检）")


def all_meshes():
    return [o for o in bpy.data.objects if o.type == "MESH"]


def all_armatures():
    return [o for o in bpy.data.objects if o.type == "ARMATURE"]


def anim_target(meshes=None):
    """动作要赋给谁：优先骨架；没有骨架时退化成"挂着动画数据的对象"（单件道具也适用）。"""
    arms = all_armatures()
    if arms:
        return arms[0]
    for ob in bpy.data.objects:
        if ob.animation_data and ob.animation_data.action:
            return ob
    for ob in (meshes or []):
        if ob.animation_data:
            return ob
    return None


# ---------------------------------------------------------------- 自检用的方块骷髅

SELFTEST_PARTS = [
    # name,      size(xyz),           location,             color(rgb)
    ("Pelvis", (0.36, 0.20, 0.18), (0.0, 0.0, 0.86), (0.70, 0.67, 0.56)),
    ("Torso", (0.40, 0.22, 0.42), (0.0, 0.0, 1.14), (0.70, 0.67, 0.56)),
    ("Head", (0.24, 0.24, 0.24), (0.0, 0.0, 1.46), (0.70, 0.66, 0.54)),
    ("Face", (0.16, 0.03, 0.10), (0.0, -0.12, 1.46), (0.75, 0.10, 0.10)),
    ("Helm", (0.30, 0.30, 0.14), (0.0, 0.0, 1.60), (0.13, 0.15, 0.22)),
    ("ArmL", (0.10, 0.10, 0.46), (-0.25, 0.0, 1.10), (0.70, 0.67, 0.56)),
    ("ArmR", (0.10, 0.10, 0.46), (0.25, 0.0, 1.10), (0.70, 0.67, 0.56)),
    ("LegL", (0.12, 0.12, 0.62), (-0.10, 0.0, 0.44), (0.70, 0.67, 0.56)),
    ("LegR", (0.12, 0.12, 0.62), (0.10, 0.0, 0.44), (0.70, 0.67, 0.56)),
    ("Sword", (0.05, 0.05, 0.70), (0.34, -0.06, 1.16), (0.85, 0.65, 0.18)),
]

# 每个动作：一帧一个姿态；姿态 = {part: (rx, ry, rz) 度, 可选 "loc": (x,y,z) 偏移}
def _pose(**kw):
    return kw


SELFTEST_ACTIONS = [
    {"action": 0, "name": "stand", "count": 4, "poses": [
        _pose(Torso=(0, 0, 0), ArmL=(6, 0, 0), ArmR=(-6, 0, 0)),
        _pose(Torso=(1.5, 0, 0), ArmL=(9, 0, 0), ArmR=(-9, 0, 0)),
        _pose(Torso=(2.5, 0, 0), ArmL=(11, 0, 0), ArmR=(-11, 0, 0)),
        _pose(Torso=(1.5, 0, 0), ArmL=(9, 0, 0), ArmR=(-9, 0, 0)),
    ]},
    {"action": 1, "name": "walk", "count": 6, "poses": [
        _pose(LegL=(-22, 0, 0), LegR=(22, 0, 0), ArmL=(16, 0, 0), ArmR=(-16, 0, 0)),
        _pose(LegL=(-10, 0, 0), LegR=(10, 0, 0), ArmL=(8, 0, 0), ArmR=(-8, 0, 0), Torso=(2, 0, 0)),
        _pose(LegL=(8, 0, 0), LegR=(-8, 0, 0), ArmL=(-6, 0, 0), ArmR=(6, 0, 0)),
        _pose(LegL=(22, 0, 0), LegR=(-22, 0, 0), ArmL=(-16, 0, 0), ArmR=(16, 0, 0)),
        _pose(LegL=(10, 0, 0), LegR=(-10, 0, 0), ArmL=(-8, 0, 0), ArmR=(8, 0, 0), Torso=(2, 0, 0)),
        _pose(LegL=(-8, 0, 0), LegR=(8, 0, 0), ArmL=(6, 0, 0), ArmR=(-6, 0, 0)),
    ]},
    {"action": 9, "name": "attack", "count": 6, "poses": [
        _pose(ArmR=(-40, 0, 25), Torso=(0, 0, -6)),
        _pose(ArmR=(-70, 0, 40), Torso=(-4, 0, -10)),
        _pose(ArmR=(-20, 0, 10), Torso=(4, 0, 6)),
        _pose(ArmR=(45, 0, -30), Torso=(8, 0, 14), LegL=(-12, 0, 0), LegR=(12, 0, 0)),
        _pose(ArmR=(30, 0, -18), Torso=(5, 0, 9), LegL=(-8, 0, 0), LegR=(8, 0, 0)),
        _pose(ArmR=(6, 0, 0), Torso=(0, 0, 0)),
    ]},
    {"action": 18, "name": "struck", "count": 2, "poses": [
        _pose(Torso=(-14, 0, 0), ArmL=(-25, 0, 0), ArmR=(-25, 0, 0)),
        _pose(Torso=(-6, 0, 0), ArmL=(-10, 0, 0), ArmR=(-10, 0, 0)),
    ]},
    {"action": 21, "name": "die", "count": 10, "poses": [
        _pose(Torso=(-10, 0, 0)),
        _pose(Torso=(-22, 0, 5)),
        _pose(Torso=(-38, 0, 10), Loc=(0, 0.02, -0.02)),
        _pose(Torso=(-56, 0, 14), Loc=(0, 0.05, -0.08)),
        _pose(Torso=(-74, 0, 16), Loc=(0, 0.08, -0.18)),
        _pose(Torso=(-84, 0, 16), Loc=(0, 0.10, -0.30)),
        _pose(Torso=(-88, 0, 14), Loc=(0, 0.11, -0.44)),
        _pose(Torso=(-90, 0, 12), Loc=(0, 0.11, -0.56)),
        _pose(Torso=(-90, 0, 10), Loc=(0, 0.11, -0.64)),
        _pose(Torso=(-90, 0, 8), Loc=(0, 0.11, -0.68)),
    ]},
    {"action": 22, "name": "corpse", "count": 1, "poses": [
        _pose(Torso=(-90, 0, 6), Loc=(0, 0.11, -0.70)),
    ]},
]


def build_selftest():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    parts = {}
    for name, size, loc, colour in SELFTEST_PARTS:
        bpy.ops.mesh.primitive_cube_add(size=1.0, location=loc)
        ob = bpy.context.object
        ob.name = name
        ob.scale = Vector(size)
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        mat = bpy.data.materials.new("M_" + name)
        mat.use_nodes = True
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        bsdf.inputs["Base Color"].default_value = (colour[0], colour[1], colour[2], 1.0)
        bsdf.inputs["Roughness"].default_value = 0.85
        if "Metallic" in bsdf.inputs:
            bsdf.inputs["Metallic"].default_value = 0.0
        ob.data.materials.append(mat)
        parts[name] = ob
    log("自检模型：%d 个方块部件" % len(parts))
    return parts


def apply_selftest_pose(parts, action, index):
    poses = action.get("poses") or []
    pose = poses[index % len(poses)] if poses else {}
    for name, ob in parts.items():
        vals = pose.get(name, (0, 0, 0))
        base = dict((p[0], Vector(p[2])) for p in SELFTEST_PARTS)[name]
        loc = Vector(pose.get("Loc", (0, 0, 0))) if name == "Torso" else Vector((0, 0, 0))
        ob.rotation_mode = "XYZ"
        ob.rotation_euler = Euler([math.radians(v) for v in vals], "XYZ")
        # 旋转绕自身底部（枢轴在脚/腰），这样摆腿摆臂看起来才像动画而不是整体漂移
        pivot = Vector(base)
        offset = ob.location.copy()
        ob.location = pivot + loc
        ob.delta_location = Vector((0, 0, 0))
        if name in ("Torso", "Head", "Face", "Helm"):
            pass
        ob.location = offset + loc


# ---------------------------------------------------------------- 相机 / 灯光

def setup_camera(target, ortho_scale, elev_deg):
    cam_data = bpy.data.cameras.new("MirCam")
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = ortho_scale
    cam_data.lens = 50
    cam = bpy.data.objects.new("MirCam", cam_data)
    bpy.context.scene.collection.objects.link(cam)
    e = math.radians(elev_deg)
    direction = Vector((0.0, math.cos(e), -math.sin(e)))   # 朝 +Y 且向下压 e 度
    dist = 12.0
    cam.location = Vector(target) - direction * dist
    cam.rotation_mode = "XYZ"
    cam.rotation_euler = Euler((math.radians(90.0 - elev_deg), 0.0, 0.0), "XYZ")
    bpy.context.scene.camera = cam
    return cam


def setup_light(sun_energy=2.2, ambient=0.35, fill=0.5):
    """世界空间固定光：屏幕左上、略偏前。关键：**不要** parent 到旋转 rig 上，
    这样 8 个方向在屏幕上的高光方向才一致（官方美术就是这个做法）。
    另加一盏很弱的补光（从右前方），避免暗面死黑又保留明暗对比。"""
    data = bpy.data.lights.new("MirSun", type="SUN")
    data.energy = sun_energy
    data.angle = math.radians(8)
    sun = bpy.data.objects.new("MirSun", data)
    bpy.context.scene.collection.objects.link(sun)
    frm = Vector((-0.8, -1.0, 1.3))
    direction = (-frm).normalized()
    sun.rotation_mode = "QUATERNION"
    sun.rotation_quaternion = direction.to_track_quat("-Z", "Y")
    # 补一点环境光，避免背面全黑（官方帧的暗部是灰棕而不是纯黑）
    world = bpy.context.scene.world or bpy.data.worlds.new("MirWorld")
    bpy.context.scene.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    if bg:
        bg.inputs[0].default_value = (0.26, 0.27, 0.32, 1.0)
        bg.inputs[1].default_value = ambient
    if fill > 0:
        fd = bpy.data.lights.new("MirFill", type="SUN")
        fd.energy = fill
        fd.angle = math.radians(25)
        fo = bpy.data.objects.new("MirFill", fd)
        bpy.context.scene.collection.objects.link(fo)
        fd_dir = (-Vector((0.9, -0.6, 0.5))).normalized()
        fo.rotation_mode = "QUATERNION"
        fo.rotation_quaternion = fd_dir.to_track_quat("-Z", "Y")
    return sun


def setup_render(res_x, res_y, samples, engine):
    scene = bpy.context.scene
    candidates = {
        "eevee": ["BLENDER_EEVEE_NEXT", "BLENDER_EEVEE"],
        "workbench": ["BLENDER_WORKBENCH"],
    }
    chosen = None
    order = candidates.get(engine) or (candidates["eevee"] + candidates["workbench"])
    for name in order:
        try:
            scene.render.engine = name
            chosen = name
            break
        except Exception:
            continue
    if chosen is None:
        die("找不到可用的渲染引擎")
    if chosen.startswith("BLENDER_EEVEE"):
        ee = scene.eevee
        for attr in ("taa_render_samples", "taa_samples"):
            if hasattr(ee, attr):
                try:
                    setattr(ee, attr, samples)
                except Exception:
                    pass
        if hasattr(ee, "use_gtao"):
            ee.use_gtao = False
        if hasattr(ee, "use_bloom"):
            ee.use_bloom = False
        # EEVEE Next：开屏幕空间光追 → 肋骨/盔甲缝隙有接触阴影，像素画才不"糊成一片白"
        for attr, val in (("use_raytracing", True), ("use_shadows", True)):
            if hasattr(ee, attr):
                try:
                    setattr(ee, attr, val)
                except Exception:
                    pass
        rt = getattr(ee, "ray_tracing_options", None)
        if rt is not None:
            for attr, val in (("use_denoise", True), ("screen_trace_quality", 0.8),
                              ("screen_trace_thickness", 0.2), ("trace_max_roughness", 0.6)):
                if hasattr(rt, attr):
                    try:
                        setattr(rt, attr, val)
                    except Exception:
                        pass
        sh = getattr(ee, "shadow_ray_count", None)
        if sh is not None:
            try:
                ee.shadow_ray_count = 2
                ee.shadow_step_count = 6
            except Exception:
                pass
    else:
        shading = scene.display.shading
        shading.light = "STUDIO"
        shading.color_type = "MATERIAL"
        shading.show_shadows = False
    scene.render.resolution_x = res_x
    scene.render.resolution_y = res_y
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGBA"
    scene.render.image_settings.color_depth = "8"
    scene.render.use_file_extension = True
    # 关键：关掉输出抖动，否则 8bit PNG 会被加上噪点 → posterize 后变成脏点
    try:
        scene.render.dither_intensity = 0.0
    except Exception:
        pass
    # 像素画必须关掉胶片式色彩映射，否则颜色会被 AgX/Filmic 压灰
    try:
        scene.view_settings.view_transform = "Standard"
        scene.view_settings.look = "None"
        scene.view_settings.exposure = 0.0
        scene.view_settings.gamma = 1.0
    except Exception:
        pass
    log("渲染引擎: %s  %dx%d  samples=%s" % (chosen, res_x, res_y, samples))
    return chosen


# ---------------------------------------------------------------- 包围盒 / 自动取景

def world_bbox(objects):
    dg = bpy.context.evaluated_depsgraph_get()
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for ob in objects:
        ev = ob.evaluated_get(dg)
        try:
            me = ev.to_mesh()
        except Exception:
            continue
        if me is None:
            continue
        mw = ev.matrix_world
        for v in me.vertices:
            p = mw @ v.co
            lo = Vector((min(lo.x, p.x), min(lo.y, p.y), min(lo.z, p.z)))
            hi = Vector((max(hi.x, p.x), max(hi.y, p.y), max(hi.z, p.z)))
        ev.to_mesh_clear()
    return lo, hi


def bbox_height(objects):
    lo, hi = world_bbox(objects)
    return max(1e-6, hi.z - lo.z), lo, hi


# ---------------------------------------------------------------- 方向 rig

def ensure_yaw_rig(root_objects):
    empty = bpy.data.objects.get("MirYaw")
    if empty is None:
        empty = bpy.data.objects.new("MirYaw", None)
        bpy.context.scene.collection.objects.link(empty)
    empty.location = (0, 0, 0)
    empty.rotation_mode = "XYZ"
    for ob in root_objects:
        if ob.parent is None and ob is not empty:
            ob.parent = empty
            ob.matrix_parent_inverse = empty.matrix_world.inverted()
    return empty


def set_yaw(empty, deg):
    empty.rotation_euler = Euler((0.0, 0.0, math.radians(deg)), "XYZ")
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- 阴影

def make_shadow(scale_w):
    """一块压扁的圆，作为"影子"；Node 侧会把它抖成 50% 棋盘（官方帧的阴影就是棋盘格）。"""
    old = bpy.data.objects.get("MirShadow")
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    bpy.ops.mesh.primitive_circle_add(vertices=24, radius=0.5, fill_type="NGON", location=(0, 0, 0))
    ob = bpy.context.object
    ob.name = "MirShadow"
    ob.scale = Vector((scale_w, scale_w * 0.72, 1.0))
    ob.rotation_euler = Euler((math.radians(90), 0, 0), "XYZ")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    mat = bpy.data.materials.new("M_MirShadow")
    mat.use_nodes = True
    nt = mat.node_tree
    for node in list(nt.nodes):
        if node.type != "OUTPUT_MATERIAL":
            nt.nodes.remove(node)
    out = nt.nodes[0]
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs[0].default_value = (0.0, 0.0, 0.0, 1.0)
    em.inputs[1].default_value = 1.0
    nt.links.new(em.outputs[0], out.inputs[0])
    ob.data.materials.append(mat)
    ob.hide_render = True
    return ob


# ---------------------------------------------------------------- 主流程

def action_list_from_config(cfg):
    acts = cfg.get("actions") or []
    out = []
    for i, a in enumerate(acts):
        out.append({
            "action": int(a.get("action", i)),
            "name": a.get("name") or ("action%d" % a.get("action", i)),
            "clip": a.get("clip"),
            "start": int(a.get("start", 1)),
            "count": int(a.get("count", 1)),
        })
    return out


def assign_action(armature_ob, clip, frame):
    ad = armature_ob.animation_data or armature_ob.animation_data_create()
    act = bpy.data.actions.get(clip)
    if act is None:
        die("找不到动画 %s（用 --list 查看现有 actions）" % clip)
    try:
        ad.action = act
    except Exception as exc:
        die("无法赋动画 %s: %s" % (clip, exc))
    # Blender 4.4+ 的 slotted action 需要显式选 slot
    if hasattr(ad, "action_slot") and getattr(act, "slots", None):
        try:
            ad.action_slot = act.slots[0]
        except Exception:
            pass
    bpy.context.scene.frame_set(int(frame))
    bpy.context.view_layer.update()


def main():
    opt = parse_argv()

    if opt["list"]:
        load_scene(opt)
        log("对象: %s" % ", ".join("%s(%s)" % (o.name, o.type) for o in bpy.data.objects))
        log("动画: %s" % ", ".join(a.name for a in bpy.data.actions))
        return

    cfg = {}
    if opt["config"]:
        with open(os.path.abspath(opt["config"]), "r", encoding="utf-8") as fh:
            cfg = json.load(fh)
        log("配置: %s" % opt["config"])

    out_dir = os.path.abspath(opt["out"] or cfg.get("out") or "render-8dir")
    os.makedirs(out_dir, exist_ok=True)

    yaw0 = opt["yaw0"] if opt["yaw0"] is not None else float(cfg.get("yaw0", 180.0))
    yawstep = opt["yawstep"] if opt["yawstep"] is not None else float(cfg.get("yawStep", -45.0))
    elev = opt["elev"] if opt["elev"] is not None else float(cfg.get("elevation", 33.0))
    shadow_mode = opt["shadow"] or cfg.get("shadow", "ellipse")
    shadow_scale = opt["shadow_scale"] if opt["shadow_scale"] is not None else float(cfg.get("shadowScale", 0.55))
    res = opt["res"] or str(cfg.get("res", "512"))
    samples = opt["samples"] if opt["samples"] is not None else int(cfg.get("samples", 32))
    res_x, res_y = [int(v) for v in (res.lower().split("x") if "x" in res else [res, res])]

    parts = None
    if opt["selftest"]:
        parts = build_selftest()
        actions = [{"action": a["action"], "name": a["name"], "clip": None,
                    "start": 0, "count": a["count"], "poses": a["poses"]} for a in SELFTEST_ACTIONS]
    else:
        load_scene(opt)
        actions = action_list_from_config(cfg)
        if not actions:
            die("配置里没有 actions（或该 blend 里没有可渲染的动作）")

    if opt["only"]:
        wanted = set(x.strip() for x in opt["only"].split(",") if x.strip())
        actions = [a for a in actions if a["name"] in wanted or str(a["action"]) in wanted]
        if not actions:
            die("--only 没匹配到任何动作")

    meshes = all_meshes()
    if not meshes:
        die("场景里没有 mesh 对象")

    # 参与渲染的根对象：所有没有父级的东西（骨架、单件道具、自检方块）。
    # 注意不能只取 mesh —— 模型部件都 parent 到骨架，那样 yaw rig 会一个都没转到（8 个方向渲出来一模一样）
    roots = [o for o in bpy.data.objects
             if o.parent is None and o.type in {"MESH", "ARMATURE", "EMPTY"} and o.name != "MirYaw"]
    yaw_rig = None if opt["selftest"] else ensure_yaw_rig(roots)
    if opt["selftest"]:
        yaw_rig = bpy.data.objects.new("MirYaw", None)
        bpy.context.scene.collection.objects.link(yaw_rig)
        for ob in roots:
            ob.parent = yaw_rig
            ob.matrix_parent_inverse = yaw_rig.matrix_world.inverted()

    sun_e = opt["sun"] if opt["sun"] is not None else float(cfg.get("sun", 2.0))
    amb = opt["ambient"] if opt["ambient"] is not None else float(cfg.get("ambient", 0.12))
    fil = opt["fill"] if opt["fill"] is not None else float(cfg.get("fill", 0.22))
    setup_light(sun_e, amb, fil)
    engine = setup_render(res_x, res_y, samples, opt["engine"])

    # 取景：用 --fit-action（默认第一个动作）的第一帧高度算一个**全局固定**的 ortho_scale，
    # 保证所有动作/方向的比例一致（官方就是这么锁死相机的）。
    fit_action = opt["fit_action"] or actions[0]["name"]
    fit = next((a for a in actions if a["name"] == fit_action), actions[0])
    if opt["selftest"]:
        apply_selftest_pose(parts, fit, 0)
    elif fit.get("clip"):
        tgt = anim_target(meshes)
        if tgt is None:
            die("场景里找不到可赋动画的对象（需要骨架，或一个带 action 的对象）")
        assign_action(tgt, fit["clip"], fit.get("start", 1))
    set_yaw(yaw_rig, yaw0)
    h, lo, hi = bbox_height(meshes)
    if str(opt["ortho"]).lower() == "auto":
        ortho_scale = h / max(0.05, opt["fit_height"])
    else:
        ortho_scale = float(opt["ortho"])
    target = Vector(((lo.x + hi.x) / 2.0, (lo.y + hi.y) / 2.0, lo.z + h * 0.5))
    log("取景: 高 %.3f 世界单位 → ortho_scale %.3f（目标高度占比 %.0f%%）"
        % (h, ortho_scale, opt["fit_height"] * 100))
    cam = setup_camera(target, ortho_scale, elev)

    shadow_ob = None
    if shadow_mode != "none":
        shadow_ob = make_shadow(max(0.05, (hi.x - lo.x) * shadow_scale))
        shadow_ob.location = Vector(((lo.x + hi.x) / 2.0, (lo.y + hi.y) / 2.0, lo.z + 0.002))

    total = 0
    for act in actions:
        for i in range(act["count"]):
            if parts is not None:
                apply_selftest_pose(parts, act, i)
            elif act.get("clip"):
                tgt = anim_target(meshes)
                if tgt is not None:
                    assign_action(tgt, act["clip"], act["start"] + i)
            bpy.context.view_layer.update()
            for d in range(8):
                set_yaw(yaw_rig, yaw0 + d * yawstep)
                base = "a%02d-d%d-f%d" % (act["action"], d, i)
                char_path = os.path.join(out_dir, base + ".png")
                if not (opt["skip_existing"] and os.path.exists(char_path)):
                    bpy.context.scene.render.filepath = char_path
                    bpy.ops.render.render(write_still=True)
                    total += 1
                if shadow_ob is not None:
                    sh_path = os.path.join(out_dir, base + "-shadow.png")
                    if not (opt["skip_existing"] and os.path.exists(sh_path)):
                        for ob in meshes:
                            ob.hide_render = True
                        shadow_ob.hide_render = False
                        bpy.context.scene.render.filepath = sh_path
                        bpy.ops.render.render(write_still=True)
                        for ob in meshes:
                            ob.hide_render = False
                        shadow_ob.hide_render = True
        log("动作 %02d %-8s %d 帧 × 8 方向" % (act["action"], act["name"], act["count"]))

    meta = {
        "tool": "render-8dir.py",
        "blender": bpy.app.version_string,
        "engine": engine,
        "res": [res_x, res_y],
        "samples": samples,
        "elevation": elev,
        "yaw0": yaw0,
        "yawStep": yawstep,
        "orthoScale": ortho_scale,
        "fitAction": fit_action,
        "fitHeight": opt["fit_height"],
        "light": {"sun": sun_e, "ambient": amb, "fill": fil, "space": "world-fixed"},
        "shadow": shadow_mode,
        "shadowScale": shadow_scale,
        "selftest": bool(opt["selftest"]),
        "dirs": 8,
        "actions": [{"action": a["action"], "name": a["name"], "count": a["count"], "start": a.get("start", 0)} for a in actions],
        "rendered": total,
    }
    with open(os.path.join(out_dir, "render-meta.json"), "w", encoding="utf-8") as fh:
        json.dump(meta, fh, indent=2, ensure_ascii=False)
    log("完成：%d 张 → %s" % (total, out_dir))


if __name__ == "__main__":
    main()
