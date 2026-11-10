# 游戏现有 Debuff（减益/异常状态）机制调研

> 目标：在做符文系统时搞清楚「这个游戏能对目标施加什么样的减益」，
> 哪些可以直接复用、哪些需要新增、客户端表现怎么走。
>
> 代码基线：`Server/MirEnvir/Envir.cs` `Version = 118`（符文系统已合入）。

---

## 0. 一句话结论

游戏里**并存两套减益机制**，互不相通：

| 机制 | 类型 | 管什么 | 数据载体 | 客户端表现 |
|---|---|---|---|---|
| **PoisonType**（异常状态） | `[Flags] ushort`，11 种 | 减速 / 冰冻 / 眩晕 / 麻痹 / 中毒 DoT / 流血 / 减防 / 增伤 / 致盲 / 延迟爆炸 | `Poison` 对象（逐个 tick 计时，不进存档） | 头顶**彩点**（10 色）+ 名字颜色 |
| **BuffType**（属性减益） | `byte`，200+ 个 | 通过 `Stats` 加**负数属性**实现减益（降攻、降命中、减伤…） | `Buff` 对象（**进存档**，有 StackType） | Buff 栏图标（仅 `Visible = true` 的显示） |

设计时的重要事实：

- **真正让目标"动不了"的只有 PoisonType**（Frozen / Paralysis / LRParalysis 会直接封掉 CanMove/CanWalk/CanRun/CanAttack/CanCast）。
- **BuffType 不会阻止行动**，它只是加减属性（所以"诅咒降攻"是 Buff，"冰冻定身"是 Poison）。
- 两者都会被道士技能 **`Spell.Purification`（净化）** 清掉（`HumanObject.cs:6265`、`:6283`）。

---

## 1. PoisonType 异常状态（第一套）

### 1.1 枚举（`Shared/Enums.cs:1010`）

```csharp
[Flags]
public enum PoisonType : ushort
{
    None = 0,
    Green = 1,          // 绿毒：每 tick 掉血（道术/施毒术）
    Red = 2,            // 红毒：降低护甲（ArmourRate）
    Slow = 4,           // 减速
    Frozen = 8,         // 冰冻：完全定身
    Stun = 16,          // 眩晕：玩家禁施法 / 怪物禁行动
    Paralysis = 32,     // 麻痹：定身
    DelayedExplosion = 64,  // 三段延迟爆炸
    Bleeding = 128,     // 流血：每 tick 掉血 + 特效
    LRParalysis = 256,  // 长时麻痹（LionRoar 狮子吼）
    Blindness = 512,    // 致盲：转成 BuffType.Blindness（降命中）
    Dazed = 1024        // 晕眩：禁攻击 + 禁施法 + 播 Stunned 特效
}
```

### 1.2 数据结构（`Server/MirObjects/MapObject.cs:998`）

```csharp
public class Poison
{
    public MapObject Owner;          // 施加者（Hero 会自动换成主人）
    public PoisonType PType;
    public int Value;                // 绿毒=每 tick 伤害；红毒=降防幅度；致盲=降命中
    public long Duration, Time, TickTime, TickSpeed;   // Duration 是"tick 数"，不是毫秒！
}
```

> **重要**：`Duration` 的单位是 **tick 次数**，实际时长 = `Duration * TickSpeed`。
> `ProcessPoison()` 里 `poison.Time++`，`Time >= Duration` 时移除（`HumanObject.cs:668-671`）。
> 但 `AddBuff(BuffType.Blindness, ..., (int)(p.Duration * p.TickSpeed), ...)` 里又把它换算成毫秒喂给 Buff（`HumanObject.cs:7459`）。

### 1.3 每种状态的实际效果（服务端）

