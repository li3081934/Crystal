using System;
using System.IO;
using System.Linq;
using Server.MirDatabase;
using Server.MirEnvir;

namespace RuneTestTool
{
    /// <summary>
    /// 离线测试账号/角色创建工具（符文系统联调用）。
    ///
    /// 用法（必须在服务器目录下运行，或显式指定 --dir）：
    ///   dotnet run --project Tools/RuneTestTool -- --dir Build/Server/Release \
    ///        --account testgm --password 123456 --char 符文测试 --class Warrior --level 7 --gm \
    ///        --skills Fencing:3 --rune Fencing:1
    ///
    /// 注意：
    ///  - 运行前必须先关闭服务器进程（否则服务器内存里的账号数据会在保存时覆盖本工具的修改）。
    ///  - 本工具只写 Server.MirADB（账号/角色），不写 Server.MirDB（世界数据）。
    ///  - 旧版本存档（117）会在保存时自动升级到当前版本并补齐新字段。
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var options = ParseArgs(args);

            if (options.ContainsKey("smoke"))
            {
                log4net.Config.BasicConfigurator.Configure();

                string smokeHost = options.GetValueOrDefault("host", "127.0.0.1");
                int smokePort = int.TryParse(options.GetValueOrDefault("port", "7000"), out int sp) ? sp : 7000;
                string smokeAccount = options.GetValueOrDefault("account", "testgm");
                string smokePassword = options.GetValueOrDefault("password", "123456");
                string smokeSpell = options.GetValueOrDefault("spell", "Fencing");
                ushort smokeRune = ushort.TryParse(options.GetValueOrDefault("rune", "1"), out ushort sr) ? sr : (ushort)1;

                if (!Enum.TryParse(smokeSpell, true, out Spell spell))
                {
                    Console.WriteLine($"未知技能: {smokeSpell}");
                    return 1;
                }

                bool lifesteal = options.ContainsKey("lifesteal");
                string expect = options.GetValueOrDefault("expect", "buff");
                bool crit = options.ContainsKey("crit");
                bool repeat = options.ContainsKey("repeat");
                bool maxatk = options.ContainsKey("maxatk");
                bool onlySpell = options.ContainsKey("onlyspell");
                bool stun = options.ContainsKey("stun");
                bool dual = options.ContainsKey("dual");
                bool pet = options.ContainsKey("pet");
                bool extraPet = options.ContainsKey("extrapet");
                return Smoke.Run(smokeHost, smokePort, smokeAccount, smokePassword, spell, smokeRune, lifesteal, expect, crit, repeat, maxatk, onlySpell, stun, dual, pet, extraPet);
            }

