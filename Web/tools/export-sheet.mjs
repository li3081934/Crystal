/**
 * 把 .Lib 里的所有动作导出成"人/模型可编辑"的网格精灵表，并写一份 layout.json 方便切回去。
 *
 * 用法:
 *   node Web/tools/export-sheet.mjs <Lib> <outDir> [--scale 3] [--pad 8] [--bg #FF00FF]
 *
 * 约定：
 *   - 每个动作一张 png：行 = 8 个方向（上/右上/右/右下/下/左下/左/左上），列 = 该动作帧数
 *   - 所有格子用**同一个整数放大倍数**（默认 3x），这样改图时每格像素比例一致
 *   - 精灵水平居中、底部对齐（留 pad 像素），背景铺纯色（默认洋红 #FF00FF）
 *   - 帧序号仍是 Lib 原序号：index = Frame.Start + dir * (Frame.Count + Frame.Skip) + frame
 *   - 需要切回 Lib 时：
 *       node Web/tools/sheet-to-lib.mjs <原Lib> <sheet.png> <out.Lib> <action> 0 --scale 1/3 ...
 */
import fs from 'node:fs';
import path from 'node:path';
import { readLib, readImage } from './lib.mjs';
import { encodePng, decodePng, hexToRgb } from './png.mjs';

const ACTION_NAMES = {
  0: 'stand',
  1: 'walk',
  2: 'run',
  3: 'dash',
  9: 'attack',
  10: 'range-attack',
  18: 'struck',
  21: 'die',
  22: 'corpse',
  24: 'harvest',
  25: 'cast',
  26: 'channel',
  28: 'revive',
};

const argv = process.argv.slice(2);
const positional = argv.filter((a) => !a.startsWith('--'));
const opt = (name, def) => {
  const i = argv.indexOf(`--${name}`);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : def;
};

const libFile = positional[0];
const outDir = positional[1];
if (!libFile || !outDir) {
  console.error('用法: node Web/tools/export-sheet.mjs <Lib> <outDir> [--scale 3] [--pad 8] [--bg #FF00FF]');
  process.exit(1);
}

const scale = Number(opt('scale', 3));
const pad = Number(opt('pad', 8));
const bg = hexToRgb(opt('bg', '#FF00FF'));
const dirs = 8;

const lib = readLib(libFile);
fs.mkdirSync(outDir, { recursive: true });

/** 该动作所有帧里最大的精灵尺寸（决定格子大小） */
function measure(frame) {
  const stride = frame.count + frame.skip;
  let w = 1;
  let h = 1;
  for (let d = 0; d < dirs; d++) {
    for (let c = 0; c < frame.count; c++) {
      const img = readImage(lib, frame.start + d * stride + c);
      if (!img) continue;
      w = Math.max(w, img.w);
      h = Math.max(h, img.h);
    }
  }
  return { w, h, stride };
}

const layout = { lib: path.basename(libFile), scale, pad, dirs, actions: [] };

