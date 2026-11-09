import { chromium } from 'playwright';
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) { if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break; await page.waitForTimeout(500); }
await page.waitForTimeout(2000);
await page.evaluate(() => window.chat('@LOGIN')); await page.waitForTimeout(400);
await page.evaluate(() => window.chat('C#Mir 4.0')); await page.waitForTimeout(3000);
const out = await page.evaluate(() => {
  const m = window.renderer.map;
  const rows = [];
  for (let y = 624; y <= 627; y++) {
    const r = [];
    for (let x = 303; x <= 308; x++) {
      const c = m.at(x, y);
      r.push(`f${c.frontImage === -32768 ? 0 : c.frontImage & 0x7fff}`);
    }
    rows.push(`y${y}: ${r.join(' ')}`);
  }
  return { size: [m.width, m.height], rows };
});
console.log(JSON.stringify(out, null, 1));
await browser.close();
