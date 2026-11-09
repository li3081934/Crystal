/**
 * 入口：登录 → 选角 → 进图 → 走路/聊天 → 装备外观 + HUD（背包/装备/技能栏）
 *
 * Phase 0：跑通 协议 / 图库 Range 解码 / 地图 / 角色帧 / 移动同步
 * Phase 1：装备与背包数据（UserItem/ItemInfo）、自己外观随装备变化、HUD 面板
 */
import { Renderer } from './render/renderer.ts';
import { GameSocket } from './net/socket.ts';
import { send, type GamePacket } from './net/packets.ts';
import { parseMap, type GameMap } from './formats/map.ts';
import type { ClientMagicInfo, ItemInfo, UserItem } from './formats/item.ts';
import { Actor, WALK_MS, directionOffset, directionTo, type Look } from './game/world.ts';
import { appearanceFromEquipment, computeStats } from './game/stats.ts';
import { Hud, type HudState } from './ui/hud.ts';
import { EquipmentSlot, ItemType, MirAction, MirClass, MirDirection, MirGender, MirGridType, Stat } from './net/enums.ts';

const canvasHost = document.querySelector<HTMLDivElement>('#stage')!;
const overlay = document.querySelector<HTMLDivElement>('#overlay')!;
const statusEl = document.querySelector<HTMLDivElement>('#status')!;
const logEl = document.querySelector<HTMLDivElement>('#log')!;
const hudEl = document.querySelector<HTMLDivElement>('#hud')!;
const hudInfo = document.querySelector<HTMLDivElement>('#hud-info')!;
const hudHost = document.querySelector<HTMLDivElement>('#hud-root-host')!;
const chatInput = document.querySelector<HTMLInputElement>('#chat-input')!;
const loginForm = document.querySelector<HTMLFormElement>('#login-form')!;
const accountInput = document.querySelector<HTMLInputElement>('#account')!;
const passwordInput = document.querySelector<HTMLInputElement>('#password')!;
const hostInput = document.querySelector<HTMLInputElement>('#gateway')!;

const DEFAULT_LOOK: Look = {
  cls: MirClass.Warrior,
  gender: MirGender.Male,
  hair: 0,
  weapon: -1,
  weaponEffect: -1,
  armour: 0,
  wingEffect: 0,
  transformType: -1,
  mountType: -1,
  ridingMount: false,
};

interface State {
  socket: GameSocket;
  actors: Map<number, Actor>;
  self: Actor | null;
  selfId: number;
  map: GameMap | null;
  mapTitle: string;
  moveTarget: { x: number; y: number } | null;
  lastStepAt: number;
  targetId: number | null;
  attackReadyAt: number;

  // 人物数据
  cls: number;
  gender: number;
  hair: number;
  level: number;
  hp: number;
  mp: number;
  exp: number;
  maxExp: number;
  gold: number;
  inventory: (UserItem | null)[];
  equipment: (UserItem | null)[];
  magics: ClientMagicInfo[];
  itemInfo: Map<number, ItemInfo>;
  stats: Record<number, number>;
  hudDirty: boolean;
}

const state: State = {
  socket: new GameSocket(),
  actors: new Map(),
  self: null,
  selfId: 0,
  map: null,
  mapTitle: '',
  moveTarget: null,
  lastStepAt: 0,
  targetId: null,
  attackReadyAt: 0,
  cls: MirClass.Warrior,
  gender: MirGender.Male,
  hair: 0,
  level: 1,
  hp: 0,
  mp: 0,
  exp: 0,
  maxExp: 1,
  gold: 0,
  inventory: [],
  equipment: [],
  magics: [],
  itemInfo: new Map(),
  stats: {},
  hudDirty: false,
};

const renderer = new Renderer();
const hud = new Hud(renderer.store, hudHost);

function log(line: string) {
  const div = document.createElement('div');
  div.textContent = line;
  logEl.appendChild(div);
  while (logEl.childElementCount > 200) logEl.firstElementChild?.remove();
  logEl.scrollTop = logEl.scrollHeight;
}

function setStatus(text: string) {
  statusEl.textContent = text;
}

