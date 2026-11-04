using Client.MirControls;
using Client.MirGraphics;
using Client.MirObjects;
using Client.MirSounds;

namespace Client.MirScenes.Dialogs
{
    /// <summary>
    /// 客户端内挂配置界面（Tab 布局）
    ///
    ///   战斗   = 总开关 + 寻敌 + 战斗 + 状态
    ///   恢复   = 自动喝药 + HP/MP 药水 + 检测间隔
    ///   拾取   = 自动拾取 / 范围 / 过滤 / 先捡后打 / 回收范围
    ///   职业   = 道士治疗术
    ///
    /// 性能要点：
    ///  1) 背景自绘（MirControl + BackColour + Border），不依赖任何窗口底图；
    ///  2) 所有文字标签关掉 OutLine、只画单行（标签绘制从 5 次 DrawText 降到 1 次）；
    ///  3) **全局只用一个 MirTextBox**，点哪个数字才把它移过去 ——
    ///     避免一次创建十个 WinForms TextBox 句柄导致首次打开卡几秒。
    /// </summary>
    public sealed class AutoFightDialog : MirControl
    {
        private const int W = 440;
        private const int H = 260;

        private const int TabY = 30;         // Tab 文字 y
        private const int UnderlineY = 47;   // 当前 Tab 的下划线 y
        private const int Row0 = 56;         // 内容首行

        private static readonly Color ColourTitle = Color.FromArgb(255, 245, 226, 180);
        private static readonly Color ColourTabOn = Color.FromArgb(255, 245, 226, 180);
        private static readonly Color ColourTabOff = Color.FromArgb(255, 150, 150, 150);
        private static readonly Color ColourHint = Color.FromArgb(255, 140, 140, 140);

        private readonly string[] TabNames = { "战斗", "恢复", "拾取", "职业" };

        public MirButton CloseButton;

        private MirControl _header;
        private MirLabel TitleLabel;

        // ---- Tab ----
        private readonly MirLabel[] _tabLabels = new MirLabel[4];
        private readonly List<MirControl>[] _pages = { new List<MirControl>(), new List<MirControl>(), new List<MirControl>(), new List<MirControl>() };
        private MirControl _tabUnderline;
        private int _tab = -1;
        private int _buildPage;
        private int _tabLabelX = 24;

        // ---- 可点击的值标签 ----
        private MirLabel _enabledVal;
        private MirLabel _priorityVal, _ignorePlayersVal, _ignoreListVal, _roamVal;
        private MirLabel _chaseVal, _basicAttackVal, _autoSkillVal, _skillNameVal;
        private MirLabel _keepVal, _losVal;
        private MirLabel _autoPotVal;
        private MirLabel _lootVal, _lootFirstVal;
        private readonly Dictionary<MirCheckBox, Func<bool>> _lootChecks = new Dictionary<MirCheckBox, Func<bool>>();
        private MirLabel _hpPot, _mpPot;
        private MirLabel _healVal;
        private MirCheckBox _checkPoisonGreen, _checkPoisonRed;
        private readonly List<MirControl> _taoistControls = new List<MirControl>();
        private readonly List<MirControl> _wizardControls = new List<MirControl>();
        private readonly Dictionary<MirControl, int> _wizardBaseY = new Dictionary<MirControl, int>();
        private MirClass _buildClass;                 // 构建职业页时当前分组属于哪个职业
        private MirLabel _noClassHint;
        private int _classCache = -1;
        private MirLabel _poisonNowVal;
        private MirLabel _statusVal;

        // ---- 数字字段：值用廉价 MirLabel 显示，点击后弹出唯一的共享输入框 ----
        private sealed class NumField
        {
            public int Min, Max, Digits;
            public Func<int> Get;
            public Action<int> Set;
            public MirLabel Label;
            public Point Pos;
        }

        private readonly List<NumField> _fields = new List<NumField>();
        private NumField _editing;
        private MirTextBox _editBox;

        public AutoFightDialog()
        {
            Size = new Size(W, H);
            BackColour = Color.FromArgb(255, 16, 16, 16);
            Border = true;
            BorderColour = Color.FromArgb(255, 168, 138, 84);
            DrawControlTexture = true;
            Movable = true;
            Sort = true;
            Visible = false;

            Location = new Point((Settings.ScreenWidth - Size.Width) / 2, (Settings.ScreenHeight - Size.Height) / 2);

            // ---- 标题条 ----
            _header = new MirControl
            {
                BackColour = Color.FromArgb(255, 34, 30, 22),
                Border = true,
                BorderColour = Color.FromArgb(255, 120, 100, 60),
                DrawControlTexture = true,
                Location = new Point(0, 0),
                NotControl = true,
                Size = new Size(W, 26),
                Parent = this,
            };

            TitleLabel = new MirLabel
            {
                AutoSize = true,
                Font = new System.Drawing.Font(Settings.FontName, 10F),
                ForeColour = ColourTitle,
                Location = new Point(10, 4),
                Parent = _header,
                OutLine = false,
                DrawFormat = TextFormatFlags.NoPrefix,
                Text = GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AutoFight),
            };

            CloseButton = new MirButton
            {
                HoverIndex = 361,
                Index = 360,
                Location = new Point(W - 24, 2),
                Library = Libraries.Prguse2,
                Parent = this,
                PressedIndex = 362,
                Sound = SoundList.ButtonA,
            };
            CloseButton.Click += (o, e) => Hide();

