/**
 * 复现"窗口尺寸/DPR 变化后画面出现黑边"：
 *   进图 → 改窗口尺寸 → 采样四边黑边宽度 + canvas 与 host 的尺寸对比
 *   node tools/diag-resize.mjs
 */
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 }, deviceScaleFactor: 2 });
page.on('pageerror', (e) => console.log('PAGEERROR', e.message));
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) {
  if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break;
  await page.waitForTimeout(500);
}
await page.waitForTimeout(2500);
const chat = async (t) => { await page.evaluate((x) => window.chat(x), t); await page.waitForTimeout(400); };
await chat('@LOGIN'); await chat('C#Mir 4.0'); await chat('@MOVE 286 607');
await page.waitForTimeout(3500);

const geom = () => page.evaluate(() => {
  const app = window.renderer.app;
  const host = document.querySelector('#stage');
  return {
    canvasPx: [app.canvas.width, app.canvas.height],
    canvasCss: [app.canvas.clientWidth, app.canvas.clientHeight],
    hostCss: [host.clientWidth, host.clientHeight],
    rendererSize: [window.renderer.width, window.renderer.height],
    dpr: window.devicePixelRatio,
    resolution: app.renderer.resolution,
  };
});
console.log('初始', JSON.stringify(await geom()));
await page.screenshot({ path: path.join(outDir, 'resize-before.png') });

// 改窗口尺寸（模拟用户拖窗口 / 换分辨率）
await page.setViewportSize({ width: 1522, height: 761 });
await page.waitForTimeout(600);
console.log('改尺寸后', JSON.stringify(await geom()));
await page.screenshot({ path: path.join(outDir, 'resize-after.png') });

// 再模拟缩放（改 DPR）：Playwright 不支持动态改 DPR，用浏览器缩放 API 不行，这里只测尺寸
await page.setViewportSize({ width: 1000, height: 700 });
await page.waitForTimeout(600);
console.log('再改尺寸', JSON.stringify(await geom()));
await page.screenshot({ path: path.join(outDir, 'resize-after2.png') });

await browser.close();