/** 把 UserItem 关联到物品表（NewItemInfo 是后到的，所以每次都要重连一遍） */
function linkItems() {
  const link = (item: UserItem | null) => {
    if (!item) return;
    item.info = state.itemInfo.get(item.itemIndex);
    for (const slot of item.slots ?? []) link(slot);
  };
  for (const item of state.inventory) link(item);
  for (const item of state.equipment) link(item);
}

/** 装备/等级变化后重算属性 + 自己的外观 */
function refreshDerived() {
  state.stats = computeStats(state.cls, state.level, state.equipment);
  const look = appearanceFromEquipment(state.equipment);
  handleLook(look);
  state.hudDirty = true;
}

function handleLook(newLook: ReturnType<typeof appearanceFromEquipment>) {
  const self = state.self;
  if (!self) return;
  // 只有 0-7 是标准外观（Crystal 里更大型号是变身/坐骑，Phase 1 先不管）
  self.look = {
    ...self.look,
    armour: newLook.armour >= 0 && newLook.armour < 100 ? newLook.armour : 0,
    weapon: newLook.weapon < 100 ? newLook.weapon : -1,
    weaponEffect: newLook.weaponEffect,
    wingEffect: newLook.wingEffect,
  };
}

function maxHp() {
  return Math.max(state.stats[Stat.HP] ?? 0, state.hp);
}

function maxMp() {
  return Math.max(state.stats[Stat.MP] ?? 0, state.mp);
}

function hudState(): HudState {
  return {
    name: state.self?.name ?? '',
    level: state.level,
    hp: state.hp,
    maxHp: maxHp(),
    mp: state.mp,
    maxMp: maxMp(),
    exp: state.exp,
    maxExp: state.maxExp,
    gold: state.gold,
    inventory: state.inventory,
    equipment: state.equipment,
    magics: state.magics,
    stats: state.stats,
  };
}

// ---------------- 背包操作 ----------------

function findItemByUniqueId(items: (UserItem | null)[], uniqueId: number) {
  for (let i = 0; i < items.length; i++) {
    if (items[i]?.uniqueId === uniqueId) return i;
  }
  return -1;
}

function firstFreeSlot() {
  for (let i = 0; i < Math.max(state.inventory.length, 40); i++) {
    if (!state.inventory[i]) return i;
  }
  return -1;
}

function addItem(item: UserItem) {
  item.info = state.itemInfo.get(item.itemIndex);
  const stackSize = item.info?.stackSize ?? 1;
  if (stackSize > 1) {
    for (const slot of state.inventory) {
      if (!slot || slot.itemIndex !== item.itemIndex || !slot.info) continue;
      if (slot.count >= slot.info.stackSize) continue;
      const room = slot.info.stackSize - slot.count;
      if (item.count <= room) {
        slot.count += item.count;
        return;
      }
      item.count -= room;
      slot.count = slot.info.stackSize;
    }
  }
  const free = firstFreeSlot();
  if (free < 0) return;
  state.inventory[free] = item;
}

/** 物品类型 -> 装备槽（对应 MirItemCell 里的选择规则） */
function equipmentSlotFor(item: UserItem): number {
  switch (item.info?.type) {
    case ItemType.Weapon:
      return EquipmentSlot.Weapon;
    case ItemType.Armour:
      return EquipmentSlot.Armour;
    case ItemType.Helmet:
      return EquipmentSlot.Helmet;
    case ItemType.Torch:
      return EquipmentSlot.Torch;
    case ItemType.Necklace:
      return EquipmentSlot.Necklace;
    case ItemType.Bracelet:
      return state.equipment[EquipmentSlot.BraceletR] ? EquipmentSlot.BraceletL : EquipmentSlot.BraceletR;
    case ItemType.Ring:
      return state.equipment[EquipmentSlot.RingR] ? EquipmentSlot.RingL : EquipmentSlot.RingR;
    case ItemType.Amulet:
      return EquipmentSlot.Amulet;
    case ItemType.Belt:
      return EquipmentSlot.Belt;
    case ItemType.Boots:
      return EquipmentSlot.Boots;
    case ItemType.Stone:
      return EquipmentSlot.Stone;
    case ItemType.Mount:
      return EquipmentSlot.Mount;
    default:
      return -1;
  }
}

