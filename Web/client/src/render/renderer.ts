/**
 * PixiJS 渲染层（Phase 0）
 *  - 地图：按 C# 规则画 back/middle/front 三层瓦片（48x32 网格，back 只在偶数格）
 *  - 角色：身体 + 头发 + 武器 三层精灵，帧号算法与 PlayerObject 一致
 *  - 贴图：全部按需从 .Lib 里 Range 取 + gzip 解 + BGRA→RGBA，缓存成 ImageBitmap
 *
 * 加载是异步的：瓦片精灵先占位（隐藏），之后每帧补一批贴图，
 * 这样镜头移动时不会因为等待网络而卡住。
 */
import { Application, Container, Graphics, Sprite, Text, TextStyle, Texture, TilingSprite } from 'pixi.js';
import { Lib, LibStore, type MImageMeta } from '../formats/lib.ts';
import { CELL_HEIGHT, CELL_WIDTH, GameMap, mapLibPath } from '../formats/map.ts';
import { MirAction, MirClass, MirGender } from '../net/enums.ts';
import { Actor, MONSTER_FRAMES, PLAYER_FRAMES } from '../game/world.ts';

interface ActorSprites {
  body: Sprite;
  hair: Sprite;
  weapon1: Sprite;
  weapon2: Sprite;
  label: Text;
  bubble: Text;
  hpBar: Graphics;
  /** 最近一次绘制的屏幕位置与行号（用于点击命中测试） */
  screenX: number;
  screenY: number;
  row: number;
  alive: boolean;
}

export interface FloatTextSpec {
  /** 世界格子坐标（会跟着镜头走） */
  x: number;
  y: number;
  text: string;
  color: number;
  until: number;
  start: number;
}

interface FloatText extends FloatTextSpec {
  view: Text;
}

interface PendingTile {
  sprite: Sprite;
  path: string;
  index: number;
  sx: number;
  sy: number;
  row: number;
  /** 行内次序：地面 0 / 中景 1 / 前景 2 */
  order: number;
  mode: TileMode;
  alpha: number;
  /** 影子/半透明层：原版用 DrawBlend（加法混合）画 */
  blend?: boolean;
  flatOnly?: boolean;
}

/** top = 左上角对齐；bottom = 底部对齐（DrawUp / 前景物体）；tallOnly = 只画比格子高的图 */
type TileMode = 'top' | 'bottom' | 'tallOnly';

const libPath2 = (dir: string, index: number) => `Data/${dir}/${String(index).padStart(2, '0')}.Lib`;
const monsterPath = (index: number) => `Data/Monster/${String(index).padStart(3, '0')}.Lib`;

const TILE_FETCH_PER_FRAME = 260;
/** 贴图缓存上限；超了丢最老的（瓦片会重建，丢掉不影响正确性） */
const MAX_TEXTURES = 6000;
/** 最近这段时间内用过的贴图绝不销毁（屏幕上正在显示的瓦片必须留活口，否则会一块块闪没） */
const TEXTURE_HOT_MS = 8000;
/** 兜底色：万一某格瓦片还没下载/解码好，先用这个底色，避免出现"黑块撕裂" */
const DEFAULT_GROUND = 0x6a5a45;

/**
 * 行内 z 序：每一行占 ROW_Z 个名额，行内再细分
 *   0/10/20 = 平铺(地面) / 中景 / 前景   50 = 角色   100+ = 下一行
 * 地面统一加 GROUND_Z_BASE 压到最下面（原版是整屏画完 DrawFloor 再按行画 DrawObjects）。
 */
const ROW_Z = 100;
const GROUND_Z_BASE = -1000000;

/** 瓦片在行内的次序：地面 0、中景 1、前景 2 */
function tileOrderOf(mode: TileMode) {
  return mode === 'tallOnly' ? 2 : mode === 'bottom' ? 1 : 0;
}

/** 平铺尺寸：原版认为"一格"或"两格"大小的图是地面/地板，左上角对齐 */
function isFlatTile(w: number, h: number) {
  return (w === CELL_WIDTH && h === CELL_HEIGHT) || (w === CELL_WIDTH * 2 && h === CELL_HEIGHT * 2);
}

/** 地图动画节拍：C# 是每 100ms 让 AnimationCount++（GameScene.Process），门是 50ms */
const ANIM_TICK_MS = 100;
const DOOR_TICK_MS = 50;
const DOOR_AUTO_CLOSE_MS = 5000;

/** 需要每 tick 重算帧号的地图瓦片（中/前景动画、门、Shanda 动画层） */
interface AnimTile {
  sprite: Sprite;
  path: string;
  /** 基础图索引（已经减过 1） */
  base: number;
  /** 动画帧数（0 = 只有门在动） */
  frames: number;
  /** 动画节拍（越大越慢） */
  tick: number;
  kind: 'mid' | 'front' | 'tileAnim';
  /** tileAnim 专用：animationoffset ^ 0x2000 */
  animOffset: number;
  doorIndex: number;
  doorOffset: number;
  sx: number;
  sy: number;
  row: number;
  /** 行内次序：地面 0 / 中景 1 / 前景 2 */
  order: number;
  mode: TileMode;
  alpha: number;
  blend: boolean;
  /** 原版 DrawBlend 的 offSet 参数：2723~2732（灯光）要额外叠加图片自带偏移 */
  blendOffset: boolean;
  lastIndex: number;
}

interface DoorInfo {
  /** DoorState: 0 关 / 1 开中 / 2 开 / 3 关中 */
  state: number;
  imageIndex: number;
  lastTick: number;
}

export class Renderer {
  readonly store: LibStore;
  private app?: Application;
  /** 最底层：整屏兜底地面（不参与瓦片回收） */
  private backLayer = new Container();
  /** 世界层：瓦片和角色放在一起，用 zIndex 按行交织，实现"建筑挡住后面的人" */
  private worldLayer = new Container();
  /** UI 层：名字、聊天气泡永远在最上面 */
  private uiLayer = new Container();
  private tilePool: Sprite[] = [];
  private pendingTiles: PendingTile[] = [];
  private animTiles: AnimTile[] = [];
  private animCounter = -1;
  private doors = new Map<number, DoorInfo>();
  private doorsDirty = false;
  private actorSprites = new Map<number, ActorSprites>();
  /** key -> { 贴图, 元信息 }。元信息一起存，避免重建时还要等 Lib 解析 meta */
  private textures = new Map<string, { tex: Texture; meta: MImageMeta }>();
  private textureKeysByAge: string[] = [];
  /** 近期贴图未命中原因（调试用，最多 120 条） */
  missLog: string[] = [];
  private noteMiss(msg: string) {
    if (!(globalThis as any).__texDebug) return; // 排查时才开：window.__texDebug = true
    this.missLog.push(msg);
    if (this.missLog.length > 120) this.missLog.shift();
  }

