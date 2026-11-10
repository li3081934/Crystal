# 道士召唤骷髅（BoneFamiliar）原始精灵图

来源：`Build/Client/Release/Data/Monster/078.Lib`（`Monster.BoneFamiliar = 78`，也就是道士 `召唤骷髅` 实际用的图库）。
之前误用了 `022.Lib`（`Monster.Skeleton`，是野外骷髅**怪物**），美术比例、装备都不一样，已作废。

## 文件

| 文件 | 动作 | 网格 |
| --- | --- | --- |
| `action00-stand-d0-7.png` | 站立 | 4 帧 × 8 方向 |
| `action01-walk-d0-7.png` | 走路 | 6 帧 × 8 方向 |
| `action09-attack-d0-7.png` | 攻击 | 6 帧 × 8 方向 |
| `action18-struck-d0-7.png` | 受击 | 2 帧 × 8 方向 |
| `action21-die-d0-7.png` | 死亡 | 10 帧 × 8 方向 |
| `action22-corpse-d0-7.png` | 尸体 | 1 帧 × 8 方向 |
| `action27-action27-d0-7.png` | 帧表里的 blend 特效帧（召唤/复活光效），一般不用改 |
| `overview.png` | 上面几张的 1x 缩略总览 | — |
| `layout.json` | 每格对应的 Lib 帧序号、原始画布与偏移 | — |

## 坐标约定

- 行 = 方向，从上到下 `0..7`：上、右上、右、右下、下、左下、左、左上
- 列 = 该动作的帧序号
- 每格 **3 倍最近邻放大**（`--scale 3`），精灵水平居中、底部对齐，下面留 8px
- 背景是纯洋红 `#FF00FF`，改图时请保留洋红背景，方便切回去时抠图
- 每格像素尺寸见 `layout.json` 的 `cellW` / `cellH`（同一动作内所有格一致）
- 原始帧序号：`index = start + dir * (count + skip) + frame`（`start/count/skip` 见 `layout.json`）

## 改完之后怎么切回 Lib

```bash
# 站立（动作 0）
node Web/tools/sheet-to-lib.mjs 078.Lib action00-stand-d0-7.png 200.Lib 0 0 \
     --cell <layout.cellW> --key FF00FF --quantize 24

# 走路（动作 1）必须基于上一步的输出继续叠加，动作 9/18/21/22 同理
node Web/tools/sheet-to-lib.mjs 200.Lib action01-walk-d0-7.png 200.Lib 1 0 \
     --cell <layout.cellW> --key FF00FF --quantize 24
```

要点：

- `--cell` 填该动作的格子边长（`layout.json` 里的 `cellW`，正方形时 `cellW == cellH`）
- 第 2 个参数（参考 Lib）传上一步的输出，逐动作叠加；第一次用 `078.Lib`
- 工具会按"参考帧精灵高度 / 新格子精灵高度"自动缩放、底部对齐、保留原版地面阴影
- 换皮编号：`200~299` 保留给符文皮肤，当前 `Runes.json` 里「召唤·白骨将军」用 `"PetSkin": 200`

## 当前 200.Lib 状态

`Build/Client/Release/Data/Monster/200.Lib` 目前是 **078.Lib 的整体冷色重绘占位**（骨白 + 冷铁色阶，234 帧全改），
用来验证"符文生效 → 换皮包 → 客户端切图库"链路，等你的正式美术替换。
