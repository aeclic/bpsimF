// Re-export the captured HDR lighting without running the demo again.
import {readFile,writeFile} from 'node:fs/promises';
const records=JSON.parse(await readFile('_codex_work/water-reference/draw-inputs.json','utf8')).records;
const surface=records.find(p=>p.kind==='WaterSurface'),vp=surface.uniforms['hlslcc_mtx4x4unity_MatrixVP[0]'],model=surface.uniforms['hlslcc_mtx4x4unity_ObjectToWorld[0]'];
const distance=vp[15]+model[14],cx=-vp[12]/vp[0]-model[12],cy=-vp[13]/vp[5],w=2*distance/vp[0],h=2*distance/vp[5];
const lighting={},base='Mods/LevelEditor/Web/bpsim/ultimate-water/';
for(const record of records) {
 const light=record.textures._ShapeLightTexture0;if(!light)continue;
 const file=record.kind+'.lighting.bin';await writeFile(base+file,Buffer.from(light.rgba,'base64'));
 lighting[record.kind]={file,width:light.width,height:light.height,type:light.type,rect:[cx-w/2,cy-h/2,w,h]};
}
await writeFile(base+'lighting-profile.json',JSON.stringify(lighting,null,2));
