using Client.MirSounds;

namespace Client
{
    class Settings
    {
        public const long CleanDelay = 600000;

        public static int ScreenWidth = 1024, ScreenHeight = 768;
        private static InIReader Reader = new InIReader(@".\Mir2Config.ini");
        private static InIReader QuestTrackingReader = new InIReader(Path.Combine(UserDataPath, @".\QuestTracking.ini"));

        private static bool _useTestConfig;
        public static bool UseTestConfig
        {
            get
            {
                return _useTestConfig;
            }
            set
            {
                if (value == true)
                {
                    Reader = new InIReader(@".\Mir2Test.ini");
                }
                _useTestConfig = value;
            }
        }

        public const string DataPath = @".\Data\",
                            MapPath = @".\Map\",
                            SoundPath = @".\Sound\",
                            ExtraDataPath = @".\Data\Extra\",
                            ShadersPath = @".\Data\Shaders\",
                            MonsterPath = @".\Data\Monster\",
                            GatePath = @".\Data\Gate\",
                            FlagPath = @".\Data\Flag\",
                            SiegePath = @".\Data\Siege\",
                            NPCPath = @".\Data\NPC\",
                            CArmourPath = @".\Data\CArmour\",
                            CWeaponPath = @".\Data\CWeapon\",
                            CWeaponEffectPath = @".\Data\CWeaponEffect\",
                            CHairPath = @".\Data\CHair\",
                            AArmourPath = @".\Data\AArmour\",
                            AWeaponPath = @".\Data\AWeapon\",
                            AHairPath = @".\Data\AHair\",
                            ARArmourPath = @".\Data\ARArmour\",
                            ARWeaponPath = @".\Data\ARWeapon\",
                            ARHairPath = @".\Data\ARHair\",
                            CHumEffectPath = @".\Data\CHumEffect\",
                            AHumEffectPath = @".\Data\AHumEffect\",
                            ARHumEffectPath = @".\Data\ARHumEffect\",
                            MountPath = @".\Data\Mount\",
                            FishingPath = @".\Data\Fishing\",
                            PetsPath = @".\Data\Pet\",
                            TransformPath = @".\Data\Transform\",
                            TransformMountsPath = @".\Data\TransformRide2\",
                            TransformEffectPath = @".\Data\TransformEffect\",
                            TransformWeaponEffectPath = @".\Data\TransformWeaponEffect\",
                            MouseCursorPath = @".\Data\Cursors\",
                            ResourcePath = @".\DirectX\",
                            UserDataPath = @".\Data\UserData\",
                            DbLanguageJsonPath = @".\DbLanguage.json";

        //Logs
        public static bool LogErrors = true;
        public static bool LogChat = true;
        public static int RemainingErrorLogs = 100;

        //Graphics
        public static bool FullScreen = true, Borderless = true, TopMost = true, MouseClip = false;
        public static string FontName = "Arial"; //"MS Sans Serif"
        public static float FontSize = 8F;
        public static bool UseMouseCursors = true;

        public static bool FPSCap = true;
        public static int MaxFPS = 100;
        public static int Resolution = 1024;
        public static bool DebugMode = false;

        //Network
        public static bool UseConfig = false;
        public static string IPAddress = "127.0.0.1";
        public static int Port = 7000;
        public const int TimeOut = 5000;

        //Sound
        public static int SoundOverLap = 3;
        private static byte _volume = 100;
        public static int SoundCleanMinutes = 5;

        public static byte Volume
        {
            get { return _volume; }
            set
            {
                switch (value)
                {
                    case > 100:
                        _volume = (byte)100;
                        break;
                    case <= 0:
                        _volume = (byte)0;
                        break;
                    default:
                        _volume = value;
                        break;
                }

                SoundManager.Vol = Convert.ToInt32(_volume);
            }
        }

        private static byte _musicVolume = 100;
        public static byte MusicVolume
        {
            get { return _musicVolume; }
            set
            {
                switch (value)
                {
                    case > 100:
                        _musicVolume = (byte)100;
                        break;
                    case <= 0:
                        _musicVolume = (byte)0;
                        break;
                    default:
                        _musicVolume = value;
                        break;
                }

                SoundManager.MusicVol = Convert.ToInt32(_musicVolume);
            }
        }

        //AutoPot (客户端内挂：自动喝药)
        public static bool AutoPotEnabled = true;      // 默认开启：只要配了药水+阈值就该生效
        public static int AutoPotHPPercent = 50;
        public static int AutoPotMPPercent = 30;
        public static int AutoPotHPItem = 0;    // 药水 ItemInfo.Index，0 = 未设置
        public static int AutoPotMPItem = 0;
        public static int AutoPotDelay = 1000;  // 检测间隔(毫秒)

        // ==== 道士：自动治疗术 ====
        public static bool AutoHealEnabled = true;   // 血量低于阈值时给自己放治疗术
        public static int AutoHealPercent = 60;      // 治疗术触发血量百分比（0=关闭）

        // ==== 道士：自动施毒（目标身上没有对应毒时补）====
        public static bool AutoPoisonGreen = false;  // 绿毒
        public static bool AutoPoisonRed = false;    // 红毒

        // ==== 法师：自动技能 ====
        public static bool AutoSkillEnabled = false; // 自动用技能打
        public static int AutoSkillSpell = 0;        // 选中的技能 (int)Spell，0 = 未选择

        // ==== 内挂配置按角色隔离 ====
        /// <summary>当前载入的内挂配置所属角色名（空 = 还没进游戏，用主配置）</summary>
        public static string AutoFightProfile = "";

        private static InIReader ProfileReader;

