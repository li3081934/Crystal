/**
 * 协议层：包分帧 + 服务端包解码 + 客户端包编码。
 *
 * 帧格式（与 Shared/Packet.cs 完全一致）：
 *   [0..1] uint16 总长度（含这 4 字节头）
 *   [2..3] int16  包 ID（= ServerPacketIds / ClientPacketIds 的枚举值）
 *   [4..]  payload（小端，字符串为 7bit 变长长度 + UTF8）
 *
 * 没实现的包会带上 unknown=true 和原始字节，不会打断连接。
 */
import { ByteReader, ByteWriter } from './bytes.ts';
import { readItemInfo, readMagic, readUserItem, readUserItemArray, type ItemInfo, type UserItem } from '../formats/item.ts';
import { ClientPacketIds, ServerPacketIds, ServerPacketIdsName } from './enums.ts';

export interface GamePacket {
  /** 包 ID */
  id: number;
  /** 包名（ServerPacketIds 里的枚举名） */
  name: string;
  /** 解出来的字段。放在 data 里，避免和包名 name 冲突 */
  data: Record<string, any>;
  /** 未实现解码时为 true，原始字节在 payload */
  unknown?: boolean;
  decodeError?: string;
  payload?: Uint8Array;
}

const S = ServerPacketIds;
const C = ClientPacketIds;

function argb(v: number) {
  return {
    a: (v >>> 24) & 0xff,
    r: (v >>> 16) & 0xff,
    g: (v >>> 8) & 0xff,
    b: v & 0xff,
    css: `#${((v & 0xffffff) >>> 0).toString(16).padStart(6, '0')}`,
  };
}

type Reader = (r: ByteReader) => Record<string, unknown>;

function readSelectInfo(r: ByteReader) {
  return {
    index: r.i32(),
    name: r.str(),
    level: r.u16(),
    class: r.u8(),
    gender: r.u8(),
    lastAccess: r.i64(),
  };
}

function readObjectPlayer(r: ByteReader) {
  const p: Record<string, unknown> = {
    objectId: r.u32(),
    name: r.str(),
    guildName: r.str(),
    guildRankName: r.str(),
    nameColour: argb(r.i32()),
    class: r.u8(),
    gender: r.u8(),
    level: r.u16(),
    location: r.point(),
    direction: r.u8(),
    hair: r.u8(),
    light: r.u8(),
    weapon: r.i16(),
    weaponEffect: r.i16(),
    armour: r.i16(),
    poison: r.u16(),
    dead: r.bool(),
    hidden: r.bool(),
    effect: r.u8(),
    wingEffect: r.u8(),
    extra: r.bool(),
    mountType: r.i16(),
    ridingMount: r.bool(),
    fishing: r.bool(),
    transformType: r.i16(),
    elementOrbEffect: r.u32(),
    elementOrbLvl: r.u32(),
    elementOrbMax: r.u32(),
  };
  const buffCount = r.i32();
  p.buffs = Array.from({ length: buffCount }, () => r.u8());
  p.levelEffects = r.u16();
  return p;
}

/** UserInformation：解到 Magics 结束就停（后面的宠物结构 Phase 1 用不到） */
function readUserInformation(r: ByteReader) {
  const p: Record<string, unknown> = {
    objectId: r.u32(),
    realId: r.u32(),
    name: r.str(),
    guildName: r.str(),
    guildRank: r.str(),
    nameColour: argb(r.i32()),
    class: r.u8(),
    gender: r.u8(),
    level: r.u16(),
    location: r.point(),
    direction: r.u8(),
    hair: r.u8(),
    hp: r.i32(),
    mp: r.i32(),
    experience: r.i64(),
    maxExperience: r.i64(),
    levelEffects: r.u16(),
    hasHero: r.bool(),
    heroBehaviour: r.u8(),
  };
  p.inventory = readUserItemArray(r);
  p.equipment = readUserItemArray(r);
  p.questInventory = readUserItemArray(r);
  p.gold = r.u32();
  p.credit = r.u32();
  p.hasExpandedStorage = r.bool();
  p.hasStoragePassword = r.bool();
  p.requireStoragePassword = r.bool();
  p.storagePasswordLastSet = r.i64();
  p.expandedStorageExpiryTime = r.i64();
  const magicCount = r.i32();
  p.magics = Array.from({ length: Math.max(0, magicCount) }, () => readMagic(r));
  return p;
}

