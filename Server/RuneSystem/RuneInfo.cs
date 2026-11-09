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

        /// <summary>连击类：触发时立刻再打出的次数（1 = 再打一次）。</summary>
        public int RepeatCount { get; set; } = 1;

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

        /// <summary>下列表里对应的行为实现（RuneBehaviors/*）。为空时仅走纯数值能力。</summary>
        public string BehaviorId { get; set; } = string.Empty;

        // ---- 运行时缓存（不参与序列化） ----
        [JsonIgnore] public BuffType ResolvedBuffType { get; set; } = BuffType.None;
        [JsonIgnore] public Stats ResolvedBuffStats { get; set; } = new Stats();
        [JsonIgnore] public bool Valid { get; set; } = true;

        public bool HasEffect(RuneEffect effect) => (Effect & effect) == effect;

        public override string ToString() => $"{Id}:{Name}";
    }
}