  /** 这张图是"还没好"还是"根本不存在"（Lib 里存在空槽，比如 Objects6.Lib#1039~1055） */
  private textureState(path: string, index: number): 'ready' | 'wait' | 'invalid' {
    if (this.textures.has(this.textureKey(path, index))) return 'ready';
    const lib = this.store.peek(path);
    if (!lib) return 'wait';
    const meta = lib.peekMeta(index); // undefined = 还没解析完, null = 这张不存在
    if (meta === null) return 'invalid';
    return 'wait';
  }
  /** 上一次 rebuild 的统计（调试用） */
  rebuildStats = { pushed: 0, pending: 0, at: 0 };
  /** key -> 最后一次被用到的时刻（performance.now()） */
  private textureUsed = new Map<string, number>();
  /** 地面兜底：底色 + 平铺的地砖（都垫在最底下，缺图时看起来仍是地面） */
  private groundFill?: Sprite;
  private groundTile?: TilingSprite;
  private groundSet = false;
  private groundColor = DEFAULT_GROUND;
  /** 一张已知可用的地砖，用来反复尝试确定兜底色（库是异步打开的，第一次必定失败） */
  private groundProbe: { path: string; index: number } | null = null;
  private floats: FloatText[] = [];
  private floatStyleCache = new Map<string, TextStyle>();

  private map: GameMap | null = null;
  private camCellX = Number.NaN;
  private camCellY = Number.NaN;
  private camFloatX = 0;
  private camFloatY = 0;
  /** 当前这批瓦片是按哪个整格相机建的（不一致就必须在建之前重建，否则会有一帧错位闪烁） */
  private builtCamX = Number.NaN;
  private builtCamY = Number.NaN;
  private tilesDirty = true;

  width = 1024;
  height = 768;

  constructor(store = new LibStore()) {
    this.store = store;
  }

  async init(host: HTMLElement) {
    const app = new Application();
    await app.init({
      background: 0x0b0908,
      antialias: false,
      resolution: Math.min(window.devicePixelRatio || 1, 2),
      autoDensity: true,
      width: host.clientWidth || 1024,
      height: host.clientHeight || 768,
      preference: 'webgl',
    });
    app.ticker.stop(); // 用我们自己的 rAF 驱动
    host.appendChild(app.canvas);
    app.stage.addChild(this.backLayer, this.worldLayer, this.uiLayer);
    this.worldLayer.sortableChildren = true;

    // 兜底地面：垫在所有瓦片下面，任何"还没画出来"的格子都不会露出纯黑。
    // 注意放在独立的 backLayer —— 直接丢进 worldLayer 会被 rebuildTiles 回收进对象池，
    // 结果兜底块被当成瓦片挪走，屏幕上又出现黑块。
    const ground = new Sprite(Texture.WHITE);
    ground.tint = DEFAULT_GROUND;
    ground.width = this.width;
    ground.height = this.height;
    this.backLayer.addChild(ground);
    this.groundFill = ground;

    // 上面再盖一层"平铺的地砖"，缺图/未加载的格子看起来就是延续的地面而不是补丁色
    const tiled = new TilingSprite({ texture: Texture.WHITE, width: this.width, height: this.height });
    tiled.visible = false;
    this.backLayer.addChild(tiled);
    this.groundTile = tiled;
    this.app = app;
    this.resize();
    window.addEventListener('resize', () => this.resize());
    // 容器尺寸变化也要跟上（窗口拉伸、HUD 折叠、手机旋转）
    if (typeof ResizeObserver !== 'undefined') {
      new ResizeObserver(() => this.resize()).observe(host);
    }
  }

  resize() {
    if (!this.app) return;
    const host = this.app.canvas.parentElement;
    if (!host) return;
    this.width = host.clientWidth || 1024;
    this.height = host.clientHeight || 768;
    this.app.renderer.resize(this.width, this.height);
    if (this.groundFill) {
      this.groundFill.width = this.width;
      this.groundFill.height = this.height;
    }
    if (this.groundTile) {
      this.groundTile.width = this.width;
      this.groundTile.height = this.height;
    }
    this.tilesDirty = true;
  }

  setMap(map: GameMap | null) {
    this.map = map;
    this.groundSet = false;
    this.groundProbe = null;
    this.groundColor = DEFAULT_GROUND;
    if (this.groundFill) this.groundFill.tint = DEFAULT_GROUND;
    this.tilesDirty = true;
    this.animTiles = [];
    this.doors.clear();
    for (const entry of this.actorSprites.values()) {
      for (const s of [entry.body, entry.hair, entry.weapon1, entry.weapon2, entry.label, entry.bubble, entry.hpBar]) s.destroy();
    }
    this.actorSprites.clear();
  }

  setCamera(x: number, y: number) {
    this.camFloatX = x;
    this.camFloatY = y;
    const cx = Math.round(x);
    const cy = Math.round(y);
    if (cx !== this.camCellX || cy !== this.camCellY) {
      this.camCellX = cx;
      this.camCellY = cy;
      this.tilesDirty = true;
    }
  }

  /**
   * 格子 -> 屏幕像素（浮点相机，鼠标拾取/UI 用）。
   *
   * 注意：世界层（瓦片+角色）不用这个，而是用"整格相机 + 整层亚格偏移"（见 toWorldScreen），
   * 否则瓦片只在整格处跳动、角色却在慢慢滑，就会出现"背景不动、人先走完一格"的错觉。
   */
  toScreen(x: number, y: number) {
    return {
      x: (x - this.camFloatX) * CELL_WIDTH + this.width / 2,
      y: (y - this.camFloatY) * CELL_HEIGHT + this.height / 2,
    };
  }

  /** 世界层内部坐标：按整格相机摆放，再由 worldLayer.position 补偿亚格位移 */
  private toWorldScreen(x: number, y: number) {
    return {
      x: (x - this.camCellX) * CELL_WIDTH + this.width / 2,
      y: (y - this.camCellY) * CELL_HEIGHT + this.height / 2,
    };
  }

