/** 把某张地图图导出成 png 看内容  node tools/dump-tile.mjs Objects.Lib 8681 [输出名] */
import { chromium } from 'playwright';
import fs from 'node:fs';
const [lib = 'Objects.Lib', idx = '8681', out = `tile-${idx}.png`] = process.argv.slice(2);
const browser = await chromium.launch();
const page = await browser.newPage();
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
await page.waitForTimeout(4000);
const data = await page.evaluate(async ([lib, idx]) => {
  const r = window.renderer;
  const libObj = await r.store.prime(`Data/Map/WemadeMir2/${lib}`);
  if (!libObj) return null;
  const bmp = await libObj.image(Number(idx));
  if (!bmp) return null;
  const c = document.createElement('canvas');
  c.width = bmp.width; c.height = bmp.height;
  const ctx = c.getContext('2d');
  ctx.drawImage(bmp, 0, 0);
  return c.toDataURL('image/png');
}, [lib, idx]);
if (!data) { console.log('取不到'); process.exit(1); }
fs.writeFileSync(`shots/${out}`, Buffer.from(data.split(',')[1], 'base64'));
console.log('saved', out);
await browser.close();
