using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Server
{
    /// <summary>
    /// 词条定义仓库：读取/校验/缓存 Envir/Affix/Affixes.json。
    /// 文件缺失时用内置 56 族 × 5 档默认表生成并写盘；损坏时回退默认表，保证开服可用。
    /// </summary>
    public static class AffixRepository
    {
        public sealed class AffixInfoDto
        {
            public ushort Id { get; set; }
            public string Name { get; set; }
            public string NameKey { get; set; }
            public bool Suffix { get; set; }
            public byte Tier { get; set; }
            public int MinIlvl { get; set; }
            public string Group { get; set; }
            public string Stat { get; set; }
            public int MinValue { get; set; }
            public int MaxValue { get; set; }
            public string[] Slots { get; set; }
            public int Weight { get; set; }
            public ushort[] Excludes { get; set; }
            public int MaxPerItem { get; set; }
            public string Tags { get; set; }
        }

        public static readonly string AffixPath = Path.Combine(Settings.EnvirPath, "Affix");
        public static readonly string FilePath = Path.Combine(AffixPath, "Affixes.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        private static List<AffixInfo> _affixes = new List<AffixInfo>();
        private static Dictionary<ushort, AffixInfo> _byId = new Dictionary<ushort, AffixInfo>();
        private static Dictionary<ItemType, List<AffixInfo>> _bySlot = new Dictionary<ItemType, List<AffixInfo>>();
        private static readonly List<AffixInfo> Empty = new List<AffixInfo>();

        public static bool Any => _affixes.Count > 0;

        public static void Load()
        {
            try
            {
                if (!Directory.Exists(AffixPath))
                    Directory.CreateDirectory(AffixPath);

                if (!File.Exists(FilePath))
                {
                    LoadDefaults();
                    Save();
                    MessageQueue.Instance.Enqueue($"[Affix] 已生成默认词条池: {FilePath}");
                    return;
                }

                string json = File.ReadAllText(FilePath);
                List<AffixInfoDto> list;
                try
                {
                    list = JsonSerializer.Deserialize<List<AffixInfoDto>>(json, JsonOptions) ?? new List<AffixInfoDto>();
                }
                catch (Exception ex)
                {
                    MessageQueue.Instance.Enqueue(new Exception($"[Affix] 词条池解析失败，回退默认表：{ex.Message}", ex));
                    LoadDefaults();
                    return;
                }

                var byId = new Dictionary<ushort, AffixInfo>();
                for (int i = 0; i < list.Count; i++)
                {
                    AffixInfo info;
                    if (!TryCreate(list[i], byId, out info)) continue;
                    byId[info.Id] = info;
                }

                if (byId.Count == 0)
                {
                    MessageQueue.Instance.Enqueue("[Affix] 词条池没有有效条目，回退默认表。");
                    LoadDefaults();
                    return;
                }

                Build(byId.Values.OrderBy(x => x.Id).ToList());
                MessageQueue.Instance.Enqueue($"[Affix] 载入 {_affixes.Count} 条词条定义。");
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue(new Exception($"[Affix] 词条池载入失败，回退默认表：{ex.Message}", ex));
                LoadDefaults();
            }
        }

        public static List<AffixInfo> For(ItemType type)
        {
            return _bySlot.TryGetValue(type, out List<AffixInfo> list) ? list : Empty;
        }

        public static AffixInfo Get(ushort id)
        {
            _byId.TryGetValue(id, out AffixInfo info);
            return info;
        }

        public static List<ClientAffixInfo> GetClientInfos()
        {
            var result = new List<ClientAffixInfo>(_affixes.Count);
            for (int i = 0; i < _affixes.Count; i++)
            {
                AffixInfo info = _affixes[i];
                result.Add(new ClientAffixInfo
                {
                    Id = info.Id,
                    Name = info.Name,
                    NameKey = info.NameKey,
                    Tier = info.Tier,
                    Stat = info.Stat,
                    Suffix = info.Suffix,
                });
            }
            return result;
        }

        private static void LoadDefaults()
        {
            var byId = new Dictionary<ushort, AffixInfo>();
            List<AffixInfo> defaults = CreateDefaults();
            for (int i = 0; i < defaults.Count; i++)
            {
                byId[defaults[i].Id] = defaults[i];
            }
            Build(defaults);
        }

        private static void Build(List<AffixInfo> affixes)
        {
            _affixes = affixes;
            _byId = new Dictionary<ushort, AffixInfo>(affixes.Count);
            _bySlot = new Dictionary<ItemType, List<AffixInfo>>();

            for (int i = 0; i < affixes.Count; i++)
            {
                AffixInfo info = affixes[i];
                _byId[info.Id] = info;

                for (int j = 0; j < info.Slots.Length; j++)
                {
                    ItemType slot = info.Slots[j];
                    if (!_bySlot.TryGetValue(slot, out List<AffixInfo> list))
                        _bySlot[slot] = list = new List<AffixInfo>();
                    list.Add(info);
                }
            }
        }

        private static bool TryCreate(AffixInfoDto dto, Dictionary<ushort, AffixInfo> byId, out AffixInfo info)
        {
            info = null;
            if (dto == null) return false;

            if (dto.Id < 1001 || (dto.Id > 1275 && dto.Id < 2001) || dto.Id > 2275)
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：Id 越界 (Id={dto.Id}, {dto.Name})");
                return false;
            }

            if (byId.ContainsKey(dto.Id))
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：Id 重复 (Id={dto.Id}, {dto.Name})");
                return false;
            }

            bool expectedSuffix = dto.Id >= 2000;
            if (dto.Suffix != expectedSuffix)
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：前后缀与 Id 段不一致 (Id={dto.Id}, Suffix={dto.Suffix})");
                return false;
            }

            if (dto.Tier < 1 || dto.Tier > 5)
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：Tier 越界 (Id={dto.Id}, Tier={dto.Tier})");
                return false;
            }

            if (dto.Id % 10 != dto.Tier)
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：Id 与 Tier 不匹配 (Id={dto.Id}, Tier={dto.Tier})");
                return false;
            }

            if (!Enum.TryParse(dto.Stat, true, out Stat stat) || !Enum.IsDefined(typeof(Stat), stat) || stat == Stat.Unknown)
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：未知 Stat {dto.Stat} (Id={dto.Id})");
                return false;
            }

            if (dto.MinValue > dto.MaxValue || dto.MinValue < short.MinValue || dto.MaxValue > short.MaxValue)
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：数值区间非法 (Id={dto.Id}, {dto.MinValue}-{dto.MaxValue})");
                return false;
            }

            if (dto.Weight <= 0)
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：Weight<=0 (Id={dto.Id})");
                return false;
            }

            if (string.IsNullOrWhiteSpace(dto.Group))
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：Group 为空 (Id={dto.Id})");
                return false;
            }

            if (dto.Slots == null || dto.Slots.Length == 0)
            {
                MessageQueue.Instance.Enqueue($"[Affix] 跳过：Slots 为空 (Id={dto.Id})");
                return false;
            }

            var slots = new List<ItemType>(dto.Slots.Length);
            for (int i = 0; i < dto.Slots.Length; i++)
            {
                if (!Enum.TryParse(dto.Slots[i], true, out ItemType slot) || !Enum.IsDefined(typeof(ItemType), slot))
                {
                    MessageQueue.Instance.Enqueue($"[Affix] 跳过：未知部位 {dto.Slots[i]} (Id={dto.Id})");
                    return false;
                }
                if (!slots.Contains(slot)) slots.Add(slot);
            }

            if (slots.Count == 0) return false;

            info = new AffixInfo
            {
                Id = dto.Id,
                Name = string.IsNullOrWhiteSpace(dto.Name) ? "#" + dto.Id : dto.Name,
                NameKey = string.IsNullOrWhiteSpace(dto.NameKey) ? "Affix_" + dto.Id : dto.NameKey,
                Suffix = dto.Suffix,
                Tier = dto.Tier,
                MinIlvl = Math.Max(0, dto.MinIlvl),
                Group = dto.Group,
                Stat = stat,
                MinValue = dto.MinValue,
                MaxValue = dto.MaxValue,
                Slots = slots.ToArray(),
                Weight = dto.Weight,
                Excludes = dto.Excludes ?? Array.Empty<ushort>(),
                MaxPerItem = dto.MaxPerItem <= 0 ? 1 : dto.MaxPerItem,
                Tags = dto.Tags ?? string.Empty,
            };
            return true;
        }

        private static void Save()
        {
            try
            {
                var list = new List<AffixInfoDto>(_affixes.Count);
                for (int i = 0; i < _affixes.Count; i++)
                {
                    AffixInfo info = _affixes[i];
                    var slots = new string[info.Slots.Length];
                    for (int j = 0; j < info.Slots.Length; j++) slots[j] = info.Slots[j].ToString();

                    list.Add(new AffixInfoDto
                    {
                        Id = info.Id,
                        Name = info.Name,
                        NameKey = info.NameKey,
                        Suffix = info.Suffix,
                        Tier = info.Tier,
                        MinIlvl = info.MinIlvl,
                        Group = info.Group,
                        Stat = info.Stat.ToString(),
                        MinValue = info.MinValue,
                        MaxValue = info.MaxValue,
                        Slots = slots,
                        Weight = info.Weight,
                        Excludes = info.Excludes,
                        MaxPerItem = info.MaxPerItem,
                        Tags = info.Tags,
                    });
                }

                File.WriteAllText(FilePath, JsonSerializer.Serialize(list, JsonOptions));
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue(new Exception($"[Affix] 默认词条池写盘失败：{ex.Message}", ex));
            }
        }

        private sealed class Family
        {
            public int BaseId;
            public string Name;
            public string Group;
            public Stat Stat;
            public string[] Values;
            public ItemType[] Slots;
            public int Weight;
        }

        private static readonly ItemType[] SlotWeapon = { ItemType.Weapon };
        private static readonly ItemType[] SlotArmour = { ItemType.Armour };
        private static readonly ItemType[] SlotArmourHelmet = { ItemType.Armour, ItemType.Helmet };
        private static readonly ItemType[] SlotArmourBeltBoots = { ItemType.Armour, ItemType.Belt, ItemType.Boots };
        private static readonly ItemType[] SlotArmourHelmetBelt = { ItemType.Armour, ItemType.Helmet, ItemType.Belt };
        private static readonly ItemType[] SlotArmourSet = { ItemType.Armour, ItemType.Helmet, ItemType.Belt, ItemType.Boots };
        private static readonly ItemType[] SlotJewelry = { ItemType.Necklace, ItemType.Bracelet, ItemType.Ring };
        private static readonly ItemType[] SlotWeaponJewelry = { ItemType.Weapon, ItemType.Necklace, ItemType.Bracelet, ItemType.Ring };
        private static readonly ItemType[] SlotWeaponRing = { ItemType.Weapon, ItemType.Ring };
        private static readonly ItemType[] SlotWeaponRingBracelet = { ItemType.Weapon, ItemType.Ring, ItemType.Bracelet };
        private static readonly ItemType[] SlotWeaponNecklaceRing = { ItemType.Weapon, ItemType.Necklace, ItemType.Ring };
        private static readonly ItemType[] SlotArmourJewelry = { ItemType.Armour, ItemType.Necklace, ItemType.Bracelet, ItemType.Ring };
        private static readonly ItemType[] SlotArmourHelmetJewelry = { ItemType.Armour, ItemType.Helmet, ItemType.Necklace, ItemType.Bracelet, ItemType.Ring };
        private static readonly ItemType[] SlotArmourWeapon = { ItemType.Weapon, ItemType.Armour };
        private static readonly ItemType[] SlotAllGear =
        {
            ItemType.Weapon, ItemType.Armour, ItemType.Helmet, ItemType.Necklace,
            ItemType.Bracelet, ItemType.Ring, ItemType.Belt, ItemType.Boots,
        };

        private static readonly int[] TierMinIlvl = { 1, 21, 41, 61, 81 };

        private static readonly List<Family> Families = new List<Family>
        {
            // 前缀 A：武器向（10 族）
            // 数值口径：单族前缀+对应后缀合计约等于该件装备本体的 25%~35%，不再出现“一条顶半件装备”。
            new Family { BaseId = 1000, Name = "凶残的", Group = "p_maxdc_flat", Stat = Stat.MaxDC, Weight = 80, Slots = SlotWeapon, Values = new[] { "1-2", "2-4", "4-6", "6-8", "8-10" } },
            new Family { BaseId = 1010, Name = "锋利的", Group = "p_accuracy", Stat = Stat.Accuracy, Weight = 70, Slots = SlotWeapon, Values = new[] { "1-2", "2-3", "3-4", "4-5", "5-7" } },
            // 攻速：1 点 = -60ms 攻击间隔（HumanObject.cs:1821），且 550ms 地板在 AttackSpeed=8 时就到，
            // 所以每档恒为 1 点，靠出率（权重 25）而不是靠数值拉开强弱。
            new Family { BaseId = 1020, Name = "迅捷的", Group = "p_attack_speed", Stat = Stat.AttackSpeed, Weight = 25, Slots = SlotWeapon, Values = new[] { "1", "1", "1", "1", "1" } },
            new Family { BaseId = 1030, Name = "致命的", Group = "p_crit_rate", Stat = Stat.CriticalRate, Weight = 50, Slots = SlotWeapon, Values = new[] { "1", "1-2", "2-3", "3-4", "4-5" } },
            new Family { BaseId = 1040, Name = "沉重的", Group = "p_crit_damage", Stat = Stat.CriticalDamage, Weight = 45, Slots = SlotWeapon, Values = new[] { "1", "1-2", "2-3", "3-5", "5-8" } },
            new Family { BaseId = 1050, Name = "淬毒的", Group = "p_poison_attack", Stat = Stat.PoisonAttack, Weight = 45, Slots = SlotWeapon, Values = new[] { "1", "1", "1-2", "2", "2" } },
            new Family { BaseId = 1060, Name = "冰封的", Group = "p_freezing", Stat = Stat.Freezing, Weight = 40, Slots = SlotWeapon, Values = new[] { "1", "1", "1", "1-2", "2" } },
            new Family { BaseId = 1070, Name = "嗜血的", Group = "p_hp_drain", Stat = Stat.HPDrainRatePercent, Weight = 30, Slots = SlotWeapon, Values = new[] { "1", "1", "1-2", "2", "2-3" } },
            new Family { BaseId = 1080, Name = "神圣的", Group = "p_holy", Stat = Stat.Holy, Weight = 40, Slots = SlotWeapon, Values = new[] { "1", "1-2", "2", "2-3", "3-4" } },
            new Family { BaseId = 1090, Name = "锋锐的", Group = "p_maxdc_rate", Stat = Stat.MaxDCRatePercent, Weight = 25, Slots = SlotWeapon, Values = new[] { "1", "2", "2", "3", "4" } },

            // 前缀 B：防具/耐久向（8 族）
            new Family { BaseId = 1100, Name = "坚固的", Group = "p_maxac", Stat = Stat.MaxAC, Weight = 80, Slots = SlotArmourSet, Values = new[] { "1", "1-2", "2-4", "3-5", "4-6" } },
            new Family { BaseId = 1110, Name = "抗魔的", Group = "p_maxmac", Stat = Stat.MaxMAC, Weight = 80, Slots = SlotArmourSet, Values = new[] { "1", "1-2", "2-4", "4-5", "5-7" } },
            new Family { BaseId = 1120, Name = "厚重的", Group = "p_hp", Stat = Stat.HP, Weight = 80, Slots = SlotArmourSet, Values = new[] { "10-20", "20-30", "30-40", "40-50", "40-60" } },
            new Family { BaseId = 1130, Name = "灵气的", Group = "p_mp", Stat = Stat.MP, Weight = 70, Slots = SlotArmourSet, Values = new[] { "10-20", "20-30", "30-40", "40-50", "40-60" } },
            new Family { BaseId = 1140, Name = "荆棘的", Group = "p_reflect", Stat = Stat.Reflect, Weight = 30, Slots = SlotArmourSet, Values = new[] { "1", "1", "1-2", "2", "2-3" } },
            new Family { BaseId = 1150, Name = "轻盈的", Group = "p_agility", Stat = Stat.Agility, Weight = 70, Slots = SlotArmourSet, Values = new[] { "1", "1-2", "2", "2-3", "3-4" } },
            new Family { BaseId = 1160, Name = "坚壁的", Group = "p_damage_reduction", Stat = Stat.DamageReductionPercent, Weight = 25, Slots = SlotArmourSet, Values = new[] { "1", "1", "1-2", "2", "2-3" } },
            new Family { BaseId = 1170, Name = "强化的", Group = "p_strong", Stat = Stat.Strong, Weight = 30, Slots = SlotArmourWeapon, Values = new[] { "1", "1", "1-2", "2", "2-3" } },

            // 前缀 C：通用/收益向（10 族）
            new Family { BaseId = 1180, Name = "幸运的", Group = "p_luck", Stat = Stat.Luck, Weight = 60, Slots = SlotJewelry, Values = new[] { "1", "1", "1-2", "2", "2-3" } },
            new Family { BaseId = 1190, Name = "富贵的", Group = "p_gold", Stat = Stat.GoldDropRatePercent, Weight = 70, Slots = SlotJewelry, Values = new[] { "5-10", "10-15", "15-20", "20-30", "30-40" } },
            new Family { BaseId = 1200, Name = "寻宝的", Group = "p_item_drop", Stat = Stat.ItemDropRatePercent, Weight = 45, Slots = SlotJewelry, Values = new[] { "1-2", "2-3", "3-5", "5-7", "7-10" } },
            new Family { BaseId = 1210, Name = "博学的", Group = "p_exp", Stat = Stat.ExpRatePercent, Weight = 45, Slots = SlotJewelry, Values = new[] { "1-2", "2-3", "3-4", "4-5", "5-7" } },
            new Family { BaseId = 1220, Name = "强健的", Group = "p_health_recovery", Stat = Stat.HealthRecovery, Weight = 55, Slots = SlotAllGear, Values = new[] { "1", "1", "1-2", "2", "2" } },
            new Family { BaseId = 1230, Name = "冥想的", Group = "p_spell_recovery", Stat = Stat.SpellRecovery, Weight = 55, Slots = SlotAllGear, Values = new[] { "1", "1", "1-2", "2", "2" } },
            new Family { BaseId = 1240, Name = "解毒的", Group = "p_poison_recovery", Stat = Stat.PoisonRecovery, Weight = 50, Slots = SlotAllGear, Values = new[] { "1", "1", "1", "1-2", "2" } },
            new Family { BaseId = 1250, Name = "负重的", Group = "p_bag_weight", Stat = Stat.BagWeight, Weight = 38, Slots = SlotArmourBeltBoots, Values = new[] { "5-10", "10-20", "20-30", "30-40", "40-60" } },
            new Family { BaseId = 1260, Name = "强壮的", Group = "p_wear_weight", Stat = Stat.WearWeight, Weight = 38, Slots = SlotArmourHelmetBelt, Values = new[] { "3-5", "5-10", "10-16", "16-24", "24-32" } },
            new Family { BaseId = 1270, Name = "灵巧的", Group = "p_hand_weight", Stat = Stat.HandWeight, Weight = 30, Slots = SlotJewelry, Values = new[] { "2-4", "4-8", "8-12", "12-16", "16-24" } },

            // 后缀 A：输出向（8 族）
            new Family { BaseId = 2000, Name = "之熊", Group = "s_maxdc_flat", Stat = Stat.MaxDC, Weight = 80, Slots = SlotWeaponRingBracelet, Values = new[] { "1-2", "2-3", "3-4", "4-5", "5-6" } },
            new Family { BaseId = 2010, Name = "之烈焰", Group = "s_maxmc_flat", Stat = Stat.MaxMC, Weight = 80, Slots = SlotWeaponNecklaceRing, Values = new[] { "2-3", "3-4", "4-5", "5-6", "6-8" } },
            new Family { BaseId = 2020, Name = "之道法", Group = "s_maxsc_flat", Stat = Stat.MaxSC, Weight = 80, Slots = SlotWeaponNecklaceRing, Values = new[] { "2-3", "3-4", "4-5", "5-6", "6-8" } },
            new Family { BaseId = 2030, Name = "之精准", Group = "s_accuracy", Stat = Stat.Accuracy, Weight = 70, Slots = SlotAllGear, Values = new[] { "1", "1-2", "2-3", "3-4", "4-6" } },
            new Family { BaseId = 2040, Name = "之迅捷", Group = "s_attack_speed", Stat = Stat.AttackSpeed, Weight = 25, Slots = SlotWeaponJewelry, Values = new[] { "1", "1", "1", "1", "1" } },
            new Family { BaseId = 2050, Name = "之暴戾", Group = "s_crit_rate", Stat = Stat.CriticalRate, Weight = 50, Slots = SlotWeaponRing, Values = new[] { "1", "1-2", "2-3", "3-4", "4-5" } },
            new Family { BaseId = 2060, Name = "之重击", Group = "s_crit_damage", Stat = Stat.CriticalDamage, Weight = 45, Slots = SlotWeaponRing, Values = new[] { "1", "1", "1-2", "2", "2-3" } },
            new Family { BaseId = 2070, Name = "之剧毒", Group = "s_poison_attack", Stat = Stat.PoisonAttack, Weight = 45, Slots = SlotWeaponRing, Values = new[] { "1", "1", "1", "2", "2" } },

            // 后缀 B：防御/资源向（10 族）
            new Family { BaseId = 2080, Name = "之守护", Group = "s_maxmac", Stat = Stat.MaxMAC, Weight = 80, Slots = SlotArmourHelmetJewelry, Values = new[] { "1", "1-2", "2-3", "3-4", "4-5" } },
            new Family { BaseId = 2090, Name = "之坚韧", Group = "s_maxac", Stat = Stat.MaxAC, Weight = 80, Slots = SlotArmourHelmetJewelry, Values = new[] { "1", "1-2", "2-3", "3-4", "3-5" } },
            new Family { BaseId = 2100, Name = "之生命", Group = "s_hp", Stat = Stat.HP, Weight = 80, Slots = SlotAllGear, Values = new[] { "20-40", "30-50", "50-70", "70-90", "80-120" } },
            new Family { BaseId = 2110, Name = "之魔力", Group = "s_mp", Stat = Stat.MP, Weight = 70, Slots = SlotAllGear, Values = new[] { "20-40", "30-50", "50-70", "70-90", "80-120" } },
            new Family { BaseId = 2120, Name = "之疾风", Group = "s_agility", Stat = Stat.Agility, Weight = 70, Slots = SlotAllGear, Values = new[] { "1", "1-2", "2-3", "3-4", "4-6" } },
            new Family { BaseId = 2130, Name = "之巨力", Group = "s_wear_weight", Stat = Stat.WearWeight, Weight = 35, Slots = SlotAllGear, Values = new[] { "3-6", "6-12", "12-20", "20-28", "28-40" } },
            new Family { BaseId = 2140, Name = "之荆棘", Group = "s_reflect", Stat = Stat.Reflect, Weight = 30, Slots = SlotArmourHelmet, Values = new[] { "1", "1", "1-2", "2", "2-3" } },
            new Family { BaseId = 2150, Name = "之抗魔", Group = "s_magic_resist", Stat = Stat.MagicResist, Weight = 45, Slots = SlotArmourJewelry, Values = new[] { "1", "1-2", "2", "2-3", "3-4" } },
            new Family { BaseId = 2160, Name = "之解毒", Group = "s_poison_resist", Stat = Stat.PoisonResist, Weight = 40, Slots = SlotArmourJewelry, Values = new[] { "1", "1", "1-2", "2", "2" } },
            new Family { BaseId = 2170, Name = "之冰霜", Group = "s_freezing", Stat = Stat.Freezing, Weight = 40, Slots = SlotWeaponRing, Values = new[] { "1", "1", "1-2", "2", "2" } },

            // 后缀 C：恢复/收益/比例向（9 族）
            new Family { BaseId = 2180, Name = "之再生", Group = "s_health_recovery", Stat = Stat.HealthRecovery, Weight = 55, Slots = SlotAllGear, Values = new[] { "1", "1", "1-2", "2", "2" } },
            new Family { BaseId = 2190, Name = "之冥想", Group = "s_spell_recovery", Stat = Stat.SpellRecovery, Weight = 55, Slots = SlotAllGear, Values = new[] { "1", "1", "1-2", "2", "2" } },
            new Family { BaseId = 2200, Name = "之贪婪", Group = "s_gold", Stat = Stat.GoldDropRatePercent, Weight = 70, Slots = SlotJewelry, Values = new[] { "5-10", "10-15", "15-20", "20-30", "30-40" } },
            new Family { BaseId = 2210, Name = "之幸运", Group = "s_luck", Stat = Stat.Luck, Weight = 50, Slots = SlotJewelry, Values = new[] { "1", "1", "1-2", "2", "2-3" } },
            new Family { BaseId = 2220, Name = "之猎魔", Group = "s_item_drop", Stat = Stat.ItemDropRatePercent, Weight = 45, Slots = SlotJewelry, Values = new[] { "1", "2-3", "3-4", "4-6", "6-8" } },
            new Family { BaseId = 2230, Name = "之经验", Group = "s_exp", Stat = Stat.ExpRatePercent, Weight = 40, Slots = SlotJewelry, Values = new[] { "1", "1-2", "2", "2-3", "3-4" } },
            new Family { BaseId = 2240, Name = "之轻身", Group = "s_bag_weight", Stat = Stat.BagWeight, Weight = 35, Slots = SlotArmourBeltBoots, Values = new[] { "5-10", "10-15", "15-25", "25-35", "35-50" } },
            new Family { BaseId = 2250, Name = "之活力", Group = "s_hp_rate", Stat = Stat.HPRatePercent, Weight = 25, Slots = SlotArmourJewelry, Values = new[] { "1", "1", "1-2", "2", "2-3" } },
            new Family { BaseId = 2260, Name = "之睿智", Group = "s_mp_rate", Stat = Stat.MPRatePercent, Weight = 25, Slots = SlotArmourJewelry, Values = new[] { "1", "1", "1-2", "2", "2-3" } },
            // 2270 之攻速（AttackSpeedRatePercent）已删除：HumanObject.cs:1812 是整数乘除，
            // Stats[AttackSpeed] 常规值 2~8，5%~15% 的结果恒为 0，属死词条。需要攻速请用 1020/2040。
        };

        private static List<AffixInfo> CreateDefaults()
        {
            var result = new List<AffixInfo>(Families.Count * TierMinIlvl.Length);
            for (int i = 0; i < Families.Count; i++)
            {
                Family family = Families[i];
                for (int tier = 1; tier <= 5; tier++)
                {
                    ParseRange(family.Values[tier - 1], out int min, out int max);
                    ushort id = (ushort)(family.BaseId + tier);
                    result.Add(new AffixInfo
                    {
                        Id = id,
                        Name = family.Name,
                        NameKey = "Affix_" + id,
                        Suffix = family.BaseId >= 2000,
                        Tier = (byte)tier,
                        MinIlvl = TierMinIlvl[tier - 1],
                        Group = family.Group,
                        Stat = family.Stat,
                        MinValue = min,
                        MaxValue = max,
                        Slots = family.Slots,
                        Weight = family.Weight,
                        Excludes = Array.Empty<ushort>(),
                        MaxPerItem = 1,
                        Tags = string.Empty,
                    });
                }
            }
            return result;
        }

        private static void ParseRange(string text, out int min, out int max)
        {
            min = 0;
            max = 0;
            if (string.IsNullOrWhiteSpace(text)) return;

            int split = text.IndexOf('-');
            if (split < 0)
            {
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out min);
                max = min;
                return;
            }

            int.TryParse(text.Substring(0, split), NumberStyles.Integer, CultureInfo.InvariantCulture, out min);
            int.TryParse(text.Substring(split + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out max);
            if (max < min) max = min;
        }
    }
}
