using Client.MirNetwork;
using Client.MirObjects;
using Client.MirScenes;
using C = ClientPackets;

namespace Client
{
    /// <summary>
    /// 客户端内挂：自动战斗
    ///
    /// 全部通过客户端**原有的操作通道**驱动，服务端看到的和真人操作完全一样：
    ///   · 走位   → 复用 MapControl.AutoPath + PathFinder 寻路（和"右键点地面自动走"同一套）
    ///   · 攻击   → 设置 User.QueuedAction = Attack1 / AttackRange1（和鼠标点击攻击同一套）
    ///   · 拾取   → 站在物品格上发 C.PickUp（和按 Tab 拾取同一套）
    ///
    /// 只发普通的 C.Attack / C.PickUp，不新增任何协议。
    /// </summary>
    internal static class AutoFight
    {
        // ---------- 给界面看的状态 ----------
        public static string Status = string.Empty;
        public static uint TargetID;

        // ---------- 内部 ----------
        private static MapObject _target;
        private static long _nextTick, _nextAttackTime, _nextPathTime;
        private static Point _pathDest = Point.Empty;

        // 卡住检测
        private static Point _lastProgressPos = Point.Empty;
        private static long _lastProgressTime;

        // 暂时拉黑的目标（走不到 / 打不动就换一个）
        private static uint _avoidId;
        private static long _avoidUntil;

        // 拾取：捡不到的物品各自冷却（字典，避免 A 被覆盖后提前解禁）
        private static uint _lootId;
        private static readonly Dictionary<uint, long> _lootAvoid = new Dictionary<uint, long>();

        // 漫游寻敌
        private static Point _anchor = Point.Empty;      // 巡逻锚点
        private static long _nextRoamTime;
        private static readonly Random _rand = new Random();

        // 方向性游走：原来的"锚点周围随机挑点"会在同一片地来回抖，
        // 清完身边怪之后就原地打转。改成记住一个方向、大部分时间沿着它继续走。
        private static MirDirection _roamDir;
        private static bool _hasRoamDir;
        private static long _idleSince;                  // 从"没有目标"开始计时
        private static long _outOfRangeSince;            // 不追击时，从"够不着"开始计时
        private static int _roamFailStreak;              // 连续挑不到落点/走不过去的次数
        private static readonly Point[] _recentRoam = new Point[6];
        private static int _recentRoamIndex;

        // 打不动检测：连续攻击但对方血量一次都没变
        private static uint _streakTargetId;
        private static int _streakHp = -1;
        private static int _streak;

        private const int TickInterval = 100;      // 逻辑节流，10Hz 足够
        private const int RepathInterval = 700;    // 追击时重新寻路的间隔
        private const int PathBudget = 4000;       // 单次 A* 的节点预算，保证每次开销有上界
        private const int StuckTimeout = 3000;     // 追击中这么久没挪窝 → 判定走不到
        private const int AvoidDuration = 5000;    // 拉黑时长（走不到）
        private const int NoDamageAvoid = 30000;   // 拉黑时长（打不动，多半是免疫怪）
        private const int NoDamageAttacks = 15;    // 连续这么多次攻击血量没变化 → 判定打不动
        private const int LootAvoidDuration = 8000; // 捡不到的物品跳过多长时间
        private const int CounterWindow = 4000;     // "最近打过我"的时间窗（优先反击）
        private const int PoisonRetryDelay = 2500;  // 施毒后多久再检查一次（毒生效有延迟，别连着浪费毒药）
        private const int PoisonEquipDelay = 1500;  // 换毒药的冷却（等服务器回包，别连发装备包）

        private static long _nextPoisonTime;
        private static long _nextPoisonEquipTime;

        private static string _loggedStatus;
        private static Point _lastPos = Point.Empty;
        private static long _buttonsDownSince;

        private const int RepositionInterval = 600;      // 调整站位的扫描间隔(ms)
        private const int RepositionHoldMs = 1200;       // 选定落点后沿用多久(ms)

        private static long _nextReposition;
        private static long _repositionUntil;
        private static Point _repositionDest = Point.Empty;
        private static bool _hasLastPos;

        public static MapObject Target => _target;

        /// <summary>只写日志文件，不动状态栏</summary>
        private static void Log(string line)
        {
            try
            {
                string dir = Path.Combine(".", "Logs");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.AppendAllText(Path.Combine(dir, "AutoFight.txt"),
                    DateTime.Now.ToString("HH:mm:ss") + "  " + line + Environment.NewLine);
            }
            catch
            {
                // 日志失败无所谓
            }
        }

        /// <summary>更新状态并把变化写进 Logs\AutoFight.txt（排查用）</summary>
        private static void SetStatus(string status)
        {
            Status = status;

            if (status == _loggedStatus) return;
            _loggedStatus = status;

            Log(status);
        }

        /// <summary>鼠标按键卡住超过 2.5 秒就强制清掉（内挂开启时）</summary>
        private static void WatchStuckMouseButtons(GameScene scene, long now)
        {
            if (MapControl.MapButtons == MouseButtons.None)
            {
                _buttonsDownSince = 0;
                return;
            }

            if (_buttonsDownSince == 0)
            {
                _buttonsDownSince = now;
                return;
            }

            if (now - _buttonsDownSince < 2500) return;

            SetStatus(string.Format("[鼠标按键残留已清除] btn={0} 持续 {1}ms",
                MapControl.MapButtons, now - _buttonsDownSince));

            MapControl.MapButtons = MouseButtons.None;
            MapControl.AutoRun = false;

            if (scene.MapControl != null && scene.MapControl.AutoPath)
                scene.MapControl.AutoPath = false;

            _buttonsDownSince = 0;
        }

        /// <summary>停止自动战斗：清目标 / 停寻路</summary>
        public static void Stop(string status = null)
        {
            _target = null;
            TargetID = 0;
            Status = status ?? string.Empty;

            // 漫游状态也清掉：否则下次开启时会带着"已经空闲很久"的计时直接大步乱跑
            _idleSince = 0;
            _roamFailStreak = 0;
            _hasRoamDir = false;
            Array.Clear(_recentRoam, 0, _recentRoam.Length);
            _recentRoamIndex = 0;

            MapControl map = GameScene.Scene == null ? null : GameScene.Scene.MapControl;
            if (map != null && map.AutoPath)
                map.AutoPath = false;
        }

        // ================= 主循环 =================

        /// <summary>
        /// 内挂配置按角色隔离：检测到当前角色变了就加载对应的配置档案
        /// （.\AutoFight\{角色名}.ini）。
        /// </summary>
        public static void EnsureProfile()
        {
            UserObject user = GameScene.User;
            if (user == null) return;

            string name = user.Name;
            if (string.IsNullOrEmpty(name)) return;
            if (Settings.AutoFightProfile == name) return;

            Stop();                    // 上一个角色的跑图状态清掉
            AutoPotion.Reset();

            Settings.LoadAutoFightProfile(name);
        }

