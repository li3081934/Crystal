/**
 * .Lib v2/v3 解码器（图片库）
 *
 * 结构（对照 Client/MirGraphics/MLibrary.cs）：
 *   int32 version          // 2 或 3
 *   int32 count
 *   [v3] int32 frameSeek   // FrameSet 表偏移
 *   int32 index[count]     // 每张图的记录偏移
 *   记录： int16 w,h,x,y,shadowX,shadowY | uint8 shadow | int32 length | gzip(data)
 *   shadow 最高位为 1 表示还有一层 mask（Phase 0 不处理）
 *   像素：D3D A8R8G8B8 → 内存字节序 B,G,R,A，需要换回 RGBA
 *
 * 关键：Tiles.Lib 本体 430MB，浏览器绝对不能整体下载。
 * 所以这里用 HTTP Range + 64KB 块缓存，只取需要的图片记录；
 * warm() 负责把一批图片索引合并成少量区间请求预取（进图/镜头移动时调用）。
 */

export interface MImageMeta {
  width: number;
  height: number;
  x: number;
  y: number;
  shadowX: number;
  shadowY: number;
  shadow: number;
  length: number;
  dataOffset: number;
  hasMask: boolean;
}

export interface LibFrame {
  start: number;
  count: number;
  skip: number;
  interval: number;
  effectStart: number;
  effectCount: number;
  effectSkip: number;
  effectInterval: number;
  reverse: boolean;
  blend: boolean;
}

export async function gunzip(data: Uint8Array): Promise<Uint8Array> {
  const ds = new DecompressionStream('gzip');
  const stream = new Blob([data as BlobPart]).stream().pipeThrough(ds);
  const buf = await new Response(stream).arrayBuffer();
  return new Uint8Array(buf);
}

/** D3D A8R8G8B8（内存里是 B,G,R,A）-> RGBA */
export function bgraToRgba(src: Uint8Array | Uint8ClampedArray, out?: Uint8ClampedArray): Uint8ClampedArray {
  const dst = out ?? new Uint8ClampedArray(src.length);
  for (let p = 0; p + 3 < src.length; p += 4) {
    dst[p] = src[p + 2];
    dst[p + 1] = src[p + 1];
    dst[p + 2] = src[p];
    dst[p + 3] = src[p + 3];
  }
  return dst;
}

const BLOCK_SIZE = 64 * 1024;
const MAX_BLOCKS = 700; // ~44MB 上限

interface Source {
  readonly size: number;
  read(offset: number, length: number): Promise<Uint8Array>;
}

class BufferSource implements Source {
  readonly size: number;
  private buf: Uint8Array;
  constructor(buf: Uint8Array) {
    this.buf = buf;
    this.size = buf.length;
  }
  async read(offset: number, length: number) {
    const end = Math.min(this.size, offset + length);
    if (offset >= end) return new Uint8Array(0);
    return this.buf.subarray(offset, end);
  }
}

class HttpSource implements Source {
  readonly size: number;
  private url: string;
  constructor(url: string, size: number) {
    this.url = url;
    this.size = size;
  }
  static async open(url: string): Promise<HttpSource> {
    // 先 HEAD；桥对"远端补丁站代理"的文件可能给不出 Content-Length，
    // 这时用一次 0-0 的 Range 请求从 Content-Range 里读总长度
    let size = 0;
    try {
      const head = await fetch(url, { method: 'HEAD' });
      if (head.ok) size = Number(head.headers.get('content-length') ?? 0);
    } catch {
      /* 落到下面的探测 */
    }
    if (!size) {
      const probe = await fetch(url, { headers: { Range: 'bytes=0-0' } });
      if (!probe.ok && probe.status !== 206) throw new Error(`${url}: HTTP ${probe.status}`);
      const total = Number((probe.headers.get('content-range') ?? '').split('/')[1] ?? 0);
      size = total || Number(probe.headers.get('content-length') ?? 0);
    }
    if (!size) throw new Error(`${url}: 拿不到文件大小（需要 Range 支持）`);
    return new HttpSource(url, size);
  }
  async read(offset: number, length: number): Promise<Uint8Array> {
    const end = Math.min(this.size, offset + length) - 1;
    if (offset > end) return new Uint8Array(0);
    const res = await fetch(this.url, { headers: { Range: `bytes=${offset}-${end}` } });
    if (!res.ok && res.status !== 206) throw new Error(`${this.url}: Range 请求失败 ${res.status}`);
    return new Uint8Array(await res.arrayBuffer());
  }
}

