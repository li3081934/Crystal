/**
 * .map 解码器（对照 Client/MirObjects/MapCode.cs）
 *
 * 服务端地图目录里 95% 是 wemade 2010 格式（type1），其余是 C# 自定义（type100）、
 * 老格式（type0/2/3）和 mir3（type5/6/7）。这里全部覆盖，未知格式抛错。
 *
 * 格子坐标索引：cells[x * height + y]（和 C# 的二维数组遍历顺序一致）
 */

export const CELL_WIDTH = 48;
export const CELL_HEIGHT = 32;

export interface Cell {
  backIndex: number;
  backImage: number;
  midIndex: number;
  midImage: number;
  frontIndex: number;
  frontImage: number;
  doorIndex: number;
  doorOffset: number;
  frontAnimFrame: number;
  frontAnimTick: number;
  midAnimFrame: number;
  midAnimTick: number;
  tileAnimImage: number;
  tileAnimOffset: number;
  tileAnimFrames: number;
  light: number;
}

export type MapFormat = 'type0' | 'type1' | 'type2' | 'type3' | 'type5' | 'type6' | 'type7' | 'type100';

export class GameMap {
  readonly width: number;
  readonly height: number;
  readonly format: MapFormat;
  readonly cells: Cell[];

  constructor(width: number, height: number, format: MapFormat) {
    this.width = width;
    this.height = height;
    this.format = format;
    this.cells = new Array(width * height);
    for (let i = 0; i < this.cells.length; i++) this.cells[i] = newCell();
  }

  at(x: number, y: number): Cell | null {
    if (x < 0 || y < 0 || x >= this.width || y >= this.height) return null;
    return this.cells[x * this.height + y];
  }

  inside(x: number, y: number) {
    return x >= 0 && y >= 0 && x < this.width && y < this.height;
  }
}

function newCell(): Cell {
  return {
    backIndex: 0,
    backImage: 0,
    midIndex: 1,
    midImage: 0,
    frontIndex: -1,
    frontImage: 0,
    doorIndex: 0,
    doorOffset: 0,
    frontAnimFrame: 0,
    frontAnimTick: 0,
    midAnimFrame: 0,
    midAnimTick: 0,
    tileAnimImage: 0,
    tileAnimOffset: 0,
    tileAnimFrames: 0,
    light: 0,
  };
}

export function detectFormat(b: Uint8Array): MapFormat {
  if (b[2] === 0x43 && b[3] === 0x23) return 'type100'; // "C#"
  if (b[0] === 0) return 'type5'; // wemade mir3：没有标题，直接空字节开头
  if (b[0] === 0x0f && b[5] === 0x53 && b[14] === 0x33) return 'type6'; // "(C) SNDA, MIR3."
  if (b[0] === 0x10 && b[2] === 0x61 && b[7] === 0x31 && b[14] === 0x31) return 'type1'; // Map 2010 Ver 1.0
  if (b[0] === 0x0d && b[1] === 0x4c && b[7] === 0x20 && b[11] === 0x6d) return 'type7';
  if (b[4] === 0x0f || (b[4] === 0x03 && b[18] === 0x0d && b[19] === 0x0a)) {
    const w = b[0] + (b[1] << 8);
    const h = b[2] + (b[3] << 8);
    return b.length > 52 + w * h * 14 ? 'type3' : 'type2';
  }
  return 'type0';
}

