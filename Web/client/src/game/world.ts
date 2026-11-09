/**
 * 世界模型：地图对象 + 动作/插值。只做 Phase 0 需要的部分：
 * 站立 / 走路 / 跑步 的插值，其它动作先当站立处理。
 */
import { MirAction, MirDirection } from '../net/enums.ts';

export const WALK_MS = 600;
export const RUN_MS = 600;

/** 一次性动作（播完回到站立） */
const ONE_SHOT_ACTIONS = new Set<number>([
  MirAction.Attack1,
  MirAction.Attack2,
  MirAction.Attack3,
  MirAction.Attack4,
  MirAction.Struck,
  MirAction.Die,
  MirAction.Mine,
]);

/** 受击/攻击动画时长（毫秒），用于服务端没发的补帧 */
export function actionDuration(action: number, frames: typeof PLAYER_FRAMES): number {
  const spec = frames[action];
  if (!spec) return 0;
  return spec.interval * spec.count;
}

/** 与 Client/MirObjects/Frames.cs 里的 FrameSet.Player 一致 */
export const PLAYER_FRAMES: Record<number, { start: number; count: number; skip: number; interval: number }> = {
  [MirAction.Standing]: { start: 0, count: 4, skip: 0, interval: 500 },
  [MirAction.Walking]: { start: 32, count: 6, skip: 0, interval: 100 },
  [MirAction.Running]: { start: 80, count: 6, skip: 0, interval: 100 },
  [MirAction.Attack1]: { start: 136, count: 6, skip: 0, interval: 100 },
  [MirAction.Attack2]: { start: 184, count: 6, skip: 0, interval: 100 },
  [MirAction.Struck]: { start: 360, count: 3, skip: 0, interval: 100 },
  [MirAction.Die]: { start: 384, count: 4, skip: 0, interval: 100 },
  [MirAction.Dead]: { start: 387, count: 1, skip: 3, interval: 1000 },
};

/** 默认怪物帧（FrameSet.DefaultMonster） */
export const MONSTER_FRAMES: Record<number, { start: number; count: number; skip: number; interval: number }> = {
  [MirAction.Standing]: { start: 0, count: 4, skip: 0, interval: 500 },
  [MirAction.Walking]: { start: 32, count: 6, skip: 0, interval: 100 },
  [MirAction.Attack1]: { start: 80, count: 6, skip: 0, interval: 100 },
  [MirAction.Struck]: { start: 128, count: 2, skip: 0, interval: 200 },
  [MirAction.Die]: { start: 144, count: 10, skip: 0, interval: 100 },
  [MirAction.Dead]: { start: 153, count: 1, skip: 9, interval: 1000 },
};

export interface Look {
  cls: number;
  gender: number;
  hair: number;
  weapon: number;
  weaponEffect: number;
  armour: number;
  wingEffect: number;
  transformType: number;
  mountType: number;
  ridingMount: boolean;
}

export type ActorKind = 'player' | 'monster';

export class Actor {
  id: number;
  kind: ActorKind;
  name: string;
  look: Look;
  monsterImage = 0;
  level = 0;
  dead = false;
  hidden = false;
  direction: number = MirDirection.Down;
  action: number = MirAction.Standing;

  cellX = 0;
  cellY = 0;

  /** 移动插值 */
  private fromX = 0;
  private fromY = 0;
  private moveStart = 0;
  private moveDuration = 0;
  private moving = false;

  /** 动作起始时间，用于帧动画 */
  actionStart = 0;
  chatText = '';
  chatUntil = 0;

  /** 敌人血条（服务端 ObjectHealth 只发百分比 + 过期时间） */
  hpPercent: number | null = null;
  hpUntil = 0;

  constructor(id: number, kind: ActorKind, name: string, look: Look) {
    this.id = id;
    this.kind = kind;
    this.name = name;
    this.look = look;
  }

  get isMoving() {
    return this.moving;
  }

  setPosition(x: number, y: number, dir?: number) {
    this.cellX = x;
    this.cellY = y;
    this.fromX = x;
    this.fromY = y;
    this.moving = false;
    if (dir !== undefined) this.direction = dir;
  }