        public static void Process()
        {
            EnsureProfile();

            if (!Settings.AutoFightEnabled)
            {
                _anchor = Point.Empty;
                if (Status.Length > 0) Stop();
                return;
            }

            GameScene scene = GameScene.Scene;
            UserObject user = MapObject.User;

            if (scene == null || scene.MapControl == null || user == null)
            {
                Stop();
                return;
            }

            if (user.Dead || GameScene.Observing)
            {
                Stop("状态：已停止");
                return;
            }

            // 正在跟 NPC/交易 交互时不要乱跑
            if (scene.NPCDialog.Visible || scene.TradeDialog.Visible || scene.TrustMerchantDialog.Visible)
            {
                Stop("状态：交互中暂停");
                return;
            }

            long now = CMain.Time;
            if (now < _nextTick) return;
            _nextTick = now + TickInterval;

            // ---------- 鼠标按键残留看门狗 ----------
            // 客户端靠 MapControl.MapButtons 判断"左键按住 → 朝鼠标方向走/攻击"。
            // 如果鼠标在窗口外松开，WinForms 收不到 MouseUp，MapButtons 会永久卡在 Left，
            // 于是人物会自己一直朝最后记录的鼠标位置走 —— 看起来就像内挂自己走去贴怪。
            WatchStuckMouseButtons(scene, now);

            // ---------- 1) 目标有效性 ----------
            if (_target != null && !IsValidTarget(_target))
                ClearTarget();

            // 追太远就放弃（避免被一只怪拖着满地图跑）
            int leash = Math.Max(Settings.AutoFightTargetRange, Settings.AutoFightSeekRange) + 3;
            if (_target != null && Dist(user.CurrentLocation, _target.CurrentLocation) > leash)
                ClearTarget();

            // ---------- 1b) 优先反击 ----------
            // 场景：当前目标是个"不还手"的怪（比如稻草人/中立怪），旁边却有别的怪在打我。
            // 这时应该立刻换去打我的那只，而不是继续对着不还手的目标输出。
            // 条件：当前目标没在打我 且 附近确实有怪在打我。
            // 没有目标时不用额外扫一遍（FindTarget 自带"优先打正在打我的"排序）
            if (_target != null && !IsAttackingUser(_target, now))
            {
                MapObject attacker = FindRecentAttacker(user, now);

                if (attacker != null && attacker != _target)
                {
                    _target = attacker;
                    TouchProgress(user, now);
                    _anchor = user.CurrentLocation;
                }
            }

            // 不追击时，把"能锁定的距离"压到实际打得到的范围：
            // 否则会锁定一只永远够不着的怪，站在原地盯着它看 —— 表现为"空转"。
            int seekLimit = int.MaxValue;

            if (!Settings.AutoFightChase)
                seekLimit = Math.Max(1, Math.Max(Settings.AutoFightAttackRange, SkillRange(user)));

            // ---------- 2) 近处寻敌（只找"身边"的怪） ----------
            // 关键：这里只用 TargetRange，不扩大到 SeekRange。
            // 之前把远近两次搜索放在一起，16 格内总能搜到怪 → _target 几乎永远非空 →
            // 「先捡后打 = 否」要求 _target == null 才捡，结果就永远不捡了。
            // 现在把"远处寻敌"挪到拾取之后，中间这段空隙就是用来捡东西的。
            if (_target == null && FindNearTarget(user, now, out MapObject near, Math.Min(Settings.AutoFightTargetRange, seekLimit)))
                AcquireTarget(near, user, now);

            // ---------- 3) 拾取 ----------
            //   先捡后打 = 是 → 有东西就捡（哪怕身边有怪）
            //   先捡后打 = 否 → 只有身边没怪时才捡
            bool lootAllowed = Settings.AutoFightLoot
                               && (Settings.AutoFightLootFirst || _target == null);

            // 背包没空格子时一件都别捡：
            // 发包会被服务端默默拒绝，人却会一直站在物品上反复发（表现为"卡住不动"）。
            bool bagFull = user.FreeInventorySlots <= 0;

            if (lootAllowed && !bagFull)
            {
                // 身边清干净了 → 放大到回收范围，把之前漏掉的捡回来
                int lootRange = Settings.AutoFightLootRange;

                if (_target == null && Settings.AutoFightLootSweep > lootRange)
                    lootRange = Settings.AutoFightLootSweep;

                MapObject loot = FindLoot(lootRange);

                if (loot != null)
                {
                    DoLoot(scene, user, loot, now);
                    return;
                }
            }

            // ---------- 4) 远处寻敌（身边和掉落都处理完了，再去看远处的怪） ----------
            int seekRange = Math.Min(Settings.AutoFightSeekRange, seekLimit);

            if (_target == null
                && seekRange > Settings.AutoFightTargetRange
                && FindNearTarget(user, now, out MapObject far, seekRange))
            {
                AcquireTarget(far, user, now);
            }

            if (_target == null)
            {
                // ---------- 4b) 漫游寻敌 ----------
                if (Settings.AutoFightRoam)
                {
                    DoRoam(scene, user, now);
                    return;
                }

                Stop(bagFull ? "状态：背包已满，停止拾取" : "状态：附近没有目标");
                return;
            }

            if (MapObject.TargetObjectID != _target.ObjectID)     // 避免每 tick 重复赋值
                MapObject.TargetObjectID = _target.ObjectID;

            TargetID = _target.ObjectID;

            int wantRange = Math.Max(1, Settings.AutoFightAttackRange);
            int skillRange = SkillRange(user);

            // 交战距离：技能射程和近战距离取大者。
            // 之前只用 wantRange(默认 1)，法师就算技能有 9 格射程也会先贴到怪脸上，
            // 表现就是"一路小跑过去还顺手砍"。
            int engageRange = Math.Max(wantRange, skillRange);

            int dist = Dist(user.CurrentLocation, _target.CurrentLocation);

            // 直线视野：中间隔着墙的时候远程技能打不中（弹道照样飞，伤害落空），
            // 这时候站着放就是纯空放，得换个能直线打到的位置。
            bool los = HasLineOfSight(scene.MapControl, user.CurrentLocation, _target.CurrentLocation);

            // 保持距离的圈：不能超过交战距离，否则永远打不到
            int keepRange = Math.Min(Math.Max(1, Settings.AutoFightKeepRange), engageRange);

            // ---------- 3.5) 施毒（道士） ----------
            // 目标身上没有对应毒就补一发。优先级高于普通攻击：
            // 毒会持续掉血/降防，先上毒收益更大。
            if (los && TryPoison(user, _target, now, engageRange))
            {
                TouchProgress(user, now);
                return;
            }

            // ---------- 4) 追击 ----------
            if (dist > engageRange)
            {
                if (!Settings.AutoFightChase)
                {
                    if (scene.MapControl.AutoPath)
                        scene.MapControl.AutoPath = false;

                    // 先留着目标等它自己靠近（Stop() 会清空目标，导致每 tick 重新搜索、状态乱跳）；
                    // 但一直够不着就别死盯着了 —— 3 秒后放弃，回去正常寻敌/漫游。
                    if (_outOfRangeSince == 0)
                        _outOfRangeSince = now;

                    if (now - _outOfRangeSince > 3000)
                    {
                        _outOfRangeSince = 0;

                        MapObject.TargetObjectID = 0;
                        ClearTarget();

                        SetStatus("状态：超出攻击距离，放弃");
                        return;
                    }

                    SetStatus(string.Format("状态：超出攻击距离 d{0}/r{1}（不追击）", dist, engageRange));
                    return;
                }

                SetStatus(string.Format("状态：追击 {0}  d{1}/r{2} w{3} s{4}  me({5},{6}) tg({7},{8})",
                    _target.Name, dist, engageRange, wantRange, skillRange,
                    user.CurrentLocation.X, user.CurrentLocation.Y,
                    _target.CurrentLocation.X, _target.CurrentLocation.Y));

                // 卡住检测：追击中 3 秒没挪窝 → 这个目标根本走不到，换一个
                if (IsStuck(user, now))
                {
                    GiveUp(scene, now, "状态：走不到，换目标");
                    return;
                }

                Point dest = ResolveChaseDest(scene.MapControl, _target, user.CurrentLocation);

                if (dest == Point.Empty)
                {
                    GiveUp(scene, now, "状态：被挡住，换目标");
                    return;
                }

                MoveTo(dest);
                return;
            }

            // ---------- 5) 攻击 ----------
            _outOfRangeSince = 0;      // 已经进射程了

            // 调整站位（风筝/绕障碍）刚下过路径时不要立刻清掉，否则走一格就被自己停住
            if (scene.MapControl.AutoPath && now >= _repositionUntil)
                scene.MapControl.AutoPath = false;

            TouchProgress(user, now);      // 站着攻击不算卡住

            // 已经进入输出状态（距离 <= 交战距离），理论上不该再移动。
            // 如果位置还在变，就说明是**客户端自己的输入状态**在带着人走
            // （鼠标左键/MouseButtons 残留、自动跑 AutoRun 没关），
            // 这段会记录下来并把它们清掉，免得看起来像"内挂自己走去贴脸"。
            if (_hasLastPos && user.CurrentLocation != _lastPos)
            {
                Log(string.Format("[移动] ({0},{1}) → ({2},{3})  d{4}/r{5}  act={6} btn={7} autorun={8} path={9}",
                    _lastPos.X, _lastPos.Y, user.CurrentLocation.X, user.CurrentLocation.Y,
                    dist, engageRange, user.CurrentAction, MapControl.MapButtons,
                    MapControl.AutoRun, scene.MapControl.AutoPath));
            }

            if (!_hasLastPos)
            {
                _hasLastPos = true;
                _lastPos = user.CurrentLocation;
            }
            else if (user.CurrentLocation != _lastPos)
            {
                bool external = !scene.MapControl.AutoPath;

                if (external && (MapControl.AutoRun || MapControl.MapButtons != MouseButtons.None))
                {
                    SetStatus(string.Format("[外部移动已抑制] btn={0} autorun={1} act={2} d{3}/r{4}",
                        MapControl.MapButtons, MapControl.AutoRun, user.CurrentAction, dist, engageRange));

                    MapControl.AutoRun = false;
                    MapControl.MapButtons = MouseButtons.None;
                }

                _lastPos = user.CurrentLocation;
            }

            // 法师：先尝试用选定技能打（放得出来就不普攻）
            // 没视野就别放，放了也是空放
            if (los && TryCastSkill(user, _target, now, engageRange)) return;

            // ---------- 5.5) 风筝 / 绕障碍 ----------
            //   a) 开了「保持距离」且怪已经贴到 keepRange 以内（技能又还在冷却）→ 后撤
            //   b) 中间隔着墙，技能打不中 → 挪到能直线命中的格子
            bool tooClose = Settings.AutoFightKeepDistance && dist < keepRange;

            if (tooClose || !los)
            {
                int minR = Settings.AutoFightKeepDistance ? keepRange : 1;
                int maxR = tooClose ? Math.Min(keepRange + 2, engageRange) : engageRange;

                if (TryReposition(scene, user, _target, now, minR, maxR))
                {
                    SetStatus(string.Format("状态：{0} d{1}/r{2} 保持{3}",
                        tooClose ? "拉开距离" : "绕开障碍", dist, engageRange, keepRange));

                    TouchProgress(user, now);      // 主动走位不算卡住
                    return;
                }
            }

            // 隔墙：附近没有能直线命中的格子 → 主动绕过去（A* 会沿墙走）
            if (!los)
            {
                if (Settings.AutoFightChase && dist > 1)
                {
                    if (IsStuck(user, now))
                    {
                        GiveUp(scene, now, "状态：障碍挡路，换目标");
                        return;
                    }

                    Point dest = ResolveChaseDest(scene.MapControl, _target, user.CurrentLocation);

                    if (dest != Point.Empty)
                    {
                        SetStatus(string.Format("状态：绕过障碍 {0}  d{1}/r{2}", _target.Name, dist, engageRange));

                        // 记下"正在走位"，否则下一 tick 进攻击段会把刚下的路径清掉（走一格就停）
                        _repositionDest = dest;
                        _repositionUntil = now + RepositionHoldMs;

                        MoveTo(dest);
                        return;
                    }
                }

                SetStatus("状态：被障碍物挡住 " + _target.Name);
                return;
            }

            // 站在技能射程内、但够不着近战 → 等技能冷却好，别贴上去空挥
            // （这条要在普攻开关之前判断，否则状态栏会误报"普攻已关闭"）
            if (dist > wantRange)
            {
                SetStatus("状态：等待技能 " + _target.Name);
                return;
            }

            // 普通攻击开关
            if (!Settings.AutoFightBasicAttack)
            {
                Status = "状态：普攻已关闭";
                return;
            }

            Status = "状态：攻击 " + _target.Name;

            if (now < GameScene.AttackTime) return;
            if (now < _nextAttackTime) return;

            if (CheckNoDamage(_target, now)) return;

            Attack(user, _target, now);
        }

