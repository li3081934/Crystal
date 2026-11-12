using System;
using System.Collections.Generic;
using Server.MirDatabase;
using Server.RuneSystem;
using S = ServerPackets;

namespace Server.MirObjects
{
    /// <summary>
    /// HumanObject 的符文扩展（partial）。
    /// 放在独立目录里，原有 HumanObject.cs 只需要加一个 partial 关键字。
    /// </summary>
    public partial class HumanObject
    {
        /// <summary>登录时把存档里的符文选择同步到 UserMagic.RuneId（并做一次合法性校验）。</summary>
        internal void InitRunes()
        {
            if (!RuneRepository.Any || Info?.Magics == null) return;

            foreach (var magic in Info.Magics)
            {
                ushort runeId = Info.Runes.Get(magic.Spell);
                if (runeId == 0) { magic.RuneId = 0; continue; }

                var rune = RuneRepository.Get(runeId);
                if (rune == null || rune.Spell != magic.Spell || !IsRuneUsable(magic, rune))
                {
                    // 数据改过 / 技能被重置：清掉失效选择，避免脏数据
                    Info.Runes.Set(magic.Spell, 0);
                    magic.RuneId = 0;
                    continue;
                }

                magic.RuneId = runeId;
            }
        }

        /// <summary>是否满足该符文的解锁条件。</summary>
        public bool IsRuneUsable(UserMagic magic, RuneInfo rune)
        {
            if (magic == null || rune == null) return false;
            if (rune.Spell != magic.Spell) return false;
            if (!rune.Valid) return false;

            if (rune.Unlock == RuneUnlock.Always) return true;
            if (Info.Runes.Unlocked.Contains(rune.Id)) return true;

            return magic.Level >= rune.NeedMagicLevel;
        }

        /// <summary>
        /// 客户端请求选择/取消符文。所有校验都在服务端做，客户端只负责展示。
        /// </summary>
        public void SetRune(Spell spell, ushort runeId)
        {
            if (!RuneRepository.Any) return;

            var magic = GetMagic(spell);
            if (magic == null) return;

            if (runeId == 0)
            {
                magic.RuneId = 0;
                Info.Runes.Set(spell, 0);
                Enqueue(new S.SetRune { ObjectID = ObjectID, Spell = spell, RuneId = 0 });
                KillSkeletonPets(spell);
                RefreshStats(); //被动属性类符文（背包负重/腕力）取消后要立刻回落
                return;
            }

            var rune = RuneRepository.Get(runeId);

            if (rune == null || rune.Spell != spell || !IsRuneUsable(magic, rune))
            {
                // 回一个当前真实状态，让客户端把 UI 回滚
                Enqueue(new S.SetRune { ObjectID = ObjectID, Spell = spell, RuneId = magic.RuneId });
                return;
            }

            magic.RuneId = runeId;
            Info.Runes.Set(spell, runeId);

            Enqueue(new S.SetRune { ObjectID = ObjectID, Spell = spell, RuneId = runeId });
            KillSkeletonPets(spell);
            RefreshStats(); //被动属性类符文（背包负重/腕力）选中后要立刻生效
        }

        /// <summary>把服务端的被动属性表转成下发格式（Stat 名 → 各等级值），客户端只用于显示。</summary>
        private static Dictionary<string, int[]> ToClientStatBonus(RuneInfo rune)
        {
            if (rune?.ResolvedStatBonus == null || rune.ResolvedStatBonus.Count == 0) return null;

            var result = new Dictionary<string, int[]>(rune.ResolvedStatBonus.Count);

            foreach (var pair in rune.ResolvedStatBonus)
                result[pair.Key.ToString()] = pair.Value;

            return result;
        }

