using System;
using Server.MirDatabase;
using Server.MirObjects;
using Server.MirEnvir;

namespace Server.RuneSystem
{
    /// <summary>
    /// 数值门面 —— 原有系统只允许调用这里的静态方法。
    /// 约定：没有符文时第一行就返回原值，零额外开销、零内存分配。
    /// </summary>
    public static class RuneHooks
    {
        /// <summary>
        /// 本次攻击是否强制按"角色最大攻击力"计算（用于 HeadHumanObject.Attack 的 damageBase）。
        /// 没有对应符文时只多一次 byte 判断。
        /// </summary>
        public static void ApplyMaxAttack(HumanObject owner, Spell spell, ref int damageBase)
        {
            if (owner == null || !RuneRepository.Any) return;

            var rune = GetActiveRune(owner, spell, RuneEffect.MaxAttackOnHit);
            if (rune == null) return;

            // 按 ProcChance 掷骰（每次出手一次，结果由 RuneEvents.OnSwing 记录）
            if (!RuneEffects.IsSwingProcActive(owner, rune)) return;

            damageBase = owner.Stats[Stat.MaxDC];
        }

        /// <summary>本次攻击是否无视目标防御（护甲归零；GuaranteedHit 时连命中判定一起跳过）。</summary>
        public static void ApplyIgnoreDefence(HumanObject owner, Spell spell, ref DefenceType defence)
        {
            if (owner == null || !RuneRepository.Any) return;

            var rune = GetActiveRune(owner, spell, RuneEffect.IgnoreDefence);
            if (rune == null) return;

            // 与最大攻击共用同一次掷骰结果
            if (!RuneEffects.IsSwingProcActive(owner, rune)) return;

            defence = rune.GuaranteedHit ? DefenceType.None : DefenceType.Agility;
        }

        /// <summary>
        /// 召唤物增益（符文「召唤·白骨将军」等）：把 Pet*Pct 按召唤物等级累加，
        /// 直接改这只召唤物的最终属性。只在 MonsterObject.RefreshAll() 末尾被调用，
        /// 所以"刚召唤出来""升一级""重新登录载入宠物""切换符文"都会自动按当前等级重算。
        ///
        /// 没有对应符文时开场几个空判断就返回，不给普通怪物增加任何开销。
        /// </summary>
        public static void ApplyPetBoon(MonsterObject pet)
        {
            if (pet == null || !RuneRepository.Any) return;

            var master = pet.Master as HumanObject;
            if (master?.Info == null) return;

            // 只对"召唤骷髅"这个技能召唤出来的那只生效
            if (pet.Info == null || pet.Info.Name != Settings.SkeletonName)
            {
                RuneEffects.SyncPetSkin(pet, 0);
                return;
            }

            int stacks = pet.PetLevel + 1;   // 0 级技能召唤出来也按 1 层算
            if (stacks < 1) stacks = 1;

            var rune = GetActiveRune(master, Spell.SummonSkeleton, RuneEffect.PetBoon);
            if (rune == null)
            {
                RuneEffects.SyncPetSkin(pet, 0);
                return;
            }

            ScaleStat(pet, Stat.HP, rune.PetHpPct * stacks);
            ScaleStat(pet, Stat.MinAC, rune.PetDefencePct * stacks);
            ScaleStat(pet, Stat.MaxAC, rune.PetDefencePct * stacks);
            ScaleStat(pet, Stat.MinMAC, rune.PetDefencePct * stacks);
            ScaleStat(pet, Stat.MaxMAC, rune.PetDefencePct * stacks);
            ScaleStat(pet, Stat.MinDC, rune.PetDamagePct * stacks);
            ScaleStat(pet, Stat.MaxDC, rune.PetDamagePct * stacks);

            // 速度：数值是"间隔毫秒"，越小越快，所以按百分比减少，并保持原版 400ms 下限
            if (rune.PetMoveSpeedPct > 0) pet.MoveSpeed = ShortenDelay(pet.MoveSpeed, rune.PetMoveSpeedPct * stacks);
            if (rune.PetAttackSpeedPct > 0) pet.AttackSpeed = ShortenDelay((ushort)Math.Min(pet.AttackSpeed, ushort.MaxValue), rune.PetAttackSpeedPct * stacks);

            // 换皮：贴图由数据指定，客户端只负责按编号切图库
            RuneEffects.SyncPetSkin(pet, rune.PetSkin);
        }