        /// <summary>内挂配置写到哪里：有角色档案就写档案，否则写主配置</summary>
        private static InIReader AutoIni
        {
            get { return ProfileReader ?? Reader; }
        }

        //AutoFight (客户端内挂：自动战斗)
        public static bool AutoFightEnabled = false;

        // 寻敌
        public static int AutoFightTargetRange = 8;          // 寻敌范围(格)
        public static int AutoFightSeekRange = 16;           // 范围内没怪时，扩大到这个范围继续找
        public static bool AutoFightRoam = true;             // 完全没有目标时随机漫游
        public static int AutoFightRoamRange = 6;            // 漫游步长(格)：一次朝某个方向走多远
        public static int AutoFightPriority = 0;             // 0=最近 1=血最少
        public static bool AutoFightIgnorePlayers = true;    // 不打玩家
        public static string AutoFightIgnoreMonsters = "";   // 不攻击的怪物名单（逗号分隔，按名字匹配）

        // 战斗
        public static bool AutoFightChase = true;            // 追击
        public static int AutoFightAttackRange = 1;          // 攻击距离(格)
        public static int AutoFightAttackDelay = 300;        // 额外攻击间隔(ms)
        public static bool AutoFightBasicAttack = true;      // 普通攻击开关
        public static bool AutoFightKeepDistance = true;     // 保持距离（风筝）
        public static int AutoFightKeepRange = 5;            // 保持距离(格)：怪近到这个距离以内就后撤
        public static bool AutoFightLineOfSight = true;      // 障碍物检测：中间隔墙时不远程空放

        // 拾取
        public static bool AutoFightLoot = true;
        public static int AutoFightLootRange = 3;            // 拾取范围(格)
        public static int AutoFightLootSweep = 10;           // 周围没怪时，扩大到这个范围回收掉落(0=关闭)
        // 拾取类型过滤（按 CategoryType 分类，未勾选的一律不捡）
        public static bool AutoFightLootGold = true;         // 金币
        public static bool AutoFightLootPotion = true;       // 药品/食物
        public static bool AutoFightLootBook = true;         // 书籍
        public static bool AutoFightLootWeapon = true;       // 武器
        public static bool AutoFightLootArmour = true;       // 衣服/头盔/靴子/腰带
        public static bool AutoFightLootJewel = true;        // 饰品（项链/手镯/戒指）
        public static bool AutoFightLootOther = true;        // 其它（矿石/材料/卷轴…）
        public static bool AutoFightLootTreasure = true;     // 极品：带附加属性的物品，不看类型
        public static bool AutoFightLootFirst = false;       // 先捡后打

        //Download (资源按需下载)
        public static bool DownloadEnabled = true;
        public static string DownloadHost = "https://mirfiles.co.uk/resources/mir2/crystal/patch/";
        public static string DownloadProxy = "";           // 例: http://user:pass@127.0.0.1:7890 或 socks5://127.0.0.1:1080
        public static string DownloadProxyUser = "";
        public static string DownloadProxyPass = "";
        public static int DownloadConcurrent = 4;            // 同时下几个文件
        public static int DownloadSegmentWorkers = 4;        // 单个大文件分几段并行（1 = 关闭分段）
        public static int DownloadRetry = 5;
        public static bool DownloadSound = true;             // 音效（Sound/）也按需下载
        public static int DownloadMaxConnections = 16;       // 全局并发连接硬上限

        //Game
        public static string AccountID = "",
                             Password = "";

        public static bool
            SkillMode = false,
            SkillBar = true,
            //SkillSet = true,
            Effect = true,
            LevelEffect = true,
            DropView = true,
            NameView = true,
            HPView = true,
            TransparentChat = false,
            ModeView = false,
            DuraView = false,
            DisplayDamage = true,
            TargetDead = false,
            HighlightTarget = true,
            ExpandedBuffWindow = true,
            ExpandedHeroBuffWindow = true,
            DisplayBodyName = false,
            NewMove = false;

        public static string Language = "English";

        public static int[,] SkillbarLocation = new int[2, 2] { { 0, 0 }, { 216, 0 } };

        //Quests
        public static int[] TrackedQuests = new int[5];

        //Chat
        public static bool
            ShowNormalChat = true,
            ShowYellChat = true,
            ShowWhisperChat = true,
            ShowLoverChat = true,
            ShowMentorChat = true,
            ShowGroupChat = true,
            ShowGuildChat = true;

        //Filters
        public static bool
            FilterNormalChat = false,
            FilterWhisperChat = false,
            FilterShoutChat = false,
            FilterSystemChat = false,
            FilterLoverChat = false,
            FilterMentorChat = false,
            FilterGroupChat = false,
            FilterGuildChat = false;


        //AutoPatcher
        public static bool P_Patcher = true;
        public static string P_Host = @"http://mirfiles.com/mir2/cmir/patch/";
        public static string P_PatchFileName = @"PList.gz";
        public static bool P_NeedLogin = false;
        public static string P_Login = string.Empty;
        public static string P_Password = string.Empty;
        public static string P_ServerName = string.Empty;
        public static string P_BrowserAddress = "https://www.lomcn.org/mir2-patchsite/";
        public static string P_Client = Application.StartupPath + "\\";
        public static bool P_AutoStart = false;
        public static int P_Concurrency = 1;

