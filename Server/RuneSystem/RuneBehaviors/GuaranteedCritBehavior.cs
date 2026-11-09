using System;

namespace Server.RuneSystem
{
    /// <summary>
    /// 「必暴」类符文（例：基本剑术「剑术·蓄势」）。
    ///
    /// 机制：攻击时给自己上一个 Buff —— 接下来 N 次攻击必定暴击。
    /// 次数存在 Buff.Values[1] 里（Values[0] 是符文 Id），客户端可用来显示剩余次数。
    ///
    /// 暴击本身**完全复用游戏现有实现**：
    ///  - 暴击判定：HumanObject.Attacked / MonsterObject.Attacked 里的
    ///    `Envir.Random.Next(100) < CriticalRate * Settings.CriticalRateWeight`
    ///    —— 我们的符文只是在这条判定里"或"上一个强制为真（RuneEvents.ForceCritical），
    ///    因为 Stat.CriticalRate 有职级上限（默认 18 × 权重 5 = 90%），纯加属性做不到 100%。
    ///  - 暴击伤害：继续走 `Stat.CriticalDamage` 与 `Settings.CriticalDamageWeight`，
    ///    数据里的 CritDamagePct（200%）在 RuneRepository 载入时被换算成对应的 CriticalDamage 值。
    ///
    /// 因此在客户端会看到游戏原本的暴击特效/暴击飘字，不需要任何新的表现层。
    /// </summary>
    public sealed class GuaranteedCritBehavior : IRuneBehavior
    {
        public string Id => RuneBehaviorRegistry.GuaranteedCritOnSwing;

        /// <summary>挥砍时确保 Buff 存在（次数满额）。次数在真正造成伤害时才扣。</summary>
        public void OnSwing(RuneContext ctx)
        {
            if (ctx?.Owner == null || ctx.Rune == null) return;

            if (!RuneEffects.TryProc(ctx.Rune)) return;
            if (ctx.Owner.TryGetRuneBuff(ctx.Rune.Id, out _)) return;

            RuneEffects.ApplyBuff(ctx, new[] { (int)ctx.Rune.Id, Math.Max(1, ctx.Rune.CritCharges) });
        }

        /// <summary>真正打出伤害后消耗一次（落空的挥砍不消耗，符合"下 3 次攻击"的直觉）。</summary>
        public void OnDealtDamage(RuneContext ctx, int damage)
        {
            if (ctx?.Owner == null || ctx.Rune == null) return;
            if (damage <= 0) return;

            if (!ctx.Owner.TryGetRuneBuff(ctx.Rune.Id, out _)) return;

            int charges = ctx.Owner.GetRuneBuffCharges(ctx.Rune.Id);
            if (charges < 0) return; // 无次数概念（只看持续时间）

            charges--;

            if (charges <= 0)
            {
                ctx.Owner.RemoveBuff(ctx.Rune.ResolvedBuffType);
                return;
            }

            // 只更新剩余次数，不刷新持续时间
            RuneEffects.ApplyBuff(ctx, new[] { (int)ctx.Rune.Id, charges }, true);
        }
    }
}