const decoders: Record<number, Reader> = {
  [S.Connected]: () => ({}),
  [S.ClientVersion]: (r) => ({ result: r.u8() }),
  [S.Disconnect]: (r) => ({ reason: r.u8() }),
  [S.KeepAlive]: (r) => ({ time: r.i64() }),
  [S.NewAccount]: (r) => ({ result: r.u8() }),
  [S.Login]: (r) => ({ result: r.u8() }),
  [S.LoginBanned]: (r) => ({ reason: r.str(), expiry: r.i64() }),
  [S.LoginSuccess]: (r) => {
    const count = r.i32();
    return { characters: Array.from({ length: count }, () => readSelectInfo(r)) };
  },
  [S.NewCharacter]: (r) => ({ result: r.u8() }),
  [S.NewCharacterSuccess]: (r) => ({ character: readSelectInfo(r) }),
  [S.DeleteCharacterSuccess]: (r) => ({ characterIndex: r.i32() }),
  [S.StartGame]: (r) => ({ result: r.u8(), resolution: r.i32() }),
  [S.StartGameBanned]: (r) => ({ reason: r.str(), expiry: r.i64() }),
  [S.StartGameDelay]: (r) => ({ milliseconds: r.i32() }),
  [S.MapInformation]: (r) => {
    const p: Record<string, unknown> = {
      mapIndex: r.i32(),
      fileName: r.str(),
      title: r.str(),
      miniMap: r.u16(),
      bigMap: r.u16(),
      lights: r.u8(),
    };
    const bools = r.u8();
    p.lightning = (bools & 0x01) === 0x01;
    p.fire = (bools & 0x02) === 0x02;
    p.mapDarkLight = r.u8();
    p.music = r.u16();
    p.weather = r.u16();
    return p;
  },
  [S.UserInformation]: readUserInformation,
  [S.UserLocation]: (r) => ({ location: r.point(), direction: r.u8() }),
  [S.ObjectPlayer]: (r) => ({ player: readObjectPlayer(r) }),
  [S.ObjectMonster]: (r) => {
    const p: Record<string, unknown> = {
      objectId: r.u32(),
      name: r.str(),
      nameColour: argb(r.i32()),
      location: r.point(),
      image: r.u16(),
      direction: r.u8(),
      effect: r.u8(),
      ai: r.u8(),
      light: r.u8(),
      dead: r.bool(),
      skeleton: r.bool(),
      poison: r.u16(),
      hidden: r.bool(),
      shockTime: r.i64(),
      bindingShotCenter: r.bool(),
      extra: r.bool(),
      extraByte: r.u8(),
      masterObjectId: r.u32(),
      rarity: r.u8(),
    };
    const buffCount = r.i32();
    p.buffs = Array.from({ length: buffCount }, () => r.u8());
    return p;
  },
  [S.ObjectHero]: (r) => ({ player: readObjectPlayer(r), ownerName: r.str() }),
  [S.ObjectRemove]: (r) => ({ objectId: r.u32() }),
  [S.ObjectTurn]: (r) => ({ objectId: r.u32(), location: r.point(), direction: r.u8() }),
  [S.ObjectWalk]: (r) => ({ objectId: r.u32(), location: r.point(), direction: r.u8() }),
  [S.ObjectRun]: (r) => ({ objectId: r.u32(), location: r.point(), direction: r.u8() }),
  [S.NewItemInfo]: (r) => ({ info: readItemInfo(r) }),
  [S.UserSlotsRefresh]: (r) => ({ inventory: readUserItemArray(r), equipment: readUserItemArray(r) }),
  [S.HealthChanged]: (r) => ({ hp: r.i32(), mp: r.i32() }),
  [S.LevelChanged]: (r) => ({ level: r.u16(), experience: r.i64(), maxExperience: r.i64() }),
  [S.GainedItem]: (r) => ({ item: readUserItem(r) }),
  [S.GainedGold]: (r) => ({ gold: r.u32() }),
  [S.LoseGold]: (r) => ({ gold: r.u32() }),
  [S.DeleteItem]: (r) => ({ uniqueId: r.u64(), count: r.u16() }),
  [S.RemoveItem]: (r) => ({ grid: r.u8(), uniqueId: r.u64(), to: r.i32(), success: r.bool() }),
  [S.MoveItem]: (r) => ({ grid: r.u8(), from: r.i32(), to: r.i32(), success: r.bool() }),
  [S.EquipItem]: (r) => ({ grid: r.u8(), uniqueId: r.u64(), to: r.i32(), success: r.bool() }),
  [S.ObjectAttack]: (r) => ({
    objectId: r.u32(),
    location: r.point(),
    direction: r.u8(),
    spell: r.u8(),
    level: r.u8(),
    attackType: r.u8(),
  }),
  [S.Struck]: (r) => ({ attackerId: r.u32() }),
  [S.ObjectStruck]: (r) => ({ objectId: r.u32(), attackerId: r.u32(), location: r.point(), direction: r.u8() }),
  [S.DamageIndicator]: (r) => ({ damage: r.i32(), damageType: r.u8(), objectId: r.u32() }),
  [S.Death]: (r) => ({ location: r.point(), direction: r.u8() }),
  [S.ObjectDied]: (r) => ({ objectId: r.u32(), location: r.point(), direction: r.u8(), deathType: r.u8() }),
  [S.ObjectHealth]: (r) => ({ objectId: r.u32(), percent: r.u8(), expire: r.u8() }),
  [S.Opendoor]: (r) => ({ doorIndex: r.u8(), close: r.bool() }),
  [S.NewMagic]: (r) => ({ magic: readMagic(r), hero: r.bool() }),
  [S.MagicLeveled]: (r) => ({ objectId: r.u32(), spell: r.u8(), level: r.u8(), experience: r.u16() }),
  [S.Chat]: (r) => ({ message: r.str(), chatType: r.u8() }),
  [S.ObjectChat]: (r) => ({ objectId: r.u32(), text: r.str(), chatType: r.u8() }),
};

