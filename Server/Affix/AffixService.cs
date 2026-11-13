using System;
using System.Collections.Generic;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server
{
    /// <summary>
    /// 词条生成与校验。掉落、GM 调试共用同一套规则：
    /// 先按 ilvl 掷数量，再按族取“当前 ilvl 可用的最高档”，最后在族之间按权重抽取。
    /// </summary>
    public static class AffixService
    {
        public static bool IsEligible(ItemType type)
        {
            return AffixRepository.For(type).Count > 0;
        }

        /// <summary>
        /// 给刚创建的掉落物附加词条。已有词条的物品不会被覆盖。
        /// </summary>
        public static void Attach(UserItem item, int ilvl)
        {
            if (item == null || item.Info == null) return;
            if (!AffixRules.Current.Enabled) return;
            if (!IsEligible(item.Info.Type)) return;

            if (item.Affixes == null) item.Affixes = new List<AffixInstance>();
            if (item.Affixes.Count > 0) return;

            int count = AffixRules.Current.RollCount(ilvl);
            if (count <= 0) return;

            var prefixes = new List<AffixInfo>();
            var suffixes = new List<AffixInfo>();
            List<AffixInfo> pool = AffixRepository.For(item.Info.Type);
            for (int i = 0; i < pool.Count; i++)
            {
                AffixInfo info = pool[i];
                if (info.Suffix) suffixes.Add(info);
                else prefixes.Add(info);
            }

            int prefixCount = count / 2;
            int suffixCount = count - prefixCount;
            if (Envir.Main.Random.Next(2) == 0)
            {
                int swap = prefixCount;
                prefixCount = suffixCount;
                suffixCount = swap;
            }

            RollSide(item, prefixes, prefixCount, ilvl);
            RollSide(item, suffixes, suffixCount, ilvl);

            int remaining = count - item.Affixes.Count;
            for (int i = 0; i < remaining; i++)
            {
                bool prefixFirst = Envir.Main.Random.Next(2) == 0;
                if (prefixFirst)
                {
                    if (TryAdd(item, prefixes, ilvl)) continue;
                    if (!TryAdd(item, suffixes, ilvl)) break;
                }
                else
                {
                    if (TryAdd(item, suffixes, ilvl)) continue;
                    if (!TryAdd(item, prefixes, ilvl)) break;
                }
            }

            if (item.Affixes.Count > 0)
                item.AffixIlvl = (ushort)Math.Clamp(ilvl, 0, ushort.MaxValue);
        }

        /// <summary>
        /// GM 重掷：清空后按 ilvl 重新生成。
        /// </summary>
        public static bool TryRoll(UserItem item, int ilvl, out string error)
        {
            error = null;
            if (item == null || item.Info == null)
            {
                error = "物品不存在。";
                return false;
            }

            if (!IsEligible(item.Info.Type))
            {
                error = "该物品类型不支持词条。";
                return false;
            }

            item.Affixes ??= new List<AffixInstance>();
            item.Affixes.Clear();
            item.AffixIlvl = 0;

            Attach(item, ilvl);
            if (item.Affixes.Count == 0)
            {
                error = "本次没有生成词条（检查 ilvl 与 AffixRules.json）。";
                return false;
            }

            return true;
        }

        /// <summary>
        /// GM 直接添加指定词条；value 小于等于 0 时按定义区间随机。
        /// </summary>
        public static bool Add(UserItem item, ushort id, int value, out string error)
        {
            error = null;
            if (item == null || item.Info == null)
            {
                error = "物品不存在。";
                return false;
            }

            AffixInfo info = AffixRepository.Get(id);
            if (info == null)
            {
                error = $"词条 #{id} 不存在。";
                return false;
            }

            if (!SlotAllows(info, item.Info.Type))
            {
                error = $"词条 #{id} 不能出现在 {item.Info.Type} 上。";
                return false;
            }

            item.Affixes ??= new List<AffixInstance>();
            int maxAffixes = Math.Max(0, AffixRules.Current.MaxAffixes);
            if (item.Affixes.Count >= maxAffixes)
            {
                error = maxAffixes == 0
                    ? "词条上限为 0（AffixRules.json 的 MaxAffixes），当前不允许添加词条。"
                    : $"该物品已有 {item.Affixes.Count} 条词条，达到上限 {maxAffixes}。";
                return false;
            }

            if (!CanAdd(item, info, out error)) return false;

            if (value <= 0)
                value = info.MinValue >= info.MaxValue ? info.MinValue : Envir.Main.Random.Next(info.MinValue, info.MaxValue + 1);

            if (value < short.MinValue || value > short.MaxValue)
            {
                error = $"词条数值 {value} 超出范围。";
                return false;
            }

            item.Affixes.Add(new AffixInstance
            {
                Id = info.Id,
                Tier = info.Tier,
                Stat = info.Stat,
                Value = (short)value,
            });

            if (item.AffixIlvl == 0)
                item.AffixIlvl = (ushort)Math.Clamp(info.MinIlvl, 0, ushort.MaxValue);

            return true;
        }

        public static void Clear(UserItem item)
        {
            if (item == null) return;
            item.Affixes?.Clear();
            item.AffixIlvl = 0;
        }

        /// <summary>
        /// 校验一条物品上的词条（ID、部位、属性、重复、互斥、数量上限）。
        /// 数值允许 GM 覆写，因此不强制落在定义区间内。
        /// </summary>
        public static bool Validate(UserItem item, out string error)
        {
            error = null;
            if (item == null || item.Info == null)
            {
                error = "物品不存在。";
                return false;
            }

            if (item.Affixes == null || item.Affixes.Count == 0) return true;

            int maxAffixes = Math.Max(0, AffixRules.Current.MaxAffixes);
            if (item.Affixes.Count > maxAffixes)
            {
                error = $"词条数量 {item.Affixes.Count} 超过上限 {maxAffixes}。";
                return false;
            }

            var seen = new HashSet<ulong>();
            for (int i = 0; i < item.Affixes.Count; i++)
            {
                AffixInstance affix = item.Affixes[i];
                if (affix == null)
                {
                    error = "存在空词条。";
                    return false;
                }

                AffixInfo info = AffixRepository.Get(affix.Id);
                if (info == null)
                {
                    error = $"词条 #{affix.Id} 不存在。";
                    return false;
                }

                if (info.Tier != affix.Tier)
                {
                    error = $"词条 #{affix.Id} 的档位与定义不一致。";
                    return false;
                }

                if (info.Stat != affix.Stat)
                {
                    error = $"词条 #{affix.Id} 的属性与定义不一致。";
                    return false;
                }

                if (!SlotAllows(info, item.Info.Type))
                {
                    error = $"词条 #{affix.Id} 不能出现在 {item.Info.Type} 上。";
                    return false;
                }

                if (affix.Value < short.MinValue || affix.Value > short.MaxValue)
                {
                    error = $"词条 #{affix.Id} 数值越界。";
                    return false;
                }

                ulong key = affix.Id;
                if (!seen.Add(key))
                {
                    error = $"词条 #{affix.Id} 重复。";
                    return false;
                }

                if (!CanAdd(item, info, out error, skipId: affix.Id)) return false;
            }

            return true;
        }

        /// <summary>
        /// 启动时校验全部存档物品上的词条，只报告不修改。
        /// 用于发现：手工改库、旧版残留、Affixes.json 改定义后与存档不一致。
        /// </summary>
        public static void ValidateDatabase(List<AccountInfo> accounts)
        {
            if (accounts == null || !AffixRepository.Any) return;

            // 系统关闭（或上限为 0）时，“存档里有词条”不算异常：那些词条只是不再参与新的生成。
            if (!AffixRules.Current.Enabled || AffixRules.Current.MaxAffixes <= 0)
            {
                MessageQueue.Instance.Enqueue("[Affix] 词条系统已关闭或 MaxAffixes<=0，跳过存档词条校验。");
                return;
            }

            int checkedItems = 0, badItems = 0;

            foreach (AccountInfo account in accounts)
            {
                if (account == null) continue;

                string accountName = string.IsNullOrEmpty(account.UserName) ? $"账号 #{account.Index}" : $"账号 {account.UserName}";
                ValidateItems(account.Storage, accountName, ref checkedItems, ref badItems);

                if (account.Characters == null) continue;

                foreach (CharacterInfo character in account.Characters)
                {
                    if (character == null) continue;

                    string owner = $"{accountName}/{character.Name}";
                    ValidateItems(character.Equipment, owner, ref checkedItems, ref badItems);
                    ValidateItems(character.Inventory, owner, ref checkedItems, ref badItems);
                    ValidateItems(character.QuestInventory, owner, ref checkedItems, ref badItems);
                }
            }

            if (checkedItems == 0 && badItems == 0)
            {
                MessageQueue.Instance.Enqueue("[Affix] 词条校验：存档中没有任何带词条的物品。");
                return;
            }

            MessageQueue.Instance.Enqueue($"[Affix] 词条校验完成：检查 {checkedItems} 件带词条物品，异常 {badItems} 件。");
        }

        private static void ValidateItems(UserItem[] items, string owner, ref int checkedItems, ref int badItems)
        {
            if (items == null) return;

            for (int i = 0; i < items.Length; i++)
            {
                UserItem item = items[i];
                if (item == null || item.Info == null) continue;
                if (item.Affixes == null || item.Affixes.Count == 0) continue;

                checkedItems++;
                if (Validate(item, out string error)) continue;

                badItems++;
                if (badItems > 20) continue; // 只打前 20 条，避免损坏库刷屏

                string name = string.IsNullOrEmpty(item.Info.FriendlyName) ? item.Info.Name : item.Info.FriendlyName;
                MessageQueue.Instance.Enqueue($"[Affix] 词条校验失败：{owner} 的 {name}(UniqueID={item.UniqueID})：{error}");
            }
        }

        private static void RollSide(UserItem item, List<AffixInfo> pool, int count, int ilvl)
        {
            for (int i = 0; i < count; i++)
            {
                if (!TryAdd(item, pool, ilvl)) break;
            }
        }

        private static bool TryAdd(UserItem item, List<AffixInfo> pool, int ilvl)
        {
            var fresh = new Dictionary<string, AffixInfo>(StringComparer.OrdinalIgnoreCase);
            var repeated = new Dictionary<string, AffixInfo>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < pool.Count; i++)
            {
                AffixInfo info = pool[i];
                if (info == null || info.MinIlvl > ilvl) continue;
                if (!CanAdd(item, info, out _)) continue;

                // 同族未满 MaxPerItem 才允许再来一条；优先抽“新族”，避免一件装备全是同一属性。
                Dictionary<string, AffixInfo> bucket = GroupCount(item, info.Group) == 0 ? fresh : repeated;
                if (!bucket.TryGetValue(info.Group, out AffixInfo current) || info.Tier > current.Tier)
                    bucket[info.Group] = info;
            }

            AffixInfo chosen = PickWeighted(fresh) ?? PickWeighted(repeated);
            if (chosen == null) return false;

            chosen = UpgradeTier(chosen, AffixRules.Current.TierUpgradeChance(item.Info.Grade));

            int value = chosen.MinValue >= chosen.MaxValue
                ? chosen.MinValue
                : Envir.Main.Random.Next(chosen.MinValue, chosen.MaxValue + 1);

            item.Affixes.Add(new AffixInstance
            {
                Id = chosen.Id,
                Tier = chosen.Tier,
                Stat = chosen.Stat,
                Value = (short)Math.Clamp(value, short.MinValue, short.MaxValue),
            });
            return true;
        }

        /// <summary>
        /// 品质带来的“档位越级”：按百分比概率把档位 +1（最高 T5）。
        /// 越级后的档位可以高于 ilvl 对应的自然档位，这是品质的价值所在；
        /// AffixRepository 每个族都定义了 T1..T5，所以升级目标一定存在（取不到时保持原样）。
        /// </summary>
        private static AffixInfo UpgradeTier(AffixInfo info, int chancePercent)
        {
            if (info == null || chancePercent <= 0 || info.Tier >= 5) return info;
            if (Envir.Main.Random.Next(100) >= chancePercent) return info;

            ushort baseId = (ushort)(info.Id - info.Tier);
            return AffixRepository.Get((ushort)(baseId + info.Tier + 1)) ?? info;
        }

        /// <summary>
        /// 同族数量上限由 AffixInfo.MaxPerItem 决定（默认 1 = 同族互斥）。
        /// skipId 用于校验：跳过与被校验词条同 Id 的既有实例，避免自己和自己冲突。
        /// </summary>
        private static bool CanAdd(UserItem item, AffixInfo info, out string error, ushort skipId = 0)
        {
            error = null;
            if (item.Affixes == null) return true;

            int sameGroup = 0;
            for (int i = 0; i < item.Affixes.Count; i++)
            {
                AffixInstance affix = item.Affixes[i];
                if (affix == null || affix.Id == skipId) continue;

                AffixInfo existing = AffixRepository.Get(affix.Id);
                if (existing != null && existing.Group.Equals(info.Group, StringComparison.OrdinalIgnoreCase))
                    sameGroup++;

                if (Contains(info.Excludes, affix.Id) || (existing != null && Contains(existing.Excludes, info.Id)))
                {
                    error = $"词条 #{info.Id} 与 #{affix.Id} 互斥。";
                    return false;
                }
            }

            int maxPerItem = Math.Max(1, info.MaxPerItem);
            if (sameGroup >= maxPerItem)
            {
                error = $"词条族 {info.Group} 已达单件上限 {maxPerItem}。";
                return false;
            }

            return true;
        }

        private static int GroupCount(UserItem item, string group)
        {
            if (item.Affixes == null) return 0;

            int count = 0;
            for (int i = 0; i < item.Affixes.Count; i++)
            {
                AffixInstance affix = item.Affixes[i];
                if (affix == null) continue;
                AffixInfo info = AffixRepository.Get(affix.Id);
                if (info != null && info.Group.Equals(group, StringComparison.OrdinalIgnoreCase)) count++;
            }

            return count;
        }

        private static AffixInfo PickWeighted(Dictionary<string, AffixInfo> candidates)
        {
            if (candidates == null || candidates.Count == 0) return null;

            int total = 0;
            foreach (AffixInfo info in candidates.Values)
                total += Math.Max(1, info.Weight);

            int roll = Envir.Main.Random.Next(total);
            foreach (AffixInfo info in candidates.Values)
            {
                int weight = Math.Max(1, info.Weight);
                if (roll < weight) return info;
                roll -= weight;
            }

            return null;
        }

        private static bool SlotAllows(AffixInfo info, ItemType type)
        {
            if (info.Slots == null) return false;
            for (int i = 0; i < info.Slots.Length; i++)
                if (info.Slots[i] == type) return true;
            return false;
        }

        private static bool Contains(ushort[] values, ushort id)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Length; i++)
                if (values[i] == id) return true;
            return false;
        }
    }
}