function equipFromInventory(index: number) {
  const item = state.inventory[index];
  if (!item?.info) return;
  const to = equipmentSlotFor(item);
  if (to < 0) {
    if (item.info.type === ItemType.Potion || item.info.type === ItemType.Scroll) {
      state.socket.send(send.useItem(MirGridType.Inventory, item.uniqueId));
    } else {
      log(`${item.info.name} 不能装备`);
    }
    return;
  }
  state.socket.send(send.equipItem(MirGridType.Inventory, item.uniqueId, to));
}

hud.onEquip = (item) => {
  const index = findItemByUniqueId(state.inventory, item.uniqueId);
  if (index >= 0) equipFromInventory(index);
};

hud.onUnequip = (slotIndex: number) => {
  const item = state.equipment[slotIndex];
  if (!item) return;
  const free = firstFreeSlot();
  if (free < 0) {
    log('背包已满，无法卸下');
    return;
  }
  // 注意：服务端 RemoveItem 的 Grid 是“目标网格”（背包），源固定是装备栏
  state.socket.send(send.removeItem(MirGridType.Inventory, item.uniqueId, free));
};

hud.onUseSkill = (key) => {
  const magic = state.magics.find((m) => m.key === key);
  if (!magic) return;
  log(`技能 ${magic.name}（Lv${magic.level}）—— 战斗系统 Phase 2 再接`);
};

// ---------------- 网络 ----------------

function wireSocket() {
  const socket = state.socket;
  socket.onStatus = (s, info) => {
    if (s === 'open') {
      setStatus('已连接网关，正在握手…');
      socket.send(send.clientVersion());
    } else if (s === 'connecting') {
      setStatus('连接中…');
    } else {
      setStatus(`连接断开 (${info ?? ''})`);
    }
  };
  socket.onUnknown = (p) => {
    if (p.decodeError) log(`[warn] ${p.name} 解析失败: ${p.decodeError}`);
  };
  socket.onPacket = handlePacket;
}

