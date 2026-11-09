/**
 * 战斗验证：GM 登录 → 传送到野外 → 升级 → 刷一只鸡 → 点它 → 追 → 砍 → 击杀
 * 采集：攻击动作、伤害/闪避飘字、怪物血条、死亡、掉落/经验
 *   node tools/shot-combat.mjs [account] [password]
 */
import { chromium } from 'playwright';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const outDir = path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'shots');
const account = process.argv[2] ?? 'webdemo';
const password = process.argv[3] ?? 'webdemo123';
const GM_PASSWORD = 'C#Mir 4.0';

const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1200, height: 860 } });
page.on('pageerror', (e) => console.log('PAGEERROR', e.message));

const chat = async (text) => {
  await page.click('#chat-input');
  await page.fill('#chat-input', text);
  await page.keyboard.press('Enter');
  await page.waitForTimeout(250);
};

await page.goto(`http://127.0.0.1:5188/?account=${account}&password=${password}`, { waitUntil: 'load' });
for (let i = 0; i < 60; i++) {
  if (await page.evaluate(() => document.querySelector('#overlay')?.classList.contains('hidden'))) break;
  await page.waitForTimeout(500);
}
await page.waitForTimeout(2500);

await chat('@LOGIN');
await chat(GM_PASSWORD);
await chat('@MOVE 300 615'); // 城堡外开阔地，鸡刷在身边、不会被房子挡住
await page.waitForTimeout(2000);
await chat('@LEVEL 22');
// 把剑穿上（准确 +命中，不然 Lv1 的 5 点准确几乎打不中）
await page.evaluate(() => {
  const s = window.state;
  const idx = s.inventory.findIndex((i) => i?.info?.name === 'WoodenSword');
  if (idx >= 0) s.socket.send(window.send.equipItem(1, s.inventory[idx].uniqueId, 0));
});
await page.waitForTimeout(600);
await chat('@MOB Hen');
await chat('@MOB Hen');
// 等镜头真的落到自己身上（传送后相机要追一下，不等的话怪全在屏幕外）
for (let i = 0; i < 40; i++) {
  const ok = await page.evaluate(() => {
    const r = window.renderer, s = window.state.self;
    return !!s && Math.abs(r.camCellX - s.cellX) <= 1 && Math.abs(r.camCellY - s.cellY) <= 1;
  });
  if (ok) break;
  await page.waitForTimeout(250);
}
await page.waitForTimeout(1500);

const target = await page.evaluate(() => {
  const s = window.state, r = window.renderer;
  const self = s.self;
  const mobs = [...s.actors.values()]
    .filter((a) => a.kind === 'monster' && !a.dead && a.name === 'Hen')
    .sort((a, b) => Math.hypot(a.cellX - self.cellX, a.cellY - self.cellY) - Math.hypot(b.cellX - self.cellX, b.cellY - self.cellY));
  const m = mobs.find((a) => {
    const sp = r.toScreen(a.cellX, a.cellY);
    return sp.x > 60 && sp.x < r.width - 60 && sp.y > 120 && sp.y < r.height - 120;
  });
  if (!m) return null;
  const sp = r.toScreen(m.cellX, m.cellY);
  return { id: m.id, name: m.name, cell: [m.cellX, m.cellY], screen: [Math.round(sp.x), Math.round(sp.y)] };
});
console.log('靶子:', JSON.stringify(target));
if (!target) {
  console.log('没找到靶子，附近的怪：', JSON.stringify(await page.evaluate(() => {
    const s = window.state, r = window.renderer;
    return [...s.actors.values()].filter((a) => a.kind === 'monster')
      .map((m) => ({ name: m.name, dead: m.dead, cell: [m.cellX, m.cellY], screen: [Math.round(r.toScreen(m.cellX, m.cellY).x), Math.round(r.toScreen(m.cellX, m.cellY).y)], wh: [r.width, r.height] }));
  })));
  await browser.close();
  process.exit(0);
}

await page.mouse.click(target.screen[0], target.screen[1] - 10);
await page.waitForTimeout(200);