  /** 开始一次移动（服务端已算好落点） */
  startMove(x: number, y: number, dir: number, now: number, ms = WALK_MS) {
    this.fromX = this.cellX;
    this.fromY = this.cellY;
    this.cellX = x;
    this.cellY = y;
    this.direction = dir;
    this.moveStart = now;
    this.moveDuration = ms;
    this.moving = true;
    this.action = ms >= RUN_MS ? MirAction.Running : MirAction.Walking;
    this.actionStart = now;
  }

  /** 渲染用的浮点格子坐标 */
  renderPos(now: number): { x: number; y: number } {
    if (!this.moving) return { x: this.cellX, y: this.cellY };
    const t = Math.min(1, (now - this.moveStart) / this.moveDuration);
    if (t >= 1) {
      this.moving = false;
      this.action = MirAction.Standing;
      this.actionStart = now;
      return { x: this.cellX, y: this.cellY };
    }
    return {
      x: this.fromX + (this.cellX - this.fromX) * t,
      y: this.fromY + (this.cellY - this.fromY) * t,
    };
  }

  /** 切动作（攻击/受击/死亡） */
  setAction(action: number, now: number, dir?: number) {
    if (dir !== undefined) this.direction = dir;
    this.action = action;
    this.actionStart = now;
    if (action === MirAction.Die) this.dead = true;
  }

  /** 当前动作对应的帧序号（0..count-1）与帧表 */
  currentFrame(now: number, frames: typeof PLAYER_FRAMES) {
    // 一次性动作播完自动收尾
    if (ONE_SHOT_ACTIONS.has(this.action)) {
      const spec = frames[this.action];
      const dur = spec ? spec.interval * spec.count : 0;
      if (dur > 0 && now - this.actionStart >= dur) {
        const wasDie = this.action === MirAction.Die;
        this.action = wasDie ? MirAction.Dead : MirAction.Standing;
        this.actionStart = this.actionStart + dur;
      }
    }

    const spec = frames[this.action] ?? frames[MirAction.Standing];
    const elapsed = now - this.actionStart;
    let index: number;
    if (this.action === MirAction.Walking || this.action === MirAction.Running) {
      index = Math.floor(elapsed / spec.interval);
      if (index >= spec.count) index = spec.count - 1;
    } else if (ONE_SHOT_ACTIONS.has(this.action)) {
      index = Math.min(spec.count - 1, Math.floor(elapsed / Math.max(1, spec.interval)));
    } else if (this.action === MirAction.Dead) {
      index = 0;
    } else {
      index = Math.floor(elapsed / spec.interval) % spec.count;
    }
    return { spec, index, offset: spec.count + spec.skip };
  }
}

/** 八方向 -> 格子增量 */
export function directionOffset(dir: number): { dx: number; dy: number } {
  switch (dir) {
    case MirDirection.Up:
      return { dx: 0, dy: -1 };
    case MirDirection.UpRight:
      return { dx: 1, dy: -1 };
    case MirDirection.Right:
      return { dx: 1, dy: 0 };
    case MirDirection.DownRight:
      return { dx: 1, dy: 1 };
    case MirDirection.Down:
      return { dx: 0, dy: 1 };
    case MirDirection.DownLeft:
      return { dx: -1, dy: 1 };
    case MirDirection.Left:
      return { dx: -1, dy: 0 };
    case MirDirection.UpLeft:
      return { dx: -1, dy: -1 };
    default:
      return { dx: 0, dy: 0 };
  }
}

/** 从 A 点看向 B 点最接近的方向（用于点击转向/单步移动） */
export function directionTo(fromX: number, fromY: number, toX: number, toY: number): number {
  const dx = Math.sign(toX - fromX);
  const dy = Math.sign(toY - fromY);
  const table: Record<string, number> = {
    '0,-1': MirDirection.Up,
    '1,-1': MirDirection.UpRight,
    '1,0': MirDirection.Right,
    '1,1': MirDirection.DownRight,
    '0,1': MirDirection.Down,
    '-1,1': MirDirection.DownLeft,
    '-1,0': MirDirection.Left,
    '-1,-1': MirDirection.UpLeft,
  };
  return table[`${dx},${dy}`] ?? MirDirection.Down;
}
