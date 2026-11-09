using System;
using System.Drawing;
using Client.MirControls;
using Client.MirGraphics;
using Client.MirScenes;
using Client.MirSounds;

namespace Client.RuneSystem
{
    /// <summary>
    /// 暗黑3 风格的符文选择面板。
    /// 纯数据驱动：符文列表、名称、描述、解锁条件全部来自服务器下发的 ClientRuneInfo，
    /// 新增符文不需要改这个文件。
    /// </summary>
    public sealed class RuneDialog : MirControl
    {
        private const int RowHeight = 42;
        private const int RowWidth = 328;
        private const int MaxVisibleRows = 5;

        private static RuneDialog _instance;

        private readonly ClientMagic _magic;
        private Spell _spell;

        private readonly MirLabel _titleLabel;
        private readonly MirLabel _subTitleLabel;
        private readonly MirControl[] _rows = new MirControl[MaxVisibleRows];
        private readonly MirLabel[] _rowName = new MirLabel[MaxVisibleRows];
        private readonly MirLabel[] _rowState = new MirLabel[MaxVisibleRows];

        private RuneDialog(ClientMagic magic, Spell spell)
        {
            _magic = magic;
            _spell = spell;

            Size = new Size(RowWidth + 24, 108 + RowHeight * MaxVisibleRows);
            BackColour = Color.FromArgb(240, 18, 20, 26);
            Border = true;
            BorderColour = Color.FromArgb(255, 120, 96, 52);
            DrawControlTexture = true;

            Modal = true;
            Movable = true;
            Parent = GameScene.Scene;
            Location = Center;
            Visible = false;

            _titleLabel = new MirLabel
            {
                Parent = this,
                NotControl = true,
                Location = new Point(12, 10),
                Size = new Size(RowWidth, 20),
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                Font = new Font(Settings.FontName, 10F, FontStyle.Bold),
                ForeColour = Color.FromArgb(255, 255, 220, 140),
            };

            _subTitleLabel = new MirLabel
            {
                Parent = this,
                NotControl = true,
                Location = new Point(12, 32),
                Size = new Size(RowWidth, 18),
                DrawFormat = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter,
                Font = new Font(Settings.FontName, 8F),
                ForeColour = Color.FromArgb(255, 190, 190, 190),
            };

            for (int i = 0; i < MaxVisibleRows; i++)
            {
                int index = i;

                _rows[i] = new MirControl
                {
                    Parent = this,
                    Location = new Point(12, 58 + i * RowHeight),
                    Size = new Size(RowWidth, RowHeight - 4),
                    BackColour = Color.FromArgb(200, 30, 32, 40),
                    Border = true,
                    BorderColour = Color.FromArgb(120, 90, 90, 110),
                    DrawControlTexture = true,
                    Visible = false,
                };
                _rows[i].Click += (o, e) => Select(index);

                _rowName[i] = new MirLabel
                {
                    Parent = _rows[i],
                    NotControl = true,
                    Location = new Point(8, 4),
                    Size = new Size(RowWidth - 110, 18),
                    Font = new Font(Settings.FontName, 9F, FontStyle.Bold),
                    ForeColour = Color.White,
                };

                _rowState[i] = new MirLabel
                {
                    Parent = _rows[i],
                    NotControl = true,
                    Location = new Point(RowWidth - 104, 4),
                    Size = new Size(96, 18),
                    DrawFormat = TextFormatFlags.Right | TextFormatFlags.VerticalCenter,
                    Font = new Font(Settings.FontName, 8F),
                    ForeColour = Color.FromArgb(255, 150, 220, 150),
                };
            }

            var noneButton = new MirButton
            {
                Index = 1656,
                HoverIndex = 1657,
                PressedIndex = 1658,
                Library = Libraries.Prguse,
                Parent = this,
                Location = new Point(12, Size.Height - 40),
                Size = new Size(120, 26),
                Sound = SoundList.ButtonA,
                Text = "移除符文",
            };
            noneButton.Click += (o, e) => { RuneLibrary.Send(_spell, 0); Hide(); };

            var closeButton = new MirButton
            {
                Index = 1656,
                HoverIndex = 1657,
                PressedIndex = 1658,
                Library = Libraries.Prguse,
                Parent = this,
                Location = new Point(Size.Width - 132, Size.Height - 40),
                Size = new Size(120, 26),
                Sound = SoundList.ButtonA,
                Text = "关闭",
            };
            closeButton.Click += (o, e) => Hide();

            Refresh();
        }

        public static void Open(ClientMagic magic)
        {
            if (magic == null) return;
            if (!RuneLibrary.CanSelectAny(magic.Spell)) return;

            if (_instance != null && !_instance.IsDisposed)
                _instance.Dispose();

            _instance = new RuneDialog(magic, magic.Spell);
            _instance.Show();
            _instance.Refresh();
        }

        public static void Close()
        {
            if (_instance == null || _instance.IsDisposed) return;
            _instance.Dispose();
            _instance = null;
        }

        public override void Show()
        {
            if (Visible) return;
            Visible = true;
            BringToFront();
        }

        public override void Hide()
        {
            if (!Visible) return;
            Visible = false;
            Close();
        }

        public void Refresh()
        {
            if (IsDisposed) return;

            _titleLabel.Text = string.Format("符文 · {0}", _magic?.Name ?? _spell.ToString());

            var runes = RuneLibrary.ForSpell(_spell);
            var selected = RuneLibrary.GetSelected(_spell);

            _subTitleLabel.Text = selected == null
                ? "当前未选择符文"
                : string.Format("当前：{0}", selected.Name);

            for (int i = 0; i < MaxVisibleRows; i++)
            {
                if (i >= runes.Count)
                {
                    _rows[i].Visible = false;
                    continue;
                }

                var rune = runes[i];
                bool unlocked = RuneLibrary.IsUnlocked(rune);
                bool isSelected = selected != null && selected.Id == rune.Id;

                _rows[i].Visible = true;
                _rows[i].BackColour = isSelected
                    ? Color.FromArgb(230, 70, 55, 20)
                    : Color.FromArgb(200, 30, 32, 40);
                _rows[i].BorderColour = isSelected
                    ? Color.FromArgb(255, 220, 180, 90)
                    : Color.FromArgb(120, 90, 90, 110);
                _rows[i].Redraw();

                _rowName[i].Text = rune.Name;
                _rowName[i].ForeColour = unlocked ? Color.White : Color.FromArgb(255, 130, 130, 130);

                _rowState[i].Text = !unlocked
                    ? string.Format("需要技能 {0} 级", rune.NeedMagicLevel)
                    : isSelected ? "已选择" : "点击选择";
                _rowState[i].ForeColour = !unlocked
                    ? Color.FromArgb(255, 220, 120, 120)
                    : isSelected ? Color.FromArgb(255, 255, 220, 140) : Color.FromArgb(255, 150, 220, 150);

                _rows[i].Hint = string.IsNullOrEmpty(rune.Description) ? rune.Name : rune.Description;
            }
        }

        private void Select(int index)
        {
            var runes = RuneLibrary.ForSpell(_spell);
            if (index < 0 || index >= runes.Count) return;

            var rune = runes[index];
            if (!RuneLibrary.IsUnlocked(rune)) return;

            RuneLibrary.Send(_spell, rune.Id);
            Hide();
        }

        protected override void Dispose(bool disposing)
        {
            if (_instance == this) _instance = null;

            base.Dispose(disposing);
        }
    }
}