export class Lib {
  readonly name: string;
  version = 0;
  count = 0;
  readonly index = new Int32Array(0);
  readonly frames = new Map<number, LibFrame>();

  private src: Source;
  private blocks = new Map<number, Uint8Array>();
  private blockOrder: number[] = [];
  private headerTask: Promise<void> | null = null;
  private metaCache = new Map<number, MImageMeta | null>();
  private metaPending = new Map<number, Promise<MImageMeta | null>>();
  private textures = new Map<number, ImageBitmap | null>();
  /** 位图缓存上限，超了按插入顺序丢最老的（丢掉的会重新解压） */
  private static readonly MAX_BITMAPS = 4000;
  private texturePending = new Map<number, Promise<ImageBitmap | null>>();
  private framesReady = false;

  private constructor(name: string, src: Source) {
    this.name = name;
    this.src = src;
  }

  static async open(url: string): Promise<Lib> {
    const lib = new Lib(url, await HttpSource.open(url));
    await lib.ensureHeader();
    return lib;
  }

  static fromBuffer(name: string, buf: Uint8Array): Lib {
    return new Lib(name, new BufferSource(buf));
  }

  // ---------------- 块缓存 ----------------

  private async block(n: number) {
    const hit = this.blocks.get(n);
    if (hit) return hit;
    const data = await this.src.read(n * BLOCK_SIZE, BLOCK_SIZE);
    this.blocks.set(n, data);
    this.blockOrder.push(n);
    while (this.blockOrder.length > MAX_BLOCKS) {
      const old = this.blockOrder.shift()!;
      this.blocks.delete(old);
    }
    return data;
  }

  private async range(offset: number, length: number): Promise<Uint8Array> {
    const startBlock = Math.floor(offset / BLOCK_SIZE);
    const endBlock = Math.floor((offset + length - 1) / BLOCK_SIZE);
    if (startBlock === endBlock) {
      const b = await this.block(startBlock);
      const s = offset - startBlock * BLOCK_SIZE;
      return b.subarray(s, Math.min(b.length, s + length));
    }
    const out = new Uint8Array(length);
    let written = 0;
    for (let n = startBlock; n <= endBlock; n++) {
      const b = await this.block(n);
      const s = Math.max(0, offset - n * BLOCK_SIZE);
      const take = Math.min(b.length - s, length - written);
      out.set(b.subarray(s, s + take), written);
      written += take;
      if (written >= length) break;
    }
    return written === length ? out : out.subarray(0, written);
  }

  // ---------------- 头部 / 索引 / FrameSet ----------------

  ensureHeader(): Promise<void> {
    if (!this.headerTask) this.headerTask = this.loadHeader();
    return this.headerTask;
  }

  private async loadHeader() {
    const head = await this.range(0, 12);
    const dv = new DataView(head.buffer, head.byteOffset, head.byteLength);
    this.version = dv.getInt32(0, true);
    if (this.version < 2) throw new Error(`${this.name}: 不支持的 Lib 版本 ${this.version}`);
    this.count = dv.getInt32(4, true);
    if (this.count <= 0 || this.count > 200000) throw new Error(`${this.name}: 图片数量异常 ${this.count}`);

    const frameSeek = this.version >= 3 ? dv.getInt32(8, true) : 0;
    const indexPath = this.version >= 3 ? 12 : 8;
    const raw = await this.range(indexPath, this.count * 4);
    const iv = new DataView(raw.buffer, raw.byteOffset, raw.byteLength);
    // index 是 readonly 字段，这里用普通数组替换（TS 里 readonly 只是编译期约束）
    (this as { index: Int32Array }).index = new Int32Array(this.count);
    for (let i = 0; i < this.count; i++) this.index[i] = iv.getInt32(i * 4, true);

    if (this.version >= 3 && frameSeek > 0) {
      try {
        const cntRaw = await this.range(frameSeek, 4);
        const frameCount = new DataView(cntRaw.buffer, cntRaw.byteOffset, 4).getInt32(0, true);
        if (frameCount > 0 && frameCount < 4096) {
          const body = await this.range(frameSeek + 4, frameCount * 35);
          const fv = new DataView(body.buffer, body.byteOffset, body.byteLength);
          let p = 0;
          for (let i = 0; i < frameCount && p + 35 <= body.length; i++) {
            const action = fv.getUint8(p);
            this.frames.set(action, {
              start: fv.getInt32(p + 1, true),
              count: fv.getInt32(p + 5, true),
              skip: fv.getInt32(p + 9, true),
              interval: fv.getInt32(p + 13, true),
              effectStart: fv.getInt32(p + 17, true),
              effectCount: fv.getInt32(p + 21, true),
              effectSkip: fv.getInt32(p + 25, true),
              effectInterval: fv.getInt32(p + 29, true),
              reverse: fv.getUint8(p + 33) !== 0,
              blend: fv.getUint8(p + 34) !== 0,
            });
            p += 35;
          }
        }
      } catch (e) {
        console.warn('[lib] FrameSet 读取失败', this.name, e);
      }
    }
    this.framesReady = true;
  }

