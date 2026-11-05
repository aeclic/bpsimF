// Replay the actual demo's camera, time and capture textures through the port.
import {createRequire} from 'node:module';
import {readFile,writeFile} from 'node:fs/promises';
import path from 'node:path';
const require=createRequire('C:/Users/aecli/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/entry.cjs');
const {chromium}=require('playwright');
const directory=process.argv.includes('--interact')?'_codex_work/water-interaction':'_codex_work/water-reference';
const reference=JSON.parse(await readFile(directory+'/draw-inputs.json','utf8'));
const input=reference.records.filter(p=>p.kind!=='LightRays').map(({kind,uniforms,textures})=>({kind,uniforms,textures}));
const browser=await chromium.launch({executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',headless:true,args:['--use-angle=swiftshader','--enable-unsafe-swiftshader']});
try {
 const page=await browser.newPage({viewport:{width:1280,height:762}});
 await page.goto('http://127.0.0.1:8099/bpsim/ultimate-water/demo-profile.json');
 await page.addScriptTag({url:'/bpsim/ultimate-water-renderer.js'});await page.addScriptTag({url:'/bpsim/water-canvas.js'});
 const result=await page.evaluate(async records=>{
  const renderer=new UltimateWaterRenderer();await renderer.loading;if(!renderer.ready)throw Error(renderer.error);
  const gl=renderer.gl;gl.getExtension('OES_texture_float_linear');renderer.referenceTextures={};
  for(const record of records)renderer.runtime[record.kind]={...renderer.runtime[record.kind],...record.uniforms};
  const encode=x=>x<=.0031308?x*12.92:1.055*Math.pow(Math.max(x,0),1/2.4)-.055;
  let source;
  for(const record of records) {
   renderer.referenceTextures[record.kind]={};
   for(const [key,data]of Object.entries(record.textures)) {
    const bytes=Uint8Array.from(atob(data.rgba),c=>c.charCodeAt(0));
    let colors=data.type===5126?new Float32Array(bytes.buffer):Float32Array.from(bytes,x=>x/255);
    const texture=renderer.texture();gl.bindTexture(gl.TEXTURE_2D,texture);gl.pixelStorei(gl.UNPACK_FLIP_Y_WEBGL,false);
    gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_S,gl.CLAMP_TO_EDGE);gl.texParameteri(gl.TEXTURE_2D,gl.TEXTURE_WRAP_T,gl.CLAMP_TO_EDGE);
    gl.texImage2D(gl.TEXTURE_2D,0,gl.RGBA32F,data.width,data.height,0,gl.RGBA,gl.FLOAT,colors);
    renderer.referenceTextures[record.kind][key]=texture;
    if(key==='_CameraSortingLayerTexture') {
     source=document.createElement('canvas');source.width=data.width;source.height=data.height;
     const pixels=new Uint8ClampedArray(colors.length);
     for(let y=0;y<data.height;y++)for(let x=0;x<data.width*4;x++)pixels[y*data.width*4+x]=Math.round((x%4===3?colors[(data.height-y-1)*data.width*4+x]:encode(colors[(data.height-y-1)*data.width*4+x]))*255);
     source.getContext('2d').putImageData(new ImageData(pixels,data.width,data.height),0,0);
    }
   }
  }
  const surface=records.find(p=>p.kind==='WaterSurface'),u=surface.uniforms,vp=u['hlslcc_mtx4x4unity_MatrixVP[0]'],model=u['hlslcc_mtx4x4unity_ObjectToWorld[0]'];
  renderer.rippleCount=Number(u._RippleCount);renderer.updateRipples=()=>{};
  const distance=vp[15]+model[14],cameraX=(-vp[12]/vp[0]-model[12])/8,cameraY=-vp[13]/vp[5]/8;
  const camera={x:cameraX,y:cameraY,zoom:762*.5*vp[5]/distance*8};
  const body=bpsimWaterCanvas.migrate({id:'reference',name:'reference',x:-151.875/8,surface_y:0,width:303.75/8,depth:57.105667/8,wave_amplitude:0,wave_speed:0,color:'#00a9fd',opacity:1});
  renderer.draw([body],camera,{width:1280,height:762},source,[],u._Time[1],[],'Underwater');
  const display=document.createElement('canvas');display.width=1280;display.height=762;display.getContext('2d').drawImage(source,0,0);display.getContext('2d').drawImage(renderer.canvas,0,0);
  const pixels=new Uint8Array(renderer.canvas.width*renderer.canvas.height*4);
  gl.readPixels(0,0,renderer.canvas.width,renderer.canvas.height,gl.RGBA,gl.UNSIGNED_BYTE,pixels);
  let visible=0,left=renderer.canvas.width,top=renderer.canvas.height,right=-1,bottom=-1;
  for(let y=0;y<renderer.canvas.height;y++)for(let x=0;x<renderer.canvas.width;x++)if(pixels[(y*renderer.canvas.width+x)*4+3]>2){
   visible++;left=Math.min(left,x);right=Math.max(right,x);top=Math.min(top,y);bottom=Math.max(bottom,y);
  }
  return {png:display.toDataURL(),water:renderer.canvas.toDataURL(),source:source.toDataURL(),camera,time:u._Time[1],error:gl.getError(),
   output:{width:renderer.canvas.width,height:renderer.canvas.height,visiblePixels:visible,bounds:visible?[left,top,right,bottom]:null}};
 },input);
 await writeFile(directory+'/port-replay.png',Buffer.from(result.png.split(',')[1],'base64'));delete result.png;
 await writeFile(directory+'/port-water.png',Buffer.from(result.water.split(',')[1],'base64'));delete result.water;
 await writeFile(directory+'/port-source.png',Buffer.from(result.source.split(',')[1],'base64'));delete result.source;
 console.log(JSON.stringify(result));
}finally{await browser.close();}