  /** 每帧把世界层整体平移，补偿"浮点相机 - 整格相机"的小数部分 */
  private syncLayerOffset() {
    if (!Number.isFinite(this.camCellX) || !Number.isFinite(this.camFloatX)) return;
    const dx = (this.camCellX - this.camFloatX) * CELL_WIDTH;
    const dy = (this.camCellY - this.camFloatY) * CELL_HEIGHT;
    this.worldLayer.position.set(dx, dy);
    // 平铺地砖跟着世界一起滚，看起来和真瓦片同向移动
    if (this.groundTile?.visible) {
      this.groundTile.tilePosition.set((dx % 100000) - this.width / 2, (dy % 100000) - this.height / 2);
    }
  }

  /** 屏幕像素 -> 格子 */
  toCell(px: number, py: number) {
    return {
      x: Math.round((px - this.width / 2) / CELL_WIDTH + this.camFloatX),
      y: Math.round((py - this.height / 2) / CELL_HEIGHT + this.camFloatY),
    };
  }

  render(now: number, actors: Iterable<Actor>) {
    if (!this.app) return;
    // 相机的整格位置和"这批瓦片是按哪个格子建的"不一致 → 本帧就得重建。
    // 只信 tilesDirty 的话，如果 setCamera 和 render 跑在不同的 rAF 回调里，
    // 就会有整整一帧是"新相机 + 旧瓦片"，看起来就是边上几块贴图闪一下。
    if (this.tilesDirty || this.camCellX !== this.builtCamX || this.camCellY !== this.builtCamY) this.rebuildTiles();
    // 库是异步打开的：兜底色/平铺地砖要一直重试到成功为止
    if (!this.groundSet && this.groundProbe) this.maybeSetGroundColor(this.groundProbe.path, this.groundProbe.index);
    this.syncLayerOffset();
    this.fillPendingTiles();
    this.processDoors(now);
    const tick = Math.floor(now / ANIM_TICK_MS);
    if (tick !== this.animCounter || this.doorsDirty) {
      this.animCounter = tick;
      this.doorsDirty = false;
      this.updateAnimTiles();
    }
    this.updateActors(now, actors);
    this.updateFloats(now);
    this.app.render();
  }

  // ---------------- 贴图 ----------------

  private textureKey(path: string, index: number) {
    return `${path}#${index}`;
  }

  /** 记住一个贴图并维护上限；返回同一个 Texture 方便链式用 */
  /** 标记"这一帧还在用"，让 LRU 不敢动它 */
  private touchTexture(key: string, now = performance.now()) {
    this.textureUsed.set(key, now);
  }

  private rememberTexture(key: string, tex: Texture, meta: MImageMeta): Texture {
    const now = performance.now();
    const hit = this.textures.get(key);
    if (hit) {
      this.touchTexture(key, now);
      return hit.tex;
    }
    this.textures.set(key, { tex, meta });
    this.textureUsed.set(key, now);
    this.textureKeysByAge.push(key);
    this.trimTextures(now);
    return tex;
  }

  /**
   * LRU 回收。
   *
   * 注意：**不能**把最近还在用的贴图销毁 —— 那些瓦片正钉在屏幕上，纹理一销毁就整块变空白，
   * 下一次重建又重新解码出来，于是走动时"有的地方一会消失一会又出现"。所以只有
   * 超过 TEXTURE_HOT_MS 没被碰过的才允许回收；真到上限也宁可短暂超一点。
   */
  private trimTextures(now: number) {
    let guard = this.textureKeysByAge.length + 1;
    while (this.textures.size > MAX_TEXTURES && guard-- > 0) {
      const oldKey = this.textureKeysByAge.shift();
      if (oldKey === undefined) break;
      const lastUsed = this.textureUsed.get(oldKey) ?? 0;
      if (now - lastUsed < TEXTURE_HOT_MS) {
        this.textureKeysByAge.push(oldKey); // 还是热的，挪到队尾下次再看
        continue;
      }
      const old = this.textures.get(oldKey);
      this.textures.delete(oldKey);
      this.textureUsed.delete(oldKey);
      // 兜底平铺用的那张贴图永远留着
      if (this.groundTile && old?.tex === this.groundTile.texture) continue;
      // 只销毁 Texture，不动底层 ImageBitmap（它还在 Lib 的位图缓存里，可以重新建纹理）
      try { old?.tex.destroy(false); } catch { /* ignore */ }
      const hash = oldKey.lastIndexOf('#');
      if (hash > 0) {
        const libPath = oldKey.slice(0, hash);
        const imgIdx = Number(oldKey.slice(hash + 1));
        void this.store.peek(libPath)?.forget?.(imgIdx);
      }
    }
  }

  /** 只取贴图，不做任何副作用（给兜底平铺用） */
  private peekTextureRaw(path: string, index: number): Texture | null {
    const key = this.textureKey(path, index);
    return this.textures.get(key)?.tex ?? null;
  }

  /** 用第一张解出来的 back 瓦片决定兜底色，别的图未就绪时看起来像地面而不是黑 */
  private maybeSetGroundColor(path: string, index: number) {
    if (this.groundSet) return;
    // 注意：一开始库还没打开（peek 为 null），不能提前打上"已设置"标记，否则永远补不上
    const lib = this.store.peek(path);
    if (!lib) return;
    void lib.image(index).then((bmp) => {
      if (!bmp) return;
      try {
        // 取整张图里"不透明像素"的平均色（中心点可能正好是透明的）
        const w = Math.min(96, bmp.width);
        const h = Math.min(64, bmp.height);
        const c = document.createElement('canvas');
        c.width = w;
        c.height = h;
        const ctx = c.getContext('2d');
        if (!ctx) return;
        ctx.drawImage(bmp, 0, 0, w, h);
        const d = ctx.getImageData(0, 0, w, h).data;
        let r = 0, g = 0, b = 0, n = 0;
        for (let i = 0; i < d.length; i += 4) {
          if (d[i + 3] < 200) continue;
          r += d[i]; g += d[i + 1]; b += d[i + 2]; n++;
        }
        if (n < w * h * 0.3) return; // 太透的图不当参考
        this.groundSet = true;
        this.groundColor = ((Math.round(r / n) << 16) | (Math.round(g / n) << 8) | Math.round(b / n));
        if (this.groundFill) this.groundFill.tint = this.groundColor;
        // 用同一张地砖平铺，缺图的地方就是"延续的地面"
        if (this.groundTile && bmp.width >= 32 && bmp.height >= 32) {
          const tex = this.peekTextureRaw(path, index) ?? Texture.from(bmp);
          this.groundTile.texture = tex;
          this.groundTile.visible = true;
        }
      } catch {
        /* ignore */
      }
    });
  }

