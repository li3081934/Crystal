import { PacketStream, send, type GamePacket } from './packets.ts';

export type ConnStatus = 'connecting' | 'open' | 'closed';

/**
 * WebSocket 连接（浏览器和 Node 都能用；Node 22 自带全局 WebSocket）。
 * 桥那头直接接裸 TCP 服务端，所以这里收到的就是原始协议字节流。
 */
export class GameSocket {
  private ws?: WebSocket;
  private stream = new PacketStream();
  private keepAliveTimer?: ReturnType<typeof setInterval>;

  onPacket?: (p: GamePacket) => void;
  onStatus?: (s: ConnStatus, info?: string) => void;
  /** 未识别/解析失败的包（调试用） */
  onUnknown?: (p: GamePacket) => void;

  get connected() {
    return this.ws?.readyState === 1;
  }

  connect(url: string) {
    this.close();
    this.stream.reset();
    this.onStatus?.('connecting');

    const ws = new WebSocket(url);
    ws.binaryType = 'arraybuffer';
    this.ws = ws;

    ws.onopen = () => {
      this.onStatus?.('open');
      // 服务端 5s 收不到数据会踢人：4s 一个 KeepAlive
      this.keepAliveTimer = setInterval(() => {
        if (this.connected) this.send(send.keepAlive(Date.now()));
      }, 4000);
    };

    ws.onmessage = (ev) => {
      const data = ev.data;
      if (data instanceof Blob) {
        void data.arrayBuffer().then((buf) => this.ingest(new Uint8Array(buf)));
        return;
      }
      this.ingest(new Uint8Array(data as ArrayBuffer));
    };

    ws.onclose = (ev) => {
      this.stopKeepAlive();
      this.onStatus?.('closed', ev.reason || `code ${ev.code}`);
    };
    ws.onerror = () => {
      /* onclose 会跟着来 */
    };
  }

  private ingest(bytes: Uint8Array) {
    if (!bytes.length) return;
    for (const p of this.stream.push(bytes)) {
      if (p.unknown || p.decodeError) this.onUnknown?.(p);
      else this.onPacket?.(p);
    }
  }

  send(bytes: Uint8Array) {
    if (!this.connected) return false;
    this.ws!.send(bytes);
    return true;
  }

  private stopKeepAlive() {
    if (this.keepAliveTimer) clearInterval(this.keepAliveTimer);
    this.keepAliveTimer = undefined;
  }

  close() {
    this.stopKeepAlive();
    const ws = this.ws;
    this.ws = undefined;
    if (ws && (ws.readyState === 0 || ws.readyState === 1)) {
      try {
        ws.close();
      } catch {
        /* ignore */
      }
    }
  }
}
