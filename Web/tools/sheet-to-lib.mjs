/**
 * 把"网格精灵表"切回 .Lib 帧：用参考 Lib 的几何（画布尺寸、偏移、阴影）逐格替换。
 *
 * 用法:
 *   node Web/tools/sheet-to-lib.mjs <参考Lib> <精灵表png> <输出Lib> <action> <dirStart> \
 *        [--cell 256] [--key FF00FF] [--posterize 6] [--threshold 90] [--debug 目录]
 *
 * 做什么：
 *   1. 逐格抠掉纯色背景（默认洋红），得到每格的精灵外框
 *   2. 按"精灵高度 = 参考帧精灵高度"缩放（面积平均下采样，保证像素不被平均成糊）
 *   3. 底部对齐、水平居中贴回参考帧的画布，画布尺寸 / x,y / 阴影完全沿用参考帧
 *   4. 帧表照抄参考 Lib，只换像素
 *
 * 其余动作（走/砍/躺）仍用参考 Lib 原帧 —— 之后可以用同一工具再跑一遍。
 */
import fs from 'node:fs';
import path from 'node:path';
import { readLib, readImage, writeLib } from './lib.mjs';
import { decodePng, encodePng, hexToRgb } from './png.mjs';

const args = process.argv.slice(2);
const positional = [];
let PALETTE_SIZE = 40;
const opt = { fitx: false, uniform: false, fresh: false, keepAll: false, cell: 256, cellW: 0, cellH: 0, key: 'FF00FF', posterize: 0, threshold: 90, quantize: 0, scale: 0, debug: null, palette: null };

for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === '--fresh') opt.fresh = true;
  else if (a === '--keep-all') opt.keepAll = true;
  else if (a === '--uniform') opt.uniform = true;
  else if (a === '--cell') opt.cell = Number(args[++i]);
  else if (a === '--cellw') opt.cellW = Number(args[++i]);
  else if (a === '--cellh') opt.cellH = Number(args[++i]);
  else if (a === '--key') opt.key = args[++i];
  else if (a === '--posterize') opt.posterize = Number(args[++i]);
  else if (a === '--threshold') opt.threshold = Number(args[++i]);
  else if (a === '--quantize') opt.quantize = Number(args[++i]);
  else if (a === '--palette-from') opt.palette = args[++i];
  else if (a === '--palette-size') PALETTE_SIZE = Number(args[++i]);
  else if (a === '--scale') opt.scale = Number(args[++i]);
  else if (a === '--fitx') opt.fitx = true;
  else if (a === '--debug') opt.debug = args[++i];
  else positional.push(a);
}

const [refFile, sheetFile, outFile, actionArg, dirStartArg] = positional;
if (!refFile || !sheetFile || !outFile) {
  console.error('用法: node Web/tools/sheet-to-lib.mjs <参考Lib> <精灵表png> <输出Lib> <action> <dirStart> [--cell 256] [--key FF00FF] [--posterize 6] [--threshold 90] [--debug 目录]');
  process.exit(1);
}

const action = Number(actionArg ?? 0);
const dirStart = Number(dirStartArg ?? 0);
const cell = opt.cell;
const cellW = opt.cellW || cell;
const cellH = opt.cellH || cell;
const key = hexToRgb(opt.key);

const refLib = readLib(refFile);

// ---- 调色板吸附（必须在 refLib 之后构建）----
const OPT_PALETTE = opt.palette ? buildPalette(readLib(opt.palette), PALETTE_SIZE) : null;
// 参考 Lib 的调色板里没有金/绿这类原版没有的颜色，直接用会把它们吸附成骨色 → 再把源表的主色补进调色板
if (OPT_PALETTE) {
  const sheetPng = decodePng(fs.readFileSync(sheetFile));
  const added = augmentPalette(OPT_PALETTE, sheetPng, 12, 68);
  console.log('调色板: ' + OPT_PALETTE.length + ' 色（' + (OPT_PALETTE.length - added) + ' 取自 ' + opt.palette + '，' + added + ' 取自源表新增色）');
}
const frame = refLib.frames.find((f) => f.action === action);
if (!frame) {
  console.error(`参考 Lib 里没有动作 ${action}`);
  process.exit(1);
}
const stride = frame.count + frame.skip;

const sheet = decodePng(fs.readFileSync(sheetFile));
const sheetW = sheet.w;
const sheetH = sheet.h;
const sheetPx = sheet.rgba;
const cols = Math.floor(sheetW / cellW);
const rows = Math.floor(sheetH / cellH);
console.log(`精灵表 ${sheetW}x${sheetH} → ${cols} 列 x ${rows} 行（每格 ${cellW}x${cellH}px）`);

