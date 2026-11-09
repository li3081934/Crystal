#!/usr/bin/env node
/**
 * 从 Shared/BaseStats.cs 生成 TS 的职业基础属性表（HP/MP 上限要自己算，服务端只发当前值）。
 *   node tools/gen-basestats.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(__dirname, '..', '..');
const src = path.join(repo, 'Shared', 'BaseStats.cs');
const out = path.join(__dirname, '..', 'client', 'src', 'game', 'basestats.ts');

const text = fs.readFileSync(src, 'utf8').replace(/\r\n/g, '\n');

const classes = {};
const caseRe = /case MirClass\.(\w+):/g;
let m;
const marks = [];
while ((m = caseRe.exec(text)) !== null) marks.push({ name: m[1], at: m.index });

for (let i = 0; i < marks.length; i++) {
  const { name, at } = marks[i];
  const end = i + 1 < marks.length ? marks[i + 1].at : text.length;
  const chunk = text.slice(at, end);
  const stats = [];
  const statRe =
    /new BaseStat\(Stat\.(\w+)\)\s*\{\s*FormulaType\s*=\s*StatFormula\.(\w+),\s*Base\s*=\s*(-?[\d.]+)F?\s*,\s*Gain\s*=\s*(-?[\d.]+)F?\s*,\s*GainRate\s*=\s*(-?[\d.]+)F?\s*,\s*Max\s*=\s*(-?[\d.]+)/g;
  let s;
  while ((s = statRe.exec(chunk)) !== null) {
    stats.push({
      stat: s[1],
      formula: s[2],
      base: Number(s[3]),
      gain: Number(s[4]),
      gainRate: Number(s[5]),
      max: Number(s[6]),
    });
  }
  if (stats.length) classes[name] = stats;
}

const lines = [
  '// 本文件由 tools/gen-basestats.mjs 从 Shared/BaseStats.cs 自动生成，请勿手改。',
  '/* eslint-disable */',
  "import { MirClass, Stat, StatFormula } from '../net/enums.ts';",
  '',
  'export interface BaseStatRow {',
  '  stat: number;',
  '  formula: number;',
  '  base: number;',
  '  gain: number;',
  '  gainRate: number;',
  '  max: number;',
  '}',
  '',
  'export const BASE_STATS: Record<number, BaseStatRow[]> = {',
];

for (const [name, stats] of Object.entries(classes)) {
  lines.push(`  [MirClass.${name}]: [`);
  for (const s of stats) {
    lines.push(
      `    { stat: Stat.${s.stat}, formula: StatFormula.${s.formula}, base: ${s.base}, gain: ${s.gain}, gainRate: ${s.gainRate}, max: ${s.max} }, // ${s.stat}`,
    );
  }
  lines.push('  ],');
}
lines.push('};', '');

fs.writeFileSync(out, lines.join('\n'), 'utf8');
console.log(`生成 ${path.relative(repo, out)}`);
for (const [k, v] of Object.entries(classes)) console.log(`  ${k}: ${v.length} 项`);
