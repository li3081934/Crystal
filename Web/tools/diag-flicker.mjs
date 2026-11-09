/**
 * 走动时贴图闪烁检查
 *
 * 原理：屏幕中间区域（不该有"还没加载"的瓦片）里，如果某一格上一帧有瓦片、这一帧没了，
 * 就是一次"闪烁"（贴图被回收/被角色精灵顶掉/重建后没补上都会表现为这个）。
 *
 * 用法: node tools/diag-flicker.mjs
 */
import { chromium } from 'playwright';

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
await page.waitForTimeout(3000);

const probe = () => page.evaluate(() => {
  const r = window.renderer;
  const cx = r.width / 2, cy = r.height / 2;
  const keys = new Set();
  let broken = 0;
  for (const c of r.worldLayer.children) {
    if (!c.__isTile || !c.visible) continue;
    // 只看镜头正中间 ±2 格（边缘会因为镜头滚动/预取而正常进出）
    const row = Math.floor(c.zIndex / 10);
    // 用"建这批瓦片时用的相机"换算，避免拿新相机去量旧瓦片（那是测量假象）
    const bx = Number.isFinite(r.builtCamX) ? r.builtCamX : r.camCellX;
    const by = Number.isFinite(r.builtCamY) ? r.builtCamY : r.camCellY;
    const cellX = Math.round((c.x - r.width / 2) / 48) + bx;
    if (Math.abs(cellX - bx) > 2 || Math.abs(row - by) > 2) continue;
    keys.add(`${row}|${cellX}`);
    const t = c.texture;
    if (t && (t.destroyed || t.source?.destroyed || (t.width === 1 && t.height === 1))) broken++;
  }
  return { keys: [...keys], broken, cache: r.textures.size, pending: r.pendingTiles.length, miss: r.missLog.slice(-6) };
});

const detail = (keys) => page.evaluate((ks) => {
  const r = window.renderer;
  const cellOf = (c) => {
    const row = Math.floor(c.zIndex / 10);
    const cellX = Math.round((c.x - r.width / 2) / 48) + r.camCellX;
    return `${row}|${cellX}`;
  };
  const out = [];
  for (const k of ks.slice(0, 4)) {
    const sprites = r.worldLayer.children.filter((c) => c.__isTile && cellOf(c) === k)
      .map((c) => ({ l: c.label, v: c.visible, w: Math.round(c.width), h: Math.round(c.height), z: c.zIndex }));
    out.push({ key: k, sprites });
  }
  return out;
}, keys);

let prev = new Set();
let blinks = 0, frames = 0, maxBroken = 0, maxPending = 0;
const vp = page.viewportSize();
for (let step = 0; step < 10; step++) {
  await page.mouse.click(vp.width / 2 + 240, vp.height / 2 + 40);
  for (let i = 0; i < 5; i++) {
    await page.waitForTimeout(90);
    const p = await probe();
    const cur = new Set(p.keys);
    // 上一帧在、这一帧不在的格子数
    let gone = 0;
    for (const k of prev) if (!cur.has(k)) gone++;
    if (prev.size) blinks += gone;
    prev = cur;
    frames++;
    maxBroken = Math.max(maxBroken, p.broken);
    maxPending = Math.max(maxPending, p.pending);
    if (gone > 0) {
      const lost = [...prev].filter((k) => !cur.has(k));
      console.log('  LOST', JSON.stringify(await detail(lost)));
    }
    if (gone > 0) console.log(`step${step}+${i} gone=${gone} broken=${p.broken} pending=${p.pending} miss=${JSON.stringify(p.miss)}`);
  }
}
console.log(`帧数 ${frames}｜闪烁格次 ${blinks}｜纹理坏了最多 ${maxBroken}｜pending 最多 ${maxPending}`);
await browser.close();
