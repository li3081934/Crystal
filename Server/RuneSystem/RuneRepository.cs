using System;
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
                                 || rune.HasEffect(RuneEffect.RepeatCastOnSwing);

            bool hasNumeric = rune.DamagePct != 0 || rune.DamageFlat != 0 || rune.CostPct != 0
                              || rune.CooldownPct != 0 || rune.PowerPct != 0 || rune.PowerFlat != 0;

            if (needsBehavior && !RuneBehaviorRegistry.Knows(rune))
            {
                rune.Valid = hasNumeric;
                MessageQueue.Instance.Enqueue($"[Rune] {rune.Name}: 找不到行为实现 \"{rune.BehaviorId}\"，该符文暂时不可选。");
            }
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
                    BehaviorId = RuneBehaviorRegistry.RepeatCastOnSwing,
                },
            };
        }
    }
}
