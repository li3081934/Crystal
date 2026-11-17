# 数据库迁移（Flyway 风格）

`Server.MirDB` 不再提交到 git。服务端启动时会读取本目录的 `V*.json` 迁移文件，
把技能、道具、NPC 等内容同步到数据库，并把已执行的版本记录在 `Server.MirDB` 里。

## 规则

- 文件名：`V001__说明.json`、`V002__说明.json`，按 `version` 升序执行。
- 已执行过的文件不要修改；内容变更请新增一个版本文件（校验和不一致会跳过并写日志）。
- 每个文件可包含 `magic`、`items`、`npcs` 三个数组，均可省略。
- 迁移是按名称/技能 ID 幂等 upsert 的：已存在则按文件中的字段更新，不存在则创建。
- 只有至少执行了一个新迁移时才会 `SaveDB()`；`SaveDB()` 会按原有逻辑备份数据库。
- NPC 脚本仍需放在 `Envir/NPCs/<FileName>.txt`；迁移只负责 `NPCInfo` 和货架关联。

## 字段

### magic

`name`、`spell`（枚举名或数字）、`icon`、`level1/2/3`、`need1/2/3`、
`baseCost`、`levelCost`、`delayBase`、`delayReduction`、
`powerBase`、`powerBonus`、`mPowerBase`、`mPowerBonus`、
`multiplierBase`、`multiplierBonus`、`range`。

### items

`name`、`index`（可省略，省略时自动分配）、`type`、`grade`、`requiredType`、
`requiredClass`、`requiredGender`、`shape`、`weight`、`light`、`requiredAmount`、
`image`、`durability`、`price`、`stackSize`、`effect`、`bind`、`unique`、
`toolTip`、`stats`（`{ "MinDC": 1 }` 这样的键值）。

### npcs

`fileName`、`name`、`mapIndex`、`x`、`y`、`rate`、`image`、
`showOnBigMap`、`bigMapIcon`、`canTeleportTo`。

## 示例

```json
{
  "version": 3,
  "description": "Example item",
  "items": [
    { "name": "ExampleScroll", "type": "Scroll", "shape": 20, "price": 100 }
  ]
}
```
