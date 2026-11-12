using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// 符文定义（下发客户端用于渲染符文面板）。服务器端 RuneInfo 是它的超集，
/// 这里只带展示层需要的字段 —— 客户端不参与任何数值计算。
/// </summary>
public class ClientRuneInfo
{
    public ushort Id;
    public Spell Spell;
    public byte Slot;
    public byte Icon;
    public byte NeedMagicLevel;
    public string Name = string.Empty;
    public string Description = string.Empty;
    /// <summary>技能特效放大倍数（1 = 原版；仅客户端渲染用）。</summary>
    public float EffectScale = 1F;
    /// <summary>技能特效着色（ARGB，0 = 不染色）。例如「雷电·烈焰」把雷电画成火红色。</summary>
    public int TintArgb;
    /// <summary>
    /// 被动属性加成（Stat 名 → 各技能等级的值）。客户端只用来同步显示（背包负重条等），服务端才是权威。
    /// </summary>
    public Dictionary<string, int[]> StatBonus;

    public ClientRuneInfo() { }

    public ClientRuneInfo(BinaryReader reader)
    {
        Id = reader.ReadUInt16();
        Spell = (Spell)reader.ReadByte();
        Slot = reader.ReadByte();
        Icon = reader.ReadByte();
        NeedMagicLevel = reader.ReadByte();
        Name = reader.ReadString();
        Description = reader.ReadString();
        EffectScale = reader.ReadSingle();
        TintArgb = reader.ReadInt32();

        int statCount = reader.ReadByte();
        if (statCount > 0)
        {
            StatBonus = new Dictionary<string, int[]>(statCount);
            for (int i = 0; i < statCount; i++)
            {
                string key = reader.ReadString();
                int valueCount = reader.ReadByte();
                int[] values = new int[valueCount];
                for (int j = 0; j < valueCount; j++) values[j] = reader.ReadInt32();
                StatBonus[key] = values;
            }
        }
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write(Id);
        writer.Write((byte)Spell);
        writer.Write(Slot);
        writer.Write(Icon);
        writer.Write(NeedMagicLevel);
        writer.Write(Name ?? string.Empty);
        writer.Write(Description ?? string.Empty);
        writer.Write(EffectScale <= 0F ? 1F : EffectScale);
        writer.Write(TintArgb);

        if (StatBonus == null || StatBonus.Count == 0)
        {
            writer.Write((byte)0);
        }
        else
        {
            writer.Write((byte)Math.Min(255, StatBonus.Count));
            foreach (var pair in StatBonus)
            {
                writer.Write(pair.Key ?? string.Empty);
                int[] values = pair.Value ?? new int[0];
                writer.Write((byte)Math.Min(255, values.Length));
                foreach (int value in values) writer.Write(value);
            }
        }
    }
}

/// <summary>某个技能当前选择的符文（0 = 无符文）。</summary>
public class ClientRuneState
{
    public Spell Spell;
    public ushort RuneId;

    public ClientRuneState() { }

    public ClientRuneState(Spell spell, ushort runeId)
    {
        Spell = spell;
        RuneId = runeId;
    }

    public ClientRuneState(BinaryReader reader)
    {
        Spell = (Spell)reader.ReadByte();
        RuneId = reader.ReadUInt16();
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write((byte)Spell);
        writer.Write(RuneId);
    }
}