  /** 同步取贴图；没有就返回 null 并触发异步加载 */
  private peekTexture(path: string, index: number): { tex: Texture; meta: MImageMeta } | null {
    const key = this.textureKey(path, index);
    // 缓存命中就不用再问 Lib —— 否则每次镜头换格重建瓦片，只要 meta 还没解析好
    // 就会被判成"没贴图"，屏幕上一块贴图消失一帧再冒出来（走一步闪一下就是这个）。
    const cached = this.textures.get(key);
    if (cached) {
      this.touchTexture(key);
      return cached;
    }

    const lib = this.store.peek(path);
    if (!lib) {
      this.noteMiss(`nolib:${path}`);
      return null;
    }
    const meta = lib.peekMeta(index);
    if (!meta) {
      this.noteMiss(`nometa:${path}#${index}`);
      return null;
    }

    const bmp = lib.peekTexture(index);
    if (bmp) {
      const tex = this.rememberTexture(key, Texture.from(bmp), meta);
      return { tex, meta };
    }

    this.noteMiss(`nobmp:${key}`);
    void lib.image(index).then((image) => {
      if (!image || this.textures.has(key)) return;
      this.rememberTexture(key, Texture.from(image), meta);
    });
    return null;
  }

  // ---------------- 地图瓦片 ----------------

  private rebuildTiles() {
    this.tilesDirty = false;
    this.builtCamX = this.camCellX;
    this.builtCamY = this.camCellY;
    const map = this.map;
    // 注意：worldLayer 里除了瓦片还有角色的身体/武器（为了和场景按行交错遮挡）。
    // 只能回收"瓦片精灵"，否则角色的精灵会被当成瓦片复用 → 地图贴图一块块闪、人物还会串位。
    for (const child of [...this.worldLayer.children]) {
      const sprite = child as Sprite & { __isTile?: boolean };
      if (!sprite.__isTile) continue;
      this.worldLayer.removeChild(sprite);
      sprite.visible = false;
      this.tilePool.push(sprite);
    }
    this.pendingTiles = [];
    this.animTiles = [];
    this.rebuildStats = { pushed: 0, pending: 0, at: performance.now() };
    if (!map) return;

    const rangeX = Math.ceil(this.width / 2 / CELL_WIDTH) + 2;
    const rangeY = Math.ceil(this.height / 2 / CELL_HEIGHT) + 3;
    // back 瓦片只锚在 (偶数, 偶数) 格；窗口边界对齐到偶数，否则边缘会缺一整条
    const rawStartX = Math.max(0, this.camCellX - rangeX);
    const rawEndX = Math.min(map.width - 1, this.camCellX + rangeX);
    const rawStartY = Math.max(0, this.camCellY - rangeY);
    const rawEndY = Math.min(map.height - 1, this.camCellY + rangeY + 4);
    const startX = Math.max(0, rawStartX - (rawStartX % 2));
    const startY = Math.max(0, rawStartY - (rawStartY % 2));
    const endX = Math.min(map.width - 1, rawEndX + (rawEndX % 2));
    const endY = Math.min(map.height - 1, rawEndY + (rawEndY % 2));

    // 先按 lib 归并预热请求，避免每张瓦片单独一个 HTTP 请求
    const want = new Map<string, Set<number>>();
    const addWant = (libIdx: number, imgIdx: number) => {
      if (libIdx < 0 || imgIdx < 0) return;
      const path = mapLibPath(libIdx);
      if (!path) return;
      let set = want.get(path);
      if (!set) want.set(path, (set = new Set()));
      set.add(imgIdx);
    };

    // 视野外再预取几圈：镜头移动时新进画面的瓦片已经在解码队列里，不会先黑一片
    const ringX = 3;
    const ringY = 3;
    for (let y = startY - ringY; y <= endY + ringY; y++) {
      for (let x = startX - ringX; x <= endX + ringX; x++) {
        const c = map.at(x, y);
        if (!c) continue;
        const inner = x >= startX && x <= endX && y >= startY && y <= endY;
        const wantRing = !inner;
        if (y % 2 === 0 && x % 2 === 0 && c.backImage !== 0 && c.backIndex !== -1) {
          const idx = (c.backImage & 0x1fffffff) - 1;
          addWant(c.backIndex, idx);
          if (wantRing) this.prefetchTile(c.backIndex, idx);
        }
        if (c.midIndex !== -1 && c.midImage > 0) {
          addWant(c.midIndex, c.midImage - 1);
          if (wantRing) this.prefetchTile(c.midIndex, c.midImage - 1);
        }
        if (c.frontIndex !== -1 && c.frontIndex !== 200 && c.frontImage !== -32768) {
          const idx = (c.frontImage & 0x7fff) - 1;
          addWant(c.frontIndex, idx);
          if (wantRing) this.prefetchTile(c.frontIndex, idx);
        }
      }
    }
    for (const [path, indices] of want) {
      void this.store.prime(path).then((lib) => lib?.warm(indices));
    }

    // 地图里有些格子压根没画地面（原版是纯黑），先用窗口内任意一张地砖当补丁
    let fallbackBack: { path: string; index: number } | null = null;
    let lastBack: { path: string; index: number } | null = null;
    for (let y = startY; y <= endY && !fallbackBack; y++) {
      for (let x = startX; x <= endX; x++) {
        if (y % 2 !== 0 || x % 2 !== 0) continue;
        const probe = map.at(x, y);
        if (!probe || probe.backImage === 0 || probe.backIndex === -1) continue;
        const path = mapLibPath(probe.backIndex);
        if (!path) continue;
        fallbackBack = { path, index: (probe.backImage & 0x1fffffff) - 1 };
        this.groundProbe = fallbackBack;
        break;
      }
    }

    for (let y = startY; y <= endY; y++) {
      for (let x = startX; x <= endX; x++) {
        const c = map.at(x, y);
        if (!c) continue;
        const sx = (x - this.camCellX) * CELL_WIDTH + this.width / 2;
        const sy = (y - this.camCellY) * CELL_HEIGHT + this.height / 2;

        // Back：只有偶数格有，左上角对齐（一张 96x64 覆盖 2x2 格）
        if (y % 2 === 0 && x % 2 === 0) {
          if (c.backImage !== 0 && c.backIndex !== -1) {
            const idx = (c.backImage & 0x1fffffff) - 1;
            const path = mapLibPath(c.backIndex);
            if (path) lastBack = { path, index: idx };
            this.pushTile(c.backIndex, idx, sx, sy, y, 'top', 1);
          } else {
            // 地图本身就没画这一块（原版这里是纯黑），拿同行的地砖补上，免得看起来像撕裂
            const patch = lastBack ?? fallbackBack;
            if (patch) this.pushTileByPath(patch.path, patch.index, sx, sy, y, 1);
          }
        }

        // Middle：48x32 / 96x64 铺在格子里，其它（长条）按底部对齐（C# 的 DrawUp）
        if (c.midIndex !== -1 && c.midImage > 0) {
          const midFrames = c.midAnimFrame > 0 && c.midAnimFrame < 255 ? c.midAnimFrame & 0x0f : 0;
          // 动画层：C# 在 DrawObjects 里用 DrawUp 叠一层会动的图
          // 原版：低位 nibble 非 0 时 blend = true，只有 8/10 帧的组才真的用加法混合画
          const midBlend = midFrames === 8 || midFrames === 10;
          if (midFrames > 0) {
            this.pushAnimTile(c.midIndex, c.midImage - 1, sx, sy, y, 'bottom', {
              frames: midFrames,
              tick: c.midAnimTick,
              kind: 'mid',
              blend: midBlend,
            });
          }
          // 底图：平铺尺寸的画在格子里（没动画时也会走到这里）
          if (midFrames === 0) {
            this.pushTile(c.midIndex, c.midImage - 1, sx, sy, y, 'bottom', 1);
          } else {
            this.pushTile(c.midIndex, c.midImage - 1, sx, sy, y, 'top', 1, true);
          }
        }

        // Front：C# 里平铺尺寸且无动画的前景会被跳过，其余按底部对齐
        if (c.frontIndex !== -1 && c.frontIndex !== 200 && c.frontImage !== -32768) {
          const frontImage = c.frontImage & 0x7fff;
          const rawAnim = c.frontAnimFrame & 0x7f;
          const base = frontImage - 1;
          // 原版：frontAnimFrame 的 0x80 位 = 影子/半透明层（DrawBlend）
          const frontBlend = (c.frontAnimFrame & 0x80) !== 0;
          if (rawAnim > 0 || c.doorIndex > 0) {
            this.pushAnimTile(c.frontIndex, base, sx, sy, y, 'tallOnly', {
              frames: rawAnim,
              tick: c.frontAnimTick,
              kind: 'front',
              doorIndex: c.doorIndex,
              doorOffset: c.doorOffset,
              blend: frontBlend,
            });
          } else {
            this.pushTile(c.frontIndex, base, sx, sy, y, 'tallOnly', 1, false, frontBlend);
          }
        }

        // Shanda 地图的独立动画层（MapLibs[190] AniTiles1）
        if (c.tileAnimImage > 0 && c.tileAnimFrames > 0) {
          this.pushAnimTile(190, c.tileAnimImage - 1, sx, sy, y, 'bottom', {
            frames: c.tileAnimFrames,
            tick: 0,
            kind: 'tileAnim',
            animOffset: c.tileAnimOffset ^ 0x2000,
          });
        }
      }
    }
  }

