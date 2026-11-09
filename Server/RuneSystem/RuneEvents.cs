using System;
using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

namespace Server.RuneSystem
{
    /// <summary>符文事件的上下文（只在确实有符文生效时才会创建）。</summary>
    public sealed class RuneContext
    {
        public HumanObject Owner;
        public UserMagic Magic;
        public RuneInfo Rune;

        public MirDirection Direction;
        public Point Location;
        public MapObject Target;
        public Spell UsedSpell;

        /// <summary>本次事件的伤害量（OnDealtDamage 时有效，已扣除护甲/减伤）。</summary>
        public int Damage;

        public long Time => Envir.Main.Time;
    }

    /// <summary>
    /// 事件门面 —— 原有系统只允许调用这里的静态方法（每个战斗时机一行）。
    /// </summary>
    public static class RuneEvents
    {
        /// <summary>每次成功挥砍（HumanObject.Attack 里，AttackTime 设定之后调用一次）。</summary>
        public static void OnSwing(HumanObject owner, MirDirection dir, Spell usedSpell)
        {
            if (owner == null || !RuneRepository.Any) return;

            ForEachActiveRuneMask(owner, SwingEffects, (ctx) =>
            {
                ctx.Direction = dir;
                ctx.UsedSpell = usedSpell;
                ctx.Location = owner.CurrentLocation;
                RuneBehaviorRegistry.Dispatch(ctx, BehaviorEvent.Swing);
            });
        }

        /// <summary>真正造成伤害时（HumanObject.Attacked 里调用）。</summary>
        public static void OnDealtDamage(HumanObject attacker, MapObject target, int damage)
        {
            if (attacker == null || target == null || !RuneRepository.Any) return;
            if (damage <= 0) return;

            ForEachActiveRuneMask(attacker, DealtDamageEffects, (ctx) =>
            {
                ctx.Target = target;
                ctx.Location = target.CurrentLocation;
                ctx.Damage = damage;
                RuneBehaviorRegistry.Dispatch(ctx, BehaviorEvent.DealtDamage);
            });
        }

        /// <summary>
        /// 是否强制本次伤害暴击（复用游戏现有暴击代码，绕过 CriticalRate 的职级上限）。
        /// 由 HumanObject.Attacked / MonsterObject.Attacked 里的暴击判定调用。
        /// </summary>
        public static bool ForceCritical(HumanObject attacker)
        {
            if (attacker == null || !RuneRepository.Any) return false;

            var magics = attacker.Info?.Magics;
            if (magics == null || magics.Count == 0) return false;

            for (int i = 0; i < magics.Count; i++)
            {
                var magic = magics[i];
                if (magic.RuneId == 0) continue;

                var rune = RuneRepository.Get(magic.RuneId);
                if (rune == null || !rune.Valid) continue;
                if (!rune.HasEffect(RuneEffect.GuaranteedCritical)) continue;

                if (attacker.GetRuneBuffCharges(rune.Id) != 0) return true;
            }

            return false;
        }

        /// <summary>
        /// 施法派发前（HumanObject.Magic 的 switch 之前）。
        /// 返回 true = 符文已经接管这次施法，调用方跳过原技能实现。
        /// </summary>
        public static bool TryCast(HumanObject owner, UserMagic magic, MapObject target, Point location, ref uint targetId)
        {
            if (owner == null || magic == null || magic.RuneId == 0 || !RuneRepository.Any) return false;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null || !rune.HasEffect(RuneEffect.TakeOverCast)) return false;

            var ctx = new RuneContext
            {
                Owner = owner,
                Magic = magic,
                Rune = rune,
                Target = target,
                Location = location,
                UsedSpell = magic.Spell,
            };

            return RuneBehaviorRegistry.DispatchResult(ctx, BehaviorEvent.Cast);
        }

        /// <summary>法术结算后（HumanObject.CompleteMagic 开头）。返回 true = 已完全接管。</summary>
        public static bool OnMagicComplete(HumanObject owner, UserMagic magic, IList<object> data)
        {
            if (owner == null || magic == null || magic.RuneId == 0 || !RuneRepository.Any) return false;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null) return false;
            if (!rune.HasEffect(RuneEffect.BuffOnMagicComplete)) return false;

            var ctx = new RuneContext
            {
                Owner = owner,
                Magic = magic,
                Rune = rune,
                UsedSpell = magic.Spell,
            };

            return RuneBehaviorRegistry.DispatchResult(ctx, BehaviorEvent.MagicComplete, data);
        }

        /// <summary>
        /// 遍历角色身上所有"带了符文且拥有指定能力"的技能。
        /// 这样被动技能（如基本剑术）的符文也能在普攻/受击等事件里生效。
        /// </summary>
        private const RuneEffect SwingEffects =
            RuneEffect.BuffOnSwing | RuneEffect.GuaranteedCritical | RuneEffect.RepeatCastOnSwing;

        private const RuneEffect DealtDamageEffects =
            RuneEffect.BuffOnDealtDamage | RuneEffect.HealOnDealtDamage | RuneEffect.GuaranteedCritical;

        private static void ForEachActiveRune(HumanObject owner, RuneEffect effect, Action<RuneContext> action)
        {
            ForEachActiveRuneMask(owner, effect, action);
        }

        /// <summary>遍历所有"带了符文且拥有 mask 中任意能力位"的技能。</summary>
        private static void ForEachActiveRuneMask(HumanObject owner, RuneEffect mask, Action<RuneContext> action)
        {
            var magics = owner.Info?.Magics;
            if (magics == null || magics.Count == 0) return;

            for (int i = 0; i < magics.Count; i++)
            {
                var magic = magics[i];
                if (magic.RuneId == 0) continue;

                var rune = RuneRepository.Get(magic.RuneId);
                if (rune == null || !rune.Valid || (rune.Effect & mask) == 0) continue;

                action(new RuneContext
                {
                    Owner = owner,
                    Magic = magic,
                    Rune = rune,
                    UsedSpell = magic.Spell,
                });
            }
        }
    }
}
