#!/usr/bin/env node
// preview-compare.mjs —— 把 3D 渲染帧和原版 .Lib 里的某一帧摆在一起看「1x 是否还站得住」。
//
//   node Web/tools/preview-compare.mjs <参考Lib> <帧号> <渲染png> <输出png>
//        [--zoom 4] [--bg 2a2a30] [--gap 6]
//
// 做的事：
//   1. 读参考 Lib 的该帧（原版像素，含它的棋盘阴影），取 alpha 外框高度
//   2. 读渲染 png（带 alpha 的 3D 渲染），按外框**高度**缩放到与参考帧一致（等比例，最近邻，保持像素硬边）
//   3. 并排合成：左=原版，右=新渲染；底对齐、水平居中；输出放大 --zoom 倍便于肉眼判断
//   4. 同时输出一张 1x 的对照条 <输出png>.1x.png（游戏里实际大小）

import fs from 'node:fs';
import path from 'node:path';
import { readLib, readImage } from './lib.mjs';
import { decodePng, encodePng, hexToRgb } from './png.mjs';

const args = process.argv.slice(2);
const positional = args.filter((a) => !a.startsWith('--'));
const opt = (name, def) => {
  const i = args.indexOf('--' + name);
  return i < 0 ? def : args[i + 1];
};
if (positional.length < 4) {
  console.error('用法: node Web/tools/preview-compare.mjs <参考Lib> <帧号> <渲染png> <输出png> [--zoom 4] [--bg 2a2a30] [--gap 6]');
  process.exit(2);
}
const [refLibPath, frameArg, renderPath, outPath] = positional;
const zoom = Number(opt('zoom', 4));
const gap = Number(opt('gap', 6));
const bg = hexToRgb(opt('bg', '2a2a30'));

const lib = readLib(refLibPath);
const frameIndex = Number(frameArg);
const ref = readImage(lib, frameIndex);
const refW = ref.w;
const refH = ref.h;
const refRgba = Buffer.from(ref.bgra); // BGRA
const refAlpha = (x, y) => refRgba[(y * refW + x) * 4 + 3];

function bbox(getPx, w, h) {
  let x0 = w, y0 = h, x1 = -1, y1 = -1;
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < w; x++) {
      if (getPx(x, y) > 16) {
        if (x < x0) x0 = x;
        if (x > x1) x1 = x;
        if (y < y0) y0 = y;
        if (y > y1) y1 = y;
      }
    }
  }
  return { x0, y0, x1, y1, w: x1 - x0 + 1, h: y1 - y0 + 1 };
}

const render = decodePng(fs.readFileSync(renderPath));
const rd = render.rgba;
const rdA = (x, y) => rd[(y * render.w + x) * 4 + 3];

const rb = bbox(refAlpha, refW, refH);
const nb = bbox(rdA, render.w, render.h);

// 渲染帧按"外框高度对齐"缩放（最近邻，保持硬边）
const scale = rb.h / nb.h;
const nw = Math.max(1, Math.round(nb.w * scale));
const nh = rb.h;

function sampleNearest(x, y) {
  const sx = Math.min(nb.w - 1, Math.max(0, Math.floor(x / scale)));
  const sy = Math.min(nb.h - 1, Math.max(0, Math.floor(y / scale)));
  const px = nb.x0 + sx;
  const py = nb.y0 + sy;
  const i = (py * render.w + px) * 4;
  return [rd[i], rd[i + 1], rd[i + 2], rd[i + 3]];
}

// 画布：两张并排，各自用原帧画布尺寸（参考帧用 lib 的 x/y 偏移定位）
const cellW = Math.max(rb.w, nw);
const canvasW = Math.ceil(cellW * 2 + gap);
const canvasH = Math.max(refH, nh + 4);
const rgba = Buffer.alloc(canvasW * canvasH * 4);
for (let i = 0; i < canvasW * canvasH; i++) {
  rgba[i * 4] = bg.r;
  rgba[i * 4 + 1] = bg.g;
  rgba[i * 4 + 2] = bg.b;
  rgba[i * 4 + 3] = 255;
}
const blend = (x, y, r, g, b, a) => {
  if (x < 0 || y < 0 || x >= canvasW || y >= canvasH || a === 0) return;
  const i = (y * canvasW + x) * 4;
  const k = a / 255;
  rgba[i] = Math.round(r * k + rgba[i] * (1 - k));
  rgba[i + 1] = Math.round(g * k + rgba[i + 1] * (1 - k));
  rgba[i + 2] = Math.round(b * k + rgba[i + 2] * (1 - k));
  rgba[i + 3] = 255;
};

// 左：原版帧（按"墨迹外框"贴到画布左上，避免 x/y 是相对角色锚点导致跑出画面）
for (let y = rb.y0; y <= rb.y1; y++) {
  for (let x = rb.x0; x <= rb.x1; x++) {
    const i = (y * refW + x) * 4;
    const a = refRgba[i + 3];
    if (a > 0) blend(x - rb.x0, y - rb.y0, refRgba[i + 2], refRgba[i + 1], refRgba[i], a);
  }
}
// 右：渲染帧（底对齐、水平居中于同一格）
const offX = Math.round(cellW + gap / 2 + (cellW - nw) / 2);
const offY = rb.h - nh;      // 与参考帧底对齐
for (let y = 0; y < nh; y++) {
  for (let x = 0; x < nw; x++) {
    const [r, g, b, a] = sampleNearest(x, y);
    if (a > 8) blend(offX + x, offY + y, r, g, b, a);
  }
}

// 放大输出
const z = Math.max(1, Math.round(zoom));
const zw = canvasW * z;
const zh = canvasH * z;
const zoomed = Buffer.alloc(zw * zh * 4);
for (let y = 0; y < zh; y++) {
  for (let x = 0; x < zw; x++) {
    const si = ((Math.floor(y / z)) * canvasW + Math.floor(x / z)) * 4;
    const di = (y * zw + x) * 4;
    zoomed[di] = rgba[si];
    zoomed[di + 1] = rgba[si + 1];
    zoomed[di + 2] = rgba[si + 2];
    zoomed[di + 3] = 255;
  }
}
fs.mkdirSync(path.dirname(path.resolve(outPath)), { recursive: true });
fs.writeFileSync(outPath, encodePng(zoomed, zw, zh));
fs.writeFileSync(outPath.replace(/\.png$/, '') + '.1x.png', encodePng(rgba, canvasW, canvasH));
console.log(`参考帧 #${frameIndex}: 画布 ${refW}x${refH} offset=(${ref.x},${ref.y}) 墨迹 ${rb.w}x${rb.h}`);
console.log(`渲染帧: ${render.w}x${render.h} 墨迹 ${nb.w}x${nb.h} → 缩放 ${scale.toFixed(3)} → ${nw}x${nh}`);
console.log(`输出: ${outPath} (${zw}x${zh}, ${z}x) + ${outPath.replace(/\.png$/, '')}.1x.png`);
