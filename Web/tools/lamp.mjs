/** 检查某盏路灯：柱子(非 blend) 和 光晕(#2728 blend) 的位置关系 */
import { chromium } from 'playwright';
const [cx, cy] = (process.argv[2] ?? '293,623').split(',').map(Number);
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(1500);
await page.evaluate(() => window.chat('@LOGIN')); await page.waitForTimeout(400);
await page.evaluate(() => window.chat('C#Mir 4.0')); await page.waitForTimeout(400);
for (let i = 0; i < 14; i++) {
  const c = await page.evaluate(() => [window.renderer.camCellX, window.renderer.camCellY]);
  if (Math.abs(c[0] - cx) <= 2 && Math.abs(c[1] - cy) <= 2) break;
  await page.mouse.click(600 + Math.max(-380, Math.min(380, (cx - c[0]) * 48)), 400 + Math.max(-240, Math.min(240, (cy - c[1]) * 32)));
  await page.waitForTimeout(1800);
}
await page.waitForTimeout(1200);
const out = await page.evaluate(([cx, cy]) => {
  const r = window.renderer;
  const bx = Number.isFinite(r.builtCamX) ? r.builtCamX : r.camCellX;
  const by = Number.isFinite(r.builtCamY) ? r.builtCamY : r.camCellY;
  const sx = (cx - bx) * 48 + r.width / 2;
  const res = [];
  for (const c of r.worldLayer.children) {
    if (!c.__isTile || !c.visible) continue;
    if (Math.abs(c.x - sx) > 48) continue;
    const cellY = Math.round((c.y + c.height - r.height / 2) / 32) + by; // 底对齐反推
    res.push({ l: c.label.split('/').pop(), pos: [Math.round(c.x), Math.round(c.y)], size: [Math.round(c.width), Math.round(c.height)], z: c.zIndex, blend: c.blendMode, cellYBottom: cellY });
  }
  // 也打印这些图的 meta 自带偏移
  return { cam: [bx, by], sx, res };
}, [cx, cy]);
console.log('cam', out.cam, 'sx', out.sx);
for (const t of out.res) console.log(JSON.stringify(t));
await browser.close();