const images = [];
for (let i = 0; i < refLib.count; i++) images.push(readImage(refLib, i));

/** 把整帧的 BGRA 转成可写回的结构 */
function toLibImage(src) {
  return {
    w: src.w,
    h: src.h,
    x: src.x,
    y: src.y,
    shadowX: src.shadowX,
    shadowY: src.shadowY,
    shadow: src.shadow,
    bgra: Buffer.from(src.bgra),
  };
  // --fresh：不要参考帧的原像素（只借用画布尺寸与对齐），直接给一张全透明帧
  if (opt.fresh) frame.bgra.fill(0);
}

/** 抠色 + 外框 */
function cellSprite(cx, cy) {
  const pixels = [];
  let minX = Infinity, minY = Infinity, maxX = -1, maxY = -1;

  for (let y = 0; y < cellH; y++) {
    for (let x = 0; x < cellW; x++) {
      const gx = cx + x;
      const gy = cy + y;
      if (gx >= sheetW || gy >= sheetH) continue;

      const si = (gy * sheetW + gx) * 4;
      const r = sheetPx[si], g = sheetPx[si + 1], b = sheetPx[si + 2];

      // 洋红背景判定：绝对距离够近，或者"红蓝都很亮、绿明显偏低"（模型混色后的粉边）
      const d = Math.abs(r - key.r) + Math.abs(g - key.g) + Math.abs(b - key.b);
      const magentaish = Math.min(r, b) - g > 55;            // 洋红背景（含模型混出来的粉）
      const purpleEdge = Math.min(r, b) - g > 28 && Math.max(r, b) > 45; // 模型留的紫边
      if (d < opt.threshold || magentaish || purpleEdge) continue;

      pixels.push({ x, y, r, g, b });
      if (x < minX) minX = x;
      if (y < minY) minY = y;
      if (x > maxX) maxX = x;
      if (y > maxY) maxY = y;
    }
  }

  if (maxX < 0) return null;
  return { pixels, minX, minY, maxX, maxY, w: maxX - minX + 1, h: maxY - minY + 1 };
}

/** 参考帧里"精灵"的外框（alpha > 16） */
function refBounds(img) {
  let minX = img.w, minY = img.h, maxX = -1, maxY = -1;
  for (let y = 0; y < img.h; y++) {
    for (let x = 0; x < img.w; x++) {
      if (img.bgra[(y * img.w + x) * 4 + 3] <= 16) continue;
      if (x < minX) minX = x;
      if (y < minY) minY = y;
      if (x > maxX) maxX = x;
      if (y > maxY) maxY = y;
    }
  }
  if (maxX < 0) return { x: 0, y: 0, w: img.w, h: img.h };
  return { x: minX, y: minY, w: maxX - minX + 1, h: maxY - minY + 1 };
}

/** 从原版 Lib 里统计出它的调色板（跳过阴影色 #100808），新像素吸附上去就能"同色系" */
function buildPalette(lib, maxColors) {
  const hist = new Map();
  const frames = Math.min(lib.count, 300);
  for (let i = 0; i < frames; i++) {
    const img = readImage(lib, i);
    if (!img) continue;
    for (let p = 0; p < img.w * img.h; p++) {
      const a = img.bgra[p * 4 + 3];
      if (a < 200) continue;
      const b = img.bgra[p * 4], g = img.bgra[p * 4 + 1], r = img.bgra[p * 4 + 2];
      if (b <= 40 && g <= 30 && r <= 40) continue;          // 棋盘阴影
      const key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
      const e = hist.get(key);
      if (e) { e.n++; e.r += r; e.g += g; e.b += b; }
      else hist.set(key, { n: 1, r, g, b });
    }
  }
  return [...hist.values()]
    .sort((a, b) => b.n - a.n)
    .slice(0, maxColors)
    .map((e) => [Math.round(e.r / e.n), Math.round(e.g / e.n), Math.round(e.b / e.n)]);
}

