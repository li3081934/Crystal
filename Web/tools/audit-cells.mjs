/**
 * 审计：地图每格引用的图，是否真的有一张精灵摆在"该在的位置"上。
 * 位置算法和 renderer.placeTile 一致：平铺尺寸 左上角，其余 底部对齐。
 * 用法: node tools/audit-cells.mjs [x0,x1,y0,y1]
 */
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

const out = await page.evaluate(async () => {
  const r = window.renderer;
  const map = r.map;
  const LIB = ['Tiles', 'SmTiles', 'Objects', 'Objects2', 'Objects3', 'Objects4', 'Objects5', 'Objects6', 'Objects7', 'Objects8', 'Objects9', 'Objects10', 'Objects11', 'Objects12', 'Objects13', 'Objects14', 'Objects15', 'Objects16', 'Objects17', 'Objects18', 'Objects19', 'Objects20', 'Objects21', 'Objects22', 'Objects23', 'Objects24', 'Objects25', 'Objects26', 'Objects27', 'Objects28', 'Objects29'];
  // 收集所有瓦片精灵，按 label 建索引
  const byLabel = new Map();
  for (const c of r.worldLayer.children) {
    if (!c.__isTile) continue;
    const arr = byLabel.get(c.label) ?? [];
    arr.push({ x: Math.round(c.x), y: Math.round(c.y), v: c.visible, h: Math.round(c.height) });
    byLabel.set(c.label, arr);
  }
  const problems = [];
  const cx0 = r.camCellX - 8, cx1 = r.camCellX + 10;
  const cy0 = r.camCellY - 6, cy1 = r.camCellY + 8;
  for (let y = cy0; y <= cy1; y++) {
    for (let x = cx0; x <= cx1; x++) {
      const c = map.at(x, y);
      if (!c) continue;
      const sx = (x - r.camCellX) * 48 + r.width / 2;
      const sy = (y - r.camCellY) * 32 + r.height / 2;
      const checks = [];
      if (y % 2 === 0 && x % 2 === 0 && c.backImage !== 0 && c.backIndex !== -1) checks.push(['back', c.backIndex, (c.backImage & 0x1fffffff) - 1]);
      if (c.midIndex !== -1 && c.midImage > 0) checks.push(['mid', c.midIndex, c.midImage - 1]);
      if (c.frontIndex !== -1 && c.frontIndex !== 200 && c.frontImage !== -32768) checks.push(['front', c.frontIndex, (c.frontImage & 0x7fff) - 1]);
      for (const [kind, li, ii] of checks) {
        if (ii < 0 || li < 0) continue;
        const name = LIB[li] ?? `Lib${li}`;
        const label = `Data/Map/WemadeMir2/${name}.Lib#${ii}`;
        const hits = byLabel.get(label) ?? [];
        // 期望：x 必须等于 sx；y 等于 sy（平铺）或 sy+32-高（非平铺：DrawObjects 把底边对齐到格子底边）
        // 例外：2723~2732（路灯）原版 DrawBlend 会再叠加图片自带的 (mi.X, mi.Y)，位置因图而异，只校验“在”
        const lamp = kind === 'front' && ii >= 2723 && ii <= 2732;
        const ok = lamp
          ? hits.some((h) => h.v)
          : hits.some((h) => h.v && Math.abs(h.x - sx) <= 1 && (Math.abs(h.y - sy) <= 1 || Math.abs(h.y - (sy + 32 - h.h)) <= 1));
        if (!ok) problems.push({ cell: `${x},${y}`, kind, what: `${name}#${ii}`, hits: hits.slice(0, 3) });
      }
    }
  }
  return { cam: [r.camCellX, r.camCellY], n: problems.length, sample: problems.slice(0, 14) };
});
console.log('cam', out.cam, '问题数', out.n);
for (const p of out.sample) console.log(JSON.stringify(p));
await browser.close();
