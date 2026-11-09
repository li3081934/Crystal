import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 800 } });
page.on('pageerror', (e) => console.log('PAGEERROR', e.message));
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 60; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(3000);
await page.keyboard.press('i');
await page.waitForTimeout(600);

// 找到 WoodenSword / BaseDress 的槽位并双击
const target = await page.evaluate(() => {
  const s = window.state;
  return s.inventory.map((it, i) => it && { i, name: it.info?.name }).filter(Boolean);
});
console.log('背包:', JSON.stringify(target));

for (const name of ['WoodenSword', 'BaseDress']) {
  const idx = await page.evaluate((n) => window.state.inventory.findIndex((it) => it?.info?.name?.includes(n)), name);
  if (idx < 0) continue;
  const slot = page.locator('#inventory-grid .slot').nth(idx);
  await slot.dblclick();
  await page.waitForTimeout(900);
}
await page.waitForTimeout(1200);
const after = await page.evaluate(() => ({
  equip: window.state.equipment.map((it, i) => it && `${i}:${it.info?.name}`).filter(Boolean),
  look: window.state.self?.look,
  hp: window.state.stats[9],
  stats: Object.entries(window.state.stats).slice(0, 12),
}));
console.log('装备后:', JSON.stringify(after));
await page.keyboard.press('c');
await page.waitForTimeout(800);
await page.screenshot({ path: path.join(outDir, 'equip-panels.png') });
await page.screenshot({ path: path.join(outDir, 'equip-avatar.png'), clip: { x: 440, y: 220, width: 400, height: 380 } });
await browser.close();
