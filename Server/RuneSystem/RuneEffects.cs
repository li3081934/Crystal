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
        /// 双发类符文：登记 CastCount-1 次"补发"。
        /// 每发都比上一发晚 CastDelayMs（默认 600ms，够客户端播完一次施法动作，
        /// 否则第二段动作会打断第一段、导致第一张符根本没飞出去）。
        /// </summary>
        internal static void ScheduleExtraCasts(HumanObject owner, RuneInfo rune, MapObject target, int damage)
        {
            if (owner == null || rune == null || target == null) return;

            int count = Math.Max(0, Math.Min(4, rune.CastCount - 1));
            if (count <= 0) return;

            int delay = Math.Max(200, rune.CastDelayMs);
            long now = Envir.Main.Time;

            for (int i = 0; i < count; i++)
            {
                owner.RunePendingCasts.Add(new RunePendingCast
                {
                    DueTime = now + (delay * (i + 1)),
                    Spell = rune.Spell,
                    TargetId = target.ObjectID,
                    TargetLocation = target.CurrentLocation,
                    Damage = damage,
                    RuneId = rune.Id,
                });
            }
        }

        /// <summary>到期执行所有待补发的法术（RuneEvents.OnTick 每帧调用）。</summary>
        internal static void ExecutePendingCasts(HumanObject owner)
        {
            if (owner == null || owner.RunePendingCasts.Count == 0) return;

            long now = Envir.Main.Time;

            for (int i = owner.RunePendingCasts.Count - 1; i >= 0; i--)
            {
                var cast = owner.RunePendingCasts[i];
                if (now < cast.DueTime) continue;

                owner.RunePendingCasts.RemoveAt(i);
                FirePendingCast(owner, cast);
            }
        }

        /// <summary>补发一次法术：先发表现（客户端播施法动作 → 生成投射物），再结算伤害。</summary>
        private static void FirePendingCast(HumanObject owner, RunePendingCast cast)
        {
            if (owner == null || cast == null) return;
            if (owner.Dead || owner.CurrentMap == null) return;

            var magic = owner.GetMagic(cast.Spell);
            if (magic == null) return;

            //别人看：普通广播（Broadcast 不会发给自己）
            owner.Broadcast(new ServerPackets.ObjectMagic
            {
                ObjectID = owner.ObjectID,
                Direction = owner.Direction,
                Location = owner.CurrentLocation,
                Spell = cast.Spell,
                TargetID = cast.TargetId,
                Target = cast.TargetLocation,
                Cast = true,
                Level = magic.Level,
                RuneId = cast.RuneId,
                Instant = true,     //纯表现：客户端只放投射物，不再进完整施法动作
            });

            //自己看：单独投递一份，客户端凭 SelfBroadcast 只播动画、不再发 C.Magic
            owner.Enqueue(new ServerPackets.ObjectMagic
            {
                ObjectID = owner.ObjectID,
                Direction = owner.Direction,
                Location = owner.CurrentLocation,
                Spell = cast.Spell,
                TargetID = cast.TargetId,
                Target = cast.TargetLocation,
                Cast = true,
                Level = magic.Level,
                RuneId = cast.RuneId,
                SelfBroadcast = true,
                Instant = true,
            });

            var target = Envir.Main.GetObject(cast.TargetId);
            if (target == null) return;
            if (target.Dead || target.Node == null || target.CurrentMap != owner.CurrentMap) return;
            if (!target.IsAttackTarget(owner)) return;

            target.Attacked(owner, cast.Damage, DefenceType.MAC, false);
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
