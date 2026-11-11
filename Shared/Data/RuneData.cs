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
