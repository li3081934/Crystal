using System;
using System.Collections.Generic;
using System.Drawing;

/// <summary>
/// 一条已生成的词条实例，随 UserItem 一起存档/下发。
/// </summary>
public sealed class AffixInstance
{
    public ushort Id;
    public byte Tier;
    public Stat Stat;
    public short Value;
    /// <summary>
    /// 保留位（随物品存档/下发的记录布局保持稳定，避免给 119 版存档再加一次版本迁移）。
    /// 预留给“词条来源/是否锁定”等后续扩展，当前没有任何逻辑读写。
    /// </summary>
    public byte Flags;
}

/// <summary>
/// 服务端词条定义。由 Envir/Affix/Affixes.json 反序列化，或由 AffixRepository 内置默认表生成。
/// </summary>
public sealed class AffixInfo
{
    public ushort Id;
    public string Name;
    public string NameKey;
    public bool Suffix;
    public byte Tier;
    public int MinIlvl;
    public string Group;
    public Stat Stat;
    public int MinValue;
    public int MaxValue;
    public ItemType[] Slots;
    public int Weight;
    public ushort[] Excludes;
    public int MaxPerItem;
    public string Tags;
}

/// <summary>
/// 客户端展示用的最小词条信息，只下发 Id/名称/档位/属性/前后缀。
/// </summary>
public sealed class ClientAffixInfo
{
    public ushort Id;
    public string Name;
    public string NameKey;
    public byte Tier;
    public Stat Stat;
    public bool Suffix;
}

/// <summary>
/// 词条属性的读写辅助。服务端与客户端共用，避免两边各写一套加成逻辑。
/// </summary>
public static class AffixStats
{
    /// <summary>
    /// 词条档位色板，采用魔兽世界式的品质色阶：绿 → 蓝 → 紫 → 橙 → 红。
    /// 服务端地面掉落物与客户端 tooltip 共用这一套颜色。
    /// </summary>
    public static Color TierColor(byte tier)
    {
        switch (tier)
        {
            case 1:
                return Color.FromArgb(30, 255, 0);    // #1EFF00 绿
            case 2:
                return Color.FromArgb(0, 112, 221);   // #0070DD 蓝
            case 3:
                return Color.FromArgb(163, 53, 238);  // #A335EE 紫
            case 4:
                return Color.FromArgb(255, 128, 0);   // #FF8000 橙
            case 5:
                return Color.FromArgb(255, 0, 0);     // #FF0000 红（最高档）
            default:
                return Color.White;
        }
    }

    public static byte HighestTier(UserItem item)
    {
        if (item == null || item.Affixes == null || item.Affixes.Count == 0) return 0;

        byte tier = 0;
        for (int i = 0; i < item.Affixes.Count; i++)
        {
            AffixInstance affix = item.Affixes[i];
            if (affix != null && affix.Tier > tier) tier = affix.Tier;
        }
        return tier;
    }

    public static void Apply(UserItem item, Stats stats)
    {
        if (item == null || item.Affixes == null || stats == null) return;

        for (int i = 0; i < item.Affixes.Count; i++)
        {
            AffixInstance affix = item.Affixes[i];
            if (affix == null) continue;
            stats[affix.Stat] += affix.Value;
        }
    }

    public static int Sum(UserItem item, Stat stat)
    {
        if (item == null || item.Affixes == null) return 0;

        int total = 0;
        for (int i = 0; i < item.Affixes.Count; i++)
        {
            AffixInstance affix = item.Affixes[i];
            if (affix != null && affix.Stat == stat) total += affix.Value;
        }
        return total;
    }
}