| PoisonType | 效果 | 代码位置 |
|---|---|---|
| `Green` | 每 tick `PoisonDamage(-Value)`，设置 `LastHitter`（掉血算击杀归属） | `HumanObject.cs:678-694` |
| `Red` | `ArmourRate -= 0.10F`（玩家）/ `-= 0.5F`（怪物）→ 护甲打折，受伤更重 | `HumanObject.cs:718`、`MonsterObject.cs:1538` |
| `Slow` | 玩家：`GetDelayTime()` 里 **动作时间 ×2**（`HumanObject.cs:248-255`）；怪物：每 tick `MoveSpeed/AttackSpeed += 100`（上限 3500） | `MonsterObject.cs:1545-1557` |
| `Frozen` | 直接否决 `CanMove/CanWalk/CanRun/CanAttack/CanCast` —— **完全定身** | `HumanObject.cs:122-158`、`MonsterObject.cs:645-664` |
| `Stun` | 玩家：只封 `CanCast`（还能普攻）；怪物：Light 不是 5/10 时禁行动；`DamageRate += 0.20F`（受伤 +20%） | `HumanObject.cs:157`、`:721`、`MonsterObject.cs:1540` |
| `Paralysis` / `LRParalysis` | 封 `CanMove/CanWalk/CanRun/CanAttack/CanCast` | `HumanObject.cs:122-158` |
| `Bleeding` | 每 tick 掉血 + 广播 `SpellEffect.Bleeding` 特效 | `HumanObject.cs:684-690` |
| `Blindness` | 转成 `BuffType.Blindness`，`Stat.Accuracy = -Value` | `HumanObject.cs:7457-7462` |
| `Dazed` | 禁攻击 + 禁施法，并广播 `SpellEffect.Stunned`（时长 = `Duration * TickSpeed`） | `HumanObject.cs:7450-7456` |
| `DelayedExplosion` | 三段倒计时，最后一段结算爆炸伤害 | `HumanObject.cs:730-760`（`ProcessDelayedExplosion`） |

### 1.4 施加接口与抵抗/覆盖规则（`HumanObject.cs:7393`）

```csharp
public override void ApplyPoison(Poison p, MapObject Caster = null,
                                 bool NoResist = false, bool ignoreDefence = true)
```

- **抵抗**：`Random.Next(Settings.PoisonResistWeight=10) < Stats[Stat.PoisonResist]` → 直接返回；
  只有"攻击者是玩家 且 `Settings.PvpCanResistPoison`"时才给玩家这条抵抗（默认 false，即 PvP 必中）。
- **绿毒被魔防削弱**：`ignoreDefence == false` 时，绿毒 `Value -= 随机 MinMAC..MaxMAC`，削到 ≤0 就变 `None`。
- **同名覆盖规则**（`:7427-7440`）——几条很容易踩的：
  - 绿毒：**弱毒不能覆盖强毒**（`Value` 更小直接 return）；
  - 其它类型：**短时间不能覆盖长时间**（剩余时间更长的直接 return）；
  - **Frozen / Slow / Paralysis / LRParalysis 只要已存在就直接 return**（防永久控）；
  - `DelayedExplosion` 不能重复叠。
- **红名**：玩家给别人上毒会把自己变褐名（`BrownTime`，`:7398-7414`）。
- **只对玩家生效的修正**：绿/红毒时长会被 `Stats[Stat.PoisonRecovery]` 缩减（`:7426`）。

### 1.5 现有来源

**技能**（`HumanObject.CompleteMagic` / `Map.cs`）：

| 技能 | 施加 | 出处 |
|---|---|---|
| `Spell.Hemorrhage` 血凤击 | `Bleeding`（`Value = MaxDC+1`，Duration 依等级+幸运） | `HumanObject.cs:3119-3145` |
| `Spell.PoisonSword` 猛毒剑气 | `Green` | `HumanObject.cs:5313` |
| `Spell.FrostCrunch` 寒冰掌 | `Slow` + `Frozen` | `HumanObject.cs:5981/5993` |
| `Spell.Poisoning` 施毒术 | `Green` + `Red` | `HumanObject.cs:6062/6072` |
| `Spell.Entrapment` 捕蝇剑 | `Paralysis` | `HumanObject.cs:6342` |
| `Spell.CatTongue` | `Frozen` / `Stun` / `Slow`（按目标类型选） | `HumanObject.cs:6409-6419` |
| `Spell.DelayedExplosion` 延爆箭 | `DelayedExplosion` | `HumanObject.cs:6481` |
| `Spell.CrippleShot` 致残射击 | `Green` | `HumanObject.cs:6631/6655` |
| `Spell.TwinDrakeBlade` 双龙斩 | `Stun`（延迟动作） | `HumanObject.cs:5494`、`:6818` |
| `Spell.IceThrust` 冰刺术 | `Slow` + `Frozen`（概率按等级） | `Map.cs:1540-1560` |
| `Spell.Curse` 诅咒术 | `Slow` + **`BuffType.Curse`**（降 MaxDC/MC/SC 百分比 + 降攻速） | `Map.cs:1872-1888` |
| `Spell.Plague` 瘟疫术 | `Green`/`Red`（按手持毒类型随机） | `Map.cs:2024` |
| `Spell.MoonMist` 月影迷雾 | 对 **Undead** 目标 `Stun` | `Map.cs:1379` |
| `Spell.LionRoar` 狮子吼 | `LRParalysis` | `Map.cs:1421` |
| `Spell.OneWithNature` 天人合一 | `Green` | `Map.cs:2147` |
| `Spell.Purification` 净化术 | **清除**所有 `Debuff` Buff + 清空 PoisonList | `HumanObject.cs:6258-6285` |