        /// <summary>
        /// 换/取召唤类符丈时，场上已存在的骷髅直接解散（杀死），由玩家重新召唤。
        /// 这样新召唤出来的是完全按新符文生成的一只，不需要处理"换皮/改属性"的中间态；
        /// 将来新符文也只需要在数据里声明 PetSkin / PetBoonPct，不用再改这段代码。
        /// </summary>
        private void KillSkeletonPets(Spell spell)
        {
            if (Pets == null || Pets.Count == 0) return;
            if (spell != Spell.SummonSkeleton) return;

            bool killed = false;

            for (int i = Pets.Count - 1; i >= 0; i--)
            {
                var pet = Pets[i];
                if (pet == null || pet.Dead) continue;
                if (pet.Info == null || pet.Info.Name != Settings.SkeletonName) continue;

                pet.Die();
                Pets.RemoveAt(i);
                killed = true;
            }

            if (killed)
                ReceiveChat("符文变动，召唤的骷髅已消散。", ChatType.System);
        }

        /// <summary>符文连击的重入保护（防止"再打一次"再触发自己）。</summary>
        internal bool RuneRepeatGuard;

        /// <summary>最近一次挥砍用的技能（供"只对该技能伤害生效"的符文判断）。</summary>
        internal Spell RuneLastHitSpell;
        internal long RuneLastHitSpellUntil;

        /// <summary>当前这次伤害是否来自该技能（挥砍后 1 秒内有效）。</summary>
        public bool IsRuneHitFrom(Spell spell)
        {
            if (RuneLastHitSpell != spell) return false;
            return Server.MirEnvir.Envir.Main.Time <= RuneLastHitSpellUntil;
        }

        /// <summary>待补发的法术（双发类符文），由 RuneEvents.OnTick 到期执行。</summary>
        internal readonly List<RunePendingCast> RunePendingCasts = new List<RunePendingCast>();

        /// <summary>连击符文的待执行时间（0 = 没有待执行的补刀）。</summary>
        internal long RuneRepeatDueTime;
        internal MirDirection RuneRepeatDir;
        internal Spell RuneRepeatSpell;
        internal ushort RuneRepeatRuneId;

        /// <summary>本次挥砍已掷骰的"纯数值类"符文 Id（0 = 无）。</summary>
        internal ushort RuneProcRuneId;

        /// <summary>本次挥砍那次掷骰的结果（同一刀的所有目标共用，避免逐目标结果不一致）。</summary>
        internal bool RuneProcRuneResult;

        /// <summary>查找该符文当前的 Buff（Values[0] = 符文Id）。</summary>
        public bool TryGetRuneBuff(ushort runeId, out Buff buff)
        {
            buff = null;

            for (int i = 0; i < Buffs.Count; i++)
            {
                var b = Buffs[i];
                if (b.Type != BuffType.Rune) continue;
                if (b.Values == null || b.Values.Length == 0) continue;
                if (b.Values[0] != runeId) continue;

                buff = b;
                return true;
            }

            return false;
        }

        /// <summary>该符文 Buff 的剩余次数（Values[1]）；没有 Buff 时返回 0，无次数概念时返回 -1。</summary>
        public int GetRuneBuffCharges(ushort runeId)
        {
            if (!TryGetRuneBuff(runeId, out var buff)) return 0;
            if (buff.Values.Length < 2) return -1;

            return buff.Values[1];
        }

        /// <summary>下发符文表 + 当前选择（登录时调用一次）。</summary>
        internal void SendRuneInfo()
        {
            if (!RuneRepository.Any) return;

            var packet = new S.RuneList();

            foreach (var rune in RuneRepository.All)
            {
                packet.Runes.Add(new ClientRuneInfo
                {
                    Id = rune.Id,
                    Spell = rune.Spell,
                    Slot = rune.Slot,
                    Icon = rune.Icon,
                    NeedMagicLevel = rune.NeedMagicLevel,
                    Name = rune.Name,
                    Description = rune.Description,
                    EffectScale = rune.EffectScale,
                    TintArgb = rune.ResolvedTintArgb,
                    StatBonus = ToClientStatBonus(rune),
                });
            }

            if (Info?.Magics != null)
            {
                foreach (var magic in Info.Magics)
                {
                    if (magic.RuneId == 0) continue;
                    packet.States.Add(new ClientRuneState(magic.Spell, magic.RuneId));
                }
            }

            Enqueue(packet);
        }
    }
}
