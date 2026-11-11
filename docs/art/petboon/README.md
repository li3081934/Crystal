# 白骨将军（召唤骷髅符文皮肤）美术流程

目标：符文「召唤·白骨将军」生效时，把宠物骷髅换成"披甲持骨刃"的版本，风格与原版 16-bit 手绘一致。

## 工具链（全部零依赖 node 脚本，`Web/tools/`）

| 脚本 | 作用 |
|---|---|
| `sheet-from-lib.mjs` | 把 `022.Lib`（原版骷髅）某个动作的若干方向拼成网格精灵表 PNG（行=方向，列=帧，格子默认 256px，背景铺纯洋红 `#FF00FF`） |
| `gen-image.mjs` | 调 OpenRouter `tencent/hy-image-v3.5-preview`，`--ref` 把精灵表当 `input_references` 传进去做"原位改画" |
| `sheet-to-lib.mjs` | 把生成结果按格子切回来：抠洋红/紫边 → 按参考帧精灵高度缩放（面积平均）→ 底部对齐贴回参考帧画布 → 保留原阴影与帧表。会自动跳过尺寸异常（模型偶尔放大一格）的格子 |
| `dump-lib.mjs` / `montage-lib.mjs` / `pack-lib.mjs` / `recolor-lib.mjs` | 导帧、总览图、把 PNG 帧打包成 Lib、整体换色 |

## 实际操作

```bash
# 1. 导出原版骷髅"站立"动作的 4 个方向（行=方向，列=4 帧）
node Web/tools/sheet-from-lib.mjs Build/Client/Release/Data/Monster/022.Lib 0 0 4 /tmp/sheet-0-3.png 256 '#FF00FF'
node Web/tools/sheet-from-lib.mjs Build/Client/Release/Data/Monster/022.Lib 0 4 4 /tmp/sheet-4-7.png 256 '#FF00FF'

# 2. 交给模型原位改画（提示词见 prompt-sheet.txt）
node Web/tools/gen-image.mjs --prompt docs/art/petboon/prompt-sheet.txt --ref /tmp/sheet-0-3.png \
     --out /tmp/gen-0-3.png --size 1024x1024 --n 1 --seed 90210

# 3. 切回 Lib（第二半要基于第一半的结果继续叠加）
node Web/tools/sheet-to-lib.mjs Build/Client/Release/Data/Monster/022.Lib /tmp/gen-0-3.png \
     /tmp/022-a.Lib 0 0 --cell 256 --posterize 12 --quantize 24
node Web/tools/sheet-to-lib.mjs /tmp/022-a.Lib /tmp/gen-4-7.png \
     /tmp/022-b.Lib 0 4 --cell 256 --posterize 12 --quantize 24

# 4. 装进客户端（编号 = RuneInfo.PetSkin，当前 200）
cp /tmp/022-b.Lib Build/Client/Release/Data/Monster/200.Lib
```

## 提示词要点（`prompt-sheet.txt`）

- 强调"同一个角色的换装"，保持 4×4 网格、格子尺寸、朝向、姿势、脚线
- **必须删掉原图的地面投影**（否则模型会把它画成洋红色杂物），阴影由工具从原帧保留
- 背景必须是纯洋红 `#FF00FF`，精灵里不许出现洋红
- 风格词：chunky 16-bit pixel art、可见像素块、24 色、硬边、无抗锯齿/无渐变/无 3D 感
- 换装内容：暗铁护肩+铆钉、带骨角的铁盔（T 形眼缝）、护臂护胫、破损猩红腰布、骨刺腰带；武器换成骨刃长刀（握持手与角度不变）

## 抠色与对齐的关键点

- 洋红判定：三通道距离 < 阈值，或 `min(r,b) - g > 55`（模型混出的粉边）
- 紫边判定：`min(r,b) - g > 28`（模型为了让精灵与洋红背景分开会描一圈紫边）
- 缩放：按"参考帧精灵高度 / 新格子精灵高度"，再面积平均下采样，避免糊
- 画布：新精灵比原画布宽时向外扩，并同步修正帧的 `x/y`，保证脚底位置不动
- 颜色量化：`--quantize 24` 压成 24 级，视觉上更接近原版

## 现状