**装备/属性词条**（`MapObject.ApplyNegativeEffects`，`MapObject.cs:509`）——每次命中被动触发：

| 条件 | 效果 |
|---|---|
| `SpecialItemMode.Paralize` | 1/15 概率 `Paralysis` 5 tick |
| `Stats[Stat.Freezing] > 0` | 概率 `Slow`，时长 `min(10, 3+Random(Freezing))`；**PvP 需 `Settings.PvpCanFreeze`（默认 false）** |
| `Stats[Stat.PoisonAttack] > 0` | 概率 `Green`，`Value = min(10, 3+Random(PoisonAttack))` |

**怪物**：73 个怪物脚本调用 `MonsterObject.PoisonTarget(target, chance, duration, poisonType, tickSpeed)`（`MonsterObject.cs:3534`），例如 `AntCommander`（Green/Slow/Dazed）、`Behemoth`（Bleeding/Paralysis/Dazed）、`AncientBringer`（Paralysis）。

### 1.6 客户端表现

- 服务端只在**集合变化时**发包：`S.Poisoned{P oison}`（自己，`ServerPackets.cs:2951`）、`S.ObjectPoisoned{ObjectID, Poison}`（周围，`:2966`）。
- 客户端存 `MapObject.Poison`，`DrawPoison()` 在头顶画小圆点（`Client/MirObjects/MapObject.cs:519`）：

  | 状态 | 颜色 |
  |---|---|
  | Green | 绿 |
  | Red | 红 |
  | Bleeding | 暗红 |
  | Slow | 紫 |
  | Stun / Dazed | 黄 |
  | Blindness | 紫红 |
  | Frozen | 蓝 |
  | Paralysis / LRParalysis | 灰 |
  | DelayedExplosion | 橙 |

- 怪物名字颜色也按状态变（`Client/MirObjects/MonsterObject.cs:451-458`）。
- 客户端**没有任何"状态时长"显示**，也没有图标，只有圆点。

---

## 2. BuffType 属性减益（第二套）

### 2.1 结构

```csharp
// Server/MirDatabase/BuffInfo.cs:6
public class BuffInfo
{
    public BuffType Type; public BuffStackType StackType;
    public BuffProperty Properties; public int Icon; public bool Visible;
}
public static List<BuffInfo> Load()   // 硬编码，运行时注册进 Envir
```

```csharp
// Server/MirDatabase/BuffInfo.cs:90
public class Buff
{
    public BuffInfo Info; public MapObject Caster;
    public uint ObjectID; public long ExpireTime;   // ExpireTime 这里是"持续时间"语义
    public Stats Stats; public int[] Values;
    public Dictionary<string,object> Data;          // 自定义负载（Get<T>/Set）
    public bool FlagForRemoval, Paused;
}
```

- `BuffProperty`（`Shared/Enums.cs:1405`）：`RemoveOnDeath | RemoveOnExit | **Debuff** | PauseInSafeZone`。
  `Debuff` 是唯一的"减益标记"，目前只被三处读取：
  - `Spell.Purification` 净化（`HumanObject.cs:6265`）
  - 道士英雄的解毒判断（`Hero/TaoistHero.cs:34`）
  - 怪物 `PowerBead` 的驱散逻辑（`Monsters/PowerBead.cs:113`）
- `BuffStackType`（`:1414`）：`None / ResetDuration / StackDuration / StackStat / StackStatAndDuration / Infinite / ResetStat / ResetStatAndDuration`，在 `MapObject.AddBuff`（`MapObject.cs:570`）里实现。

### 2.2 现有的"减益 Buff"只有 3 个

| BuffType | Properties | 效果 | 来源 |
|---|---|---|---|
| `Curse`（道士诅咒） | `RemoveOnDeath \| Debuff` | `MaxDC/MC/SC` 按百分比下降 + 玩家降攻速（`MaxDCRatePercent` 等负值） | `Map.cs:1882` |
| `RhinoPriestDebuff` | `Debuff` | `MaxDC/MC/SC = -damage` | `Monsters/RhinoPriest.cs:96` |
| `Blindness` | `RemoveOnDeath \| Debuff` | `Stat.Accuracy = -Value` | 由 `PoisonType.Blindness` 转成（`HumanObject.cs:7459`） |

> 注意：**`Visible` 默认 false**，所以这三个 debuff 在客户端 Buff 栏里**都不显示**，
> 玩家只能通过头顶圆点 / 属性变化感知。

