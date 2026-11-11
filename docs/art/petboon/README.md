# 白骨将军（召唤骷髅符文皮肤）美术流程

目标：符文「召唤·白骨将军」生效时，把宠物骷髅（`Monster.BoneFamiliar = 78` → `Data/Monster/078.Lib`）
换成"暗铁盔甲 + 骨角铁盔 + 金色长斧"的版本，风格与原版 16-bit 手绘一致。

## 编号约定

- **`2000~2999` 保留给「符文皮肤」**，绝不覆盖官方图库（官方怪物是 `000~199`）
- 当前「召唤·白骨将军」用 **`Data/Monster/2000.Lib`**，服务端在 `Runes.json` 里用 `"PetSkin": 2000` 指定
- 客户端 `MonsterObject.ApplyRuneSkin(skin)` 会切到 `Libraries.Monsters[skin]`；`0` 表示恢复原贴图
- 新增皮肤：Lib 丢进 `Build/Client/Release/Data/Monster/`，改对应符文的 `PetSkin` 即可，客户端无需重新编译
- `.gitignore` 已放行 `2*.Lib`，皮肤文件会随仓库走

## 工具链（`Web/tools/`，零依赖，只用 Node 内置 zlib）

| 脚本 | 作用 |
|---|---|
| `export-sheet.mjs` | Lib → 每个动作一张精灵表 PNG（行=8 方向，列=帧，洋红底，3 倍最近邻放大）+ `layout.json` |
| `gen-image.mjs` | 调 OpenRouter 出图，支持 `--ref`（多张 `input_references`）、`--proxy`、`--resolution/--aspect-ratio` |
| `make-sheet-prompt.mjs` | 按动作/行列/尺寸生成提示词；`--color` 追加盔甲/斧子固定色值，`--design-ref` 声明"设定图 + 底图"两张参考的分工 |
| `make-sheet-prompt` + `design-ref` | 用定稿站立图拼出装备设定图（8 个方向视角） |
| `resize-png.mjs` | 把模型输出缩回原表尺寸（`box` 面积平均 / `nearest`） |
| `sheet-to-lib.mjs` | 精灵表 → Lib：抠洋红/紫边 → 按参考帧缩放 → 贴回参考帧画布；`--fresh` 丢掉旧像素、`--fitx` X 方向贴合原宽、`--cellw/--cellh` 支持非正方形格 |
| `dump-lib.mjs` / `montage-lib.mjs` / `pack-lib.mjs` / `recolor-lib.mjs` / `sheet-from-lib.mjs` | 导帧、总览图、PNG→Lib、整体换色、Lib 动作表 |

## 最终管线（078 → 2000.Lib）

