/** 列出视野内所有"影子/光照"（blend）瓦片：位置、尺寸、图片自带偏移 */
import { chromium } from 'playwright';
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
const out = await page.evaluate(() => {
  const r = window.renderer;
  const bx = Number.isFinite(r.builtCamX) ? r.builtCamX : r.camCellX;
  const by = Number.isFinite(r.builtCamY) ? r.builtCamY : r.camCellY;
  const list = [];
  for (const c of r.worldLayer.children) {
    if (!c.__isTile || !c.visible || c.blendMode !== 'add') continue;
    const h = c.height, w = c.width;
    list.push({
      l: c.label.split('/').pop(),
      pos: [Math.round(c.x), Math.round(c.y)],
      size: [Math.round(w), Math.round(h)],
      // 反推格子（底对齐假设）
      cellBottom: [Math.round((c.x - r.width / 2) / 48) + bx, Math.round((c.y + h - r.height / 2) / 32) + by],
      cellTop: [Math.round((c.x - r.width / 2) / 48) + bx, Math.round((c.y - r.height / 2) / 32) + by],
    });
  }
  return { cam: [bx, by], list };
});
console.log('cam', out.cam, 'blend 瓦片数', out.list.length);
for (const t of out.list.slice(0, 14)) console.log(JSON.stringify(t));
await browser.close();