  /** 只解码不画：给外圈预取用 */
  private prefetchTile(libIdx: number, imgIdx: number) {
    if (imgIdx < 0) return;
    const path = mapLibPath(libIdx);
    if (!path) return;
    const key = this.textureKey(path, imgIdx);
    if (this.textures.has(key)) return;
    const lib = this.store.peek(path);
    if (!lib) return;
    void lib.meta(imgIdx)
      .then((meta) => (meta && !this.textures.has(key) ? lib.image(imgIdx).then((bmp) => bmp && this.rememberTexture(key, Texture.from(bmp), meta)) : null))
      .catch(() => null);
  }

  /**
   * 摆一张瓦片。
   *
   * 对齐规则照抄原版：**平铺尺寸**（48x32 / 96x64）按格子左上角画（C# 的 DrawFloor），
   * 其它（高的建筑/树等）按**格子底边**对齐画。
   *
   * 注意底边是 `sy + CELL_HEIGHT`，不是 `sy`：C# DrawObjects 里
   * `drawY = (y - User.Y + OffSetY + 1) * CellHeight`（地板那遍没有 `+1`），
   * 再交给 `DrawUp()` 做 `y -= mi.Height`。所以高图的底边压在本格的底边、
   * 顶边在 `sy + 32 - 高`。少加这 32 会让所有建筑/树/路灯整体上浮一格，
   * 和地面/平铺墙脚对不上 —— 表现就是"墙里挖掉一条 32px 的沙色横带"。
   *
   * 我们只有一遍绘制，所以每张图都只画一次；千万不能像原版那样"平铺尺寸的跳过、地板里画过"，
   * 因为这里没有那遍地板绘制 —— 跳过就等于整块建筑贴图消失、露出地面。
   */
  private placeTile(
    sprite: Sprite,
    hit: { tex: Texture; meta: MImageMeta },
    item: { sx: number; sy: number; row: number; alpha: number; order?: number; blend?: boolean; blendOffset?: boolean },
  ) {
    sprite.texture = hit.tex;
    sprite.alpha = item.alpha;
    sprite.visible = true;
    const flat = isFlatTile(hit.meta.width, hit.meta.height);
    sprite.position.set(item.sx, flat ? item.sy : item.sy + CELL_HEIGHT - hit.meta.height);
    // 平铺的（地面/地板）永远压在下面：原版是先整屏画完 DrawFloor、再按行画 DrawObjects。
    // 如果只按行号排序，下面几行的地面 z 会比上面那栋楼的 z 大，把楼"啃"出一块块台阶状的洞。
    const order = item.order ?? 0;
    sprite.zIndex = flat ? GROUND_Z_BASE + item.row * ROW_Z + order : item.row * ROW_Z + order * 10;
    // 原版 DrawBlend：SourceAlpha + One（加法混合）。影子图就是靠这个变半透明的，
    // 当成普通不透明图硬贴上去 → 屏幕上一块块硬边的"沙色方块"。
    sprite.blendMode = item.blend ? 'add' : 'normal';
    // 原版对 2723~2732 这些图会额外叠加图片自带的偏移
    if (item.blend && item.blendOffset) {
      sprite.position.set(sprite.x + hit.meta.x, sprite.y + hit.meta.y);
    }
  }

