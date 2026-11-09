/**
 * 相机跟随 + 缺地砖验证
 *   - 走路时角色屏幕坐标基本不动，worldLayer 亚格偏移连续变化（背景平滑滚动）
 *   - 兜底平铺地砖生效后，屏幕上不应再有"纯色补丁块"
 * 用法: node tools/diag-camera.mjs
 */
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 800 } });
page.on('pageerror', (e) => console.log('PAGEERROR', e.message));
await page.goto('http://127.0.0.1:5188/?account=webdemo&password=webdemo123', { waitUntil: 'load' });
for (let i = 0; i < 40; i++) {
  if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break;
  await page.waitForTimeout(500);
}
await page.waitForTimeout(2500);
const chat = async (t) => { await page.evaluate((x) => window.chat(x), t); await page.waitForTimeout(400); };
await chat('@LOGIN'); await chat('C#Mir 4.0'); await chat('@MOVE 300 615');
await page.waitForTimeout(3500);

const sample = () => page.evaluate(() => {
  const r = window.renderer;
  const self = window.state.self;
  const e = self ? r.actorSprites.get(self.id) : null;
  return {
    cam: [+r.camFloatX.toFixed(2), +r.camFloatY.toFixed(2)],
    camCell: [r.camCellX, r.camCellY],
    layer: [Math.round(r.worldLayer.x), Math.round(r.worldLayer.y)],
    selfScreen: e ? [Math.round(e.body.x), Math.round(e.body.y)] : null,
    moving: self ? self.isMoving : null,
    ground: r.groundColor.toString(16),
    tiled: !!r.groundTile?.visible,
  };
});

console.log('静止:', JSON.stringify(await sample()));
// 点地面走（和真人一样），找到一个能走的落点
const vp = page.viewportSize();
let clicked = null;
for (const [ox, oy] of [[240, 40], [-240, 40], [40, 200], [-40, -200], [120, 120], [-120, -120]]) {
  await page.mouse.click(vp.width / 2 + ox, vp.height / 2 + oy);
  await page.waitForTimeout(150);
  if (await page.evaluate(() => window.state.self?.isMoving)) { clicked = [ox, oy]; break; }
}
console.log('点到的偏移 =', clicked);
for (let i = 0; i < 5; i++) {
  await page.waitForTimeout(110);
  console.log(`+${(i + 1) * 110}ms`, JSON.stringify(await sample()));
  if (i === 2) await page.screenshot({ path: path.join(outDir, 'camera-midstep.png') });
}
await page.waitForTimeout(800);
console.log('停下:', JSON.stringify(await sample()));
await browser.close();