- `Build/Client/Release/Data/Monster/200.Lib` = 站立 8 方向全部替换（188 帧里 32 帧换新，其余动作仍是原版骷髅）
- 站立第 7 方向第 2 帧因模型把精灵放大异常被跳过，已用同方向第 1 帧顶替，避免闪烁
- 待做：走路（action 1，6 帧 × 8 方向）、攻击（action 9）、受击/死亡（18/21）也用同一流程替换，否则换方向移动时会掉回原版造型

## 图库编号与仓库

- 皮肤 Lib 编号 **200~299 留给符文皮肤**，不要覆盖原版怪物图库（`000~199`）
- 当前「召唤·白骨将军」用 `Data/Monster/200.Lib`，服务端在 `Runes.json` 里用 `"PetSkin": 200` 指定
- 新增皮肤：Lib 放进 `Build/Client/Release/Data/Monster/`，改对应符文的 `PetSkin`；`.gitignore` 已放行 `2*.Lib`
- **换 / 取消符文会让旧骷髅直接死亡**（`HumanObject.RuneExt.cs` 的 `KillSkeletonPets`），玩家重新召唤即可，所以皮肤只需要保证"召唤那一刻"的样子

## 重要更正：目标图库是 078，不是 022

- `Monster.BoneFamiliar = 78` → 道士「召唤骷髅」用的是 `Data/Monster/078.Lib`（骨架 + 镰刀，尺寸约 68x76）
- `022.Lib` 是野外骷髅**怪物**（金属头盔 + 钉头锤，尺寸约 76x96），比例与装备都不同，**不能拿来做宠物皮肤**
- 正式的"可编辑精灵表"已导出到 `docs/art/petboon/src-078/`（站/走/砍/受击/死亡/尸体，8 方向，3 倍放大），见该目录的 `README.md`
- 当前 `200.Lib` = `078.Lib` 的整体冷色重绘**占位**（234 帧全改），只用于验证换皮链路
- 早先基于 `022.Lib` 生成的那批图（`sheet-*`、`hero-*`、`preview-standing.png`）仅作历史留档

## 最终管线（078 → 200.Lib，2026-11-11）

1. **导出原版底图**：`node Web/tools/export-sheet.mjs Build/Client/Release/Data/Monster/078.Lib docs/art/petboon/src-078 --scale 3 --pad 8`
   → 每个动作一张 8 行（方向）× N 列（帧）的洋红底精灵表。
2. **定稿站立造型**：用 `gen-image.mjs`（`meta/muse-image` + 代理）以 `src-078/action00-stand-d0-7.png` 为底图生成，
   提示词见 `prompt-078-stand-color.txt`（含第 10/11 条"盔甲/斧子颜色一致性"）。定稿结果 `gen-078/muse-stand-v6.webp`。
3. **做装备设定图**：从定稿站立表每行取第 1 帧拼成 `gen-078/design-ref.png`（8 个方向视角）。
4. **逐动作生成**（两张参考图：设定图 + 该动作的原版表）：
   ```bash
   node Web/tools/make-sheet-prompt.mjs --color --design-ref --w <宽> --h <高> --cols <列> --frames "<n> 帧" --action "<动作名>" --out docs/art/petboon/prompt-078-<动作>-ref.txt
   node Web/tools/gen-image.mjs --model meta/muse-image --proxy "<proxy>" \
     --prompt docs/art/petboon/prompt-078-<动作>-ref.txt \
     --ref docs/art/petboon/gen-078/design-ref.png \
     --ref docs/art/petboon/src-078/action<xx>-<动作>-d0-7.png \
     --out docs/art/petboon/gen-078/muse-<动作>-ref-v1.png
   node Web/tools/resize-png.mjs <生成图> <缩放图> <原表宽>x<原表高>
   ```
5. **切回 Lib**（必须 `--fresh` 丢掉旧像素、`--fitx` 让 X 方向贴合原帧宽度）：
   ```bash
   node Web/tools/sheet-to-lib.mjs <上一步 Lib> <缩放图> <输出 Lib> <actionId> 0 \
     --fresh --fitx --cellw <格宽> --cellh <格高> --posterize 6 --quantize 24
   ```
   动作按 `站立0 → 走路1 → 攻击9 → 受击18 → 死亡21 → 尸体22` 顺序串联，最后一份就是 `200.Lib`。
6. **安装**：`cp <最终 Lib> Build/Client/Release/Data/Monster/200.Lib`，客户端无需重新编译（数据文件）。

命中率与时序：`meta/muse-image` 只接受采样参数，尺寸完全由参考图决定；生成后会返回 webp，需先转 png 再缩放。
