using System;
using System.Text.Json.Serialization;

namespace Server.RuneSystem
{
    /// <summary>
    /// 符文"能力位"。一个符文可以同时挂多个能力（数据里按位或）。
    /// 纯数值能力由 RuneHooks 直接处理；其余能力需要 RuneBehaviors 里对应的 BehaviorId 实现。
    /// </summary>
    [Flags]
    public enum RuneEffect
    {
        None = 0,

        // ---- 纯数值（无需代码，RuneHooks 生效） ----
        DamageUp = 1 << 0,
        DamageDown = 1 << 1,
        CostDown = 1 << 2,
        CostUp = 1 << 3,
        CooldownDown = 1 << 4,
        CooldownUp = 1 << 5,
        PowerUp = 1 << 6,
        PowerDown = 1 << 7,

        // ---- 需要行为实现（RuneBehaviors） ----
        BuffOnSwing = 1 << 10,
        BuffOnDealtDamage = 1 << 11,
        BuffOnMagicComplete = 1 << 12,
        HealOnDealtDamage = 1 << 13,
        GuaranteedCritical = 1 << 14,
        RepeatCastOnSwing = 1 << 15,
        MaxAttackOnHit = 1 << 16,
        IgnoreDefence = 1 << 17,
        DebuffOnHit = 1 << 18,
        DualPoison = 1 << 19,

        /// <summary>召唤物增益：按召唤物等级提升 攻击/防御/血量/移速/攻速（见 Pet*Pct 字段）。</summary>
        PetBoon = 1 << 23,

        /// <summary>召唤上限：把「一次只能有一只」改成「最多 PetCount 只」（见 PetCount 字段）。</summary>
        ExtraPet = 1 << 24,

        // ---- 预留（暂未实现，先占位以固定数据格式） ----
        ExtraTargets = 1 << 20,
        RangeUp = 1 << 21,
        TakeOverCast = 1 << 22,
    }

    /// <summary>符文的解锁方式（预留：本期只支持按技能等级解锁）。</summary>
    public enum RuneUnlock
    {
        MagicLevel = 0,
        Always = 1,
    }

    /// <summary>
    /// 符文定义（数据）。存放在 Envir/Rune/Runes.json，改完重启或热重载生效。
    /// 只允许"加字段"，不允许改变已有字段语义；旧数据缺字段时用默认值。
    /// </summary>
    public class RuneInfo
    {
        public ushort Id { get; set; }
        public Spell Spell { get; set; }
        public byte Slot { get; set; } = 1;

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>MagIcon 图库帧索引（2 帧一个图标，与技能图标规则一致）。</summary>
        public byte Icon { get; set; }

        public RuneUnlock Unlock { get; set; } = RuneUnlock.MagicLevel;

        /// <summary>解锁需要的技能等级（0 = 学会技能即可）。</summary>
        public byte NeedMagicLevel { get; set; }

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RuneEffect Effect { get; set; } = RuneEffect.None;

        /// <summary>触发概率（百分比），100 = 必定触发。</summary>
        public int ProcChance { get; set; } = 100;

        // ---- 纯数值参数（RuneHooks 使用） ----
        public float DamagePct { get; set; }
        public int DamageFlat { get; set; }
        public float CostPct { get; set; }
        public float CooldownPct { get; set; }
        public float PowerPct { get; set; }
        public int PowerFlat { get; set; }

        /// <summary>吸血类：按造成伤害的百分比回复自身生命（100 = 100%）。</summary>
        public float HealPct { get; set; }

        // ---- 表现（可选）----
        /// <summary>符文生效时给技能/角色加的着色（十六进制，如 "#FFD24A"）；空 = 不着色。</summary>
        public string TintColor { get; set; } = string.Empty;

        /// <summary>着色持续时间（毫秒）。</summary>
        public int TintMs { get; set; }

        /// <summary>是否连角色本体一起着色（false = 只给技能特效/刀光着色）。</summary>
        public bool TintBody { get; set; } = true;

        /// <summary>效果只在"符文所属技能"打出的伤害上生效（用于主动技能符文，例如攻杀吸血）。</summary>
        public bool RequireSpellHit { get; set; }

        /// <summary>
        /// 只对"穿刺/溅射命中的额外目标"生效（例如刺杀剑术剑气打到的身后目标）。
        /// 判定方式：目标正好站在玩家正前方第 2 格（普通近战只能打到第 1 格）。
        /// </summary>
        public bool RequireSecondTarget { get; set; }

        // ---- 召唤物增益（RuneEffect.PetBoon）----
        // 都是"每一级 PetLevel 提升的百分比"，按召唤物当前等级累加（stacks = PetLevel + 1）。

