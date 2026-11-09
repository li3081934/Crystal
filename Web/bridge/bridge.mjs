#!/usr/bin/env node
/**
 * Mir2 Web Bridge
 * ---------------
 * 浏览器只能跑 WebSocket，服务端只会跑裸 TCP —— 这个进程负责在两者之间转发字节，
 * 一个字节都不改（协议本身是 `2B 长度 + 2B 包ID + payload`）。
 *
 * 同时它还把客户端资源目录（Build/Client/Release）以 /assets/** 暴露出来，
 * 支持 Range（大 .Lib 分段下载）和长缓存头。
 *
 * 用法:
 *   node bridge/bridge.mjs
 *   PORT=8787 MIR2_ASSETS="/path/to/Build/Client/Release" \
 *   MIR2_HOST=127.0.0.1 MIR2_PORT=7000 node bridge/bridge.mjs
 *
 * 端点:
 *   GET  /health                     -> { ok: true }
 *   GET  /assets/<任意相对路径>       -> 静态资源（Data/*.Lib, Map/*.map ...）
 *   WS   /ws?host=&port=             -> TCP 隧道
 */
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';
import net from 'node:net';
import { fileURLToPath } from 'node:url';
import { pipeline } from 'node:stream/promises';
import { Readable } from 'node:stream';
import { ProxyAgent, fetch as undiciFetch } from 'undici';
import { WebSocketServer } from 'ws';

const __dirname = path.dirname(fileURLToPath(import.meta.url));

const HTTP_PORT = Number(process.env.PORT ?? 8787);
const ASSET_ROOT = path.resolve(
  process.env.MIR2_ASSETS ?? path.join(__dirname, '..', '..', 'Build', 'Client', 'Release'),
);
const GAME_HOST = process.env.MIR2_HOST ?? '127.0.0.1';
const GAME_PORT = Number(process.env.MIR2_PORT ?? 7000);

/**
 * 原版客户端是"缺什么就从补丁站下什么"。这里用同样的思路：
 * 本地没有的资源（比如 Objects17.Lib、CHair/06.Lib）就去补丁站取，同时后台下一份完整的到 .cache，
 * 下次直接走本地文件（Range 也就正常了）。
 */
/**
 * 补丁站 + 代理：优先读环境变量，其次读原版客户端的 Mir2Config.ini（[Download] Host / Proxy），
 * 这样和 C# 客户端保持同一套配置，不用重复填。
 */
function readClientDownloadConfig() {
  const candidates = [
    path.join(__dirname, '..', '..', 'Build', 'Client', 'Release', 'Mir2Config.ini'),
    path.join(__dirname, '..', '..', 'Build', 'Client', 'Debug', 'Mir2Config.ini'),
  ];
  for (const file of candidates) {
    try {
      const text = fs.readFileSync(file, 'utf8');
      let inSection = false;
      let host;
      let proxy;
      for (const raw of text.split(String.fromCharCode(10))) {
        const line = raw.replace(String.fromCharCode(13), '').trim();
        if (line.startsWith('[')) { inSection = line === '[Download]'; continue; }
        if (!inSection) continue;
        const eq = line.indexOf('=');
        if (eq < 0) continue;
        const key = line.slice(0, eq).trim().toLowerCase();
        const value = line.slice(eq + 1).trim();
        if (key === "host") host = value;
        if (key === "proxy") proxy = value;
      }
      if (host || proxy) return { host, proxy };
    } catch {
      /* 下一个 */
    }
  }
  return {};
}
const clientCfg = readClientDownloadConfig();
const PATCH_BASE = process.env.MIR2_PATCH_URL ?? clientCfg.host ?? 'https://mirfiles.co.uk/resources/mir2/crystal/patch/';
const PATCH_PROXY = process.env.MIR2_PATCH_PROXY ?? clientCfg.proxy ?? '';
const proxyAgent = PATCH_PROXY
  ? new ProxyAgent({ uri: PATCH_PROXY, requestTls: { rejectUnauthorized: false } })
  : null;
