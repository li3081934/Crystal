/**
 * 客户端属性计算（对照 UserObject.RefreshLevelStats / RefreshEquipmentStats）
 *
 * 只用它算 **上限**（HP/MP/负重）和装备外观 —— 当前 HP/MP 永远以服务端下发的为准。
 * 技能加成、buff、套装这些还没搬，所以高等级时上限会偏低（见 README 的 Phase 1 待办）。
 */
import { BASE_STATS, type BaseStatRow } from './basestats.ts';
import { ItemType, MirClass, Stat, StatFormula, StatName } from '../net/enums.ts';
import type { UserItem } from '../formats/item.ts';

export type StatMap = Record<number, number>;

/** 照抄 Shared/BaseStats.cs 里 BaseStat.Calculate */
export function calculateBase(row: BaseStatRow, cls: number, level: number): number {
  const cap = row.max > 0 ? row.max : Number.MAX_SAFE_INTEGER;
  if (row.gain === 0) return row.base;

  if (row.formula === StatFormula.Health) {
    if (cls === MirClass.Warrior) {
      return Math.min(cap, Math.trunc(row.base + (level / row.gain + row.gainRate + level / 20) * level));
    }
    return Math.min(cap, Math.trunc(row.base + (level / row.gain + row.gainRate) * level));
  }
  if (row.formula === StatFormula.Mana) {
    if (cls === MirClass.Wizard) {
      return Math.min(cap, Math.trunc(row.base + (level / row.gain + 2) * 2.2 * level + level * row.gainRate));
    }
    if (cls === MirClass.Taoist) {
      return Math.min(cap, Math.trunc(row.base + ((level / row.gain) * 2.2 * level) + level * row.gainRate));
    }
    return Math.min(cap, Math.trunc(row.base + level * row.gain + level * row.gainRate));
  }
  if (row.formula === StatFormula.Weight) {
    return Math.min(cap, Math.trunc(row.base + (level / row.gain) * level));
  }
  return Math.min(cap, Math.trunc(row.base + level / row.gain));
}

function addStats(target: StatMap, src: Record<number, number> | undefined) {
  if (!src) return;
  for (const [key, value] of Object.entries(src)) {
    const stat = Number(key);
    target[stat] = (target[stat] ?? 0) + value;
  }
}

/** 等级基础 + 装备（含附加属性），只做 HP/MP 的百分比收尾 */
export function computeStats(cls: number, level: number, equipment: (UserItem | null)[] | null): StatMap {
  const stats: StatMap = {};
  for (const row of BASE_STATS[cls] ?? []) stats[row.stat] = calculateBase(row, cls, level);

  for (const item of equipment ?? []) {
    if (!item?.info) continue;
    if (item.currentDura === 0 && item.info.durability > 0) continue;
    addStats(stats, item.info.stats);
    addStats(stats, item.addedStats);
  }

  const hp = stats[Stat.HP] ?? 0;
  const mp = stats[Stat.MP] ?? 0;
  stats[Stat.HP] = hp + Math.floor((hp * (stats[Stat.HPRatePercent] ?? 0)) / 100);
  stats[Stat.MP] = mp + Math.floor((mp * (stats[Stat.MPRatePercent] ?? 0)) / 100);
  return stats;
}

export interface Appearance {
  armour: number;
  weapon: number;
  weaponEffect: number;
  wingEffect: number;
  mountType: number;
}

/** 装备 -> 外观（PlayerObject 用的就是 ItemInfo.Shape / Effect） */
export function appearanceFromEquipment(equipment: (UserItem | null)[] | null): Appearance {
  const look: Appearance = { armour: 0, weapon: -1, weaponEffect: 0, wingEffect: 0, mountType: -1 };
  for (const item of equipment ?? []) {
    if (!item?.info) continue;
    const info = item.info;
    if (item.currentDura === 0 && info.durability > 0) continue;
    if (info.type === ItemType.Armour) {
      look.armour = info.shape;
      look.wingEffect = info.effect;
    } else if (info.type === ItemType.Weapon) {
      look.weapon = info.shape;
      look.weaponEffect = info.effect;
    } else if (info.type === ItemType.Mount) {
      look.mountType = info.shape;
    }
  }
  return look;
}

const STAT_NAMES: Record<number, string> = {
  [Stat.MinAC]: '防御下限',
  [Stat.MaxAC]: '防御上限',
  [Stat.MinMAC]: '魔防下限',
  [Stat.MaxMAC]: '魔防上限',
  [Stat.MinDC]: '攻击下限',
  [Stat.MaxDC]: '攻击上限',
  [Stat.MinMC]: '魔法下限',
  [Stat.MaxMC]: '魔法上限',
  [Stat.MinSC]: '道术下限',
  [Stat.MaxSC]: '道术上限',
  [Stat.HP]: 'HP',
  [Stat.MP]: 'MP',
  [Stat.Accuracy]: '准确',
  [Stat.Agility]: '敏捷',
  [Stat.Luck]: '幸运',
  [Stat.AttackSpeed]: '攻速',
  [Stat.Strong]: '强度',
  [Stat.MagicResist]: '魔御',
  [Stat.PoisonResist]: '毒御',
  [Stat.HealthRecovery]: 'HP恢复',
  [Stat.SpellRecovery]: 'MP恢复',
  [Stat.PoisonRecovery]: '毒恢复',
  [Stat.CriticalRate]: '暴击率',
  [Stat.CriticalDamage]: '暴击伤害',
  [Stat.Freezing]: '冰冻',
  [Stat.PoisonAttack]: '毒攻',
  [Stat.Holy]: '神圣',
  [Stat.Reflect]: '反伤',
  [Stat.BagWeight]: '背包负重',
  [Stat.HandWeight]: '腕力',
  [Stat.WearWeight]: '穿戴负重',
  [Stat.HPRatePercent]: 'HP 加成%',
  [Stat.MPRatePercent]: 'MP 加成%',
  [Stat.MaxDCRatePercent]: '攻击加成%',
  [Stat.MaxMCRatePercent]: '魔法加成%',
  [Stat.MaxSCRatePercent]: '道术加成%',
  [Stat.ExpRatePercent]: '经验加成%',
};

export function statName(stat: number) {
  return STAT_NAMES[stat] ?? StatName[stat] ?? `Stat${stat}`;
}

export function formatStats(stats: Record<number, number> | undefined): string[] {
  if (!stats) return [];
  const out: string[] = [];
  for (const [key, value] of Object.entries(stats)) {
    if (!value) continue;
    out.push(`${statName(Number(key))} ${value > 0 ? '+' : ''}${value}`);
  }
  return out;
}
