#!/usr/bin/env node
/**
 * 本地探针：不开浏览器，直接验证
 *   1. .Lib 解码（版本/索引/gzip 图片尺寸）
 *   2. .map 解码（格式/尺寸/几格数据）
 *   3. 协议编解码往返（C.Login 帧 -> 解析回来）
 *
 *   node --experimental-strip-types tools/smoke.ts [--net]
 *   --net 额外连一次真实服务端，跑完 握手->登录->选角->进图
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { Lib } from '../client/src/formats/lib.ts';
import { parseMap, detectFormat } from '../client/src/formats/map.ts';
import { PacketStream, decodePacket, frame as framePacket, send, ServerPacketIds } from '../client/src/net/packets.ts';

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const repo = path.resolve(__dirname, '..', '..');
const clientRoot = path.join(repo, 'Build', 'Client', 'Release');
const mapRoot = path.join(repo, 'Build', 'Server', 'Release', 'Maps');

function ok(msg: string) {
  console.log('  \x1b[32m✓\x1b[0m ' + msg);
}
function bad(msg: string) {
  console.log('  \x1b[31m✗\x1b[0m ' + msg);
}
function head(msg: string) {
  console.log('\n\x1b[1m' + msg + '\x1b[0m');
}

head('1) .Lib 解码');
const libFiles = [
  'Data/CArmour/00.Lib',
  'Data/CHair/03.Lib',
  'Data/CWeapon/00.Lib',
  'Data/Monster/000.Lib',
  'Data/Map/WemadeMir2/Tiles.Lib',
  'Data/Map/WemadeMir2/Smtiles.Lib',
  'Data/Map/WemadeMir2/Objects.Lib',
];
for (const rel of libFiles) {
  const file = path.join(clientRoot, rel);
  if (!fs.existsSync(file)) {
    bad(`${rel} 不存在`);
    continue;
  }
  const lib = Lib.fromBuffer(rel, new Uint8Array(fs.readFileSync(file)));
  await lib.ensureHeader();
  const firstMeta = await lib.meta([0, 1, 2, 5, 10, 100].find((i) => i < lib.count) ?? 0);
  let decoded = '未测试';
  for (let i = 0; i < Math.min(lib.count, 50); i++) {
    const px = await lib.pixels(i);
    if (px) {
      decoded = `${px.width}x${px.height} RGBA ${px.rgba.length}B`;
      break;
    }
  }
  ok(
    `${rel}: v${lib.version} ${lib.count} 张, frames=${lib.frames.size}, ` +
      `首图 ${firstMeta ? `${firstMeta.width}x${firstMeta.height}@(${firstMeta.x},${firstMeta.y})` : '?'}, 解压 ${decoded}`,
  );
}

head('2) .map 解码');
const mapNames = ['0.map', '0100.map', 'D001.map', '3.map'];
for (const name of mapNames) {
  const file = path.join(mapRoot, name);
  if (!fs.existsSync(file)) {
    bad(`${name} 不存在`);
    continue;
  }
  const buf = new Uint8Array(fs.readFileSync(file));
  try {
    const map = parseMap(buf);
    const sample = map.at(Math.floor(map.width / 2), Math.floor(map.height / 2))!;
    ok(
      `${name}: ${detectFormat(buf)} ${map.width}x${map.height}, ` +
        `中心格 back=[${sample.backIndex},${sample.backImage}] mid=[${sample.midIndex},${sample.midImage}] front=[${sample.frontIndex},${sample.frontImage}]`,
    );
  } catch (e) {
    bad(`${name}: ${e}`);
  }
}

head('3) 协议帧往返');
{
  const login = send.login('testacc', 'testpass');
  const stream = new PacketStream();
  const packets = stream.push(login.subarray(0, 3)).concat(stream.push(login.subarray(3)));
  const id = new DataView(login.buffer).getInt16(2, true);
  ok(`C.Login 帧 ${login.length}B, id=${id}, 拆成两块后解析出 ${packets.length} 个包（半包缓冲正常）`);

  // 服务端包：手工拼一个 ObjectWalk 验证解码
  const walkBody = new Uint8Array(4 + 4 + 4 + 1);
  const wv = new DataView(walkBody.buffer);
  wv.setUint32(0, 12345, true);
  wv.setInt32(4, 330, true);
  wv.setInt32(8, 270, true);
  wv.setUint8(12, 4);
  const walk = framePacket(ServerPacketIds.ObjectWalk, walkBody);
  const parsed = new PacketStream().push(walk)[0];
  ok(`S.ObjectWalk: ${parsed.name} id=${(parsed.data.objectId as number)} loc=${JSON.stringify(parsed.data.location)} dir=${parsed.data.direction}`);

  const unknown = framePacket(9999, new Uint8Array([1, 2, 3]));
  const u = new PacketStream().push(unknown)[0];
  ok(`未知包不致命: ${u.name} unknown=${u.unknown}`);

  void decodePacket;
}

if (process.argv.includes('--net')) {
  head('4) 真实服务端握手（需要先启动 Server.exe）');
  const { GameSocket } = await import('../client/src/net/socket.ts');
  const host = process.env.MIR2_HOST ?? '127.0.0.1';
  const port = Number(process.env.MIR2_PORT ?? 7000);
  const account = process.env.MIR2_ACCOUNT ?? 'webtest01';
  const password = process.env.MIR2_PASSWORD ?? 'webtest01';

  const sock = new GameSocket();
  sock.onUnknown = (p) => console.log(`    [unknown] ${p.name} ${p.decodeError ?? ''}`);
  const seen: string[] = [];
  const done = new Promise<void>((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('20s 内没走完流程')), 20000);
    let stage = 0;
    sock.onPacket = (p) => {
      seen.push(p.name);
      if (p.name === 'Connected') sock.send(send.clientVersion());
      else if (p.name === 'ClientVersion') sock.send(send.login(account, password));
      else if (p.name === 'Login') {
        const result = p.data.result as number;
        if (result !== 0 && result !== 3) {
          reject(new Error(`登录失败 result=${result}`));
          return;
        }
        if (result === 3) {
          console.log('    账号不存在，创建新账号…');
          sock.send(send.newAccount(account, password, account));
        } else {
          stage = 1;
        }
      } else if (p.name === 'NewAccount') {
        console.log('    建号结果', p.data.result);
        sock.send(send.login(account, password));
      } else if (p.name === 'LoginSuccess') {
        const chars = p.data.characters as Array<{ index: number; name: string; level: number; class: number }>;
        console.log(`    角色列表 ${chars.length} 个: ${chars.map((c) => `${c.name}(Lv${c.level},class${c.class})`).join(', ')}`);
        if (!chars.length) {
          const name = `Web${Math.floor(Math.random() * 100000)}`;
          console.log(`    自动建角 ${name}`);
          sock.send(send.newCharacter(name, 0, 0));
          return;
        }
        sock.send(send.startGame(chars[0].index));
      } else if (p.name === 'NewCharacter') {
        console.log('    建角结果', p.data.result);
        if (p.data.result !== 0) reject(new Error(`建角失败 result=${p.data.result}`));
      } else if (p.name === 'NewCharacterSuccess') {
        const character = p.data.character as { index: number; name: string };
        console.log(`    角色创建成功 ${character.name}（index=${character.index}）`);
        sock.send(send.startGame(character.index));
      } else if (p.name === 'StartGame') {
        console.log('    StartGame result =', p.data.result, 'resolution =', p.data.resolution);
      } else if (p.name === 'MapInformation') {
        console.log(`    进图: ${p.data.fileName} "${p.data.title}" ${p.data.miniMap ? '' : ''}`);
      } else if (p.name === 'UserInformation') {
        console.log(`    自身: ${p.name} Lv${p.data.level} at ${JSON.stringify(p.data.location)}`);
        stage = 2;
      } else if (p.name === 'ObjectPlayer' || p.name === 'ObjectMonster') {
        stage = Math.max(stage, 3);
      }
    };
    sock.onStatus = (s, info) => {
      if (s === 'open') sock.send(send.clientVersion());
      if (s === 'closed') reject(new Error(`连接关闭: ${info}`));
    };
    const poll = setInterval(() => {
      if (stage >= 3) {
        clearInterval(poll);
        clearTimeout(timer);
        resolve();
      }
    }, 200);
    void stage;
  });

  sock.connect(`ws://${host}:8787/ws`);
  await done;
  ok(`握手/登录/进图成功，收到包: ${[...new Set(seen)].slice(0, 25).join(' → ')}`);
  sock.close();
}

console.log('\n全部检查完成');