        public static void Load()
        {


            if (!Directory.Exists(DataPath)) Directory.CreateDirectory(DataPath);
            if (!Directory.Exists(MapPath)) Directory.CreateDirectory(MapPath);
            if (!Directory.Exists(SoundPath)) Directory.CreateDirectory(SoundPath);

            //Graphics
            FullScreen = Reader.ReadBoolean("Graphics", "FullScreen", FullScreen);
            Borderless = Reader.ReadBoolean("Graphics", "Borderless", Borderless);
            MouseClip = Reader.ReadBoolean("Graphics", "MouseClip", MouseClip);
            TopMost = Reader.ReadBoolean("Graphics", "AlwaysOnTop", TopMost);
            FPSCap = Reader.ReadBoolean("Graphics", "FPSCap", FPSCap);
            Resolution = Reader.ReadInt32("Graphics", "Resolution", Resolution);
            DebugMode = Reader.ReadBoolean("Graphics", "DebugMode", DebugMode);
            UseMouseCursors = Reader.ReadBoolean("Graphics", "UseMouseCursors", UseMouseCursors);

            //Network
            UseConfig = Reader.ReadBoolean("Network", "UseConfig", UseConfig);
            if (UseConfig)
            {
                IPAddress = Reader.ReadString("Network", "IPAddress", IPAddress);
                Port = Reader.ReadInt32("Network", "Port", Port);
            }

            //Logs
            LogErrors = Reader.ReadBoolean("Logs", "LogErrors", LogErrors);
            LogChat = Reader.ReadBoolean("Logs", "LogChat", LogChat);

            //Sound
            Volume = Reader.ReadByte("Sound", "Volume", Volume);
            SoundOverLap = Reader.ReadInt32("Sound", "SoundOverLap", SoundOverLap);
            MusicVolume = Reader.ReadByte("Sound", "Music", MusicVolume);
            var n = Reader.ReadInt32("Sound", "CleanMinutes", SoundCleanMinutes);
            if (n < 1 || n > 60 * 3) n = SoundCleanMinutes;
            SoundCleanMinutes = n;

            ReadAutoConfig(Reader);

            //Download
            DownloadEnabled = Reader.ReadBoolean("Download", "Enabled", DownloadEnabled);
            DownloadHost = Reader.ReadString("Download", "Host", DownloadHost);
            DownloadProxy = Reader.ReadString("Download", "Proxy", DownloadProxy);
            DownloadProxyUser = Reader.ReadString("Download", "ProxyUser", DownloadProxyUser);
            DownloadProxyPass = Reader.ReadString("Download", "ProxyPass", DownloadProxyPass);
            DownloadConcurrent = Reader.ReadInt32("Download", "MaxConcurrent", DownloadConcurrent);
            DownloadSegmentWorkers = Reader.ReadInt32("Download", "SegmentsPerFile", DownloadSegmentWorkers);
            DownloadRetry = Reader.ReadInt32("Download", "RetryCount", DownloadRetry);
            DownloadSound = Reader.ReadBoolean("Download", "Sound", DownloadSound);
            DownloadMaxConnections = Reader.ReadInt32("Download", "MaxConnections", DownloadMaxConnections);

            if (string.IsNullOrWhiteSpace(DownloadHost)) DownloadHost = "https://mirfiles.co.uk/resources/mir2/crystal/patch/";
            if (DownloadConcurrent < 1) DownloadConcurrent = 1;
            if (DownloadConcurrent > 16) DownloadConcurrent = 16;
            if (DownloadSegmentWorkers < 1) DownloadSegmentWorkers = 1;
            if (DownloadSegmentWorkers > 16) DownloadSegmentWorkers = 16;
            if (DownloadRetry < 1) DownloadRetry = 1;
            if (DownloadRetry > 20) DownloadRetry = 20;
            if (DownloadMaxConnections < 1) DownloadMaxConnections = 1;
            if (DownloadMaxConnections > 64) DownloadMaxConnections = 64;


            //Game
            AccountID = Reader.ReadString("Game", "AccountID", AccountID);
            Password = Reader.ReadString("Game", "Password", Password);

            SkillMode = Reader.ReadBoolean("Game", "SkillMode", SkillMode);
            SkillBar = Reader.ReadBoolean("Game", "SkillBar", SkillBar);
            //SkillSet = Reader.ReadBoolean("Game", "SkillSet", SkillSet);
            Effect = Reader.ReadBoolean("Game", "Effect", Effect);
            LevelEffect = Reader.ReadBoolean("Game", "LevelEffect", Effect);
            DropView = Reader.ReadBoolean("Game", "DropView", DropView);
            NameView = Reader.ReadBoolean("Game", "NameView", NameView);
            HPView = Reader.ReadBoolean("Game", "HPMPView", HPView);
            ModeView = Reader.ReadBoolean("Game", "ModeView", ModeView);
            FontName = Reader.ReadString("Game", "FontName", FontName);
            TransparentChat = Reader.ReadBoolean("Game", "TransparentChat", TransparentChat);
            DisplayDamage = Reader.ReadBoolean("Game", "DisplayDamage", DisplayDamage);
            TargetDead = Reader.ReadBoolean("Game", "TargetDead", TargetDead);
            HighlightTarget = Reader.ReadBoolean("Game", "HighlightTarget", HighlightTarget);
            ExpandedBuffWindow = Reader.ReadBoolean("Game", "ExpandedBuffWindow", ExpandedBuffWindow);
            ExpandedHeroBuffWindow = Reader.ReadBoolean("Game", "ExpandedHeroBuffWindow", ExpandedHeroBuffWindow);
            DuraView = Reader.ReadBoolean("Game", "DuraWindow", DuraView);
            DisplayBodyName = Reader.ReadBoolean("Game", "DisplayBodyName", DisplayBodyName);
            NewMove = Reader.ReadBoolean("Game", "NewMove", NewMove);
            Language = Reader.ReadString("Game", "Language", Language);

            for (int i = 0; i < SkillbarLocation.Length / 2; i++)
            {
                SkillbarLocation[i, 0] = Reader.ReadInt32("Game", "Skillbar" + i.ToString() + "X", SkillbarLocation[i, 0]);
                SkillbarLocation[i, 1] = Reader.ReadInt32("Game", "Skillbar" + i.ToString() + "Y", SkillbarLocation[i, 1]);
            }

            //Chat
            ShowNormalChat = Reader.ReadBoolean("Chat", "ShowNormalChat", ShowNormalChat);
            ShowYellChat = Reader.ReadBoolean("Chat", "ShowYellChat", ShowYellChat);
            ShowWhisperChat = Reader.ReadBoolean("Chat", "ShowWhisperChat", ShowWhisperChat);
            ShowLoverChat = Reader.ReadBoolean("Chat", "ShowLoverChat", ShowLoverChat);
            ShowMentorChat = Reader.ReadBoolean("Chat", "ShowMentorChat", ShowMentorChat);
            ShowGroupChat = Reader.ReadBoolean("Chat", "ShowGroupChat", ShowGroupChat);
            ShowGuildChat = Reader.ReadBoolean("Chat", "ShowGuildChat", ShowGuildChat);

            //Filters
            FilterNormalChat = Reader.ReadBoolean("Filter", "FilterNormalChat", FilterNormalChat);
            FilterWhisperChat = Reader.ReadBoolean("Filter", "FilterWhisperChat", FilterWhisperChat);
            FilterShoutChat = Reader.ReadBoolean("Filter", "FilterShoutChat", FilterShoutChat);
            FilterSystemChat = Reader.ReadBoolean("Filter", "FilterSystemChat", FilterSystemChat);
            FilterLoverChat = Reader.ReadBoolean("Filter", "FilterLoverChat", FilterLoverChat);
            FilterMentorChat = Reader.ReadBoolean("Filter", "FilterMentorChat", FilterMentorChat);
            FilterGroupChat = Reader.ReadBoolean("Filter", "FilterGroupChat", FilterGroupChat);
            FilterGuildChat = Reader.ReadBoolean("Filter", "FilterGuildChat", FilterGuildChat);

            //AutoPatcher
            P_Patcher = Reader.ReadBoolean("Launcher", "Enabled", P_Patcher);
            P_Host = Reader.ReadString("Launcher", "Host", P_Host);
            P_PatchFileName = Reader.ReadString("Launcher", "PatchFile", P_PatchFileName);
            P_NeedLogin = Reader.ReadBoolean("Launcher", "NeedLogin", P_NeedLogin);
            P_Login = Reader.ReadString("Launcher", "Login", P_Login);
            P_Password = Reader.ReadString("Launcher", "Password", P_Password);
            P_AutoStart = Reader.ReadBoolean("Launcher", "AutoStart", P_AutoStart);
            P_ServerName = Reader.ReadString("Launcher", "ServerName", P_ServerName);
            P_BrowserAddress = Reader.ReadString("Launcher", "Browser", P_BrowserAddress);
            P_Concurrency = Reader.ReadInt32("Launcher", "ConcurrentDownloads", P_Concurrency);


            if (!P_Host.EndsWith("/")) P_Host += "/";
            if (P_Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) P_Host = P_Host.Insert(0, "http://");
            if (P_BrowserAddress.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) P_BrowserAddress = P_BrowserAddress.Insert(0, "http://");

            //Temp check to update everyones address
            if (P_Host.ToLower() == "http://mirfiles.co.uk/mir2/cmir/patch/")
            {
                P_Host = "http://mirfiles.com/mir2/cmir/patch/";
            }

            if (P_Concurrency < 1) P_Concurrency = 1;
            if (P_Concurrency > 100) P_Concurrency = 100;

            try
            {
                string languageDirectory = @".\Localization\";
                if (!Directory.Exists(languageDirectory))
                {
                    Directory.CreateDirectory(languageDirectory);
                }
                string settingLanguageFile = Path.Combine(languageDirectory, Language + ".json");
                GameLanguage.LoadClientLanguage(settingLanguageFile);
            }
            catch (Exception ex)
            {
                CMain.SaveError($"Load Client Language Error:{ex.Message}");
            }
            
        }

