#!/usr/bin/env node
/**
 * gen-image.mjs — OpenRouter 图片生成 + PNG 主色统计（零依赖，Node 18+）
 *
 * 生成:
 *   node Web/tools/gen-image.mjs --prompt p.txt --out a.png [--size 1024x1024 | --resolution 2K --aspect-ratio 9:21] [--n 1]
 *        [--ref ref1.png] [--ref ref2.png] [--seed 123] [--background transparent]
 *        [--model tencent/hy-image-v3.5-preview] [--timeout 300000]
 *
 *   --prompt  可以是提示词文件路径，也可以直接是提示词文本
 *   --ref     参考图（可重复），以 data URL 放进 input_references
 *   --n       >1 时输出名自动变成 a-01.png, a-02.png ...
 *
 * 取色:
 *   node Web/tools/gen-image.mjs --palette a.png [--top 12]
 *   输出 JSON: [{hex,rgb,count,ratio}]
 *
 * key 只从 C:/Users/lijs8/.pi/agent/auth.json 的 openrouter.key 读取，绝不打印。
 */
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';
import path from 'node:path';
import zlib from 'node:zlib';

const AUTH_FILE = 'C:/Users/lijs8/.pi/agent/auth.json';
const ENDPOINT = 'https://openrouter.ai/api/v1/images';
const DEFAULT_MODEL = 'tencent/hy-image-v3.5-preview';

const MIME = { '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg', '.webp': 'image/webp', '.gif': 'image/gif' };

function die(msg) {
  console.error(msg);
  process.exit(1);
}

function parseArgs(argv) {
  const out = { refs: [] };
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i];
    const next = () => {
      const v = argv[++i];
      if (v === undefined) die(`缺少参数值: ${a}`);
      return v;
    };
    switch (a) {
      case '--prompt': out.prompt = next(); break;
      case '--out': out.out = next(); break;
      case '--size': out.size = next(); break;
      case '--resolution': out.resolution = next(); break;
      case '--aspect-ratio': out.aspectRatio = next(); break;
      case '--output-format': out.outputFormat = next(); break;
      case '--proxy': out.proxy = next(); break;
      case '--n': out.n = Number(next()); break;
      case '--ref': out.refs.push(next()); break;
      case '--seed': out.seed = Number(next()); break;
      case '--background': out.background = next(); break;
      case '--model': out.model = next(); break;
      case '--timeout': out.timeout = Number(next()); break;
      case '--palette': out.palette = next(); break;
      case '--top': out.top = Number(next()); break;
      case '--help': case '-h': out.help = true; break;
      default: die(`未知参数: ${a}`);
    }
  }
  return out;
}

function readKey() {
  if (!fs.existsSync(AUTH_FILE)) die(`找不到鉴权文件: ${AUTH_FILE}`);
  let json;
  try {
    json = JSON.parse(fs.readFileSync(AUTH_FILE, 'utf8'));
  } catch (e) {
    die(`鉴权文件不是合法 JSON: ${e.message}`);
  }
  const key = json?.openrouter?.key;
  if (!key || typeof key !== 'string') die('auth.json 里没有 openrouter.key');
  return key.trim();
}

function resolvePrompt(p) {
  if (!p) die('缺少 --prompt');
  try {
    if (fs.existsSync(p) && fs.statSync(p).isFile()) return fs.readFileSync(p, 'utf8').trim();
  } catch { /* 当成纯文本 */ }
  return p;
}

function refToDataUrl(file) {
  if (!fs.existsSync(file)) die(`参考图不存在: ${file}`);
  const mime = MIME[path.extname(file).toLowerCase()] || 'image/png';
  return `data:${mime};base64,${fs.readFileSync(file).toString('base64')}`;
}

