import { chromium, devices } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const browser = await chromium.launch();
const context = await browser.newContext({ ...devices['iPhone 13'] });
const page = await context.newPage();
page.on('pageerror', (e) => console.log('PAGEERROR', e.message));
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 60; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(5000);
await page.screenshot({ path: path.join(outDir, 'mobile-map.png') });
// 点屏幕右上角走一段
await page.touchscreen.tap(280, 300);
await page.waitForTimeout(2500);
await page.screenshot({ path: path.join(outDir, 'mobile-walk.png') });
console.log('mobile shots done', await page.evaluate(() => document.querySelector('#hud-info')?.textContent));
await browser.close();
