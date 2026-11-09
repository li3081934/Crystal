/** 查询屏幕上某些点"到底被哪些精灵覆盖"（按 z 从下到上） */
import { chromium } from 'playwright';
const pts = (process.argv[2] ?? '650,470;700,520;660,600').split(';').map((s) => s.split(',').map(Number));
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(1500);
await page.evaluate(() => window.chat('@LOGIN'));
await page.waitForTimeout(400);
await page.evaluate(() => window.chat('C#Mir 4.0'));
await page.waitForTimeout(400);
await page.evaluate(() => window.chat('@MOVE 301 623'));
await page.waitForTimeout(3500);
const out = await page.evaluate((points) => {
  const r = window.renderer;
  const res = [];
  for (const [px, py] of points) {
    const wx = px - r.worldLayer.x, wy = py - r.worldLayer.y;
    const hits = [];
    for (const c of r.worldLayer.children) {
      if (!c.__isTile || !c.visible) continue;
      const w = c.width, h = c.height;
      if (wx >= c.x && wx <= c.x + w && wy >= c.y && wy <= c.y + h) {
        hits.push({ z: c.zIndex, label: c.label.split('/').pop(), pos: [Math.round(c.x), Math.round(c.y)], size: [Math.round(w), Math.round(h)] });
      }
    }
    hits.sort((a, b) => a.z - b.z);
    // 反推格子
    const bx = Number.isFinite(r.builtCamX) ? r.builtCamX : r.camCellX;
    const by = Number.isFinite(r.builtCamY) ? r.builtCamY : r.camCellY;
    const cellX = Math.floor((wx - r.width / 2) / 48) + bx;
    const cellY = Math.floor((wy - r.height / 2) / 32) + by;
    res.push({ point: [px, py], cell: [cellX, cellY], cover: hits.slice(-6) });
  }
  return { cam: [r.camCellX, r.camCellY], built: [r.builtCamX, r.builtCamY], res };
}, pts);
console.log('cam', out.cam, 'built', out.built);
for (const r of out.res) console.log(JSON.stringify(r));
await browser.close();