        /// <summary>
        /// 诱惑之光驯服成功时改写宠物等级：技能等级 + PetLevelBonus。
        /// 原版驯服出来的宠物是 0 级（MaxPetLevel = 1 + 技能等级 * 2），这里把两者一起抬上去。
        /// </summary>
        /// <summary>
        /// 被动属性类符文（StatBonus）：选中就生效，直接加在当前角色的 Stats 上。
        /// 只在 RefreshStats() 里调用（那里 Stats 刚从零重建），所以天然幂等。
        /// </summary>
        /// <summary>
        /// 击杀回蓝（ManaOnKill）：怪物死亡、经验结算给击杀者时调用。
        /// 回复量 = 怪物最大 HP × 符文配置的百分比（按技能等级取值）。
        /// </summary>
        public static void ApplyManaOnKill(MapObject killer, MonsterObject monster)
        {
            if (killer == null || monster == null || !RuneRepository.Any) return;
            if (killer is not HumanObject owner) return;
            if (owner.Info?.Magics == null || owner.Info.Magics.Count == 0) return;

            int maxHp = monster.Stats[Stat.HP];
            if (maxHp <= 0) return;

            foreach (var magic in owner.Info.Magics)
            {
                if (magic == null) continue;

                var rune = GetActiveRune(owner, magic.Spell, RuneEffect.ManaOnKill);
                if (rune?.ManaPct == null || rune.ManaPct.Length == 0) continue;

                int pct = rune.ManaPct[Math.Min(magic.Level, rune.ManaPct.Length - 1)];
                if (pct <= 0) continue;

                long amount = (long)maxHp * pct / 100;
                if (amount <= 0) continue;

                owner.ChangeMP((int)Math.Min(int.MaxValue, amount));
                return;   // 只吃一次
            }
        }

        public static void ApplyPassiveStats(HumanObject owner)
        {
            if (owner == null || owner.Info == null || !RuneRepository.Any) return;
            if (owner.Info.Magics == null) return;

            foreach (var magic in owner.Info.Magics)
            {
                if (magic == null) continue;

                var rune = GetActiveRune(owner, magic.Spell, RuneEffect.StatBonus);
                if (rune?.ResolvedStatBonus == null) continue;

                // 下标直接取技能等级（0~3）；数组比技能等级短时退回最后一档
                int index = magic.Level;

                foreach (var pair in rune.ResolvedStatBonus)
                {
                    int[] values = pair.Value;
                    if (values == null || values.Length == 0) continue;

                    owner.Stats[pair.Key] += values[Math.Min(index, values.Length - 1)];
                }
            }
        }

        public static void ApplyTameLevel(HumanObject owner, MonsterObject pet, UserMagic magic)
        {
            if (owner == null || pet == null || magic == null || !RuneRepository.Any) return;

            var rune = GetActiveRune(owner, magic.Spell, RuneEffect.TameLevel);
            if (rune == null) return;

            int level = magic.Level + Math.Max(0, rune.PetLevelBonus);
            if (level <= 0) return;
            if (level > byte.MaxValue) level = byte.MaxValue;

            pet.PetLevel = (byte)level;

            // 原版 MaxPetLevel 是 (1 + 技能等级 * 2)，可能比新等级还低，先抬到新等级
            if (pet.MaxPetLevel < pet.PetLevel) pet.MaxPetLevel = pet.PetLevel;

            pet.RefreshAll();

            if (pet.HP > pet.Stats[Stat.HP]) pet.HP = pet.Stats[Stat.HP];
            pet.BroadcastHealthChange();
        }

        /// <summary>按百分比提升某个属性（pct 为已乘过层数的总百分比）。</summary>
        private static void ScaleStat(MonsterObject pet, Stat stat, int pct)
        {
            if (pct == 0) return;

            int value = pet.Stats[stat];
            if (value == 0) return;

            long scaled = (long)value * (100 + pct) / 100;
            if (scaled < 1) scaled = 1;
            if (scaled > int.MaxValue) scaled = int.MaxValue;

            pet.Stats[stat] = (int)scaled;
        }

        /// <summary>把"间隔毫秒"缩短 pct%（= 提速），并保持 400ms 下限。</summary>
        private static ushort ShortenDelay(ushort delay, int pct)
        {
            if (pct <= 0) return delay;

            if (pct >= 100) pct = 99;

            long scaled = (long)delay * (100 - pct) / 100;
            if (scaled < 400) scaled = 400;
            if (scaled > ushort.MaxValue) scaled = ushort.MaxValue;

            return (ushort)scaled;
        }