            string dir = options.GetValueOrDefault("dir");
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.SetCurrentDirectory(Path.GetFullPath(dir));
            }

            log4net.Config.BasicConfigurator.Configure();

            Console.WriteLine($"工作目录: {Directory.GetCurrentDirectory()}");

            if (!File.Exists(Envir.AccountPath))
            {
                Console.WriteLine($"找不到账号库 {Envir.AccountPath}，请确认 --dir 指向服务器目录。");
                return 1;
            }

            string accountId = options.GetValueOrDefault("account", "testgm");
            string password = options.GetValueOrDefault("password", "123456");
            string charName = options.GetValueOrDefault("char", "符文测试");
            string classText = options.GetValueOrDefault("class", "Warrior");
            string genderText = options.GetValueOrDefault("gender", "Male");
            bool isGm = !options.ContainsKey("gm") || options["gm"] != "0";
            int level = int.TryParse(options.GetValueOrDefault("level", "0"), out int lv) ? lv : 0;

            if (!Enum.TryParse(classText, true, out MirClass mirClass))
            {
                Console.WriteLine($"未知职业: {classText}");
                return 1;
            }

            if (!Enum.TryParse(genderText, true, out MirGender mirGender))
            {
                Console.WriteLine($"未知性别: {genderText}");
                return 1;
            }

            Envir envir = Envir.Main;

            if (File.Exists(Envir.DatabasePath))
            {
                // 需要世界库里的 MagicInfoList 才能创建 UserMagic
                Console.WriteLine("载入世界库（只读，不保存）...");
                envir.LoadDB();
            }

            // 调试用：按关键字搜物品（例如 --searchitems 毒），方便测试脚本找物品名
            if (options.TryGetValue("searchitems", out string itemKeyword))
            {
                string kw = itemKeyword ?? string.Empty;
                int found = 0;

                foreach (var info in envir.ItemInfoList)
                {
                    bool byType = kw.StartsWith("type:");
                    ItemType wantType = ItemType.Nothing;

                    if (byType)
                    {
                        if (!Enum.TryParse(kw.Substring(5), true, out wantType)) continue;
                        if (info.Type != wantType) continue;
                    }
                    else if (!string.IsNullOrEmpty(kw) &&
                        (info.Name?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0) &&
                        (info.FriendlyName?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0))
                        continue;

                    Console.WriteLine($"#{info.Index} {info.Name} / {info.FriendlyName} type={info.Type} shape={info.Shape} stack={info.StackSize} reqLevel={info.RequiredAmount}");
                    found++;
                }

                Console.WriteLine($"匹配 {found} 条");
                return 0;
            }

            // 调试用：按关键字搜怪物（例如 --searchmonsters 骷髅），输出 Lib 文件名用的 Image 索引
            if (options.TryGetValue("searchmonsters", out string mobKeyword))
            {
                string kw = mobKeyword ?? string.Empty;
                int found = 0;

                foreach (var info in envir.MonsterInfoList)
                {
                    if (!string.IsNullOrEmpty(kw) &&
                        (info.Name?.IndexOf(kw, StringComparison.OrdinalIgnoreCase) < 0))
                        continue;

                    Console.WriteLine($"#{info.Index} {info.Name} Image={info.Image} -> {(Monster)info.Image} AI={(int)info.AI} level={info.Level}");
                    found++;
                }

                Console.WriteLine($"匹配 {found} 条");
                return 0;
            }

            // 角色存档里存着 Buff（按 BuffType 反序列化），必须先把 BuffInfo 表准备好
            envir.BuffInfoList.Clear();
            foreach (var buff in BuffInfo.Load())
                envir.BuffInfoList.Add(buff);

            // 符文定义（用于校验预选符文）
            Server.RuneSystem.RuneBehaviorRegistry.Scan();
            Server.RuneSystem.RuneRepository.Load();

            Console.WriteLine("载入账号库...");
            envir.LoadAccounts();

            var account = envir.AccountList.FirstOrDefault(x =>
                string.Equals(x.AccountID, accountId, StringComparison.OrdinalIgnoreCase));

            if (account == null)
            {
                account = new AccountInfo { Index = ++envir.NextAccountID, AccountID = accountId };
                envir.AccountList.Add(account);
                Console.WriteLine($"已创建账号: {accountId}");
            }
            else
            {
                Console.WriteLine($"已存在账号: {accountId}（将更新密码/GM 权限）");
            }

            account.Password = password;
            account.AdminAccount = isGm;

            // 新建账号时把序列化用到的字符串字段补齐（BinaryWriter 不接受 null）
            account.UserName ??= string.Empty;
            account.SecretQuestion ??= string.Empty;
            account.SecretAnswer ??= string.Empty;
            account.EMailAddress ??= string.Empty;
            account.CreationIP ??= "127.0.0.1";
            account.BanReason ??= string.Empty;
            account.LastIP ??= string.Empty;
            if (account.CreationDate == default) account.CreationDate = DateTime.Now;

            var character = account.Characters.FirstOrDefault(x => !x.Deleted && x.Name == charName);

            if (character == null)
            {
                character = new CharacterInfo
                {
                    Index = ++envir.NextCharacterID,
                    Name = charName,
                    Class = mirClass,
                    Gender = mirGender,
                    AccountInfo = account,
                    Level = 0, // 0 = 让服务器首次登录时自动初始化（等级、初始装备、回城点）
                };

                character.Heroes = new HeroInfo[character.MaximumHeroCount];
                character.CreationIP = "127.0.0.1";
                character.CreationDate = DateTime.Now;
                character.BanReason = string.Empty;
                character.LastIP = string.Empty;

                account.Characters.Add(character);
                envir.CharacterList.Add(character);
                Console.WriteLine($"已创建角色: {charName}（{mirClass}/{mirGender}）");
            }
            else
            {
                Console.WriteLine($"已存在角色: {charName}（将更新技能/等级/符文）");
            }

            if (level > 0)
                character.Level = (ushort)level;

            // 技能：--skills Fencing:3,Slaying:2
            string skills = options.GetValueOrDefault("skills");
            if (!string.IsNullOrWhiteSpace(skills))
            {
                if (envir.MagicInfoList.Count == 0)
                {
                    Console.WriteLine("警告: 世界库尚未载入，无法写入技能。");
                }
                else
                {
                    foreach (string entry in skills.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        string[] parts = entry.Split(':');
                        if (!Enum.TryParse(parts[0].Trim(), true, out Spell spell) || spell == Spell.None) continue;

                        byte spellLevel = parts.Length > 1 && byte.TryParse(parts[1], out byte sl) ? (byte)Math.Min((byte)3, sl) : (byte)0;

                        var magic = character.Magics.FirstOrDefault(x => x.Spell == spell);
                        if (magic == null)
                        {
                            magic = new UserMagic(spell);
                            character.Magics.Add(magic);
                        }

                        magic.Level = spellLevel;
                        Console.WriteLine($"  技能: {spell} = {spellLevel}");
                    }
                }
            }

            // 移除技能：--removeskills FireBall,Slaying
            string removeSkills = options.GetValueOrDefault("removeskills");
            if (!string.IsNullOrWhiteSpace(removeSkills))
            {
                foreach (string entry in removeSkills.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!Enum.TryParse(entry.Trim(), true, out Spell spell)) continue;

                    int removed = character.Magics.RemoveAll(x => x.Spell == spell);
                    if (removed > 0) Console.WriteLine($"  移除技能: {spell}");
                }
            }

            // 预选符文：--rune Fencing:1
            string runes = options.GetValueOrDefault("rune");
            if (!string.IsNullOrWhiteSpace(runes))
            {
                foreach (string entry in runes.Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] parts = entry.Split(':');
                    if (!Enum.TryParse(parts[0].Trim(), true, out Spell spell)) continue;
                    if (parts.Length < 2 || !ushort.TryParse(parts[1], out ushort runeId)) continue;

                    character.Runes.Set(spell, runeId);
                    Console.WriteLine($"  符文: {spell} -> #{runeId}");
                }
            }

            Console.WriteLine("保存账号库...");
            envir.SaveAccounts();

            Console.WriteLine();
            Console.WriteLine("完成。进游戏后可用 GM 命令继续调试：");
            Console.WriteLine("  @LEVEL 7                       # 升级到 7 级（Fencing 可学）");
            Console.WriteLine("  @GIVESKILL Fencing 3           # 学会并升到 3 级基本剑术");
            Console.WriteLine("  @GIVESKILL Slaying 3           # 攻杀剑术（用于对比）");
            Console.WriteLine("  @SUPERMAN                      # GM 无敌（可选）");
            Console.WriteLine("  @CLEARBUFFS                    # 清空 Buff（测试符文 Buff 时可用来重置）");
            return 0;
        }

        private static Dictionary<string, string> ParseArgs(string[] args)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--")) continue;

                string key = arg.Substring(2);
                string value = "1";

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--"))
                {
                    value = args[++i];
                }

                result[key] = value;
            }

            return result;
        }
    }
}
