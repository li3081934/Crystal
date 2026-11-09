using System;
using Server.MirEnvir;

namespace Server.RuneSystem
{
    /// <summary>
    /// 通用符文效果工具（供各行为复用）：触发概率、上 Buff 等。
    /// 所有方法都只在"确实有符文生效"的路径上被调用。
    /// </summary>
    public static class RuneEffects
    {
        /// <summary>按 ProcChance 掷骰。</summary>
        public static bool TryProc(RuneInfo rune)
        {
            if (rune == null) return false;
            if (rune.ProcChance >= 100) return true;
            if (rune.ProcChance <= 0) return false;

            return Envir.Main.Random.Next(100) < rune.ProcChance;
        }

        /// <summary>
        /// 给符文拥有者自己上 Buff（BuffType + BuffStats + BuffDurationMs 全部来自数据）。
        /// Buff 用统一的 BuffType.Rune + Values[0]=符文Id 区分具体是哪个符文，
        /// 因此新增符文 Buff 不需要新增枚举值、也不需要改客户端。
        /// </summary>
        public static void ApplyBuff(RuneContext ctx)
        {
            ApplyBuff(ctx, new[] { (int)ctx.Rune.Id });
        }

        /// <summary>同上，但可以由调用方附加运行时数据（例如剩余次数）。</summary>
        public static void ApplyBuff(RuneContext ctx, int[] values, bool updateOnly = false)
        {
            if (ctx?.Owner == null || ctx.Rune == null) return;
            if (ctx.Rune.ResolvedBuffType == BuffType.None) return;

            ctx.Owner.AddBuff(
                ctx.Rune.ResolvedBuffType,
                ctx.Owner,
                Math.Max(1000, ctx.Rune.BuffDurationMs),
                new Stats(ctx.Rune.ResolvedBuffStats),
                !updateOnly,
                updateOnly,
                values);
        }
    }
}