        // ---- 不攻击怪物名单 ----
        private static HashSet<string> _autoFightIgnoreSet;
        private static string _autoFightIgnoreCached;

        private static HashSet<string> IgnoreSet()
        {
            if (_autoFightIgnoreSet != null && _autoFightIgnoreCached == AutoFightIgnoreMonsters)
                return _autoFightIgnoreSet;

            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(AutoFightIgnoreMonsters))
                foreach (string part in AutoFightIgnoreMonsters.Split(','))
                {
                    string name = part.Trim();
                    if (name.Length > 0) set.Add(name);
                }

            _autoFightIgnoreCached = AutoFightIgnoreMonsters;
            _autoFightIgnoreSet = set;
            return set;
        }

        // 名字前缀分隔符：数据库里常见 Frozen_deer / PastBichon\OmaValley\OmaGuard 这种写法
        private static readonly char[] MonsterNameSeparators = { '_', (char)92, '/' };

        /// <summary>
        /// 这个怪物名是否在不攻击名单里（不区分大小写）。
        /// 除了完全一致，还认「带前缀的强化版」：
        ///   名单里有 deer  →  非凡_deer / Frozen_deer / 某个目录\deer  都不打
        /// 判定方式是取名字里最后一个分隔符后面的那一段做匹配，
        /// 所以 deer 不会误伤 deer2 / ArcherGuard3 这类不同的怪。
        /// </summary>
        public static bool IsMonsterIgnored(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            string n = name.Trim();
            HashSet<string> set = IgnoreSet();
            if (set.Count == 0) return false;

            if (set.Contains(n)) return true;                       // 完全一致

            int cut = n.LastIndexOfAny(MonsterNameSeparators);
            if (cut >= 0 && cut + 1 < n.Length && set.Contains(n.Substring(cut + 1)))
                return true;                                        // 前缀变体

            return false;
        }