        private static bool IsLootAvoided(uint objectId, long now)
        {
            long until;
            if (!_lootAvoid.TryGetValue(objectId, out until)) return false;

            if (now < until) return true;

            _lootAvoid.Remove(objectId);
            return false;
        }

        private static void AvoidLoot(uint objectId, long now)
        {
            if (_lootAvoid.Count > 64) _lootAvoid.Clear();      // 不会无限增长
            _lootAvoid[objectId] = now + LootAvoidDuration;
        }

        /// <summary>在指定范围内找目标（等价于原来的 FindTarget，只是包一层方便 out 值）</summary>
        private static bool FindNearTarget(UserObject user, long now, out MapObject target, int range)
        {
            target = FindTarget(now, range);
            return target != null;
        }

        private static bool FindNearTarget(UserObject user, long now, out MapObject target)
        {
            target = FindTarget(now, Settings.AutoFightTargetRange);
            return target != null;
        }

        /// <summary>拿到新目标时刷新卡住计时 / 巡逻锚点</summary>
        private static void AcquireTarget(MapObject target, UserObject user, long now)
        {
            if (target == null) return;

            _target = target;
            _lastProgressPos = user.CurrentLocation;
            _lastProgressTime = now;
            _anchor = user.CurrentLocation;      // 巡逻锚点跟着战斗位置走

            // 重新接敌：清空"多久没怪"的计时，漫游步长回到正常
            _idleSince = 0;
            _roamFailStreak = 0;
            _outOfRangeSince = 0;
        }

