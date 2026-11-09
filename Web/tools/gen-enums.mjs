#!/usr/bin/env node
/**
 * 从 Shared/Enums.cs 生成 TS 枚举（协议 ID / 方向 / 职业 / 动作 ...），
 * 避免手抄数字导致和 C# 服务端不同步。
 *
 *   npm run gen:ids
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(__dirname, '..', '..');
// 枚举散落在几个文件里：Enums.cs 是主文件，Stat/StatFormula 在 Data/Stat.cs
const srcFiles = [path.join(repoRoot, 'Shared', 'Enums.cs'), path.join(repoRoot, 'Shared', 'Data', 'Stat.cs')];
const outFile = path.join(__dirname, '..', 'client', 'src', 'net', 'enums.ts');

const text = srcFiles.map((f) => fs.readFileSync(f, 'utf8')).join('\n');

/** 找到 `public enum Name ... { ... }` 并返回成员 */
function parseEnums(src) {
  const enums = [];
  const re = /public\s+enum\s+([A-Za-z0-9_]+)[^{]*\{([\s\S]*?)\n\s*\}/g;
  let m;
  while ((m = re.exec(src)) !== null) {
    const name = m[1];
    const body = m[2]
      .replace(/\/\*[\s\S]*?\*\//g, '')
      .replace(/\/\/[^\n]*/g, '');
    const members = [];
    let next = 0;
    for (const raw of body.split(',')) {
      const entry = raw.trim();
      if (!entry) continue;
      const eq = entry.indexOf('=');
      let memberName = (eq >= 0 ? entry.slice(0, eq) : entry).trim();
      if (!/^[A-Za-z0-9_]+$/.test(memberName)) continue;
      let value = next;
      if (eq >= 0) {
        const v = entry.slice(eq + 1).trim();
        const num = /^(-?0x[0-9a-fA-F]+|-?\d+)$/.exec(v);
        if (num) value = Number(num[0]);
        else {
          // 形如 `A = B` / `A = B + 1`：尽量解析
          const ref = /^([A-Za-z0-9_]+)\s*(?:\+\s*(-?\d+))?$/.exec(v);
          const known = ref && members.find((x) => x.name === ref[1]);
          if (known) value = known.value + (ref[2] ? Number(ref[2]) : 0);
          else {
            console.warn(`  ! ${name}.${memberName} 无法解析值 "${v}"，按递增处理`);
            value = next;
          }
        }
      }
      members.push({ name: memberName, value });
      next = value + 1;
    }
    if (members.length) enums.push({ name, members });
  }
  return enums;
}

const enums = parseEnums(text);
const lines = [
  '// 本文件由 tools/gen-enums.mjs 从 Shared/Enums.cs 自动生成，请勿手改。',
  '/* eslint-disable */',
  '',
];

for (const e of enums) {
  const entries = e.members.map((m) => `  ${m.name}: ${m.value},`).join('\n');
  lines.push(`export const ${e.name} = {\n${entries}\n} as const;`);
  lines.push(`export type ${e.name} = number;`);
  // 数值 -> 名字，日志里方便看
  lines.push(
    `export const ${e.name}Name: Record<number, string> = Object.fromEntries(Object.entries(${e.name}).map(([k, v]) => [v as number, k]));`,
  );
  lines.push('');
}

fs.mkdirSync(path.dirname(outFile), { recursive: true });
fs.writeFileSync(outFile, lines.join('\n'), 'utf8');
console.log(`生成 ${path.relative(repoRoot, outFile)}：${enums.length} 个枚举`);
for (const n of ['ClientPacketIds', 'ServerPacketIds', 'MirDirection', 'MirClass', 'MirAction']) {
  const e = enums.find((x) => x.name === n);
  console.log(`  ${n}: ${e ? e.members.length + ' 项' : '缺失!'}`);
}
