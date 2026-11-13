public class BaseStats
{
    public MirClass Job;
    public List<BaseStat> Stats = new List<BaseStat>();

    public Stats Caps = new Stats();

    public BaseStats(MirClass job)
    {
        Job = job;

        switch (job)
        {
            #region Warrior
            case MirClass.Warrior:
                Stats.Add(new BaseStat(Stat.HP) { FormulaType = StatFormula.Health, Base = 14, Gain = 4F, GainRate = 4.5F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MP) { FormulaType = StatFormula.Mana, Base = 11, Gain = 3.5F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.BagWeight) { FormulaType = StatFormula.Weight, Base = 50, Gain = 3F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.WearWeight) { FormulaType = StatFormula.Weight, Base = 15, Gain = 20F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.HandWeight) { FormulaType = StatFormula.Weight, Base = 12, Gain = 13F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinAC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 0, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxAC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 7, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 5, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 5, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Agility) { FormulaType = StatFormula.Stat, Base = 15, Gain = 0, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Accuracy) { FormulaType = StatFormula.Stat, Base = 5, Gain = 0, GainRate = 0F, Max = 0 });
                break;
            #endregion
            #region Wizard
            case MirClass.Wizard:
                Stats.Add(new BaseStat(Stat.HP) { FormulaType = StatFormula.Health, Base = 14, Gain = 15F, GainRate =1.8F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MP) { FormulaType = StatFormula.Mana, Base = 13, Gain = 5F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.BagWeight) { FormulaType = StatFormula.Weight, Base = 50, Gain = 5F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.WearWeight) { FormulaType = StatFormula.Weight, Base = 15, Gain = 100F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.HandWeight) { FormulaType = StatFormula.Weight, Base = 12, Gain = 90F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 7, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 7, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinMC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 7, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxMC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 7, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Agility) { FormulaType = StatFormula.Stat, Base = 15, Gain = 0, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Accuracy) { FormulaType = StatFormula.Stat, Base = 5, Gain = 0, GainRate = 0F, Max = 0 });
                break;
            #endregion
            #region Taoist
            case MirClass.Taoist:
                Stats.Add(new BaseStat(Stat.HP) { FormulaType = StatFormula.Health, Base = 14, Gain = 6F, GainRate = 2.5F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MP) { FormulaType = StatFormula.Mana, Base = 13, Gain = 8F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.BagWeight) { FormulaType = StatFormula.Weight, Base = 50, Gain = 4F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.WearWeight) { FormulaType = StatFormula.Weight, Base = 15, Gain = 50F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.HandWeight) { FormulaType = StatFormula.Weight, Base = 12, Gain = 42F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinMAC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 12, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxMAC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 6, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 7, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 7, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinSC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 7, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxSC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 7, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Agility) { FormulaType = StatFormula.Stat, Base = 18, Gain = 0, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Accuracy) { FormulaType = StatFormula.Stat, Base = 5, Gain = 0, GainRate = 0F, Max = 0 });
                break;
            #endregion
            #region Assassin
            case MirClass.Assassin:
                Stats.Add(new BaseStat(Stat.HP) { FormulaType = StatFormula.Health, Base = 14, Gain = 4F, GainRate = 3.25F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MP) { FormulaType = StatFormula.Mana, Base = 11, Gain = 5F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.BagWeight) { FormulaType = StatFormula.Weight, Base = 50, Gain = 3.5F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.WearWeight) { FormulaType = StatFormula.Weight, Base = 15, Gain = 33F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.HandWeight) { FormulaType = StatFormula.Weight, Base = 12, Gain = 30F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 8, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 8, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Agility) { FormulaType = StatFormula.Stat, Base = 20, Gain = 0, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Accuracy) { FormulaType = StatFormula.Stat, Base = 5, Gain = 0, GainRate = 0F, Max = 0 });
                break;
            #endregion
            #region Archer
            case MirClass.Archer:
                Stats.Add(new BaseStat(Stat.HP) { FormulaType = StatFormula.Health, Base = 14, Gain = 4F, GainRate = 3.25F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MP) { FormulaType = StatFormula.Mana, Base = 11, Gain = 4F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.BagWeight) { FormulaType = StatFormula.Weight, Base = 50, Gain = 4F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.WearWeight) { FormulaType = StatFormula.Weight, Base = 15, Gain = 33F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.HandWeight) { FormulaType = StatFormula.Weight, Base = 12, Gain = 30F, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 8, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxDC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 8, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MinMC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 8, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.MaxMC) { FormulaType = StatFormula.Stat, Base = 0, Gain = 8, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Agility) { FormulaType = StatFormula.Stat, Base = 15, Gain = 0, GainRate = 0F, Max = 0 });
                Stats.Add(new BaseStat(Stat.Accuracy) { FormulaType = StatFormula.Stat, Base = 8, Gain = 0, GainRate = 0F, Max = 0 });
                break;
                #endregion
        }

        // 上限（Caps）在 RefreshStatCaps（HumanObject.cs:2286）里是 Math.Min 硬截断，
        // 超出部分静默丢弃，所以这里的值必须覆盖“装备 + 宝石 + 词条”的合理总量。
        // 2026-11-13 调整：原值被单件现有装备直接打满（MirNecklace 魔防 8 > Cap 2、
        // MirArmour 生命恢复 20 > Cap 8、Orb 暴击 100 > Cap 18），词条落在这些属性上恒为 0 收益。
        Caps[Stat.MagicResist] = 18;
        Caps[Stat.PoisonResist] = 12;
        Caps[Stat.CriticalRate] = 40;
        Caps[Stat.CriticalDamage] = 25;
        Caps[Stat.Freezing] = 6;
        Caps[Stat.PoisonAttack] = 6;
        Caps[Stat.HealthRecovery] = 16;
        Caps[Stat.SpellRecovery] = 16;
        Caps[Stat.PoisonRecovery] = 12;
        // 新增：这两个属性原本完全没有上限（词条可堆到 24% 常驻减伤 / 30%+ 反弹）。
        Caps[Stat.Reflect] = 20;
        Caps[Stat.DamageReductionPercent] = 15;
    }

    public BaseStats(BinaryReader reader)
    {
        Job = (MirClass)reader.ReadByte();
        var count = reader.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            Stats.Add(new BaseStat((Stat)reader.ReadByte())
            {
                FormulaType = (StatFormula)reader.ReadByte(),
                Base = reader.ReadInt32(),
                Gain = reader.ReadSingle(),
                GainRate = reader.ReadSingle(),
                Max = reader.ReadInt32()
            });
        }

        Caps = new Stats(reader);
    }

    public void Save(BinaryWriter writer)
    {
        writer.Write((byte)Job);
        writer.Write(Stats.Count);

        foreach (var stat in Stats)
        {
            writer.Write((byte)stat.Type);
            writer.Write((byte)stat.FormulaType);
            writer.Write(stat.Base);
            writer.Write(stat.Gain);
            writer.Write(stat.GainRate);
            writer.Write(stat.Max);
        }

        Caps.Save(writer);
    }
}

