/**
 * 把 .Lib 里的若干张图导出成 PNG（调美术参考 / 比对贴图用）
 *
 * 用法：
 *   node Web/tools/dump-lib.mjs <Lib路径> <起始索引> <张数> <输出目录> [放大倍数]
 *   node Web/tools/dump-lib.mjs Data/Monster/081.Lib 0 8 out/mon081 2
 *
 * 格式见 Client/MirGraphics/MLibrary.cs：
 *   int32 version(2/3) | int32 count | [v3] int32 frameSeek | int32 index[count]
 *   记录：int16 w,h,x,y,shadowX,shadowY | uint8 shadow | int32 length | gzip(BGRA)
 */
import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';

/* ---------------- 极简 PNG 编码器（RGBA，8bit） ---------------- */
const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();

function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

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
    raw[y * (width * 4 + 1)] = 0; // filter: none
    rgba.copy(raw, y * (width * 4 + 1) + 1, y * width * 4, (y + 1) * width * 4);
  }
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(width, 0);
  ihdr.writeUInt32BE(height, 4);
  ihdr[8] = 8; // bit depth
  ihdr[9] = 6; // colour type RGBA
  return Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
    chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
    chunk('IEND', Buffer.alloc(0)),
  ]);
}

/* ---------------- Lib 解析 ---------------- */
function readLib(file) {
  const buf = fs.readFileSync(file);
  const version = buf.readInt32LE(0);
  const count = buf.readInt32LE(4);
  const headerSize = version >= 3 ? 12 : 8;
  const index = new Int32Array(count);
  for (let i = 0; i < count; i++) index[i] = buf.readInt32LE(headerSize + i * 4);
  return { buf, version, count, index };
}

function readImage(lib, i) {
  const off = lib.index[i];
  if (!off) return null;
  const w = lib.buf.readInt16LE(off);
  const h = lib.buf.readInt16LE(off + 2);
  const x = lib.buf.readInt16LE(off + 4);
  const y = lib.buf.readInt16LE(off + 6);
  const sx = lib.buf.readInt16LE(off + 8);
  const sy = lib.buf.readInt16LE(off + 10);
  const shadow = lib.buf.readUInt8(off + 12);
  const len = lib.buf.readInt32LE(off + 13);
  const data = zlib.gunzipSync(lib.buf.subarray(off + 17, off + 17 + len));
  return { w, h, x, y, sx, sy, shadow, data };
}

function bgraToRgba(src) {
  const dst = Buffer.alloc(src.length);
  for (let p = 0; p + 3 < src.length; p += 4) {
    dst[p] = src[p + 2];
    dst[p + 1] = src[p + 1];
    dst[p + 2] = src[p];
    dst[p + 3] = src[p + 3];
  }
  return dst;
}

function scale(rgba, w, h, k) {
  if (k === 1) return { rgba, w, h };
  const W = w * k;
  const H = h * k;
  const out = Buffer.alloc(W * H * 4);
  for (let y = 0; y < H; y++) {
    for (let x = 0; x < W; x++) {
      const s = (Math.floor(y / k) * w + Math.floor(x / k)) * 4;
      const d = (y * W + x) * 4;
      out[d] = rgba[s];
      out[d + 1] = rgba[s + 1];
      out[d + 2] = rgba[s + 2];
      out[d + 3] = rgba[s + 3];
    }
  }
  return { rgba: out, w: W, h: H };
}

const [, , file, startArg, countArg, outDir, scaleArg] = process.argv;
if (!file || !outDir) {
  console.error('用法: node Web/tools/dump-lib.mjs <Lib路径> <起始索引> <张数> <输出目录> [放大倍数]');
  process.exit(1);
}
const start = Number(startArg ?? 0);
const count = Number(countArg ?? 1);
const k = Number(scaleArg ?? 1);

const lib = readLib(file);
console.log(`${path.basename(file)}: version=${lib.version} count=${lib.count}`);
fs.mkdirSync(outDir, { recursive: true });

for (let i = start; i < start + count && i < lib.count; i++) {
  const img = readImage(lib, i);
  if (!img) {
    console.log(`  [${i}] <空>`);
    continue;
  }
  const rgba = bgraToRgba(img.data.subarray(0, img.w * img.h * 4));
  const s = scale(rgba, img.w, img.h, k);
  const out = path.join(outDir, `${String(i).padStart(4, '0')}.png`);
  fs.writeFileSync(out, encodePng(s.rgba, s.w, s.h));
  console.log(`  [${i}] ${img.w}x${img.h} (offset ${img.x},${img.y}) -> ${out}`);
}
