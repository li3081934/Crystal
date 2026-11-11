/**
 * 把 combine-lib-sheet.mjs 拼出来的"大 sheet"按动作切回每个动作一张精灵表，
 * 同时清掉模型擅自画回来的紫色地面阴影（原版阴影层要求整块删除）。
 *
 * 用法:
 *   node Web/tools/split-big-sheet.mjs <big-sheet.png> <layout.json> <outDir> [--prefix muse-all] [--noshadow 1]
 *
 * 输出: <outDir>/<prefix>-action00-stand.png … 每张 8 行 × 该动作帧数列，格宽格高与 layout 一致
 */
import fs from 'node:fs';
import path from 'node:path';
import { decodePng, encodePng } from './png.mjs';

const args = process.argv.slice(2);
const get = (flag, def) => {
  const i = args.indexOf(flag);
  return i >= 0 && i + 1 < args.length ? args[i + 1] : def;
};
const positional = args.filter((a, i) => !a.startsWith('--') && (i === 0 || !args[i - 1].startsWith('--')));
const [sheetFile, layoutFile, outDir] = positional;
if (!sheetFile || !layoutFile || !outDir) {
  console.error('用法: node Web/tools/split-big-sheet.mjs <big-sheet.png> <layout.json> <outDir> [--prefix name] [--noshadow 1]');
  process.exit(1);
}
const prefix = get('--prefix', 'sheet');
const noShadow = get('--noshadow', '1') !== '0';

const layout = JSON.parse(fs.readFileSync(layoutFile, 'utf8'));
const { cellW, cellH, width: SW, height: SH } = layout;
const { w: gw, h: gh, rgba } = decodePng(fs.readFileSync(sheetFile));
if (gw !== SW || gh !== SH) {
  console.warn(`[split] 注意：大图尺寸 ${gw}x${gh} 与 layout ${SW}x${SH} 不一致，按 layout 逐格裁切可能会错位（请先 resize）`);
}
fs.mkdirSync(outDir, { recursive: true });

/** 紫色阴影判定：偏品红的暗色（b>r>g 且 g 明显低）——盔甲是 b>g>r 的暗蓝，金子是 r>g>b，都不会命中 */
function isPurpleShadow(r, g, b) {
  if (!noShadow) return false;
  // 模型画回来的阴影是"暗洋红"：G 通道几乎为 0，R/B 在 60~235 之间且接近
  return g < 32 && r > 45 && b > 60 && r < 240 && b < 240 && Math.abs(r - b) < 70;
}

let removed = 0;
for (const [key, a] of Object.entries(layout.actions)) {
  const cols = a.colCount;
  const outW = cols * cellW;
  const outH = 8 * cellH;
  const out = Buffer.alloc(outW * outH * 4);
  for (let y = 0; y < outH; y++) {
    for (let x = 0; x < outW; x++) {
      const sx = (a.colStart * cellW) + x;
      const sy = y;
      const si = (sy * gw + sx) * 4;
      const di = (y * outW + x) * 4;
      const r = rgba[si], g = rgba[si + 1], b = rgba[si + 2];
      if (isPurpleShadow(r, g, b)) {
        removed++;
        out[di] = 255; out[di + 1] = 0; out[di + 2] = 255; out[di + 3] = 255;
        continue;
      }
      out[di] = r;
      out[di + 1] = g;
      out[di + 2] = b;
      out[di + 3] = rgba[si + 3];
    }
  }
  const name = `${prefix}-action${String(a.action).padStart(2, '0')}-${key}.png`;
  const outPath = path.join(outDir, name);
  fs.writeFileSync(outPath, encodePng(out, outW, outH));
  console.log(`[split] ${outPath}  ${outW}x${outH} (${cols} 列 × 8 行, cell ${cellW}x${cellH})`);
}
console.log(`[split] 去阴影像素 ${removed}`);
