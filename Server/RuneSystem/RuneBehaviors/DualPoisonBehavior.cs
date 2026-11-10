using System.Collections.Generic;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.RuneSystem
{
    /// <summary>
    /// 「双毒」类符文（例如 道士·施毒·双生）：施放施毒术时，不管身上带的是绿毒还是红毒，
    /// 目标都会同时中上绿毒 + 红毒。
    ///
    /// 实现方式（零侵入）：
    ///   CompleteMagic 的 #region Poisoning 本来会按 UserItem.Shape 只上一种毒，
    ///   这里在技能结算之前（RuneEvents.OnMagicComplete，原技能照常执行）读同一份 data
    ///   （data[1]=伤害值 data[2]=目标 data[3]=毒符），把"另一种毒"按原版公式补上。
    ///   因此数值、时长、tick 频率与原版完全一致，客户端表现（头顶红绿圆点、掉血飘字）免费复用。
    ///
    /// 数据开关：
    ///   ProcChance 触发概率；
    ///   DebuffTickSpeed / DebuffDuration 若 > 0 则覆盖原版公式（默认 0 = 用原版公式）。
    /// </summary>
    public sealed class DualPoisonBehavior : IRuneBehavior
    {
        public string Id => RuneBehaviorRegistry.DualPoison;

        public bool OnMagicComplete(RuneContext ctx, IList<object> data)
        {
            if (ctx?.Owner == null || ctx.Rune == null || ctx.Magic == null) return false;
            if (ctx.UsedSpell != Spell.Poisoning) return false;
            if (data == null || data.Count < 4) return false;

            if (!(data[1] is int value)) return false;
            if (!(data[2] is MapObject target)) return false;
            if (!(data[3] is UserItem item) || item.Info == null) return false;

            int shape = item.Info.Shape;

            //只有真正的绿毒(1)/红毒(2)才补，其它护身符形状不动
            if (shape != 1 && shape != 2) return false;

            HumanObject owner = ctx.Owner;

            //与原版 #region Poisoning 完全一致的入场检查
            if (target.Dead || target.CurrentMap != owner.CurrentMap || target.Node == null) return false;
            if (!target.IsAttackTarget(owner)) return false;

            if (!RuneEffects.TryProc(ctx.Rune)) return false;

            int duration = ctx.Rune.DebuffDuration > 0
                ? ctx.Rune.DebuffDuration
                : (value * 2) + ((ctx.Magic.Level + 1) * 7);

            int tickSpeed = ctx.Rune.DebuffTickSpeed > 0 ? ctx.Rune.DebuffTickSpeed : 2000;

            if (shape == 1)
            {
                //带绿毒 → 补红毒（降低防御）
                target.ApplyPoison(new Poison
                {
                    Duration = duration,
                    Owner = owner,
                    PType = PoisonType.Red,
                    TickSpeed = tickSpeed
                }, owner);
            }
            else
            {
                //带红毒 → 补绿毒（持续掉血）
                target.ApplyPoison(new Poison
                {
                    Duration = duration,
                    Owner = owner,
                    PType = PoisonType.Green,
                    TickSpeed = tickSpeed,
                    Value = value / 15 + ctx.Magic.Level + 1 + Envir.Main.Random.Next(owner.Stats[Stat.PoisonAttack])
                }, owner);
            }

            return false; //不接管，原技能继续按原版逻辑施加携带的那种毒
        }
    }
}
