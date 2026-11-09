using System;

namespace Server.RuneSystem
{
    /// <summary>
    /// 「造成伤害时吸血」类符文。
    ///
    /// 触发点是 RuneEvents.OnDealtDamage —— 玩家的**普通攻击和技能伤害**都会经过它
    /// （HumanObject.Attacked / MonsterObject.Attacked 的 HumanObject 攻击者重载，
    ///  在扣完护甲/减伤的最终伤害处调用），因此一次伤害回一次血。
    ///
    /// 数值全部来自数据：HealPct（造成伤害的百分比）、ProcChance（触发概率）。
    /// </summary>
    public sealed class LifestealBehavior : IRuneBehavior
    {
        public string Id => RuneBehaviorRegistry.LifestealOnDealtDamage;

        public void OnDealtDamage(RuneContext ctx, int damage)
        {
            if (ctx?.Owner == null || ctx.Rune == null) return;
            if (damage <= 0) return;
            if (ctx.Owner.Dead) return;
            if (ctx.Rune.HealPct <= 0) return;

            if (!RuneEffects.TryProc(ctx.Rune)) return;

            int heal = (int)Math.Round(damage * (ctx.Rune.HealPct / 100f));
            if (heal <= 0) return;

            ctx.Owner.ChangeHP(heal);
        }
    }
}
