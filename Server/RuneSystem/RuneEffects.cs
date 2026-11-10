using System;
using Server.MirEnvir;
using Server.MirObjects;

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
        /// 为"一次挥砍"掷一次骰（供 MaxAttackOnHit / IgnoreDefence 这类逐目标生效的能力位使用）。
        /// 同一刀后续的所有目标都复用这次结果，保证"破防"与"最大攻击"同时成立或同时不成立。
        /// </summary>
        public static void RollSwingProc(RuneContext ctx)
        {
            if (ctx?.Owner == null || ctx.Rune == null) return;

            ctx.Owner.RuneProcRuneId = ctx.Rune.Id;
            ctx.Owner.RuneProcRuneResult = TryProc(ctx.Rune);
        }

        /// <summary>
        /// 执行连击符文的"补刀"：再走一次原版 Attack（不额外吃冷却），
        /// 并把这一刀单独发给本人客户端（服务端 Broadcast 不会发给本人），
        /// 让客户端补播一次攻击动画。
        /// </summary>
        internal static void ExecuteRepeat(HumanObject owner)
        {
            if (owner == null) return;

            owner.RuneRepeatDueTime = 0;

            if (owner.Dead) return;

            var rune = RuneRepository.Get(owner.RuneRepeatRuneId);
            if (rune == null) return;

            Spell spell = owner.RuneRepeatSpell;
            MirDirection dir = owner.RuneRepeatDir;

            owner.RuneRepeatGuard = true;

            try
            {
                long atkCd = owner.AttackTime;
                long actionCd = owner.ActionTime;

                //攻杀剑术的"就绪"标记已被第一刀消耗，这里补回去，保证第二刀仍是攻杀
                if (spell == Spell.Slaying) owner.Slaying = true;

                owner.AttackTime = 0;
                owner.ActionTime = 0;

                try
                {
                    owner.Attack(dir, spell);

                    var magic = owner.GetMagic(spell);

                    owner.Enqueue(new ServerPackets.ObjectAttack
                    {
                        ObjectID = owner.ObjectID,
                        Direction = dir,
                        Location = owner.CurrentLocation,
                        Spell = spell,
                        Level = magic?.Level ?? 0,
                        SelfBroadcast = true,
                    });
                }
                finally
                {
                    //恢复成第一刀算出的冷却（延迟补刀不能把 CD 往后推）
                    owner.AttackTime = atkCd;
                    owner.ActionTime = Math.Max(owner.ActionTime, actionCd);
                }
            }
            finally
            {
                owner.RuneRepeatGuard = false;
            }
        }

        /// <summary>
        /// 符文"本次生效"的表现通知：把着色发给本人与周围玩家（客户端据此给技能/角色上色）。
        /// 只有数据里配了 TintColor + TintMs 的符文才会发。
        /// </summary>
        public static void SendProcVisual(RuneContext ctx)
        {
            if (ctx?.Owner == null || ctx.Rune == null) return;
            if (ctx.Rune.ResolvedTintArgb == 0 || ctx.Rune.TintMs <= 0) return;

            var packet = new ServerPackets.RuneProc
            {
                ObjectID = ctx.Owner.ObjectID,
                Spell = ctx.Rune.Spell,
                RuneId = ctx.Rune.Id,
                TintArgb = ctx.Rune.ResolvedTintArgb,
                TintMs = ctx.Rune.TintMs,
                TintBody = ctx.Rune.TintBody,
            };

            ctx.Owner.Enqueue(packet);
            ctx.Owner.Broadcast(packet);
        }

        /// <summary>本次挥砍该符文是否掷骰成功。</summary>
        public static bool IsSwingProcActive(HumanObject owner, RuneInfo rune)
        {
            if (owner == null || rune == null) return false;

            return owner.RuneProcRuneId == rune.Id && owner.RuneProcRuneResult;
        }

        /// <summary>
        /// 召唤物换皮：把某个召唤物当前的图库编号同步给周围玩家（只在变化时发一次）。
        /// skin = 0 表示恢复原贴图；只发数据包，不改服务器上的任何战斗数值。
        /// </summary>
        public static void SyncPetSkin(MonsterObject pet, ushort skin)
        {
            if (pet == null) return;
            if (pet.RuneSkin == skin) return;

            pet.RuneSkin = skin;

            pet.Broadcast(new ServerPackets.PetSkin
            {
                ObjectID = pet.ObjectID,
                Skin = skin,
            });
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
