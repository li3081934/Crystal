/** 审计：视野内每个格子的左上角，是否被一张"地面瓦片"盖住（没盖住就会露出兜底平铺色） */
import { chromium } from 'playwright';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(1500);
await page.evaluate(() => window.chat('@LOGIN')); await page.waitForTimeout(400);
await page.evaluate(() => window.chat('C#Mir 4.0')); await page.waitForTimeout(400);
for (let i = 0; i < 10; i++) {
  const cam = await page.evaluate(() => [window.renderer.camCellX, window.renderer.camCellY]);
  if (cam[0] >= 300) break;
  await page.mouse.click(1120, 400);
  await page.waitForTimeout(2500);
}
await page.waitForTimeout(2000);
const out = await page.evaluate(() => {
  const r = window.renderer;
  const map = r.map;
  const ground = r.worldLayer.children.filter((c) => c.__isTile && c.visible && c.zIndex < -500000);
  const rects = ground.map((c) => ({ x: c.x, y: c.y, w: c.width, h: c.height, l: c.label.split('/').pop() }));
  const holes = [];
  for (let y = r.camCellY - 10; y <= r.camCellY + 12; y++) {
    for (let x = r.camCellX - 14; x <= r.camCellX + 16; x++) {
      if (!map.at(x, y)) continue;
      const px = (x - r.camCellX) * 48 + r.width / 2 + 4;
      const py = (y - r.camCellY) * 32 + r.height / 2 + 4;
      if (px < 0 || py < 0 || px > r.width || py > r.height) continue;
      const cover = rects.some((t) => px >= t.x && px <= t.x + t.w && py >= t.y && py <= t.y + t.h);
      if (!cover) holes.push(`${x},${y}`);
    }
  }
  return { cam: [r.camCellX, r.camCellY], ground: rects.length, holes: holes.length, sample: holes.slice(0, 25) };
});
console.log(JSON.stringify(out));
await browser.close();
