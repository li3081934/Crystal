using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Server.MirDatabase;

namespace Server.MirEnvir
{
    /// <summary>
    /// Applies Flyway-style JSON migration files from Envir/Migrations to Server.MirDB.
    /// Files are named V001__description.json and are applied once, in version order.
    /// Applied versions and their checksums are stored in Server.MirDB.
    /// </summary>
    public static class DbMigrator
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static bool Apply()
        {
            Envir envir = Envir.Main;

            if (!Directory.Exists(Settings.MigrationsPath))
                return true;

            var files = Directory.GetFiles(Settings.MigrationsPath, "V*.json", SearchOption.TopDirectoryOnly)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var documents = new List<MigrationFile>();
            foreach (string file in files)
            {
                string json = File.ReadAllText(file);
                string rawChecksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
                string checksum = Checksum(json);

                MigrationDocument document;
                try
                {
                    document = JsonSerializer.Deserialize<MigrationDocument>(json, JsonOptions);
                }
                catch (Exception ex)
                {
                    MessageQueue.Instance.Enqueue($"Database migration '{Path.GetFileName(file)}' could not be parsed: {ex.Message}");
                    return false;
                }

                if (document == null || document.Version <= 0)
                {
                    MessageQueue.Instance.Enqueue($"Database migration '{Path.GetFileName(file)}' has an invalid version.");
                    return false;
                }

                if (documents.Any(x => x.Version == document.Version))
                {
                    MessageQueue.Instance.Enqueue($"Database migration version {document.Version} is defined more than once.");
                    return false;
                }

                documents.Add(new MigrationFile
                {
                    Version = document.Version,
                    Name = string.IsNullOrWhiteSpace(document.Description)
                        ? Path.GetFileNameWithoutExtension(file)
                        : document.Description,
                    RawChecksum = rawChecksum,
                    Checksum = checksum,
                    Document = document
                });
            }

            if (envir.AppliedMigrations == null)
                envir.AppliedMigrations = new List<MigrationRecord>();

            bool changed = false;
            foreach (MigrationFile migration in documents.OrderBy(x => x.Version))
            {
                MigrationRecord applied = envir.AppliedMigrations
                    .FirstOrDefault(x => x.Version == migration.Version);

                if (applied != null)
                {
                    if (!string.Equals(applied.Checksum, migration.Checksum, StringComparison.OrdinalIgnoreCase))
                    {
                        // Older records hashed the raw file (including CRLF). Upgrade them silently.
                        if (string.Equals(applied.Checksum, migration.RawChecksum, StringComparison.OrdinalIgnoreCase))
                        {
                            applied.Checksum = migration.Checksum;
                            changed = true;
                        }
                        else
                        {
                            MessageQueue.Instance.Enqueue(
                                $"Database migration V{migration.Version:000} checksum does not match the applied version. " +
                                "Create a new migration file instead of editing an applied one.");
                        }
                    }

                    continue;
                }

                try
                {
                    ApplyDocument(envir, migration.Document);
                }
                catch (Exception ex)
                {
                    MessageQueue.Instance.Enqueue($"Database migration V{migration.Version:000} failed: {ex.Message}");
                    return false;
                }

                envir.AppliedMigrations.Add(new MigrationRecord
                {
                    Version = migration.Version,
                    Name = migration.Name,
                    Checksum = migration.Checksum,
                    AppliedAt = DateTime.UtcNow.Ticks
                });

                changed = true;
                MessageQueue.Instance.Enqueue($"Applied database migration V{migration.Version:000}: {migration.Name}");
            }

            if (changed)
                envir.SaveDB();

            return true;
        }

