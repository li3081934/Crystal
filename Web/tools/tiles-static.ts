/**
 * 纯离线排查：直接读本地 .map + .Lib，把某片区域的瓦片（含尺寸）列出来，
 * 并按"网页端当前规则"和"原版 DrawObjects 规则"两种算法给出绘制矩形。
 *
 * 用法: node --experimental-strip-types tools/tiles-static.ts [map路径] [x0,x1,y0,y1]
 */
import { readFileSync } from 'node:fs';
import { parseMap, mapLibPath, CELL_WIDTH, CELL_HEIGHT } from '../client/src/formats/map.ts';
import { Lib } from '../client/src/formats/lib.ts';

const ASSETS = process.env.MIR2_ASSETS ?? '../Build/Client/Release';
const mapFile = process.argv[2] ?? `${ASSETS}/Map/0.map`;
const [x0, x1, y0, y1] = (process.argv[3] ?? '296,312,614,634').split(',').map(Number);

const map = parseMap(new Uint8Array(readFileSync(mapFile)));
console.log(`${mapFile}: ${map.width}x${map.height} format=${map.format}`);

const libs = new Map<string, Lib>();
async function lib(path: string) {
  let l = libs.get(path);
  if (!l) {
    const buf = new Uint8Array(readFileSync(`${ASSETS}/${path}`));
    l = Lib.fromBuffer(path, buf);
    await l.ensureHeader();
    libs.set(path, l);
  }
  return l;
}

const isFlat = (w: number, h: number) =>
  (w === CELL_WIDTH && h === CELL_HEIGHT) || (w === CELL_WIDTH * 2 && h === CELL_HEIGHT * 2);

const stats = new Map<string, number>();
for (let y = y0; y <= y1; y++) {
  for (let x = x0; x <= x1; x++) {
    const c = map.at(x, y);
    if (!c) continue;
    const parts: string[] = [];
    const draws: string[] = [];
    const add = async (kind: 'back' | 'mid' | 'front', li: number, ii: number) => {
      const path = mapLibPath(li);
      if (!path) return;
      try {
        const meta = await (await lib(path)).meta(ii);
        if (!meta) {
          parts.push(`${kind}=${path.split('/').pop()}#${ii}(空槽)`);
          return;
        }
        const flat = isFlat(meta.width, meta.height);
        const key = `${kind}:${path.split('/').pop()}#${ii}:${meta.width}x${meta.height}`;
        stats.set(key, (stats.get(key) ?? 0) + 1);
        parts.push(`${kind}=${path.split('/').pop()}#${ii} ${meta.width}x${meta.height} off(${meta.x},${meta.y}) ${flat ? 'flat' : 'tall'}`);
        // 相对格子左上角：x + 0, y + 0(flat) / -h(tall, 网页) / +32-h(原版 DrawObjects)
        draws.push(
          `${kind} ${flat ? `web=(0,0) w=${meta.width} h=${meta.height}` : `web=(0,-${meta.height}) csharp=(0,${32 - meta.height})`}`,
        );
      } catch {
        parts.push(`${kind}=${path.split('/').pop()}#${ii}(读不到)`);
      }
    };
    if (y % 2 === 0 && x % 2 === 0 && c.backImage !== 0 && c.backIndex !== -1) {
      await add('back', c.backIndex, (c.backImage & 0x1fffffff) - 1);
    }
    if (c.midIndex !== -1 && c.midImage > 0) await add('mid', c.midIndex, c.midImage - 1);
    if (c.frontIndex !== -1 && c.frontIndex !== 200 && c.frontImage !== -32768) {
      await add('front', c.frontIndex, (c.frontImage & 0x7fff) - 1);
    }
    if (c.midIndex === -1 || c.midImage <= 0) parts.push('mid=空');
    if (c.frontIndex === -1 || c.frontIndex === 200 || c.frontImage === -32768) parts.push('front=空');
    const extra = [
      c.midAnimFrame ? `midAnim=${c.midAnimFrame & 0x0f}/${c.midAnimTick}` : '',
      c.frontAnimFrame ? `frontAnim=0x${c.frontAnimFrame.toString(16)}` : '',
      c.doorIndex ? `door=${c.doorIndex}/${c.doorOffset}` : '',
    ].filter(Boolean).join(' ');
    console.log(`${x},${y} ${c.backImage ? `backImg=0x${(c.backImage >>> 0).toString(16)}` : ''} ${parts.join(' | ')} ${extra} ${draws.length > 1 ? '=> ' + draws.join(' ; ') : ''}`);
  }
}

console.log('\n--- 区域里出现的图（按尺寸归类）---');
for (const [k, n] of [...stats.entries()].sort((a, b) => b[1] - a[1])) console.log(`${n}x ${k}`);