const CACHE_ROOT = path.resolve(process.env.MIR2_CACHE ?? path.join(__dirname, '..', '.cache', 'assets'));
const downloads = new Map(); // relPath -> Promise（single-flight）

const MIME = {
  '.lib': 'application/octet-stream',
  '.map': 'application/octet-stream',
  '.part': 'application/octet-stream',
  '.json': 'application/json; charset=utf-8',
  '.bin': 'application/octet-stream',
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.wav': 'audio/wav',
  '.mp3': 'audio/mpeg',
  '.ini': 'text/plain; charset=utf-8',
  '.txt': 'text/plain; charset=utf-8',
};

function log(...args) {
  console.log(new Date().toISOString().slice(11, 23), ...args);
}

// 上游补丁站偶尔会超时/RST，桥必须活着（不能让未处理的 error 事件把进程带走）
process.on('uncaughtException', (e) => log(`uncaughtException: ${e?.message ?? e}`));
process.on('unhandledRejection', (e) => log(`unhandledRejection: ${e?.message ?? e}`));

/** 只允许访问 ASSET_ROOT 内部的路径 */
function resolveAsset(urlPath) {
  const rel = decodeURIComponent(urlPath.replace(/^\/assets\/?/, ''));
  const full = path.resolve(ASSET_ROOT, rel);
  const rootWithSep = ASSET_ROOT.endsWith(path.sep) ? ASSET_ROOT : ASSET_ROOT + path.sep;
  if (full !== ASSET_ROOT && !full.startsWith(rootWithSep)) return null;
  return full;
}

function cachePathFor(rel) {
  const full = path.resolve(CACHE_ROOT, rel);
  const rootWithSep = CACHE_ROOT.endsWith(path.sep) ? CACHE_ROOT : CACHE_ROOT + path.sep;
  if (!full.startsWith(rootWithSep)) return null;
  return full;
}

/** 后台整份下载到缓存（single-flight），失败就放弃，不影响当前请求 */
const MAX_CONCURRENT_DOWNLOADS = 2;
let activeDownloads = 0;

function ensureCached(rel, size) {
  if (!size || size < 1) return;
  if (downloads.has(rel)) return;
  if (activeDownloads >= MAX_CONCURRENT_DOWNLOADS) return; // 别把带宽全占了，否则 range 请求会超时
  const target = cachePathFor(rel);
  if (!target) return;

  activeDownloads++;
  const task = (async () => {
    const tmp = `${target}.part`;
    try {
      fs.mkdirSync(path.dirname(target), { recursive: true });
      const { response: upstream, via } = await patchFetch(
        new URL(rel.split('/').map(encodeURIComponent).join('/'), PATCH_BASE),
        { timeout: 10 * 60 * 1000 },
      );
      log(`cache 下载 ${rel}（${via}）`);
      if (!upstream.ok || !upstream.body) {
        log(`cache 跳过 ${rel}: HTTP ${upstream.status}`);
        return;
      }
      await pipeline(Readable.fromWeb(upstream.body), fs.createWriteStream(tmp));
      fs.renameSync(tmp, target);
      log(`cache  ${rel} (${(fs.statSync(target).size / 1024 / 1024).toFixed(1)}MB)`);
    } catch (e) {
      log(`cache 失败 ${rel}: ${e.message}`);
      try {
        fs.unlinkSync(tmp);
      } catch {
        /* ignore */
      }
    } finally {
      activeDownloads--;
      downloads.delete(rel);
    }
  })();
  downloads.set(rel, task);
}

/** 访问补丁站：配了代理就先走代理，失败自动回退直连 */
/** 访问补丁站：配了代理就先走代理，失败自动回退直连。
 *  注意：必须用 undici 自带的 fetch，Node 自带的 global fetch 不认外部 undici 的 dispatcher。
 */