  /** 预热：把一批图片索引涉及的字节区间合并成少量 HTTP 请求塞进块缓存 */
  async warm(indices: Iterable<number>) {
    await this.ensureHeader();
    const offsets: number[] = [];
    for (const i of indices) {
      if (this.metaCache.has(i)) continue;
      const off = this.index[i];
      if (off > 0) offsets.push(off);
    }
    if (!offsets.length) return;
    offsets.sort((a, b) => a - b);

    // 合并相邻区间：间隙 < 32KB 就一起取，单次最多 1.5MB
    const gaps: Array<[number, number]> = [];
    let start = offsets[0];
    let end = start + 17;
    const MERGE_GAP = 32 * 1024;
    const MAX_SPAN = 1536 * 1024;
    for (let k = 1; k < offsets.length; k++) {
      const off = offsets[k];
      if (off - end <= MERGE_GAP && off + 64 - start <= MAX_SPAN) {
        end = off + 64;
      } else {
        gaps.push([start, end]);
        start = off;
        end = off + 64;
      }
    }
    gaps.push([start, end]);

    await Promise.all(
      gaps.map(async ([s, e]) => {
        const first = Math.floor(s / BLOCK_SIZE);
        const last = Math.floor(e / BLOCK_SIZE);
        for (let n = first; n <= last; n++) {
          if (!this.blocks.has(n)) await this.block(n);
        }
      }),
    );
  }

  // ---------------- 单张图 ----------------

  private async loadMeta(i: number): Promise<MImageMeta | null> {
    await this.ensureHeader();
    if (i < 0 || i >= this.count) return null;
    const off = this.index[i];
    if (off <= 0) return null;
    let raw: Uint8Array;
    try {
      raw = await this.range(off, 17);
    } catch {
      return null;
    }
    if (raw.length < 17) return null;
    const dv = new DataView(raw.buffer, raw.byteOffset, raw.byteLength);
    const width = dv.getInt16(0, true);
    const height = dv.getInt16(2, true);
    const length = dv.getInt32(13, true);
    if (width <= 0 || height <= 0 || length <= 0) return null;
    const shadow = dv.getUint8(12);
    const meta: MImageMeta = {
      width,
      height,
      x: dv.getInt16(4, true),
      y: dv.getInt16(6, true),
      shadowX: dv.getInt16(8, true),
      shadowY: dv.getInt16(10, true),
      shadow,
      length,
      dataOffset: off + 17,
      hasMask: (shadow >> 7) === 1,
    };
    this.metaCache.set(i, meta);
    return meta;
  }

  /** 同步窥探（渲染循环用）：undefined = 还没加载 */
  peekMeta(i: number): MImageMeta | null | undefined {
    if (this.metaCache.has(i)) return this.metaCache.get(i);
    if (!this.headerTask) return undefined;
    void this.meta(i);
    return undefined;
  }

  meta(i: number): Promise<MImageMeta | null> {
    if (this.metaCache.has(i)) return Promise.resolve(this.metaCache.get(i) ?? null);
    let task = this.metaPending.get(i);
    if (!task) {
      task = this.loadMeta(i).finally(() => this.metaPending.delete(i));
      this.metaPending.set(i, task);
    }
    return task;
  }

