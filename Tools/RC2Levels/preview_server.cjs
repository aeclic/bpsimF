"use strict";
// Isolated read-only asset preview. Edits are only written under _codex_work/rc2-level-mod/preview-saves.
const http=require("node:http"),fs=require("node:fs"),path=require("node:path"),crypto=require("node:crypto");
const project=path.resolve(__dirname,"../.."),web=path.join(__dirname,"../../Mods/LevelEditor/Web/bpsim"),pack=path.join(project,"_codex_work/rc2-level-mod/original-pack");
const catalog=JSON.parse(fs.readFileSync(path.join(pack,"catalog.json"),"utf8")),saveRoot=path.resolve(process.env.RC2_PREVIEW_SAVE_ROOT||path.join(project,"_codex_work/rc2-level-mod/preview-saves"));
const scratch=path.join(project,"_codex_work/rc2-level-mod");
if(!saveRoot.startsWith(scratch+path.sep))throw new Error("Preview saves must remain in the RC2 scratch directory");
fs.mkdirSync(saveRoot,{recursive:true});let current=JSON.parse(fs.readFileSync(path.join(pack,catalog.levels.find(l=>l.mission==="Mission_1").path),"utf8"));
const json=(res,data,status=200)=>{res.writeHead(status,{"Content-Type":"application/json; charset=utf-8"});res.end(JSON.stringify(data));};
const port=Number(process.env.PORT||8173);
http.createServer((req,res)=> {
    const url=new URL(req.url,"http://127.0.0.1");
    if(url.pathname==="/api/session")return json(res,{ok:true,catalog,levels:catalog.levels.map(l=>({id:l.id,title:l.title})),document:current});
    if(url.pathname==="/api/runtime-status")return json(res,{ok:true,runtime_state:"preview_only"});
    if(url.pathname.startsWith("/api/asset/")){const [kind,key]=url.pathname.slice(11).split("/"),metadata=catalog.assets[kind]?.[key];if(!metadata)return json(res,{ok:false,error:"missing original asset"},404);res.writeHead(200,{"Content-Type":kind==="textures"?"image/png":"application/json"});fs.createReadStream(path.join(pack,metadata.path)).pipe(res);return;}
    if(url.pathname.startsWith("/api/")){let body="";req.on("data",b=>body+=b);req.on("end",()=>{try{const data=body?JSON.parse(body):{};if(url.pathname==="/api/open"){const id=data.id.replaceAll("-","");const item=catalog.levels.find(l=>l.id.replaceAll("-","")===id);if(!item)throw Error("unknown level");const saved=path.join(saveRoot,id+".rc2level");current=JSON.parse(fs.readFileSync(!data.original&&fs.existsSync(saved)?saved:path.join(pack,item.path),"utf8"));}else if(url.pathname==="/api/save"||url.pathname==="/api/play"){if(data.id!==current.id)throw Error("document identity changed");if(data.source.apk_sha256!==catalog.apk_sha256)throw Error("source APK changed");current=data;const id=current.id.replaceAll("-","");if(!/^[0-9a-f]{32}$/i.test(id))throw Error("invalid ID");fs.writeFileSync(path.join(saveRoot,id+".rc2level"),JSON.stringify(current),"utf8");}else if(url.pathname==="/api/import")current=data;return json(res,{ok:true,document:current,...(url.pathname==="/api/play"?{runtime_state:"preview_only"}: {})});}catch(e){json(res,{ok:false,error:e.message},400);}});return;}
    const file=path.resolve(web,"."+decodeURIComponent(url.pathname==="/"?"/index.html":url.pathname));if(!file.startsWith(web+path.sep))return json(res,{error:"forbidden"},403);
    if(!fs.existsSync(file))return json(res,{error:"not found"},404);const types={".html":"text/html; charset=utf-8",".js":"text/javascript; charset=utf-8",".css":"text/css; charset=utf-8"};res.writeHead(200,{"Content-Type":types[path.extname(file)]||"application/octet-stream"});fs.createReadStream(file).pipe(res);
}).listen(port,"127.0.0.1",()=>console.log("RC2_PREVIEW_READY http://127.0.0.1:"+port+"/#bpsim-token=isolated-preview"));