async function patchFetch(url, init = {}) {
  const timeout = init.timeout ?? 30 * 1000;
  if (proxyAgent) {
    try {
      const r = await undiciFetch(url, { ...init, dispatcher: proxyAgent, signal: AbortSignal.timeout(timeout) });
      return { response: r, via: 'proxy' };
    } catch (e) {
      const reason = e?.cause?.code ?? e?.cause?.message ?? e.message;
      log(`代理失败（${reason}），回退直连`);
    }
  }
  const r = await undiciFetch(url, { ...init, signal: AbortSignal.timeout(timeout) });
  return { response: r, via: 'direct' };
}

async function proxyAsset(req, res, rel) {
  const url = new URL(rel.split('/').map(encodeURIComponent).join('/'), PATCH_BASE);
  const range = req.headers.range;
  const headers = {};
  if (range) headers.range = range;

  let upstream;
  try {
    const { response, via } = await patchFetch(url, {
      headers,
      method: req.method === 'HEAD' ? 'HEAD' : 'GET',
      timeout: 25 * 1000,
    });
    upstream = response;
    if (via === 'direct' && proxyAgent) log(`patch 直连 ${rel}`);
  } catch (e) {
    // HEAD 在补丁站经常超时，直接当 404 让客户端走 Range 探测
    log(`patch ${req.method} 失败 ${rel}: ${e?.cause?.message ?? e.message}`);
    res.writeHead(req.method === 'HEAD' ? 405 : 502, { 'Accept-Ranges': 'bytes' }).end();
    return;
  }

  if (!upstream.ok && upstream.status !== 206) {
    res.writeHead(upstream.status === 404 ? 404 : 502).end(`upstream ${upstream.status}`);
    return;
  }

  const outHeaders = {
    'Content-Type': upstream.headers.get('content-type') ?? 'application/octet-stream',
    'Accept-Ranges': 'bytes',
    'Cache-Control': 'public, max-age=86400',
  };
  for (const h of ['content-length', 'content-range']) {
    const v = upstream.headers.get(h);
    if (v) outHeaders[h === 'content-length' ? 'Content-Length' : 'Content-Range'] = v;
  }

  // 后台缓存整份（HEAD 不带 body，用 content-range 的总长推断）
  if (outHeaders['Content-Length'] && upstream.status === 206) {
    const total = Number((outHeaders['Content-Range'] ?? '').split('/')[1] ?? 0);
    ensureCached(rel, total);
  } else if (outHeaders['Content-Length']) {
    ensureCached(rel, Number(outHeaders['Content-Length']));
  }

  res.writeHead(upstream.status, outHeaders);
  if (req.method === 'HEAD' || !upstream.body) {
    res.end();
    return;
  }
  try {
    await pipeline(Readable.fromWeb(upstream.body), res);
  } catch (e) {
    log(`patch 流中断 ${rel}: ${e.message}`);
    try { res.destroy(); } catch { /* ignore */ }
  }
}

function serveAsset(req, res, urlPath) {
  const rel = decodeURIComponent(urlPath.replace(/^\/assets\/?/, ''));
  const full = resolveAsset(urlPath);
  if (!full) {
    res.writeHead(403).end('forbidden');
    return;
  }

  let st = null;
  try {
    st = fs.statSync(full);
  } catch {
    st = null;
  }
  if (st && st.isDirectory()) {
    res.writeHead(404).end('not a file');
    return;
  }

  // 本地没有 → 看缓存 → 再不行就代理补丁站
  if (!st) {
    const cached = cachePathFor(rel);
    if (cached && fs.existsSync(cached)) {
      st = fs.statSync(cached);
      serveLocalFile(req, res, cached, st);
      return;
    }
    void proxyAsset(req, res, rel);
    return;
  }

  serveLocalFile(req, res, full, st);
}

