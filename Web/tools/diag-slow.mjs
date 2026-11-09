/**
 * 模拟慢网络：给 /assets 请求加延迟，再看走路时还会不会出现黑块（应该只剩兜底地面色）
 *   node tools/diag-slow.mjs
 */
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1522, height: 761 }, deviceScaleFactor: 2 });
page.on('pageerror', (e) => console.log('PAGEERROR', e.message));

// 每个资源请求延迟 450ms，模拟"边下边玩"
let slowed = false;
await page.route('**/assets/**', async (route) => {
  if (slowed) await new Promise((r) => setTimeout(r, 450));
  await route.continue();
});

await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) {
  if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break;
  await page.waitForTimeout(500);
}
await page.waitForTimeout(2500);
const chat = async (t) => { await page.evaluate((x) => window.chat(x), t); await page.waitForTimeout(400); };
await chat('@LOGIN'); await chat('C#Mir 4.0'); await chat('@MOVE 299 602');
await page.waitForTimeout(4000);

slowed = true;
const stat = () => page.evaluate(() => {
  const r = window.renderer;
  const app = r.app;
  app.render();
  const c = document.createElement('canvas');
  c.width = app.canvas.width; c.height = app.canvas.height;
  const ctx = c.getContext('2d');
  ctx.drawImage(app.canvas, 0, 0);
  const img = ctx.getImageData(0, 0, c.width, c.height);
  const dpr = c.width / r.width;
  let black = 0, total = 0;
  for (let py = 0; py < r.height - 280; py += 4) {
    for (let px = 0; px < r.width; px += 4) {
      total++;
      const i = (Math.round(py * dpr) * c.width + Math.round(px * dpr)) * 4;
      if (img.data[i] < 12 && img.data[i + 1] < 12 && img.data[i + 2] < 12) black++;
    }
  }
  return { blackPct: +((black / total) * 100).toFixed(2), pending: r.pendingTiles.length, ground: r.groundColor.toString(16) };
});

for (let i = 0; i < 8; i++) {
  await page.mouse.click(300 + i * 110, 250 + i * 40);
  await page.waitForTimeout(350);
  console.log(i, JSON.stringify(await stat()));
  if (i === 3) await page.screenshot({ path: path.join(outDir, 'slow-walk.png') });
}

await browser.close();
