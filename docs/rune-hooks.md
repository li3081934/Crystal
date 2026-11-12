# 符文系统 · 侵入点清单（Seam Manifest）

> 这份清单是符文系统"低侵入"约定的唯一事实来源。
> **规则：新增符文内容（数据 / 行为）不允许修改本清单之外的文件。**
> 若确需新的战斗时机，只能在 `HumanObject` 里加一行 `RuneEvents.OnXxx(...)`，并登记到本文件。

生成时间：首个符文（战士·基本剑术「剑术·疾风」）落地时。

---

## 1. 服务端（10 个文件 / 28 行）

| # | 文件 | 行 | 改动 |
|---|---|---|---|
| S1 | `Server/MirObjects/HumanObject.cs` | 12 | `public class` → `public partial class`（让符文扩展文件能以同类身份访问私有成员） |
| S2 | 同上 | 2977 | `RuneEvents.OnSwing(this, dir, spell);`（挥砍一次触发一次） |
| S3 | 同上 | 3400 | `return RuneHooks.Cost(magic, cost);`（耗蓝） |
| S4 | 同上 | 3502 | `if (!RuneEvents.TryCast(...)) switch (spell) {...}`（允许符文接管施法） |
| S5 | 同上 | 5910 | `if (RuneEvents.OnMagicComplete(this, magic, data)) return;`（法术结算） |
| S6 | 同上 | 7221 | `RuneEvents.OnDealtDamage(attacker, this, damage - armour);`（真实造成伤害 · 受害者是人类） |
| S6c | `Server/MirObjects/HumanObject.cs` | 7162 | 暴击判定追加 `|| RuneEvents.ForceCritical(attacker)`（必暴符文） |
| S6d | `Server/MirObjects/MonsterObject.cs` | 2595 | 同上（受害者是怪物时） |
| S6g | `Server/MirObjects/HumanObject.cs`（`Process` 开头） | `RuneEvents.OnTick(this);`（符文心跳，用于延迟补刀等定时效果） |
| S6e | `Server/MirObjects/HumanObject.cs` | 2985 | `RuneHooks.ApplyMaxAttack(this, spell, ref damageBase);`（按最大攻击力计算） |
| S6f | 同上 | 3205 | `RuneHooks.ApplyIgnoreDefence(this, spell, ref defence);`（无视目标防御） |
| S6b | `Server/MirObjects/MonsterObject.cs` | 2682 | `RuneEvents.OnDealtDamage(attacker, this, damage - armour);`（真实造成伤害 · 受害者是怪物）；外加 `using Server.RuneSystem;` 一行 |
| S7 | `Server/MirDatabase/MagicInfo.cs` | 103 / 140 / 151 | `UserMagic.RuneId` 字段 + 存档读写（`version > 117` 守卫） |
| S8 | 同上 | 190 / 200 / 224 / 229 | `GetDamage` / `GetPower` ×2 / `GetDelay` 各包一层 `RuneHooks.*` |
| S9 | `Server/MirDatabase/CharacterInfo.cs` | 84 / 394 / 576 | `RuneBook Runes` 字段 + 存档读写（`version > 117` 守卫） |
| S10 | `Server/MirEnvir/Envir.cs` | 57 | `Version = 118`（117 → 118，为新存档字段） |
| S11 | 同上 | 2910-2911 | `RuneBehaviorRegistry.Scan(); RuneRepository.Load();` |
| S12 | `Server/MirNetwork/MirConnection.cs` | 511-512 / 1653-1658 | `C.SetRune` 分发 → `Player.SetRune(...)` |
| S13 | `Server/MirObjects/PlayerObject.cs` | 232 / 1712 | `InitRunes()`（登录校验）/ `SendRuneInfo()`（下发符文表） |
| S14 | 同上 | 3233 | GM 调试命令 `@RUNE` / `@RUNELIST`（纯调试，可删） |
| S15 | `Server/MirDatabase/BuffInfo.cs` | 86 | `BuffType.Rune` 的 BuffInfo（**所有符文 Buff 共用**） |
| S16 | `Server/MirObjects/MonsterObject.cs`（`RefreshAll` 末尾） | `RuneHooks.ApplyPetBoon(this);`（召唤物增益：按宠物等级重算属性，`RefreshBase` 会先重置，天然幂等） |
| S17 | `Server/MirObjects/HumanObject.cs`（`SummonSkeleton`） | `int petLimit = RuneHooks.PetLimit(this, Spell.SummonSkeleton, RuneEffect.ExtraPet, 1);`——把"已有一只就召回"和"宠物数 >= 2"两处硬编码换成 `skeletons >= petLimit` / `>= petLimit + 1`；无符文时 `petLimit == 1`，与原生逐字等价 |
| S18 | `Server/MirObjects/HumanObject.cs`（`CompleteMagic` 的 `#region Poisoning` 绿毒分支） | `greenValue = RuneHooks.PoisonValue(magic, PoisonType.Green, greenValue);`（毒伤翻倍；只改 `Poison.Value`，时长/tick/抗性全部原样） |
| S19 | `Server/MirObjects/HumanObject.cs`（`Magic` 末尾的 `Broadcast(new S.ObjectMagic …)`） | 给广播包补 `RuneId = magic != null ? magic.RuneId : (ushort)0`——让客户端知道"这次施法带的是哪个符文"，用于按 `EffectScale` 放大技能特效；纯表现字段，服务端逻辑不读 |
| S20 | `Server/MirObjects/HumanObject.cs`（`ElectricShock` 的驯服成功分支，`target.MaxPetLevel = (byte)(1 + magic.Level * 2);` 之后） | `RuneHooks.ApplyTameLevel(this, target, magic);`（1 行）——符文可在驯服成功那一刻改写宠物等级；没有符文时第一行就 return，行为与原生完全一致 |
| S21 | `Server/MirObjects/HumanObject.cs`（`RefreshStats()` 里 `RefreshStatCaps();` 之后） | `RuneHooks.ApplyPassiveStats(this);`（1 行）——被动属性类符文（如「奥术·巨力」加背包负重/穿戴/腕力）在属性重算的最后一刻叠加；`RefreshStats()` 开头 `Stats.Clear()`，所以幂等，无符文时循环直接跳过 |

