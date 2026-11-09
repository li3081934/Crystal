import { defineConfig } from 'vite';

// 8787 = bridge (WS + assets). 浏览器里所有请求都走同源，避免 CORS / 混合内容问题。
const BRIDGE = process.env.MIR2_BRIDGE ?? 'http://127.0.0.1:8787';
// 前端 dev 端口（默认 5188，可用 MIR2_WEB_PORT 覆盖）。tools/ 下所有脚本都从这里取。
const PORT = Number(process.env.MIR2_WEB_PORT ?? 5188);

export default defineConfig({
  root: __dirname,
  server: {
    host: true, // 手机同局域网可直连 dev server
    port: PORT,
    strictPort: true, // 端口被占就直接报错，不要偷偷换成别的端口（脚本全按固定端口连）
    proxy: {
      '/ws': { target: BRIDGE.replace(/^http/, 'ws'), ws: true },
      '/assets': { target: BRIDGE },
      '/health': { target: BRIDGE },
    },
  },
  // `npm run preview` 也用同一个端口，代理规则一致
  preview: {
    host: true,
    port: PORT,
    strictPort: true,
    proxy: {
      '/ws': { target: BRIDGE.replace(/^http/, 'ws'), ws: true },
      '/assets': { target: BRIDGE },
      '/health': { target: BRIDGE },
    },
  },
  build: {
    outDir: '../dist',
    target: 'es2022',
  },
});
