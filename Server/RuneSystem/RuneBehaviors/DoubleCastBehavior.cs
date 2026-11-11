using System;
using System.Collections.Generic;
using Server.MirObjects;

namespace Server.RuneSystem
{
    /// <summary>
    /// 「双发」类符文（例：道士灵魂火符「灵魂·双发」）。
    ///
    /// 触发时机：CompleteMagic（第一发已经由原版逻辑算好伤害 value、目标 target）。
    /// 做法：把"再来一发"登记到 owner.RunePendingCasts，由 RuneEvents.OnTick 在
    ///       CastDelayMs 之后执行（RuneEffects.ExecutePendingCasts）：
    ///        - 发一次 S.ObjectMagic（别人靠 Broadcast，本人靠 SelfBroadcast 单独投递）
    ///          → 客户端会完整播一遍施法动作，动作最后一帧生成第二张火符；
    ///        - 对同一个目标再结算一次伤害（沿用第一发的数值，不重复掷骰）。
    /// 不额外消耗护身符：符文本身就是"一符双发"。
    /// </summary>
    public sealed class DoubleCastBehavior : IRuneBehavior
    {
        public string Id => RuneBehaviorRegistry.DoubleCast;

        public bool OnMagicComplete(RuneContext ctx, IList<object> data)
        {
            if (ctx?.Owner == null || ctx.Rune == null) return false;

            //只在"这次施的确实是符文所属技能"时触发
            if (ctx.UsedSpell != ctx.Rune.Spell) return false;

            if (data == null || data.Count < 3) return false;
            if (!(data[1] is int value) || value <= 0) return false;
            if (!(data[2] is MapObject target)) return false;

            var owner = ctx.Owner;

            if (owner.Dead) return false;
            if (target.Dead || target.Node == null || target.CurrentMap != owner.CurrentMap) return false;
            if (!target.IsAttackTarget(owner)) return false;

            if (ctx.Rune.CastCount <= 1) return false;

            if (!RuneEffects.TryProc(ctx.Rune)) return false;

            RuneEffects.ScheduleExtraCasts(owner, ctx.Rune, target, value);

            //不接管：第一发仍由原版 CompleteMagic 结算
            return false;
        }
    }
}