        public static int IgnoreMonsterCount => IgnoreSet().Count;

        public static List<string> GetIgnoreMonsters()
        {
            return new List<string>(IgnoreSet());
        }

        /// <summary>增/减一个名字（已存在就移除），并立即写盘</summary>
        public static void ToggleIgnoreMonster(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return;

            name = name.Trim();
            List<string> list = GetIgnoreMonsters();

            int index = list.FindIndex(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) list.RemoveAt(index);
            else if (list.Count < 64) list.Add(name);

            SetIgnoreMonsters(list);
        }

        public static void SetIgnoreMonsters(IEnumerable<string> names)
        {
            List<string> parts = new List<string>();
            if (names != null)
                foreach (string n in names)
                {
                    string name = (n ?? string.Empty).Trim();
                    if (name.Length > 0 && !parts.Contains(name, StringComparer.OrdinalIgnoreCase))
                        parts.Add(name);
                }

            AutoFightIgnoreMonsters = string.Join(",", parts);
            _autoFightIgnoreSet = null;
            SaveAutoFight();
        }

        public static void SaveAutoFight()
        {
            InIReader ini = AutoIni;
            ini.SuspendSave = true;
            try
            {
                ini.Write("AutoFight", "Enabled", AutoFightEnabled);
                ini.Write("AutoFight", "TargetRange", AutoFightTargetRange);
                ini.Write("AutoFight", "SeekRange", AutoFightSeekRange);
                ini.Write("AutoFight", "Roam", AutoFightRoam);
                ini.Write("AutoFight", "RoamRange", AutoFightRoamRange);
                ini.Write("AutoFight", "Priority", AutoFightPriority);
                ini.Write("AutoFight", "IgnorePlayers", AutoFightIgnorePlayers);
                ini.Write("AutoFight", "IgnoreMonsters", AutoFightIgnoreMonsters);
                ini.Write("AutoFight", "Chase", AutoFightChase);
                ini.Write("AutoFight", "AttackRange", AutoFightAttackRange);
                ini.Write("AutoFight", "AttackDelay", AutoFightAttackDelay);
                ini.Write("AutoFight", "BasicAttack", AutoFightBasicAttack);
                ini.Write("AutoFight", "SkillEnabled", AutoSkillEnabled);
                ini.Write("AutoFight", "SkillSpell", AutoSkillSpell);
                ini.Write("AutoFight", "Loot", AutoFightLoot);
                ini.Write("AutoFight", "LootRange", AutoFightLootRange);
                ini.Write("AutoFight", "LootSweep", AutoFightLootSweep);
                ini.Write("AutoFight", "LootGold", AutoFightLootGold);
                ini.Write("AutoFight", "LootPotion", AutoFightLootPotion);
                ini.Write("AutoFight", "LootBook", AutoFightLootBook);
                ini.Write("AutoFight", "LootWeapon", AutoFightLootWeapon);
                ini.Write("AutoFight", "LootArmour", AutoFightLootArmour);
                ini.Write("AutoFight", "LootJewel", AutoFightLootJewel);
                ini.Write("AutoFight", "LootOther", AutoFightLootOther);
                ini.Write("AutoFight", "LootTreasure", AutoFightLootTreasure);
                ini.Write("AutoFight", "LootFirst", AutoFightLootFirst);
            ini.Write("AutoFight", "PoisonGreen", AutoPoisonGreen);
            ini.Write("AutoFight", "PoisonRed", AutoPoisonRed);
        
            }
            finally
            {
                ini.SuspendSave = false;
                ini.Save();
            }
        }

        public static void SaveAutoPot()
        {
            InIReader ini = AutoIni;
            ini.SuspendSave = true;
            try
            {
                ini.Write("AutoPot", "Enabled", AutoPotEnabled);
                ini.Write("AutoPot", "HPPercent", AutoPotHPPercent);
                ini.Write("AutoPot", "MPPercent", AutoPotMPPercent);
                ini.Write("AutoPot", "HPItem", AutoPotHPItem);
                ini.Write("AutoPot", "MPItem", AutoPotMPItem);
                ini.Write("AutoPot", "Delay", AutoPotDelay);
            ini.Write("AutoPot", "HealEnabled", AutoHealEnabled);
            ini.Write("AutoPot", "HealPercent", AutoHealPercent);
        
            }
            finally
            {
                ini.SuspendSave = false;
                ini.Save();
            }
        }

        /// <summary>
        /// 读取内挂相关配置（自动喝药 / 自动战斗 / 治疗术 / 施毒）。
        /// 主配置 Load() 和「按角色加载档案」都走这一个函数，键名和夹取逻辑只有一份。
        /// </summary>
        /// <summary>
        /// 按角色加载内挂配置：.\AutoFight\{角色名}.ini
        /// 第一次用某个角色时，会把当前（主配置里的）内挂设置落一份，作为该角色的初始配置。
        /// </summary>
        public static void LoadAutoFightProfile(string characterName)
        {
            if (string.IsNullOrWhiteSpace(characterName)) return;

            string name = characterName.Trim();

            try
            {
                string dir = Path.Combine(".", "AutoFight");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 角色名里可能有 \ / : * ? " < > | 之类，替换掉
                foreach (char c in Path.GetInvalidFileNameChars())
                    name = name.Replace(c, '_');

                string file = Path.Combine(dir, name + ".ini");

                bool exists = File.Exists(file);

                ProfileReader = new InIReader(file);
                AutoFightProfile = characterName.Trim();

                if (exists)
                {
                    ReadAutoConfig(ProfileReader);
                }
                else
                {
                    // 新角色：把当前内存里的内挂配置写成它的初始档案。
                    // 但自动战斗开关强制关掉 —— 免得一进游戏就自动开打（per 角色独立开关）。
                    AutoFightEnabled = false;

                    SaveAutoFight();
                    SaveAutoPot();
                }
            }
            catch (Exception ex)
            {
                ProfileReader = null;
                AutoFightProfile = "";
                CMain.SaveError("LoadAutoFightProfile " + characterName + " : " + ex.Message);
            }
        }

