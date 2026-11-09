/**
 * 物品结构（对照 Shared/Data/ItemData.cs，DB v117 分支）
 *
 * 只实现 v>84 的读法（当前服务端 DB v117）：老版本分支（<=84 的逐字节属性）全部跳过，
 * 这样代码量和可读性都好很多，代价是不兼容 v84 以下的老库。
 */
import type { ByteReader } from '../net/bytes.ts';

export type Stats = Record<number, number>;

export interface ItemInfo {
  index: number;
  name: string;
  type: number;
  grade: number;
  requiredType: number;
  requiredClass: number;
  requiredGender: number;
  set: number;
  shape: number;
  weight: number;
  light: number;
  requiredAmount: number;
  image: number;
  durability: number;
  stackSize: number;
  price: number;
  startItem: boolean;
  effect: number;
  needIdentify: boolean;
  showGroupPickup: boolean;
  classBased: boolean;
  levelBased: boolean;
  canMine: boolean;
  globalDropNotify: boolean;
  bind: number;
  unique: number;
  randomStatsId: number;
  canFastRun: boolean;
  canAwakening: boolean;
  slots: number;
  stats: Stats;
  toolTip: string;
}

export interface AwakeInfo {
  type: number;
  values: number[];
}

export interface UserItem {
  uniqueId: number;
  itemIndex: number;
  currentDura: number;
  maxDura: number;
  count: number;
  soulBoundId: number;
  identified: boolean;
  cursed: boolean;
  slots: (UserItem | null)[];
  gemCount: number;
  addedStats: Stats;
  awake: AwakeInfo;
  refinedValue: number;
  refineAdded: number;
  refineSuccessChance: number;
  weddingRing: number;
  expireDate: number | null;
  rental: { ownerName: string; bindingFlags: number; expiryDate: number; locked: boolean } | null;
  isShopItem: boolean;
  sealed: { expiryDate: number; nextSealDate: number } | null;
  gmMade: boolean;
  /** 由 itemIndex 关联到物品表，客户端填 */
  info?: ItemInfo;
}

export interface ClientMagicInfo {
  name: string;
  spell: number;
  baseCost: number;
  levelCost: number;
  icon: number;
  level1: number;
  level2: number;
  level3: number;
  need1: number;
  need2: number;
  need3: number;
  level: number;
  key: number;
  experience: number;
  delay: number;
  range: number;
  castTime: number;
}

export function readStats(r: ByteReader): Stats {
  const out: Stats = {};
  const count = r.i32();
  for (let i = 0; i < count; i++) {
    const stat = r.u8();
    out[stat] = r.i32();
  }
  return out;
}

export function readItemInfo(r: ByteReader): ItemInfo {
  const index = r.i32();
  const name = r.str();
  const type = r.u8();
  const grade = r.u8();
  const requiredType = r.u8();
  const requiredClass = r.u8();
  const requiredGender = r.u8();
  const set = r.u8();
  const shape = r.i16();
  const weight = r.u8();
  const light = r.u8();
  const requiredAmount = r.u8();
  const image = r.u16();
  const durability = r.u16();
  const stackSize = r.u16();
  const price = r.u32();
  const startItem = r.bool();
  const effect = r.u8();
  const bools = r.u8();
  const bind = r.i16();
  const unique = r.i16();
  const randomStatsId = r.u8();
  const canFastRun = r.bool();
  const canAwakening = r.bool();
  const slots = r.u8();
  const stats = readStats(r);
  const hasTooltip = r.bool();
  const toolTip = hasTooltip ? r.str() : '';

  return {
    index,
    name,
    type,
    grade,
    requiredType,
    requiredClass,
    requiredGender,
    set,
    shape,
    weight,
    light,
    requiredAmount,
    image,
    durability,
    stackSize,
    price,
    startItem,
    effect,
    needIdentify: (bools & 0x01) === 0x01,
    showGroupPickup: (bools & 0x02) === 0x02,
    classBased: (bools & 0x04) === 0x04,
    levelBased: (bools & 0x08) === 0x08,
    canMine: (bools & 0x10) === 0x10,
    globalDropNotify: (bools & 0x20) === 0x20,
    bind,
    unique,
    randomStatsId,
    canFastRun,
    canAwakening,
    slots,
    stats,
    toolTip,
  };
}

export function readUserItem(r: ByteReader): UserItem {
  const uniqueId = r.u64();
  const itemIndex = r.i32();
  const currentDura = r.u16();
  const maxDura = r.u16();
  const count = r.u16();
  const soulBoundId = r.i32();
  const bools = r.u8();

  const slotCount = r.i32();
  const slots: (UserItem | null)[] = new Array(Math.max(0, slotCount)).fill(null);
  for (let i = 0; i < slotCount; i++) {
    if (r.bool()) continue;
    slots[i] = readUserItem(r);
  }

  const gemCount = r.u16();
  const addedStats = readStats(r);

  const awakeType = r.u8();
  const awakeCount = r.i32();
  const awakeValues = Array.from({ length: awakeCount }, () => r.u8());

  const refinedValue = r.u8();
  const refineAdded = r.u8();
  const refineSuccessChance = r.i32();
  const weddingRing = r.i32();

  const expireDate = r.bool() ? r.i64() : null;
  const rental = r.bool()
    ? (() => {
        const ownerName = r.str();
        const bindingFlags = r.i16();
        const expiryDate = r.i64();
        const locked = r.bool();
        return { ownerName, bindingFlags, expiryDate, locked };
      })()
    : null;
  const isShopItem = r.bool();
  const sealed = r.bool() ? { expiryDate: r.i64(), nextSealDate: r.i64() } : null;
  const gmMade = r.bool();

  return {
    uniqueId,
    itemIndex,
    currentDura,
    maxDura,
    count,
    soulBoundId,
    identified: (bools & 0x01) === 0x01,
    cursed: (bools & 0x02) === 0x02,
    slots,
    gemCount,
    addedStats,
    awake: { type: awakeType, values: awakeValues },
    refinedValue,
    refineAdded,
    refineSuccessChance,
    weddingRing,
    expireDate,
    rental,
    isShopItem,
    sealed,
    gmMade,
  };
}

export function readMagic(r: ByteReader): ClientMagicInfo {
  const name = r.str();
  const spell = r.u8();
  const baseCost = r.u8();
  const levelCost = r.u8();
  const icon = r.u8();
  const level1 = r.u8();
  const level2 = r.u8();
  const level3 = r.u8();
  const need1 = r.u16();
  const need2 = r.u16();
  const need3 = r.u16();
  const level = r.u8();
  const key = r.u8();
  const experience = r.u16();
  const delay = r.i64();
  const range = r.u8();
  const castTime = r.i64();
  return {
    name,
    spell,
    baseCost,
    levelCost,
    icon,
    level1,
    level2,
    level3,
    need1,
    need2,
    need3,
    level,
    key,
    experience,
    delay,
    range,
    castTime,
  };
}

/** 读 UserItem[] 数组（服务端用 bool 表示“有没有”，再 bool 表示“这一格空不空”） */
export function readUserItemArray(r: ByteReader): (UserItem | null)[] | null {
  if (!r.bool()) return null;
  const len = r.i32();
  const arr: (UserItem | null)[] = new Array(Math.max(0, len)).fill(null);
  for (let i = 0; i < len; i++) {
    if (!r.bool()) continue;
    arr[i] = readUserItem(r);
  }
  return arr;
}
