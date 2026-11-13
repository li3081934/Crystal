using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Server.MirEnvir;

namespace Server
{
    /// <summary>
    /// 词条数量/档位规则：条数只看 ilvl 段，品质只提供“档位越级”的概率。
    /// 规则文件：Envir/Affix/AffixRules.json；缺失或损坏时使用内置默认值。
    /// </summary>
    public sealed class AffixRuleBand
    {
        public int MinIlvl { get; set; }
        public int MaxIlvl { get; set; }
        public int MinCount { get; set; }
        public int MaxCount { get; set; }
    }

    /// <summary>
    /// 品质加成：<see cref="Bonus"/> 对 <c>TierUpgradeChances</c> 是“档位 +1 的概率（%）”。
    /// 旧配置里的 GradeBonuses（加条数）已废弃：条数只由 ilvl 段决定，避免品质在终局被 MaxAffixes 抹平。
    /// </summary>
    public sealed class AffixGradeBonus
    {
        public ItemGrade Grade { get; set; }
        public int Bonus { get; set; }
    }

    public sealed class AffixRules
    {
        public bool Enabled { get; set; } = true;
        public int MaxAffixes { get; set; } = 4;
        public List<AffixRuleBand> Bands { get; set; } = CreateDefaultBands();
        /// <summary>
        /// 品质 → 档位越级概率（百分比）。命中则本次词条档位 +1（最高 T5），
        /// 用来让 Mythical/Heroic 在高 ilvl 段依然有存在感（条数已由 MaxAffixes 封顶）。
        /// </summary>
        public List<AffixGradeBonus> TierUpgradeChances { get; set; } = CreateDefaultTierChances();

        public static AffixRules Current { get; private set; } = new AffixRules();

        private static bool _warnedNoBand;

        public static readonly string AffixPath = Path.Combine(Settings.EnvirPath, "Affix");
        public static readonly string FilePath = Path.Combine(AffixPath, "AffixRules.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter() },
        };

        public static void Load()
        {
            try
            {
                if (!Directory.Exists(AffixPath))
                    Directory.CreateDirectory(AffixPath);

                if (!File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(new AffixRules(), JsonOptions));
                    MessageQueue.Instance.Enqueue($"[Affix] 已生成默认词条数量规则: {FilePath}");
                }

                AffixRules loaded = JsonSerializer.Deserialize<AffixRules>(File.ReadAllText(FilePath), JsonOptions);
                if (loaded == null)
                {
                    MessageQueue.Instance.Enqueue("[Affix] 词条数量规则为空，使用内置默认值。");
                    loaded = new AffixRules();
                }

                loaded.Normalize();
                Current = loaded;
                MessageQueue.Instance.Enqueue($"[Affix] 词条数量规则载入：{Current.Bands.Count} 个 ilvl 段，最大 {Current.MaxAffixes} 条。");
            }
            catch (Exception ex)
            {
                Current = new AffixRules();
                MessageQueue.Instance.Enqueue(new Exception($"[Affix] 词条数量规则载入失败，使用内置默认值：{ex.Message}", ex));
            }
        }

        public int TierUpgradeChance(ItemGrade grade)
        {
            if (TierUpgradeChances == null) return 0;

            for (int i = 0; i < TierUpgradeChances.Count; i++)
            {
                AffixGradeBonus bonus = TierUpgradeChances[i];
                if (bonus != null && bonus.Grade == grade) return Math.Clamp(bonus.Bonus, 0, 100);
            }

            return 0;
        }

        /// <summary>
        /// 掷词条条数：只看 ilvl 段（品质不再加条数，否则 81+ 段全被 MaxAffixes 拉平）。
        /// </summary>
        public int RollCount(int ilvl)
        {
            if (!Enabled || MaxAffixes <= 0) return 0;
            if (ilvl < 1) ilvl = 1;

            AffixRuleBand band = FindBand(ilvl);
            if (band == null)
            {
                // Bands 在 Normalize 时已按 MinIlvl 排序，因此 null 只有“ilvl 低于所有段”或“Bands 全空”两种可能。
                WarnOnce(ref _warnedNoBand,
                    $"[Affix] ilvl={ilvl} 没有任何词条规则段覆盖（Bands 为空或 MinIlvl 全部大于该等级），该等级不会掉落词条。检查 {FilePath}");
                return 0;
            }

            int count = Envir.Main.Random.Next(band.MinCount, band.MaxCount + 1);
            return Math.Max(0, Math.Min(MaxAffixes, count));
        }