  private pushTile(
    libIdx: number,
    imgIdx: number,
    sx: number,
    sy: number,
    row: number,
    mode: TileMode,
    alpha: number,
    flatOnly = false,
    blend = false,
  ) {
    if (imgIdx < 0) return;
    const path = mapLibPath(libIdx);
    if (!path) return;

    const sprite = (this.tilePool.pop() ?? new Sprite()) as Sprite & { __isTile?: boolean };
    sprite.__isTile = true;
    sprite.label = `${path}#${imgIdx}`;
    sprite.alpha = alpha;
    sprite.position.set(sx, sy);
    if (!sprite.parent) this.worldLayer.addChild(sprite);

    sprite.zIndex = row * ROW_Z + tileOrderOf(mode) * 10;
    const item = { sprite, path, index: imgIdx, sx, sy, row, mode, alpha, flatOnly, blend,
      blendOffset: blend && imgIdx >= 2723 && imgIdx <= 2732, order: tileOrderOf(mode) };
    if (mode === 'top') this.maybeSetGroundColor(path, imgIdx);
    const hit = this.peekTexture(path, imgIdx);
    this.rebuildStats.pushed++;
    if (hit) {
      // 注意：这里**不能**像原版 DrawObjects 那样"平铺尺寸就跳过" ——
      // 那句话成立的前提是地板已经由 DrawFloor 画过一遍，我们只有一遍绘制。
      this.placeTile(sprite, hit, item);
    } else {
      sprite.visible = false;
      this.pendingTiles.push(item);
      this.rebuildStats.pending++;
    }
  }

  /** 用已知路径直接摆一张静态瓦片（地图缺图时补洞用） */
  private pushTileByPath(path: string, imgIdx: number, sx: number, sy: number, row: number, alpha: number) {
    if (imgIdx < 0) return;
    const sprite = (this.tilePool.pop() ?? new Sprite()) as Sprite & { __isTile?: boolean };
    sprite.__isTile = true;
    sprite.label = `${path}#${imgIdx}`;
    sprite.alpha = alpha;
    sprite.position.set(sx, sy);
    sprite.zIndex = row * ROW_Z;
    if (!sprite.parent) this.worldLayer.addChild(sprite);

    const item = { sprite, path, index: imgIdx, sx, sy, row, mode: 'top' as TileMode, alpha, order: 0 };
    const hit = this.peekTexture(path, imgIdx);
    if (hit) this.placeTile(sprite, hit, item);
    else {
      sprite.visible = false;
      this.pendingTiles.push(item);
    }
  }

  /** 建一个会动的瓦片（中/前景动画、门、Shanda 动画层） */
  private pushAnimTile(
    libIdx: number,
    imgIdx: number,
    sx: number,
    sy: number,
    row: number,
    mode: TileMode,
    opts: {
      frames: number;
      tick: number;
      kind: AnimTile['kind'];
      animOffset?: number;
      doorIndex?: number;
      doorOffset?: number;
      blend?: boolean;
    },
  ) {
    if (imgIdx < 0) return;
    const path = mapLibPath(libIdx);
    if (!path) return;

    const sprite = (this.tilePool.pop() ?? new Sprite()) as Sprite & { __isTile?: boolean };
    sprite.__isTile = true;
    sprite.label = `${path}#${imgIdx}`;
    sprite.zIndex = row * ROW_Z + tileOrderOf(mode) * 10;
    if (!sprite.parent) this.worldLayer.addChild(sprite);

    const tile: AnimTile = {
      sprite,
      path,
      base: imgIdx,
      frames: opts.frames,
      tick: opts.tick,
      kind: opts.kind,
      animOffset: opts.animOffset ?? 0,
      doorIndex: opts.doorIndex ?? 0,
      doorOffset: opts.doorOffset ?? 0,
      sx,
      sy,
      row,
      order: opts.kind === 'front' ? 2 : 1,
      blend: opts.blend === true,
      blendOffset: opts.blend === true && imgIdx >= 2723 && imgIdx <= 2732,
      mode,
      alpha: 1,
      lastIndex: -1,
    };

    // 关键：立刻按"当前动画帧/门状态"落图。只摆基础帧的话，每次镜头换格重建瓦片，
    // 火把/水花/门都会闪回第 0 帧（有的组第 0 帧是空的，看起来就是"贴图一闪一闪"）。
    const index = this.animIndexFor(tile);
    const exact = this.peekTexture(path, index);
    if (exact) {
      tile.lastIndex = index;
      sprite.label = `${path}#${index}`;
      this.placeTile(sprite, exact, tile);
    } else {
      // 当前帧还没解码好：先拿基础帧顶着（别留空白），并把 lastIndex 置 -1 让下一拍重试
      const fallback = this.peekTexture(path, imgIdx);
      tile.lastIndex = -1;
      if (fallback) {
        sprite.label = `${path}#${imgIdx}`;
        this.placeTile(sprite, fallback, tile);
      } else {
        sprite.visible = false;
      }
    }
    this.animTiles.push(tile);
  }

  /** 每帧补一批还没就绪的瓦片贴图 */
  private fillPendingTiles() {
    if (!this.pendingTiles.length) return;
    const remaining: PendingTile[] = [];
    let budget = TILE_FETCH_PER_FRAME;
    let i = 0;
    for (; i < this.pendingTiles.length && budget > 0; i++, budget--) {
      const item = this.pendingTiles[i];
      const hit = this.peekTexture(item.path, item.index);
      if (hit) {
        this.placeTile(item.sprite, hit, item);
      } else if (this.textureState(item.path, item.index) !== 'invalid') {
        remaining.push(item);
      }
    }
    for (; i < this.pendingTiles.length; i++) remaining.push(this.pendingTiles[i]);
    this.pendingTiles = remaining;
  }

  // ---------------- 门 ----------------

  /** 服务端 S.Opendoor：关=开始关，开=开始开（对应 MapControl.OpenDoor） */
  setDoor(index: number, close: boolean) {
    if (index <= 0) return;
    let door = this.doors.get(index);
    if (!door) {
      door = { state: 0, imageIndex: 0, lastTick: 0 };
      this.doors.set(index, door);
    }
    door.state = close ? 3 : door.state === 2 ? 2 : 1;
    door.imageIndex = 0;
    door.lastTick = performance.now();
    this.doorsDirty = true;
  }

  private processDoors(now: number) {
    for (const door of this.doors.values()) {
      if (door.state === 1 || door.state === 3) {
        // 开/关过程：每 50ms 走一帧，两帧走完换状态
        if (now - door.lastTick >= DOOR_TICK_MS) {
          door.lastTick = now;
          door.imageIndex++;
          if (door.imageIndex > 1) {
            door.imageIndex = 0;
            door.state = (door.state + 1) % 4;
          }
          this.doorsDirty = true;
        }
      } else if (door.state === 2 && now - door.lastTick >= DOOR_AUTO_CLOSE_MS) {
        door.lastTick = now;
        door.state = 3;
        this.doorsDirty = true;
      }
    }
  }

