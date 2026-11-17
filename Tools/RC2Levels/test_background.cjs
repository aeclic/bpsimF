"use strict";
// Command: node Tools/RC2Levels/test_background.cjs
// PASS: all 17 source-associated bindings preserve terrain/source documents and Z,
// keep projection under pan/zoom, and produce finite repeated background matrices.
// Boundary: the documented host camera binding contract, not APK update/frame phase.
// Invalidated by source catalog, binding, core or coordinate convention changes.
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict'),core=require('../../Mods/LevelEditor/Web/bpsim/rc2-core.js');
const root=path.resolve(__dirname,'../../_codex_work/rc2-level-mod/original-pack'),catalog=JSON.parse(fs.readFileSync(path.join(root,'catalog.json'),'utf8'));
const report={pass:false,levels:0,cameraStates:0,terrainMatrices:0,repeatNodes:0,boundary:'source camera binding contract; no APK frame-phase claim'};
try{
 for(const level of catalog.levels){const doc=JSON.parse(fs.readFileSync(path.join(root,level.path),'utf8')),scene=doc.scene,original=JSON.stringify(scene),binding=scene.background_binding;
  assert.equal(binding.source_mission.data._prefabName,level.mission);assert.equal(binding.background_key,binding.source_mission.data._backgroundPrefabName);assert.equal(binding.camera_ortho_size,binding.source_camera.data['orthographic size']);
  const pure=core.worlds(scene),index=scene.nodes.findIndex(n=>n.id===binding.root_id);assert(index>=0);const ratio=scene.nodes[index].s[0]/binding.camera_ortho_size;
  for(const camera of [{x:0,y:0,half_height:8},{x:345,y:-7,half_height:12},{x:2000,y:3,half_height:6}]){
   const bound=core.backgroundWorlds(scene,camera);assert.equal(bound.matrices.length,pure.length);assert.equal(bound.matrices[index][14],pure[index][14]);assert(Math.abs(bound.matrices[index][0]/camera.half_height-ratio)<1e-9);
   for(let i=0;i<scene.nodes.length;i++){assert(bound.matrices[i].every(Number.isFinite));if(!scene.nodes[i].background){assert.deepEqual(bound.matrices[i],pure[i]);report.terrainMatrices++;}}
   for(const deltas of bound.repeats.values())for(const delta of deltas){assert.equal(delta.length,3);assert(delta.every(Number.isFinite));}assert(bound.repeats.size>0);report.repeatNodes+=bound.repeats.size;report.cameraStates++;
  }
  assert.equal(JSON.stringify(scene),original,'Camera binding changed source/edit document');report.levels++;
 }
 assert.equal(report.levels,17);report.pass=true;
}catch(error){report.firstFailure=error.stack;process.exitCode=1;}
fs.writeFileSync(path.resolve(root,'../background-tests.json'),JSON.stringify(report,null,2)+'\n','utf8');console.log(JSON.stringify(report));