        /// <summary>
        /// 道士自动施毒：目标身上没有对应毒就补一发 Spell.Poisoning。
        ///
        /// 毒的类型由**装备在 Amulet 槽的那件毒药**决定（服务端 GetPoison 只扫装备槽）：
        ///   Info.Type == ItemType.Amulet 且 Shape == 1 → 绿毒
        ///                                    Shape == 2 → 红毒
        ///                                    Shape == 0 → 护身符（不能施毒）
        ///
        /// 所以流程是：
        ///   1) 看目标缺哪种毒（按勾选项）
        ///   2) 当前装备的毒正好能补 → 直接施法
        ///   3) 装备的毒不对 → 去**背包**里找对应 Shape 的毒药，找到就换装（下一次 tick 再施法）
        ///   4) 背包里也没有 → 这次不施法
        /// </summary>
        private static bool TryPoison(UserObject user, MapObject target, long now, int attackRange)
        {
            if (user == null || target == null || target.Dead) return false;
            if (user.Class != MirClass.Taoist) return false;

            bool wantGreen = Settings.AutoPoisonGreen;
            bool wantRed = Settings.AutoPoisonRed;
            if (!wantGreen && !wantRed) return false;

            if (user.NextMagic != null) return false;                  // 正在施法
            if (user.RidingMount) return false;
            if (CMain.Time < GameScene.SpellTime) return false;        // 全局施法锁

            // 目标身上缺哪种毒
            bool lackGreen = wantGreen && !target.Poison.HasFlag(PoisonType.Green);
            bool lackRed = wantRed && !target.Poison.HasFlag(PoisonType.Red);
            if (!lackGreen && !lackRed) return false;

            int equippedShape = AmuletShape(user.Equipment[(int)EquipmentSlot.Amulet]);

            // 决定这次要放的毒：优先用已经装好的（省一次换装），其次挑背包里有的
            int needShape;

            if (equippedShape == 1 && lackGreen) needShape = 1;
            else if (equippedShape == 2 && lackRed) needShape = 2;
            else if (lackGreen && FindAmuletInInventory(user, 1) != null) needShape = 1;
            else if (lackRed && FindAmuletInInventory(user, 2) != null) needShape = 2;
            else return false;                                        // 要的毒背包里也没有 → 不释放

            // 装备的毒不对 → 先去背包换上，这一轮不施法
            if (equippedShape != needShape)
                return TryEquipPoison(user, needShape, now);

            // 已经装备好对应的毒 → 施法
            if (now < _nextPoisonTime) return false;

            ClientMagic magic = user.GetMagic(Spell.Poisoning);
            if (magic == null) return false;                            // 没学施毒术
            if (CMain.Time <= magic.CastTime + magic.Delay) return false;   // 冷却中

            int range = Math.Max(attackRange, (int)magic.Range);
            if (!Functions.InRange(user.CurrentLocation, target.CurrentLocation, range)) return false;

            int cost = magic.Level * magic.LevelCost + magic.BaseCost;
            if (user.Stats[Stat.ManaPenaltyPercent] > 0)
                cost += (cost * user.Stats[Stat.ManaPenaltyPercent]) / 100;
            if (cost > user.MP) return false;

            user.NextMagic = magic;
            user.NextMagicObject = target;
            user.NextMagicLocation = target.CurrentLocation;
            user.NextMagicDirection = Functions.DirectionFromPoint(user.CurrentLocation, target.CurrentLocation);

            _nextPoisonTime = now + PoisonRetryDelay;
            Status = "状态：施毒(" + (needShape == 1 ? "绿" : "红") + ") " + target.Name;

            return true;
        }

        /// <summary>道具是毒药的话返回 Shape（1=绿 2=红），否则 0</summary>
        private static int AmuletShape(UserItem item)
        {
            if (item == null || item.Info == null) return 0;
            if (item.Info.Type != ItemType.Amulet) return 0;

            return item.Info.Shape;
        }

        /// <summary>在背包（含快捷栏）里找指定 Shape 的毒药</summary>
        private static UserItem FindAmuletInInventory(UserObject user, int shape)
        {
            if (user == null || user.Inventory == null) return null;

            for (int i = 0; i < user.Inventory.Length; i++)
            {
                UserItem item = user.Inventory[i];
                if (item == null || item.Info == null) continue;
                if (item.Info.Type != ItemType.Amulet) continue;
                if (item.Info.Shape != shape) continue;

                return item;
            }

            return null;
        }

        /// <summary>
        /// 把背包里指定 Shape 的毒药换到 Amulet 槽。
        /// 服务端 PlayerObject.EquipItem 会把原来装备的东西塞回同一个背包格子（自动互换），
        /// 所以一个 C.EquipItem 就够，不用先脱再穿。
        /// </summary>
        private static bool TryEquipPoison(UserObject user, int shape, long now)
        {
            if (now < _nextPoisonEquipTime) return false;      // 等服务器回包，别连发

            UserItem item = FindAmuletInInventory(user, shape);
            if (item == null) return false;

            _nextPoisonEquipTime = now + PoisonEquipDelay;

            Network.Enqueue(new C.EquipItem
            {
                Grid = MirGridType.Inventory,
                UniqueID = item.UniqueID,
                To = (int)EquipmentSlot.Amulet,
            });

            Status = "状态：换毒药(" + (shape == 1 ? "绿" : "红") + ")";
            return true;
        }

        /// <summary>这只怪最近是否攻击过我（4 秒内）</summary>
        private static bool IsAttackingUser(MapObject ob, long now)
        {
            if (ob == null || ob.AttackedUserTime == 0) return false;
            return now - ob.AttackedUserTime <= CounterWindow;
        }

        /// <summary>标记"有进展"（用来做卡住检测）</summary>
        private static void TouchProgress(UserObject user, long now)
        {
            _lastProgressPos = user.CurrentLocation;
            _lastProgressTime = now;
        }

        /// <summary>
        /// 卡住检测：位置在 StuckTimeout 内没变化 → 判定走不了。
        /// 触发后会重置计时，避免同一个状态每 tick 都报。
        /// </summary>
        private static bool IsStuck(UserObject user, long now)
        {
            if (user.CurrentLocation != _lastProgressPos)
            {
                TouchProgress(user, now);
                return false;
            }

            if (now - _lastProgressTime <= StuckTimeout) return false;

            _lastProgressTime = now;
            return true;
        }

        /// <summary>放弃当前目标并短暂拉黑</summary>
        private static void GiveUp(GameScene scene, long now, string status, int avoidMs = AvoidDuration)
        {
            _avoidId = _target == null ? 0 : _target.ObjectID;
            _avoidUntil = now + avoidMs;

            MapObject.TargetObjectID = 0;
            ClearTarget();

            if (scene.MapControl.AutoPath) scene.MapControl.AutoPath = false;
            Status = status;
        }

        // ================= 追击目标点 =================

        /// <summary>
        /// 关键修正：**不能把寻路终点设成怪物所在的格子**。
        /// MapControl.EmptyCell() 会把"有阻挡物的格子"判为不可走，而怪物自己就站在目标格上，
        /// 于是 A* 永远到不了终点、必然搜索失败 → 表现为"卡在障碍物附近不动"。
        /// 所以改成寻路到目标**旁边**最近的一个可走格子。
        /// </summary>
        private static Point ResolveChaseDest(MapControl map, MapObject target, Point from)
        {
            Point cell = target.CurrentLocation;

            if (InBounds(map, cell) && map.EmptyCell(cell))
                return cell;

            return FindWalkableNeighbour(map, cell, from);
        }

