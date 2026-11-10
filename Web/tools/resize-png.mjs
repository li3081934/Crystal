/**
 * 把 PNG 缩放到指定尺寸（用于"模型输出的档位尺寸 → 精灵表原始尺寸"）。
 *
 * 用法:
 *   node Web/tools/resize-png.mjs <in.png> <out.png> <W>x<H> [--filter nearest|box]
 *
 *   nearest  最近邻（像素风首选，但缩小会丢像素）
 *   box      面积平均（缩小更平滑，默认）
 */
import fs from 'node:fs';
import { decodePng, encodePng } from './png.mjs';

const args = process.argv.slice(2);
const [inFile, outFile, sizeArg] = args;
if (!inFile || !outFile || !sizeArg) {
  console.error('用法: node Web/tools/resize-png.mjs <in.png> <out.png> <W>x<H> [--filter nearest|box]');
  process.exit(1);
}
const filterIdx = args.indexOf('--filter');
const filter = filterIdx >= 0 ? args[filterIdx + 1] : 'box';
const m = /^(\d+)x(\d+)$/.exec(sizeArg);
if (!m) {
  console.error('尺寸格式应为 WxH');
  process.exit(1);
}
const outW = Number(m[1]);
const outH = Number(m[2]);

const { w, h, rgba } = decodePng(fs.readFileSync(inFile));
const out = Buffer.alloc(outW * outH * 4);

if (filter === 'nearest') {
  for (let y = 0; y < outH; y++) {
    const sy = Math.min(h - 1, Math.floor((y * h) / outH));
    for (let x = 0; x < outW; x++) {
      const sx = Math.min(w - 1, Math.floor((x * w) / outW));
      const si = (sy * w + sx) * 4;
      const di = (y * outW + x) * 4;
      out[di] = rgba[si];
      out[di + 1] = rgba[si + 1];
      out[di + 2] = rgba[si + 2];
      out[di + 3] = rgba[si + 3];
    }
  }
} else {
  // 面积平均（分离可分的 box 采样，先横后竖）
  const tmp = new Float32Array(outW * h * 4);
  for (let y = 0; y < h; y++) {
    for (let x = 0; x < outW; x++) {
      const x0 = (x * w) / outW;
      const x1 = ((x + 1) * w) / outW;
      let r = 0, g = 0, b = 0, a = 0, n = 0;
      for (let sx = Math.floor(x0); sx < Math.ceil(x1); sx++) {
        const cx = Math.min(w - 1, sx);
        const si = (y * w + cx) * 4;
        r += rgba[si];
        g += rgba[si + 1];
        b += rgba[si + 2];
        a += rgba[si + 3];
        n++;
      }
      const di = (y * outW + x) * 4;
      tmp[di] = r / n;
      tmp[di + 1] = g / n;
      tmp[di + 2] = b / n;
      tmp[di + 3] = a / n;
    }
  }
  for (let y = 0; y < outH; y++) {
    const y0 = (y * h) / outH;
    const y1 = ((y + 1) * h) / outH;
    for (let x = 0; x < outW; x++) {
      let r = 0, g = 0, b = 0, a = 0, n = 0;
      for (let sy = Math.floor(y0); sy < Math.ceil(y1); sy++) {
        const cy = Math.min(h - 1, sy);
        const si = (cy * outW + x) * 4;
        r += tmp[si];
        g += tmp[si + 1];
        b += tmp[si + 2];
        a += tmp[si + 3];
        n++;
      }
      const di = (y * outW + x) * 4;
      out[di] = Math.round(r / n);
      out[di + 1] = Math.round(g / n);
      out[di + 2] = Math.round(b / n);
      out[di + 3] = Math.round(a / n);
    }
  }
}

fs.writeFileSync(outFile, encodePng(out, outW, outH));
console.log(`${inFile} ${w}x${h} → ${outFile} ${outW}x${outH} (${filter})`);
