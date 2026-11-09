// 本文件由 tools/gen-basestats.mjs 从 Shared/BaseStats.cs 自动生成，请勿手改。
/* eslint-disable */
import { MirClass, Stat, StatFormula } from '../net/enums.ts';

export interface BaseStatRow {
  stat: number;
  formula: number;
  base: number;
  gain: number;
  gainRate: number;
  max: number;
}

export const BASE_STATS: Record<number, BaseStatRow[]> = {
  [MirClass.Warrior]: [
    { stat: Stat.HP, formula: StatFormula.Health, base: 14, gain: 4, gainRate: 4.5, max: 0 }, // HP
    { stat: Stat.MP, formula: StatFormula.Mana, base: 11, gain: 3.5, gainRate: 0, max: 0 }, // MP
    { stat: Stat.BagWeight, formula: StatFormula.Weight, base: 50, gain: 3, gainRate: 0, max: 0 }, // BagWeight
    { stat: Stat.WearWeight, formula: StatFormula.Weight, base: 15, gain: 20, gainRate: 0, max: 0 }, // WearWeight
    { stat: Stat.HandWeight, formula: StatFormula.Weight, base: 12, gain: 13, gainRate: 0, max: 0 }, // HandWeight
    { stat: Stat.MinAC, formula: StatFormula.Stat, base: 0, gain: 0, gainRate: 0, max: 0 }, // MinAC
    { stat: Stat.MaxAC, formula: StatFormula.Stat, base: 0, gain: 7, gainRate: 0, max: 0 }, // MaxAC
    { stat: Stat.MinDC, formula: StatFormula.Stat, base: 0, gain: 5, gainRate: 0, max: 0 }, // MinDC
    { stat: Stat.MaxDC, formula: StatFormula.Stat, base: 0, gain: 5, gainRate: 0, max: 0 }, // MaxDC
    { stat: Stat.Agility, formula: StatFormula.Stat, base: 15, gain: 0, gainRate: 0, max: 0 }, // Agility
    { stat: Stat.Accuracy, formula: StatFormula.Stat, base: 5, gain: 0, gainRate: 0, max: 0 }, // Accuracy
  ],
  [MirClass.Wizard]: [
    { stat: Stat.HP, formula: StatFormula.Health, base: 14, gain: 15, gainRate: 1.8, max: 0 }, // HP
    { stat: Stat.MP, formula: StatFormula.Mana, base: 13, gain: 5, gainRate: 0, max: 0 }, // MP
    { stat: Stat.BagWeight, formula: StatFormula.Weight, base: 50, gain: 5, gainRate: 0, max: 0 }, // BagWeight
    { stat: Stat.WearWeight, formula: StatFormula.Weight, base: 15, gain: 100, gainRate: 0, max: 0 }, // WearWeight
    { stat: Stat.HandWeight, formula: StatFormula.Weight, base: 12, gain: 90, gainRate: 0, max: 0 }, // HandWeight
    { stat: Stat.MinDC, formula: StatFormula.Stat, base: 0, gain: 7, gainRate: 0, max: 0 }, // MinDC
    { stat: Stat.MaxDC, formula: StatFormula.Stat, base: 0, gain: 7, gainRate: 0, max: 0 }, // MaxDC
    { stat: Stat.MinMC, formula: StatFormula.Stat, base: 0, gain: 7, gainRate: 0, max: 0 }, // MinMC
    { stat: Stat.MaxMC, formula: StatFormula.Stat, base: 0, gain: 7, gainRate: 0, max: 0 }, // MaxMC
    { stat: Stat.Agility, formula: StatFormula.Stat, base: 15, gain: 0, gainRate: 0, max: 0 }, // Agility
    { stat: Stat.Accuracy, formula: StatFormula.Stat, base: 5, gain: 0, gainRate: 0, max: 0 }, // Accuracy
  ],
  [MirClass.Taoist]: [
    { stat: Stat.HP, formula: StatFormula.Health, base: 14, gain: 6, gainRate: 2.5, max: 0 }, // HP
    { stat: Stat.MP, formula: StatFormula.Mana, base: 13, gain: 8, gainRate: 0, max: 0 }, // MP
    { stat: Stat.BagWeight, formula: StatFormula.Weight, base: 50, gain: 4, gainRate: 0, max: 0 }, // BagWeight
    { stat: Stat.WearWeight, formula: StatFormula.Weight, base: 15, gain: 50, gainRate: 0, max: 0 }, // WearWeight
    { stat: Stat.HandWeight, formula: StatFormula.Weight, base: 12, gain: 42, gainRate: 0, max: 0 }, // HandWeight
    { stat: Stat.MinMAC, formula: StatFormula.Stat, base: 0, gain: 12, gainRate: 0, max: 0 }, // MinMAC
    { stat: Stat.MaxMAC, formula: StatFormula.Stat, base: 0, gain: 6, gainRate: 0, max: 0 }, // MaxMAC
    { stat: Stat.MinDC, formula: StatFormula.Stat, base: 0, gain: 7, gainRate: 0, max: 0 }, // MinDC
    { stat: Stat.MaxDC, formula: StatFormula.Stat, base: 0, gain: 7, gainRate: 0, max: 0 }, // MaxDC
    { stat: Stat.MinSC, formula: StatFormula.Stat, base: 0, gain: 7, gainRate: 0, max: 0 }, // MinSC
    { stat: Stat.MaxSC, formula: StatFormula.Stat, base: 0, gain: 7, gainRate: 0, max: 0 }, // MaxSC
    { stat: Stat.Agility, formula: StatFormula.Stat, base: 18, gain: 0, gainRate: 0, max: 0 }, // Agility
    { stat: Stat.Accuracy, formula: StatFormula.Stat, base: 5, gain: 0, gainRate: 0, max: 0 }, // Accuracy
  ],
  [MirClass.Assassin]: [
    { stat: Stat.HP, formula: StatFormula.Health, base: 14, gain: 4, gainRate: 3.25, max: 0 }, // HP
    { stat: Stat.MP, formula: StatFormula.Mana, base: 11, gain: 5, gainRate: 0, max: 0 }, // MP
    { stat: Stat.BagWeight, formula: StatFormula.Weight, base: 50, gain: 3.5, gainRate: 0, max: 0 }, // BagWeight
    { stat: Stat.WearWeight, formula: StatFormula.Weight, base: 15, gain: 33, gainRate: 0, max: 0 }, // WearWeight
    { stat: Stat.HandWeight, formula: StatFormula.Weight, base: 12, gain: 30, gainRate: 0, max: 0 }, // HandWeight
    { stat: Stat.MinDC, formula: StatFormula.Stat, base: 0, gain: 8, gainRate: 0, max: 0 }, // MinDC
    { stat: Stat.MaxDC, formula: StatFormula.Stat, base: 0, gain: 8, gainRate: 0, max: 0 }, // MaxDC
    { stat: Stat.Agility, formula: StatFormula.Stat, base: 20, gain: 0, gainRate: 0, max: 0 }, // Agility
    { stat: Stat.Accuracy, formula: StatFormula.Stat, base: 5, gain: 0, gainRate: 0, max: 0 }, // Accuracy
  ],
  [MirClass.Archer]: [
    { stat: Stat.HP, formula: StatFormula.Health, base: 14, gain: 4, gainRate: 3.25, max: 0 }, // HP
    { stat: Stat.MP, formula: StatFormula.Mana, base: 11, gain: 4, gainRate: 0, max: 0 }, // MP
    { stat: Stat.BagWeight, formula: StatFormula.Weight, base: 50, gain: 4, gainRate: 0, max: 0 }, // BagWeight
    { stat: Stat.WearWeight, formula: StatFormula.Weight, base: 15, gain: 33, gainRate: 0, max: 0 }, // WearWeight
    { stat: Stat.HandWeight, formula: StatFormula.Weight, base: 12, gain: 30, gainRate: 0, max: 0 }, // HandWeight
    { stat: Stat.MinDC, formula: StatFormula.Stat, base: 0, gain: 8, gainRate: 0, max: 0 }, // MinDC
    { stat: Stat.MaxDC, formula: StatFormula.Stat, base: 0, gain: 8, gainRate: 0, max: 0 }, // MaxDC
    { stat: Stat.MinMC, formula: StatFormula.Stat, base: 0, gain: 8, gainRate: 0, max: 0 }, // MinMC
    { stat: Stat.MaxMC, formula: StatFormula.Stat, base: 0, gain: 8, gainRate: 0, max: 0 }, // MaxMC
    { stat: Stat.Agility, formula: StatFormula.Stat, base: 15, gain: 0, gainRate: 0, max: 0 }, // Agility
    { stat: Stat.Accuracy, formula: StatFormula.Stat, base: 8, gain: 0, gainRate: 0, max: 0 }, // Accuracy
  ],
};