        /// <summary>找到该角色身上"当前技能 + 指定能力位"的生效符文。</summary>
        private static RuneInfo GetActiveRune(HumanObject owner, Spell spell, RuneEffect effect)
        {
            var magics = owner.Info?.Magics;
            if (magics == null || magics.Count == 0) return null;

            for (int i = 0; i < magics.Count; i++)
            {
                var magic = magics[i];
                if (magic.RuneId == 0 || magic.Spell != spell) continue;

                var rune = RuneRepository.Get(magic.RuneId);
                if (rune == null || !rune.Valid || !rune.HasEffect(effect)) continue;

                return rune;
            }

            return null;
        }

        /// <summary>技能伤害（UserMagic.GetDamage）。</summary>
        public static int Damage(UserMagic magic, int value)
        {
            if (magic == null || magic.RuneId == 0 || !RuneRepository.Any) return value;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null) return value;

            int result = value;

            if (rune.DamageFlat != 0)
                result += rune.DamageFlat;

            if (rune.DamagePct != 0)
                result = (int)Math.Round(result * (1f + rune.DamagePct / 100f));

            return result < 0 ? 0 : result;
        }

        /// <summary>技能威力（UserMagic.GetPower，护盾/回复/召唤等）。</summary>
        public static int Power(UserMagic magic, int value)
        {
            if (magic == null || magic.RuneId == 0 || !RuneRepository.Any) return value;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null) return value;

            int result = value;

            if (rune.PowerFlat != 0)
                result += rune.PowerFlat;

            if (rune.PowerPct != 0)
                result = (int)Math.Round(result * (1f + rune.PowerPct / 100f));

            return result < 0 ? 0 : result;
        }

        /// <summary>技能冷却（UserMagic.GetDelay）。</summary>
        public static long Delay(UserMagic magic, long value)
        {
            if (magic == null || magic.RuneId == 0 || !RuneRepository.Any) return value;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null) return value;

            if (rune.CooldownPct == 0) return value;

            long result = (long)Math.Round(value * (1f + rune.CooldownPct / 100f));
            if (result < 100) result = 100;

            return result;
        }

        /// <summary>技能耗蓝（HumanObject.MagicCost）。</summary>
        public static int Cost(UserMagic magic, int value)
        {
            if (magic == null || magic.RuneId == 0 || !RuneRepository.Any) return value;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null) return value;

            if (rune.CostPct == 0) return value;

            int result = (int)Math.Round(value * (1f + rune.CostPct / 100f));
            return result < 0 ? 0 : result;
        }

        /// <summary>
        /// 毒伤（符文「施毒·蚀骨」等）：把毒每 tick 的伤害按 PoisonValuePct 放大。
        /// 只改这一层"每次掉多少血"，毒的总时长、tick 间隔、抗性与覆盖规则完全保持原版。
        /// </summary>
        public static int PoisonValue(UserMagic magic, PoisonType type, int value)
        {
            if (magic == null || magic.RuneId == 0 || !RuneRepository.Any) return value;

            var rune = RuneRepository.Get(magic.RuneId);
            if (rune == null || !rune.Valid || !rune.HasEffect(RuneEffect.PoisonValueUp)) return value;
            if (rune.PoisonValuePct == 0) return value;
            if (rune.ResolvedDebuffType != PoisonType.None && rune.ResolvedDebuffType != type) return value;

            int result = (int)Math.Round(value * (1f + rune.PoisonValuePct / 100f));
            return result < 1 ? 1 : result;
        }

        /// <summary>
        /// 召唤上限（符文「召唤·双卫」等）：返回该技能同时最多能存在几只召唤物。
        /// 没有符文、或符文不带 ExtraPet 时原值返回（原版 = 1）。
        /// 只改数量，不动召唤流程本身（消耗护身符、属性、宠物等级保持原样）。
        /// </summary>
        public static int PetLimit(HumanObject owner, Spell spell, RuneEffect effect, int baseLimit)
        {
            if (owner == null || !RuneRepository.Any) return baseLimit;

            var rune = GetActiveRune(owner, spell, effect);
            if (rune == null) return baseLimit;

            if (rune.PetCount <= baseLimit) return baseLimit;

            // 再大也不能盖过全局宠物上限，否则召唤物会占满但无法存活
            int cap = Globals.MaxPets;
            return rune.PetCount < cap ? rune.PetCount : cap;
        }
    }
}