### 2.3 施加与时长

```csharp
AddBuff(BuffType type, MapObject owner, int duration, Stats stats,
        bool refreshStats = true, bool updateOnly = false, params int[] values)
```

- `duration` 是**毫秒**（调用方普遍写 `Settings.Second * N`）。
- `Stats` 里塞**负数**就是减益（如 `[Stat.MaxDC] = -5`）。
- `Buff.Save()/Buff(BinaryReader)` 会**写进角色存档**，所以 `BuffType` 枚举只能追加（符文系统已遵守：`Rune = 216`）。
- 客户端收 `S.AddBuff` / `S.RemoveBuff` / `S.PauseBuff`（`Shared/ServerPackets.cs:3982/3997/4015`），
  `BuffDialog`（`Client/MirScenes/Dialogs/BuffDialog.cs:7`）按 `Visible` 决定是否进列表，图标取自 `BuffIcon` 图库的 `Info.Icon`。

---

## 3. 两套机制对比与选型

| 需求 | 用哪个 | 原因 |
|---|---|---|
| 减速 / 定身 / 眩晕 / 中毒 DoT / 流血 / 减防 / 易伤 | **PoisonType** | 只有它会影响 `Can*` 判定与逐 tick 伤害；客户端已有点显示 |
| 降攻击/降命中/降防/降属性（不限制行动） | **BuffType + 负 Stats** | 属性直接叠加，无需每帧逻辑；可进存档、可被净化 |
| 需要"能被净化/驱散"的 debuff | 两者都行 | 净化会清 `Debuff` Buff + 全部 Poison |
| 需要显示在 Buff 栏的 debuff | **BuffType** 且 `Visible=true` + `Icon` | Poison 只有圆点，无图标无倒计时 |

---

## 4. 如果要给符文系统加"减益符文"，三条可行路线

### 路线 A：复用 PoisonType（零新枚举、零客户端改动）

新增能力位 `DebuffOnSwing`，数据字段：`DebuffType`（PoisonType 名）、`DebuffTicks`、`DebuffValue`、`ProcChance`：

```csharp
// RuneEffects / 新行为类里
target.ApplyPoison(new Poison {
    PType = rune.DebuffType, Duration = rune.DebuffTicks,
    TickSpeed = 1000, Value = rune.DebuffValue, Owner = owner
}, owner);
```

- ✅ 客户端**完全不用改**（圆点会自动出现），服务端 1 个行为类搞定。
- ⚠️ 覆盖规则会挡：同类型已存在就不再叠（尤其 Frozen/Slow/Paralysis 直接被拒）。
- ⚠️ 会被 `Purification` 清掉，也会吃 `Stat.PoisonResist`。

### 路线 B：复用 BuffType + 负 Stats（能显示在 Buff 栏、能进存档）

需要先扩一枚 `BuffType`（只能追加，比如 `RuneDebuff1 = 217`）+ `BuffInfo.Load()` 一条（`Visible=true`、`Icon=N`）+ 客户端 `BuffIcon` 图库里有对应图。

```csharp
target.AddBuff(BuffType.RuneDebuff1, owner, rune.BuffDurationMs,
               new Stats { [Stat.MaxDC] = -rune.Value }, values: ...);
```

- ✅ 有图标/倒计时，能被净化，能跨图保存。
- ⚠️ 每加一个具体减益就要占一枚 BuffType（枚举会膨胀）；图标要美术资源。

### 路线 C：二者组合（推荐）

- "定身/减速/中毒" → 走 A（PoisonType，表现现成）；
- "降攻/易伤/降防" → 走 B（自己的 BuffType，`Debuff` 标记 + 负数 Stats）。

这样 `RuneInfo` 只需加 3~4 个数据字段，行为类 1 个（`DebuffOnSwingBehavior`），客户端只在"要显示 Buff 图标"时才需要动。

---

## 5. 坑与注意事项（实测/读码结论）

