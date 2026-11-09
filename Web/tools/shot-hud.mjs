#!/usr/bin/env node
/**
 * HUD 截图：进图 → 打开背包/装备 → 悬停物品出提示 → 截图
 *   node tools/shot-hud.mjs [account] [password]
 */
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const account = process.argv[2] ?? 'webdemo';
const password = process.argv[3] ?? 'webdemo123';

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1280, height: 800 }, deviceScaleFactor: 1 });
page.on('pageerror', (e) => console.log('PAGEERROR', e.message));
page.on('console', (m) => {
  if (m.type() === 'error') console.log('PAGE error', m.text());
});

await page.goto(`http://127.0.0.1:5188/?account=${account}&password=${password}`, { waitUntil: 'load' });
for (let i = 0; i < 60; i++) {
  if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break;
  await page.waitForTimeout(500);
}
await page.waitForTimeout(3500);

// 打开背包 + 装备
await page.keyboard.press('i');
await page.keyboard.press('c');
await page.waitForTimeout(1200);
await page.screenshot({ path: path.join(outDir, 'hud-panels.png') });

// 悬停第一件物品出 tooltip
const firstSlot = page.locator('#inventory-grid .slot').first();
const box = await firstSlot.boundingBox();
if (box) {
  await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
  await page.waitForTimeout(400);
  await page.screenshot({ path: path.join(outDir, 'hud-tooltip.png') });
}

// 场景区中央近景（看装备外观）
await page.screenshot({ path: path.join(outDir, 'hud-avatar.png'), clip: { x: 440, y: 200, width: 400, height: 400 } });

const info = await page.evaluate(() => {
  const s = window.state;
  return {
    equipment: s.equipment.filter(Boolean).map((i) => `${i.info?.name ?? i.itemIndex} d=${i.currentDura}/${i.maxDura} shape=${i.info?.shape}`),
    inventory: s.inventory.filter(Boolean).map((i) => i.info?.name ?? String(i.itemIndex)),
    stats: s.stats,
    look: s.self?.look,
    maxHp: s.stats[12] !== undefined,
  };
});
console.log('装备:', info.equipment);
console.log('背包:', info.inventory);
console.log('外观:', info.look);
console.log('属性 HP/MP:', info.stats[12], '/', info.stats[13]); // Stat.HP=12, Stat.MP=13
await browser.close();
