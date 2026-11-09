/** 打印区域内 back 图层的图片尺寸（back 永远是左上角对齐，尺寸可能不是 96x64） */
import { parseMap } from '../client/src/formats/map.ts';
import { Lib } from '../client/src/formats/lib.ts';
import fs from 'node:fs';
const [x0, x1, y0, y1] = (process.argv[2] ?? '300,308,620,628').split(',').map(Number);
const LIB = ['Tiles', 'SmTiles', 'Objects', 'Objects2', 'Objects3', 'Objects4', 'Objects5', 'Objects6', 'Objects7', 'Objects8'];
const map = parseMap(new Uint8Array(fs.readFileSync('../Build/Server/Release/Maps/0.map')));
const libs = new Map();
const libOf = async (i) => {
  const n = LIB[i]; if (!n) return null;
  if (!libs.has(n)) libs.set(n, await Lib.open(`http://127.0.0.1:8787/assets/Data/Map/WemadeMir2/${n}.Lib`).catch(() => null));
  return libs.get(n);
};
for (let y = y0; y <= y1; y++) {
  const out = [];
  for (let x = x0; x <= x1; x++) {
    const c = map.at(x, y);
    if (x % 2 || y % 2 || !c || c.backImage === 0 || c.backIndex === -1) { out.push('-'); continue; }
    const lib = await libOf(c.backIndex);
    const idx = (c.backImage & 0x1fffffff) - 1;
    const meta = lib ? await lib.meta(idx) : null;
    out.push(`${LIB[c.backIndex]}#${idx}=${meta ? `${meta.width}x${meta.height}` : 'EMPTY'}`);
  }
  console.log(y, out.join('  '));
}