export function parseMap(buf: Uint8Array): GameMap {
  const format = detectFormat(buf);
  const dv = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  const u16 = (o: number) => dv.getUint16(o, true);
  const i16 = (o: number) => dv.getInt16(o, true);
  const i32 = (o: number) => dv.getInt32(o, true);

  switch (format) {
    case 'type100': {
      if (buf[0] !== 1 || buf[1] !== 0) throw new Error('type100: 只支持版本 1');
      const width = i16(4);
      const height = i16(6);
      const map = new GameMap(width, height, format);
      let off = 8;
      for (let x = 0; x < width; x++) {
        for (let y = 0; y < height; y++) {
          const c = map.cells[x * height + y];
          c.backIndex = i16(off); off += 2;
          c.backImage = i32(off); off += 4;
          c.midIndex = i16(off); off += 2;
          c.midImage = i16(off); off += 2;
          c.frontIndex = i16(off); off += 2;
          c.frontImage = i16(off); off += 2;
          c.doorIndex = buf[off++] & 0x7f;
          c.doorOffset = buf[off++];
          c.frontAnimFrame = buf[off++];
          c.frontAnimTick = buf[off++];
          c.midAnimFrame = buf[off++];
          c.midAnimTick = buf[off++];
          c.tileAnimImage = i16(off); off += 2;
          c.tileAnimOffset = i16(off); off += 2;
          c.tileAnimFrames = buf[off++];
          c.light = buf[off++];
        }
      }
      return map;
    }

    case 'type1': {
      let off = 21;
      const w = i16(off); off += 2;
      const xor = i16(off); off += 2;
      const h = i16(off);
      const width = w ^ xor;
      const height = h ^ xor;
      const map = new GameMap(width, height, format);
      off = 54;
      for (let x = 0; x < width; x++) {
        for (let y = 0; y < height; y++) {
          const c = map.cells[x * height + y];
          c.backIndex = 0;
          c.backImage = (i32(off) ^ 0xaa38aa38) | 0;
          off += 4;
          c.midIndex = 1;
          c.midImage = i16(off) ^ xor;
          off += 2;
          c.frontImage = i16(off) ^ xor;
          off += 2;
          c.doorIndex = buf[off++] & 0x7f;
          c.doorOffset = buf[off++];
          c.frontAnimFrame = buf[off++];
          c.frontAnimTick = buf[off++];
          c.frontIndex = buf[off++] + 2;
          c.light = buf[off++];
          off++; // unknown
        }
      }
      return map;
    }

    case 'type0': {
      const width = i16(0);
      const height = i16(2);
      const map = new GameMap(width, height, format);
      let off = 52;
      for (let x = 0; x < width; x++) {
        for (let y = 0; y < height; y++) {
          const c = map.cells[x * height + y];
          c.backIndex = 0;
          c.midIndex = 1;
          c.backImage = i16(off); off += 2;
          c.midImage = i16(off); off += 2;
          c.frontImage = i16(off); off += 2;
          c.doorIndex = buf[off++] & 0x7f;
          c.doorOffset = buf[off++];
          c.frontAnimFrame = buf[off++];
          c.frontAnimTick = buf[off++];
          c.frontIndex = buf[off++] + 2;
          c.light = buf[off++];
          if ((c.backImage & 0x8000) !== 0) c.backImage = ((c.backImage & 0x7fff) | 0x20000000) | 0;
        }
      }
      return map;
    }

    case 'type2': {
      const width = i16(0);
      const height = i16(2);
      const map = new GameMap(width, height, format);
      let off = 52;
      for (let x = 0; x < width; x++) {
        for (let y = 0; y < height; y++) {
          const c = map.cells[x * height + y];
          c.backImage = i16(off); off += 2;
          c.midImage = i16(off); off += 2;
          c.frontImage = i16(off); off += 2;
          c.doorIndex = buf[off++] & 0x7f;
          c.doorOffset = buf[off++];
          c.frontAnimFrame = buf[off++];
          c.frontAnimTick = buf[off++];
          c.frontIndex = buf[off++] + 120;
          c.light = buf[off++];
          c.backIndex = buf[off++] + 100;
          c.midIndex = buf[off++] + 110;
          if ((c.backImage & 0x8000) !== 0) c.backImage = ((c.backImage & 0x7fff) | 0x20000000) | 0;
        }
      }
      return map;
    }

    case 'type3': {
      const width = i16(0);
      const height = i16(2);
      const map = new GameMap(width, height, format);
      let off = 52;
      for (let x = 0; x < width; x++) {
        for (let y = 0; y < height; y++) {
          const c = map.cells[x * height + y];
          c.backImage = i16(off); off += 2;
          c.midImage = i16(off); off += 2;
          c.frontImage = i16(off); off += 2;
          c.doorIndex = buf[off++] & 0x7f;
          c.doorOffset = buf[off++];
          c.frontAnimFrame = buf[off++];
          c.frontAnimTick = buf[off++];
          c.frontIndex = buf[off++] + 120;
          c.light = buf[off++];
          c.backIndex = buf[off++] + 100;
          c.midIndex = buf[off++] + 110;
          c.tileAnimImage = i16(off); off += 2;
          off += 7;
          c.tileAnimFrames = buf[off++];
          c.tileAnimOffset = i16(off); off += 2;
          off += 14;
          if ((c.backImage & 0x8000) !== 0) c.backImage = ((c.backImage & 0x7fff) | 0x20000000) | 0;
        }
      }
      return map;
    }

    case 'type5': {
      let off = 20;
      off += 2; // attribute
      const width = i16(off); off += 2;
      const height = i16(off); off += 2;
      const map = new GameMap(width, height, format);
      let p = 28;
      for (let x = 0; x < Math.floor(width / 2); x++) {
        for (let y = 0; y < Math.floor(height / 2); y++) {
          for (let i = 0; i < 4; i++) {
            const c = map.cells[(x * 2 + (i % 2)) * height + (y * 2 + Math.floor(i / 2))];
            c.backIndex = buf[p] !== 255 ? buf[p] + 200 : -1;
            c.backImage = u16(p + 1) + 1;
          }
          p += 3;
        }
      }
      p = 28 + 3 * (Math.floor(width / 2) + (width % 2)) * Math.floor(height / 2);
      for (let x = 0; x < width; x++) {
        for (let y = 0; y < height; y++) {
          const c = map.cells[x * height + y];
          const flag = buf[p++];
          c.midAnimFrame = buf[p++];
          c.frontAnimFrame = buf[p] === 255 ? 0 : buf[p];
          c.frontAnimFrame &= 0x8f;
          p++;
          c.midAnimTick = 0;
          c.frontAnimTick = 0;
          c.frontIndex = buf[p] !== 255 ? buf[p] + 200 : -1;
          p++;
          c.midIndex = buf[p] !== 255 ? buf[p] + 200 : -1;
          p++;
          c.midImage = u16(p) + 1; p += 2;
          c.frontImage = u16(p) + 1;
          if (c.frontImage === 1 && c.frontIndex === 200) c.frontIndex = -1;
          p += 2;
          p += 3;
          c.light = buf[p] & 0x0f;
          p += 2;
          if ((flag & 0x01) !== 1) c.backImage |= 0x20000000;
          if ((flag & 0x02) !== 2) c.frontImage = (c.frontImage | 0x8000) & 0xffff;
        }
      }
      return map;
    }

    case 'type6': {
      let off = 16;
      const width = i16(off); off += 2;
      const height = i16(off);
      const map = new GameMap(width, height, format);
      let p = 40;
      for (let x = 0; x < width; x++) {
        for (let y = 0; y < height; y++) {
          const c = map.cells[x * height + y];
          const flag = buf[p++];
          c.backIndex = buf[p] !== 255 ? buf[p] + 300 : -1; p++;
          c.midIndex = buf[p] !== 255 ? buf[p] + 300 : -1; p++;
          c.frontIndex = buf[p] !== 255 ? buf[p] + 300 : -1; p++;
          c.backImage = i16(p) + 1; p += 2;
          c.midImage = i16(p) + 1; p += 2;
          c.frontImage = i16(p) + 1; p += 2;
          if (c.frontImage === 1 && c.frontIndex === 200) c.frontIndex = -1;
          c.midAnimFrame = buf[p++];
          c.frontAnimFrame = buf[p] === 255 ? 0 : buf[p];
          if (c.frontAnimFrame > 0x0f) c.frontAnimFrame &= 0x0f;
          p++;
          c.midAnimTick = 1;
          c.frontAnimTick = 1;
          c.light = (buf[p] & 0x0f) * 4;
          p += 8;
          if ((flag & 0x01) !== 1) c.backImage |= 0x20000000;
          if ((flag & 0x02) !== 2) c.frontImage = (c.frontImage | 0x8000) & 0xffff;
        }
      }
      return map;
    }

    case 'type7': {
      let off = 21;
      const width = i16(off);
      off += 4;
      const height = i16(off);
      const map = new GameMap(width, height, format);
      let p = 54;
      for (let x = 0; x < width; x++) {
        for (let y = 0; y < height; y++) {
          const c = map.cells[x * height + y];
          c.backIndex = 0;
          c.backImage = i32(p);
          p += 4;
          c.midIndex = 1;
          c.midImage = i16(p); p += 2;
          c.frontImage = i16(p); p += 2;
          c.doorIndex = buf[p++] & 0x7f;
          c.doorOffset = buf[p++];
          c.frontAnimFrame = buf[p++];
          c.frontAnimTick = buf[p++];
          c.frontIndex = buf[p++] + 2;
          c.light = buf[p++];
          p++; // unknown
          if ((c.backImage & 0x8000) !== 0) c.backImage = ((c.backImage & 0x7fff) | 0x20000000) | 0;
        }
      }
      return map;
    }

    default:
      throw new Error(`不支持的地图格式: ${format}`);
  }
}

