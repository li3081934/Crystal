#!/usr/bin/env node
/**
 * 浏览器截图探针：自动登录、进图、点地走路，各阶段截图。
 *   node tools/shot.mjs [account] [password]
 * 需要先启动 Server.exe / bridge / vite dev。
 */
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const out = path.join(__dirname, '..', 'shots');
const account = process.argv[2] ?? 'webdemo';
const password = process.argv[3] ?? 'webdemo123';

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1024, height: 768 }, deviceScaleFactor: 1 });
const errors = [];
page.on('console', (m) => {
  const t = m.text();
  if (m.type() === 'error' || t.includes('[lib]') || t.includes('warn')) console.log('PAGE', m.type(), t);
});
page.on('pageerror', (e) => {
  errors.push(e.message);
  console.log('PAGEERROR', e.message);
});

await page.goto(`http://127.0.0.1:5188/?account=${account}&password=${password}`, { waitUntil: 'load' });
await page.waitForTimeout(2500);
await page.screenshot({ path: path.join(out, '01-login.png') });

// 等进图
let entered = false;
for (let i = 0; i < 40; i++) {
  const hidden = await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'));
  if (hidden) {
    entered = true;
    break;
  }
  await page.waitForTimeout(500);
}
console.log('entered map:', entered);
await page.waitForTimeout(4000); // 等图库解码
await page.screenshot({ path: path.join(out, '02-map.png') });

// 点几个位置走路
for (const [x, y] of [
  [620, 420],
  [520, 500],
  [720, 520],
]) {
  await page.mouse.click(x, y);
  await page.waitForTimeout(1200);
}
await page.screenshot({ path: path.join(out, '03-walk.png') });

// 聊天
await page.click('#chat-input');
await page.keyboard.type('hello from browser');
await page.keyboard.press('Enter');
await page.waitForTimeout(1500);
await page.screenshot({ path: path.join(out, '04-chat.png') });

const info = await page.evaluate(() => ({
  hud: document.querySelector('#hud-info')?.textContent,
  log: [...document.querySelectorAll('#log div')].map((d) => d.textContent).slice(-12),
  sprites: {
    tiles: document.querySelectorAll('#stage canvas').length,
  },
}));
console.log('HUD:', info.hud);
console.log('LOG:', info.log?.join('\n     '));
console.log('pageerrors:', errors.length ? errors : 'none');

await browser.close();