            // ---- Tab 栏 ----
            for (int i = 0; i < TabNames.Length; i++)
            {
                int index = i;

                _tabLabels[i] = new MirLabel
                {
                    AutoSize = true,
                    Font = new System.Drawing.Font(Settings.FontName, 9F),
                    ForeColour = ColourTabOff,
                    Location = new Point(_tabLabelX, TabY),
                    Parent = this,
                    OutLine = false,
                    DrawFormat = TextFormatFlags.NoPrefix,
                    Sound = SoundList.ButtonA,
                    Text = TabNames[i],
                };
                _tabLabels[i].Click += (o, e) => SelectTab(index);

                _tabLabelX += 72;
            }

            _tabUnderline = new MirControl
            {
                BackColour = ColourTabOn,
                DrawControlTexture = true,
                Location = new Point(24, UnderlineY),
                NotControl = true,
                Size = new Size(32, 2),
                Parent = this,
            };

            // ---- 唯一的共享输入框（隐藏，不参与首次绘制）----
            _editBox = new MirTextBox
            {
                BackColour = Color.FromArgb(255, 36, 32, 24),
                ForeColour = Color.LightGreen,
                Font = new System.Drawing.Font(Settings.FontName, 9F),
                Location = new Point(0, 0),
                Size = new Size(30, 16),
                MaxLength = 4,
                Parent = this,
                Visible = false,
            };

