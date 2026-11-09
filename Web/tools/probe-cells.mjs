/** 打印指定区域内每格的中景/前景瓦片：map 数据 + 实际渲染的精灵 */
import { chromium } from 'playwright';
const [x0, x1, y0, y1] = (process.argv[2] ?? '301,306,616,624').split(',').map(Number);
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(1200);
await page.evaluate(() => window.chat('@LOGIN'));
await page.waitForTimeout(400);
await page.evaluate(() => window.chat('C#Mir 4.0'));
await page.waitForTimeout(400);
await page.evaluate((s) => window.chat(`@MOVE ${s}`), `${(x0 + x1) >> 1} ${(y0 + y1) >> 1}`);
await page.waitForTimeout(3000);
const out = await page.evaluate(([x0, x1, y0, y1]) => {
  const r = window.renderer;
  const map = r.map;
  const rows = [];
  for (let y = y0; y <= y1; y++) {
    for (let x = x0; x <= x1; x++) {
      const c = map.at(x, y);
      if (!c) continue;
      const sx = (x - r.camCellX) * 48 + r.width / 2;
      const sy = (y - r.camCellY) * 32 + r.height / 2;
      const near = r.worldLayer.children.filter((s) => s.__isTile && Math.abs(s.x - sx) < 30 && Math.abs(s.y - sy) < 90)
        .map((s) => `${s.label.split('/').pop()}|${s.visible ? 'vis' : 'HID'}|${Math.round(s.x)},${Math.round(s.y)}|${s.zIndex}`);
      const hid = near.filter((t) => t.includes('HID'));
      rows.push({
        hid,
        cell: `${x},${y}`,
        back: c.backImage ? `${c.backIndex}:${c.backImage & 0x1fffffff}` : '-',
        mid: c.midImage > 0 ? `${c.midIndex}:${c.midImage}${c.midAnimFrame ? `(anim${c.midAnimFrame})` : ''}` : '-',
        front: c.frontImage !== -32768 && c.frontImage ? `${c.frontIndex}:${c.frontImage & 0x7fff}${c.frontAnimFrame ? `(anim${c.frontAnimFrame})` : ''}${c.doorIndex ? `(door${c.doorIndex})` : ''}` : '-',
        sprites: near,
      });
    }
  }
  return { cam: [r.camCellX, r.camCellY], rows };
}, [x0, x1, y0, y1]);
console.log('cam', out.cam);
for (const r of out.rows) console.log(JSON.stringify(r));
await browser.close();
