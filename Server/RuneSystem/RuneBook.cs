using System;
using System.IO;

namespace Server.RuneSystem
{
    /// <summary>
    /// 角色的符文状态（存在 CharacterInfo.Runes 里）。
    /// 自带版本头：以后给符文状态加字段，只改本文件，不需要再动 CharacterInfo 的存档格式。
    /// </summary>
    public class RuneBook
    {
        private const byte CurrentVersion = 1;

        /// <summary>每个技能当前选择的符文（0 / 不存在 = 无符文）。</summary>
        public readonly Dictionary<Spell, ushort> Selected = new Dictionary<Spell, ushort>();

        /// <summary>已解锁的符文（等级解锁是算出来的，这里只存"额外解锁"来源，为后续道具/任务预留）。</summary>
        public readonly HashSet<ushort> Unlocked = new HashSet<ushort>();

        public RuneBook() { }

        public RuneBook(BinaryReader reader)
        {
            byte version = reader.ReadByte();

            Selected.Clear();
            int count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var spell = (Spell)reader.ReadByte();
                ushort runeId = reader.ReadUInt16();
                if (runeId == 0) continue;
                Selected[spell] = runeId;
            }

            Unlocked.Clear();
            count = reader.ReadInt32();
            for (int i = 0; i < count; i++)
                Unlocked.Add(reader.ReadUInt16());

            // 未来版本新增字段时在这里按 version 分支读取（不要改变已存在字段的顺序）
        }

        public void Save(BinaryWriter writer)
        {
            writer.Write(CurrentVersion);

            writer.Write(Selected.Count);
            foreach (var pair in Selected)
            {
                writer.Write((byte)pair.Key);
                writer.Write(pair.Value);
            }

            writer.Write(Unlocked.Count);
            foreach (var id in Unlocked)
                writer.Write(id);
        }

        public ushort Get(Spell spell)
        {
            return Selected.TryGetValue(spell, out ushort id) ? id : (ushort)0;
        }

        public void Set(Spell spell, ushort runeId)
        {
            if (runeId == 0)
                Selected.Remove(spell);
            else
                Selected[spell] = runeId;
        }
    }
}
