/** 给若干格子，列出"该格左上角区域"被哪些精灵覆盖（按 z 从下到上），用来判断墙面是否真被画出来 */
import { chromium } from 'playwright';
const cells = (process.argv[2] ?? '303,623;304,623;305,624;306,625').split(';').map((s) => s.split(',').map(Number));
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(1500);
await page.evaluate(() => window.chat('@LOGIN')); await page.waitForTimeout(400);
await page.evaluate(() => window.chat('C#Mir 4.0')); await page.waitForTimeout(400);
for (let i = 0; i < 14; i++) {
  const c = await page.evaluate(() => [window.renderer.camCellX, window.renderer.camCellY]);
  if (Math.abs(c[0] - 301) <= 1 && Math.abs(c[1] - 623) <= 1) break;
  await page.mouse.click(600 + Math.max(-380, Math.min(380, (301 - c[0]) * 48)), 400 + Math.max(-240, Math.min(240, (623 - c[1]) * 32)));
  await page.waitForTimeout(1800);
}
await page.waitForTimeout(1500);
const out = await page.evaluate((cells) => {
  const r = window.renderer;
  const bx = Number.isFinite(r.builtCamX) ? r.builtCamX : r.camCellX;
  const by = Number.isFinite(r.builtCamY) ? r.builtCamY : r.camCellY;
  const res = [];
  for (const [cx, cy] of cells) {
    const sx = (cx - bx) * 48 + r.width / 2 + 16;
    const sy = (cy - by) * 32 + r.height / 2 + 8;
    const wx = sx - r.worldLayer.x, wy = sy - r.worldLayer.y;
    const cover = [];
    for (const c of r.worldLayer.children) {
      if (!c.__isTile || !c.visible) continue;
      if (wx >= c.x && wx <= c.x + c.width && wy >= c.y && wy <= c.y + c.height) {
        cover.push({ z: c.zIndex, l: c.label.split('/').pop(), pos: [Math.round(c.x), Math.round(c.y)], size: [Math.round(c.width), Math.round(c.height)] });
      }
    }
    cover.sort((a, b) => a.z - b.z);
    res.push({ cell: `${cx},${cy}`, top: cover.slice(-3), n: cover.length });
  }
  return { cam: [bx, by], res };
}, cells);
console.log('built cam', out.cam);
for (const r of out.res) console.log(JSON.stringify(r));
await browser.close();