function serveLocalFile(req, res, full, st) {
  const type = MIME[path.extname(full).toLowerCase()] ?? 'application/octet-stream';
  const range = req.headers.range;
  const headers = {
    'Content-Type': type,
    'Cache-Control': 'public, max-age=86400',
    'Accept-Ranges': 'bytes',
  };

  if (range) {
    const m = /bytes=(\d*)-(\d*)/.exec(range);
    let start = m && m[1] ? Number(m[1]) : 0;
    let end = m && m[2] ? Number(m[2]) : st.size - 1;
    if (Number.isNaN(start) || Number.isNaN(end) || start > end || start >= st.size) {
      res.writeHead(416, { 'Content-Range': `bytes */${st.size}` }).end();
      return;
    }
    end = Math.min(end, st.size - 1);
    res.writeHead(206, {
      ...headers,
      'Content-Range': `bytes ${start}-${end}/${st.size}`,
      'Content-Length': end - start + 1,
    });
    fs.createReadStream(full, { start, end }).pipe(res);
    return;
  }

  res.writeHead(200, { ...headers, 'Content-Length': st.size });
  if (req.method === 'HEAD') return res.end();
  fs.createReadStream(full).pipe(res);
}

const server = http.createServer((req, res) => {
  const url = req.url ?? '/';
  if (url === '/health' || url.startsWith('/health?')) {
    res.writeHead(200, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ ok: true, assets: ASSET_ROOT, game: `${GAME_HOST}:${GAME_PORT}` }));
    return;
  }
  if (url.startsWith('/assets')) {
    serveAsset(req, res, url);
    return;
  }
  res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
  res.end('mir2 bridge: use /ws for the game tunnel, /assets/** for game data');
});

const wss = new WebSocketServer({ server, path: '/ws', maxPayload: 16 * 1024 * 1024 });

wss.on('connection', (ws, req) => {
  const q = new URL(req.url ?? '/ws', 'http://localhost').searchParams;
  const host = q.get('host') || GAME_HOST;
  const port = Number(q.get('port') || GAME_PORT);
  const peer = `${req.socket.remoteAddress}`;

  log(`ws open  ${peer} -> tcp ${host}:${port}`);

  const tcp = net.connect({ host, port });
  tcp.setNoDelay(true);

  let open = true;
  const teardown = (why) => {
    if (!open) return;
    open = false;
    log(`ws close ${peer} (${why})`);
    try { tcp.destroy(); } catch {}
    try { ws.close(); } catch {}
  };

  // 浏览器 -> 服务端
  ws.on('message', (data, isBinary) => {
    const buf = isBinary ? data : Buffer.from(data);
    if (tcp.readyState === 'open') tcp.write(buf);
  });

  // 服务端 -> 浏览器
  tcp.on('data', (chunk) => {
    if (ws.readyState === ws.OPEN) ws.send(chunk, { binary: true });
  });

  tcp.on('connect', () => log(`tcp open ${peer} ${host}:${port}`));
  tcp.on('error', (e) => {
    log(`tcp error ${peer}: ${e.message}`);
    teardown('tcp-error');
  });
  tcp.on('close', () => teardown('tcp-close'));
  ws.on('close', () => teardown('ws-close'));
  ws.on('error', (e) => {
    log(`ws error ${peer}: ${e.message}`);
    teardown('ws-error');
  });
});

server.listen(HTTP_PORT, () => {
  log(`bridge listening on :${HTTP_PORT}`);
  log(`  ws      ws://127.0.0.1:${HTTP_PORT}/ws  ->  tcp ${GAME_HOST}:${GAME_PORT}`);
  log(`  assets  ${ASSET_ROOT}`);
  log(`  patch   ${PATCH_BASE}${PATCH_PROXY ? `  代理 ${PATCH_PROXY.replace(/\/\/[^@]*@/, '//***@')}` : '（未配代理）'}`);
});