async function generate(args) {
  const key = readKey();
  const prompt = resolvePrompt(args.prompt);
  const n = Number.isFinite(args.n) && args.n > 0 ? Math.min(10, Math.floor(args.n)) : 1;
  const size = args.size || null;
  const outPath = args.out || die('缺少 --out');

  const body = {
    model: args.model || DEFAULT_MODEL,
    prompt,
    n,
  };
  if (size) body.size = size;
  if (args.resolution) body.resolution = args.resolution;
  if (args.aspectRatio) body.aspect_ratio = args.aspectRatio;
  if (args.outputFormat) body.output_format = args.outputFormat;
  if (args.seed !== undefined && Number.isFinite(args.seed)) body.seed = Math.floor(args.seed);
  if (args.background) body.background = args.background;
  if (args.refs.length) {
    body.input_references = args.refs.map((f) => ({ type: 'image_url', image_url: { url: refToDataUrl(f) } }));
  }

  // 不打印 key，也不打印完整 body（参考图 base64 会淹没终端）
  console.log(`[gen] model=${body.model} ${size ? `size=${size}` : ''}` +
    `${args.resolution ? ` resolution=${args.resolution}` : ''}${args.aspectRatio ? ` aspect=${args.aspectRatio}` : ''}` +
    `${args.outputFormat ? ` fmt=${args.outputFormat}` : ''}${args.proxy ? ' proxy=on' : ''} n=${n} refs=${args.refs.length}` +
    `${body.seed !== undefined ? ` seed=${body.seed}` : ''}${body.background ? ` bg=${body.background}` : ''}`);

  const ac = new AbortController();
  const timeout = Number.isFinite(args.timeout) && args.timeout > 0 ? args.timeout : 300000;
  const timer = setTimeout(() => ac.abort(new Error(`请求超时 ${timeout}ms`)), timeout);

  const payload = JSON.stringify(body);
  const headers = {
    Authorization: `Bearer ${key}`,
    'Content-Type': 'application/json',
    'HTTP-Referer': 'https://github.com/mir2-local',
    'X-Title': 'mir2-petboon-art',
  };

  let ok;
  let statusText;
  let text;
  try {
    if (args.proxy) {
      // Node 内置 fetch 不走系统代理，这里用 curl 走 HTTP 代理（-x）
      const out = execFileSync('curl', [
        '-sS', '--max-time', String(Math.floor(timeout / 1000)),
        '-x', args.proxy,
        '-X', 'POST', ENDPOINT,
        ...Object.entries(headers).flatMap(([k, v]) => ['-H', `${k}: ${v}`]),
        '--data-binary', '@-',
      ], { input: payload, maxBuffer: 256 * 1024 * 1024, encoding: 'utf8' });
      ok = true;
      statusText = 'via proxy';
      text = out;
    } else {
      const res = await fetch(ENDPOINT, { method: 'POST', headers, body: payload, signal: ac.signal });
      ok = res.ok;
      statusText = `${res.status} ${res.statusText}`;
      text = await res.text();
    }
  } finally {
    clearTimeout(timer);
  }

  if (!ok) {
    console.error(`[gen] HTTP ${statusText}`);
    try {
      console.error(JSON.stringify(JSON.parse(text), null, 2));
    } catch {
      console.error(text.slice(0, 4000));
    }
    process.exit(1);
  }

  let json;
  try {
    json = JSON.parse(text);
  } catch {
    console.error('[gen] 响应不是 JSON：');
    console.error(text.slice(0, 4000));
    process.exit(1);
  }

  const items = Array.isArray(json?.data) ? json.data : [];
  if (!items.length) {
    console.error('[gen] 响应里没有 data[]：');
    console.error(JSON.stringify(json, null, 2).slice(0, 4000));
    process.exit(1);
  }

  fs.mkdirSync(path.dirname(path.resolve(outPath)), { recursive: true });
  const written = [];
  for (let i = 0; i < items.length; i++) {
    const b64 = items[i]?.b64_json;
    if (!b64) {
      console.error(`[gen] 第 ${i + 1} 张没有 b64_json，跳过`);
      continue;
    }
    const buf = Buffer.from(b64, 'base64');
    if (buf.length < 1024) {
      console.error(`[gen] 第 ${i + 1} 张解码后只有 ${buf.length} 字节，疑似坏图`);
      continue;
    }
    const mediaType = items[i]?.media_type || '';
    const realExt = mediaType === 'image/jpeg' ? '.jpg' : mediaType === 'image/webp' ? '.webp' : '.png';
    let basePath = outPath;
    if (realExt !== '.png' && !outPath.toLowerCase().endsWith(realExt)) {
      basePath = outPath.replace(/\.png$/i, realExt);
      console.log(`[gen] 服务端返回 ${mediaType}，改写为 ${path.basename(basePath)}`);
    }
    let file = basePath;
    if (items.length > 1) {
      const ext = path.extname(basePath);
      file = path.join(path.dirname(basePath), `${path.basename(basePath, ext)}-${String(i + 1).padStart(2, '0')}${ext}`);
    }
    fs.writeFileSync(file, buf);
    const abs = path.resolve(file);
    written.push(abs);
    console.log(`[ok] ${abs}  ${buf.length} bytes (${(buf.length / 1024).toFixed(1)} KB)`);
  }

  if (!written.length) die('[gen] 一张都没写出来');

  // 把用过的提示词与参数记在旁边，方便复现
  const metaFile = outPath.replace(/\.png$/i, '') + '.json';
  try {
    fs.writeFileSync(metaFile, JSON.stringify({
      model: body.model, size: size ?? null, outputFormat: args.outputFormat ?? null, resolution: args.resolution ?? null, aspectRatio: args.aspectRatio ?? null,
      n, seed: body.seed ?? null, background: body.background ?? null,
      refs: args.refs.map((f) => path.resolve(f)), prompt, files: written,
      at: new Date().toISOString(),
    }, null, 2));
  } catch { /* 记录失败不影响出图 */ }
}