        private static Point FindWalkableNeighbour(MapControl map, Point cell, Point from)
        {
            Point best = Point.Empty;
            int bestDist = int.MaxValue;

            for (int i = 0; i < 8; i++)
            {
                Point p = Functions.PointMove(cell, (MirDirection)i, 1);

                if (!InBounds(map, p)) continue;
                if (!map.EmptyCell(p)) continue;

                int d = Dist(from, p);
                if (d < bestDist) { bestDist = d; best = p; }
            }

            return best;
        }

        private static bool InBounds(MapControl map, Point p)
        {
            return p.X >= 0 && p.Y >= 0 && p.X < map.Width && p.Y < map.Height;
        }

        /// <summary>
        /// 打不动检测：连续攻击对方血量一点没变 → 判定免疫/不可攻击，换目标。
        /// 注意 PercentHealth 是整数百分比，只有"完全没变化"才计数；任何变化（包括首次变成非 0）都重置。
        /// </summary>
        private static bool CheckNoDamage(MapObject target, long now)
        {
            int hp = target.PercentHealth;

            if (target.ObjectID != _streakTargetId)
            {
                _streakTargetId = target.ObjectID;
                _streakHp = hp;
                _streak = 0;
                return false;
            }

            if (hp != _streakHp)
            {
                _streakHp = hp;
                _streak = 0;
                return false;
            }

            _streak++;
            if (_streak < NoDamageAttacks) return false;

            GiveUp(GameScene.Scene, now, "状态：打不动，换目标", NoDamageAvoid);
            return true;
        }

        // ================= 漫游寻敌 =================

        /// <summary>
        /// 附近完全没有目标时随机走动找怪。围绕锚点（最后一次战斗的位置）在漫游半径内游走，
        /// 走太远会先走回锚点，避免被拖着满地图乱跑。
        /// </summary>
        private static void DoRoam(GameScene scene, UserObject user, long now)
        {
            MapControl map = scene.MapControl;

            if (_anchor == Point.Empty || !InBounds(map, _anchor))
                _anchor = user.CurrentLocation;

            // 正在走向漫游点
            if (map.AutoPath && map.CurrentPath != null && map.CurrentPath.Count > 0)
            {
                Status = "状态：漫游寻敌";
                return;
            }

            if (now < _nextRoamTime)
            {
                Status = "状态：原地等待";
                return;
            }

            // 已经很久没怪 → 走得更远（原来半径固定 6 格，等于把自己关在一个小圈里）
            if (_idleSince == 0) _idleSince = now;
            long idle = now - _idleSince;

            int radius = Math.Max(1, Settings.AutoFightRoamRange);
            int step;

            if (idle < 8000)
                step = radius;                                          // 刚清完怪：正常巡逻
            else if (idle < 20000)
                step = Math.Max(radius, radius * 3 / 2);                // 十几秒没怪：走远一点
            else
                step = Math.Max(radius, 12);                            // 很久没怪：大步探索

            // 卡住检测：3 秒没挪窝 → 换个方向重挑
            if (IsStuck(user, now))
            {
                _hasRoamDir = false;
                _roamFailStreak++;
                Status = "状态：卡住了，换方向";
                _nextRoamTime = now + 200;
                return;
            }

            MirDirection dir = PickRoamDirection(idle);

            Point dest = PickRoamDest(map, user.CurrentLocation, dir, step, radius);

            if (dest == Point.Empty)
            {
                // 这个方向走不通（撞墙/水/悬崖）→ 下次换方向
                _hasRoamDir = false;
                _roamFailStreak++;

                if (_roamFailStreak >= 3)
                {
                    // 连续走不动：原地歇一秒，把锚点放到自己脚下，
                    // 免得以后"离锚点太远 → 返回巡逻点"之类的问题
                    _anchor = user.CurrentLocation;
                    _roamFailStreak = 0;
                    Status = "状态：周围走不动";
                    _nextRoamTime = now + 1000;
                    return;
                }

                Status = "状态：换个方向";
                _nextRoamTime = now + 200;
                return;
            }

            _roamFailStreak = 0;
            _roamDir = dir;
            _hasRoamDir = true;
            _anchor = user.CurrentLocation;          // 锚点跟着人走，别把人拉回老地方

            Status = "状态：漫游寻敌 " + step + "格";

            MoveTo(dest);

            // MoveTo 找不到路时不会置 AutoPath，立刻退回来换方向（别卡在"寻路失败"上）
            if (!map.AutoPath)
            {
                _hasRoamDir = false;
                _roamFailStreak++;
                Status = "状态：走不过去，换方向";
                _nextRoamTime = now + 200;
                return;
            }

            _nextRoamTime = now + 400;
        }

        /// <summary>
        /// 挑漫游方向：
        ///   刚清完怪 → 60% 沿着上次方向继续（一路走过去，不原地抖）
        ///   十几秒没怪 → 60% 随便换新方向
        ///   很久没怪 → 一定不回头（之前的区域已经清空了，回头就是空转）
        /// </summary>
        private static MirDirection PickRoamDirection(long idle)
        {
            if (!_hasRoamDir) return (MirDirection)_rand.Next(8);

            int r = _rand.Next(100);

            if (idle < 8000)
            {
                if (r < 60) return _roamDir;
                if (r < 80) return OffsetDir(_roamDir, 1);
                return OffsetDir(_roamDir, -1);
            }

            if (idle < 20000)
            {
                if (r < 40) return _roamDir;
                return (MirDirection)_rand.Next(8);
            }

            MirDirection opposite = OffsetDir(_roamDir, 4);
            MirDirection dir = (MirDirection)_rand.Next(8);

            if (dir == opposite) dir = OffsetDir(dir, 1);      // 不选正后方

            return dir;
        }

        /// <summary>
        /// 沿指定方向挑一个落脚点（尽量远，避开最近去过的区域）。
        /// 前 24 次只在方向 ±1 格内试（保持"一路走过去"的感觉），后面放开全方向兜底。
        /// 不管目的地中间有没有障碍，MoveTo 的 A* 会自己绕。
        /// </summary>
        private static Point PickRoamDest(MapControl map, Point from, MirDirection dir, int step, int minStep)
        {
            if (minStep < 2) minStep = 2;
            if (step < minStep) step = minStep;

            for (int i = 0; i < 40; i++)
            {
                MirDirection d;

                if (i < 24)
                    d = OffsetDir(dir, _rand.Next(-1, 2));       // -1 / 0 / +1
                else
                    d = (MirDirection)_rand.Next(8);

                int r = _rand.Next(minStep, step + 1);
                Point p = Functions.PointMove(from, d, r);

                if (!InBounds(map, p)) continue;
                if (!map.EmptyCell(p)) continue;
                if (NearRecentRoam(p)) continue;

                RememberRoam(p);
                return p;
            }

            return Point.Empty;
        }

        /// <summary>方向偏移（绕 8 方向环转）</summary>
        private static MirDirection OffsetDir(MirDirection dir, int offset)
        {
            int d = ((int)dir + offset) % 8;
            if (d < 0) d += 8;
            return (MirDirection)d;
        }

        /// <summary>目的地离最近去过的几个点太近 → 换一个（避免来回踩同一片地）</summary>
        private static bool NearRecentRoam(Point p)
        {
            for (int i = 0; i < _recentRoam.Length; i++)
            {
                if (_recentRoam[i] == Point.Empty) continue;
                if (Dist(p, _recentRoam[i]) <= 2) return true;
            }

            return false;
        }

