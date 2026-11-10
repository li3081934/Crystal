/**
 * 整体换色：把源 Lib 的每张图按规则重新着色，输出新 Lib（几何/帧表原样保留）
 *
 * 用法:
 *   node Web/tools/recolor-lib.mjs <源Lib> <输出Lib> <配色json> [--debug 导出对比图目录]
 *
 * 配色 json（规则按顺序匹配，第一条命中即用）：
 * {
 *   "rules": [
 *     { "name": "眼窝红光",
 *       "hue": null, "satMax": 0.35, "lumaMax": 0.22,
 *       "mode": "solid", "color": "#ff2b2b" },
 *     { "name": "骨骼",
 *       "hue": null, "satMax": 0.30,
 *       "mode": "ramp",
 *       "stops": [ {"at": 0, "color": "#2a2018"}, {"at": 0.4, "color": "#8a7654"},
 *                  {"at": 0.75, "color": "#cdb98f"}, {"at": 1, "color": "#f6ecc9"} ] },
 *     { "name": "金饰→黄铜",
 *       "hue": [30, 75], "satMin": 0.25,
 *       "mode": "ramp",
 *       "stops": [ ... ] }
 *   ],
 *   "fallback": { "mode": "keep" }
 * }
 * mode: solid（固定色，保留明暗：与 luma 相乘）/ flat（纯色）/ ramp（按亮度取色）/ keep（不动）
 */
import fs from 'node:fs';
import path from 'node:path';
import { readLib, readImage, writeLib, bgraToRgba } from './lib.mjs';
import { encodePng, hexToRgb, decodePng } from './png.mjs';

const [, , srcFile, outFile, cfgFile, ...rest] = process.argv;
if (!srcFile || !outFile || !cfgFile) {
  console.error('用法: node Web/tools/recolor-lib.mjs <源Lib> <输出Lib> <配色json> [--debug <目录>]');
  process.exit(1);
}
const debugIdx = rest.indexOf('--debug');
const debugDir = debugIdx >= 0 ? rest[debugIdx + 1] : null;

const cfg = JSON.parse(fs.readFileSync(cfgFile, 'utf8'));
const rules = cfg.rules ?? [];
const fallback = cfg.fallback ?? { mode: 'keep' };

/* ---------- 颜色工具 ---------- */
function rgbToHsl(r, g, b) {
  r /= 255;
  g /= 255;
  b /= 255;
  const max = Math.max(r, g, b);
  const min = Math.min(r, g, b);
  const l = (max + min) / 2;
  if (max === min) return { h: 0, s: 0, l };
  const d = max - min;
  const s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
  let h;
  if (max === r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6;
  else if (max === g) h = ((b - r) / d + 2) / 6;
  else h = ((r - g) / d + 4) / 6;
  return { h: h * 360, s, l };
}

const luma = (r, g, b) => (0.299 * r + 0.587 * g + 0.114 * b) / 255;

function rampColor(stops, t) {
  const sorted = [...stops].sort((a, b) => a.at - b.at);
  if (t <= sorted[0].at) return hexToRgb(sorted[0].color);
  const last = sorted[sorted.length - 1];
  if (t >= last.at) return hexToRgb(last.color);
  for (let i = 0; i < sorted.length - 1; i++) {
    const a = sorted[i];
    const b = sorted[i + 1];
    if (t >= a.at && t <= b.at) {
      const k = (t - a.at) / (b.at - a.at || 1);
      const ca = hexToRgb(a.color);
      const cb = hexToRgb(b.color);
      return {
        r: ca.r + (cb.r - ca.r) * k,
        g: ca.g + (cb.g - ca.g) * k,
        b: ca.b + (cb.b - ca.b) * k,
      };
    }
  }
  return hexToRgb(last.color);
}

function pickRule(h, s, l) {
  for (const r of rules) {
    if (r.hue) {
      if (h < r.hue[0] || h > r.hue[1]) continue;
    }
    if (r.satMin !== undefined && s < r.satMin) continue;
    if (r.satMax !== undefined && s > r.satMax) continue;
    if (r.lumaMin !== undefined && l < r.lumaMin) continue;
    if (r.lumaMax !== undefined && l > r.lumaMax) continue;
    return r;
  }
  return fallback;
}

function applyRule(rule, r, g, b, l) {
  switch (rule.mode) {
    case 'keep':
      return null;
    case 'flat':
      return hexToRgb(rule.color);
    case 'solid': {
      // 固定色相乘明暗：保留骨骼结构的深浅
      const c = hexToRgb(rule.color);
      const k = Math.min(1, l * 2);
      return { r: c.r * k, g: c.g * k, b: c.b * k };
    }
    case 'ramp':
      return rampColor(rule.stops, l);
    default:
      return null;
  }
}

/* ---------- 主流程 ---------- */
const lib = readLib(srcFile);
console.log(`${path.basename(srcFile)}: version=${lib.version} count=${lib.count} frames=${lib.frames.length}`);

const out = new Array(lib.count).fill(null);
let touched = 0;
const usedRules = new Map();

for (let i = 0; i < lib.count; i++) {
  const img = readImage(lib, i);
  if (!img) continue;
  const src = Buffer.from(img.bgra);
  const outBgra = Buffer.alloc(src.length);
  for (let p = 0; p + 3 < src.length; p += 4) {
    const b = src[p];
    const g = src[p + 1];
    const r = src[p + 2];
    const a = src[p + 3];
    outBgra[p + 3] = a;
    if (a === 0) continue;
    const { h, s, l } = rgbToHsl(r, g, b);
    if (l > 0.96 && s < 0.06) {
      // 近白高光保留，避免整只怪"发灰"
    }
    const rule = pickRule(h, s, l);
    usedRules.set(rule.name ?? rule.mode, (usedRules.get(rule.name ?? rule.mode) ?? 0) + 1);
    const c = applyRule(rule, r, g, b, l);
    if (!c) {
      outBgra[p] = b;
      outBgra[p + 1] = g;
      outBgra[p + 2] = r;
    } else {
      outBgra[p] = Math.round(c.b);
      outBgra[p + 1] = Math.round(c.g);
      outBgra[p + 2] = Math.round(c.r);
      touched++;
    }
  }
  out[i] = { ...img, bgra: outBgra };
}

const res = writeLib(outFile, out, lib.frames);
console.log(`写出 ${res.file}  图片 ${res.count} 张  帧表 ${lib.frames.length} 项  ${(res.size / 1024).toFixed(1)}KB  改色像素 ${touched}`);
console.log('规则命中:', [...usedRules.entries()].map(([k, v]) => `${k}=${v}`).join(' '));

if (debugDir) {
  fs.mkdirSync(debugDir, { recursive: true });
  for (let i = 0; i < Math.min(lib.count, 12); i++) {
    const a = readImage(lib, i);
    const d = out[i];
    if (!a || !d) continue;
    fs.writeFileSync(path.join(debugDir, `${String(i).padStart(4, '0')}-src.png`), encodePng(bgraToRgba(Buffer.from(a.bgra)), a.w, a.h));
    fs.writeFileSync(path.join(debugDir, `${String(i).padStart(4, '0')}-dst.png`), encodePng(bgraToRgba(Buffer.from(d.bgra)), d.w, d.h));
  }
  console.log(`对比图已导出到 ${debugDir}`);
}
