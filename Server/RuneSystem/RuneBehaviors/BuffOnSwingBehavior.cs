using System;

namespace Server.RuneSystem
{
    /// <summary>
    /// 「攻击时给自身上 Buff」类符文（首个符文：基本剑术 → 攻击速度 +5）。
    ///
    /// 这是一个纯数据驱动的行为：具体上什么 Buff、加多少属性、持续多久、触发概率
    /// 全部来自 Envir/Rune/Runes.json，本文件不写任何数值。
    /// </summary>
    public sealed class BuffOnSwingBehavior : IRuneBehavior
    {
        public string Id => RuneBehaviorRegistry.BuffOnSwing;

        public void OnSwing(RuneContext ctx)
        {
            if (ctx?.Owner == null || ctx.Rune == null) return;

            if (!RuneEffects.TryProc(ctx.Rune)) return;

            RuneEffects.ApplyBuff(ctx);
        }
    }
}