  /** 取出一张图的原始 RGBA 像素（不依赖浏览器 API，Node 里也能测） */
  async pixels(i: number): Promise<{ width: number; height: number; rgba: Uint8ClampedArray } | null> {
    const m = await this.meta(i);
    if (!m) return null;
    const raw = await this.range(m.dataOffset, m.length);
    const inflated = await gunzip(raw);
    const need = m.width * m.height * 4;
    if (inflated.length < need) return null;
    return { width: m.width, height: m.height, rgba: bgraToRgba(inflated.subarray(0, need)) };
  }

  peekTexture(i: number) {
    return this.textures.get(i);
  }

  /** 解码成可渲染位图（带缓存与去重） */
  image(i: number): Promise<ImageBitmap | null> {
    if (this.textures.has(i)) return Promise.resolve(this.textures.get(i) ?? null);
    let task = this.texturePending.get(i);
    if (!task) {
      task = (async () => {
        try {
          const px = await this.pixels(i);
          if (!px) return null;
          noteDecode();
          const bmp = await createImageBitmap(new ImageData(px.rgba as unknown as ImageDataArray, px.width, px.height));
          this.textures.set(i, bmp);
          if (this.textures.size > Lib.MAX_BITMAPS) {
            const oldest = this.textures.keys().next().value;
            if (oldest !== undefined && oldest !== i) this.textures.delete(oldest);
          }
          return bmp;
        } catch {
          this.textures.set(i, null);
          return null;
        } finally {
          this.texturePending.delete(i);
        }
      })();
      this.texturePending.set(i, task);
    }
    return task;
  }

  frame(action: number): LibFrame | undefined {
    return this.frames.get(action);
  }

  get framesLoaded() {
    return this.framesReady;
  }

  /** 从位图缓存里丢掉一张（不 close，交给 GC；下次会重新解压） */
  forget(index: number) {
    this.textures.delete(index);
  }
}

let decodedCounter = 0;
let lastDecodeLog = 0;

function noteDecode() {
  decodedCounter++;
  const now = Date.now();
  if (now - lastDecodeLog > 5000) {
    lastDecodeLog = now;
    if (decodedCounter > 200) {
      console.debug(`[lib] 已解码 ${decodedCounter} 张图`);
      decodedCounter = 0;
    }
  }
}

/** Lib 缓存：同一个路径只打开一次；peek() 是给渲染循环用的同步入口 */
export class LibStore {
  private pending = new Map<string, Promise<Lib | null>>();
  private ready = new Map<string, Lib>();
  /** 加载失败（比如资源目录里本来就没有这个 Lib）就不反复重试了 */
  private failed = new Set<string>();
  private baseUrl: string;

  constructor(baseUrl = '/assets') {
    this.baseUrl = baseUrl;
  }

  url(relPath: string) {
    const clean = relPath.replace(/\\/g, '/').replace(/^\.?\//, '');
    return `${this.baseUrl}/${clean}`;
  }

  private key(relPath: string) {
    return relPath.replace(/\\/g, '/').toLowerCase();
  }

  get(relPath: string): Promise<Lib | null> {
    const key = this.key(relPath);
    const ready = this.ready.get(key);
    if (ready) return Promise.resolve(ready);
    if (this.failed.has(key)) return Promise.resolve(null);

    let task = this.pending.get(key);
    if (!task) {
      task = Lib.open(this.url(relPath))
        .then((lib) => {
          this.ready.set(key, lib);
          return lib;
        })
        .catch((e) => {
          this.failed.add(key);
          console.warn('[lib] 打开失败', relPath, String(e));
          return null;
        })
        .finally(() => this.pending.delete(key));
      this.pending.set(key, task);
    }
    return task;
  }

  /** 同步取：没打开过就顺手发起加载，本次返回 null */
  peek(relPath: string): Lib | null {
    const key = this.key(relPath);
    const ready = this.ready.get(key);
    if (ready) return ready;
    void this.get(relPath);
    return null;
  }

  prime(relPath: string) {
    return this.get(relPath);
  }
}