/* ---------------- PNG decode (8-bit, non-interlaced, gray/RGB/RGBA) ---------------- */

const CRC_TABLE = (() => {
  const t = new Uint32Array(256);
  for (let n = 0; n < 256; n++) {
    let c = n;
    for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
    t[n] = c >>> 0;
  }
  return t;
})();
function crc32(buf) {
  let c = 0xffffffff;
  for (let i = 0; i < buf.length; i++) c = CRC_TABLE[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return (c ^ 0xffffffff) >>> 0;
}

function decodePng(file) {
  const buf = fs.readFileSync(file);
  if (buf.readUInt32BE(0) !== 0x89504e47) throw new Error('不是 PNG 文件');
  let off = 8;
  let ihdr = null;
  let palette = null;
  let trns = null;
  const idat = [];
  while (off < buf.length) {
    const len = buf.readUInt32BE(off);
    const type = buf.toString('ascii', off + 4, off + 8);
    const data = buf.subarray(off + 8, off + 8 + len);
    if (type === 'IHDR') {
      ihdr = {
        width: data.readUInt32BE(0),
        height: data.readUInt32BE(4),
        depth: data[8],
        colorType: data[9],
        interlace: data[12],
      };
    } else if (type === 'PLTE') {
      palette = Buffer.from(data);
    } else if (type === 'tRNS') {
      trns = Buffer.from(data);
    } else if (type === 'IDAT') {
      idat.push(Buffer.from(data));
    } else if (type === 'IEND') {
      break;
    }
    off += 12 + len;
  }
  if (!ihdr) throw new Error('PNG 缺少 IHDR');
  if (ihdr.depth !== 8) throw new Error(`只支持 8-bit PNG（当前 ${ihdr.depth}）`);
  if (ihdr.interlace !== 0) throw new Error('不支持隔行扫描 PNG');

  const ch = { 0: 1, 2: 3, 3: 1, 4: 2, 6: 4 }[ihdr.colorType];
  if (!ch) throw new Error(`不支持的颜色类型 ${ihdr.colorType}`);
  const { width: w, height: h } = ihdr;
  const raw = zlib.inflateSync(Buffer.concat(idat));
  const stride = w * ch;
  if (raw.length < (stride + 1) * h) throw new Error('PNG 数据不完整');

  const out = Buffer.alloc(w * h * 4);
  const prev = Buffer.alloc(stride);
  const cur = Buffer.alloc(stride);
  for (let y = 0; y < h; y++) {
    const base = y * (stride + 1);
    const filter = raw[base];
    raw.copy(cur, 0, base + 1, base + 1 + stride);
    for (let x = 0; x < stride; x++) {
      const a = x >= ch ? cur[x - ch] : 0;
      const b = prev[x];
      const c = x >= ch ? prev[x - ch] : 0;
      let v = cur[x];
      if (filter === 1) v += a;
      else if (filter === 2) v += b;
      else if (filter === 3) v += (a + b) >> 1;
      else if (filter === 4) {
        const p = a + b - c;
        const pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
        v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
      }
      cur[x] = v & 0xff;
    }
    for (let x = 0; x < w; x++) {
      const o = (y * w + x) * 4;
      let r, g, bl, al = 255;
      if (ihdr.colorType === 0) { r = g = bl = cur[x]; }
      else if (ihdr.colorType === 2) { r = cur[x * 3]; g = cur[x * 3 + 1]; bl = cur[x * 3 + 2]; }
      else if (ihdr.colorType === 3) {
        const idx = cur[x];
        r = palette[idx * 3]; g = palette[idx * 3 + 1]; bl = palette[idx * 3 + 2];
        if (trns && idx < trns.length) al = trns[idx];
      } else if (ihdr.colorType === 4) { r = g = bl = cur[x * 2]; al = cur[x * 2 + 1]; }
      else { r = cur[x * 4]; g = cur[x * 4 + 1]; bl = cur[x * 4 + 2]; al = cur[x * 4 + 3]; }
      out[o] = r; out[o + 1] = g; out[o + 2] = bl; out[o + 3] = al;
    }
    cur.copy(prev);
  }
  return { width: w, height: h, rgba: out };
}

/* ---------------- palette ---------------- */

const hex = (r, g, b) => '#' + [r, g, b].map((v) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0')).join('').toUpperCase();
const lum = (r, g, b) => 0.2126 * r + 0.7152 * g + 0.0722 * b;

function palette(args) {
  const img = decodePng(args.palette);
  const topN = Number.isFinite(args.top) && args.top > 0 ? args.top : 12;
  const px = []; // 有效像素 [r,g,b] 扁平数组
  const buckets = new Map(); // 6bit -> {r,g,b,n}，用于播种
  for (let i = 0; i < img.rgba.length; i += 4) {
    const a = img.rgba[i + 3];
    if (a < 128) continue; // 透明像素
    const r = img.rgba[i], g = img.rgba[i + 1], b = img.rgba[i + 2];
    // 忽略近白背景（纯白/浅灰纸底）
    if (r > 238 && g > 238 && b > 238) continue;
    px.push(r, g, b);
    const key = ((r >> 2) << 12) | ((g >> 2) << 6) | (b >> 2);
    const e = buckets.get(key);
    if (e) { e.r += r; e.g += g; e.b += b; e.n++; }
    else buckets.set(key, { r, g, b, n: 1 });
  }
  const total = px.length / 3;
  if (!total) throw new Error('统计不到有效像素（全是背景或透明）');

  // 播种：按频次取互相距离足够远的颜色，避免全部落在描边黑里
  const sorted = [...buckets.values()].sort((x, y) => y.n - x.n);
  const seeds = [];
  const MIN_SEED_DIST = 34;
  for (const b of sorted) {
    const r = b.r / b.n, g = b.g / b.n, bl = b.b / b.n;
    if (seeds.length >= topN) break;
    if (seeds.every((s) => Math.hypot(s.r - r, s.g - g, s.b - bl) >= MIN_SEED_DIST)) seeds.push({ r, g, b: bl });
  }
  while (seeds.length < topN) {
    const b = sorted[seeds.length % sorted.length];
    seeds.push({ r: b.r / b.n, g: b.g / b.n, b: b.b / b.n });
  }

  // k-means：比贪心合并更能保住不同色相（骨白/铁灰/黄铜/暗红/描边）
  const K = seeds.length;
  const cen = seeds.map((s) => ({ r: s.r, g: s.g, b: s.b, n: 0, sr: 0, sg: 0, sb: 0 }));
  for (let iter = 0; iter < 12; iter++) {
    for (const c of cen) { c.n = 0; c.sr = 0; c.sg = 0; c.sb = 0; }
    for (let i = 0; i < px.length; i += 3) {
      const r = px[i], g = px[i + 1], b = px[i + 2];
      let bi = 0, bd = Infinity;
      for (let k = 0; k < K; k++) {
        const c = cen[k];
        const dr = r - c.r, dg = g - c.g, db = b - c.b;
        const d = dr * dr + dg * dg + db * db;
        if (d < bd) { bd = d; bi = k; }
      }
      const c = cen[bi];
      c.n++; c.sr += r; c.sg += g; c.sb += b;
    }
    let moved = 0;
    for (const c of cen) {
      if (!c.n) continue;
      const nr = c.sr / c.n, ng = c.sg / c.n, nb = c.sb / c.n;
      moved = Math.max(moved, Math.abs(nr - c.r) + Math.abs(ng - c.g) + Math.abs(nb - c.b));
      c.r = nr; c.g = ng; c.b = nb;
    }
    if (moved < 0.5) break;
  }

  const clusters = cen.filter((c) => c.n > 0).sort((x, y) => y.n - x.n);
  const result = clusters.slice(0, topN).map((c, i) => ({
    rank: i + 1,
    hex: hex(c.r, c.g, c.b),
    rgb: [Math.round(c.r), Math.round(c.g), Math.round(c.b)],
    pixels: c.n,
    ratio: +(c.n / total * 100).toFixed(2),
    lum: Math.round(lum(c.r, c.g, c.b)),
  }));
  console.log(JSON.stringify({ file: path.resolve(args.palette), size: `${img.width}x${img.height}`, countedPixels: total, colors: result }, null, 2));
}

/* ---------------- main ---------------- */

const args = parseArgs(process.argv.slice(2));
if (args.help) {
  console.log(fs.readFileSync(new URL(import.meta.url), 'utf8').split('*/')[0].replace(/^#!.*\n/, ''));
  process.exit(0);
}
if (args.palette) {
  try { palette(args); } catch (e) { die(`[palette] ${e.message}`); }
} else {
  generate(args).catch((e) => die(`[gen] ${e?.message || e}`));
}