function handlePacket(p: GamePacket) {
  const d = p.data as Record<string, any>;
  switch (p.name) {
    case 'Connected':
      break;

    case 'ClientVersion':
      if (d.result !== 1) {
        setStatus('客户端版本校验失败');
        return;
      }
      setStatus('登录中…');
      state.socket.send(send.login(accountInput.value.trim(), passwordInput.value));
      break;

    case 'Login': {
      const result = d.result as number;
      const messages: Record<number, string> = {
        0: '登录被禁用',
        1: '账号名不合法',
        2: '密码不合法',
        3: '账号不存在',
        4: '账号或密码错误',
        5: '需要先修改密码',
      };
      if (result === 3) {
        setStatus('账号不存在，自动创建…');
        state.socket.send(send.newAccount(accountInput.value.trim(), passwordInput.value, accountInput.value.trim()));
        return;
      }
      if (result !== 0 && result !== 3 && result !== 5) setStatus(messages[result] ?? `登录失败 (${result})`);
      break;
    }

    case 'NewAccount': {
      const result = d.result as number;
      if (result === 8) {
        log('账号创建成功，继续登录');
        state.socket.send(send.login(accountInput.value.trim(), passwordInput.value));
      } else {
        const messages: Record<number, string> = {
          0: '账号创建已禁用',
          1: '账号名不合法',
          2: '密码不合法（8-15 位字母数字）',
          7: '账号已存在',
        };
        setStatus(messages[result] ?? `创建账号失败 (${result})`);
      }
      break;
    }

    case 'LoginSuccess': {
      const characters = d.characters as Array<{ index: number; name: string; level: number; class: number }>;
      log(`角色列表：${characters.length} 个 ${characters.map((c) => `${c.name}(Lv${c.level})`).join(', ')}`);
      if (characters.length > 0) {
        state.socket.send(send.startGame(characters[0].index));
      } else {
        const name = `Web${Math.floor(Math.random() * 100000)}`;
        log(`没有角色，自动创建 ${name}`);
        state.socket.send(send.newCharacter(name, MirGender.Male, MirClass.Warrior));
      }
      break;
    }

    case 'NewCharacterSuccess': {
      const character = d.character as { index: number; name: string };
      log(`角色 ${character.name} 创建成功，进入游戏`);
      state.socket.send(send.startGame(character.index));
      break;
    }

    case 'StartGame':
      setStatus((d.result as number) === 4 ? '进入游戏…' : `进入游戏失败 (${d.result})`);
      break;

    case 'MapInformation': {
      const fileName = d.fileName as string;
      state.mapTitle = (d.title as string) || fileName;
      void loadMap(fileName);
      break;
    }

    case 'NewItemInfo': {
      const info = d.info as ItemInfo;
      state.itemInfo.set(info.index, info);
      linkItems();
      break;
    }

    case 'UserInformation': {
      state.cls = d.class as number;
      state.gender = d.gender as number;
      state.hair = d.hair as number;
      state.level = d.level as number;
      state.hp = d.hp as number;
      state.mp = d.mp as number;
      state.exp = d.experience as number;
      state.maxExp = d.maxExperience as number;
      state.gold = d.gold as number;
      state.inventory = (d.inventory as (UserItem | null)[]) ?? [];
      state.equipment = (d.equipment as (UserItem | null)[]) ?? [];
      state.magics = (d.magics as ClientMagicInfo[]) ?? [];

      const look: Look = {
        ...DEFAULT_LOOK,
        cls: state.cls,
        gender: state.gender,
        hair: state.hair,
      };
      const self = new Actor(d.objectId as number, 'player', d.name as string, look);
      self.level = state.level;
      const loc = d.location as { x: number; y: number };
      self.setPosition(loc.x, loc.y, d.direction as number);
      state.self = self;
      state.selfId = self.id;
      state.actors.set(self.id, self);

      linkItems();
      refreshDerived();

      overlay.classList.add('hidden');
      hudEl.classList.remove('hidden');
      log(`已进入 ${state.mapTitle}，坐标 (${loc.x},${loc.y})`);
      log(`背包 ${state.inventory.filter(Boolean).length} 件 · 技能 ${state.magics.length} 个`);
      break;
    }

    case 'UserSlotsRefresh':
      state.inventory = (d.inventory as (UserItem | null)[]) ?? state.inventory;
      state.equipment = (d.equipment as (UserItem | null)[]) ?? state.equipment;
      linkItems();
      refreshDerived();
      break;

    case 'HealthChanged':
      state.hp = d.hp as number;
      state.mp = d.mp as number;
      state.hudDirty = true;
      break;

    case 'LevelChanged':
      state.level = d.level as number;
      state.exp = d.experience as number;
      state.maxExp = d.maxExperience as number;
      refreshDerived();
      break;

    case 'GainedItem': {
      const item = d.item as UserItem;
      addItem(item);
      refreshDerived();
      log(`获得物品 ${item.info?.name ?? `#${item.itemIndex}`}${item.count > 1 ? ` ×${item.count}` : ''}`);
      break;
    }

    case 'GainedGold':
      state.gold += d.gold as number;
      state.hudDirty = true;
      break;

    case 'LoseGold':
      state.gold = Math.max(0, state.gold - (d.gold as number));
      state.hudDirty = true;
      break;

    case 'DeleteItem': {
      const uniqueId = d.uniqueId as number;
      const count = d.count as number;
      const index = findItemByUniqueId(state.inventory, uniqueId);
      if (index >= 0) {
        const item = state.inventory[index]!;
        if (item.count <= count) state.inventory[index] = null;
        else item.count -= count;
      }
      refreshDerived();
      break;
    }

    case 'MoveItem': {
      if (!d.success) break;
      const grid = d.grid as number;
      const from = d.from as number;
      const to = d.to as number;
      const list = grid === MirGridType.Equipment ? state.equipment : state.inventory;
      const item = list[from] ?? null;
      list[from] = list[to] ?? null;
      list[to] = item;
      refreshDerived();
      break;
    }

    case 'RemoveItem': {
      if (!d.success) break;
      const index = findItemByUniqueId(state.equipment, d.uniqueId as number);
      if (index < 0) break;
      const to = d.to as number;
      state.inventory[to] = state.equipment[index];
      state.equipment[index] = null;
      refreshDerived();
      break;
    }

    case 'EquipItem': {
      if (!d.success) {
        log('装备失败（可能等级/重量/职业不符）');
        break;
      }
      const to = d.to as number;
      const index = findItemByUniqueId(state.inventory, d.uniqueId as number);
      if (index < 0 || to < 0) break;
      const item = state.inventory[index];
      state.inventory[index] = state.equipment[to] ?? null;
      state.equipment[to] = item;
      refreshDerived();
      break;
    }

    case 'NewMagic':
    case 'MagicLeveled': {
      const magic = (d.magic ?? null) as ClientMagicInfo | null;
      if (magic) {
        const i = state.magics.findIndex((m) => m.spell === magic.spell);
        if (i >= 0) state.magics[i] = magic;
        else state.magics.push(magic);
      } else {
        const spell = d.spell as number;
        const found = state.magics.find((m) => m.spell === spell);
        if (found) {
          found.level = d.level as number;
          found.experience = d.experience as number;
        }
      }
      state.hudDirty = true;
      break;
    }

    case 'UserLocation': {
      const loc = d.location as { x: number; y: number };
      if (state.self && (state.self.cellX !== loc.x || state.self.cellY !== loc.y)) {
        state.self.setPosition(loc.x, loc.y, d.direction as number);
      }
      break;
    }

    case 'ObjectPlayer':
    case 'ObjectHero': {
      const data = d.player as Record<string, unknown>;
      const id = data.objectId as number;
      if (id === state.selfId) break;
      const look: Look = {
        cls: data.class as number,
        gender: data.gender as number,
        hair: data.hair as number,
        weapon: data.weapon as number,
        weaponEffect: data.weaponEffect as number,
        armour: data.armour as number,
        wingEffect: data.wingEffect as number,
        transformType: data.transformType as number,
        mountType: data.mountType as number,
        ridingMount: data.ridingMount as boolean,
      };
      let actor = state.actors.get(id);
      if (!actor) {
        actor = new Actor(id, 'player', data.name as string, look);
        state.actors.set(id, actor);
      }
      actor.look = look;
      actor.level = data.level as number;
      actor.dead = data.dead as boolean;
      actor.hidden = data.hidden as boolean;
      const loc = data.location as { x: number; y: number };
      if (!actor.isMoving) actor.setPosition(loc.x, loc.y, data.direction as number);
      break;
    }

    case 'ObjectMonster': {
      const id = d.objectId as number;
      let actor = state.actors.get(id);
      if (!actor) {
        actor = new Actor(id, 'monster', d.name as string, { ...DEFAULT_LOOK });
        state.actors.set(id, actor);
      }
      actor.monsterImage = d.image as number;
      actor.dead = d.dead as boolean;
      actor.hidden = d.hidden as boolean;
      const loc = d.location as { x: number; y: number };
      if (!actor.isMoving) actor.setPosition(loc.x, loc.y, d.direction as number);
      break;
    }

    case 'ObjectAttack': {
      const actor = state.actors.get(d.objectId as number);
      const loc = d.location as { x: number; y: number };
      if (actor) {
        if (!actor.isMoving) actor.setPosition(loc.x, loc.y, d.direction as number);
        actor.setAction(MirAction.Attack1, performance.now(), d.direction as number);
      }
      break;
    }

    case 'Struck':
      if (state.self) state.self.setAction(MirAction.Struck, performance.now());
      break;

    case 'ObjectStruck': {
      const actor = state.actors.get(d.objectId as number);
      const loc = d.location as { x: number; y: number };
      if (actor) {
        if (!actor.isMoving) actor.setPosition(loc.x, loc.y, d.direction as number);
        actor.setAction(MirAction.Struck, performance.now());
      }
      break;
    }

    case 'DamageIndicator': {
      const target = state.actors.get(d.objectId as number);
      const type = d.damageType as number;
      const now = performance.now();
      const text = type === 1 ? 'MISS' : String(d.damage);
      const color = type === 1 ? 0xbbbbbb : type === 2 ? 0xffd24a : 0xffffff;
      if (target) {
        renderer.addFloatText({
          x: target.cellX,
          y: target.cellY,
          text,
          color,
          start: now,
          until: now + 900,
        });
      }
      break;
    }

    case 'ObjectHealth': {
      const actor = state.actors.get(d.objectId as number);
      if (actor) {
        actor.hpPercent = d.percent as number;
        actor.hpUntil = performance.now() + Math.max(3, d.expire as number) * 1000;
      }
      break;
    }

    case 'ObjectDied': {
      const actor = state.actors.get(d.objectId as number);
      if (actor) {
        actor.setAction(MirAction.Die, performance.now(), d.direction as number);
        if (actor.id === state.targetId) state.targetId = null;
      }
      break;
    }

    case 'Death':
      if (state.self) {
        state.self.setAction(MirAction.Die, performance.now(), d.direction as number);
        state.targetId = null;
        state.moveTarget = null;
        log('你死了…');
      }
      break;

    case 'Opendoor':
      renderer.setDoor(d.doorIndex as number, d.close as boolean);
      break;

    case 'ObjectRemove':
      if (d.objectId === state.targetId) state.targetId = null;
      state.actors.delete(d.objectId as number);
      break;

    case 'ObjectTurn': {
      const actor = state.actors.get(d.objectId as number);
      const loc = d.location as { x: number; y: number };
      if (actor && !actor.isMoving) actor.setPosition(loc.x, loc.y, d.direction as number);
      break;
    }

    case 'ObjectWalk':
    case 'ObjectRun': {
      if (d.objectId === state.selfId) break;
      const actor = state.actors.get(d.objectId as number);
      const loc = d.location as { x: number; y: number };
      if (actor) actor.startMove(loc.x, loc.y, d.direction as number, performance.now());
      break;
    }

    case 'Chat':
      log(`[聊天] ${d.message}`);
      break;

    case 'ObjectChat': {
      log(`[附近] ${d.text}`);
      const actor = state.actors.get(d.objectId as number);
      if (actor) {
        actor.chatText = d.text as string;
        actor.chatUntil = performance.now() + 6000;
      }
      break;
    }

    case 'Disconnect':
      setStatus(`被服务端断开 (reason=${d.reason})`);
      break;

    default:
      break;
  }
}

// ---------------- 地图 ----------------

async function loadMap(fileName: string) {
  try {
    const res = await fetch(`/assets/Map/${fileName}.map`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const map = parseMap(new Uint8Array(await res.arrayBuffer()));
    state.map = map;
    renderer.setMap(map);
    state.moveTarget = null;
    log(`地图 ${fileName}.map ${map.width}x${map.height} (${map.format})`);
  } catch (e) {
    log(`地图加载失败 ${fileName}: ${e}`);
  }
}

function canWalk(x: number, y: number): boolean {
  const map = state.map;
  if (!map) return false;
  const cell = map.at(x, y);
  if (!cell) return false;
  if ((cell.backImage & 0x20000000) !== 0) return false;
  if ((cell.frontImage & 0x8000) !== 0) return false;
  // 门没开就走不进去（对齐 C# 的 CheckDoorOpen）
  if (cell.doorIndex > 0 && renderer.doorState(cell.doorIndex) !== 2) return false;
  return true;
}

/** 简单 BFS：8 方向、最多 4000 次扩展 */
function findPath(fromX: number, fromY: number, toX: number, toY: number): Array<{ x: number; y: number }> {
  if (fromX === toX && fromY === toY) return [];
  const key = (x: number, y: number) => `${x},${y}`;
  const visited = new Set<string>([key(fromX, fromY)]);
  const prev = new Map<string, string>();
  const queue: Array<{ x: number; y: number }> = [{ x: fromX, y: fromY }];
  const goal = key(toX, toY);
  let found = false;
  let guard = 0;

  while (queue.length && guard++ < 4000 && !found) {
    const cur = queue.shift()!;
    for (let dir = 0; dir < 8; dir++) {
      const { dx, dy } = directionOffset(dir);
      const nx = cur.x + dx;
      const ny = cur.y + dy;
      const k = key(nx, ny);
      if (visited.has(k) || !canWalk(nx, ny)) continue;
      visited.add(k);
      prev.set(k, key(cur.x, cur.y));
      if (k === goal) {
        found = true;
        break;
      }
      queue.push({ x: nx, y: ny });
    }
  }

  if (!found) return [];
  const path: Array<{ x: number; y: number }> = [];
  let cur = goal;
  while (cur !== key(fromX, fromY)) {
    const [x, y] = cur.split(',').map(Number);
    path.push({ x, y });
    const p = prev.get(cur);
    if (!p) return [];
    cur = p;
  }
  return path.reverse();
}

// ---------------- 移动 ----------------

function tryStep(now: number) {
  const self = state.self;
  const target = state.moveTarget;
  if (!self || !target || !state.map) return;
  if (self.isMoving || now - state.lastStepAt < WALK_MS - 40) return;

  if (self.cellX === target.x && self.cellY === target.y) {
    state.moveTarget = null;
    return;
  }

  let path = findPath(self.cellX, self.cellY, target.x, target.y);
  if (!path.length) {
    // BFS 到不了（比如门还没开）：朝目标方向撞一下，顺便把门敲开
    const dir = directionTo(self.cellX, self.cellY, target.x, target.y);
    const { dx, dy } = directionOffset(dir);
    const nx = self.cellX + dx;
    const ny = self.cellY + dy;
    const cell = state.map.at(nx, ny);
    if (cell && cell.doorIndex > 0 && renderer.doorState(cell.doorIndex) !== 2) {
      checkDoor(nx, ny, now);
      return; // 等门开
    }
    if (!canWalk(nx, ny)) {
      state.moveTarget = null;
      return;
    }
    path = [{ x: nx, y: ny }];
  }
  const next = path[0];
  checkDoor(next.x, next.y, now);
  const dir = directionTo(self.cellX, self.cellY, next.x, next.y);
  state.lastStepAt = now;
  self.startMove(next.x, next.y, dir, now);
  state.socket.send(send.walk(dir));
}

function attackInterval() {
  const speed = state.stats[Stat.AttackSpeed] ?? 0;
  return Math.max(550, 1400 - (speed * 60 + Math.min(370, state.level * 14)));
}

/** 目标身边离自己最近的可行走格 */
function chaseSpot(target: Actor): { x: number; y: number } | null {
  const self = state.self;
  if (!self) return null;
  let best: { x: number; y: number } | null = null;
  let bestDist = Number.MAX_SAFE_INTEGER;
  for (let dir = 0; dir < 8; dir++) {
    const { dx, dy } = directionOffset(dir);
    const x = target.cellX + dx;
    const y = target.cellY + dy;
    if (!canWalk(x, y)) continue;
    const d = Math.max(Math.abs(x - self.cellX), Math.abs(y - self.cellY));
    if (d < bestDist) {
      bestDist = d;
      best = { x, y };
    }
  }
  return best;
}

let doorTime = 0;

/** 目标格是门就敲一下（对齐 C# CheckDoorOpen 的 4s 节流） */
function checkDoor(x: number, y: number, now: number) {
  const cell = state.map?.at(x, y);
  if (!cell || cell.doorIndex <= 0) return;
  const doorState = renderer.doorState(cell.doorIndex);
  if (doorState === 0 || doorState === 3) {
    if (now > doorTime) {
      doorTime = now + 4000;
      state.socket.send(send.openDoor(cell.doorIndex));
    }
  }
}

/** 战斗循环：追目标 / 贴身攻击 */
function combat(now: number) {
  const self = state.self;
  const target = state.targetId === null ? null : state.actors.get(state.targetId);
  if (!self || !target) return;
  if (target.dead) {
    state.targetId = null;
    return;
  }
  if (self.dead) return;

  const dist = Math.max(Math.abs(target.cellX - self.cellX), Math.abs(target.cellY - self.cellY));
  if (dist > 12) {
    state.targetId = null;
    return;
  }

  if (dist <= 1) {
    state.moveTarget = null;
    const dir = directionTo(self.cellX, self.cellY, target.cellX, target.cellY);
    if (now >= state.attackReadyAt && !self.isMoving) {
      state.attackReadyAt = now + attackInterval();
      self.setAction(MirAction.Attack1, now, dir);
      state.socket.send(send.attack(dir));
    }
    return;
  }

  // 还没贴身：走到目标旁边
  const spot = chaseSpot(target);
  state.moveTarget = spot ?? { x: target.cellX, y: target.cellY };
}

function isAdjacentOrSelf(x: number, y: number) {
  const self = state.self;
  if (!self) return false;
  return Math.abs(self.cellX - x) <= 1 && Math.abs(self.cellY - y) <= 1;
}

// ---------------- 主循环 ----------------

function directionArrow(dir: number) {
  return ['↑', '↗', '→', '↘', '↓', '↙', '←', '↖'][dir] ?? '?';
}

function frame(now: number) {
  requestAnimationFrame(frame);

  const self = state.self;
  if (self) {
    combat(now);
    tryStep(now);
    const pos = self.renderPos(now);
    renderer.setCamera(pos.x, pos.y);
    hudInfo.textContent =
      `${self.name} Lv${state.level} · ${state.mapTitle} (${self.cellX},${self.cellY}) ${directionArrow(self.direction)}` +
      ` · 对象 ${state.actors.size} · 金币 ${state.gold}`;

    if (state.hudDirty) {
      state.hudDirty = false;
      hud.update(hudState());
    }
  }
  renderer.render(now, state.actors.values());
}

// ---------------- 输入 ----------------

canvasHost.addEventListener('pointerdown', (ev) => {
  if (ev.button !== 0) return;
  if (!state.self) return;
  const rect = canvasHost.getBoundingClientRect();
  const px = ev.clientX - rect.left;
  const py = ev.clientY - rect.top;

  // 点到怪身上 = 打它（没贴身会自己走过去）
  const hitId = renderer.hitTestActor(px, py);
  const hit = hitId === null ? null : state.actors.get(hitId);
  if (hit && hit.kind === 'monster' && !hit.dead) {
    state.targetId = hit.id;
    state.moveTarget = null;
    log(`目标：${hit.name}`);
    return;
  }

  state.targetId = null;
  const cell = renderer.toCell(px, py);
  state.moveTarget = cell;
  if (isAdjacentOrSelf(cell.x, cell.y)) state.lastStepAt = 0;
});

window.addEventListener('keydown', (ev) => {
  if (document.activeElement === chatInput) return;
  const map: Record<string, number> = {
    ArrowUp: MirDirection.Up,
    ArrowRight: MirDirection.Right,
    ArrowDown: MirDirection.Down,
    ArrowLeft: MirDirection.Left,
  };
  const dir = map[ev.key];
  if (dir === undefined) return;
  ev.preventDefault();
  const self = state.self;
  if (!self) return;
  const { dx, dy } = directionOffset(dir);
  state.moveTarget = { x: self.cellX + dx, y: self.cellY + dy };
  state.lastStepAt = 0;
});

chatInput.addEventListener('keydown', (ev) => {
  if (ev.key !== 'Enter') return;
  const text = chatInput.value.trim();
  chatInput.value = '';
  if (!text) return;
  state.socket.send(send.chat(text));
});

loginForm.addEventListener('submit', (ev) => {
  ev.preventDefault();
  const gateway = hostInput.value.trim() || `${location.host}/ws`;
  const url = /^wss?:\/\//.test(gateway)
    ? gateway
    : `${location.protocol === 'https:' ? 'wss' : 'ws'}://${gateway}`;
  setStatus(`连接 ${url} …`);
  state.actors.clear();
  state.self = null;
  state.selfId = 0;
  state.map = null;
  state.itemInfo.clear();
  state.targetId = null;
  state.attackReadyAt = 0;
  state.inventory = [];
  state.equipment = [];
  state.magics = [];
  renderer.setMap(null);
  wireSocket();
  state.socket.connect(url);
});

// ---------------- 启动 ----------------

await renderer.init(canvasHost);
requestAnimationFrame(frame);
setStatus('填写账号密码后进入');
log('Phase 1 客户端就绪（装备外观 + 背包/装备/技能栏）');

{
  const params = new URLSearchParams(location.search);
  const account = params.get('account');
  if (account) {
    accountInput.value = account;
    passwordInput.value = params.get('password') ?? account;
    const gateway = params.get('gateway');
    if (gateway) hostInput.value = gateway;
    setTimeout(() => loginForm.requestSubmit(), 50);
  }
}

// 方便在控制台/自动化脚本里调试
Object.assign(window as unknown as Record<string, unknown>, {
  state,
  renderer,
  hud,
  /** 发聊天（GM 命令也用这个） */
  chat: (text: string) => state.socket.send(send.chat(text)),
  send,
});
