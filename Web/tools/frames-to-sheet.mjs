#!/usr/bin/env node
// frames-to-sheet.mjs —— 把 render-8dir.py 渲出来的逐帧 PNG 组装成 sheet-to-lib.mjs 认识的洋红底网格表。
//
// 为什么要这一步（不能直接把渲染图丢给 sheet-to-lib）：
//   1. 3D 渲染是 256/512px 的「大图」，要先按参考 Lib 的**每动作高度**做面积平均降采样；
//   2. 官方帧的阴影是**不透明 50% 棋盘格**（实测 #100808），必须先降到最终像素尺寸再抖动，
//      否则降采样会把棋盘糊成灰色半透明；
//   3. 网格尺寸/行序必须与 export-sheet.mjs 完全一致（行=8 方向，列=帧）。
//
// 用法：
//   node Web/tools/frames-to-sheet.mjs <framesDir> <refLayout.json> <outDir>
//        [--pad 8] [--shadow-checker 2] [--shadow-color 100808] [--no-shadow]
//        [--bg FF00FF] [--height-scale 1.0] [--hard] [--alpha-cut 96] [--with-shadow]
//
// 输出：action00-stand-d0-7.png … + sheet-layout.json（并打印后续 sheet-to-lib 命令）

import fs from 'node:fs';
import path from 'node:path';
import { decodePng, encodePng, hexToRgb, rgbToHex } from './png.mjs';
import { readLib, readImage } from './lib.mjs';

function arg(name, def) {
  const i = process.argv.indexOf(name);
  return i < 0 ? def : process.argv[i + 1];
}
const flag = (name) => process.argv.includes(name);

const framesDir = process.argv[2];
const layoutPath = process.argv[3];
const outDir = process.argv[4];
if (!framesDir || !layoutPath || !outDir) {
  console.error('用法: node Web/tools/frames-to-sheet.mjs <framesDir> <refLayout.json> <outDir> [--pad 8] [--shadow-color 100808] [--no-shadow]');
  process.exit(2);
}
const PAD = Number(arg('--pad', 8));
const CHECKER = Number(arg('--shadow-checker', 2));   // 棋盘周期（官方=2）
const SHADOW = hexToRgb(arg('--shadow-color', '100808'));   // {r,g,b}
const BG = hexToRgb(arg('--bg', 'FF00FF'));
const HEIGHT_SCALE = Number(arg('--height-scale', 1));
const HARD = process.argv.includes('--hard');   // 二值 alpha：去掉降采样糊出来的半透明边
const ALPHA_CUT = Number(arg('--alpha-cut', 96));
const useShadow = !flag('--no-shadow');

const layout = JSON.parse(fs.readFileSync(layoutPath, 'utf8'));

// 参考 Lib（可选但强烈建议）：用每帧精灵**外框高度**当目标尺寸。
// 这样 sheet-to-lib 的 scaleY≈1，可以传 --scale 1 做到 1:1 贴图 —— 阴影的棋盘抖动才不会被重采样糊掉。
const refLibPath = arg('--ref-lib', null);
let refLib = null;
if (refLibPath && fs.existsSync(refLibPath)) refLib = readLib(refLibPath);
// 参考帧的"角色高度"：排掉脚下的棋盘阴影（原版阴影是 alpha=255 的纯 #100808 深色点阵），
// 否则会把阴影算进身高，3D 出来的角色就被缩大了一截。--with-shadow 可关掉这个排除。
const SKIP_SHADOW = !process.argv.includes('--with-shadow');
function isShadowPixel(bgra, i) {
  const b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
  return b <= 40 && g <= 30 && r <= 40;
}
function refBBoxH(index) {
  if (!refLib || index == null || index < 0 || index >= refLib.count) return 0;
  const img = readImage(refLib, index);
  if (!img) return 0;
  let y0 = img.h, y1 = -1;
  for (let y = 0; y < img.h; y++) {
    for (let x = 0; x < img.w; x++) {
      const i = (y * img.w + x) * 4;
      if (img.bgra[i + 3] <= 16) continue;
      if (SKIP_SHADOW && isShadowPixel(img.bgra, i)) continue;
      if (y < y0) y0 = y;
      if (y > y1) y1 = y;
      break;
    }
  }
  return y1 < 0 ? 0 : y1 - y0 + 1;
}
const metaPath = path.join(framesDir, 'render-meta.json');
const meta = fs.existsSync(metaPath) ? JSON.parse(fs.readFileSync(metaPath, 'utf8')) : { dirs: 8 };
const DIRS = meta.dirs || 8;
fs.mkdirSync(outDir, { recursive: true });

