/**
 * 把一批 .Lib 的第 N 张图拼成一张总览图，便于肉眼找美术资源
 *
 * 用法: node Web/tools/montage-lib.mjs <Lib目录> <索引> <输出png> [格子=128] [最多几张]
 */
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();
const crc32 = (buf) => {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
};
function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body));
  return Buffer.concat([len, body, crc]);
}
function encodePng(rgba, width, height) {
  const raw = Buffer.alloc((width * 4 + 1) * height);
  for (let y = 0; y < height; y++) {
    raw[y * (width * 4 + 1)] = 0;
    rgba.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8;
  ihdr[9] = 6;
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

function readImage(file, i) {
  const buf = fs.readFileSync(file);
  const version = buf.readInt32LE(0);
  const count = buf.readInt32LE(4);
  if (i >= count) return null;
  const off = buf.readInt32LE((version >= 3 ? 12 : 8) + i * 4);
  if (!off) return null;
  const w = buf.readInt16LE(off);
  const h = buf.readInt16LE(off + 2);
  const len = buf.readInt32LE(off + 13);
  const data = zlib.gunzipSync(buf.subarray(off + 17, off + 17 + len));
  const out = Buffer.alloc(w * h * 4);
  for (let p = 0; p + 3 < w * h * 4; p += 4) {
    out[p] = data[p + 2];
    out[p + 1] = data[p + 1];
    out[p + 2] = data[p];
    out[p + 3] = data[p + 3];
  }
  return { w, h, rgba: out };
}

const [, , dir, indexArg, outPng, cellArg, maxArg] = process.argv;
const index = Number(indexArg ?? 0);
const cell = Number(cellArg ?? 128);
const max = Number(maxArg ?? 64);

const files = fs
  .readdirSync(dir)
  .filter((f) => f.toLowerCase().endsWith('.lib'))
  .sort((a, b) => parseInt(a) - parseInt(b))
  .slice(0, max);

const cols = Math.ceil(Math.sqrt(files.length));
const rows = Math.ceil(files.length / cols);
const W = cols * cell;
const H = rows * cell;
const canvas = Buffer.alloc(W * H * 4);
// 棋盘格背景，方便看透明区
for (let y = 0; y < H; y++) {
  for (let x = 0; x < W; x++) {
    const c = ((x >> 3) + (y >> 3)) % 2 ? 60 : 40;
    const p = (y * W + x) * 4;
    canvas[p] = c;
    canvas[p + 1] = c;
    canvas[p + 2] = c + 10;
    canvas[p + 3] = 255;
    if (x % cell === 0 || y % cell === 0) {
      canvas[p] = 120;
      canvas[p + 1] = 120;
      canvas[p + 2] = 140;
    }
  }
}

files.forEach((f, n) => {
  const cx = (n % cols) * cell;
  const cy = Math.floor(n / cols) * cell;
  let img;
  try {
    img = readImage(path.join(dir, f), index);
  } catch {
    img = null;
  }
  if (!img) return;
  for (let y = 0; y < Math.min(img.h, cell - 2); y++) {
    for (let x = 0; x < Math.min(img.w, cell - 2); x++) {
      const s = (y * img.w + x) * 4;
      if (img.rgba[s + 3] < 8) continue;
      const d = ((cy + y + 1) * W + (cx + x + 1)) * 4;
      canvas[d] = img.rgba[s];
      canvas[d + 1] = img.rgba[s + 1];
      canvas[d + 2] = img.rgba[s + 2];
      canvas[d + 3] = 255;
    }
  }
});

fs.writeFileSync(outPng, encodePng(canvas, W, H));
console.log(`${files.length} 个 Lib -> ${outPng} (${W}x${H}, 每格 ${cell}px, 顺序: ${files.map((f) => f.replace('.Lib', '')).join(',')})`);