        private static void RememberRoam(Point p)
        {
            _recentRoam[_recentRoamIndex] = p;
            _recentRoamIndex = (_recentRoamIndex + 1) % _recentRoam.Length;
        }

        /// <summary>
        /// 当前「自动技能」的有效射程（没开自动技能 / 没选技能 / 职业不符 → 0）。
        /// 用来决定"要不要跑过去"：法师开技能时应该在 9 格外就开始放，而不是贴脸。
        /// </summary>
        private static int SkillRange(UserObject user)
        {
            if (!Settings.AutoSkillEnabled || Settings.AutoSkillSpell <= 0) return 0;
            if (user == null || user.Class != MirClass.Wizard) return 0;

            ClientMagic magic = user.GetMagic((Spell)Settings.AutoSkillSpell);
            return magic == null ? 0 : magic.Range;
        }

        /// <summary>
        /// 法师自动技能：对当前目标连续释放选定的那个技能。
        ///
        /// 和施毒/治疗一样只设置 User.NextMagic*，真正发包/冷却/扣蓝都交给
        /// GameScene.UseMagic 与 PlayerObject.Process（和手动按技能键完全一致）。
        /// </summary>
        private static bool TryCastSkill(UserObject user, MapObject target, long now, int attackRange)
        {
            if (user == null || target == null || target.Dead) return false;
            if (!Settings.AutoSkillEnabled) return false;
            if (Settings.AutoSkillSpell <= 0) return false;

            if (user.Class != MirClass.Wizard) return false;          // 职业页里这张表只有法师
            if (user.NextMagic != null) return false;                 // 正在施法
            if (user.RidingMount) return false;
            if (CMain.Time < GameScene.SpellTime) return false;       // 全局施法锁

            ClientMagic magic = user.GetMagic((Spell)Settings.AutoSkillSpell);
            if (magic == null) return false;                          // 没学这个技能

            if (CMain.Time <= magic.CastTime + magic.Delay) return false;   // 冷却中

            int range = Math.Max(attackRange, (int)magic.Range);
            if (!Functions.InRange(user.CurrentLocation, target.CurrentLocation, range)) return false;

            int cost = magic.Level * magic.LevelCost + magic.BaseCost;
            if (user.Stats[Stat.ManaPenaltyPercent] > 0)
                cost += (cost * user.Stats[Stat.ManaPenaltyPercent]) / 100;
            if (cost > user.MP) return false;                         // 蓝不够

            user.NextMagic = magic;
            user.NextMagicObject = target;
            user.NextMagicLocation = target.CurrentLocation;
            user.NextMagicDirection = Functions.DirectionFromPoint(user.CurrentLocation, target.CurrentLocation);

            SetStatus(string.Format("状态：技能 {0} → {1}  d{2}/r{3}  me({4},{5}) tg({6},{7})",
                magic.Name, target.Name, Dist(user.CurrentLocation, target.CurrentLocation), range,
                user.CurrentLocation.X, user.CurrentLocation.Y,
                target.CurrentLocation.X, target.CurrentLocation.Y));
            return true;
        }

        // ================= 攻击 =================

        private static void Attack(UserObject user, MapObject target, long now)
        {
            MirDirection dir = Functions.DirectionFromPoint(user.CurrentLocation, target.CurrentLocation);

            if (user.Class == MirClass.Archer && user.HasClassWeapon && !user.RidingMount)
            {
                QueuedAction q = new QueuedAction
                {
                    Action = MirAction.AttackRange1,
                    Direction = dir,
                    Location = user.CurrentLocation,
                    Params = new List<object>(),
                };
                q.Params.Add(target.ObjectID);
                q.Params.Add(target.CurrentLocation);

                user.QueuedAction = q;
            }
            else
            {
                user.QueuedAction = new QueuedAction
                {
                    Action = MirAction.Attack1,
                    Direction = dir,
                    Location = user.CurrentLocation,
                };
            }

            _nextAttackTime = now + Math.Max(0, Settings.AutoFightAttackDelay);
        }

        // ================= 拾取 =================

        private static void DoLoot(GameScene scene, UserObject user, MapObject loot, long now)
        {
            // 走到一半背包被塞满（比如别的掉落自动进了包）→ 立刻停手
            if (user.FreeInventorySlots <= 0)
            {
                if (scene.MapControl.AutoPath) scene.MapControl.AutoPath = false;
                Status = "状态：背包已满";
                return;
            }

            Status = "状态：拾取 " + loot.Name;

            // 换了另一件物品 → 重置卡住计时
            if (loot.ObjectID != _lootId)
            {
                _lootId = loot.ObjectID;
                TouchProgress(user, now);
            }

            // 走不到 / 站在上面也捡不动（背包满）→ 这件先跳过，别一直杵在原地
            if (IsStuck(user, now))
            {
                AvoidLoot(loot.ObjectID, now);
                _lootId = 0;

                if (scene.MapControl.AutoPath) scene.MapControl.AutoPath = false;
                TouchProgress(user, now);

                Status = "状态：捡不到，跳过";
                return;
            }

            if (user.CurrentLocation == loot.CurrentLocation)
            {
                // 已经站到物品格上 → 发拾取包（和按 Tab 一样）
                if (scene.MapControl.AutoPath)
                    scene.MapControl.AutoPath = false;

                if (now > GameScene.PickUpTime)
                {
                    GameScene.PickUpTime = now + 200;
                    Network.Enqueue(new C.PickUp());
                }

                return;
            }

            // 物品不阻挡（ItemObject.Blocking = false），所以直接寻路到物品格就行
            MoveTo(loot.CurrentLocation);
        }

        private static MapObject FindLoot(int range)
        {
            UserObject user = MapObject.User;
            MapControl map = GameScene.Scene.MapControl;
            Point from = user.CurrentLocation;
            range = Math.Max(1, range);

            MapObject best = null;
            int bestDist = int.MaxValue;

            List<MapObject> list = MapControl.ObjectsList;
            for (int i = 0; i < list.Count; i++)
            {
                MapObject ob = list[i];
                if (!(ob is ItemObject item)) continue;
                if (!Functions.InRange(from, item.CurrentLocation, range)) continue;
                if (!LootAllowed(item)) continue;

                // 刚判定过捡不到 → 冷却期内跳过（到期后自动重新纳入候选）
                if (IsLootAvoided(item.ObjectID, CMain.Time)) continue;

                // 物品所在格子不可走（被怪物/别的玩家占着，或本身就是障碍格）
                // → 站不上去就永远捡不到，直接跳过，别去追。
                // 注意：自己脚下那一格要排除 —— EmptyCell 会把玩家自己也当成阻挡物。
                if (item.CurrentLocation != from && !map.EmptyCell(item.CurrentLocation)) continue;

                int d = Dist(from, item.CurrentLocation);
                if (d < bestDist) { bestDist = d; best = item; }
            }

            return best;
        }

