import {readFile,writeFile} from 'node:fs/promises';
const records=JSON.parse(await readFile('_codex_work/water-reference/draw-inputs.json','utf8')).records;
const record=records.find(p=>p.kind==='WaterSurface'),attrs=Object.fromEntries(record.attributes.map(a=>[a.name,a.values]));
const count=attrs.in_POSITION0.length/3,data=new Float32Array(count*4),indices=new Uint16Array(record.indices);
for(let i=0;i<count;i++)data.set([attrs.in_POSITION0[i*3],attrs.in_POSITION0[i*3+2],...attrs.in_TEXCOORD0.slice(i*2,i*2+2)],i*4);
const base='Mods/LevelEditor/Web/bpsim/ultimate-water/';
await writeFile(base+'surface-mesh.bin',Buffer.concat([Buffer.from(data.buffer),Buffer.from(indices.buffer)]));
await writeFile(base+'surface-mesh.json',JSON.stringify({vertices:count,indices:indices.length,sourceWidth:303.75,sourceDepth:294.121521}));