  /** 门当前状态：0 关 / 1 开中 / 2 开 / 3 关中；未知返回 0 */
  doorState(doorIndex: number) {
    return this.doors.get(doorIndex)?.state ?? 0;
  }

  private doorImageIndex(doorIndex: number) {
    const door = this.doors.get(doorIndex);
    if (!door || door.state === 0) return 0;
    return door.imageIndex + 1;
  }

  // ---------------- 动画瓦片 ----------------

  /** 每 100ms 重算可见动画瓦片的图号；C# 是整块地板重画，这里只动动画槽位 */
  /** 按当前动画计数算出这个瓦片该显示第几张图（含门状态） */
  private animIndexFor(tile: AnimTile, cnt = Math.max(0, this.animCounter)) {
    let index = tile.base;

    if (tile.kind === 'tileAnim') {
      // 照抄 DrawObjects 里的 Shanda 动画层
      index += tile.animOffset * (cnt % Math.max(1, tile.frames));
    } else if (tile.frames > 0) {
      const span = tile.frames + tile.frames * tile.tick;
      index += Math.floor((cnt % Math.max(1, span)) / (1 + tile.tick));
    }

    if (tile.doorIndex > 0) {
      index += this.doorImageIndex(tile.doorIndex) * tile.doorOffset;
    }
    return index;
  }

  private updateAnimTiles() {
    if (!this.animTiles.length) return;
    const cnt = Math.max(0, this.animCounter);

    for (const tile of this.animTiles) {
      const index = this.animIndexFor(tile, cnt);

      // 已经不可见的（贴图还没到位）每拍都重试，不能只等索引变化
      if (index === tile.lastIndex && tile.sprite.visible) continue;
      const hit = this.peekTexture(tile.path, index);
      if (!hit) continue; // 还没解码好，下一拍再试

      tile.lastIndex = index;
      this.placeTile(tile.sprite, hit, tile);
    }
  }

  // ---------------- 飘字（伤害数字等） ----------------

  private floatStyle(color: number) {
    const key = String(color);
    let style = this.floatStyleCache.get(key);
    if (!style) {
      style = new TextStyle({
        fontFamily: 'Tahoma, "Microsoft YaHei", sans-serif',
        fontSize: 14,
        fontWeight: '700',
        fill: color,
        stroke: { color: 0x000000, width: 3 },
      });
      this.floatStyleCache.set(key, style);
    }
    return style;
  }

  addFloatText(spec: FloatTextSpec) {
    const view = new Text({ text: spec.text, style: this.floatStyle(spec.color) });
    view.anchor.set(0.5, 1);
    this.uiLayer.addChild(view);
    this.floats.push({ ...spec, view });
  }

  private updateFloats(now: number) {
    if (!this.floats.length) return;
    const alive: FloatText[] = [];
    for (const f of this.floats) {
      if (now >= f.until) {
        f.view.destroy();
        continue;
      }
      const t = (now - f.start) / Math.max(1, f.until - f.start);
      const screen = this.toScreen(f.x, f.y);
      f.view.position.set(screen.x, screen.y - 34 - t * 26);
      f.view.alpha = 1 - t * t;
      f.view.zIndex = 100000;
      alive.push(f);
    }
    this.floats = alive;
  }

  /**
   * 屏幕坐标命中哪个角色（返回最上层的）。
   * 用"身体矩形"而不是精灵包围盒：精灵图带 X/Y 偏移，纯包围盒边缘会漏点，
   * 点击体验也不稳定（原版是逐像素判断，这里用近似框足够）。
   */
  hitTestActor(px: number, py: number): number | null {
    // 世界层有亚格平移，先换算回去
    const wx = px - this.worldLayer.x;
    const wy = py - this.worldLayer.y;
    let best: { id: number; row: number } | null = null;
    for (const [id, entry] of this.actorSprites) {
      if (!entry.alive) continue;
      // 脚下格子 + 上方身高
      if (Math.abs(wx - entry.screenX) > CELL_WIDTH / 2) continue;
      if (wy < entry.screenY - 76 || wy > entry.screenY + 16) continue;
      if (!best || entry.row >= best.row) best = { id, row: entry.row };
    }
    return best?.id ?? null;
  }

  // ---------------- 角色 ----------------

  private actorEntry(actor: Actor): ActorSprites {
    let entry = this.actorSprites.get(actor.id);
    if (entry) return entry;
    const style = new TextStyle({
      fontFamily: 'Tahoma, "Microsoft YaHei", sans-serif',
      fontSize: 12,
      fill: 0xffffff,
      stroke: { color: 0x000000, width: 3 },
    });
    const bubbleStyle = new TextStyle({
      fontFamily: 'Tahoma, "Microsoft YaHei", sans-serif',
      fontSize: 12,
      fill: 0xffe08a,
      stroke: { color: 0x000000, width: 3 },
    });
    entry = {
      body: new Sprite(),
      hair: new Sprite(),
      weapon1: new Sprite(),
      weapon2: new Sprite(),
      label: new Text({ text: actor.name, style }),
      bubble: new Text({ text: '', style: bubbleStyle }),
      hpBar: new Graphics(),
      screenX: 0,
      screenY: 0,
      row: 0,
      alive: true,
    };
    entry.label.anchor.set(0.5, 1);
    entry.bubble.anchor.set(0.5, 1);
    entry.bubble.visible = false;
    this.worldLayer.addChild(entry.body, entry.hair, entry.weapon1, entry.weapon2);
    this.uiLayer.addChild(entry.hpBar, entry.bubble, entry.label);
    this.actorSprites.set(actor.id, entry);
    return entry;
  }