## 2. Shared（5 个文件）

| # | 文件 | 改动 |
|---|---|---|
| H1 | `Shared/Enums.cs:1401` | `BuffType.Rune = 216`（**追加在末尾空段，绝不插中间**：玩家 Buff 会存进角色存档） |
| H2 | `Shared/Enums.cs` | `ServerPacketIds.RuneList/SetRune/RuneProc`、`ClientPacketIds.SetRune`（追加在枚举末尾） |
| H3 | `Shared/ClientPackets.cs` / `ServerPackets.cs` | 4 个新包（纯新增，不动老包结构） |
| H4 | `Shared/Packet.cs` | 4 行包注册 |
| H5 | `Shared/Data/RuneData.cs` | 新文件：`ClientRuneInfo` / `ClientRuneState` |

## 3. 客户端（6 个文件）

| # | 文件 | 改动 |
|---|---|---|
| C1 | `Client/MirScenes/GameScene.cs:1724 / 1727` | 2 个 `case` → `RuneLibrary.OnRuneList/OnSetRune` |
| C2 | `Client/MirScenes/Dialogs/MainDialogs.cs:3285 / 3407 / 3418` | `MagicButton.RuneLabel`（技能页「符」入口） |
| C3 | 同上（`SkillBarDialog.Update`） | 技能栏提示追加「符文：xxx」 |
| C4 | `Client/MirScenes/Dialogs/BuffDialog.cs:345 / 468` | `BuffType.Rune` 的图标 / 描述分支（**所有符文 Buff 共用一条分支**） |
| C5 | `Client/MirScenes/Dialogs/CharacterDialog.cs:657` | `RefreshInterface()` 由 `private` 改 `public`（符文切换后刷新界面） |
| C6 | `Client/MirObjects/MapObject.cs`（`ApplyDrawColour`） | 末尾 `return RuneVisuals.Apply(this, drawColour);`（符文着色，1 行） |
| C7 | `Client/MirObjects/PlayerObject.cs`（`MirAction.Attack1` 的 `Spell.Slaying` 特效） | 绘制颜色由 `Color.White` 改成 `RuneVisuals.Apply(this, Color.White)`（攻杀挥砍特效跟着变色，1 行） |
| C8 | `Client/MirScenes/GameScene.cs`（`ObjectAttack`） | 允许 `SelfBroadcast` 的包作用于本人 + 给补刀动作打"纯表现"标记（~5 行） |
| C9 | `Client/MirObjects/PlayerObject.cs`（`SetAction` 的 `Attack1`） | 读到"纯表现"标记时只取 Spell/SpellLevel 后 return，不发 `C.Attack`、不刷新本地攻击冷却（~7 行） |
| C10 | `Client/MirObjects/MapObject.cs`（`ApplyDrawColour` 末尾） | `RuneVisuals.Apply(this, drawColour, body: true)`（本体着色，1 行） |
| C11 | `Client/MirScenes/GameScene.cs`（`ObjectMagic`） | 追加一个"纯表现广播"参数：`action.Params.Add(p.SelfBroadcast && p.ObjectID == User.ObjectID)`（索引 7，1 行） |
| C12 | `Client/MirObjects/PlayerObject.cs`（`SetAction` 的 `MirAction.Spell`） | 该 case 从 `AttackRange2` 里拆出来；读到索引 7 为 `true` 时**不发 `C.Magic`**（否则服务端补发的施法包会让客户端再施法一次，形成递归，~10 行） |

## 4. 新增模块（不受限制，可自由扩展）

```
Server/RuneSystem/                  ← 全部业务逻辑（数据 / 存档 / 门面 / 行为插件）
Client/RuneSystem/                  ← 符文面板 + 客户端缓存 + 着色表现（RuneVisuals）
Envir/Rune/Runes.json               ← 符文数据（策划改这里，不用编译）
Tools/RuneTestTool/                 ← 离线建号 + 协议级冒烟测试
```

## 5. 边界自检

以下命令的合法输出只有 4 处（`CharacterInfo` 的存档字段、`Envir` 的启动载入、`PlayerObject` 的 GM 调试命令）：

```bash
grep -rn "RuneRepository\|RuneBook\|IRuneBehavior" Server --include=*.cs | grep -v "Server/RuneSystem"
```

扩展新符文时的检查项：

1. **纯数值 / 上 Buff 类符文** → 只改 `Envir/Rune/Runes.json`。
2. **新机制** → 在 `Server/RuneSystem/RuneBehaviors/` 新增一个实现 `IRuneBehavior` 的文件（反射自动注册），`Runes.json` 里用 `BehaviorId` 引用。
   `BehaviorId` 支持逗号分隔多个（如 `"LifestealOnDealtDamage,DebuffOnHit"`），能力位组合不需要新写行为类。
   注意：**新增 `RuneEffect` 能力位必须同时加进 `RuneEvents` 里对应的掩码**（`SwingEffects` / `SwingProcEffects` / `DealtDamageEffects`），否则永不触发。
3. **需要新战斗时机**（死亡时、受击时…）→ 才允许在 `HumanObject` 加 1 行 `RuneEvents.OnXxx(...)`，并更新本清单第 1 节。
