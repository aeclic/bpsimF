"use strict";
// Command: node Tools/RC2Levels/test_core.cjs
// PASS: all source-verified meshes/documents pass, 17 edited JSON roundtrips,
// worlds/skin samples match an independently collected SciPy/NumPy reference,
// malformed data is rejected. Not a rendered-frame/gameplay comparison.
// Invalidated by the APK, document/core implementation or geometry fixture changes.
const assert=require("node:assert/strict"),fs=require("node:fs"),path=require("node:path"),crypto=require("node:crypto");
const core=require("../../Mods/LevelEditor/Web/bpsim/rc2-core.js"),root=path.resolve(__dirname,"../../_codex_work/rc2-level-mod/original-pack"),work=path.dirname(root);
const catalog=JSON.parse(fs.readFileSync(path.join(root,"catalog.json"),"utf8")),reference=JSON.parse(fs.readFileSync(path.join(work,"geometry-reference.json"),"utf8"));
const report={pass:false,levels:0,missionNodes:0,nodes:0,renderers:0,meshes:0,skins:0,waterfallLayers:0,roundtrips:0,negativeControls:0,worldMatrices:0,vertexSamples:0,maxWorldError:0,maxVertexError:0,boundary:"source geometry arithmetic and JSON contracts, not APK rendered-frame/dynamic parity"};
const load=relative=>JSON.parse(fs.readFileSync(path.join(root,relative),"utf8")),meshes=new Map(Object.entries(catalog.assets.meshes).map(([k,m])=>[k,load(m.path)]));
const compare=(actual,expected,where,key)=>{assert.equal(actual.length,expected.length,where);let error=0;for(let i=0;i<actual.length;i++){assert(Number.isFinite(actual[i]),where);error=Math.max(error,Math.abs(actual[i]-expected[i]));assert(error<=1e-6,where+" error="+error);}report[key]=Math.max(report[key],error);};
const negative=fn=>{assert.throws(fn);report.negativeControls++;};
try{
    assert.equal(reference.apk_sha256,catalog.apk_sha256);
    for(const mesh of meshes.values()){core.validateMesh(mesh);report.meshes++;}
    for(const level of catalog.levels){
        const doc=load(level.path),scene=doc.scene,matrices=core.worlds(scene),ref=reference.levels[level.id];
        assert.equal(crypto.createHash("sha256").update(fs.readFileSync(path.join(root,level.path))).digest("hex"),ref.document_sha256);
        assert.equal(matrices.length,scene.nodes.length);assert.equal(matrices.length,ref.worlds.length);report.levels++;report.nodes+=scene.nodes.length;report.missionNodes+=scene.source_mission_node_count;
        matrices.forEach((m,i)=>{compare(m,ref.worlds[i],level.mission+" matrix["+i+"]","maxWorldError");report.worldMatrices++;});
        const outputs=new Map();
        for(let i=0;i<scene.nodes.length;i++)for(const r of scene.nodes[i].renderers){report.renderers++;if(r.waterfall)report.waterfallLayers++;const p=scene.prefabs[r.prefab];if(!p.mesh)continue;const mesh=meshes.get(p.mesh),positions=core.positions(mesh,i,r,matrices);assert.equal(positions.length,mesh.vertices.length);assert(positions.every(Number.isFinite));outputs.set(r.id,positions);if(r.bones?.length)report.skins++;}
        for(const sample of ref.samples){const values=outputs.get(sample.renderer);assert(values,"missing reference renderer");compare(values.slice(sample.vertex*3,sample.vertex*3+3),sample.p,level.mission+" vertex "+sample.renderer+"/"+sample.vertex,"maxVertexError");report.vertexSamples++;}
        const edited=core.clone(doc),target=edited.scene.nodes.find(n=>!n.background&&n.renderers.length&&n.active);target.p[0]+=.625;target.name+=" 娴嬭瘯淇敼";
        assert.deepEqual(JSON.parse(JSON.stringify(edited)),edited);assert.notDeepEqual(edited,doc);assert.equal(edited.source.apk_sha256,catalog.apk_sha256);report.roundtrips++;
        const copy=core.clone(scene),before=copy.nodes.length;let ids=0;const rootCopy=core.duplicate(copy,0,()=>String(++ids));assert.equal(rootCopy,before);core.worlds(copy);core.remove(copy,rootCopy);assert.deepEqual(copy,core.clone(scene));
    }
    assert.equal(report.levels,17);assert.equal(report.missionNodes,9632);assert.equal(report.nodes,10085);assert.equal(report.nodes,catalog.counts.embedded_nodes);assert.equal(report.renderers,4301);assert.equal(report.renderers,catalog.counts.embedded_renderers);assert.equal(report.skins,326);assert.equal(report.waterfallLayers,6);assert.equal(report.meshes,433);
    negative(()=>core.worlds({nodes:[{parent:0,p:[0,0,0],q:[0,0,0,1],s:[1,1,1]}]}));
    negative(()=>core.validateMesh({vertices:[0,0,0],uv:[0,0],colors:[1,1,1,1],submeshes:[[0,1,0]]}));
    negative(()=>core.validateMesh({vertices:[Infinity,0,0],uv:[0,0],colors:[1,1,1,1],submeshes:[[0,0,0]]}));
    const valid={vertices:[0,0,0],uv:[0,0],colors:[1,1,1,1],submeshes:[[0,0,0]],bindposes:[core.identity()],weights:[1,0,0,0],bone_indices:[0,0,0,0]};
    core.validateMesh(valid);
    negative(()=>core.validateMesh({...valid,weights:[1,0]}));
    negative(()=>core.validateMesh({...valid,bone_indices:[1,0,0,0]}));
    negative(()=>core.validateMesh({...valid,weights:[-.2,1.2,0,0]}));
    negative(()=>core.worlds({nodes:[{parent:-1,p:[NaN,0,0],q:[0,0,0,1],s:[1,1,1]}]}));
    negative(()=>core.worlds({nodes:[{parent:-1,p:[0,0],q:[0,0,0,1],s:[1,1,1]}]}));
    negative(()=>core.positions({...valid,bone_indices:[1,0,0,0]},0,{bones:[0]},[core.identity()]));
    // A 90-degree square rotation keeps its bbox, but the vertex oracle must differ.
    const square={vertices:[1,1,0,1,-1,0,-1,-1,0,-1,1,0]},a=core.positions(square,0,{},[core.identity()]),b=core.positions(square,0,{},[core.trs({p:[0,0,0],q:[0,0,Math.SQRT1_2,Math.SQRT1_2],s:[1,1,1]})]);
    const bbox=v=>[Math.min(...v.filter((_,i)=>i%3===0)),Math.max(...v.filter((_,i)=>i%3===0)),Math.min(...v.filter((_,i)=>i%3===1)),Math.max(...v.filter((_,i)=>i%3===1))];
    assert.deepEqual(bbox(a),bbox(b));assert.notDeepEqual(a,b);report.bboxInvariantOrientationControl=true;
    report.pass=true;
}catch(error){report.firstFailure=(error.stack||String(error)).slice(0,4000);process.exitCode=1;}
finally{fs.writeFileSync(path.join(work,"core-tests.json"),JSON.stringify(report,null,2)+"\n","utf8");console.log(JSON.stringify(report));}
