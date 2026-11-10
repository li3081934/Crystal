/**
 * .Lib（Mir2 图库）读写（v2 / v3）
 *
 * 结构（见 Client/MirGraphics/MLibrary.cs + Client/MirObjects/Frames.cs）：
 *   int32 version | int32 count | [v3] int32 frameSeek | int32 index[count]
 *   记录: int16 w,h,x,y,shadowX,shadowY | uint8 shadow | int32 length | gzip(BGRA)
 *   帧表(@frameSeek): int32 frameCount | { uint8 action, 8×int32, bool reverse, bool blend }
 */
import fs from 'node:fs';
import zlib from 'node:zlib';

const HEADER_PAD = 12;

export function readLib(file) {
  const buf = fs.readFileSync(file);
  const version = buf.readInt32LE(0);
  const count = buf.readInt32LE(4);
  const headerSize = version >= 3 ? 12 : 8;
  const frameSeek = version >= 3 ? buf.readInt32LE(8) : 0;
  const index = new Int32Array(count);
  for (let i = 0; i < count; i++) index[i] = buf.readInt32LE(headerSize + i * 4);

  const frames = [];
  if (frameSeek > 0 && frameSeek + 4 <= buf.length) {
    let p = frameSeek;
    const frameCount = buf.readInt32LE(p);
    p += 4;
    for (let i = 0; i < frameCount; i++) {
      const action = buf.readUInt8(p);
      p += 1;
      const f = {
        start: buf.readInt32LE(p),
        count: buf.readInt32LE(p + 4),
        skip: buf.readInt32LE(p + 8),
        interval: buf.readInt32LE(p + 12),
        effectStart: buf.readInt32LE(p + 16),
        effectCount: buf.readInt32LE(p + 20),
        effectSkip: buf.readInt32LE(p + 24),
        effectInterval: buf.readInt32LE(p + 28),
        reverse: buf.readUInt8(p + 32) !== 0,
        blend: buf.readUInt8(p + 33) !== 0,
      };
      p += 34;
      frames.push({ action, ...f });
    }
  }
  return { version, count, index, frames, buf };
}

/** 读第 i 张图，返回 BGRA 原始像素（与 .Lib 内部一致） */
export function readImage(lib, i) {
  const off = lib.index[i];
  if (!off) return null;
  const b = lib.buf;
  const w = b.readInt16LE(off);
  const h = b.readInt16LE(off + 2);
  const x = b.readInt16LE(off + 4);
  const y = b.readInt16LE(off + 6);
  const shadowX = b.readInt16LE(off + 8);
  const shadowY = b.readInt16LE(off + 10);
  const shadow = b.readUInt8(off + 12);
  const length = b.readInt32LE(off + 13);
  const data = zlib.gunzipSync(b.subarray(off + 17, off + 17 + length));
  return { w, h, x, y, shadowX, shadowY, shadow, bgra: data.subarray(0, w * h * 4) };
}

/**
 * 写一个 v3 Lib
 * images: [{ w,h,x,y,shadowX,shadowY,shadow, bgra }]（null 表示空槽）
 */
export function writeLib(file, images, frames = []) {
  const count = images.length;
  const headerSize = HEADER_PAD;
  const indexTableSize = count * 4;
  const parts = [];
  const index = new Int32Array(count);

  let offset = headerSize + indexTableSize;
  const records = [];
  for (let i = 0; i < count; i++) {
    const img = images[i];
    if (!img) {
      records.push(null);
      continue;
    }
    index[i] = offset;
    const packed = zlib.gzipSync(Buffer.from(img.bgra), { level: 9 });
    const head = Buffer.alloc(17);
    head.writeInt16LE(img.w, 0);
    head.writeInt16LE(img.h, 2);
    head.writeInt16LE(img.x, 4);
    head.writeInt16LE(img.y, 6);
    head.writeInt16LE(img.shadowX, 8);
    head.writeInt16LE(img.shadowY, 10);
    head.writeUInt8(img.shadow, 12);
    head.writeInt32LE(packed.length, 13);
    records.push(Buffer.concat([head, packed]));
    offset += 17 + packed.length;
  }

  const frameSeek = frames.length ? offset : 0;
  if (frames.length) {
    const head = Buffer.alloc(4);
    head.writeInt32LE(frames.length);
    parts.push(head);
    for (const f of frames) {
      const b = Buffer.alloc(35);
      b.writeUInt8(f.action, 0);
      b.writeInt32LE(f.start, 1);
      b.writeInt32LE(f.count, 5);
      b.writeInt32LE(f.skip, 9);
      b.writeInt32LE(f.interval, 13);
      b.writeInt32LE(f.effectStart, 17);
      b.writeInt32LE(f.effectCount, 21);
      b.writeInt32LE(f.effectSkip, 25);
      b.writeInt32LE(f.effectInterval, 29);
      b.writeUInt8(f.reverse ? 1 : 0, 33);
      b.writeUInt8(f.blend ? 1 : 0, 34);
      parts.push(b);
    }
  }
  const frameBlock = parts.length ? Buffer.concat(parts) : Buffer.alloc(0);

  const header = Buffer.alloc(headerSize + indexTableSize);
  header.writeInt32LE(3, 0);
  header.writeInt32LE(count, 4);
  header.writeInt32LE(frameSeek, 8);
  for (let i = 0; i < count; i++) header.writeInt32LE(index[i], headerSize + i * 4);

  fs.writeFileSync(file, Buffer.concat([header, ...records.filter(Boolean), frameBlock]));
  return { file, count, frameSeek, size: fs.statSync(file).size };
}

export function bgraToRgba(bgra) {
  const out = Buffer.alloc(bgra.length);
  for (let p = 0; p + 3 < bgra.length; p += 4) {
    out[p] = bgra[p + 2];
    out[p + 1] = bgra[p + 1];
    out[p + 2] = bgra[p];
    out[p + 3] = bgra[p + 3];
  }
  return out;
}

export function rgbaToBgra(rgba) {
  return bgraToRgba(rgba);
}