export function decodePacket(id: number, payload: Uint8Array): GamePacket {
  const name = ServerPacketIdsName[id] ?? `Unknown(${id})`;
  const decode = decoders[id];
  if (!decode) return { id, name, data: {}, unknown: true, payload };
  try {
    return { id, name, data: decode(new ByteReader(payload)) };
  } catch (err) {
    return { id, name, data: {}, decodeError: String(err), payload };
  }
}

/** 把任意大的 TCP 块切成一个个完整包 */
export class PacketStream {
  private buf = new Uint8Array(0);

  push(chunk: Uint8Array): GamePacket[] {
    const merged = new Uint8Array(this.buf.length + chunk.length);
    merged.set(this.buf, 0);
    merged.set(chunk, this.buf.length);

    const view = new DataView(merged.buffer, merged.byteOffset, merged.byteLength);
    const out: GamePacket[] = [];
    let off = 0;

    while (off + 4 <= merged.length) {
      const len = view.getUint16(off, true);
      if (len < 4 || off + len > merged.length) break; // 半包，等下一块
      const id = view.getInt16(off + 2, true);
      out.push(decodePacket(id, merged.subarray(off + 4, off + len)));
      off += len;
    }

    this.buf = merged.subarray(off);
    return out;
  }

  reset() {
    this.buf = new Uint8Array(0);
  }
}

export function frame(id: number, body: Uint8Array): Uint8Array {
  const total = body.length + 4;
  const out = new Uint8Array(total);
  const view = new DataView(out.buffer);
  view.setUint16(0, total, true);
  view.setInt16(2, id, true);
  out.set(body, 4);
  return out;
}

/** 客户端发包 */
export const send = {
  clientVersion(hash: Uint8Array = new Uint8Array(0)) {
    const w = new ByteWriter().i32(hash.length);
    for (const b of hash) w.u8(b);
    return frame(C.ClientVersion, w.build());
  },
  login(accountId: string, password: string) {
    return frame(C.Login, new ByteWriter().str(accountId).str(password).build());
  },
  newAccount(accountId: string, password: string, userName: string, email = '', question = '', answer = '') {
    // C# DateTime.ToBinary() 里最小可用值：1990-01-01 的 ticks（服务端不校验生日）
    const w = new ByteWriter()
      .str(accountId)
      .str(password)
      .i64(627667488000000000)
      .str(userName)
      .str(question)
      .str(answer)
      .str(email);
    return frame(C.NewAccount, w.build());
  },
  removeItem(grid: number, uniqueId: number, to: number) {
    // 脱下装备用的是 RemoveItem（Grid = Equipment，To = 背包格子）
    return frame(C.RemoveItem, new ByteWriter().u8(grid).u64(uniqueId).i32(to).build());
  },
  equipItem(grid: number, uniqueId: number, to = -1) {
    return frame(C.EquipItem, new ByteWriter().u8(grid).u64(uniqueId).i32(to).build());
  },
  useItem(grid: number, uniqueId: number) {
    // C# 顺序是 UniqueID 在前，Grid 在后
    return frame(C.UseItem, new ByteWriter().u64(uniqueId).u8(grid).build());
  },
  startGame(characterIndex: number) {
    return frame(C.StartGame, new ByteWriter().i32(characterIndex).build());
  },
  newCharacter(name: string, gender: number, cls: number) {
    return frame(C.NewCharacter, new ByteWriter().str(name).u8(gender).u8(cls).build());
  },
  attack(direction: number, spell = 0) {
    return frame(C.Attack, new ByteWriter().u8(direction).u8(spell).build());
  },
  openDoor(doorIndex: number) {
    return frame(C.Opendoor, new ByteWriter().u8(doorIndex).build());
  },
  turn(direction: number) {
    return frame(C.Turn, new ByteWriter().u8(direction).build());
  },
  walk(direction: number) {
    return frame(C.Walk, new ByteWriter().u8(direction).build());
  },
  run(direction: number) {
    return frame(C.Run, new ByteWriter().u8(direction).build());
  },
  chat(message: string) {
    return frame(C.Chat, new ByteWriter().str(message).i32(0).build());
  },
  keepAlive(time: number) {
    return frame(C.KeepAlive, new ByteWriter().i64(time).build());
  },
  logOut() {
    return frame(C.LogOut, new Uint8Array(0));
  },
};

export type { ItemInfo, UserItem };
export { ClientPacketIds, ServerPacketIds };
