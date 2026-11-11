/**
 * 把若干"动作精灵表"（8 行 × N 列，格宽格高可以各自不同）重新采样到统一格子后并排拼成一张大图，
 * 用于"锚定列"方案：左边放已定稿的站立列，右边放要生成的动作列，一次生成保持同一套装备。
 *
 * 用法:
 *   node Web/tools/compose-actions.mjs <out.png> <out.json> --cell 160x176 \
 *        --action stand=docs/art/petboon/gen-078/split/muse-all-v1-action00-stand.png:4 \
 *        --action walk=docs/art/petboon/src-078/action01-walk-d0-7.png:6
 *
 * 每个 --action 的格式：key=路径:列数
 */
import fs from 'node:fs';
import path from 'node:path';
import { decodePng, encodePng } from './png.mjs';

const args = process.argv.slice(2);
const get = (flag, def) => {
  const i = args.indexOf(flag);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : def;
};
const getAll = (flag) => {
  const out = [];
  for (let i = 0; i < args.length; i++) if (args[i] === flag && i + 1 < args.length) out.push(args[i + 1]);
  return out;
};

const positional = [];
for (let i = 0; i < args.length; i++) {
  if (args[i].startsWith('--')) { i++; continue; }
  positional.push(args[i]);
}
const [outPng, outJson] = positional;
const cellSpec = get('--cell', '160x176');
const m = /^(\d+)x(\d+)$/.exec(cellSpec);
if (!outPng || !outJson || !m) {
  console.error('用法: node Web/tools/compose-actions.mjs <out.png> <out.json> --cell 160x176 --action key=path:cols ...');
  process.exit(1);
}
const cellW = Number(m[1]);
const cellH = Number(m[2]);

const specs = getAll('--action').map((s) => {
  const mm = /^([^=]+)=(.+):(\d+)$/.exec(s);
  if (!mm) throw new Error(`--action 格式错误: ${s}`);
  return { key: mm[1], file: mm[2], cols: Number(mm[3]) };
});
if (!specs.length) {
  console.error('至少需要一个 --action');
  process.exit(1);
}

const totalCols = specs.reduce((n, s) => n + s.cols, 0);
const W = totalCols * cellW;
const H = 8 * cellH;
const out = Buffer.alloc(W * H * 4);
for (let i = 0; i < W * H; i++) {
  out[i * 4] = 255; out[i * 4 + 1] = 0; out[i * 4 + 2] = 255; out[i * 4 + 3] = 255;
}

const layout = { width: W, height: H, cellW, cellH, rows: 8, columns: [], actions: {} };
let col = 0;
for (const spec of specs) {
  const { w, h, rgba } = decodePng(fs.readFileSync(spec.file));
  const srcCellW = w / spec.cols;
  const srcCellH = h / 8;
  if (!Number.isInteger(srcCellW) || !Number.isInteger(srcCellH)) {
    console.warn(`[compose] ${spec.key}: ${w}x${h} / ${spec.cols} 列 → 格 ${srcCellW}x${srcCellH}（非整数，按浮点采样）`);
  }
  layout.actions[spec.key] = { source: spec.file, colStart: col, colCount: spec.cols, srcCellW, srcCellH };
  for (let c = 0; c < spec.cols; c++) {
    layout.columns.push({ key: spec.key, frame: c });
    for (let row = 0; row < 8; row++) {
      for (let y = 0; y < cellH; y++) {
        for (let x = 0; x < cellW; x++) {
          // 最近邻：目标格内坐标 → 源图（该行该列格内）坐标
          const sx = Math.floor(c * srcCellW + (x * srcCellW) / cellW);
          const sy = Math.floor(row * srcCellH + (y * srcCellH) / cellH);
          if (sx < 0 || sy < 0 || sx >= w || sy >= h) continue;
          const si = (sy * w + sx) * 4;
          const r = rgba[si], g = rgba[si + 1], b = rgba[si + 2], a = rgba[si + 3];
          if (a < 8) continue;
          const base = ((row * cellH + y) * W + (col * cellW + x)) * 4;
          out[base] = r; out[base + 1] = g; out[base + 2] = b; out[base + 3] = 255;
        }
      }
    }
    col++;
  }
}

// 保留 combine-lib-sheet.mjs 的字段名，方便 make-all-actions-prompt / split-big-sheet 复用
layout.columns = layout.columns.map((c) => ({ key: c.key, frame: c.frame }));
for (const [k, a] of Object.entries(layout.actions)) {
  layout.actions[k] = {
    action: { stand: 0, walk: 1, attack: 9, struck: 18, die: 21, corpse: 22 }[k] ?? 0,
    name: { stand: '站立', walk: '走路', attack: '攻击', struck: '受击', die: '死亡', corpse: '尸体' }[k] ?? k,
    start: 0, count: a.colCount, skip: 0, colStart: a.colStart, colCount: a.colCount, source: a.source,
  };
}

fs.mkdirSync(path.dirname(path.resolve(outPng)), { recursive: true });
fs.writeFileSync(outPng, encodePng(out, W, H));
fs.writeFileSync(outJson, JSON.stringify(layout, null, 2), 'utf8');
console.log(`[compose] ${outPng} ${W}x${H}（${totalCols} 列 × 8 行，格 ${cellW}x${cellH}）`);
for (const [k, a] of Object.entries(layout.actions)) console.log(`  ${k.padEnd(7)} 列 ${a.colStart + 1}~${a.colStart + a.colCount}`);
