import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1522, height: 761 }, deviceScaleFactor: 2 });
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(2500);
const chat = async (t) => { await page.evaluate((x) => window.chat(x), t); await page.waitForTimeout(400); };
await chat('@LOGIN'); await chat('C#Mir 4.0'); await chat('@MOVE 286 607');
await page.waitForTimeout(5000);
await page.screenshot({ path: path.join(outDir, 'final-clean.png') });
console.log(JSON.stringify(await page.evaluate(() => ({ pos: [window.state.self.cellX, window.state.self.cellY], pending: window.renderer.pendingTiles.length, textures: window.renderer.textures.size }))));
await browser.close();