let shotHit = false;
let sawFloat = new Set();
let hpSeen = [];
let killed = false;
let hadTarget = false;
let diedPackets = 0;

// 统计战斗相关包：攻击次数 / 命中 / 闪避 / 击杀
await page.evaluate(() => {
  const s = window.state.socket;
  window.__c = { atk: 0, hit: 0, miss: 0, died: 0, hp: [], sent: {} };
  const sendOrig = s.send.bind(s);
  s.send = (b) => {
    try {
      const id = new DataView(b.buffer, b.byteOffset ?? 0).getInt16(2, true);
      window.__c.sent[id] = (window.__c.sent[id] ?? 0) + 1;
      if (id === 47) window.__c.atk++;
    } catch { /* ignore */ }
    return sendOrig(b);
  };
  const orig = s.onPacket;
  s.onPacket = (p) => {
    if (p.name === 'ObjectDied') window.__c.died++;
    else if (p.name === 'DamageIndicator') { if (p.data.damageType === 1) window.__c.miss++; else window.__c.hit++; }
    else if (p.name === 'ObjectHealth') window.__c.hp.push(p.data.percent);
    orig(p);
  };
});

for (let i = 0; i < 260; i++) {
  await page.waitForTimeout(150);
  // 每 1.5s 重新点一次目标：怪会跑，不重新点就够不着
  if (i % 10 === 0) {
    const sp = await page.evaluate((id) => {
      const a = window.state.actors.get(id);
      if (!a || a.dead) return null;
      const e = window.renderer.actorSprites.get(id);
      if (!e) return null;
      const r = window.renderer;
      return e.body.x > 40 && e.body.x < r.width - 40 && e.body.y > 100 && e.body.y < r.height - 100
        ? [Math.round(e.body.x), Math.round(e.body.y) - 10] : null;
    }, target.id);
    if (sp) await page.mouse.click(sp[0], sp[1]);
  }
  const st = await page.evaluate(() => {
    const s = window.state;
    const t = s.targetId === null ? null : s.actors.get(s.targetId);
    return {
      targetId: s.targetId,
      dead: t?.dead ?? null,
      hp: t?.hpPercent ?? null,
      floats: window.renderer.floats.map((f) => f.text),
      action: s.self?.action,
      exp: s.exp,
      level: s.level,
      invCount: s.inventory.filter(Boolean).length,
      logs: [...document.querySelectorAll('#log div')].map((d) => d.textContent).slice(-4),
    };
  });
  for (const f of st.floats) sawFloat.add(f);
  if (st.hp !== null && !hpSeen.includes(st.hp)) hpSeen.push(st.hp);
  if (!shotHit && st.hp !== null && st.hp > 0 && st.hp < 100) {
    shotHit = true;
    await page.screenshot({ path: path.join(outDir, 'combat-hit.png') });
    console.log('  命中截图:', JSON.stringify({ hp: st.hp, action: st.action, floats: st.floats }));
  }
  if (st.targetId) hadTarget = true;
  const counters = await page.evaluate(() => window.__c);
  diedPackets = counters.died;
  if (counters.died > 0 || (hadTarget && !st.targetId && counters.hit > 0)) {
    killed = true;
    break;
  }
}

await page.waitForTimeout(600);
await page.screenshot({ path: path.join(outDir, 'combat-after.png') });
const fin = await page.evaluate(() => ({
  hp: window.state.hp,
  level: window.state.level,
  exp: window.state.exp,
  inv: window.state.inventory.filter(Boolean).map((i) => i.info?.name),
  logs: [...document.querySelectorAll('#log div')].map((d) => d.textContent).slice(-8),
}));
console.log('结果:', JSON.stringify(fin, null, 1));
const counters = await page.evaluate(() => window.__c);
console.log('战斗统计:', JSON.stringify(counters), '发出包:', JSON.stringify(counters.sent ?? {}));
console.log('击杀:', killed, '| 见过的飘字:', [...sawFloat].join(','), '| 血量序列:', hpSeen.join('→'));
await browser.close();