        /// <summary>退出到选人/登录时清掉档案，避免串号</summary>
        public static void UnloadAutoFightProfile()
        {
            ProfileReader = null;
            AutoFightProfile = "";
        }

        private static void ReadAutoConfig(InIReader r)
        {
            AutoPotEnabled = r.ReadBoolean("AutoPot", "Enabled", AutoPotEnabled);
            AutoPotHPPercent = r.ReadInt32("AutoPot", "HPPercent", AutoPotHPPercent);
            AutoPotMPPercent = r.ReadInt32("AutoPot", "MPPercent", AutoPotMPPercent);
            AutoPotHPItem = r.ReadInt32("AutoPot", "HPItem", AutoPotHPItem);
            AutoPotMPItem = r.ReadInt32("AutoPot", "MPItem", AutoPotMPItem);
            AutoPotDelay = r.ReadInt32("AutoPot", "Delay", AutoPotDelay);
            AutoHealEnabled = r.ReadBoolean("AutoPot", "HealEnabled", AutoHealEnabled);
            AutoHealPercent = r.ReadInt32("AutoPot", "HealPercent", AutoHealPercent);
            AutoPoisonGreen = r.ReadBoolean("AutoFight", "PoisonGreen", AutoPoisonGreen);
            AutoPoisonRed = r.ReadBoolean("AutoFight", "PoisonRed", AutoPoisonRed);

            if (AutoPotHPPercent < 1) AutoPotHPPercent = 1;
            if (AutoPotHPPercent > 99) AutoPotHPPercent = 99;
            if (AutoPotMPPercent < 1) AutoPotMPPercent = 1;
            if (AutoPotMPPercent > 99) AutoPotMPPercent = 99;
            if (AutoPotDelay < 300) AutoPotDelay = 300;
            if (AutoPotDelay > 5000) AutoPotDelay = 5000;
            if (AutoHealPercent < 0) AutoHealPercent = 0;
            if (AutoHealPercent > 99) AutoHealPercent = 99;
            if (AutoPotHPItem < 0) AutoPotHPItem = 0;
            if (AutoPotMPItem < 0) AutoPotMPItem = 0;

            AutoFightEnabled = r.ReadBoolean("AutoFight", "Enabled", AutoFightEnabled);
            AutoFightTargetRange = r.ReadInt32("AutoFight", "TargetRange", AutoFightTargetRange);
            AutoFightSeekRange = r.ReadInt32("AutoFight", "SeekRange", AutoFightSeekRange);
            AutoFightRoam = r.ReadBoolean("AutoFight", "Roam", AutoFightRoam);
            AutoFightRoamRange = r.ReadInt32("AutoFight", "RoamRange", AutoFightRoamRange);
            AutoFightPriority = r.ReadInt32("AutoFight", "Priority", AutoFightPriority);
            AutoFightIgnorePlayers = r.ReadBoolean("AutoFight", "IgnorePlayers", AutoFightIgnorePlayers);
            AutoFightIgnoreMonsters = r.ReadString("AutoFight", "IgnoreMonsters", AutoFightIgnoreMonsters);
            AutoFightChase = r.ReadBoolean("AutoFight", "Chase", AutoFightChase);
            AutoFightAttackRange = r.ReadInt32("AutoFight", "AttackRange", AutoFightAttackRange);
            AutoFightAttackDelay = r.ReadInt32("AutoFight", "AttackDelay", AutoFightAttackDelay);
            AutoFightBasicAttack = r.ReadBoolean("AutoFight", "BasicAttack", AutoFightBasicAttack);
            AutoFightKeepDistance = r.ReadBoolean("AutoFight", "KeepDistance", AutoFightKeepDistance);
            AutoFightKeepRange = r.ReadInt32("AutoFight", "KeepRange", AutoFightKeepRange);
            AutoFightLineOfSight = r.ReadBoolean("AutoFight", "LineOfSight", AutoFightLineOfSight);
            AutoSkillEnabled = r.ReadBoolean("AutoFight", "SkillEnabled", AutoSkillEnabled);
            AutoSkillSpell = r.ReadInt32("AutoFight", "SkillSpell", AutoSkillSpell);
            AutoFightLoot = r.ReadBoolean("AutoFight", "Loot", AutoFightLoot);
            AutoFightLootRange = r.ReadInt32("AutoFight", "LootRange", AutoFightLootRange);
            AutoFightLootSweep = r.ReadInt32("AutoFight", "LootSweep", AutoFightLootSweep);
            AutoFightLootGold = r.ReadBoolean("AutoFight", "LootGold", AutoFightLootGold);
            AutoFightLootPotion = r.ReadBoolean("AutoFight", "LootPotion", AutoFightLootPotion);
            AutoFightLootBook = r.ReadBoolean("AutoFight", "LootBook", AutoFightLootBook);
            AutoFightLootWeapon = r.ReadBoolean("AutoFight", "LootWeapon", AutoFightLootWeapon);
            AutoFightLootArmour = r.ReadBoolean("AutoFight", "LootArmour", AutoFightLootArmour);
            AutoFightLootJewel = r.ReadBoolean("AutoFight", "LootJewel", AutoFightLootJewel);
            AutoFightLootOther = r.ReadBoolean("AutoFight", "LootOther", AutoFightLootOther);
            AutoFightLootTreasure = r.ReadBoolean("AutoFight", "LootTreasure", AutoFightLootTreasure);
            AutoFightLootFirst = r.ReadBoolean("AutoFight", "LootFirst", AutoFightLootFirst);

            if (AutoFightTargetRange < 1) AutoFightTargetRange = 1;
            if (AutoFightTargetRange > 30) AutoFightTargetRange = 30;
            if (AutoFightSeekRange < 1) AutoFightSeekRange = 1;
            if (AutoFightSeekRange > 40) AutoFightSeekRange = 40;
            if (AutoFightSeekRange < AutoFightTargetRange) AutoFightSeekRange = AutoFightTargetRange;
            if (AutoFightRoamRange < 1) AutoFightRoamRange = 1;
            if (AutoFightRoamRange > 20) AutoFightRoamRange = 20;
            if (AutoFightPriority < 0) AutoFightPriority = 0;
            if (AutoFightPriority > 1) AutoFightPriority = 1;
            if (AutoFightAttackRange < 1) AutoFightAttackRange = 1;
            if (AutoFightAttackRange > 12) AutoFightAttackRange = 12;
            if (AutoFightKeepRange < 1) AutoFightKeepRange = 1;
            if (AutoFightKeepRange > 12) AutoFightKeepRange = 12;
            if (AutoFightAttackDelay < 0) AutoFightAttackDelay = 0;
            if (AutoFightAttackDelay > 3000) AutoFightAttackDelay = 3000;
            if (AutoFightLootRange < 1) AutoFightLootRange = 1;
            if (AutoFightLootRange > 20) AutoFightLootRange = 20;
            if (AutoFightLootSweep < 0) AutoFightLootSweep = 0;
            if (AutoFightLootSweep > 30) AutoFightLootSweep = 30;

            if (AutoSkillSpell < 0) AutoSkillSpell = 0;

        }

