using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Server.RuneSystem
{
    /// <summary>
    /// 符文数据仓库：读取/校验/缓存 Envir/Rune/Runes.json。
    /// 符文定义不进 Server.MirDB —— 加符文只改 JSON，不动数据库格式。
    /// </summary>
    public static class RuneRepository
    {
        public static readonly string RunePath = Path.Combine(Settings.EnvirPath, "Rune");
        public static readonly string FilePath = Path.Combine(RunePath, "Runes.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            Converters = { new JsonStringEnumConverter() },
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };

        private static Dictionary<ushort, RuneInfo> _byId = new Dictionary<ushort, RuneInfo>();
        private static Dictionary<Spell, List<RuneInfo>> _bySpell = new Dictionary<Spell, List<RuneInfo>>();

        /// <summary>没有任何符文定义时的全局快路径开关（热路径依赖它）。</summary>
        public static bool Any { get; private set; }

        public static void Load()
        {
            try
            {
                if (!Directory.Exists(RunePath))
                    Directory.CreateDirectory(RunePath);

                if (!File.Exists(FilePath))
                {
                    File.WriteAllText(FilePath, JsonSerializer.Serialize(CreateDefaults(), JsonOptions));
                    MessageQueue.Instance.Enqueue($"[Rune] 已生成默认符文数据: {FilePath}");
                }

                string json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<RuneInfo>>(json, JsonOptions) ?? new List<RuneInfo>();

                var byId = new Dictionary<ushort, RuneInfo>();
                var bySpell = new Dictionary<Spell, List<RuneInfo>>();

                foreach (var rune in list)
                {
                    if (rune == null) continue;

                    if (!Enum.IsDefined(typeof(Spell), rune.Spell))
                    {
                        MessageQueue.Instance.Enqueue($"[Rune] 跳过：未知技能 {rune.Spell} (Id={rune.Id})");
                        continue;
                    }

                    if (rune.Id == 0 || byId.ContainsKey(rune.Id))
                    {
                        MessageQueue.Instance.Enqueue($"[Rune] 跳过：Id 为 0 或重复 (Id={rune.Id}, {rune.Name})");
                        continue;
                    }

                    Resolve(rune);
                    byId[rune.Id] = rune;

                    if (!bySpell.TryGetValue(rune.Spell, out var group))
                        bySpell[rune.Spell] = group = new List<RuneInfo>();
                    group.Add(rune);
                }

                foreach (var group in bySpell.Values)
                    group.Sort((a, b) => a.Slot.CompareTo(b.Slot));

                _byId = byId;
                _bySpell = bySpell;
                Any = _byId.Count > 0;

                MessageQueue.Instance.Enqueue($"[Rune] 载入 {_byId.Count} 个符文定义，覆盖 {_bySpell.Count} 个技能。");
            }
            catch (Exception ex)
            {
                MessageQueue.Instance.Enqueue(new Exception($"[Rune] 载入失败，符文系统停用: {ex.Message}", ex));
                _byId = new Dictionary<ushort, RuneInfo>();
                _bySpell = new Dictionary<Spell, List<RuneInfo>>();
                Any = false;
            }
        }

        /// <summary>把数据里的字符串（BuffType / 属性名）解析成运行时可用的对象。</summary>
        private static void Resolve(RuneInfo rune)
        {
            rune.Valid = true;
            rune.ResolvedBuffStats = new Stats();

            ResolveTint(rune);
            ResolveDebuff(rune);
            ResolveStatBonus(rune);

            bool needsBuff = rune.HasEffect(RuneEffect.BuffOnSwing)
                             || rune.HasEffect(RuneEffect.BuffOnDealtDamage)
                             || rune.HasEffect(RuneEffect.BuffOnMagicComplete)
                             || rune.HasEffect(RuneEffect.GuaranteedCritical);

            if (needsBuff)
            {
                if (string.IsNullOrWhiteSpace(rune.BuffTypeName))
                {
                    rune.ResolvedBuffType = BuffType.Rune;
                }
                else if (Enum.TryParse(rune.BuffTypeName, true, out BuffType parsed) && Enum.IsDefined(typeof(BuffType), parsed))
                {
                    rune.ResolvedBuffType = parsed;
                }
                else
                {
                    rune.ResolvedBuffType = BuffType.Rune;
                    MessageQueue.Instance.Enqueue($"[Rune] {rune.Name}: 未知 BuffType \"{rune.BuffTypeName}\"，回退为 Rune。");
                }

                // 必暴类：把"总伤害百分比"换算成游戏现成的 Stat.CriticalDamage
                // 暴击公式：伤害 += 伤害 * (CriticalDamage / CriticalDamageWeight) * 10
                if (rune.HasEffect(RuneEffect.GuaranteedCritical) && rune.CritDamagePct > 100)
                {
                    int critDamageStat = (int)Math.Ceiling(Settings.CriticalDamageWeight * (rune.CritDamagePct - 100f) / 1000f);
                    rune.ResolvedBuffStats[Stat.CriticalDamage] = Math.Max(1, critDamageStat);
                }

                foreach (var pair in rune.BuffStats)
                {
                    if (!Enum.TryParse(pair.Key, true, out Stat stat) || !Enum.IsDefined(typeof(Stat), stat))
                    {
                        MessageQueue.Instance.Enqueue($"[Rune] {rune.Name}: 未知属性 \"{pair.Key}\"，已忽略。");
                        continue;
                    }

                    rune.ResolvedBuffStats[stat] = pair.Value;
                }
            }

            bool needsBehavior = rune.HasEffect(RuneEffect.BuffOnSwing)
                                 || rune.HasEffect(RuneEffect.BuffOnDealtDamage)
                                 || rune.HasEffect(RuneEffect.BuffOnMagicComplete)
                                 || rune.HasEffect(RuneEffect.HealOnDealtDamage)
                                 || rune.HasEffect(RuneEffect.GuaranteedCritical)
                                 || rune.HasEffect(RuneEffect.RepeatCastOnSwing)
                                 || rune.HasEffect(RuneEffect.DebuffOnHit)
                                 || rune.HasEffect(RuneEffect.DualPoison)
                                 || rune.HasEffect(RuneEffect.AreaOnHit)
                                 || rune.HasEffect(RuneEffect.DoubleCast);

            bool hasNumeric = rune.DamagePct != 0 || rune.DamageFlat != 0 || rune.CostPct != 0
                              || rune.CooldownPct != 0 || rune.PowerPct != 0 || rune.PowerFlat != 0;

            if (needsBehavior && !RuneBehaviorRegistry.Knows(rune))
            {
                rune.Valid = hasNumeric;
                MessageQueue.Instance.Enqueue($"[Rune] {rune.Name}: 找不到行为实现 \"{rune.BehaviorId}\"，该符文暂时不可选。");
            }
        }

        /// <summary>解析 DebuffType（PoisonType 名称）。</summary>
        /// <summary>把 StatBonus 里的字符串键解析成 Stat（写错的名字只报一次，不影响其它属性生效）。</summary>
        private static void ResolveStatBonus(RuneInfo rune)
        {
            if (rune.StatBonus == null || rune.StatBonus.Count == 0) return;

            var resolved = new List<KeyValuePair<Stat, int[]>>(rune.StatBonus.Count);

            foreach (var pair in rune.StatBonus)
            {
                if (!Enum.TryParse(pair.Key, true, out Stat stat) || !Enum.IsDefined(typeof(Stat), stat))
                {
                    MessageQueue.Instance.Enqueue($"[Rune] 符文 {rune} 的 StatBonus 里有无法识别的属性：{pair.Key}");
                    continue;
                }

                if (pair.Value == null || pair.Value.Length == 0) continue;

                resolved.Add(new KeyValuePair<Stat, int[]>(stat, pair.Value));
            }

            rune.ResolvedStatBonus = resolved;
        }

        private static void ResolveDebuff(RuneInfo rune)
        {
            rune.ResolvedDebuffType = PoisonType.None;

            if (rune.DebuffTickSpeed < 100) rune.DebuffTickSpeed = 100;

            if (string.IsNullOrWhiteSpace(rune.DebuffType)) return;

            if (Enum.TryParse(rune.DebuffType, true, out PoisonType type) && Enum.IsDefined(typeof(PoisonType), type))
            {
                rune.ResolvedDebuffType = type;
                return;
            }

            MessageQueue.Instance.Enqueue($"[Rune] {rune.Name}: 未知 DebuffType \"{rune.DebuffType}\"，该符文不会施加异常状态。");
        }

        /// <summary>解析 TintColor（"#RRGGBB" / "#AARRGGBB" / 十进制 ARGB）。</summary>
        private static void ResolveTint(RuneInfo rune)
        {
            rune.ResolvedTintArgb = 0;

            if (string.IsNullOrWhiteSpace(rune.TintColor) || rune.TintMs <= 0) return;

            string text = rune.TintColor.Trim().TrimStart('#');

            if (!int.TryParse(text, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int value))
            {
                MessageQueue.Instance.Enqueue($"[Rune] {rune.Name}: TintColor \"{rune.TintColor}\" 解析失败，忽略着色。");
                return;
            }

            if (text.Length <= 6)
                value |= unchecked((int)0xFF000000); // 补不透明 alpha

            rune.ResolvedTintArgb = value;
        }

        public static RuneInfo Get(ushort id)
        {
            return _byId.TryGetValue(id, out RuneInfo rune) ? rune : null;
        }

        public static IReadOnlyList<RuneInfo> ForSpell(Spell spell)
        {
            return _bySpell.TryGetValue(spell, out var group) ? group : (IReadOnlyList<RuneInfo>)Array.Empty<RuneInfo>();
        }

        public static IEnumerable<RuneInfo> All => _byId.Values;

        /// <summary>服务器第一次启动时生成的默认符文数据（只包含已上线的符文）。</summary>
        private static List<RuneInfo> CreateDefaults()
        {
            return new List<RuneInfo>
            {
                new RuneInfo
                {
                    Id = 1,
                    Spell = Spell.Fencing,
                    Slot = 1,
                    Name = "剑术·疾风",
                    Description = "攻击时 100% 概率获得「疾风」：攻击速度 +5，持续 5 秒，每次攻击刷新持续时间。",
                    Icon = 2,
                    NeedMagicLevel = 0,
                    Effect = RuneEffect.BuffOnSwing,
                    ProcChance = 100,
                    BuffTypeName = "Rune",
                    BuffDurationMs = 5000,
                    BuffStats = new Dictionary<string, int> { ["AttackSpeed"] = 5 },
                    BehaviorId = RuneBehaviorRegistry.BuffOnSwing,
                },
                new RuneInfo
                {
                    Id = 2,
                    Spell = Spell.Fencing,
                    Slot = 2,
                    Name = "剑术·饮血",
                    Description = "造成伤害时 100% 概率回复等同伤害 100% 的生命（普通攻击与技能均生效）。",
                    Icon = 2,
                    NeedMagicLevel = 0,
                    Effect = RuneEffect.HealOnDealtDamage,
                    ProcChance = 100,
                    HealPct = 100,
                    BehaviorId = RuneBehaviorRegistry.LifestealOnDealtDamage,
                },
                new RuneInfo
                {
                    Id = 3,
                    Spell = Spell.Fencing,
                    Slot = 3,
                    Name = "剑术·蓄势",
                    Description = "攻击时 100% 概率获得「蓄势」：接下来 3 次攻击必定暴击，暴击造成 200% 伤害。",
                    Icon = 2,
                    NeedMagicLevel = 0,
                    Effect = RuneEffect.GuaranteedCritical,
                    ProcChance = 100,
                    BuffTypeName = "Rune",
                    BuffDurationMs = 60000,
                    CritCharges = 3,
                    CritDamagePct = 200,
                    BehaviorId = RuneBehaviorRegistry.GuaranteedCritOnSwing,
                },
                new RuneInfo
                {
                    Id = 4,
                    Spell = Spell.Slaying,
                    Slot = 1,
                    Name = "攻杀·连斩",
                    Description = "使用攻杀剑术时 100% 概率立刻再打出一记攻杀剑术，且不额外占用攻击冷却。",
                    Icon = 6,
                    NeedMagicLevel = 0,
                    Effect = RuneEffect.RepeatCastOnSwing,
                    ProcChance = 100,
                    RepeatCount = 1,
                    RepeatDelayMs = 350,
                    BehaviorId = RuneBehaviorRegistry.RepeatCastOnSwing,
                },
                new RuneInfo
                {
                    Id = 5,
                    Spell = Spell.Slaying,
                    Slot = 2,
                    Name = "攻杀·破军",
                    Description = "本次攻杀按角色最大攻击力计算，并无视目标防御（AC）。",
                    Icon = 6,
                    NeedMagicLevel = 0,
                    Effect = RuneEffect.MaxAttackOnHit | RuneEffect.IgnoreDefence,
                    ProcChance = 100,
                    TintColor = "#FFD24A",
                    TintMs = 400,
                },
                new RuneInfo
                {
                    Id = 6,
                    Spell = Spell.Slaying,
                    Slot = 3,
                    Name = "攻杀·血饮",
                    Description = "攻杀命中时回复等同伤害 100% 的生命，并撕裂伤口造成流血（每秒 30% 伤害，持续 5 秒），刀光转为赤红。",
                    Icon = 2,
                    NeedMagicLevel = 0,
                    Effect = RuneEffect.HealOnDealtDamage | RuneEffect.DebuffOnHit,
                    ProcChance = 100,
                    HealPct = 100,
                    RequireSpellHit = true,
                    TintColor = "#FF3A3A",
                    TintMs = 450,
                    TintBody = false,
                    DebuffType = "Bleeding",
                    DebuffDuration = 5,
                    DebuffTickSpeed = 1000,
                    DebuffValuePct = 30,
                    BehaviorId = RuneBehaviorRegistry.LifestealOnDealtDamage + "," + RuneBehaviorRegistry.DebuffOnHit,
                },
                new RuneInfo
                {
                    Id = 7,
                    Spell = Spell.Thrusting,
                    Slot = 1,
                    Name = "刺杀·震魄",
                    Description = "刺杀剑气贯穿的第一个目标之后的第 2 个目标，会被震晕 2 秒（无法移动与攻击，并承受额外 50% 伤害）。",
                    Icon = 2,
                    Effect = RuneEffect.DebuffOnHit,
                    ProcChance = 100,
                    RequireSpellHit = true,
                    RequireSecondTarget = true,
                    DebuffType = "Stun",
                    DebuffDuration = 2,
                    DebuffTickSpeed = 1000,
                    DebuffValue = 1,
                    BehaviorId = RuneBehaviorRegistry.DebuffOnHit,
                },
                new RuneInfo
                {
                    Id = 8,
                    Spell = Spell.Poisoning,
                    Slot = 1,
                    Name = "施毒·双生",
                    Description = "施放施毒术时，无论携带的是绿毒还是红毒，目标都会同时中上绿毒与红毒。",
                    Icon = 2,
                    Effect = RuneEffect.DualPoison,
                    ProcChance = 100,
                    BehaviorId = RuneBehaviorRegistry.DualPoison,
                },
                new RuneInfo
                {
                    Id = 9,
                    Spell = Spell.SummonSkeleton,
                    Slot = 1,
                    Name = "召唤·白骨将军",
                    Description = "召唤出的骷髅按自身等级强化：生命 +25%、防御 +20%、攻击 +15%、移速与攻速各 +8%（每提升 1 级累加一次；7 级时为 生命 +200%、防御 +160%、攻击 +120%、速度 +64%）。",
                    Icon = 2,
                    Effect = RuneEffect.PetBoon,
                    ProcChance = 100,
                    PetHpPct = 25,
                    PetDefencePct = 20,
                    PetDamagePct = 15,
                    PetMoveSpeedPct = 8,
                    PetAttackSpeedPct = 8,
                    PetSkin = 2001,
                },
                new RuneInfo
                {
                    Id = 10,
                    Spell = Spell.SummonSkeleton,
                    Slot = 2,
                    Name = "召唤·双卫",
                    Description = "召唤骷髅的上限 +1：一次施法只召唤 1 只，但场上可以同时存在 2 只骷髅（再次施法召唤第二只，满 2 只后施法则收回身边）。",
                    Icon = 2,
                    Effect = RuneEffect.ExtraPet,
                    ProcChance = 100,
                    PetCount = 2,
                },
                new RuneInfo
                {
                    Id = 11,
                    Spell = Spell.Poisoning,
                    Slot = 2,
                    Name = "施毒·蚀骨",
                    Description = "施毒术上给目标的绿毒，每次掉血翻倍，且掉血间隔缩短 50%（总时长不变，每秒伤害翻两番）。",
                    Icon = 2,
                    Effect = RuneEffect.PoisonValueUp,
                    ProcChance = 100,
                    DebuffType = "Green",
                    PoisonValuePct = 100,
                    PoisonTickPct = 50,
                },
                new RuneInfo
                {
                    Id = 12,
                    Spell = Spell.SoulFireBall,
                    Slot = 1,
                    Name = "灵魂·炎爆",
                    Description = "灵魂火符命中时爆裂成火海：主目标周围 1 格内的所有敌人再承受 100% 伤害，爆炸特效放大 1 倍。",
                    Icon = 2,
                    Effect = RuneEffect.AreaOnHit,
                    ProcChance = 100,
                    SplashRadius = 1,
                    SplashPct = 100,
                    EffectScale = 2F,
                    BehaviorId = RuneBehaviorRegistry.AreaOnHit,
                },
                new RuneInfo
                {
                    Id = 13,
                    Spell = Spell.SoulFireBall,
                    Slot = 3,
                    Name = "灵魂·双发",
                    Description = "灵魂火符一分为二：一次施法连发两张火符，各自飞向目标并单独结算一次伤害。不额外消耗护身符。",
                    Icon = 2,
                    Effect = RuneEffect.DoubleCast,
                    ProcChance = 100,
                    CastCount = 2,
                    CastDelayMs = 150,
                    BehaviorId = RuneBehaviorRegistry.DoubleCast,
                },
                new RuneInfo
                {
                    Id = 14,
                    Spell = Spell.ElectricShock,
                    Slot = 1,
                    Name = "诱惑·御兽",
                    Description = "诱惑之光驯服成功后，宠物直接以「技能等级 + 4」的等级登场（3 级技能即 7 级宠物），生命与攻防随等级同步提高。",
                    Icon = 2,
                    Effect = RuneEffect.TameLevel,
                    ProcChance = 100,
                    PetLevelBonus = 4,
                },
                new RuneInfo
                {
                    Id = 16,
                    Spell = Spell.ThunderBolt,
                    Slot = 3,
                    Name = "雷电·赤雷",
                    Description = "雷电术的雷光转为赤红，命中后必定撕裂伤口造成流血（每秒 20% 伤害，持续 5 秒）。",
                    Icon = 2,
                    Effect = RuneEffect.DebuffOnHit,
                    ProcChance = 100,
                    DebuffType = "Bleeding",
                    DebuffDuration = 5,
                    DebuffTickSpeed = 1000,
                    DebuffValuePct = 20,
                    RequireSpellHit = true,
                    TintColor = "#FF3030",
                    TintMs = 500,
                    TintBody = false,
                    BehaviorId = RuneBehaviorRegistry.DebuffOnHit,
                },
                new RuneInfo
                {
                    Id = 15,
                    Spell = Spell.ThunderBolt,
                    Slot = 1,
                    Name = "雷电·双重",
                    Description = "雷电术必出两道：一次施法落下两记雷击，各自结算一次伤害。不额外消耗魔法值。",
                    Icon = 2,
                    Effect = RuneEffect.DoubleCast,
                    ProcChance = 100,
                    CastCount = 2,
                    CastDelayMs = 150,
                    BehaviorId = RuneBehaviorRegistry.DoubleCast,
                },
                new RuneInfo
                {
                    Id = 17,
                    Spell = Spell.ArcaneRecovery,
                    Slot = 1,
                    Name = "奥术·巨力",
                    Description = "被动：按技能等级提升背包负重上限 +10/20/30/50、穿戴重量 +1/2/3/5、腕力 +1/2/3/5。",
                    Icon = 50,
                    Effect = RuneEffect.StatBonus,
                    StatBonus = new Dictionary<string, int[]>
                    {
                        { "BagWeight", new[] { 10, 20, 30, 50 } },
                        { "WearWeight", new[] { 1, 2, 3, 5 } },
                        { "HandWeight", new[] { 1, 2, 3, 5 } },
                    },
                },
                new RuneInfo
                {
                    Id = 19,
                    Spell = Spell.FireWall,
                    Slot = 1,
                    Name = "火墙·焚天",
                    Description = "火墙的燃烧时间延长 100%（伤害不变，烧得更久）。",
                    Icon = 2,
                    Effect = RuneEffect.DurationUp,
                    ProcChance = 100,
                    DurationPct = 100,
                },
                new RuneInfo
                {
                    Id = 18,
                    Spell = Spell.ArcaneRecovery,
                    Slot = 2,
                    Name = "奥术·噬灵",
                    Description = "被动：每杀死一只怪物，按其最大生命值的 5%/10%/15%/25%（对应技能等级 0/1/2/3）回复法力。",
                    Icon = 52,
                    Effect = RuneEffect.ManaOnKill,
                    ManaPct = new[] { 5, 10, 15, 25 },
                },
            };
        }
    }
}
