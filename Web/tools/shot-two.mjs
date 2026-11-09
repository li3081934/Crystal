import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const A = { account: process.argv[2] ?? 'webdemo', password: process.argv[3] ?? 'webdemo123' };
const B = { account: process.argv[4] ?? 'webtest01', password: process.argv[5] ?? 'webtest01' };
const browser = await chromium.launch();

async function login({ account, password }) {
  const ctx = await browser.newContext({ viewport: { width: 1024, height: 768 } });
  const page = await ctx.newPage();
  page.on('pageerror', (e) => console.log('PAGEERROR', account, e.message));
  await page.goto(`http://127.0.0.1:5188/?account=${account}&password=${password}`, { waitUntil: 'load' });
  for (let i = 0; i < 80; i++) {
    if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break;
    await page.waitForTimeout(500);
  }
  await page.waitForTimeout(2500);
  return page;
}

const a = await login(A);
// 服务端每次连接都会把同 IP 封 IPBlockSeconds（默认 5s）防洪水，所以第二个客户端要等一会儿
await new Promise((r) => setTimeout(r, 6500));
const b = await login(B);
await b.keyboard.press('ArrowRight');
await b.waitForTimeout(700);
await b.keyboard.press('ArrowDown');
await b.waitForTimeout(1200);
await a.waitForTimeout(1000);

const dump = async (p) => p.evaluate(() => ({
  self: [window.state.self?.name, window.state.self?.cellX, window.state.self?.cellY],
  players: [...window.state.actors.values()].filter((x) => x.kind === 'player').map((x) => `${x.name}@${x.cellX},${x.cellY} w=${x.look.weapon} a=${x.look.armour}`),
  status: document.querySelector('#status')?.textContent,
}));
console.log('A:', JSON.stringify(await dump(a)));
console.log('B:', JSON.stringify(await dump(b)));
await a.screenshot({ path: path.join(outDir, 'two-players.png') });
await browser.close();