  private updateActors(now: number, actors: Iterable<Actor>) {
    const alive = new Set<number>();
    for (const actor of actors) {
      alive.add(actor.id);
      const sprites = this.actorEntry(actor);
      const pos = actor.renderPos(now);
      // 身体/武器在世界层（会被亚格平移带着走），名字/血条/气泡在 UI 层（用浮点相机直接定位）
      const screen = this.toWorldScreen(pos.x, pos.y);
      const screenUi = this.toScreen(pos.x, pos.y);
      const row = Math.round(pos.y) + 1;
      const depth = row * ROW_Z + 50;
      sprites.screenX = screen.x;
      sprites.screenY = screen.y;
      sprites.row = row;
      sprites.alive = !actor.dead && !actor.hidden;

      if (actor.kind === 'player') this.updatePlayer(actor, sprites, screen, depth, now);
      else this.updateMonster(actor, sprites, screen, depth, now);

      /**
       * 名字/血条/气泡要挂在**角色贴图的头顶**，不是挂在格子坐标上。
       * 原版是拿 DisplayRectangle（= DrawLocation + BodyLibrary.GetTrueSize(当前帧)）来定位的，
       * 所以高矮不同的角色/怪，名字高度自动跟着变；写死一个 -42 就会有的穿进身体、有的飘太高。
       */
      const bodyTopOffset = sprites.body.visible ? sprites.body.y - screen.y : -48; // 负值，贴图顶部相对格子左上角
      const labelY = screenUi.y + bodyTopOffset - 2; // 名字底边 = 头顶上方 2px

      if (actor.chatUntil > now && actor.chatText) {
        sprites.bubble.text = actor.chatText;
        sprites.bubble.visible = true;
        sprites.bubble.position.set(screenUi.x, labelY - 18);
        sprites.bubble.zIndex = depth;
      } else if (sprites.bubble.visible) {
        sprites.bubble.visible = false;
      }

      sprites.label.text = actor.name;
      sprites.label.visible = !actor.hidden && !actor.dead;
      sprites.label.position.set(screenUi.x, actor.dead ? labelY + 35 : labelY);
      sprites.label.zIndex = depth;

      const showHp = actor.hpPercent !== null && now < actor.hpUntil && !actor.dead;
      sprites.hpBar.visible = showHp;
      if (showHp) {
        const pct = Math.max(0, Math.min(1, (actor.hpPercent ?? 0) / 100));
        const w = 40;
        sprites.hpBar.clear();
        sprites.hpBar.rect(-w / 2 - 1, 0, w + 2, 5).fill({ color: 0x000000, alpha: 0.75 });
        sprites.hpBar.rect(-w / 2, 1, w * pct, 3).fill({ color: pct > 0.5 ? 0x63c463 : pct > 0.2 ? 0xd9b445 : 0xc4553f, alpha: 0.95 });
        // 血条压在名字下面、头顶上面
        sprites.hpBar.position.set(screenUi.x, labelY - 16);
        sprites.hpBar.zIndex = depth;
      }
    }

    for (const [id, sprites] of this.actorSprites) {
      if (alive.has(id)) continue;
      for (const s of [sprites.body, sprites.hair, sprites.weapon1, sprites.weapon2, sprites.label, sprites.bubble, sprites.hpBar]) s.destroy();
      this.actorSprites.delete(id);
    }
  }


  private applyFrameImage(sprite: Sprite, path: string | null, index: number, screen: { x: number; y: number }, depth: number) {
    if (!path || index < 0) {
      sprite.visible = false;
      return;
    }
    const hit = this.peekTexture(path, index);
    if (!hit) {
      sprite.visible = false;
      return;
    }
    sprite.visible = true;
    sprite.texture = hit.tex;
    sprite.position.set(screen.x + hit.meta.x, screen.y + hit.meta.y);
    sprite.zIndex = depth;
  }

  private updatePlayer(actor: Actor, sprites: ActorSprites, screen: { x: number; y: number }, depth: number, now: number) {
    const { spec, index, offset } = actor.currentFrame(now, PLAYER_FRAMES);
    const look = actor.look;
    const walking = actor.action === MirAction.Walking || actor.action === MirAction.Running;
    const female = look.gender === MirGender.Female;

    // 对应 PlayerObject.SetLibraries 的选择规则
    const useArcherLibs = look.cls === MirClass.Archer && walking && look.weapon >= 200;
    const useAssassinLibs = look.cls === MirClass.Assassin;

    const armourDir = useArcherLibs ? 'ARArmour' : useAssassinLibs ? 'AArmour' : 'CArmour';
    const hairDir = useArcherLibs ? 'ARHair' : useAssassinLibs ? 'AHair' : 'CHair';
    const armourOffset = female ? (useArcherLibs ? 352 : 808) : 0;
    const hairOffset = female ? (useArcherLibs ? 352 : 808) : 0;
    const weaponOffset = female ? (useArcherLibs ? 352 : 416) : 0;

    const frame = spec.start + offset * actor.direction + index;

    this.applyFrameImage(sprites.body, libPath2(armourDir, look.armour), frame + armourOffset, screen, depth);
    this.applyFrameImage(sprites.hair, libPath2(hairDir, look.hair), frame + hairOffset, screen, depth + 1);

    if (look.weapon >= 0) {
      if (useArcherLibs) {
        this.applyFrameImage(sprites.weapon2, libPath2('ARWeapon', look.weapon - 200), frame + weaponOffset, screen, depth);
        sprites.weapon1.visible = false;
      } else if (useAssassinLibs && look.weapon >= 100) {
        const w = String(look.weapon - 100).padStart(2, '0');
        this.applyFrameImage(sprites.weapon1, `Data/AWeapon/${w} L.Lib`, frame + weaponOffset, screen, depth);
        this.applyFrameImage(sprites.weapon2, `Data/AWeapon/${w} R.Lib`, frame + weaponOffset, screen, depth);
      } else {
        this.applyFrameImage(sprites.weapon1, libPath2('CWeapon', look.weapon), frame + weaponOffset, screen, depth);
        sprites.weapon2.visible = false;
      }
    } else {
      sprites.weapon1.visible = false;
      sprites.weapon2.visible = false;
    }
  }

  private updateMonster(actor: Actor, sprites: ActorSprites, screen: { x: number; y: number }, depth: number, now: number) {
    const path = monsterPath(actor.monsterImage);
    const lib = this.store.peek(path);
    const action = actor.action;
    const libFrame = lib?.frame(action);
    const spec = libFrame
      ? { start: libFrame.start, count: libFrame.count, skip: libFrame.skip, interval: libFrame.interval }
      : (MONSTER_FRAMES[action] ?? MONSTER_FRAMES[MirAction.Standing]);

    const elapsed = now - actor.actionStart;
    let index: number;
    if (action === MirAction.Walking || action === MirAction.Running) {
      index = Math.min(spec.count - 1, Math.floor(elapsed / Math.max(1, spec.interval)));
    } else if (action === MirAction.Dead) {
      index = 0;
    } else {
      index = Math.floor(elapsed / Math.max(1, spec.interval)) % Math.max(1, spec.count);
    }

    const frame = spec.start + (spec.count + spec.skip) * actor.direction + index;
    this.applyFrameImage(sprites.body, path, frame, screen, depth);
    sprites.hair.visible = false;
    sprites.weapon1.visible = false;
    sprites.weapon2.visible = false;
  }
}
