using System;
using Server.MirDatabase;

namespace Server.RuneSystem
{
    /// <summary>
    /// 数值门面 —— 原有系统只允许调用这里的静态方法。
    /// 约定：没有符文时第一行就返回原值，零额外开销、零内存分配。
    /// </summary>
    public static class RuneHooks
    {
        /// <summary>技能伤害（UserMagic.GetDamage）。</summary>
        public static int Damage(UserMagic magic, int value)
        {
            if (magic == null || magic.RuneId == 0 || !RuneRepository.Any) return value;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null) return value;

            int result = value;

            if (rune.DamageFlat != 0)
                result += rune.DamageFlat;

            if (rune.DamagePct != 0)
                result = (int)Math.Round(result * (1f + rune.DamagePct / 100f));

            return result < 0 ? 0 : result;
        }

        /// <summary>技能威力（UserMagic.GetPower，护盾/回复/召唤等）。</summary>
        public static int Power(UserMagic magic, int value)
        {
            if (magic == null || magic.RuneId == 0 || !RuneRepository.Any) return value;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null) return value;

            int result = value;

            if (rune.PowerFlat != 0)
                result += rune.PowerFlat;

            if (rune.PowerPct != 0)
                result = (int)Math.Round(result * (1f + rune.PowerPct / 100f));

            return result < 0 ? 0 : result;
        }

        /// <summary>技能冷却（UserMagic.GetDelay）。</summary>
        public static long Delay(UserMagic magic, long value)
        {
            if (magic == null || magic.RuneId == 0 || !RuneRepository.Any) return value;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null) return value;

            if (rune.CooldownPct == 0) return value;

            long result = (long)Math.Round(value * (1f + rune.CooldownPct / 100f));
            if (result < 100) result = 100;

            return result;
        }

        /// <summary>技能耗蓝（HumanObject.MagicCost）。</summary>
        public static int Cost(UserMagic magic, int value)
        {
            if (magic == null || magic.RuneId == 0 || !RuneRepository.Any) return value;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null) return value;

            if (rune.CostPct == 0) return value;

            int result = (int)Math.Round(value * (1f + rune.CostPct / 100f));
            return result < 0 ? 0 : result;
        }
    }
}