        /// <summary>每级 +x% 生命上限。</summary>
        public int PetHpPct { get; set; }

        /// <summary>每级 +x% 防御（AC 与 MAC）。</summary>
        public int PetDefencePct { get; set; }

        /// <summary>每级 +x% 攻击（DC 上下限）。</summary>
        public int PetDamagePct { get; set; }

        /// <summary>每级 +x% 移动速度（换算成移动间隔时间按比例减少）。</summary>
        public int PetMoveSpeedPct { get; set; }

        /// <summary>每级 +x% 攻击速度（换算成攻击间隔时间按比例减少）。</summary>
        public int PetAttackSpeedPct { get; set; }

        /// <summary>
        /// 换皮：符文生效时召唤物改用 Data\Monster 下的哪一套图库（{000}.Lib 的编号）。
        /// 0 = 不换皮（继续用原贴图）。图库与迁移后的原版骷髅帧表一致才能直接换。
        /// </summary>
        public ushort PetSkin { get; set; }

        /// <summary>
        /// 召唤上限（ExtraPet 使用）：该技能同时最多存在的召唤物数量。
        /// 1 = 原版行为；2 = 可以再召唤一只。
        /// </summary>
        public int PetCount { get; set; } = 1;

        /// <summary>
        /// 无视防御类：true = 连命中判定一起跳过（DefenceType.None）；
        /// false = 仅护甲归零，仍可被敏捷闪避（DefenceType.Agility）。
        /// </summary>
        public bool GuaranteedHit { get; set; }

        /// <summary>连击类：触发时立刻再打出的次数（1 = 再打一次）。</summary>
        public int RepeatCount { get; set; } = 1;

        /// <summary>连击类：第二刀与第一刀之间的间隔（毫秒，0 = 同 tick 立即补刀）。</summary>
        public int RepeatDelayMs { get; set; }

        /// <summary>必暴类：Buff 提供多少次必定暴击（0 = 不限制次数，仅看持续时间）。</summary>
        public int CritCharges { get; set; }

        /// <summary>
        /// 必暴类：暴击后的总伤害百分比（200 = 200% 伤害）。
        /// 运行时换算成游戏现有的 Stat.CriticalDamage（依赖 Settings.CriticalDamageWeight）。
        /// </summary>
        public float CritDamagePct { get; set; }

        // ---- Buff 类参数（Buff 类行为使用） ----
        public string BuffTypeName { get; set; } = string.Empty;
        public int BuffDurationMs { get; set; }
        public Dictionary<string, int> BuffStats { get; set; } = new Dictionary<string, int>();

        // ---- 减益类参数（DebuffOnHit：命中时给目标挂 PoisonType） ----
        /// <summary>异常状态名（PoisonType，如 "Bleeding"/"Green"/"Slow"）；空 = 不施加。</summary>
        public string DebuffType { get; set; } = string.Empty;

        /// <summary>持续 tick 数（真实时长 = DebuffDuration * DebuffTickSpeed，注意不是毫秒）。</summary>
        public int DebuffDuration { get; set; }

        /// <summary>每 tick 间隔（毫秒）。</summary>
        public int DebuffTickSpeed { get; set; } = 1000;

        /// <summary>每 tick 数值（绿毒/流血 = 每 tick 伤害；红毒 = 降防幅度；致盲 = 降命中）。</summary>
        public int DebuffValue { get; set; }

        /// <summary>每 tick 数值按本次伤害的百分比计算（&gt;0 时优先于 DebuffValue）。</summary>
        public float DebuffValuePct { get; set; }

        /// <summary>下列表里对应的行为实现（RuneBehaviors/*）。为空时仅走纯数值能力。</summary>
        public string BehaviorId { get; set; } = string.Empty;

        // ---- 运行时缓存（不参与序列化） ----
        [JsonIgnore] public BuffType ResolvedBuffType { get; set; } = BuffType.None;
        [JsonIgnore] public Stats ResolvedBuffStats { get; set; } = new Stats();
        [JsonIgnore] public bool Valid { get; set; } = true;

        /// <summary>TintColor 解析后的 ARGB（0 = 不着色）。</summary>
        [JsonIgnore] public int ResolvedTintArgb { get; set; }

        /// <summary>DebuffType 解析后的异常状态（None = 不施加）。</summary>
        [JsonIgnore] public PoisonType ResolvedDebuffType { get; set; } = PoisonType.None;

        public bool HasEffect(RuneEffect effect) => (Effect & effect) == effect;

        public override string ToString() => $"{Id}:{Name}";
    }
}