        public static void Save()
        {
            Reader.SuspendSave = true;
            try
            {
                //Graphics
                Reader.Write("Graphics", "FullScreen", FullScreen);
                Reader.Write("Graphics", "Borderless", Borderless);
                Reader.Write("Graphics", "MouseClip", MouseClip);
                Reader.Write("Graphics", "AlwaysOnTop", TopMost);
                Reader.Write("Graphics", "FPSCap", FPSCap);
                Reader.Write("Graphics", "Resolution", Resolution);
                Reader.Write("Graphics", "DebugMode", DebugMode);
                Reader.Write("Graphics", "UseMouseCursors", UseMouseCursors);

                //Sound
                Reader.Write("Sound", "Volume", Volume);
                Reader.Write("Sound", "SoundOverLap", SoundOverLap);
                Reader.Write("Sound", "Music", MusicVolume);
                Reader.Write("Sound", "CleanMinutes", SoundCleanMinutes);

                //AutoPot
                Reader.Write("AutoPot", "Enabled", AutoPotEnabled);
                Reader.Write("AutoPot", "HPPercent", AutoPotHPPercent);
                Reader.Write("AutoPot", "MPPercent", AutoPotMPPercent);
                Reader.Write("AutoPot", "HPItem", AutoPotHPItem);
                Reader.Write("AutoPot", "MPItem", AutoPotMPItem);
                Reader.Write("AutoPot", "Delay", AutoPotDelay);
            Reader.Write("AutoPot", "HealEnabled", AutoHealEnabled);
            Reader.Write("AutoPot", "HealPercent", AutoHealPercent);

                //AutoFight
                Reader.Write("AutoFight", "Enabled", AutoFightEnabled);
                Reader.Write("AutoFight", "TargetRange", AutoFightTargetRange);
                Reader.Write("AutoFight", "SeekRange", AutoFightSeekRange);
                Reader.Write("AutoFight", "Roam", AutoFightRoam);
                Reader.Write("AutoFight", "RoamRange", AutoFightRoamRange);
                Reader.Write("AutoFight", "Priority", AutoFightPriority);
                Reader.Write("AutoFight", "IgnorePlayers", AutoFightIgnorePlayers);
                Reader.Write("AutoFight", "IgnoreMonsters", AutoFightIgnoreMonsters);
                Reader.Write("AutoFight", "Chase", AutoFightChase);
                Reader.Write("AutoFight", "AttackRange", AutoFightAttackRange);
                Reader.Write("AutoFight", "AttackDelay", AutoFightAttackDelay);
            Reader.Write("AutoFight", "BasicAttack", AutoFightBasicAttack);
            Reader.Write("AutoFight", "KeepDistance", AutoFightKeepDistance);
            Reader.Write("AutoFight", "KeepRange", AutoFightKeepRange);
            Reader.Write("AutoFight", "LineOfSight", AutoFightLineOfSight);
            Reader.Write("AutoFight", "SkillEnabled", AutoSkillEnabled);
            Reader.Write("AutoFight", "SkillSpell", AutoSkillSpell);
                Reader.Write("AutoFight", "Loot", AutoFightLoot);
                Reader.Write("AutoFight", "LootRange", AutoFightLootRange);
                Reader.Write("AutoFight", "LootSweep", AutoFightLootSweep);
                Reader.Write("AutoFight", "LootGold", AutoFightLootGold);
                Reader.Write("AutoFight", "LootPotion", AutoFightLootPotion);
                Reader.Write("AutoFight", "LootBook", AutoFightLootBook);
                Reader.Write("AutoFight", "LootWeapon", AutoFightLootWeapon);
                Reader.Write("AutoFight", "LootArmour", AutoFightLootArmour);
                Reader.Write("AutoFight", "LootJewel", AutoFightLootJewel);
                Reader.Write("AutoFight", "LootOther", AutoFightLootOther);
                Reader.Write("AutoFight", "LootTreasure", AutoFightLootTreasure);
                Reader.Write("AutoFight", "LootFirst", AutoFightLootFirst);
            Reader.Write("AutoFight", "PoisonGreen", AutoPoisonGreen);
            Reader.Write("AutoFight", "PoisonRed", AutoPoisonRed);

                //Download
                Reader.Write("Download", "Enabled", DownloadEnabled);
                Reader.Write("Download", "Host", DownloadHost);
                Reader.Write("Download", "Proxy", DownloadProxy);
                Reader.Write("Download", "ProxyUser", DownloadProxyUser);
                Reader.Write("Download", "ProxyPass", DownloadProxyPass);
                Reader.Write("Download", "MaxConcurrent", DownloadConcurrent);
                Reader.Write("Download", "SegmentsPerFile", DownloadSegmentWorkers);
                Reader.Write("Download", "RetryCount", DownloadRetry);
                Reader.Write("Download", "Sound", DownloadSound);
                Reader.Write("Download", "MaxConnections", DownloadMaxConnections);

                //Game
                Reader.Write("Game", "AccountID", AccountID);
                Reader.Write("Game", "Password", Password);
                Reader.Write("Game", "SkillMode", SkillMode);
                Reader.Write("Game", "SkillBar", SkillBar);
                //Reader.Write("Game", "SkillSet", SkillSet);
                Reader.Write("Game", "Effect", Effect);
                Reader.Write("Game", "LevelEffect", LevelEffect);
                Reader.Write("Game", "DropView", DropView);
                Reader.Write("Game", "NameView", NameView);
                Reader.Write("Game", "HPMPView", HPView);
                Reader.Write("Game", "ModeView", ModeView);
                Reader.Write("Game", "FontName", FontName);
                Reader.Write("Game", "TransparentChat", TransparentChat);
                Reader.Write("Game", "DisplayDamage", DisplayDamage);
                Reader.Write("Game", "TargetDead", TargetDead);
                Reader.Write("Game", "HighlightTarget", HighlightTarget);
                Reader.Write("Game", "ExpandedBuffWindow", ExpandedBuffWindow);
                Reader.Write("Game", "ExpandedHeroBuffWindow", ExpandedBuffWindow);
                Reader.Write("Game", "DuraWindow", DuraView);
                Reader.Write("Game", "DisplayBodyName", DisplayBodyName);
                Reader.Write("Game", "NewMove", NewMove);
                Reader.Write("Game", "Language", Language);

                for (int i = 0; i < SkillbarLocation.Length / 2; i++)
                {

                    Reader.Write("Game", "Skillbar" + i.ToString() + "X", SkillbarLocation[i, 0]);
                    Reader.Write("Game", "Skillbar" + i.ToString() + "Y", SkillbarLocation[i, 1]);
                }

                //Chat
                Reader.Write("Chat", "ShowNormalChat", ShowNormalChat);
                Reader.Write("Chat", "ShowYellChat", ShowYellChat);
                Reader.Write("Chat", "ShowWhisperChat", ShowWhisperChat);
                Reader.Write("Chat", "ShowLoverChat", ShowLoverChat);
                Reader.Write("Chat", "ShowMentorChat", ShowMentorChat);
                Reader.Write("Chat", "ShowGroupChat", ShowGroupChat);
                Reader.Write("Chat", "ShowGuildChat", ShowGuildChat);

                //Filters
                Reader.Write("Filter", "FilterNormalChat", FilterNormalChat);
                Reader.Write("Filter", "FilterWhisperChat", FilterWhisperChat);
                Reader.Write("Filter", "FilterShoutChat", FilterShoutChat);
                Reader.Write("Filter", "FilterSystemChat", FilterSystemChat);
                Reader.Write("Filter", "FilterLoverChat", FilterLoverChat);
                Reader.Write("Filter", "FilterMentorChat", FilterMentorChat);
                Reader.Write("Filter", "FilterGroupChat", FilterGroupChat);
                Reader.Write("Filter", "FilterGuildChat", FilterGuildChat);

                //AutoPatcher
                Reader.Write("Launcher", "Enabled", P_Patcher);
                Reader.Write("Launcher", "Host", P_Host);
                Reader.Write("Launcher", "PatchFile", P_PatchFileName);
                Reader.Write("Launcher", "NeedLogin", P_NeedLogin);
                Reader.Write("Launcher", "Login", P_Login);
                Reader.Write("Launcher", "Password", P_Password);
                Reader.Write("Launcher", "ServerName", P_ServerName);
                Reader.Write("Launcher", "Browser", P_BrowserAddress);
                Reader.Write("Launcher", "AutoStart", P_AutoStart);
                Reader.Write("Launcher", "ConcurrentDownloads", P_Concurrency);
        
            }
            finally
            {
                Reader.SuspendSave = false;
                Reader.Save();
            }
        }

        public static void LoadTrackedQuests(string charName)
        {
            //Quests
            for (int i = 0; i < TrackedQuests.Length; i++)
            {
                TrackedQuests[i] = QuestTrackingReader.ReadInt32(charName, "Quest-" + i.ToString(), -1);
            }
        }

        public static void SaveTrackedQuests(string charName)
        {
            //Quests
            for (int i = 0; i < TrackedQuests.Length; i++)
            {
                QuestTrackingReader.Write(charName, "Quest-" + i.ToString(), TrackedQuests[i]);
            }
        }
    }


}