        /// <summary>
        /// 命中规则段：优先取“包含 ilvl 的段”；否则回退到 MaxIlvl 最大的、低于 ilvl 的段。
        /// 不回退到“更高的段”，避免低等级物品吃到高等级词条数量。
        /// </summary>
        private AffixRuleBand FindBand(int ilvl)
        {
            AffixRuleBand below = null;

            for (int i = 0; i < Bands.Count; i++)
            {
                AffixRuleBand item = Bands[i];
                if (item == null) continue;

                if (ilvl >= item.MinIlvl && ilvl <= item.MaxIlvl) return item;
                if (ilvl > item.MaxIlvl && (below == null || item.MaxIlvl > below.MaxIlvl)) below = item;
            }

            return below;
        }

        private static void WarnOnce(ref bool flag, string message)
        {
            if (flag) return;
            flag = true;
            MessageQueue.Instance.Enqueue(message);
        }

        private void Normalize()
        {
            if (Bands == null || Bands.Count == 0) Bands = CreateDefaultBands();
            Bands.RemoveAll(x => x == null);
            Bands.Sort((a, b) => a.MinIlvl.CompareTo(b.MinIlvl));
            if (TierUpgradeChances == null || TierUpgradeChances.Count == 0)
                TierUpgradeChances = CreateDefaultTierChances();
            TierUpgradeChances.RemoveAll(x => x == null);
            for (int i = 0; i < TierUpgradeChances.Count; i++)
                TierUpgradeChances[i].Bonus = Math.Clamp(TierUpgradeChances[i].Bonus, 0, 100);
            if (MaxAffixes < 0) MaxAffixes = 0;
            if (MaxAffixes > 255) MaxAffixes = 255;

            for (int i = 0; i < Bands.Count; i++)
            {
                AffixRuleBand band = Bands[i];
                if (band == null) continue;
                if (band.MinIlvl < 1) band.MinIlvl = 1;
                if (band.MaxIlvl < band.MinIlvl) band.MaxIlvl = band.MinIlvl;
                if (band.MinCount < 0) band.MinCount = 0;
                if (band.MaxCount < band.MinCount) band.MaxCount = band.MinCount;
            }

            // 重叠/空档都会让“命中哪一段”变得依赖配置书写，这里统一告警，行为保持确定：
            // 重叠时按 MinIlvl 排序后的先后命中第一段；空档回退到前一段。
            for (int i = 1; i < Bands.Count; i++)
            {
                AffixRuleBand prev = Bands[i - 1], band = Bands[i];

                if (band.MinIlvl <= prev.MaxIlvl)
                {
                    MessageQueue.Instance.Enqueue($"[Affix] 规则段重叠：{prev.MinIlvl}-{prev.MaxIlvl} 与 {band.MinIlvl}-{band.MaxIlvl}，ilvl 将命中 {prev.MinIlvl}-{prev.MaxIlvl}。");
                }
                else if (band.MinIlvl > prev.MaxIlvl + 1)
                {
                    MessageQueue.Instance.Enqueue($"[Affix] 规则段空档：ilvl {prev.MaxIlvl + 1}-{band.MinIlvl - 1} 未覆盖，将回退到 {prev.MinIlvl}-{prev.MaxIlvl} 段。");
                }
            }
        }

        private static List<AffixRuleBand> CreateDefaultBands()
        {
            return new List<AffixRuleBand>
            {
                new AffixRuleBand { MinIlvl = 1,  MaxIlvl = 20, MinCount = 0, MaxCount = 1 },
                new AffixRuleBand { MinIlvl = 21, MaxIlvl = 40, MinCount = 0, MaxCount = 2 },
                new AffixRuleBand { MinIlvl = 41, MaxIlvl = 60, MinCount = 1, MaxCount = 2 },
                new AffixRuleBand { MinIlvl = 61, MaxIlvl = 80, MinCount = 1, MaxCount = 3 },
                new AffixRuleBand { MinIlvl = 81, MaxIlvl = 255, MinCount = 2, MaxCount = 4 },
            };
        }

        private static List<AffixGradeBonus> CreateDefaultTierChances()
        {
            return new List<AffixGradeBonus>
            {
                new AffixGradeBonus { Grade = ItemGrade.None, Bonus = 0 },
                new AffixGradeBonus { Grade = ItemGrade.Common, Bonus = 0 },
                new AffixGradeBonus { Grade = ItemGrade.Rare, Bonus = 20 },
                new AffixGradeBonus { Grade = ItemGrade.Legendary, Bonus = 40 },
                new AffixGradeBonus { Grade = ItemGrade.Mythical, Bonus = 60 },
                new AffixGradeBonus { Grade = ItemGrade.Heroic, Bonus = 60 },
            };
        }
    }
}