1. **`Poison.Duration` 是 tick 数不是毫秒** —— 写 3000 会变成 3000 秒。
2. **`Buff.ExpireTime` 与 `Buff` 存档字段**：`AddBuff` 传的是"持续毫秒"，但字段名叫 `ExpireTime`，读代码时别被误导。
3. **Frozen/Slow/Paralysis/LRParalysis 不允许刷新**（`HumanObject.cs:7432`），想做"持续刷新减速"必须写成"到期再上"，否则只有第一次生效。
4. **怪物 Slow 是累加 `+100ms/tick` 且上限 3500**，玩家 Slow 是"动作时间 ×2"，两边手感差异很大。
5. **`Stun` 对玩家只封施法、不封普攻**；想"完全晕住"玩家要用 `Dazed`（禁攻击+禁施法）或 `Frozen`。
6. **`PoisonType.Blindness` 不是直接减命中**，它转发成 `BuffType.Blindness`；如果不走这条转发，`Blindness` 在 `ProcessPoison` 的 `switch` 里是空的（`HumanObject.cs:723`）。
7. **`BuffType` 只能追加**：玩家 Buff 进存档，插入/删除会让老存档错位（符文系统当初就是按这条规则把 `Rune` 放在 216）。
8. **净化会连"自己人身上的好 Buff"一起处理？不会** —— 只清 `BuffProperty.Debuff`，但 `PoisonList.Clear()` 是**无条件全清**（包括自己人被上的绿毒）。
9. 客户端**完全没有 debuff 倒计时 UI**：若要做 "D3 风格" 的减益显示（图标 + 剩余时间 + 层数），需要新开一个包（不能改 `ClientBuff` 的读法，理由同符文：登录缓存/版本兼容），或扩展 `BuffDialog` 复用 `S.AddBuff` 的 `ExpireTime`。

---

## 6. 与符文系统的衔接（现状）

- 符文自带的增益 Buff 已经用了一枚 `BuffType.Rune = 216`（`BuffInfo.cs:86`，`RemoveOnExit + ResetDuration + Visible + Icon 60`），
  数据来自符文 JSON（`BuffStats` + `BuffTypeName`），客户端 Buff 栏显示"符"图标并能看到剩余次数。
- 若要加"减益符文"，按第 4 节路线 A 即可，**不需要动引擎**。

### 路线 A 已落地：第六个符文「攻杀·血饮」

2026-11 已按路线 A 实现（详见 `docs/符文系统-可行性调研.md` 7.11）：

| 位置 | 内容 |
|---|---|
| `Server/RuneSystem/RuneInfo.cs` | `RuneEffect.DebuffOnHit = 1 << 18`；字段 `DebuffType`（PoisonType 名）/`DebuffDuration`（tick 数）/`DebuffTickSpeed`（毫秒）/`DebuffValue`/`DebuffValuePct` |
| `Server/RuneSystem/RuneBehaviors/DebuffOnHitBehavior.cs` | `Id => "DebuffOnHit"`，`OnDealtDamage` 里 `ctx.Target.ApplyPoison(poison, ctx.Owner)` |
| `Server/RuneSystem/RuneEvents.cs` | `DealtDamageEffects` 掩码追加 `| RuneEffect.DebuffOnHit`（不加则永不触发） |
| `Server/RuneSystem/RuneRepository.cs` | `Resolve()` 里 `ResolveDebuff(rune)`（`Enum.TryParse` 校验 PoisonType 名） |
| `Envir/Rune/Runes.json` | 符文 6：`"DebuffType": "Bleeding"`, `"DebuffDuration": 5`, `"DebuffTickSpeed": 1000`, `"DebuffValuePct": 30` |

**客户端零改动**：流血的头顶点/每 tick 特效/跳字全部走原生 `S.ObjectPoisoned` + `S.ObjectEffect{SpellEffect.Bleeding}`，
所以"减益需要独立 UI"这个担心不成立 —— 只要选的是原版已有的 `PoisonType`，表现是白送的。

实测（`--smoke --spell Slaying --rune 6 --onlyspell`）：`目标进入流血状态: Poison=Bleeding`、`流血第 1 跳`、
`跳字合计 43（目标被流血打死）`。

### 已经用上 Stun 的符文

第七个符文「刺杀·震魄」（`Runes.json` Id=7）用 `RequireSecondTarget` + `DebuffType=Stun`，
只震晕刺杀剑气贯穿到的第 2 格目标，实测见 `docs/符文系统-可行性调研.md` 7.12。

### 已经用上"绿毒 + 红毒"的符文

第八个符文「施毒·双生」（`Runes.json` Id=8，`Spell = Poisoning`）走 `RuneEffect.DualPoison` +
`DualPoisonBehavior`：施毒术结算前读 `CompleteMagic` 传入的 `data`（伤害值 / 目标 / 毒符），
按原版公式把"另一种毒"补上，因此**带绿毒 → 绿+红，带红毒 → 红+绿**，时长与 tick 频率和原版一致
（`Duration = (value * 2) + (magic.Level + 1) * 7`，`TickSpeed = 2000`）。
一次施法仍只消耗 1 个毒符，补上的毒同样受 `Stat.PoisonResist` 抵抗与 `ApplyPoison` 覆盖规则约束。
实现与实测见 `docs/符文系统-可行性调研.md` 7.13。

