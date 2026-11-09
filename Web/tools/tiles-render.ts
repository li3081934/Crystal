/**
 * 离线合成：直接读本地 .map + .Lib，把某片区域按两种规则各画一遍，输出 PNG。
 *
 *   node --experimental-strip-types tools/tiles-render.ts <map> <x0,x1,y0,y1> <out.png> [csharp|web] [scale]
 *
 * 规则差异只有一处：高的图（非 48x32 / 96x64）的底边落在哪里
 *   csharp = cellTop + 32 - h   （DrawObjects 里 drawY 是 +1 行，DrawUp 再减高度）
 *   web    = cellTop - h        （renderer.ts placeTile 现在的写法）
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { deflateSync } from 'node:zlib';
import { parseMap, mapLibPath, CELL_WIDTH, CELL_HEIGHT } from '../client/src/formats/map.ts';
import { Lib } from '../client/src/formats/lib.ts';

const ASSETS = process.env.MIR2_ASSETS ?? '../Build/Client/Release';
const mapFile = process.argv[2] ?? `${ASSETS}/Map/0.map`;
const [x0, x1, y0, y1] = (process.argv[3] ?? '292,318,610,640').split(',').map(Number);
const outFile = process.argv[4] ?? 'shots/render.png';
const mode = (process.argv[5] ?? 'csharp') as 'csharp' | 'web';
const scale = Number(process.argv[6] ?? 1);

const map = parseMap(new Uint8Array(readFileSync(mapFile)));
const W = (x1 - x0 + 1) * CELL_WIDTH;
const H = (y1 - y0 + 1) * CELL_HEIGHT;
const buf = new Uint8ClampedArray(W * H * 4);
// 背景先填空（便于看空洞）
for (let i = 0; i < W * H; i++) {
  buf[i * 4] = 20;
  buf[i * 4 + 1] = 0;
  buf[i * 4 + 2] = 40;
  buf[i * 4 + 3] = 255;
}

const libs = new Map<string, Lib>();
const libCache = async (path: string) => {
  let l = libs.get(path);
  if (!l) {
    l = Lib.fromBuffer(path, new Uint8Array(readFileSync(`${ASSETS}/${path}`)));
    await l.ensureHeader();
    libs.set(path, l);
  }
  return l;
};
const px = new Map<string, { width: number; height: number; rgba: Uint8ClampedArray } | null>();
const getPix = async (path: string, idx: number) => {
  const k = `${path}#${idx}`;
  if (px.has(k)) return px.get(k)!;
  const p = await (await libCache(path)).pixels(idx);
  px.set(k, p);
  return p;
};

const isFlat = (w: number, h: number) =>
  (w === CELL_WIDTH && h === CELL_HEIGHT) || (w === CELL_WIDTH * 2 && h === CELL_HEIGHT * 2);

function blit(img: { width: number; height: number; rgba: Uint8ClampedArray }, dx: number, dy: number, add: boolean) {
  for (let sy = 0; sy < img.height; sy++) {
    const py = dy + sy;
    if (py < 0 || py >= H) continue;
    for (let sx = 0; sx < img.width; sx++) {
      const pxx = dx + sx;
      if (pxx < 0 || pxx >= W) continue;
      const s = (sy * img.width + sx) * 4;
      const a = img.rgba[s + 3] / 255;
      if (a <= 0) continue;
      const d = (py * W + pxx) * 4;
      if (add) {
        buf[d] = Math.min(255, buf[d] + img.rgba[s] * a);
        buf[d + 1] = Math.min(255, buf[d + 1] + img.rgba[s + 1] * a);
        buf[d + 2] = Math.min(255, buf[d + 2] + img.rgba[s + 2] * a);
      } else {
        buf[d] = img.rgba[s] * a + buf[d] * (1 - a);
        buf[d + 1] = img.rgba[s + 1] * a + buf[d + 1] * (1 - a);
        buf[d + 2] = img.rgba[s + 2] * a + buf[d + 2] * (1 - a);
      }
      buf[d + 3] = 255;
    }
  }
}

const cellX = (x: number) => (x - x0) * CELL_WIDTH;
const cellY = (y: number) => (y - y0) * CELL_HEIGHT;

type Draw = { path: string; idx: number; dx: number; dy: number; add: boolean; label: string };
const flatDraws: Draw[] = [];
const tallDraws: Draw[] = [];

for (let y = y0; y <= y1; y++) {
  for (let x = x0; x <= x1; x++) {
    const c = map.at(x, y);
    if (!c) continue;
    const sx = cellX(x);
    const sy = cellY(y);
    const push = async (kind: 'back' | 'mid' | 'front', li: number, ii: number, add: boolean) => {
      if (ii < 0 || li < 0) return;
      const path = mapLibPath(li);
      if (!path) return;
      const img = await getPix(path, ii);
      if (!img) return;
      const label = `${kind} ${path.split('/').pop()}#${ii} ${img.width}x${img.height} @${x},${y}`;
      if (isFlat(img.width, img.height)) flatDraws.push({ path, idx: ii, dx: sx, dy: sy, add, label });
      else {
        const dy = mode === 'csharp' ? sy + CELL_HEIGHT - img.height : sy - img.height;
        tallDraws.push({ path, idx: ii, dx: sx, dy, add, label });
      }
    };
    // back 只在偶数格
    if (y % 2 === 0 && x % 2 === 0 && c.backImage !== 0 && c.backIndex !== -1) {
      await push('back', c.backIndex, (c.backImage & 0x1fffffff) - 1, false);
    }
    if (c.midIndex !== -1 && c.midImage > 0) {
      const midBlend = (c.midAnimFrame & 0x0f) === 8 || (c.midAnimFrame & 0x0f) === 10;
      await push('mid', c.midIndex, c.midImage - 1, midBlend);
    }
    if (c.frontIndex !== -1 && c.frontIndex !== 200 && c.frontImage !== -32768) {
      await push('front', c.frontIndex, (c.frontImage & 0x7fff) - 1, (c.frontAnimFrame & 0x80) !== 0);
    }
  }
}

for (const d of flatDraws) blit((await getPix(d.path, d.idx))!, d.dx, d.dy, d.add);
for (const d of tallDraws) blit((await getPix(d.path, d.idx))!, d.dx, d.dy, d.add);

// ---- PNG ----
const SW = W * scale;
const SH = H * scale;
const raw = Buffer.alloc((SW * 3 + 1) * SH);
for (let y = 0; y < SH; y++) {
  const row = y * (SW * 3 + 1);
  raw[row] = 0;
  for (let x = 0; x < SW; x++) {
    const s = (Math.floor(y / scale) * W + Math.floor(x / scale)) * 4;
    raw[row + 1 + x * 3] = buf[s];
    raw[row + 2 + x * 3] = buf[s + 1];
    raw[row + 3 + x * 3] = buf[s + 2];
  }
}
const table = (() => {
  const t = new Int32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c;
  }
  return t;
})();
const crc32 = (b: Buffer) => {
  let c = -1;
  for (let i = 0; i < b.length; i++) c = table[(c ^ b[i]) & 0xff] ^ (c >>> 8);
  return (c ^ -1) >>> 0;
};
const chunk = (type: string, data: Buffer) => {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
};
const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(SW, 0);
ihdr.writeUInt32BE(SH, 4);
ihdr[8] = 8;
ihdr[9] = 2;
const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  chunk('IHDR', ihdr),
  chunk('IDAT', deflateSync(raw, { level: 6 })),
  chunk('IEND', Buffer.alloc(0)),
]);
writeFileSync(outFile, png);
console.log(`${outFile}  ${SW}x${SH}  mode=${mode}  flat=${flatDraws.length} tall=${tallDraws.length}`);