for (const frame of lib.frames) {
  if (frame.reverse) continue; // 死亡倒放之类的不需要单独出图
  if (!frame.count) continue;

  const { w, h, stride } = measure(frame);
  const cellW = Math.ceil(w * scale) + pad * 2;
  const cellH = Math.ceil(h * scale) + pad * 2;
  const W = cellW * frame.count;
  const H = cellH * dirs;
  const px = Buffer.alloc(W * H * 4);

  for (let i = 0; i < W * H; i++) {
    px[i * 4] = bg.r;
    px[i * 4 + 1] = bg.g;
    px[i * 4 + 2] = bg.b;
    px[i * 4 + 3] = 255;
  }

  const cells = [];

  for (let d = 0; d < dirs; d++) {
    for (let c = 0; c < frame.count; c++) {
      const index = frame.start + d * stride + c;
      const img = readImage(lib, index);
      if (!img) continue;

      const dw = img.w * scale;
      const dh = img.h * scale;
      const ox = c * cellW + Math.floor((cellW - dw) / 2);
      const oy = d * cellH + (cellH - pad - dh);

      for (let y = 0; y < dh; y++) {
        for (let x = 0; x < dw; x++) {
          const si = (Math.floor(y / scale) * img.w + Math.floor(x / scale)) * 4;
          const a = img.bgra[si + 3];
          if (a < 8) continue;

          const di = ((oy + y) * W + (ox + x)) * 4;
          const al = a / 255;
          px[di] = Math.round(img.bgra[si + 2] * al + px[di] * (1 - al));
          px[di + 1] = Math.round(img.bgra[si + 1] * al + px[di + 1] * (1 - al));
          px[di + 2] = Math.round(img.bgra[si] * al + px[di + 2] * (1 - al));
        }
      }

      cells.push({ dir: d, frame: c, index, srcW: img.w, srcH: img.h, x: img.x, y: img.y, cellX: c, cellY: d });
    }
  }

  const name = ACTION_NAMES[frame.action] || `action${frame.action}`;
  const file = path.join(outDir, `action${String(frame.action).padStart(2, '0')}-${name}-d0-7.png`);
  fs.writeFileSync(file, encodePng(px, W, H));

  layout.actions.push({
    action: frame.action,
    name,
    file: path.basename(file),
    start: frame.start,
    count: frame.count,
    skip: frame.skip,
    stride,
    cellW,
    cellH,
    blend: frame.blend,
    columns: frame.count,
    rows: dirs,
    cells,
  });

  console.log(`action ${frame.action} (${name}): ${frame.count} 帧/方向 × ${dirs} 方向 → ${file}  ${W}x${H}`);
}

// 总览图：把各动作（跳过 blend 特效帧表）按 1x 缩略叠起来，方便一眼看全
const ovActions = layout.actions.filter((a) => !a.blend);
const ovCellW = Math.max(...ovActions.map((a) => Math.ceil(a.cellW / scale)));
const ovCellH = Math.max(...ovActions.map((a) => Math.ceil(a.cellH / scale)));
const maxCols = Math.max(...ovActions.map((a) => a.columns));
const ovW = ovCellW * maxCols;
const ovH = ovCellH * dirs * ovActions.length;
const ov = Buffer.alloc(ovW * ovH * 4);
for (let i = 0; i < ovW * ovH; i++) {
  ov[i * 4] = bg.r;
  ov[i * 4 + 1] = bg.g;
  ov[i * 4 + 2] = bg.b;
  ov[i * 4 + 3] = 255;
}

ovActions.forEach((a, ai) => {
  const { w, h, rgba } = readPngRgba(path.join(outDir, a.file));
  const sw = Math.ceil(a.cellW / scale);
  const sh = Math.ceil(a.cellH / scale);
  for (let y = 0; y < dirs * sh; y++) {
    for (let x = 0; x < a.columns * sw; x++) {
      const sx = Math.min(w - 1, x * scale);
      const sy = Math.min(h - 1, y * scale);
      const si = (sy * w + sx) * 4;
      const di = ((ai * dirs * ovCellH + y) * ovW + x) * 4;
      ov[di] = rgba[si];
      ov[di + 1] = rgba[si + 1];
      ov[di + 2] = rgba[si + 2];
      ov[di + 3] = 255;
    }
  }
});

fs.writeFileSync(path.join(outDir, 'overview.png'), encodePng(ov, ovW, ovH));
fs.writeFileSync(path.join(outDir, 'layout.json'), JSON.stringify(layout, null, 2));

console.log(`总览 ${path.join(outDir, 'overview.png')}  ${ovW}x${ovH}`);
console.log(`元数据 ${path.join(outDir, 'layout.json')}`);

function readPngRgba(file) {
  const { w, h, rgba } = decodePng(fs.readFileSync(file));
  return { w, h, rgba };
}
