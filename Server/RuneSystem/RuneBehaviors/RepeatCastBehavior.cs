using System;

namespace Server.RuneSystem
{
    /// <summary>
    /// 「连击」类符文（例：攻杀剑术「攻杀·连斩」）。
    ///
    /// 触发时机：HumanObject.Attack 里"这一刀已经确认打出"之后（RuneEvents.OnSwing）。
    /// 做法：直接再调用一次 HumanObject.Attack(dir, 同一个技能)，因此
    ///  - 第二次是完全真实的技能一击（同样的命中判定、伤害公式、技能特效/动画）；
    ///  - **不占用攻击冷却**：先把 AttackTime / ActionTime 清零以通过 CanAttack 检查，
    ///    打完立刻恢复成"只算一次攻击"的冷却（Envir.Time 在同一 tick 内不变，
    ///    所以恢复值与只打一刀完全相同）；
    ///  - 重入保护：第二次攻击自己也会走 OnSwing，用 RuneRepeatGuard 保证只连一次。
    ///
    /// 数值全部来自数据：RepeatCount（再打几次）、ProcChance（触发概率）。
    /// </summary>
    public sealed class RepeatCastBehavior : IRuneBehavior
    {
        public string Id => RuneBehaviorRegistry.RepeatCastOnSwing;

        public void OnSwing(RuneContext ctx)
        {
            if (ctx?.Owner == null || ctx.Rune == null) return;

            // 只在"打出的确实是符文所属技能"时触发（例如攻杀剑术）
            if (ctx.UsedSpell != ctx.Rune.Spell) return;

            if (!RuneEffects.TryProc(ctx.Rune)) return;

            var owner = ctx.Owner;
            if (owner.RuneRepeatGuard) return;
            if (owner.Dead) return;

            int times = Math.Max(1, ctx.Rune.RepeatCount);
            MirDirection dir = ctx.Direction;
            Spell spell = ctx.UsedSpell;

            owner.RuneRepeatGuard = true;

            try
            {
                for (int i = 0; i < times; i++)
                {
                    long atkCd = owner.AttackTime;
                    long actionCd = owner.ActionTime;

                    // 攻杀剑术的"就绪"标记由原版逻辑消耗，这里重新置上，保证第二刀仍然是攻杀
                    if (spell == Spell.Slaying) owner.Slaying = true;

                    owner.AttackTime = 0;
                    owner.ActionTime = 0;

                    try
                    {
                        owner.Attack(dir, spell);
                    }
                    finally
                    {
                        // 恢复成"只算一次攻击"的冷却
                        owner.AttackTime = Math.Max(owner.AttackTime, atkCd);
                        owner.ActionTime = Math.Max(owner.ActionTime, actionCd);
                    }
                }
            }
            finally
            {
                owner.RuneRepeatGuard = false;
            }
        }
    }
}