/**
 * MapLibs 索引 -> 资源路径（照抄 Client/MirGraphics/MLibrary.cs 的静态构造）
 */
export function mapLibPath(index: number): string | null {
  const wemade = (name: string) => `Data/Map/WemadeMir2/${name}.Lib`;
  const shanda = (name: string) => `Data/Map/ShandaMir2/${name}.Lib`;
  const wemade3 = (sub: string) => `Data/Map/WemadeMir3/${sub}.Lib`;
  const shanda3 = (sub: string) => `Data/Map/ShandaMir3/${sub}.Lib`;

  if (index < 0) return null;

  // wemade mir2: 0-99
  if (index <= 99) {
    if (index === 0) return wemade('Tiles');
    if (index === 1) return wemade('SmTiles');
    if (index === 2) return wemade('Objects');
    if (index >= 3 && index <= 28) return wemade(`Objects${index - 1}`);
    if (index === 90) return wemade('Objects_32bit');
    return null;
  }

  // shanda mir2: 100-199
  if (index <= 199) {
    if (index === 100) return shanda('Tiles');
    if (index >= 101 && index <= 109) return shanda(`Tiles${index - 99}`);
    if (index === 110) return shanda('SmTiles');
    if (index >= 111 && index <= 119) return shanda(`SmTiles${index - 109}`);
    if (index === 120) return shanda('Objects');
    if (index >= 121 && index <= 150) return shanda(`Objects${index - 119}`);
    if (index === 190) return shanda('AniTiles1');
    return null;
  }

  // wemade mir3: 200-299
  if (index <= 299) {
    const suffixes = [
      'Tilesc', 'Tiles30c', 'Tiles5c', 'SmTilesc', 'Housesc', 'Cliffsc', 'Dungeonsc',
      'Innersc', 'Furnituresc', 'Wallsc', 'smObjectsc', 'Animationsc', 'Object1c', 'Object2c',
    ];
    const dirs = ['', 'wood\\', 'sand\\', 'snow\\', 'forest\\'];
    const grp = Math.floor((index - 200) / 15);
    const slot = (index - 200) % 15;
    if (slot < suffixes.length && grp < dirs.length) {
      return wemade3(`${dirs[grp]}${suffixes[slot]}`);
    }
    return null;
  }

  // shanda mir3: 300-399
  const grp = Math.floor((index - 300) / 15);
  const slot = (index - 300) % 15;
  const suffixes = [
    'Tilesc', 'Tiles30c', 'Tiles5c', 'SmTilesc', 'Housesc', 'Cliffsc', 'Dungeonsc',
    'Innersc', 'Furnituresc', 'Wallsc', 'smObjectsc', 'Animationsc', 'Object1c', 'Object2c',
  ];
  const names = ['', 'wood', 'sand', 'snow', 'forest'];
  if (slot < suffixes.length && grp < names.length) {
    return shanda3(`${suffixes[slot]}${names[grp]}`);
  }
  return null;
}