// ---------------------------------------------------------------- 基础图像工具

function newCanvas(w, h) {
  const rgba = new Uint8Array(w * h * 4);
  for (let i = 0; i < w * h; i++) {
    rgba[i * 4] = BG.r; rgba[i * 4 + 1] = BG.g; rgba[i * 4 + 2] = BG.b; rgba[i * 4 + 3] = 255;
  }
  return { w, h, rgba };
}

function bbox(img) {
  let x0 = img.w, y0 = img.h, x1 = -1, y1 = -1;
  for (let y = 0; y < img.h; y++) {
    for (let x = 0; x < img.w; x++) {
      if (img.rgba[(y * img.w + x) * 4 + 3] > 16) {
        if (x < x0) x0 = x; if (x > x1) x1 = x;
        if (y < y0) y0 = y; if (y > y1) y1 = y;
      }
    }
  }
  return x1 < 0 ? null : { x: x0, y: y0, w: x1 - x0 + 1, h: y1 - y0 + 1 };
}

// 面积平均降采样（预乘 alpha，避免透明边缘发黑）
function resizeArea(img, dw, dh) {
  const sw = img.w, sh = img.h;
  const out = new Uint8Array(dw * dh * 4);
  const sx = sw / dw, sy = sh / dh;
  for (let y = 0; y < dh; y++) {
    const y0 = Math.floor(y * sy), y1 = Math.min(sh, Math.ceil((y + 1) * sy));
    for (let x = 0; x < dw; x++) {
      const x0 = Math.floor(x * sx), x1 = Math.min(sw, Math.ceil((x + 1) * sx));
      let r = 0, g = 0, b = 0, a = 0, n = 0;
      for (let yy = y0; yy < y1; yy++) {
        for (let xx = x0; xx < x1; xx++) {
          const i = (yy * sw + xx) * 4;
          const al = img.rgba[i + 3] / 255;
          r += img.rgba[i] * al; g += img.rgba[i + 1] * al; b += img.rgba[i + 2] * al;
          a += img.rgba[i + 3]; n++;
        }
      }
      if (!n) continue;
      const o = (y * dw + x) * 4;
      const aw = a / 255;
      out[o] = aw > 0 ? Math.round(r / aw) : 0;
      out[o + 1] = aw > 0 ? Math.round(g / aw) : 0;
      out[o + 2] = aw > 0 ? Math.round(b / aw) : 0;
      out[o + 3] = HARD ? (a / n >= ALPHA_CUT ? 255 : 0) : Math.round(a / n);
    }
  }
  return { w: dw, h: dh, rgba: out };
}

// 阴影：降到目标尺寸后抖成不透明棋盘（官方帧阴影 alpha 只有 0/255）
function ditherShadow(img, dw, dh) {
  const small = resizeArea(img, dw, dh);
  const out = new Uint8Array(dw * dh * 4);
  for (let y = 0; y < dh; y++) {
    for (let x = 0; x < dw; x++) {
      const i = (y * dw + x) * 4;
      if (small.rgba[i + 3] < 96) continue;
      if (((x + y) % CHECKER) !== 0) continue;         // 50% 棋盘
      out[i] = SHADOW.r; out[i + 1] = SHADOW.g; out[i + 2] = SHADOW.b; out[i + 3] = 255;
    }
  }
  return { w: dw, h: dh, rgba: out };
}

