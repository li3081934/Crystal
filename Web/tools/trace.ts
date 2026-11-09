#!/usr/bin/env node
/**
 * 抓包器：连服务端，走完 握手/登录/进图，把收到包按顺序打印（排查用）
 *   node --experimental-strip-types tools/trace.ts
 */
import { GameSocket } from '../client/src/net/socket.ts';
import { send } from '../client/src/net/packets.ts';

const account = process.env.MIR2_ACCOUNT ?? 'webdemo';
const password = process.env.MIR2_PASSWORD ?? 'webdemo123';
const sock = new GameSocket();
const counts = new Map<string, number>();

sock.onPacket = (p) => {
  counts.set(p.name, (counts.get(p.name) ?? 0) + 1);
  switch (p.name) {
    case 'Connected':
      sock.send(send.clientVersion());
      break;
    case 'ClientVersion':
      sock.send(send.login(account, password));
      break;
    case 'Login':
      console.log('Login result', p.data.result);
      if (p.data.result === 3) sock.send(send.newAccount(account, password, account));
      break;
    case 'NewAccount':
      console.log('NewAccount result', p.data.result);
      if (p.data.result === 8) sock.send(send.login(account, password));
      break;
    case 'LoginSuccess': {
      const chars = p.data.characters as Array<{ index: number; name: string }>;
      console.log('LoginSuccess chars', chars.length);
      if (!chars.length) sock.send(send.newCharacter(`Web${Math.floor(Math.random() * 99999)}`, 0, 0));
      else sock.send(send.startGame(chars[0].index));
      break;
    }
    case 'NewCharacterSuccess': {
      const c = p.data.character as { index: number };
      sock.send(send.startGame(c.index));
      break;
    }
    case 'MapInformation':
      console.log('MapInformation', p.data.fileName, p.data.title);
      break;
    case 'UserInformation':
      console.log('UserInformation', p.data.name, p.data.level, JSON.stringify(p.data.location));
      break;
    default:
      break;
  }
};
sock.onUnknown = (p) => {
  const key = `${p.name}${p.decodeError ? '(decode-error)' : ''}`;
  counts.set(key, (counts.get(key) ?? 0) + 1);
};

sock.connect('ws://127.0.0.1:8787/ws');
setTimeout(() => {
  console.log('\n--- 包统计 ---');
  for (const [k, v] of [...counts.entries()].sort((a, b) => b[1] - a[1])) console.log(String(v).padStart(6), k);
  sock.close();
  process.exit(0);
}, 12000);
