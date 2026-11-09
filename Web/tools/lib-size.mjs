import { Lib } from '../client/src/formats/lib.ts';
const [libName, ...ids] = process.argv.slice(2);
const lib = await Lib.open(`http://127.0.0.1:8787/assets/Data/Map/WemadeMir2/${libName}`);
for (const i of ids.map(Number)) {
  const m = await lib.meta(i);
  console.log(i, m ? `${m.width}x${m.height}` : 'EMPTY');
}