function blit(dst, src, dx, dy) {
  for (let y = 0; y < src.h; y++) {
    const ty = dy + y;
    if (ty < 0 || ty >= dst.h) continue;
    for (let x = 0; x < src.w; x++) {
      const tx = dx + x;
      if (tx < 0 || tx >= dst.w) continue;
      const si = (y * src.w + x) * 4, di = (ty * dst.w + tx) * 4;
      if (src.rgba[si + 3] === 0) continue;
      dst.rgba[di] = src.rgba[si]; dst.rgba[di + 1] = src.rgba[si + 1];
      dst.rgba[di + 2] = src.rgba[si + 2]; dst.rgba[di + 3] = src.rgba[si + 3];
    }
  }
}

// ---------------------------------------------------------------- 组装

const sheets = [];
const byAction = new Map(layout.actions.map((a) => [a.action, a]));

for (const act of layout.actions) {
  const rendered = (meta.actions || []).find((a) => a.action === act.action);
  if (!rendered) {
    console.log(`  跳过 action ${act.action} ${act.name}：渲染结果里没有`);
    continue;
  }
  const cells = act.cells || [];
  if (!cells.length) continue;
  // 目标高度 = 该动作参考帧里最大的画布高（= 官方帧 56~96 那种尺寸）
  const targetH = Math.max(...cells.map((c) => c.srcH || 0)) * HEIGHT_SCALE;
  const targetW = Math.max(...cells.map((c) => c.srcW || 0)) * HEIGHT_SCALE;

  // 第一步：扫出这一动作所有方向/帧的墨迹外框，取并集。
  // 关键：所有帧共用**同一个缩放系数、同一个原点** —— 之前每帧各按自己的墨迹高度缩放再底对齐，
  // 抬腿/抬手的那几帧就会整体上下抖（走路、劈砍最明显）。
  const entries = [];
  for (let d = 0; d < DIRS; d++) {
    for (let c = 0; c < act.count; c++) {
      const base = `a${String(act.action).padStart(2, '0')}-d${d}-f${c}.png`;
      const charPath = path.join(framesDir, base);
      const e = { d, c, ok: false };
      if (fs.existsSync(charPath)) {
        const ch = decodePng(fs.readFileSync(charPath));
        const cb = bbox(ch);
        if (cb) { e.char = ch; e.cb = cb; e.ok = true; }
      }
      const shPath = path.join(framesDir, base.replace('.png', '-shadow.png'));
      if (useShadow && fs.existsSync(shPath)) {
        const sh = decodePng(fs.readFileSync(shPath));
        const sb = bbox(sh);
        if (sb) { e.sh = sh; e.sb = sb; }
      }
      entries.push(e);
    }
  }
  const live = entries.filter((e) => e.ok);
  if (!live.length) { console.log(`  跳过 action ${act.action} ${act.name}：没有有效帧`); continue; }

  // 角色并集（缩放只认它）与"角色+阴影"并集（决定画布）分开算 ——
  // 混在一起算会把阴影高度也算进角色高度，角色会被缩水（之前就踩过）
  let cY1 = Infinity, cY2 = -1, uX = Infinity, uY = Infinity, uX2 = -1, uY2 = -1;
  for (const e of live) {
    cY1 = Math.min(cY1, e.cb.y); cY2 = Math.max(cY2, e.cb.y + e.cb.h);
    uX = Math.min(uX, e.cb.x); uY = Math.min(uY, e.cb.y);
    uX2 = Math.max(uX2, e.cb.x + e.cb.w); uY2 = Math.max(uY2, e.cb.y + e.cb.h);
    if (e.sb) {
      uX = Math.min(uX, e.sb.x); uY = Math.min(uY, e.sb.y);
      uX2 = Math.max(uX2, e.sb.x + e.sb.w); uY2 = Math.max(uY2, e.sb.y + e.sb.h);
    }
  }
  const refHs = live.map((e) => {
    const refCell = cells.find((x) => x.dir === e.d && x.frame === e.c) || cells[0];
    return refBBoxH(refCell?.index) || refCell?.srcH || targetH;
  }).filter((v) => v > 0).sort((a, b) => a - b);
  const refH = (refHs[Math.floor(refHs.length / 2)] || targetH) * HEIGHT_SCALE;
  const k = refH / (cY2 - cY1);                      // 整组动作共用一个缩放（只按角色高度）
  const outW = Math.max(1, Math.round((uX2 - uX) * k));
  const outH = Math.max(1, Math.round((uY2 - uY) * k));

  const sprites = [];   // 每帧合成好的小图（尺寸一致，位置才不抖）
  for (const e of entries) {
    if (!e.ok) { sprites.push(null); continue; }
    const canvas = newCanvas(outW, outH);
    if (e.sh) {
      const sw = Math.max(1, Math.round(e.sh.w * k)), sh = Math.max(1, Math.round(e.sh.h * k));
      // 图本身已经是整张渲染画布（内容仍在自己的坐标上），所以只需平移到并集原点
      blit(canvas, ditherShadow(e.sh, sw, sh), Math.round(-uX * k), Math.round(-uY * k));
    }
    const cw = Math.max(1, Math.round(e.char.w * k)), ch = Math.max(1, Math.round(e.char.h * k));
    blit(canvas, resizeArea(e.char, cw, ch), Math.round(-uX * k), Math.round(-uY * k));
    sprites.push(canvas);
  }

  const valid = sprites.filter(Boolean);
  if (!valid.length) { console.log(`  跳过 action ${act.action} ${act.name}：没有有效帧`); continue; }
  const cellW = Math.round(Math.max(...valid.map((s) => s.w))) + PAD * 2;
  const cellH = Math.round(Math.max(...valid.map((s) => s.h))) + PAD * 2;
  const sheet = newCanvas(cellW * act.count, cellH * DIRS);
  for (let d = 0; d < DIRS; d++) {
    for (let c = 0; c < act.count; c++) {
      const s = sprites[d * act.count + c];
      if (!s) continue;
      const ox = c * cellW + Math.floor((cellW - s.w) / 2);
      const oy = d * cellH + (cellH - PAD - s.h);
      blit(sheet, s, ox, oy);
    }
  }
  const name = `action${String(act.action).padStart(2, '0')}-${act.name}-d0-7.png`;
  const file = path.join(outDir, name);
  fs.writeFileSync(file, encodePng(Buffer.from(sheet.rgba), sheet.w, sheet.h));
  sheets.push({ action: act.action, name: act.name, file, cellW, cellH, count: act.count, cols: act.count, rows: DIRS });
  console.log(`  ${name}  ${sheet.w}x${sheet.h}  cell ${cellW}x${cellH}  ${act.count}帧×${DIRS}向`);
}

fs.writeFileSync(path.join(outDir, 'sheet-layout.json'), JSON.stringify({
  tool: 'frames-to-sheet.mjs',
  pad: PAD,
  shadowChecker: CHECKER,
  shadowColor: rgbToHex(SHADOW),
  bg: rgbToHex(BG),
  sheets,
}, null, 2));

console.log(`\n共 ${sheets.length} 张表 → ${outDir}`);
if (sheets.length) {
  console.log('\n下一步（逐动作贴回，参考 Lib 的帧由 sheet-to-lib.mjs 负责对齐）：');
  const ref = arg('--ref-lib', '<参考Lib>');
  console.log(`  node Web/tools/sheet-to-lib.mjs "${ref}" "${sheets[0].file}" out.Lib ${sheets[0].action} <方向起始帧> \\`);
  console.log('       --cellw ' + sheets[0].cellW + ' --cellh ' + sheets[0].cellH + ' --fitx --fresh --posterize 6 --quantize 24');
  console.log('  或直接跑编排脚本： node Web/tools/3d-build-lib.mjs --frames ' + framesDir + ' --ref "' + ref + '" --out my.Lib');
}