/** 把画布上每个像素换成调色板里最接近的颜色（按人眼权重加权距离） */
function augmentPalette(palette, img, maxAdd, minDist) {
  const hist = new Map();
  for (let p = 0; p < img.w * img.h; p++) {
    const a = img.rgba[p * 4 + 3];
    if (a < 200) continue;
    const r = img.rgba[p * 4], g = img.rgba[p * 4 + 1], b = img.rgba[p * 4 + 2];
    if (r > 200 && g < 90 && b > 200) continue;              // 洋红底
    const key = ((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3);
    const e = hist.get(key);
    if (e) { e.n++; e.r += r; e.g += g; e.b += b; }
    else hist.set(key, { n: 1, r, g, b });
  }
  const list = [...hist.values()].sort((a, b) => b.n - a.n).map((e) => [Math.round(e.r / e.n), Math.round(e.g / e.n), Math.round(e.b / e.n)]);
  let added = 0;
  for (const c of list) {
    if (added >= maxAdd) break;
    const near = palette.some((p) => Math.hypot(p[0] - c[0], p[1] - c[1], p[2] - c[2]) < minDist);
    if (near) continue;
    palette.push(c); added++;
  }
  return added;
}

function snapToPalette(canvas, palette) {
  if (!palette || !palette.length) return;
  const cache = new Map();
  for (let i = 0; i < canvas.length; i += 4) {
    if (canvas[i + 3] < 8) continue;
    const b = canvas[i], g = canvas[i + 1], r = canvas[i + 2];
    const key = (r << 16) | (g << 8) | b;
    let best = cache.get(key);
    if (!best) {
      let bestD = Infinity;
      for (const [pr, pg, pb] of palette) {
        const dr = (r - pr) * 0.9, dg = (g - pg) * 1.2, db = (b - pb) * 0.8;
        const d = dr * dr + dg * dg + db * db;
        if (d < bestD) { bestD = d; best = [pr, pg, pb]; }
      }
      cache.set(key, best);
    }
    canvas[i] = best[2];
    canvas[i + 1] = best[1];
    canvas[i + 2] = best[0];
  }
}

function quantizeCanvas(canvas, colors) {
  if (!colors || colors <= 0) return;
  const step = 255 / (colors - 1);
  for (let i = 0; i < canvas.length; i += 4) {
    if (canvas[i + 3] < 8) continue;
    canvas[i] = Math.round(Math.round(canvas[i] / step) * step);
    canvas[i + 1] = Math.round(Math.round(canvas[i + 1] / step) * step);
    canvas[i + 2] = Math.round(Math.round(canvas[i + 2] / step) * step);
  }
}

function posterize(v) {
  if (!opt.posterize) return v;
  const step = 255 / (opt.posterize - 1);
  return Math.round(Math.round(v / step) * step);
}

let replaced = 0;
const scales = [];
const debugDir = opt.debug;

// 先算出每格精灵外框，按行取中位数，明显跑尺寸的格子（模型偶尔会放大一格）跳过不替换
const cells = [];
for (let r = 0; r < rows; r++) {
  const row = [];
  for (let c = 0; c < cols; c++) row.push(cellSprite(c * cellW, r * cellH));
  const heights = row.filter(Boolean).map((s) => s.h).sort((a, b) => a - b);
  const median = heights.length ? heights[Math.floor(heights.length / 2)] : 0;
  cells.push({ row, median });
}

// --uniform：整张表用同一个缩放系数（取所有格子的中位数），避免不同动作/方向之间角色忽大忽小
let uniformScale = 0;
if (opt.uniform) {
  const all = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < cols; c++) {
      const sp = cells[r].row[c];
      if (!sp) continue;
      const idx = frame.start + (dirStart + r) * stride + c;
      if (idx < 0 || idx >= refLib.count || !images[idx]) continue;
      all.push(refBounds(images[idx]).h / sp.h);
    }
  }
  all.sort((a, b) => a - b);
  uniformScale = all.length ? all[Math.floor(all.length / 2)] : 0;
  console.log(`  统一缩放系数 ${uniformScale.toFixed(3)}（${all.length} 格）`);
}

