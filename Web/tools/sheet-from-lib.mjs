/**
 * 把 .Lib 里某个动作的若干方向导出成"网格精灵表"，给图像模型当输入/参考。
 *
 * 用法:
 *   node Web/tools/sheet-from-lib.mjs <Lib> <action> <dirStart> <dirCount> <out.png> [cell=256] [bg=#FF00FF] [scale=auto]
 *
 * 约定：
 *   - 行 = 方向（dirStart 起连续 dirCount 个），列 = 该动作的帧（Frame.Count 张）
 *   - 每格 cell×cell 像素，精灵按最近邻放大、水平居中、底部对齐（留 8px 边距）
 *   - 背景铺纯色（默认洋红 #FF00FF），便于生成后再抠掉
 *   - 帧序号仍是 Lib 原序号：index = Frame.Start + dir * (Count + Skip) + frame
 */
import fs from 'node:fs';
import { readLib, readImage } from './lib.mjs';
import { encodePng, hexToRgb } from './png.mjs';

const [, , libFile, actionArg, dirStartArg, dirCountArg, outFile, cellArg, bgArg] = process.argv;
if (!libFile || !outFile) {
  console.error('用法: node Web/tools/sheet-from-lib.mjs <Lib> <action> <dirStart> <dirCount> <out.png> [cell=256] [bg=#FF00FF]');
  process.exit(1);
}

const action = Number(actionArg ?? 0);
const dirStart = Number(dirStartArg ?? 0);
const dirCount = Number(dirCountArg ?? 4);
const cell = Number(cellArg ?? 256);
const bg = hexToRgb(bgArg ?? '#FF00FF');

const lib = readLib(libFile);
const frame = lib.frames.find((f) => f.action === action);
if (!frame) {
  console.error(`动作 ${action} 不在帧表里`);
  process.exit(1);
}

const stride = frame.count + frame.skip;
const cols = frame.count;
const rows = dirCount;
const W = cols * cell;
const H = rows * cell;
const px = Buffer.alloc(W * H * 4);

for (let i = 0; i < W * H; i++) {
  px[i * 4] = bg.r;
  px[i * 4 + 1] = bg.g;
  px[i * 4 + 2] = bg.b;
  px[i * 4 + 3] = 255;
}

const pad = 8;
let drawn = 0;

for (let r = 0; r < rows; r++) {
  const dir = dirStart + r;
  for (let c = 0; c < cols; c++) {
    const index = frame.start + dir * stride + c;
    if (index < 0 || index >= lib.count) continue;
    const img = readImage(lib, index);
    if (!img) continue;

    // 目标缩放：让精灵高度占满格子（留边距）
    const scale = Math.max(1, Math.floor(Math.min((cell - pad * 2) / img.w, (cell - pad * 2) / img.h)));
    const dw = img.w * scale;
    const dh = img.h * scale;
    const ox = c * cell + Math.floor((cell - dw) / 2);
    const oy = r * cell + cell - pad - dh;

    for (let y = 0; y < dh; y++) {
      for (let x = 0; x < dw; x++) {
        const sx = Math.floor(x / scale);
        const sy = Math.floor(y / scale);
        const si = (sy * img.w + sx) * 4;
        const a = img.bgra[si + 3];
        if (a < 8) continue;

        const di = ((oy + y) * W + (ox + x)) * 4;
        // BGRA -> RGB (背景不透明，直接按 alpha 混合)
        const sr = img.bgra[si + 2];
        const sg = img.bgra[si + 1];
        const sb = img.bgra[si];
        const al = a / 255;
        px[di] = Math.round(sr * al + px[di] * (1 - al));
        px[di + 1] = Math.round(sg * al + px[di + 1] * (1 - al));
        px[di + 2] = Math.round(sb * al + px[di + 2] * (1 - al));
      }
    }
    drawn++;
  }
}

fs.writeFileSync(outFile, encodePng(px, W, H));
console.log(`动作 ${action} 方向 ${dirStart}..${dirStart + rows - 1}，${cols} 帧/方向，共 ${drawn} 格`);
console.log(`输出 ${outFile}  ${W}x${H}  （每格 ${cell}px）`);
