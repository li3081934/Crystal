/**
 * 把 078.Lib 里多个动作拼成一张"大 sheet"（8 行 = 8 方向，列 = 各动作的帧按顺序排开），
 * 用来一次性喂给图片模型生成整套动画，保证 6 个动作共用同一套装备设计。
 *
 * 用法:
 *   node Web/tools/combine-lib-sheet.mjs <Lib> <out.png> <out.json> [--cellw 112] [--cellh 124] [--scale 1]
 *
 * 输出 JSON 里记录每个动作占用的列区间，供 split-sheet.mjs 切回。
 */
import fs from 'node:fs';
import path from 'node:path';
import { readLib, readImage, bgraToRgba } from './lib.mjs';
import { encodePng } from './png.mjs';

// 078.Lib 的动作表（与 export-sheet.mjs 的 ACTIONS 保持一致）
const ACTIONS = [
  { action: 0, key: 'stand', name: '站立', start: 0, count: 4, skip: 0 },
  { action: 1, key: 'walk', name: '走路', start: 32, count: 6, skip: 0 },
  { action: 9, key: 'attack', name: '攻击', start: 80, count: 6, skip: 0 },
  { action: 18, key: 'struck', name: '受击', start: 128, count: 2, skip: 0 },
  { action: 21, key: 'die', name: '死亡', start: 144, count: 10, skip: 0 },
  { action: 22, key: 'corpse', name: '尸体', start: 153, count: 1, skip: 9 },
];

const args = process.argv.slice(2);
const get = (flag, def) => {
  const i = args.indexOf(flag);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : def;
};
const positional = args.filter((a, i) => !a.startsWith('--') && (i === 0 || !args[i - 1].startsWith('--')));
const [libPath, outPng, outJson] = positional;
if (!libPath || !outPng || !outJson) {
  console.error('用法: node Web/tools/combine-lib-sheet.mjs <Lib> <out.png> <out.json> [--cellw 112] [--cellh 124] [--scale 1]');
  process.exit(1);
}

const scale = Number(get('--scale', '1'));
const cellW = Math.round(Number(get('--cellw', '112')) * scale);
const cellH = Math.round(Number(get('--cellh', '124')) * scale);

const totalCols = ACTIONS.reduce((n, a) => n + a.count, 0);
const W = totalCols * cellW;
const H = 8 * cellH;

const lib = readLib(libPath);
const sheet = Buffer.alloc(W * H * 4);
// 洋红底
for (let i = 0; i < W * H; i++) {
  sheet[i * 4] = 255;
  sheet[i * 4 + 1] = 0;
  sheet[i * 4 + 2] = 255;
  sheet[i * 4 + 3] = 255;
}

const layout = { source: libPath, width: W, height: H, cellW, cellH, scale, rows: 8, columns: [], actions: {} };
let col = 0;
for (const a of ACTIONS) {
  layout.actions[a.key] = { action: a.action, name: a.name, start: a.start, count: a.count, skip: a.skip, colStart: col, colCount: a.count };
  for (let frame = 0; frame < a.count; frame++) {
    layout.columns.push({ key: a.key, action: a.action, frame });
    for (let dir = 0; dir < 8; dir++) {
      const idx = a.start + dir * (a.count + a.skip) + frame;
      const img = readImage(lib, idx);
      if (!img) continue;
      const rgba = bgraToRgba(img.bgra);
      const srcW = img.w;
      const srcH = img.h;
      // 先算缩放：精灵需放进 cellW*0.86 x cellH*0.86 的框内
      const fit = Math.min((cellW * 0.86) / srcW, (cellH * 0.86) / srcH, 1);
      const dw = Math.max(1, Math.round(srcW * fit));
      const dh = Math.max(1, Math.round(srcH * fit));
      const cx = col * cellW + Math.floor((cellW - dw) / 2);
      const cy = dir * cellH + (cellH - dh - Math.floor(cellH * 0.04));
      for (let y = 0; y < dh; y++) {
        const sy = Math.min(srcH - 1, Math.floor(y / fit));
        for (let x = 0; x < dw; x++) {
          const sx = Math.min(srcW - 1, Math.floor(x / fit));
          const si = (sy * srcW + sx) * 4;
          if (rgba[si + 3] < 8) continue;
          const px = cx + x;
          const py = cy + y;
          if (px < 0 || py < 0 || px >= W || py >= H) continue;
          const di = (py * W + px) * 4;
          sheet[di] = rgba[si];
          sheet[di + 1] = rgba[si + 1];
          sheet[di + 2] = rgba[si + 2];
          sheet[di + 3] = 255;
        }
      }
    }
    col++;
  }
}

fs.mkdirSync(path.dirname(path.resolve(outPng)), { recursive: true });
fs.writeFileSync(outPng, encodePng(sheet, W, H));
fs.writeFileSync(outJson, JSON.stringify(layout, null, 2), 'utf8');
console.log(`[combine] ${outPng} ${W}x${H} (${totalCols} 列 × 8 行, cell ${cellW}x${cellH})`);
for (const [k, a] of Object.entries(layout.actions)) {
  console.log(`  ${k.padEnd(7)} ${a.name} 列 ${a.colStart + 1}~${a.colStart + a.colCount}`);
}