        /// <summary>
        /// 这件东西要不要捡。
        ///
        /// 地面物品（ItemObject）只带名字和名字颜色，不带 ItemInfo，所以：
        ///   · 类型 → 用名字去 GameScene.ItemInfoList 反查（带缓存，名字里的 " (数量)" 会剥掉）
        ///   · 极品 → 服务端 ItemObject.GetInfo 里 Item.IsAdded 时把 NameColour 设成青色，
        ///            其余按 ItemGrade 上色。所以「青色名字」= 极品（带附加属性）。
        /// </summary>
        private static bool LootAllowed(MapObject ob)
        {
            if (!(ob is ItemObject item)) return false;

            // 极品不看类型：只要是带附加属性的就捡
            // 注意必须比 ARGB：服务端发的是 ToArgb()，客户端 FromArgb() 读回来，
            // state 是 ARGB 而不是 KnownColor，直接和 Color.Cyan 比 == 永远不相等。
            if (Settings.AutoFightLootTreasure && item.NameColour.ToArgb() == TreasureArgb) return true;

            if (item.IsGold) return Settings.AutoFightLootGold;

            ItemInfo info = FindItemInfo(item.Name);

            if (info == null) return Settings.AutoFightLootOther;      // 查不到就归到"其它"

            switch (info.Type)
            {
                case ItemType.Potion:
                case ItemType.Meat:
                case ItemType.Food:
                    return Settings.AutoFightLootPotion;

                case ItemType.Book:
                    return Settings.AutoFightLootBook;

                case ItemType.Weapon:
                    return Settings.AutoFightLootWeapon;

                case ItemType.Armour:
                case ItemType.Helmet:
                case ItemType.Boots:
                case ItemType.Belt:
                    return Settings.AutoFightLootArmour;

                case ItemType.Necklace:
                case ItemType.Bracelet:
                case ItemType.Ring:
                    return Settings.AutoFightLootJewel;

                default:
                    return Settings.AutoFightLootOther;
            }
        }

        // ---------- 名字 → ItemInfo 缓存 ----------

        /// <summary>极品（带附加属性）物品的地面名字颜色：服务端 Item.IsAdded → Color.Cyan</summary>
        private static readonly int TreasureArgb = Color.Cyan.ToArgb();

        private static readonly Dictionary<string, ItemInfo> _itemIndex =
            new Dictionary<string, ItemInfo>(StringComparer.OrdinalIgnoreCase);

        private static int _itemIndexCount = -1;

        /// <summary>
        /// 按名字查 ItemInfo。客户端物品库是边下载边追加的，所以列表数量变了就重建索引。
        /// 地面名字会带数量后缀（"金创药(小量) (3)"），查不到时剥掉再查一次。
        /// </summary>
        private static ItemInfo FindItemInfo(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;

            List<ItemInfo> list = GameScene.ItemInfoList;
            if (list == null) return null;

            if (_itemIndexCount != list.Count)
            {
                _itemIndex.Clear();

                for (int i = 0; i < list.Count; i++)
                {
                    ItemInfo info = list[i];
                    if (info == null || string.IsNullOrEmpty(info.Name)) continue;

                    // 地面物品用的是 ItemInfo.FriendlyName（服务端 ItemObject.Name 就是它）：
                    //   去掉末尾数字 + 去掉 [] 里的内容
                    // 所以两个名字都要建索引，否则带数字后缀/方括号的物品查不到类型。
                    _itemIndex[info.Name] = info;

                    string friendly = info.FriendlyName;
                    if (!string.IsNullOrEmpty(friendly)) _itemIndex[friendly] = info;
                }

                _itemIndexCount = list.Count;
            }

            if (_itemIndex.TryGetValue(name, out ItemInfo found)) return found;

            // 去掉 " (3)" 这种数量后缀
            int p = name.LastIndexOf(" (", StringComparison.Ordinal);

            if (p > 0 && name.EndsWith(")", StringComparison.Ordinal))
            {
                if (_itemIndex.TryGetValue(name.Substring(0, p), out found)) return found;
            }

            return null;
        }

        // ================= 寻敌 =================

        private static MapObject FindTarget(long now, int range)
        {
            UserObject user = MapObject.User;
            Point from = user.CurrentLocation;
            range = Math.Max(1, range);

            MapObject best = null;
            int bestScore = int.MaxValue;

            List<MapObject> list = MapControl.ObjectsList;
            for (int i = 0; i < list.Count; i++)
            {
                MapObject ob = list[i];
                if (!IsAttackable(ob)) continue;
                if (!Functions.InRange(from, ob.CurrentLocation, range)) continue;

                // 刚被判定"走不到"的目标，冷却期跳过
                if (ob.ObjectID == _avoidId && now < _avoidUntil) continue;

                int d = Dist(from, ob.CurrentLocation);

                // 优先级：0=最近  1=血量最少（同血量再比距离）
                int score = Settings.AutoFightPriority == 1
                    ? ob.PercentHealth * 1000 + d
                    : d;

                // 正在打我的怪 → 绝对优先
                if (IsAttackingUser(ob, now))
                    score -= 1000000;

                if (score < bestScore) { bestScore = score; best = ob; }
            }

            return best;
        }

        /// <summary>找最近的一只"最近攻击过我"的怪（用于优先反击）</summary>
        private static MapObject FindRecentAttacker(UserObject user, long now)
        {
            Point from = user.CurrentLocation;
            int range = Math.Max(Settings.AutoFightTargetRange, Settings.AutoFightSeekRange);

            MapObject best = null;
            int bestDist = int.MaxValue;

            List<MapObject> list = MapControl.ObjectsList;
            for (int i = 0; i < list.Count; i++)
            {
                MapObject ob = list[i];
                if (!IsAttackable(ob)) continue;
                if (!IsAttackingUser(ob, now)) continue;
                if (!Functions.InRange(from, ob.CurrentLocation, range)) continue;

                int d = Dist(from, ob.CurrentLocation);
                if (d < bestDist) { bestDist = d; best = ob; }
            }

            return best;
        }

        private static bool IsAttackable(MapObject ob)
        {
            if (ob == null || ob.Dead || ob.Hidden) return false;
            if (ob == MapObject.User) return false;

            if (ob is MonsterObject monster)
            {
                if (IsNonCombat(monster)) return false;

                // 用户自定义的"不攻击怪物名单"（按名字，不区分大小写）
                if (Settings.IsMonsterIgnored(monster.Name)) return false;
                if (monster.MasterObjectId != 0) return false;  // 别人的宝宝/宠物不打
                return true;
            }

            if (ob is PlayerObject player)
            {
                if (Settings.AutoFightIgnorePlayers) return false;
                return player != MapObject.User;
            }

            return false;
        }

        /// <summary>
        /// 不可/不该攻击的怪物。两重判定：
        ///
        /// 1) 按怪物外观（MonsterObject.BaseImage = 服务端下发的 Monster 枚举）——
        ///    这是数据库里最稳定的类型标识。本服 DB 里"弓箭守卫"就是
        ///    name=ArcherGuard / image=139 / **AI=57(TownArcher)**，光看 AI 很容易漏。
        ///
        /// 2) 按 AI —— 不同 DB 的 AI 编号可能不同，作为第二重保险：
        ///    6 / 58 → Guard（服务端 Guard.IsAttackTarget 恒 false，Attacked 直接抛异常）
        ///    57     → TownArcher（城镇弓箭守卫）
        ///    113    → ArcherGuard : Guard
        ///    64     → IntelligentCreatureObject（智能宝宝）
        ///    70     → Hugger（客户端原生点击攻击就跳过）
        ///
        /// 注意：IceGuard/ElementGuard/DemonGuard/KingGuard（image 249~252）是正常 BOSS 小怪，
        ///      可以打，不能误排除。
        /// </summary>
        private static bool IsNonCombat(MonsterObject monster)
        {
            switch (monster.BaseImage)
            {
                case Monster.Guard:
                case Monster.TaoistGuard:
                case Monster.Guard2:
                case Monster.ArcherGuard:
                case Monster.ArcherGuard2:
                case Monster.Guard3:
                case Monster.ArcherGuard3:
                    return true;
            }

            switch (monster.AI)
            {
                case 6:   // Guard
                case 57:  // TownArcher（弓箭守卫）
                case 58:  // Guard
                case 64:  // IntelligentCreatureObject
                case 70:  // Hugger
                case 113: // ArcherGuard : Guard
                    return true;
            }

            return false;
        }