for (let r = 0; r < rows; r++) {
  const dir = dirStart + r;
  for (let c = 0; c < cols; c++) {
    const index = frame.start + dir * stride + c;
    if (index < 0 || index >= refLib.count) continue;

    const src = images[index];
    if (!src) continue;

    const sprite = cells[r].row[c];
    if (!sprite) {
      console.log(`  跳过 ${index}（第 ${c} 列第 ${r} 行：整格都是背景色）`);
      continue;
    }
    if (!opt.keepAll && cells[r].median > 0 && Math.abs(sprite.h - cells[r].median) / cells[r].median > 0.25) {
      console.log(`  跳过 ${index}（第 ${c} 列第 ${r} 行：精灵高度 ${sprite.h} 与同行中位数 ${cells[r].median} 差异过大，保留原图）`);
      continue;
    }

    const rb = refBounds(src);
    const scaleY = opt.scale > 0 ? opt.scale : (uniformScale > 0 ? uniformScale : rb.h / sprite.h);
    // --fitx：X 方向单独拟合原帧宽度（模型画的骷髅普遍偏瘦），限制在 Y 缩放的 0.75~1.6 倍
    let scaleX = scaleY;
    if (opt.fitx) {
      const fit = rb.w / sprite.w;
      scaleX = Math.min(scaleY * 1.6, Math.max(scaleY * 0.75, fit));
    }
    scales.push(scaleY);
    const dw = Math.max(1, Math.round(sprite.w * scaleX));
    const dh = Math.max(1, Math.round(sprite.h * scaleY));

    // 面积平均下采样：先把源像素装桶，再按目标像素覆盖的源面积加权
    const out = Buffer.alloc(dw * dh * 4);
    for (let ty = 0; ty < dh; ty++) {
      const sy0 = sprite.minY + (ty * sprite.h) / dh;
      const sy1 = sprite.minY + ((ty + 1) * sprite.h) / dh;
      for (let tx = 0; tx < dw; tx++) {
        const sx0 = sprite.minX + (tx * sprite.w) / dw;
        const sx1 = sprite.minX + ((tx + 1) * sprite.w) / dw;

        let ar = 0, ag = 0, ab = 0, aa = 0, wsum = 0;
        for (const p of sprite.pixels) {
          const px0 = p.x, px1 = p.x + 1, py0 = p.y, py1 = p.y + 1;
          const ox = Math.max(0, Math.min(sx1, px1) - Math.max(sx0, px0));
          const oy = Math.max(0, Math.min(sy1, py1) - Math.max(sy0, py0));
          const w = ox * oy;
          if (w <= 0) continue;
          ar += p.r * w; ag += p.g * w; ab += p.b * w; aa += w; wsum += w;
        }
        if (wsum <= 0) continue; // 目标像素落在透明区

        const di = (ty * dw + tx) * 4;
        out[di] = posterize(ab / wsum);
        out[di + 1] = posterize(ag / wsum);
        out[di + 2] = posterize(ar / wsum);
        out[di + 3] = 255;
      }
    }

    // 贴回参考帧画布：底部对齐在参考精灵底部，水平居中在参考精灵中心
    const dstX = Math.round(rb.x + rb.w / 2 - dw / 2);
    const dstY = rb.y + rb.h - dh;

    // 新精灵可能比原画布宽/高（武器更大）：向外扩画布，并同步修正 x/y 偏移，保证站位不漂
    const left = Math.min(0, dstX);
    const top = Math.min(0, dstY);
    const right = Math.max(src.w, dstX + dw);
    const bottom = Math.max(src.h, dstY + dh);
    const nw = right - left;
    const nh = bottom - top;

    const canvas = Buffer.alloc(nw * nh * 4);

    // 先把原帧（含阴影）平移到新画布上；--fresh 时不要原像素（新美术自带完整角色，不保留旧的阴影层）
    if (!opt.fresh) {
      for (let y = 0; y < src.h; y++) {
        const srow = y * src.w * 4;
        const drow = ((y - top) * nw + (0 - left)) * 4;
        src.bgra.copy(canvas, drow, srow, srow + src.w * 4);
      }
    }

    for (let y = 0; y < dh; y++) {
      for (let x = 0; x < dw; x++) {
        const cx = dstX + x - left;
        const cy = dstY + y - top;
        if (cx < 0 || cy < 0 || cx >= nw || cy >= nh) continue;
        const si = (y * dw + x) * 4;
        if (out[si + 3] < 8) continue;
        const di = (cy * nw + cx) * 4;
        canvas[di] = out[si];      // B
        canvas[di + 1] = out[si + 1]; // G
        canvas[di + 2] = out[si + 2]; // R
        canvas[di + 3] = out[si + 3]; // A
      }
    }

    snapToPalette(canvas, OPT_PALETTE);
    quantizeCanvas(canvas, opt.quantize);
    images[index] = {
      ...toLibImage(src),
      w: nw,
      h: nh,
      x: src.x + left,
      y: src.y + top,
      bgra: canvas,
    };
    replaced++;

    if (debugDir) {
      const rgba = Buffer.alloc(nw * nh * 4);
      for (let i = 0; i < nw * nh; i++) {
        rgba[i * 4] = canvas[i * 4 + 2];
        rgba[i * 4 + 1] = canvas[i * 4 + 1];
        rgba[i * 4 + 2] = canvas[i * 4];
        rgba[i * 4 + 3] = canvas[i * 4 + 3];
      }
      fs.mkdirSync(debugDir, { recursive: true });
      fs.writeFileSync(path.join(debugDir, `${String(index).padStart(3, '0')}.png`), encodePng(rgba, nw, nh));
    }
  }
}

if (scales.length) {
  const sorted = [...scales].sort((a, b) => a - b);
  console.log(`缩放系数：中位数 ${sorted[Math.floor(sorted.length / 2)].toFixed(3)}，范围 ${sorted[0].toFixed(3)}~${sorted[sorted.length - 1].toFixed(3)}`);
}

const info = writeLib(outFile, images, refLib.frames);
console.log(`替换 ${replaced} 帧 → ${outFile}（${(info.size / 1024).toFixed(1)} KB）`);
