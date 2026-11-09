using System;
using System.Collections.Generic;
using System.Linq;
using Client.MirControls;
using Client.MirScenes;
using Client.MirScenes.Dialogs;
using Client.MirNetwork;
using S = ServerPackets;

namespace Client.RuneSystem
{
    /// <summary>
    /// 客户端符文数据缓存。只负责展示，不参与任何数值计算。
    /// 数据来自 S.RuneList（登录时下发）与 S.SetRune（选择/切换时下发）。
    /// </summary>
    public static class RuneLibrary
    {
        public static readonly Dictionary<ushort, ClientRuneInfo> Runes = new Dictionary<ushort, ClientRuneInfo>();
        public static readonly Dictionary<Spell, ushort> Selected = new Dictionary<Spell, ushort>();

        public static bool Any => Runes.Count > 0;

        public static void OnRuneList(S.RuneList p)
        {
            Runes.Clear();
            Selected.Clear();

            if (p.Runes != null)
            {
                foreach (var rune in p.Runes)
                {
                    if (rune == null) continue;
                    Runes[rune.Id] = rune;
                }
            }

            if (p.States != null)
            {
                foreach (var state in p.States)
                {
                    if (state == null || state.RuneId == 0) continue;
                    Selected[state.Spell] = state.RuneId;
                }
            }

            RefreshUi();
        }

        public static void OnSetRune(S.SetRune p)
        {
            if (p.RuneId == 0)
                Selected.Remove(p.Spell);
            else
                Selected[p.Spell] = p.RuneId;

            RefreshUi();
        }

        public static void RefreshUi()
        {
            try
            {
                var scene = GameScene.Scene;
                if (scene == null) return;

                if (scene.CharacterDialog != null && scene.CharacterDialog.Visible)
                    scene.CharacterDialog.RefreshInterface();

                foreach (var bar in scene.SkillBarDialogs)
                    bar.Update();
            }
            catch
            {
                // UI 尚未初始化时忽略（登录早期）
            }
        }

        public static ClientRuneInfo Get(ushort id)
        {
            return Runes.TryGetValue(id, out var rune) ? rune : null;
        }

        public static ClientRuneInfo GetSelected(Spell spell)
        {
            return Selected.TryGetValue(spell, out var id) ? Get(id) : null;
        }

        public static string GetSelectedName(Spell spell)
        {
            return GetSelected(spell)?.Name ?? string.Empty;
        }

        public static List<ClientRuneInfo> ForSpell(Spell spell)
        {
            return Runes.Values.Where(x => x.Spell == spell).OrderBy(x => x.Slot).ToList();
        }

        /// <summary>解锁判定与服务器一致：拥有该技能，且技能等级达到要求。</summary>
        public static bool IsUnlocked(ClientRuneInfo rune)
        {
            if (rune == null) return false;

            var magic = GameScene.User?.GetMagic(rune.Spell);
            if (magic == null) return false;

            return magic.Level >= rune.NeedMagicLevel;
        }

        public static bool CanSelectAny(Spell spell)
        {
            return Any && ForSpell(spell).Count > 0;
        }

        public static void Send(Spell spell, ushort runeId)
        {
            Network.Enqueue(new ClientPackets.SetRune { Spell = spell, RuneId = runeId });
        }
    }
}
