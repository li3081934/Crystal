using System;
using System.Drawing;
using Server.MirObjects;

namespace Server.RuneSystem
{
    /// <summary>
    /// 「命中时给目标挂异常状态」类符文。
    ///
    /// 触发点是 RuneEvents.OnDealtDamage（伤害真正结算之后），所以普攻和技能都能用；
    /// 状态本身走游戏原生的 PoisonType（流血/中毒/减速/瘫痪/致盲…），
    /// 因此服务端逐 tick 掉血、客户端头顶圆点、净化术驱散全部免费复用。
    ///
    /// 数值全部来自数据：
    ///   DebuffType      PoisonType 名（"Bleeding"/"Green"/"Slow"…）
    ///   DebuffDuration  持续 tick 数（真实时长 = Duration * TickSpeed）
    ///   DebuffTickSpeed 每 tick 间隔毫秒
    ///   DebuffValue     每 tick 固定数值
    ///   DebuffValuePct  每 tick 数值按本次伤害的百分比（>0 时优先于 DebuffValue）
    ///   RequireSecondTarget 只对"穿刺/溅射的额外目标"生效（例如刺杀剑气打到的身后目标）
    /// </summary>
    public sealed class DebuffOnHitBehavior : IRuneBehavior
    {
        public string Id => RuneBehaviorRegistry.DebuffOnHit;

        public void OnDealtDamage(RuneContext ctx, int damage)
        {
            if (ctx?.Owner == null || ctx.Rune == null || ctx.Target == null) return;
            if (ctx.Owner.Dead || ctx.Target.Dead) return;

            PoisonType type = ctx.Rune.ResolvedDebuffType;
            if (type == PoisonType.None) return;
            if (ctx.Rune.DebuffDuration <= 0) return;

            //RequireSpellHit：只让"符文所属技能"打出的伤害附带异常（例如攻杀·血饮）
            if (ctx.Rune.RequireSpellHit && !ctx.Owner.IsRuneHitFrom(ctx.Rune.Spell)) return;

            //RequireSecondTarget：只给"额外目标"挂状态（例如刺杀剑气打到的第 2 格目标）
            if (ctx.Rune.RequireSecondTarget && !IsSecondTarget(ctx)) return;

            if (!RuneEffects.TryProc(ctx.Rune)) return;

            int value;

            if (ctx.Rune.DebuffValuePct > 0)
            {
                if (damage <= 0) return;

                value = (int)Math.Round(damage * (ctx.Rune.DebuffValuePct / 100f));
                if (value < 1) value = 1;
            }
            else
            {
                value = ctx.Rune.DebuffValue;
            }

            //绿毒/流血没有数值就是空转，直接不挂
            if ((type == PoisonType.Green || type == PoisonType.Bleeding) && value <= 0) return;

            var poison = new Poison
            {
                Owner = ctx.Owner,
                PType = type,
                Value = value,
                Duration = ctx.Rune.DebuffDuration,
                TickSpeed = Math.Max(100, ctx.Rune.DebuffTickSpeed),
                TickTime = ctx.Time + Math.Max(100, ctx.Rune.DebuffTickSpeed),
            };

            ctx.Target.ApplyPoison(poison, ctx.Owner);
        }

        /// <summary>
        /// 目标是不是"第 2 格"上的额外目标。
        /// 普通近战只打身前第 1 格，只有穿刺类技能才会伤到第 2 格，所以用几何判定就够，
        /// 不需要在 Attack 里加标记（保持符文系统零侵入）。
        /// </summary>
        private static bool IsSecondTarget(RuneContext ctx)
        {
            Point first = Functions.PointMove(ctx.Owner.CurrentLocation, ctx.Owner.Direction, 1);
            Point second = Functions.PointMove(first, ctx.Owner.Direction, 1);

            return ctx.Target.CurrentLocation == second;
        }
    }
}
