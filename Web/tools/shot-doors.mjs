/**
 * 动画瓦片 + 门验证
 *   1) 前景动画（比奇城的水面/喷泉，Objects.Lib#2723 共 10 帧）：图号随时间变化
 *   2) 门：发 C.Opendoor → 服务端回 S.Opendoor → 门状态机 关→开→自动关，且瓦片图号按 doorOffset 偏移
 *   node tools/shot-doors.mjs [account] [password]
 */
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const account = process.argv[2] ?? 'webdemo';
const password = process.argv[3] ?? 'webdemo123';

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1100, height: 800 } });
page.on('pageerror', (e) => console.log('PAGEERROR', e.message));

async function login() {
  for (let attempt = 0; attempt < 4; attempt++) {
    await page.goto(`http://127.0.0.1:5188/?account=${account}&password=${password}`, { waitUntil: 'load' });
    for (let i = 0; i < 40; i++) {
      if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) {
        await page.waitForTimeout(2500);
        if (await page.evaluate(() => !!window.state?.self)) return true;
      }
      await page.waitForTimeout(500);
    }
    console.log('  登录失败，6s 后重试（同 IP 连接有 5s 封禁）…');
    await page.waitForTimeout(6000);
  }
  return false;
}

if (!(await login())) {
  console.log('登录失败，退出');
  await browser.close();
  process.exit(1);
}

const chat = async (text) => {
  await page.evaluate((t) => window.chat(t), text);
  await page.waitForTimeout(350);
};
await chat('@LOGIN');
await chat('C#Mir 4.0');
// 传送到比奇城喷泉旁（门和水面都在这一带）；服务端 ValidPoint 可能拒绝，多试几个点
const spots = [[293, 624], [286, 616], [300, 631], [292, 630], [287, 620]];
let arrived = false;
for (const [x, y] of spots) {
  const beforePos = await page.evaluate(() => window.state.self && [window.state.self.cellX, window.state.self.cellY]);
  await chat(`@MOVE ${x} ${y}`);
  await page.waitForTimeout(2200);
  const pos = await page.evaluate(() => window.state.self && [window.state.self.cellX, window.state.self.cellY]);
  if (pos && ((pos[0] !== beforePos[0] || pos[1] !== beforePos[1]) || (pos[0] === x && pos[1] === y))) {
    console.log(`传送到 (${x},${y}) 成功，当前:`, JSON.stringify(pos));
    arrived = true;
    break;
  }
}
if (!arrived) {
  console.log('传送失败，看看服务端日志:', JSON.stringify(await page.evaluate(() => [...document.querySelectorAll('#log div')].map((d) => d.textContent).slice(-5))));
}
console.log('当前位置:', JSON.stringify(await page.evaluate(() => window.state.self && [window.state.self.cellX, window.state.self.cellY])));

// ---------------- 1) 前景动画瓦片 ----------------
const anim = await page.evaluate(() => {
  const tiles = window.renderer.animTiles.filter((t) => t.frames > 0);
  return {
    total: window.renderer.animTiles.length,
    withFrames: tiles.length,
    sample: tiles.slice(0, 3).map((t) => ({ label: t.sprite.label, frames: t.frames, last: t.lastIndex })),
  };
});
console.log('动画瓦片:', JSON.stringify(anim));

const before = await page.evaluate(() => window.renderer.animTiles.filter((t) => t.frames > 0).map((t) => t.lastIndex));
await page.waitForTimeout(700);
const after = await page.evaluate(() => window.renderer.animTiles.filter((t) => t.frames > 0).map((t) => t.lastIndex));
console.log('图号变化:', before.some((v, i) => v !== after[i]), JSON.stringify(before), '→', JSON.stringify(after));
await page.screenshot({ path: path.join(outDir, 'tile-anim.png'), clip: { x: 380, y: 220, width: 340, height: 280 } });

// ---------------- 2) 门 ----------------
const doorInfo = await page.evaluate(() => {
  const tile = window.renderer.animTiles.find((t) => t.doorIndex > 0 && t.sprite.visible);
  if (!tile) return null;
  return { doorIndex: tile.doorIndex, base: tile.base, last: tile.lastIndex, doorOffset: tile.doorOffset, label: tile.sprite.label };
});
console.log('门的瓦片:', JSON.stringify(doorInfo));

if (doorInfo) {
  const idx = doorInfo.doorIndex;
  const shots = [];
  const snap = async (tag) => {
    const st = await page.evaluate((d) => ({
      state: window.renderer.doorState(d),
      tiles: window.renderer.animTiles.filter((t) => t.doorIndex === d).map((t) => t.lastIndex),
    }), idx);
    shots.push({ tag, ...st });
    return st;
  };
  await snap('初始');
  await page.evaluate((d) => window.state.socket.send(window.send.openDoor(d)), idx);
  await page.waitForTimeout(130);
  await snap('敲门 130ms');
  await page.waitForTimeout(420);
  await snap('敲门 550ms');
  const clip = await page.evaluate((d) => {
    const tile = window.renderer.animTiles.find((t) => t.doorIndex === d && t.sprite.visible);
    if (!tile) return null;
    const b = tile.sprite.getBounds();
    const VW = window.renderer.width;
    const VH = window.renderer.height;
    const x = Math.max(0, Math.min(VW - 40, Math.round(b.x) - 60));
    const y = Math.max(0, Math.min(VH - 40, Math.round(b.y) - 40));
    return {
      x,
      y,
      width: Math.max(40, Math.min(360, VW - x, Math.round(b.width) + 120)),
      height: Math.max(40, Math.min(300, VH - y, Math.round(b.height) + 80)),
    };
  }, idx);
  await page.screenshot({ path: path.join(outDir, 'door-open.png'), ...(clip ? { clip } : {}) });
  await page.waitForTimeout(6200);
  await snap('6.8s 后（自动关）');

  for (const s of shots) console.log('  ', s.tag, 'state=' + s.state, 'tiles=' + JSON.stringify(s.tiles));
  const opened = shots.some((s) => s.state === 2);
  const autoClosed = shots[shots.length - 1].state === 0 || shots[shots.length - 1].state === 3;
  const indexShifted = shots.some((s) => s.tiles.join(',') !== shots[0].tiles.join(','));
  console.log('开门:', opened, '| 图号随门变化:', indexShifted, '| 自动关门:', autoClosed);
}

await browser.close();
