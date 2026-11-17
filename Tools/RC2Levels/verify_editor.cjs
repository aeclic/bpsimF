"use strict";
// Command: node Tools/RC2Levels/verify_editor.cjs
// PASS: real web UI opens/saves/reopens 17 levels; pixel-upload oracle, prefab/vertex
// edits and undo pass; no JS/WebGL errors; originals unchanged. State/calibration
// precede pointer actions. Not a test of APK frames or the host HtmlPanelView bridge.
// Invalidated by web/document/API, browser, source pack or PNG-oracle changes.
const fs=require("node:fs"),path=require("node:path"),assert=require("node:assert/strict"),crypto=require("node:crypto"),{spawn}=require("node:child_process");
const project=path.resolve(__dirname,"../.."),work=path.join(project,"_codex_work/rc2-level-mod"),pack=path.join(work,"original-pack");
const catalog=JSON.parse(fs.readFileSync(path.join(pack,"catalog.json"),"utf8"));
const textureReference=JSON.parse(fs.readFileSync(path.join(work,"texture-reference.json"),"utf8"));
assert.equal(textureReference.apk_sha256,catalog.apk_sha256);
const runId=Date.now().toString(36),saveRoot=path.join(work,"editor-qa-saves-"+runId),profile=path.join(work,"editor-qa-chrome-"+runId);
const port=18000+Math.floor(Math.random()*1000),cdpPort=port+2000;
const report={pass:false,mode:process.argv.includes("--quick")?"quick":"full",levels:[],browserErrors:[],pixelChecks:0,roundtrips:0,actions:[],boundary:"web editor and original PNG upload; not APK dynamic/frame parity or host HtmlPanelView"};
const output=path.join(work,"editor-verification.json"),sleep=ms=>new Promise(r=>setTimeout(r,ms));
const sha=file=>crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
const originals=new Map(["catalog.json",...catalog.levels.map(l=>l.path),...Object.values(catalog.backgrounds).map(l=>l.path),...Object.values(catalog.assets).flatMap(k=>Object.values(k).map(m=>m.path))].map(p=>[p,sha(path.join(pack,p))]));
let browser,server,socket,send,evaluate;
async function wait(fn,label,timeout=45000){const start=Date.now();while(Date.now()-start<timeout){if(await fn())return;await sleep(100);}throw Error("Timeout: "+label);}
async function shot(name){const r=await send("Page.captureScreenshot",{format:"png",captureBeyondViewport:false});fs.writeFileSync(path.join(work,name),Buffer.from(r.data,"base64"));}
async function click(id){await evaluate(`document.getElementById(${JSON.stringify(id)}).click()`);await wait(()=>evaluate("!window.rc2Editor.state.busy"),"UI action "+id);}
async function open(level){await evaluate(`document.getElementById('levels').value=${JSON.stringify(level.id)};document.getElementById('open').click()`);await wait(()=>evaluate(`window.rc2Editor.state.ready&&!window.rc2Editor.state.busy&&window.rc2Editor.state.document.source.mission===${JSON.stringify(level.mission)}`),"open "+level.mission);}
async function documentHash(){return evaluate("(async()=>{const text=JSON.stringify(window.rc2Editor.exportDocument()),bytes=await crypto.subtle.digest('SHA-256',new TextEncoder().encode(text));return [...new Uint8Array(bytes)].map(b=>b.toString(16).padStart(2,'0')).join('');})()");}
const jsonHash=value=>crypto.createHash('sha256').update(JSON.stringify(value)).digest('hex');
async function property(label,value,index=0){await evaluate(`(()=>{const row=[...document.querySelectorAll('#properties .property')].find(r=>r.children[0]?.textContent===${JSON.stringify(label)});if(!row)throw Error('Missing property '+${JSON.stringify(label)});const input=row.querySelectorAll('input')[${index}];if(!input)throw Error('Missing property input');if(input.type==='checkbox')input.checked=${JSON.stringify(value)};else input.value=${JSON.stringify(value)};input.dispatchEvent(new Event('change',{bubbles:true}));})()`);}
async function pixelChecks(){const result=await evaluate(`(()=>{const e=window.rc2Editor,r=e.renderer,gl=r.gl,refs=${JSON.stringify(textureReference.textures)},fbo=gl.createFramebuffer(),checks=[];let count=0;gl.bindFramebuffer(gl.FRAMEBUFFER,fbo);try{for(const [key,texture] of r.textures){if(!key||!refs[key])continue;const meta=refs[key];gl.framebufferTexture2D(gl.FRAMEBUFFER,gl.COLOR_ATTACHMENT0,gl.TEXTURE_2D,texture,0);if(gl.checkFramebufferStatus(gl.FRAMEBUFFER)!==gl.FRAMEBUFFER_COMPLETE)throw Error('Incomplete texture FBO '+key);for(const [x,y,expected]of meta.samples){const actual=new Uint8Array(4);gl.readPixels(x,meta.height-1-y,1,1,gl.RGBA,gl.UNSIGNED_BYTE,actual);count++;if(actual.some((v,i)=>v!==expected[i])){checks.push({key,x,y,expected,actual:[...actual]});if(checks.length===3)return {count,differences:checks};}}}return {count,differences:checks};}finally{gl.bindFramebuffer(gl.FRAMEBUFFER,null);gl.deleteFramebuffer(fbo);}})()`);report.pixelChecks+=result.count;assert.deepEqual(result.differences,[],"PNG/WebGL pixel orientation mismatch: "+JSON.stringify(result.differences));}
async function advanced(){
    const mission=catalog.levels.find(l=>l.mission==="Mission_1");await open(mission);
    await evaluate("window.rc2Editor.state.dirty=false;document.getElementById('original').click()");
    await wait(()=>evaluate("window.rc2Editor.state.ready&&!window.rc2Editor.state.busy"),"original reset");
    // Select a real, visible static mesh that has another shared-prefab instance.
    const selected=await evaluate(`(()=>{const e=window.rc2Editor,s=e.state.document.scene,counts={};for(const n of s.nodes)for(const r of n.renderers)counts[r.prefab]=(counts[r.prefab]||0)+1;const index=s.nodes.findIndex(n=>n.active&&!n.background&&!n.fluid&&!n.text&&n.renderers.some(r=>r.enabled&&!r.bones?.length&&s.prefabs[r.prefab].mesh&&counts[r.prefab]>1));if(index<0)throw Error('No shared static source mesh');e.select(index);e.renderer.focus(index);e.state.renderDirty=true;return {index,id:s.nodes[index].id,prefab:s.nodes[index].renderers[0].prefab};})()`);
    await click("vertices");await sleep(150);
    const calibration=await evaluate(`(()=>{const e=window.rc2Editor,r=e.renderer;r.render();const item=r.items.find(i=>i.index===e.state.selected),rect=r.canvas.getBoundingClientRect();if(!item)throw Error('Selected mesh not rendered');const p=item.positions.slice(0,3),s=r.screen(p),event={clientX:rect.left+s.x,clientY:rect.top+s.y},hit=r.vertex(event);return {canvas:{left:rect.left,top:rect.top,width:rect.width,height:rect.height},world:p,screen:event,hit:hit?.index,selected:e.state.selected,meshVertex:0,camera:{...r.camera}};})()`);
    report.calibration=calibration;assert.equal(calibration.selected,selected.index);assert.equal(calibration.hit,0,"Coordinate calibration must pick the observed source vertex");
    assert(calibration.screen.clientX>=calibration.canvas.left&&calibration.screen.clientX<calibration.canvas.left+calibration.canvas.width);
    assert(calibration.screen.clientY>=calibration.canvas.top&&calibration.screen.clientY<calibration.canvas.top+calibration.canvas.height);
    await shot("editor-calibration.png");
    // Pointer selection, then the real inspector's independent/shared mesh editing.
    const {clientX:x,clientY:y}=calibration.screen;
    await send("Input.dispatchMouseEvent",{type:"mousePressed",x,y,button:"left",clickCount:1});
    await send("Input.dispatchMouseEvent",{type:"mouseReleased",x,y,button:"left",clickCount:1});
    assert.equal(await evaluate("window.rc2Editor.state.vertex"),0);
    const originalVertex=await evaluate(`(()=>{const e=window.rc2Editor,n=e.state.document.scene.nodes[e.state.selected],p=e.state.document.scene.prefabs[n.renderers[0].prefab];return e.renderer.json.get('meshes:'+p.mesh).vertices[0];})()`);
    await property("顶点位置",originalVertex+.375);const independent=await evaluate(`(()=>{const e=window.rc2Editor,s=e.state.document.scene,r=s.nodes[e.state.selected].renderers[0];return {override:r.mesh_override,value:s.mesh_overrides[r.mesh_override].vertices[0]};})()`);assert.equal(independent.value,originalVertex+.375);
    await click("undo");assert.equal(await evaluate("!!window.rc2Editor.state.document.scene.nodes[window.rc2Editor.state.selected].renderers[0].mesh_override"),false);
    await click("redo");assert.equal(await evaluate("!!window.rc2Editor.state.document.scene.nodes[window.rc2Editor.state.selected].renderers[0].mesh_override"),true);
    await click("undo");await property("编辑共享预制体（影响全部引用实例）",true);
    // Checkbox refresh resets the inspector; select the vertex again through the UI.
    await send("Input.dispatchMouseEvent",{type:"mousePressed",x,y,button:"left",clickCount:1});await send("Input.dispatchMouseEvent",{type:"mouseReleased",x,y,button:"left",clickCount:1});
    await property("顶点位置",originalVertex+.625);
    const shared=await evaluate(`(()=>{const e=window.rc2Editor,s=e.state.document.scene,r=s.nodes[e.state.selected].renderers[0],p=s.prefabs[r.prefab];return {key:p.mesh_override,instances:s.nodes.flatMap(n=>n.renderers).filter(q=>q.prefab===r.prefab).length,value:s.mesh_overrides[p.mesh_override].vertices[0]};})()`);
    assert(shared.instances>1);assert.equal(shared.value,originalVertex+.625);report.actions.push({action:"independent/shared mesh + undo/redo",...shared});
    const before=await evaluate("window.rc2Editor.state.document.scene.nodes.length");await click("duplicate");const after=await evaluate("window.rc2Editor.state.document.scene.nodes.length");assert(after>before);await click("undo");assert.equal(await evaluate("window.rc2Editor.state.document.scene.nodes.length"),before);report.actions.push({action:"duplicate full subtree + undo",addedNodes:after-before});
    // Waterfall editor field is independent of ordinary fluids and both border layers.
    const water=await evaluate(`(()=>{const e=window.rc2Editor,s=e.state.document.scene,i=s.nodes.findIndex(n=>n.renderers.some(r=>r.waterfall?.role==='water'));e.select(i);return {index:i,speed:s.nodes[i].renderers[0].waterfall.scroll_speed,layers:s.nodes.flatMap(n=>n.renderers).filter(r=>r.waterfall).map(r=>({...r.waterfall}))};})()`);
    await property("原始 UV 滚动速度",water.speed+.125);const layers=await evaluate("window.rc2Editor.state.document.scene.nodes.flatMap(n=>n.renderers).filter(r=>r.waterfall).map(r=>({...r.waterfall}))");assert.equal(layers.length,3);assert.equal(layers.filter(l=>l.role==='water')[0].scroll_speed,water.speed+.125);assert.deepEqual(layers.filter(l=>l.role==='border'),water.layers.filter(l=>l.role==='border'));report.actions.push({action:"separate waterfall water/border parameters",layers:3});
    await click("save");const expected=await documentHash();await open(mission);assert.equal(await documentHash(),expected);report.actions.push({action:"shared mesh and waterfall save/reopen"});
    await click("spawn");await shot("editor-mission-1.png");
    const beforeInvalid=await documentHash();
    await evaluate(`(()=>{const e=window.rc2Editor,i=e.state.document.scene.nodes.findIndex(n=>n.renderers.some(r=>r.waterfall));e.select(i);document.getElementById('edit-json').click();const value=JSON.parse(document.getElementById('json-value').value);value.parent=i;document.getElementById('json-value').value=JSON.stringify(value);document.getElementById('json-apply').click();})()`);
    assert.equal(await documentHash(),beforeInvalid,"Rejected JSON node edit mutated the document");
    assert(await evaluate("document.getElementById('status').classList.contains('error')"));
    report.actions.push({action:"invalid JSON parent is rejected without document mutation"});
    await click("json-cancel");
    await evaluate("[...document.querySelectorAll('#properties button')].find(b=>b.textContent==='查看 APK 原始组件（只读存档）').click()");
    assert(await evaluate("document.getElementById('json-value').readOnly&&document.getElementById('json-apply').disabled"));
    assert((await evaluate("JSON.parse(document.getElementById('json-value').value).components.length"))>0);await click("json-cancel");
    report.actions.push({action:"full native source components are viewable/read-only"});
    await click("play");assert((await evaluate("document.getElementById('status').textContent")).includes("未实际载入 Unity"));
    report.actions.push({action:"preview does not claim a successful Unity load"});
    await shot("editor-mission-1.png");
}
async function representativeViews(){
    report.representativeViews=[];
    for(const mission of ['Mission_1','Mission_4','Mission_2']){
        await open(catalog.levels.find(l=>l.mission===mission));await click('original');
        const view=await evaluate(`(()=>{const e=window.rc2Editor,r=e.renderer;r.animate=false;r.vertexMode=false;r.wireframe=false;r.render();const skeletal=${JSON.stringify(mission==='Mission_2')};const item=r.items.find(i=>skeletal?i.renderer.bones?.length:i.renderer.waterfall?.role==='water');if(!item)throw Error('No representative visible source renderer');e.select(item.index);r.focus(item.index);r.render();return {mission:e.state.document.source.mission,selected:e.state.document.scene.nodes[item.index].name,role:skeletal?'source skinned mesh':'separate waterfall',waterfallLayers:e.state.document.scene.nodes.flatMap(n=>n.renderers).filter(r=>r.waterfall).length,stats:r.stats};})()`);
        assert.equal(view.stats.errors,0);if(mission!=='Mission_2')assert.equal(view.waterfallLayers,3);
        await shot('editor-'+mission+'.png');report.representativeViews.push(view);
    }
}
async function modeIntegration(){
    const state=await evaluate(`(()=>{const params=new URLSearchParams(location.hash.slice(1)),style=getComputedStyle(document.documentElement);return {returnUrl:params.get('bpsim-return'),levelDisabled:document.getElementById('mode-level').disabled,rc2Selected:document.getElementById('mode-rc2').getAttribute('aria-selected'),panel:style.getPropertyValue('--panel').trim(),edge:style.getPropertyValue('--edge').trim(),accent:style.getPropertyValue('--accent').trim()};})()`);
    assert.equal(state.returnUrl,report.levelEditorReturnUrl);assert.equal(state.levelDisabled,false);assert.equal(state.rc2Selected,'true');
    assert.deepEqual({panel:state.panel,edge:state.edge,accent:state.accent},{panel:'#202b30',edge:'#405259',accent:'#85bc8c'});
    const before=await evaluate('location.href');await evaluate('window.rc2Editor.state.dirty=true');await click('mode-level');
    const blocked=await evaluate(`({url:location.href,dirty:window.rc2Editor.state.dirty,status:document.getElementById('status').textContent})`);
    assert.equal(blocked.url,before);assert.equal(blocked.dirty,true);assert.match(blocked.status,/保存 RC2 修改/);
    report.modeIntegration={returnUrlParsed:state.returnUrl,blockedUnsavedReturn:true,theme: {panel:state.panel,edge:state.edge,accent:state.accent}};
    report.actions.push({action:'RC2 editor mode styling, return target and unsaved-change guard'});
    await evaluate('window.rc2Editor.state.dirty=false');
}
async function main(){
    server=spawn(process.execPath,[path.join(__dirname,"preview_server.cjs")],{cwd:project,windowsHide:true,stdio:"ignore",env:{...process.env,PORT:String(port),RC2_PREVIEW_SAVE_ROOT:saveRoot}});
    await wait(async()=>{try{return (await fetch(`http://127.0.0.1:${port}/api/session`)).ok;}catch{return false;}},"isolated preview server",10000);
    browser=spawn("C:/Program Files/Google/Chrome/Application/chrome.exe",["--headless=new","--hide-scrollbars","--force-device-scale-factor=1","--mute-audio","--remote-debugging-port="+cdpPort,"--remote-debugging-address=127.0.0.1","--user-data-dir="+profile,"--no-first-run","--no-default-browser-check","--enable-unsafe-swiftshader","about:blank"],{cwd:work,windowsHide:true,stdio:"ignore"});
    await wait(async()=>{try{return (await fetch(`http://127.0.0.1:${cdpPort}/json/version`)).ok;}catch{return false;}},"task-owned browser",20000);
    const target=await (await fetch(`http://127.0.0.1:${cdpPort}/json/new?about:blank`,{method:"PUT"})).json();socket=new WebSocket(target.webSocketDebuggerUrl);let next=0;const pending=new Map();
    socket.addEventListener("message",event=>{const m=JSON.parse(event.data);if(m.id&&pending.has(m.id)){const {resolve,reject,timer}=pending.get(m.id);clearTimeout(timer);pending.delete(m.id);if(m.error)reject(Error(JSON.stringify(m.error)));else resolve(m.result);}else if(m.method==="Runtime.exceptionThrown")report.browserErrors.push(m.params.exceptionDetails.text+": "+(m.params.exceptionDetails.exception?.description||""));else if(m.method==="Page.javascriptDialogOpening"){const known=["重新打开此关卡的 APK 原始文档？已保存修改不会删除。","放弃尚未保存的修改并打开另一关卡？"].includes(m.params.message);if(!known)report.browserErrors.push("Unexpected dialog: "+m.params.message);send("Page.handleJavaScriptDialog",{accept:known}).catch(e=>report.browserErrors.push(e.message));}});
    await new Promise(r=>socket.addEventListener("open",r));send=(method,params={})=>new Promise((resolve,reject)=>{const id=++next,timer=setTimeout(()=>{pending.delete(id);reject(Error("CDP timeout: "+method));},60000);pending.set(id,{resolve,reject,timer});socket.send(JSON.stringify({id,method,params}));});
    socket.addEventListener('close',event=>{report.socketClose={code:event.code,reason:event.reason};for(const {reject,timer}of pending.values()){clearTimeout(timer);reject(Error('CDP socket closed: '+event.code+' '+event.reason));}pending.clear();});
    evaluate=async expression=>{report.lastEvaluation=expression.slice(0,180);const result=await send("Runtime.evaluate",{expression,awaitPromise:true,returnByValue:true});if(result.exceptionDetails)throw Error(result.exceptionDetails.exception?.description||result.exceptionDetails.text);return result.result.value;};
    const levelEditorReturnUrl=`http://127.0.0.1:${port}/#bpsim-token=isolated-preview&mode=level-editor`;
    report.levelEditorReturnUrl=levelEditorReturnUrl;
    const rc2Hash=new URLSearchParams({'bpsim-token':'isolated-preview','bpsim-return':levelEditorReturnUrl});
    await send("Page.enable");await send("Runtime.enable");await send("Emulation.setDeviceMetricsOverride",{width:1600,height:1000,deviceScaleFactor:1,mobile:false});await send("Page.navigate",{url:`http://127.0.0.1:${port}/#${rc2Hash}`});
    await wait(()=>evaluate("!!window.rc2Editor?.state.ready"),"real editor initialization");report.initial=await evaluate("({mission:window.rc2Editor.state.document.source.mission,treeRows:document.querySelectorAll('#tree .tree-row').length,nodes:window.rc2Editor.state.document.scene.nodes.length,stats:window.rc2Editor.renderer.stats})");assert.equal(report.initial.treeRows,report.initial.nodes);
    await shot("editor-boot.png");await pixelChecks();
    for(const level of process.argv.includes("--quick")?[]:catalog.levels){await open(level);const state=await evaluate("(()=>{const e=window.rc2Editor; e.renderer.animate=false;e.renderer.render();return {mission:e.state.document.source.mission,nodes:e.state.document.scene.nodes.length,renderers:e.state.document.scene.nodes.reduce((a,n)=>a+n.renderers.length,0),stats:e.renderer.stats};})()");assert.equal(state.nodes,level.nodes);assert.equal(state.renderers,level.renderers);assert.equal(state.stats.errors,0);assert(state.stats.drawCalls>0);report.levels.push(state);
        report.lastStep=level.mission+' select';const edit=await evaluate("(()=>{const e=window.rc2Editor,s=e.state.document.scene,index=s.nodes.findIndex(n=>n.active&&!n.background&&n.renderers.length);e.select(index);return {index,name:s.nodes[index].name,x:s.nodes[index].p[0]};})()");report.lastStep=level.mission+' name';await property("名称",edit.name+" · QA保存");report.lastStep=level.mission+' position';await property("局部位置",edit.x+.625,0);report.lastStep=level.mission+' save';await click("save");const values=await evaluate(`({x:window.rc2Editor.state.document.scene.nodes[${edit.index}].p[0],name:window.rc2Editor.state.document.scene.nodes[${edit.index}].name})`);assert.equal(values.x,edit.x+.625);assert.equal(values.name,edit.name+" · QA保存");report.lastStep=level.mission+' hash';const expected=await documentHash();report.lastStep=level.mission+' reopen';await open(level);assert.equal(await documentHash(),expected);assert.equal(jsonHash(JSON.parse(fs.readFileSync(path.join(saveRoot,level.id.replaceAll("-","")+".rc2level"),"utf8"))),expected);report.roundtrips++;console.log("EDITOR_PASS",level.mission);
    }
    await pixelChecks();await advanced();await representativeViews();await modeIntegration();assert.deepEqual(report.browserErrors,[]);for(const [relative,hash]of originals)assert.equal(sha(path.join(pack,relative)),hash,"Original changed: "+relative);report.originalFilesChecked=originals.size;report.saveRoot=saveRoot;report.pass=true;
}
main().catch(async error=>{report.firstFailure=error.stack||String(error);if(send)try{await shot("editor-failure.png");}catch{};process.exitCode=1;}).finally(async()=>{
    if(send)try{await send("Browser.close");}catch{};if(socket)socket.close();
    for(const child of [browser,server])if(child&&child.exitCode===null){child.kill();await Promise.race([new Promise(r=>child.once("exit",r)),sleep(3000)]);}
    const resolved=path.resolve(profile);assert(resolved.startsWith(path.resolve(work)+path.sep)&&path.basename(resolved)==="editor-qa-chrome-"+runId);
    try{fs.rmSync(resolved,{recursive:true,force:true,maxRetries:4,retryDelay:500});}catch(error){report.cleanupError=error.message;}
    fs.writeFileSync(output,JSON.stringify(report,null,2)+"\n","utf8");console.log(JSON.stringify({pass:report.pass,levels:report.levels.length,roundtrips:report.roundtrips,pixelChecks:report.pixelChecks,firstFailure:report.firstFailure,report:output}));
});
