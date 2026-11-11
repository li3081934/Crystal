#!/usr/bin/env node
// 3d-build-lib.mjs —— 一条命令跑完「3D 渲染 → 网格表 → .Lib」。
//
//   node Web/tools/3d-build-lib.mjs --frames <渲染目录> --ref <参考Lib> --out <输出Lib>
//        [--work <中间目录>] [--only stand,walk] [--layout <已有 layout.json>] [--hard]
//        [--posterize 6] [--quantize 24] [--no-shadow] [--dry]
//
// 做的事：
//   1. 若没有 layout.json，先用 export-sheet.mjs 导出参考 Lib 的帧几何（画布/偏移/帧表）
//   2. frames-to-sheet.mjs：渲染帧 → 洋红底网格表（阴影在这里抖成棋盘）
//   3. 逐动作调 sheet-to-lib.mjs，链式覆盖同一个输出 Lib（像素换掉，几何照抄参考帧）
//   4. 打印 montage 校验命令
//
// 前提：渲染目录里有 render-8dir.py 产出的 *-fN.png / *-shadow.png / render-meta.json

import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

function arg(name, def) {
  const i = process.argv.indexOf(name);
  return i < 0 ? def : process.argv[i + 1];
}
const dry = process.argv.includes('--dry');
const only = arg('--only', null);
const frames = arg('--frames', null);
const ref = arg('--ref', null);
const out = arg('--out', null);
const work = arg('--work', null) || path.join(os.tmpdir(), 'mir3d-' + path.basename(frames || 'work').replace(/[^\w.-]/g, '_'));
const posterize = arg('--posterize', '6');
const quantize = arg('--quantize', '24');
const extra = [];
if (process.argv.includes('--hard')) extra.push('--hard');
// 调色板吸附：默认用参考 Lib 自己的调色板（新像素吸附到原画色系），--no-palette 关掉
const paletteArgs = [];
if (!process.argv.includes('--no-palette')) {
  paletteArgs.push('--palette-from', arg('--palette-from', ref));
  const ps = arg('--palette-size', null);
  if (ps) paletteArgs.push('--palette-size', ps);
}
if (process.argv.includes('--no-shadow') || arg('--shadow-color', null)) extra.push('--no-shadow');

if (!frames || !ref || !out) {
  console.error('用法: node Web/tools/3d-build-lib.mjs --frames <渲染目录> --ref <参考Lib> --out <输出Lib> [--work 目录] [--only stand,walk] [--dry]');
  process.exit(2);
}
fs.mkdirSync(work, { recursive: true });

const tools = path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'));
const run = (tool, args) => {
  const cmd = ['node', path.join(tools, tool), ...args];
  console.log('  $ ' + cmd.map((c) => (/\s/.test(c) ? `"${c}"` : c)).join(' '));
  if (dry) return;
  execFileSync(cmd[0], cmd.slice(1), { stdio: 'inherit' });
};

// 1) 参考 Lib 的帧几何
let layoutPath = arg('--layout', null);
if (!layoutPath) {
  layoutPath = path.join(work, 'ref-layout', 'layout.json');
  if (!fs.existsSync(layoutPath)) {
    console.log('[1/3] 导出参考 Lib 帧几何 …');
    run('export-sheet.mjs', [ref, path.join(work, 'ref-layout'), '--scale', '1', '--pad', '4']);
  } else {
    console.log('[1/3] 复用已有 layout.json');
  }
} else {
  console.log('[1/3] 使用指定 layout: ' + layoutPath);
}
if (!fs.existsSync(layoutPath) && !dry) {
  console.error('找不到 layout.json：' + layoutPath);
  process.exit(1);
}

// 2) 渲染帧 → 网格表
const sheetDir = path.join(work, 'sheets');
console.log('[2/3] 组装网格表 …');
// 高度倍率：>1 就是让成品比原版骨架更高大（Lib 帧画布同步变高，sheet-to-lib 会自动改 offset）
const hs = arg('--height-scale', null);
if (hs) extra.push('--height-scale', hs);
run('frames-to-sheet.mjs', [frames, layoutPath, sheetDir, '--ref-lib', ref, ...extra]);
if (dry) { console.log('\n[dry] 到此为止'); process.exit(0); }

const sheetLayout = JSON.parse(fs.readFileSync(path.join(sheetDir, 'sheet-layout.json'), 'utf8'));
const refLayout = JSON.parse(fs.readFileSync(layoutPath, 'utf8'));

// 3) 逐动作贴回
const wanted = only ? new Set(only.split(',').map((s) => s.trim())) : null;
const sheets = sheetLayout.sheets.filter((s) => !wanted || wanted.has(s.name) || wanted.has(String(s.action)));
if (!sheets.length) { console.error('没有可贴回的动作'); process.exit(1); }

console.log('[3/3] 贴回 Lib …');
let current = ref;
sheets.forEach((sheet, i) => {
  // dirStart 是「方向偏移」（行号），不是绝对帧号：index = frame.start + (dirStart+r)*stride + c
  const dirStart = 0;
  const isLast = i === sheets.length - 1;
  const target = isLast ? path.resolve(out) : path.join(work, `step-${i}-${sheet.action}.Lib`);
  console.log(`  action ${String(sheet.action).padStart(2, '0')} ${sheet.name}  dirStart=${dirStart}`);
  // --scale 1：1:1 贴图，绝不重采样 —— 否则阴影的 50% 棋盘会被面积平均糊成灰块
  run('sheet-to-lib.mjs', [current, sheet.file, target, String(sheet.action), String(dirStart),
    '--cellw', String(sheet.cellW), '--cellh', String(sheet.cellH),
    '--scale', '1', '--keep-all', '--fresh', '--posterize', posterize, '--quantize', quantize,
    ...paletteArgs]);
  current = target;
});

const size = fs.existsSync(out) ? fs.statSync(out).size : 0;
console.log(`\n完成: ${out}  (${size.toLocaleString()} 字节)`);
console.log(`校验: node Web/tools/montage-lib.mjs "${out}" montage.png`);
console.log(`尺寸: node Web/tools/lib-size.mjs "${out}"`);
