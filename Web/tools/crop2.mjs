/** 截图裁剪/放大：node tools/crop2.mjs <源图> <输出名> x y w h scale */
import { chromium } from 'playwright';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const [src, out, x, y, w, h, scale] = process.argv.slice(2);
if (!src || !out) {
  console.error('用法: node tools/crop2.mjs <源图> <输出名> x y w h scale');
  process.exit(1);
}

const srcPath = path.resolve(src);
const outPath = path.resolve(out);
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 900, height: 900 } });
await page.goto(pathToFileURL(srcPath).href);
const data = await page.evaluate(([x, y, w, h, scale]) => {
  const img = document.querySelector('img');
  const c = document.createElement('canvas');
  c.width = w * scale;
  c.height = h * scale;
  const ctx = c.getContext('2d');
  ctx.imageSmoothingEnabled = false;
  ctx.drawImage(img, x, y, w, h, 0, 0, w * scale, h * scale);
  return c.toDataURL('image/png');
}, [x, y, w, h, scale].map(Number));
fs.writeFileSync(outPath, Buffer.from(data.split(',')[1], 'base64'));
await browser.close();