```bash
# 1) 导出原版底图（每个动作一张：8 行方向 × N 列帧）
node Web/tools/export-sheet.mjs Build/Client/Release/Data/Monster/078.Lib docs/art/petboon/src-078 --scale 3 --pad 8

# 2) 定稿站立造型（产物：gen-078/muse-stand-v6.webp）
node Web/tools/make-sheet-prompt.mjs --color --w 928 --h 2144 --cols 4 --frames "4 帧" --action "站立" \
  --out docs/art/petboon/prompt-078-stand-color.txt
node Web/tools/gen-image.mjs --model meta/muse-image --proxy "<代理>" \
  --prompt docs/art/petboon/prompt-078-stand-color.txt \
  --ref docs/art/petboon/src-078/action00-stand-d0-7.png \
  --out docs/art/petboon/gen-078/muse-stand-v6.webp

# 3) 用定稿站立图拼装备设定图（8 方向视角，产物：gen-078/design-ref.png）
#    脚本见 docs/art/petboon/README 历史段；也可以直接用 PIL 每行取第 1 帧拼 2×4

# 4) 逐动作生成：两张参考图 = 设定图 + 该动作原版表
for k in walk attack struck die corpse; do
  node Web/tools/make-sheet-prompt.mjs --color --design-ref \
    --w <原表宽> --h <原表高> --cols <列数> --frames "<n> 帧" --action "<动作名>" \
    --out docs/art/petboon/prompt-078-$k-ref.txt
  node Web/tools/gen-image.mjs --model meta/muse-image --proxy "<代理>" \
    --prompt docs/art/petboon/prompt-078-$k-ref.txt \
    --ref docs/art/petboon/gen-078/design-ref.png \
    --ref docs/art/petboon/src-078/action<xx>-$k-d0-7.png \
    --out docs/art/petboon/gen-078/muse-$k-ref-v1.png
  node Web/tools/resize-png.mjs docs/art/petboon/gen-078/muse-$k-ref-v1.png /tmp/$k.png <原表宽>x<原表高>
done

# 5) 切回 Lib（按 站立0 → 走路1 → 攻击9 → 受击18 → 死亡21 → 尸体22 串联，最后一份即成品）
node Web/tools/sheet-to-lib.mjs Build/Client/Release/Data/Monster/078.Lib docs/art/petboon/gen-078/muse-stand-v6.png /tmp/s1.Lib 0 0 \
  --fresh --fitx --cellw 260 --cellh 300 --posterize 6 --quantize 24
node Web/tools/sheet-to-lib.mjs /tmp/s1.Lib /tmp/walk.png   /tmp/s2.Lib 1  0 --fresh --fitx --cellw 256 --cellh 280 --posterize 6 --quantize 24
node Web/tools/sheet-to-lib.mjs /tmp/s2.Lib /tmp/attack.png /tmp/s3.Lib 9  0 --fresh --fitx --cellw 376 --cellh 340 --posterize 6 --quantize 24
node Web/tools/sheet-to-lib.mjs /tmp/s3.Lib /tmp/struck.png /tmp/s4.Lib 18 0 --fresh --fitx --cellw 268 --cellh 280 --posterize 6 --quantize 24
node Web/tools/sheet-to-lib.mjs /tmp/s4.Lib /tmp/die.png    /tmp/s5.Lib 21 0 --fresh --fitx --cellw 292 --cellh 268 --posterize 6 --quantize 24
node Web/tools/sheet-to-lib.mjs /tmp/s5.Lib /tmp/corpse.png /tmp/s6.Lib 22 0 --fresh --fitx --cellw 256 --cellh 184 --posterize 6 --quantize 24

# 6) 安装（客户端不用重编，纯数据）
cp /tmp/s6.Lib Build/Client/Release/Data/Monster/2000.Lib
```

## 提示词要点（`prompt-078-*-ref.txt`）

- 两张参考图的分工必须写死：**第 1 张只作装备造型/配色参考，第 2 张才是底图**
- 只做加法：在原像素上叠头盔/护肩、镰刀改金；禁止重绘、改画风、改比例、改姿势、移动位置、改像素颗粒
- 逐行方向映射（第 1 行 = 正上 … 第 8 行 = 左上），且头盔遮挡关系必须随方向变化
- **必须删掉原图的地面投影**（模型会把它画成洋红杂物）；背景纯洋红 `#FF00FF`，精灵内不许出现洋红
- 颜色一致性（`--color` 追加的第 10/11 条）：盔甲 `#1E2430` / 高光 `#3A4354` / 阴影 `#0C0F14`；斧刃 `#D8A32A`、柄 `#4A2E1A`；骨骼 `#D9CFB4`/`#9A8F76`
- 模型选择：`meta/muse-image` 颜色一致性最好（输出 webp，尺寸随参考图）；`tencent/hy-image-v3.5-preview` 次之；
  `black-forest-labs/flux-3-image*` 会无视参考图自由发挥，且对"逐行面部描述"触发内容审核（400）

## 实测结果（2026-11-11）

| 动作 | 原版墨迹 | 新图墨迹 |
|---|---|---|
| 站立 | 56x74 | 56x74 |
| 走路 | 55x81 | 55x81 |
| 攻击 | 86x87 | 86x87 |
| 受击 | 82x61 | 82x61 |
| 死亡 | 90x71 | 76x71 |

- 234 帧全部替换，无阴影残留、无洋红残留；`--fitx` 让轮廓宽度与原版一致
- 协议回归 `dotnet run --project Tools/RuneTestTool -- --smoke --account testtao --spell SummonSkeleton --rune 9 --pet` 全绿
- **换 / 取消符文会让旧骷髅当场死亡**（`HumanObject.RuneExt.cs` 的 `KillSkeletonPets`），玩家重新召唤即可