        /// <summary>目标还活着、还在当前地图上</summary>
        private static bool IsValidTarget(MapObject target)
        {
            if (target == null || target.Dead) return false;

            MapObject current;
            if (!MapControl.Objects.TryGetValue(target.ObjectID, out current)) return false;
            if (current != target) return false;

            return IsAttackable(target);
        }

        private static void ClearTarget()
        {
            _target = null;
            TargetID = 0;
        }

        // ================= 走位 =================

        /// <summary>复用客户端的自动寻路（右键点地面那套）</summary>
        // ================= 视野 / 障碍 / 站位 =================

        /// <summary>格子是否是墙（地图数据里标记为不可走）</summary>
        private static bool IsWall(MapControl map, Point p)
        {
            if (!InBounds(map, p)) return true;
            if (map.M2CellInfo == null) return false;

            CellInfo info = map.M2CellInfo[p.X, p.Y];

            // 和 MapControl.EmptyCell() 用同一套地图标志：
            //   BackImage 0x20000000 = 不可走（墙/水面/禁止区域）
            //   FrontImage 0x8000    = 阻挡（建筑/树等静态挡片）
            return (info.BackImage & 0x20000000) != 0 || (info.FrontImage & 0x8000) != 0;
        }

        /// <summary>
        /// 两点之间是否有直线视野（Bresenham 采样，跳过起点和终点）。
        /// 只认地图墙：怪物站在中间不会挡弹道，把它们算进来会误判成"打不到"。
        /// </summary>
        private static bool HasLineOfSight(MapControl map, Point from, Point to)
        {
            if (!Settings.AutoFightLineOfSight) return true;
            if (map == null || from == to) return true;

            int dx = Math.Abs(to.X - from.X);
            int dy = Math.Abs(to.Y - from.Y);

            int sx = from.X < to.X ? 1 : -1;
            int sy = from.Y < to.Y ? 1 : -1;

            int err = dx - dy;
            Point p = from;

            for (int guard = 0; guard < 64; guard++)
            {
                int e2 = err * 2;

                if (e2 > -dy) { err -= dy; p.X += sx; }
                if (e2 < dx) { err += dx; p.Y += sy; }

                if (p == to) return true;          // 终点不判（怪自己站在那）
                if (IsWall(map, p)) return false;
            }

            return true;
        }

        /// <summary>
        /// 找一个「能直线打到目标、距离落在 [minDist, maxDist]、离自己最近」的格子走过去。
        /// 同时负责两件事：
        ///   1) 风筝：怪贴得太近 → 挪到 keepRange 那一圈
        ///   2) 绕障碍：中间隔墙打不到 → 挪到能直线命中的格子
        /// </summary>
        private static bool TryReposition(GameScene scene, UserObject user, MapObject target, long now, int minDist, int maxDist)
        {
            MapControl map = scene.MapControl;
            if (map == null) return false;

            Point tg = target.CurrentLocation;
            Point me = user.CurrentLocation;

            if (minDist < 1) minDist = 1;
            if (maxDist < minDist) maxDist = minDist;
            if (maxDist > minDist + 2) maxDist = minDist + 2;        // 只看最近几圈，够用又省 CPU
            if (maxDist > 10) maxDist = 10;

            // 上次选好的落点还没到、依然有效 → 继续走，避免每 tick 换目的地把路径抖掉
            if (_repositionDest != Point.Empty && now < _repositionUntil)
            {
                if (InBounds(map, _repositionDest)
                    && map.EmptyCell(_repositionDest)
                    && HasLineOfSight(map, _repositionDest, tg)
                    && Dist(_repositionDest, tg) >= minDist)
                {
                    MoveTo(_repositionDest);
                    return true;
                }
            }

            if (now < _nextReposition) return false;
            _nextReposition = now + RepositionInterval;

            int myDist = Dist(me, tg);

            // 现在站的位置已经满足「够得着 + 有视野」→ 不用动
            if (myDist >= minDist && myDist <= maxDist && HasLineOfSight(map, me, tg))
                return false;

            Point best = Point.Empty;
            int bestScore = int.MaxValue;

            for (int r = minDist; r <= maxDist; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r) continue;      // 只取环上的格子

                        Point p = new Point(tg.X + dx, tg.Y + dy);

                        if (!InBounds(map, p)) continue;
                        if (p == me) continue;
                        if (!map.EmptyCell(p)) continue;
                        if (!HasLineOfSight(map, p, tg)) continue;

                        int step = Dist(me, p);
                        if (step > 7) continue;                                     // 走太远不划算

                        int score = step * 16 + (maxDist - r);                      // 近的优先，其次贴着射程上限
                        if (score >= bestScore) continue;

                        bestScore = score;
                        best = p;
                    }
                }
            }

            // 环上一个能站能打的格子都没有（被墙围住/角落）→ 兜底：沿"怪→我"方向往外挪一格
            if (best == Point.Empty)
            {
                MirDirection away = Functions.DirectionFromPoint(tg, me);
                Point step = Functions.PointMove(me, away, 1);

                if (InBounds(map, step) && step != me && map.EmptyCell(step)
                    && Dist(step, tg) > myDist)
                    best = step;
            }

            if (best == Point.Empty) return false;

            _repositionDest = best;
            _repositionUntil = now + RepositionHoldMs;

            MoveTo(best);
            return true;
        }

        private static void MoveTo(Point dest)
        {
            MapControl map = GameScene.Scene.MapControl;
            long now = CMain.Time;

            // 冷却期内且目标只在 1 格内挪动 → 直接沿用（成功走同一条路，失败也不立刻重试，
            // 否则目标不可达时会变成每 tick 一次 A*）
            if (Dist(dest, _pathDest) <= 1 && now < _nextPathTime)
                return;

            _pathDest = dest;
            _nextPathTime = now + RepathInterval;

            // MaxNodes 传 0：这是"找到的路径超过 N 个节点就判失败"的后置过滤，
            // 绕障碍时路径很容易超过 20 个节点（之前就是这样被丢掉的）。
            // 开销由上界由 MaxExpansions 保证。
            List<Node> path = map.PathFinder.FindPath(MapObject.User.CurrentLocation, dest, 0, PathBudget);

            if (path == null || path.Count == 0)
            {
                if (map.AutoPath) map.AutoPath = false;
                Status = "状态：走不过去";
                return;
            }

            map.CurrentPath = path;
            map.AutoPath = true;
        }

        private static int Dist(Point a, Point b)
        {
            return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }
    }
}