        private static string Checksum(string json)
        {
            // Normalise line endings so a Windows checkout does not look like an edited migration.
            string normalised = json.Replace("\r\n", "\n");
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalised)));
        }

        private static void ApplyDocument(Envir envir, MigrationDocument document)
        {
            foreach (MagicSeed magic in document.Magic ?? new List<MagicSeed>())
                ApplyMagic(envir, magic);

            foreach (ItemSeed item in document.Items ?? new List<ItemSeed>())
                ApplyItem(envir, item);

            foreach (NpcSeed npc in document.Npcs ?? new List<NpcSeed>())
                ApplyNpc(envir, npc);
        }

        private static void ApplyMagic(Envir envir, MagicSeed seed)
        {
            if (seed.Spell == 0)
                throw new InvalidOperationException("Magic entry is missing its spell id.");

            MagicInfo info = envir.MagicInfoList.FirstOrDefault(x => x.Spell == seed.Spell);
            if (info == null)
            {
                info = new MagicInfo { Spell = seed.Spell };
                envir.MagicInfoList.Add(info);
            }

            if (!string.IsNullOrWhiteSpace(seed.Name)) info.Name = seed.Name;
            if (seed.BaseCost.HasValue) info.BaseCost = seed.BaseCost.Value;
            if (seed.LevelCost.HasValue) info.LevelCost = seed.LevelCost.Value;
            if (seed.Icon.HasValue) info.Icon = seed.Icon.Value;
            if (seed.Level1.HasValue) info.Level1 = seed.Level1.Value;
            if (seed.Level2.HasValue) info.Level2 = seed.Level2.Value;
            if (seed.Level3.HasValue) info.Level3 = seed.Level3.Value;
            if (seed.Need1.HasValue) info.Need1 = seed.Need1.Value;
            if (seed.Need2.HasValue) info.Need2 = seed.Need2.Value;
            if (seed.Need3.HasValue) info.Need3 = seed.Need3.Value;
            if (seed.DelayBase.HasValue) info.DelayBase = seed.DelayBase.Value;
            if (seed.DelayReduction.HasValue) info.DelayReduction = seed.DelayReduction.Value;
            if (seed.PowerBase.HasValue) info.PowerBase = seed.PowerBase.Value;
            if (seed.PowerBonus.HasValue) info.PowerBonus = seed.PowerBonus.Value;
            if (seed.MPowerBase.HasValue) info.MPowerBase = seed.MPowerBase.Value;
            if (seed.MPowerBonus.HasValue) info.MPowerBonus = seed.MPowerBonus.Value;
            if (seed.MultiplierBase.HasValue) info.MultiplierBase = seed.MultiplierBase.Value;
            if (seed.MultiplierBonus.HasValue) info.MultiplierBonus = seed.MultiplierBonus.Value;
            if (seed.Range.HasValue) info.Range = seed.Range.Value;
        }

        private static void ApplyItem(Envir envir, ItemSeed seed)
        {
            if (string.IsNullOrWhiteSpace(seed.Name))
                throw new InvalidOperationException("Item entry is missing its name.");

            ItemInfo info = envir.ItemInfoList.FirstOrDefault(
                x => x != null && string.Equals(x.Name, seed.Name, StringComparison.OrdinalIgnoreCase));

            if (info == null)
            {
                int index = seed.Index ?? 0;
                if (index <= 0 || envir.ItemInfoList.Any(x => x != null && x.Index == index))
                    index = NextItemIndex(envir);

                info = new ItemInfo
                {
                    Index = index,
                    Name = seed.Name
                };

                envir.ItemInfoList.Add(info);
                envir.ItemIndex = Math.Max(envir.ItemIndex, index);
            }

            if (seed.Type.HasValue) info.Type = seed.Type.Value;
            if (seed.Grade.HasValue) info.Grade = seed.Grade.Value;
            if (seed.RequiredType.HasValue) info.RequiredType = seed.RequiredType.Value;
            if (seed.RequiredClass.HasValue) info.RequiredClass = seed.RequiredClass.Value;
            if (seed.RequiredGender.HasValue) info.RequiredGender = seed.RequiredGender.Value;
            if (seed.Shape.HasValue) info.Shape = seed.Shape.Value;
            if (seed.Weight.HasValue) info.Weight = seed.Weight.Value;
            if (seed.Light.HasValue) info.Light = seed.Light.Value;
            if (seed.RequiredAmount.HasValue) info.RequiredAmount = seed.RequiredAmount.Value;
            if (seed.Image.HasValue) info.Image = seed.Image.Value;
            if (seed.Durability.HasValue) info.Durability = seed.Durability.Value;
            if (seed.Price.HasValue) info.Price = seed.Price.Value;
            if (seed.StackSize.HasValue) info.StackSize = seed.StackSize.Value;
            if (seed.Effect.HasValue) info.Effect = seed.Effect.Value;
            if (seed.Bind.HasValue) info.Bind = seed.Bind.Value;
            if (seed.Unique.HasValue) info.Unique = seed.Unique.Value;
            if (seed.ToolTip != null) info.ToolTip = seed.ToolTip;

            if (seed.Stats != null)
            {
                info.Stats ??= new Stats();
                foreach (var pair in seed.Stats)
                {
                    if (!Enum.TryParse(pair.Key, true, out Stat stat))
                        throw new InvalidOperationException($"Unknown stat '{pair.Key}' on item '{seed.Name}'.");

                    info.Stats[stat] = pair.Value;
                }
            }
        }

        private static void ApplyNpc(Envir envir, NpcSeed seed)
        {
            if (string.IsNullOrWhiteSpace(seed.FileName))
                throw new InvalidOperationException("NPC entry is missing its file name.");

            NPCInfo info = envir.NPCInfoList.FirstOrDefault(
                x => string.Equals(x.FileName, seed.FileName, StringComparison.OrdinalIgnoreCase));

            if (info == null)
            {
                int index = NextNpcIndex(envir);
                info = new NPCInfo
                {
                    Index = index,
                    FileName = seed.FileName
                };

                envir.NPCInfoList.Add(info);
                envir.NPCIndex = Math.Max(envir.NPCIndex, index);
            }

            if (!string.IsNullOrWhiteSpace(seed.Name)) info.Name = seed.Name;
            info.MapIndex = seed.MapIndex;
            info.Location = new Point(seed.X, seed.Y);
            if (seed.Rate.HasValue) info.Rate = seed.Rate.Value;
            if (seed.Image.HasValue) info.Image = seed.Image.Value;
            if (seed.ShowOnBigMap.HasValue) info.ShowOnBigMap = seed.ShowOnBigMap.Value;
            if (seed.BigMapIcon.HasValue) info.BigMapIcon = seed.BigMapIcon.Value;
            if (seed.CanTeleportTo.HasValue) info.CanTeleportTo = seed.CanTeleportTo.Value;
        }

        private static int NextItemIndex(Envir envir)
        {
            int highest = envir.ItemInfoList.Where(x => x != null).Select(x => x.Index).DefaultIfEmpty(0).Max();
            return Math.Max(envir.ItemIndex, highest) + 1;
        }

        private static int NextNpcIndex(Envir envir)
        {
            int highest = envir.NPCInfoList.Where(x => x != null).Select(x => x.Index).DefaultIfEmpty(0).Max();
            return Math.Max(envir.NPCIndex, highest) + 1;
        }

        private sealed class MigrationFile
        {
            public int Version;
            public string Name = string.Empty;
            public string RawChecksum = string.Empty;
            public string Checksum = string.Empty;
            public MigrationDocument Document;
        }

        private sealed class MigrationDocument
        {
            public int Version { get; set; }
            public string Description { get; set; }
            public List<MagicSeed> Magic { get; set; }
            public List<ItemSeed> Items { get; set; }
            public List<NpcSeed> Npcs { get; set; }
        }

        private sealed class MagicSeed
        {
            public string Name { get; set; }
            public Spell Spell { get; set; }
            public byte? BaseCost { get; set; }
            public byte? LevelCost { get; set; }
            public byte? Icon { get; set; }
            public byte? Level1 { get; set; }
            public byte? Level2 { get; set; }
            public byte? Level3 { get; set; }
            public ushort? Need1 { get; set; }
            public ushort? Need2 { get; set; }
            public ushort? Need3 { get; set; }
            public uint? DelayBase { get; set; }
            public uint? DelayReduction { get; set; }
            public ushort? PowerBase { get; set; }
            public ushort? PowerBonus { get; set; }
            public ushort? MPowerBase { get; set; }
            public ushort? MPowerBonus { get; set; }
            public float? MultiplierBase { get; set; }
            public float? MultiplierBonus { get; set; }
            public byte? Range { get; set; }
        }

        private sealed class ItemSeed
        {
            public string Name { get; set; }
            public int? Index { get; set; }
            public ItemType? Type { get; set; }
            public ItemGrade? Grade { get; set; }
            public RequiredType? RequiredType { get; set; }
            public RequiredClass? RequiredClass { get; set; }
            public RequiredGender? RequiredGender { get; set; }
            public short? Shape { get; set; }
            public byte? Weight { get; set; }
            public byte? Light { get; set; }
            public byte? RequiredAmount { get; set; }
            public ushort? Image { get; set; }
            public ushort? Durability { get; set; }
            public uint? Price { get; set; }
            public ushort? StackSize { get; set; }
            public byte? Effect { get; set; }
            public BindMode? Bind { get; set; }
            public SpecialItemMode? Unique { get; set; }
            public string ToolTip { get; set; }
            public Dictionary<string, int> Stats { get; set; }
        }

        private sealed class NpcSeed
        {
            public string FileName { get; set; }
            public string Name { get; set; }
            public int MapIndex { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public ushort? Rate { get; set; }
            public ushort? Image { get; set; }
            public bool? ShowOnBigMap { get; set; }
            public int? BigMapIcon { get; set; }
            public bool? CanTeleportTo { get; set; }
        }
    }
}