public class BaseStat
{
    public StatFormula FormulaType;
    public Stat Type;
    public int Base;
    public float Gain;
    public float GainRate;
    public int Max;

    public BaseStat(Stat type)
    {
        Type = type;
    }

    public int Calculate(MirClass job, int level)
    {
        if (Gain == 0) return Base;

        if (FormulaType == StatFormula.Health)
        {
            return job switch
            {
                MirClass.Warrior => (int)Math.Min(Max > 0 ? Max : int.MaxValue, Base + (level / Gain + GainRate + level / 20F) * level),
                _ => (int)Math.Min(Max > 0 ? Max : int.MaxValue, Base + (level / Gain + GainRate) * level),
            };
        }
        else if (FormulaType == StatFormula.Mana)
        {
            return job switch
            {
                MirClass.Wizard => (int)Math.Min(Max > 0 ? Max : int.MaxValue, Base + ((level / Gain + 2F) * 2.2F * level) + (level * GainRate)),
                MirClass.Taoist => (int)Math.Min(Max > 0 ? Max : int.MaxValue, (Base + level / Gain * 2.2F * level) + (level * GainRate)),
                _ => (int)Math.Min(Max > 0 ? Max : int.MaxValue, Base + (level * Gain) + (level * GainRate)),
            };
        }
        else
        {
            return FormulaType switch
            {
                StatFormula.Weight => (int)Math.Min(Max > 0 ? Max : int.MaxValue, Base + ((level / Gain) * level)),
                _ => (int)Math.Min(Max > 0 ? Max : int.MaxValue, Base + (level / Gain)),
            };
        }  
    }
}