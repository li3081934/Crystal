/** 在指定坐标截图（尽量走到那个格子） node tools/shot-at.mjs 300 624 [输出名] */
import { chromium } from 'playwright';
const tx = Number(process.argv[2] ?? 300), ty = Number(process.argv[3] ?? 624);
const out = process.argv[4] ?? `shot-${tx}-${ty}.png`;
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(1500);
await page.evaluate(() => window.chat('@LOGIN')); await page.waitForTimeout(400);
await page.evaluate(() => window.chat('C#Mir 4.0')); await page.waitForTimeout(400);
for (let i = 0; i < 20; i++) {
  const c = await page.evaluate(() => [window.renderer.camCellX, window.renderer.camCellY]);
  if (Math.abs(c[0] - tx) <= 1 && Math.abs(c[1] - ty) <= 1) break;
  await page.mouse.click(600 + Math.max(-380, Math.min(380, (tx - c[0]) * 48)), 400 + Math.max(-240, Math.min(240, (ty - c[1]) * 32)));
  await page.waitForTimeout(1800);
}
await page.waitForTimeout(2000);
console.log(JSON.stringify(await page.evaluate(() => [window.renderer.camCellX, window.renderer.camCellY])));
await page.screenshot({ path: `shots/${out}` });
await browser.close();
