/**
 * 打包：把一堆 PNG 帧做成 .Lib（v3），帧表可以从现成的 Lib 里抄
 *
 * 用法:
 *   node Web/tools/pack-lib.mjs <帧目录> <输出Lib> [--actions-from <参考Lib>] [--index <起始索引>]
 *
 * 帧目录里的 PNG 按文件名排序，依次成为 index 0,1,2...
 * 记录头里的偏移（x,y = 绘制偏移；shadowX,shadowY = 影子偏移）默认取 0，
 * 用 --actions-from 指定参考 Lib 时会按顺序抄它的 w/h/x/y/shadow 元数据（尺寸不同则只抄偏移）。
 */
import fs from 'node:fs';
import path from 'node:path';
import { readLib, readImage, writeLib, rgbaToBgra } from './lib.mjs';
import { decodePng } from './png.mjs';

const args = process.argv.slice(2);
const [framesDir, outFile] = args;
if (!framesDir || !outFile) {
  console.error('用法: node Web/tools/pack-lib.mjs <帧目录> <输出Lib> [--actions-from <参考Lib>] [--start <索引>]');
  process.exit(1);
}
const refIdx = args.indexOf('--actions-from');
const refLib = refIdx >= 0 ? readLib(args[refIdx + 1]) : null;
const startIdx = (() => {
  const i = args.indexOf('--start');
  return i >= 0 ? Number(args[i + 1]) : 0;
})();

const files = fs
  .readdirSync(framesDir)
  .filter((f) => f.toLowerCase().endsWith('.png'))
  .sort();

const images = new Array(startIdx).fill(null);
files.forEach((f, n) => {
  const png = decodePng(fs.readFileSync(path.join(framesDir, f)));
  let meta = { x: 0, y: -png.h, shadowX: 0, shadowY: 0, shadow: 0 };
  if (refLib) {
    const ref = readImage(refLib, n);
    if (ref) meta = { x: ref.x, y: ref.y, shadowX: ref.shadowX, shadowY: ref.shadowY, shadow: ref.shadow };
  }
  images.push({
    w: png.w,
    h: png.h,
    x: meta.x,
    y: meta.y,
    shadowX: meta.shadowX,
    shadowY: meta.shadowY,
    shadow: meta.shadow,
    bgra: rgbaToBgra(png.rgba),
  });
});

const res = writeLib(outFile, images, refLib ? refLib.frames : []);
console.log(
  `写出 ${res.file}: ${files.length} 张图（索引 ${startIdx}~${startIdx + files.length - 1}），帧表 ${refLib ? refLib.frames.length : 0} 项，${(res.size / 1024).toFixed(1)}KB`,
);
