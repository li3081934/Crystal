/** 检查某张图在场景里的实际位置，和地图里引用它的格子对照 */
import { chromium } from 'playwright';
const ids = (process.argv[2] ?? '8405,8403').split(',').map(Number);
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(1500);
await page.evaluate(() => window.chat('@LOGIN')); await page.waitForTimeout(400);
await page.evaluate(() => window.chat('C#Mir 4.0')); await page.waitForTimeout(400);
await page.waitForTimeout(4000);
const out = await page.evaluate((ids) => {
  const r = window.renderer;
  const cam = [Number.isFinite(r.builtCamX) ? r.builtCamX : r.camCellX, Number.isFinite(r.builtCamY) ? r.builtCamY : r.camCellY];
  const liveCam = [r.camCellX, r.camCellY];
  const res = [];
  for (const id of ids) {
    const sprites = r.worldLayer.children
      .filter((c) => c.__isTile && String(c.label).endsWith(`#${id}`))
      .map((c) => ({ x: Math.round(c.x), y: Math.round(c.y), w: Math.round(c.width), h: Math.round(c.height), v: c.visible, z: c.zIndex, blend: c.blendMode }));
    // 期望格子：y 必须满足 sy = y(格)*32 - camY*32 + 400，且 sy - h == sprite.y（非平铺）
    const cells = [];
    for (const s of sprites) {
      for (const anchoredBottom of [true, false]) {
        const sy = anchoredBottom ? s.y + s.h : s.y;
        const cellY = Math.round((sy - r.height / 2) / 32) + cam[1];
        const cellX = Math.round((s.x - r.width / 2) / 48) + cam[0];
        cells.push(`${cellX},${cellY}${anchoredBottom ? '(底对齐)' : '(顶对齐)'}`);
      }
    }
    res.push({ id, sprites, cells });
  }
  return { cam, liveCam, res };
}, ids);
console.log('built cam', out.cam, 'live cam', out.liveCam);
for (const r of out.res) console.log(JSON.stringify(r));
await browser.close();