            _editBox.TextBox.KeyPress += (o, e) =>
            {
                if (e.KeyChar == (char)Keys.Enter)
                {
                    CommitEdit();
                    Program.Form.ActiveControl = null;
                    e.Handled = true;
                    return;
                }

                if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar))
                    e.Handled = true;
            };

            _editBox.TextBox.LostFocus += (o, e) => CommitEdit();

            // =====================================================================
            //  Tab 0: 战斗（总开关 + 寻敌 + 战斗 + 状态）
            // =====================================================================
            _buildPage = 0;

            _enabledVal = Click(10, Row0, Color.Yellow, () =>
            {
                Settings.AutoFightEnabled = !Settings.AutoFightEnabled;
                Settings.SaveAutoFight();
                if (!Settings.AutoFightEnabled) AutoFight.Stop();
            });

            Section("── 寻敌 ──", 80);

            Lbl("范围", 10, 104);
            AddNum(46, 104, 2, 1, 30, () => Settings.AutoFightTargetRange, v => Settings.AutoFightTargetRange = v);
            Lbl("格", 78, 104);

            Lbl("优先级", 110, 104);
            _priorityVal = Click(158, 104, Color.LightCyan, () =>
            {
                Settings.AutoFightPriority = Settings.AutoFightPriority == 0 ? 1 : 0;
                Settings.SaveAutoFight();
            });

            Lbl("不打玩家", 220, 104);
            _ignorePlayersVal = Click(280, 104, Color.LightCyan, () =>
            {
                Settings.AutoFightIgnorePlayers = !Settings.AutoFightIgnorePlayers;
                Settings.SaveAutoFight();
            });

            _ignoreListVal = Click(310, 104, Color.FromArgb(255, 255, 170, 170), ShowIgnorePicker);

            Lbl("扩大搜索", 10, 128);
            AddNum(70, 128, 2, 1, 40, () => Settings.AutoFightSeekRange,
                   v => Settings.AutoFightSeekRange = Math.Max(v, Settings.AutoFightTargetRange));
            Lbl("格", 102, 128);

            Lbl("漫游", 140, 128);
            _roamVal = Click(184, 128, Color.LightCyan, () =>
            {
                Settings.AutoFightRoam = !Settings.AutoFightRoam;
                Settings.SaveAutoFight();
            });

            Lbl("半径", 224, 128);
            AddNum(258, 128, 2, 1, 20, () => Settings.AutoFightRoamRange, v => Settings.AutoFightRoamRange = v);
            Lbl("格", 290, 128);

            Section("── 战斗 ──", 152);

            Lbl("追击", 10, 176);
            _chaseVal = Click(46, 176, Color.LightCyan, () =>
            {
                Settings.AutoFightChase = !Settings.AutoFightChase;
                Settings.SaveAutoFight();
            });

            Lbl("保持距离", 96, 176);
            _keepVal = Click(158, 176, Color.LightCyan, () =>
            {
                Settings.AutoFightKeepDistance = !Settings.AutoFightKeepDistance;
                Settings.SaveAutoFight();
            });
            AddNum(196, 176, 2, 1, 12, () => Settings.AutoFightKeepRange, v => Settings.AutoFightKeepRange = v);
            Lbl("格", 224, 176);

            Lbl("障碍", 258, 176);
            _losVal = Click(294, 176, Color.LightCyan, () =>
            {
                Settings.AutoFightLineOfSight = !Settings.AutoFightLineOfSight;
                Settings.SaveAutoFight();
            });

            Lbl("攻击距离", 10, 200);
            AddNum(74, 200, 2, 1, 12, () => Settings.AutoFightAttackRange, v => Settings.AutoFightAttackRange = v);
            Lbl("格", 106, 200);

            Lbl("攻击间隔", 140, 200);
            AddNum(204, 200, 4, 0, 3000, () => Settings.AutoFightAttackDelay, v => Settings.AutoFightAttackDelay = v);
            Lbl("毫秒", 252, 200);

            Lbl("普攻", 310, 200);
            _basicAttackVal = Click(346, 200, Color.LightCyan, () =>
            {
                Settings.AutoFightBasicAttack = !Settings.AutoFightBasicAttack;
                Settings.SaveAutoFight();
            });

            _statusVal = MakeLabel("状态：空闲", 10, 224, Color.FromArgb(255, 200, 200, 200), 9F);

            // =====================================================================
            //  Tab 1: 恢复
            // =====================================================================
            _buildPage = 1;

            Lbl("自动喝药", 10, Row0);
            _autoPotVal = Click(70, Row0, Color.LightCyan, () =>
            {
                Settings.AutoPotEnabled = !Settings.AutoPotEnabled;
                if (Settings.AutoPotEnabled) AutoPotion.Reset();
                Settings.SaveAutoPot();
            });

            Lbl("检测间隔", 150, Row0);
            AddNum(212, Row0, 4, 300, 5000, () => Settings.AutoPotDelay, v =>
            {
                Settings.AutoPotDelay = v;
                AutoPotion.Reset();
            });
            Lbl("毫秒", 262, Row0);

            Lbl("HP <", 10, 80);
            AddNum(46, 80, 2, 1, 99, () => Settings.AutoPotHPPercent, v => Settings.AutoPotHPPercent = v);
            Lbl("%", 78, 80);
            Lbl("药水", 110, 80);
            _hpPot = Click(150, 80, Color.LightCyan, () => ShowPicker(Stat.HP));

            Lbl("MP <", 10, 104);
            AddNum(46, 104, 2, 1, 99, () => Settings.AutoPotMPPercent, v => Settings.AutoPotMPPercent = v);
            Lbl("%", 78, 104);
            Lbl("药水", 110, 104);
            _mpPot = Click(150, 104, Color.LightCyan, () => ShowPicker(Stat.MP));

            Hint("血量/魔法低于阈值时自动喝药", 10, 128);

            // =====================================================================
            //  Tab 2: 拾取
            // =====================================================================
            _buildPage = 2;

            Lbl("自动拾取", 10, Row0);
            _lootVal = Click(74, Row0, Color.LightCyan, () =>
            {
                Settings.AutoFightLoot = !Settings.AutoFightLoot;
                Settings.SaveAutoFight();
            });

            Lbl("范围", 130, Row0);
            AddNum(164, Row0, 2, 1, 20, () => Settings.AutoFightLootRange, v => Settings.AutoFightLootRange = v);
            Lbl("格", 196, Row0);

            Lbl("回收", 230, Row0);
            AddNum(264, Row0, 2, 0, 30, () => Settings.AutoFightLootSweep, v => Settings.AutoFightLootSweep = v);
            Lbl("格", 296, Row0);

            Lbl("先捡后打", 334, Row0);
            _lootFirstVal = Click(406, Row0, Color.LightCyan, () =>
            {
                Settings.AutoFightLootFirst = !Settings.AutoFightLootFirst;
                Settings.SaveAutoFight();
            });

            Section("── 拾取类型（不勾的不捡） ──", 80);

            AddLootCheck("金币", 10, 104, () => Settings.AutoFightLootGold, v => Settings.AutoFightLootGold = v);
            AddLootCheck("药品", 120, 104, () => Settings.AutoFightLootPotion, v => Settings.AutoFightLootPotion = v);
            AddLootCheck("书籍", 230, 104, () => Settings.AutoFightLootBook, v => Settings.AutoFightLootBook = v);
            AddLootCheck("武器", 340, 104, () => Settings.AutoFightLootWeapon, v => Settings.AutoFightLootWeapon = v);

            AddLootCheck("衣服", 10, 128, () => Settings.AutoFightLootArmour, v => Settings.AutoFightLootArmour = v);
            AddLootCheck("饰品", 120, 128, () => Settings.AutoFightLootJewel, v => Settings.AutoFightLootJewel = v);
            AddLootCheck("其它", 230, 128, () => Settings.AutoFightLootOther, v => Settings.AutoFightLootOther = v);
            AddLootCheck("极品", 340, 128, () => Settings.AutoFightLootTreasure, v => Settings.AutoFightLootTreasure = v);

            Hint("衣服=衣服/头盔/靴子/腰带    饰品=项链/手镯/戒指", 10, 156);
            Hint("极品=带附加属性的物品（地面名字为青色），勾上就不看类型一律捡", 10, 176);
            Hint("回收=周围没怪时放大到该范围回收掉落（0=关闭）", 10, 196);

            // =====================================================================
            //  Tab 3: 职业
            // =====================================================================
            _buildPage = 3;

            // ---- 道士组 ----
            _buildClass = MirClass.Taoist;

            Section("── 道士 ──", Row0);

            _healVal = Click(10, 80, Color.LightCyan, () =>
            {
                Settings.AutoHealEnabled = !Settings.AutoHealEnabled;
                Settings.SaveAutoPot();
            });

            AddNum(80, 80, 2, 0, 99, () => Settings.AutoHealPercent, v => Settings.AutoHealPercent = v);
            Lbl("%", 112, 80);
            Hint("血量低于该百分比自动放治疗术（0=关闭）", 140, 82);

            Section("── 施毒 ──", 108);

            _checkPoisonGreen = new MirCheckBox
            {
                UnTickedIndex = 2086,
                TickedIndex = 2087,
                Index = 2086,
                Library = Libraries.Prguse,
                LabelText = "绿毒",
                Location = new Point(10, 132),
                Parent = this,
                Sound = SoundList.ButtonB,
                Checked = Settings.AutoPoisonGreen,
            };
            Reg(_checkPoisonGreen);
            _checkPoisonGreen.Click += (o, e) =>
            {
                Settings.AutoPoisonGreen = _checkPoisonGreen.Checked;
                Settings.SaveAutoFight();
            };

            _checkPoisonRed = new MirCheckBox
            {
                UnTickedIndex = 2086,
                TickedIndex = 2087,
                Index = 2086,
                Library = Libraries.Prguse,
                LabelText = "红毒",
                Location = new Point(110, 132),
                Parent = this,
                Sound = SoundList.ButtonB,
                Checked = Settings.AutoPoisonRed,
            };
            Reg(_checkPoisonRed);
            _checkPoisonRed.Click += (o, e) =>
            {
                Settings.AutoPoisonRed = _checkPoisonRed.Checked;
                Settings.SaveAutoFight();
            };

            _poisonNowVal = MakeLabel("当前毒药：未装备", 200, 134, Color.LightCyan, 9F);

            Hint("勾选的毒若没装备，会自动从背包装上（需已学施毒术）", 10, 158);

            // ---- 法师组 ----
            _buildClass = MirClass.Wizard;

            Section("── 法师 ──", 182);

            Lbl("自动技能", 10, 206);
            _autoSkillVal = Click(76, 206, Color.LightCyan, () =>
            {
                Settings.AutoSkillEnabled = !Settings.AutoSkillEnabled;
                Settings.SaveAutoFight();
            });

            Lbl("技能", 116, 206);
            _skillNameVal = Click(152, 206, Color.FromArgb(255, 200, 255, 200), ShowSkillPicker);

            Hint("法师专用：选中技能对目标连续释放", 10, 232);

            // 非道士时显示（不加入 _pages，由 ApplyVisibility 单独控制）
            _noClassHint = new MirLabel
            {
                AutoSize = true,
                Font = new System.Drawing.Font(Settings.FontName, 9F),
                ForeColour = ColourHint,
                Location = new Point(10, Row0),
                Parent = this,
                OutLine = false,
                DrawFormat = TextFormatFlags.NoPrefix,
                Visible = false,
                Text = "本职业暂无专属功能（目前支持道士 / 法师）",
            };

            // ---- 收尾 ----
            SelectTab(0);

            BeforeDraw += (o, e) => Refresh();
        }

        // ================= 小控件工厂 =================

        private const int ClassPage = 3;   // 职业 Tab

        private T Reg<T>(T control) where T : MirControl
        {
            if (control != null && _buildPage >= 0 && _buildPage < _pages.Length)
            {
                _pages[_buildPage].Add(control);

                // 职业页按 _buildClass 分组，只有对应职业才显示
                if (_buildPage == ClassPage)
                {
                    if (_buildClass == MirClass.Taoist)
                    {
                        _taoistControls.Add(control);
                    }
                    else if (_buildClass == MirClass.Wizard)
                    {
                        _wizardControls.Add(control);
                        _wizardBaseY[control] = control.Location.Y;
                    }
                }
            }

            return control;
        }

        private MirLabel MakeLabel(string text, int x, int y, Color colour, float size)
        {
            return Reg(new MirLabel
            {
                AutoSize = true,
                Font = new System.Drawing.Font(Settings.FontName, size),
                ForeColour = colour,
                Location = new Point(x, y),
                Parent = this,
                OutLine = false,                                  // 深色底不需要描边，省掉 4 次 DrawText
                DrawFormat = TextFormatFlags.NoPrefix,            // 不要 WordBreak，避开 CJK 断行算法
                Text = text,
            });
        }

        private MirLabel Lbl(string text, int x, int y)
        {
            return MakeLabel(text, x, y, Color.White, 9F);
        }

        private MirLabel Section(string text, int y)
        {
            return MakeLabel(text, 10, y, ColourTitle, 9F);
        }

        private MirLabel Hint(string text, int x, int y)
        {
            return MakeLabel(text, x, y, ColourHint, 8F);
        }

        private MirLabel Click(int x, int y, Color colour, Action onClick)
        {
            MirLabel label = MakeLabel(string.Empty, x, y, colour, 9F);
            label.Sound = SoundList.ButtonA;
            label.Click += (o, e) => onClick();
            return label;
        }

        // ================= Tab 切换 =================

        private void SelectTab(int index)
        {
            if (index < 0 || index >= _pages.Length) return;
            if (_tab == index) return;

            CommitEdit();
            ClosePicker();
            CloseIgnorePicker();
            CloseSkillPicker();

            _tab = index;

            for (int i = 0; i < _tabLabels.Length; i++)
            {
                if (_tabLabels[i] == null) continue;
                _tabLabels[i].ForeColour = i == index ? ColourTabOn : ColourTabOff;
            }

            if (_tabUnderline != null && !_tabUnderline.IsDisposed && index < _tabLabels.Length && _tabLabels[index] != null)
                _tabUnderline.Location = new Point(_tabLabels[index].Location.X, UnderlineY);

            _classCache = CurrentClass;
            ApplyVisibility();
        }

        /// <summary>按「当前 Tab + 当前职业」决定每个控件的可见性</summary>
        /// <summary>职业页里"法师块"的起始 y 与"道士块"起始 y 的差值（道士块隐藏时法师块整体上移）</summary>
        private const int WizardTop = 182;
        private const int ClassTop = Row0;

        private static int CurrentClass
        {
            get { return GameScene.User == null ? -1 : (int)GameScene.User.Class; }
        }

        private void ApplyVisibility()
        {
            int cls = CurrentClass;

            for (int i = 0; i < _pages.Length; i++)
            {
                bool showPage = i == _tab;

                foreach (MirControl control in _pages[i])
                {
                    if (control == null || control.IsDisposed) continue;

                    bool show = showPage;

                    if (show && i == ClassPage)
                    {
                        // 道士专属：只有道士显示
                        if (_taoistControls.Contains(control) && cls != (int)MirClass.Taoist)
                            show = false;

                        // 法师专属：只有法师显示
                        else if (_wizardControls.Contains(control) && cls != (int)MirClass.Wizard)
                            show = false;
                    }

                    control.Visible = show;
                }
            }

            // 道士块隐藏时，把法师块整体上移，避免中间留一大片空白
            int shift = (cls == (int)MirClass.Wizard) ? ClassTop - WizardTop : 0;

            foreach (KeyValuePair<MirControl, int> pair in _wizardBaseY)
            {
                if (pair.Key == null || pair.Key.IsDisposed) continue;
                pair.Key.Location = new Point(pair.Key.Location.X, pair.Value + shift);
            }

            if (_noClassHint != null && !_noClassHint.IsDisposed)
                _noClassHint.Visible = _tab == ClassPage
                                       && cls != (int)MirClass.Taoist
                                       && cls != (int)MirClass.Wizard;
        }

        // ================= 数字字段（共享一个输入框） =================

        /// <summary>拾取类型勾选框（勾选后立刻存盘，和毒药勾选一样的机制）</summary>
        private void AddLootCheck(string text, int x, int y, Func<bool> get, Action<bool> set)
        {
            MirCheckBox box = new MirCheckBox
            {
                UnTickedIndex = 2086,
                TickedIndex = 2087,
                Index = 2086,
                Library = Libraries.Prguse,
                LabelText = text,
                Location = new Point(x, y),
                Parent = this,
                Sound = SoundList.ButtonB,
                Checked = get(),
            };

            Reg(box);

            box.Click += (o, e) =>
            {
                set(box.Checked);
                Settings.SaveAutoFight();
            };

            _lootChecks[box] = get;
        }

        private void AddNum(int x, int y, int digits, int min, int max, Func<int> get, Action<int> set)
        {
            NumField field = new NumField
            {
                Min = min,
                Max = max,
                Digits = digits,
                Get = get,
                Set = set,
                Pos = new Point(x, y),
            };

            field.Label = MakeLabel(get().ToString(), x + 4, y, Color.LightGreen, 9F);
            field.Label.Sound = SoundList.ButtonA;
            field.Label.Click += (o, e) => BeginEdit(field);

            _fields.Add(field);
        }

        private void BeginEdit(NumField field)
        {
            if (field == null || _editing == field) return;

            CommitEdit();

            _editing = field;
            field.Label.Visible = false;

            _editBox.MaxLength = field.Digits;
            _editBox.Size = new Size(field.Digits * 9 + 10, 16);
            _editBox.Location = field.Pos;
            _editBox.Text = field.Get().ToString();
            _editBox.Visible = true;
            _editBox.SetFocus();
        }

        private void CommitEdit()
        {
            NumField field = _editing;
            if (field == null) return;

            _editing = null;

            int v;
            if (int.TryParse(_editBox.Text, out v))
            {
                if (v < field.Min) v = field.Min;
                if (v > field.Max) v = field.Max;

                field.Set(v);
                Settings.SaveAutoFight();
                Settings.SaveAutoPot();
            }

            _editBox.Visible = false;
            field.Label.Visible = _tab == IndexOfPage(field);
            field.Label.Text = field.Get().ToString();
        }

        private int IndexOfPage(NumField field)
        {
            for (int i = 0; i < _pages.Length; i++)
                if (_pages[i].Contains(field.Label)) return i;

            return _tab;
        }

        // ================= 打开/关闭 =================

        protected override void OnVisibleChanged()
        {
            base.OnVisibleChanged();

            if (!Visible)
            {
                if (_editing != null) CommitEdit();
                if (_editBox != null && !_editBox.IsDisposed) _editBox.Visible = false;
                ClosePicker();
                CloseIgnorePicker();
                CloseSkillPicker();
            }
        }

        // ================= 不攻击怪物名单编辑器 =================

        private MirControl _ignorePicker;

        private void CloseIgnorePicker()
        {
            if (_ignorePicker != null)
            {
                _ignorePicker.Dispose();
                _ignorePicker = null;
            }
        }

        private void ShowIgnorePicker()
        {
            bool wasOpen = _ignorePicker != null && !_ignorePicker.IsDisposed;

            CloseIgnorePicker();
            if (wasOpen) return;

            BuildIgnorePicker();
        }

        private void BuildIgnorePicker()
        {
            CloseIgnorePicker();

            UserObject user = GameScene.Scene == null ? null : GameScene.User;

            List<string> current = Settings.GetIgnoreMonsters();

            // 附近可见、但还没加入名单的怪
            List<string> nearby = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (user != null)
            {
                foreach (MapObject ob in MapControl.ObjectsList)
                {
                    if (!(ob is MonsterObject)) continue;
                    if (string.IsNullOrEmpty(ob.Name)) continue;
                    if (!Functions.InRange(user.CurrentLocation, ob.CurrentLocation, 20)) continue;
                    if (Settings.IsMonsterIgnored(ob.Name)) continue;
                    if (!seen.Add(ob.Name)) continue;

                    nearby.Add(ob.Name);
                }
            }

            const int maxRows = 10;
            int hiddenCurrent = Math.Max(0, current.Count - maxRows);
            if (current.Count > maxRows) current.RemoveRange(maxRows, current.Count - maxRows);

            int hiddenNearby = Math.Max(0, nearby.Count - maxRows);
            if (nearby.Count > maxRows) nearby.RemoveRange(maxRows, nearby.Count - maxRows);

            int rows = 3
                     + Math.Max(1, current.Count) + (hiddenCurrent > 0 ? 1 : 0)
                     + Math.Max(1, nearby.Count) + (hiddenNearby > 0 ? 1 : 0);

            int w = 300;
            int h = 10 + rows * 20 + 10;

            MirControl owner = Parent as MirControl;
            if (owner == null) owner = this;

            _ignorePicker = new MirControl
            {
                BackColour = Color.FromArgb(255, 22, 20, 16),
                Border = true,
                BorderColour = Color.FromArgb(255, 168, 138, 84),
                DrawControlTexture = true,
                Location = new Point((Settings.ScreenWidth - w) / 2, (Settings.ScreenHeight - h) / 2),
                Size = new Size(w, h),
                Sort = true,
                Parent = owner,
            };

            int y = 8;

            AddIgnoreText("不攻击的怪物（点击切换）", ColourTitle, ref y);
            AddIgnoreText("── 已加入（点击移除）──", Color.Silver, ref y);

            if (current.Count == 0) AddIgnoreText("（还没有添加）", Color.Gray, ref y);

            foreach (string name in current)
            {
                string captured = name;
                AddIgnoreRow(captured, Color.FromArgb(255, 255, 160, 160), ref y, () =>
                {
                    Settings.ToggleIgnoreMonster(captured);
                    BuildIgnorePicker();
                });
            }

            if (hiddenCurrent > 0)
                AddIgnoreText("…还有 " + hiddenCurrent + " 个（可直接改 ini 的 IgnoreMonsters）", Color.Gray, ref y);

            AddIgnoreText("── 附近可见（点击加入）──", Color.Silver, ref y);

            if (nearby.Count == 0) AddIgnoreText("（附近没有其它怪物）", Color.Gray, ref y);

            foreach (string name in nearby)
            {
                string captured = name;
                AddIgnoreRow(captured, Color.LightCyan, ref y, () =>
                {
                    Settings.ToggleIgnoreMonster(captured);
                    BuildIgnorePicker();
                });
            }

            if (hiddenNearby > 0)
                AddIgnoreText("…还有 " + hiddenNearby + " 种没显示（走近一点再打开）", Color.Gray, ref y);

            _ignorePicker.BringToFront();
        }

        private void AddIgnoreText(string text, Color colour, ref int y)
        {
            if (_ignorePicker == null) return;

            new MirLabel
            {
                AutoSize = true,
                Font = new System.Drawing.Font(Settings.FontName, 9F),
                ForeColour = colour,
                Location = new Point(10, y),
                Parent = _ignorePicker,
                OutLine = false,
                DrawFormat = TextFormatFlags.NoPrefix,
                Text = text,
            };

            y += 20;
        }

        private void AddIgnoreRow(string text, Color colour, ref int y, Action onClick)
        {
            if (_ignorePicker == null) return;

            MirLabel label = new MirLabel
            {
                AutoSize = true,
                Font = new System.Drawing.Font(Settings.FontName, 9F),
                ForeColour = colour,
                Location = new Point(18, y),
                Parent = _ignorePicker,
                OutLine = false,
                DrawFormat = TextFormatFlags.NoPrefix,
                Sound = SoundList.ButtonA,
                Text = text,
            };

            label.Click += (o, e) =>
            {
                onClick();
                Refresh();
            };

            y += 20;
        }

        // ================= 法师技能选择 =================

        private MirControl _skillPicker;

        private void CloseSkillPicker()
        {
            if (_skillPicker != null)
            {
                _skillPicker.Dispose();
                _skillPicker = null;
            }
        }

        private void ShowSkillPicker()
        {
            bool wasOpen = _skillPicker != null && !_skillPicker.IsDisposed;

            CloseSkillPicker();
            if (wasOpen) return;

            BuildSkillPicker();
        }

        private void BuildSkillPicker()
        {
            CloseSkillPicker();

            UserObject user = GameScene.Scene == null ? null : GameScene.User;

            List<ClientMagic> magics = new List<ClientMagic>();
            if (user != null && user.Magics != null)
                foreach (ClientMagic m in user.Magics)
                    if (m != null) magics.Add(m);

            magics.Sort((a, b) => a.Spell.CompareTo(b.Spell));

            const int maxRows = 14;
            int hidden = Math.Max(0, magics.Count - maxRows);
            if (magics.Count > maxRows) magics.RemoveRange(maxRows, magics.Count - maxRows);

            int rows = 3 + Math.Max(1, magics.Count) + (hidden > 0 ? 1 : 0);
            int w = 300;
            int h = 10 + rows * 20 + 10;

            MirControl owner = Parent as MirControl;
            if (owner == null) owner = this;

            _skillPicker = new MirControl
            {
                BackColour = Color.FromArgb(255, 22, 20, 16),
                Border = true,
                BorderColour = Color.FromArgb(255, 168, 138, 84),
                DrawControlTexture = true,
                Location = new Point((Settings.ScreenWidth - w) / 2, (Settings.ScreenHeight - h) / 2),
                Size = new Size(w, h),
                Sort = true,
                Parent = owner,
            };

            int y = 8;

            AddSkillText("选择技能（点击设置）", ColourTitle, ref y);
            AddSkillText("── 已学魔法 ──", Color.Silver, ref y);

            AddSkillRow("不使用（清除）", Color.FromArgb(255, 255, 160, 160), ref y, () =>
            {
                Settings.AutoSkillSpell = 0;
                Settings.SaveAutoFight();
            });

            if (magics.Count == 0)
                AddSkillText("（这个角色还没有学会任何魔法）", Color.Gray, ref y);

            foreach (ClientMagic magic in magics)
            {
                ClientMagic captured = magic;
                string text = captured.Name + "  Lv" + captured.Level;

                bool selected = Settings.AutoSkillSpell == (int)captured.Spell;

                AddSkillRow(text, selected ? Color.Yellow : Color.LightCyan, ref y, () =>
                {
                    Settings.AutoSkillSpell = (int)captured.Spell;
                    Settings.SaveAutoFight();
                });
            }

            if (hidden > 0)
                AddSkillText("…还有 " + hidden + " 个没显示", Color.Gray, ref y);

            _skillPicker.BringToFront();
        }

        private void AddSkillText(string text, Color colour, ref int y)
        {
            if (_skillPicker == null) return;

            new MirLabel
            {
                AutoSize = true,
                Font = new System.Drawing.Font(Settings.FontName, 9F),
                ForeColour = colour,
                Location = new Point(10, y),
                Parent = _skillPicker,
                OutLine = false,
                DrawFormat = TextFormatFlags.NoPrefix,
                Text = text,
            };

            y += 20;
        }

        private void AddSkillRow(string text, Color colour, ref int y, Action onClick)
        {
            if (_skillPicker == null) return;

            MirLabel label = new MirLabel
            {
                AutoSize = true,
                Font = new System.Drawing.Font(Settings.FontName, 9F),
                ForeColour = colour,
                Location = new Point(18, y),
                Parent = _skillPicker,
                OutLine = false,
                DrawFormat = TextFormatFlags.NoPrefix,
                Sound = SoundList.ButtonA,
                Text = text,
            };

            label.Click += (o, e) =>
            {
                onClick();
                CloseSkillPicker();
                Refresh();
            };

            y += 20;
        }

        // ================= 药水选择 =================

        private MirControl _picker;
        private Stat _pickerStat;

        private void ClosePicker()
        {
            if (_picker != null)
            {
                _picker.Dispose();
                _picker = null;
            }
        }

        private void ShowPicker(Stat stat)
        {
            bool wasOpen = _picker != null && !_picker.IsDisposed && _pickerStat == stat;
            ClosePicker();
            if (wasOpen) return;

            UserObject user = GameScene.Scene == null ? null : GameScene.User;
            var list = user == null ? new List<UserItem>() : AutoPotion.GetUsablePotions(user, stat);

            _pickerStat = stat;

            const int rowH = 20;
            int rows = 1 + list.Count + (list.Count == 0 ? 1 : 0);
            int w = 300;
            int h = 24 + rows * rowH + 4;

            MirControl owner = Parent as MirControl;
            if (owner == null) owner = this;

            _picker = new MirControl
            {
                BackColour = Color.FromArgb(255, 22, 20, 16),
                Border = true,
                BorderColour = Color.FromArgb(255, 168, 138, 84),
                DrawControlTexture = true,
                Location = new Point((Settings.ScreenWidth - w) / 2, (Settings.ScreenHeight - h) / 2),
                Size = new Size(w, h),
                Sort = true,
                Parent = owner,
            };

            new MirLabel
            {
                AutoSize = true,
                Font = new System.Drawing.Font(Settings.FontName, 9F),
                ForeColour = ColourTitle,
                Location = new Point(8, 4),
                Parent = _picker,
                OutLine = false,
                DrawFormat = TextFormatFlags.NoPrefix,
                Text = (stat == Stat.HP ? "HP" : "MP") + " 使用哪种药水",
            };

            int row = 0;
            AddPickRow("不使用（清除设置）", Color.Silver, row++, () => ApplyPotion(stat, 0));

            foreach (UserItem potion in list)
            {
                int index = potion.Info.Index;
                string name = potion.FriendlyName;
                AddPickRow(name, Color.White, row++, () => ApplyPotion(stat, index));
            }

            if (list.Count == 0)
                AddPickRow(GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AutoPotNoPotion), Color.Gray, row++, null);

            _picker.BringToFront();
        }

        private void AddPickRow(string text, Color colour, int row, Action onClick)
        {
            if (_picker == null) return;

            MirLabel label = new MirLabel
            {
                AutoSize = true,
                Font = new System.Drawing.Font(Settings.FontName, 9F),
                ForeColour = colour,
                Location = new Point(12, 26 + row * 20),
                Parent = _picker,
                OutLine = false,
                DrawFormat = TextFormatFlags.NoPrefix,
                Sound = onClick != null ? SoundList.ButtonA : SoundList.None,
                Text = text,
            };

            if (onClick != null)
            {
                label.Click += (o, e) =>
                {
                    onClick();
                    ClosePicker();
                };
            }
        }

        private void ApplyPotion(Stat stat, int itemIndex)
        {
            if (stat == Stat.HP) Settings.AutoPotHPItem = itemIndex;
            else Settings.AutoPotMPItem = itemIndex;

            Settings.SaveAutoPot();
            Refresh();
        }

        public override void Hide()
        {
            ClosePicker();
            CloseIgnorePicker();
            CloseSkillPicker();
            base.Hide();
        }

        // ================= 刷新 =================

        private static void SetText(MirLabel label, string text)
        {
            if (label == null || label.IsDisposed || label.Text == text) return;
            label.Text = text;
        }

        private static string PotionText(UserObject user, int itemIndex)
        {
            if (itemIndex <= 0)
                return GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AutoPotNotSet);

            UserItem potion = AutoPotion.FindPotion(user, itemIndex);
            if (potion == null)
                return GameLanguage.ClientTextMap.GetLocalization(ClientTextKeys.AutoPotNoPotion);

            return potion.FriendlyName;
        }

        /// <summary>当前装备槽里的"毒"是什么（决定施毒术实际会放哪种毒）</summary>
        private static string PoisonEquippedText(UserObject user)
        {
            if (user == null) return "未装备";

            UserItem item = user.Equipment[(int)EquipmentSlot.Amulet];
            if (item == null || item.Info == null) return "未装备";
            if (item.Info.Type != ItemType.Amulet) return "非毒药";

            switch (item.Info.Shape)
            {
                case 1: return "绿毒";
                case 2: return "红毒";
                default: return "护身符（不能施毒）";
            }
        }

        /// <summary>选中的法师技能名</summary>
        private static string SkillNameText(UserObject user)
        {
            if (Settings.AutoSkillSpell <= 0) return "未选择";
            if (user == null) return "未选择";

            ClientMagic magic = user.GetMagic((Spell)Settings.AutoSkillSpell);
            return magic == null ? "未学习" : magic.Name;
        }

        private static string YN(bool v)
        {
            return v ? "是" : "否";
        }

        public void Refresh()
        {
            UserObject user = GameScene.Scene == null ? null : GameScene.User;

            // 数字字段（正在编辑的那个不要覆盖）
            for (int i = 0; i < _fields.Count; i++)
            {
                NumField field = _fields[i];
                if (field == _editing) continue;

                SetText(field.Label, field.Get().ToString());
            }

            SetText(_enabledVal, Settings.AutoFightEnabled ? "已开启（点击关闭）" : "已关闭（点击开启）");

            SetText(_priorityVal, Settings.AutoFightPriority == 0 ? "最近" : "血最少");
            SetText(_ignorePlayersVal, YN(Settings.AutoFightIgnorePlayers));
            SetText(_ignoreListVal, "不攻击名单(" + Settings.IgnoreMonsterCount + ")");
            SetText(_roamVal, YN(Settings.AutoFightRoam));
            SetText(_chaseVal, YN(Settings.AutoFightChase));
            SetText(_basicAttackVal, YN(Settings.AutoFightBasicAttack));
            SetText(_keepVal, YN(Settings.AutoFightKeepDistance));
            SetText(_losVal, YN(Settings.AutoFightLineOfSight));

            SetText(_autoPotVal, Settings.AutoPotEnabled ? "是（点击关闭）" : "否（点击开启）");
            SetText(_hpPot, PotionText(user, Settings.AutoPotHPItem));
            SetText(_mpPot, PotionText(user, Settings.AutoPotMPItem));

            SetText(_healVal, "治疗术：" + YN(Settings.AutoHealEnabled));

            // 毒药是装备在 Amulet 槽的道具，类型由 Shape 决定；勾选框要和实际设置保持一致
            if (_checkPoisonGreen != null && !_checkPoisonGreen.IsDisposed && _checkPoisonGreen.Checked != Settings.AutoPoisonGreen)
                _checkPoisonGreen.Checked = Settings.AutoPoisonGreen;

            if (_checkPoisonRed != null && !_checkPoisonRed.IsDisposed && _checkPoisonRed.Checked != Settings.AutoPoisonRed)
                _checkPoisonRed.Checked = Settings.AutoPoisonRed;

            SetText(_poisonNowVal, "当前毒药：" + PoisonEquippedText(user));

            SetText(_autoSkillVal, YN(Settings.AutoSkillEnabled));
            SetText(_skillNameVal, SkillNameText(user));

            // 角色职业变了（切角色/第一次进游戏）→ 重算职业页的可见性与位置
            if (_classCache != CurrentClass)
            {
                _classCache = CurrentClass;
                ApplyVisibility();
            }

            SetText(_lootVal, YN(Settings.AutoFightLoot));

            // 拾取类型勾选框：换角色/读档后要和设置对齐
            foreach (KeyValuePair<MirCheckBox, Func<bool>> pair in _lootChecks)
            {
                if (pair.Key == null || pair.Key.IsDisposed) continue;

                bool want = pair.Value();
                if (pair.Key.Checked != want) pair.Key.Checked = want;
            }

            SetText(_lootFirstVal, YN(Settings.AutoFightLootFirst));

            string status = string.IsNullOrEmpty(AutoFight.Status) ? "状态：空闲" : AutoFight.Status;

            // 内挂开着、却忘了开自动喝药 → 在状态栏上明确提示一句
            if (Settings.AutoFightEnabled && !Settings.AutoPotEnabled)
                status += "    （自动喝药未开启）";

            SetText(_statusVal, status);
        }
    }
}
