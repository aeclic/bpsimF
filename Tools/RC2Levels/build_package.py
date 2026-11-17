"""Rebuild RC2 mod and URP shaders without changing the user's open Unity scene.

Usage: python Tools/RC2Levels/build_package.py --unity D:/.../Editor/Unity.exe
PASS: Mono compilation exit 0, isolated shader bundle present, ZIP CRC and manifest pass.
Scope: current project host ABI and Windows Shader bundle; no APK execution claim.
Invalidation: Unity / host assemblies / source assets / shader target changes.
"""
import argparse
import json
import os
import pathlib
import re
import shutil
import subprocess
import zipfile
import hashlib
import xml.etree.ElementTree as ET


def run(command, cwd, log, timeout=180):
    env=dict(os.environ)
    env['TEMP']=env['TMP']=str(log.parent)
    result=subprocess.run(command,cwd=cwd,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,
                          creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0),timeout=timeout)
    text=result.stdout.decode('utf-8',errors='replace')
    log.write_text(text,encoding='utf-8')
    if result.returncode:
        raise RuntimeError('Command failed ('+str(result.returncode)+'): '+text[-9000:])
    return text


def compile_mod(project, unity, mod, work):
    retained=project/'_codex_work/level-editor-mod/mod-compile.rsp'
    if not retained.exists():
        raise FileNotFoundError('Build the existing LevelEditor mod once to record the exact host reference set: '+str(retained))
    references=[]
    for line in retained.read_text(encoding='utf-8-sig').splitlines():
        if line.startswith('-r:'):
            path=pathlib.Path(line[3:].strip('"'))
            if path.exists() and path not in references:
                references.append(path)
    host=project/'Library/ScriptAssemblies/Assembly-CSharp.dll'
    if host not in references:
        references.append(host)
    quote=lambda p:'"'+str(p.resolve()).replace('\\','/')+'"'
    assembly_name=ET.parse(mod/'Mod.xml').find('./Assemblies/Assembly').attrib['path']
    options=['-target:library','-langversion:latest','-nostdlib+','-noconfig','-out:'+quote(mod/assembly_name)]
    options+=['-r:'+quote(p) for p in references]
    options+=[quote(p) for p in sorted((mod/'Code').glob('*.cs'))]
    response=work/'mod-compile.rsp'; response.write_text('\n'.join(options)+'\n',encoding='utf-8')
    runtime=unity.parent/'Data/MonoBleedingEdge'
    run([str(runtime/'bin/mono.exe'),str(runtime/'lib/mono/4.5/mcs.exe'),'@'+str(response)],project,work/'mod-compile.log',120)
    print('COMPILED',mod/assembly_name,flush=True)


def shaders(project,unity,mod,work):
    private=work/'shader-project'
    (private/'Assets/Editor').mkdir(parents=True,exist_ok=True)
    (private/'Assets/RC2').mkdir(parents=True,exist_ok=True)
    (private/'ProjectSettings').mkdir(exist_ok=True)
    (private/'Packages').mkdir(exist_ok=True)
    shutil.copy2(project/'ProjectSettings/ProjectVersion.txt',private/'ProjectSettings/ProjectVersion.txt')
    # AssetBundle's native manifest object is stripped when its built-in module is
    # absent. A file with Shader objects alone parses offline but native Unity rejects it.
    (private/'Packages/manifest.json').write_text(json.dumps({'dependencies':{
        'com.unity.modules.assetbundle':'1.0.0','com.unity.modules.imageconversion':'1.0.0'}}),encoding='utf-8')
    for source in (mod/'Shaders').glob('*.shader'):
        shutil.copy2(source,private/'Assets/RC2'/source.name)
    output=mod/'Assets'; output.mkdir(exist_ok=True)
    script='''using System;
using System.IO;
using UnityEditor;
using UnityEngine;
namespace Bpsim.RC2Build {
public static class ShaderBuilder {
public static void Run() {
string output=OUTPUT;
Directory.CreateDirectory(output);
AssetBundleBuild build=new AssetBundleBuild {
assetBundleName="rc2-windows.bundle",
assetNames=new[]{"Assets/RC2/RC2Original.shader","Assets/RC2/RC2Waterfall.shader"}
};
AssetBundleManifest manifest=BuildPipeline.BuildAssetBundles(output,new[]{build},BuildAssetBundleOptions.ChunkBasedCompression|BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64);
if(manifest==null) throw new Exception("RC2 Shader bundle build failed");
AssetBundle native=AssetBundle.LoadFromFile(Path.Combine(output,build.assetBundleName));
if(native==null) throw new Exception("RC2 native Shader bundle load failed");
try {
bool original=false,waterfall=false;
foreach(Shader shader in native.LoadAllAssets<Shader>()) {
if(!shader.isSupported) throw new Exception("RC2 unsupported Shader: "+shader.name);
if(shader.name=="Bpsim/RC2/OriginalUnlit") original=true;
if(shader.name=="Bpsim/RC2/Waterfall") waterfall=true;
}
if(!original||!waterfall) throw new Exception("RC2 expected Shader assets missing");
} finally { native.Unload(true); }
Debug.Log("RC2_SHADER_BUILD_PASS "+Path.Combine(output,build.assetBundleName));
}
}}
'''.replace('OUTPUT',json.dumps(str(output).replace('\\','/')))
    (private/'Assets/Editor/ShaderBuilder.cs').write_text(script,encoding='utf-8')
    log=work/'shader-build.log'
    run([str(unity),'-batchmode','-force-d3d11','-quit','-projectPath',str(private),'-executeMethod','Bpsim.RC2Build.ShaderBuilder.Run','-logFile',str(log)],project,work/'shader-process.log',600)
    if not (output/'rc2-windows.bundle').exists() or 'RC2_SHADER_BUILD_PASS' not in log.read_text(encoding='utf-8',errors='replace'):
        raise RuntimeError('Shader build was not confirmed; inspect '+str(log))
    print('BUILT SHADERS',output/'rc2-windows.bundle',flush=True)


def stage(source,mod):
    catalog=json.loads((source/'catalog.json').read_text(encoding='utf-8'))
    paths={'catalog.json'}
    paths.update(l['path'] for l in catalog['levels'])
    paths.update(l['path'] for l in catalog.get('backgrounds',{}).values())
    for kind in catalog['assets'].values():
        paths.update(m['path'] for m in kind.values())
    for relative in sorted(paths):
        path=(source/relative).resolve()
        if not path.is_relative_to(source.resolve()):
            raise ValueError('Escaping source asset path')
        target=mod/relative; target.parent.mkdir(parents=True,exist_ok=True)
        shutil.copy2(path,target)
    return catalog


def package(project,mod,catalog,playable=False):
    reports={
        'source':project/'_codex_work/rc2-level-mod/source-verification.json',
        'editor':project/'_codex_work/rc2-level-mod/editor-verification.json',
        'managed':project/'_codex_work/rc2-level-mod/build/managed-verification.json',
        'unity':project/('_codex_work/rc2-level-mod/native-play-verification.json' if playable else '_codex_work/rc2-level-mod/build/unity-verification.json')}
    for stage,path in reports.items():
        report=json.loads(path.read_text(encoding='utf-8')) if path.exists() else {}
        if not report.get('pass') or stage=='editor' and (report.get('mode')!='full' or len(report.get('levels',[]))!=17):
            raise RuntimeError('Final RC2 ZIP is blocked by the unpassed '+stage+' verification gate: '+str(path))
    manifest=ET.parse(mod/'Mod.xml');assembly=manifest.find('./Assemblies/Assembly').attrib['path'];version=manifest.findtext('./Version')
    if playable:
        live=json.loads(reports['unity'].read_text(encoding='utf-8'))
        if len(live.get('levels',[]))!=17:raise RuntimeError('Native playable gate did not cover 17 source prefabs')
        for field,file in [('mod_dll_sha256',mod/assembly),('catalog_sha256',mod/'catalog.json'),('bundle_sha256',mod/'Assets/rc2-windows.bundle')]:
            if live['artifacts'][field]!=hashlib.sha256(file.read_bytes()).hexdigest():raise RuntimeError('Native gate is stale: '+field)
    paths={'Mod.xml',assembly,'README.md','catalog.json','Assets/rc2-windows.bundle'}
    paths.update(level['path'] for level in catalog['levels'])
    paths.update(level['path'] for level in catalog.get('backgrounds',{}).values())
    for entries in catalog['assets'].values():
        for metadata in entries.values():
            file=(mod/metadata['path']).resolve()
            if not file.is_relative_to(mod.resolve()) or hashlib.sha256(file.read_bytes()).hexdigest()!=metadata['sha256']:raise RuntimeError('Staged asset does not match source verification: '+metadata['path'])
            paths.add(metadata['path'])
    for directory in ('Code','Web','Shaders'):
        paths.update(file.relative_to(mod).as_posix() for file in (mod/directory).rglob('*') if file.is_file() and not file.name.endswith(('.meta','.dshclaim')))
    output=project/'Builds/Mods'/('RC2关卡mod-'+version+('-试玩修复版' if playable else '')+'.zip')
    output.parent.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(output,'w',zipfile.ZIP_DEFLATED,compresslevel=6) as z:
        for relative in sorted(paths):
            z.write(mod/relative,'RC2Levels/'+relative)
    with zipfile.ZipFile(output) as z:
        error=z.testzip()
        if error:
            raise ValueError('ZIP CRC failure: '+error)
        if 'RC2Levels/Mod.xml' not in z.namelist() or 'RC2Levels/'+assembly not in z.namelist() or 'RC2Levels/Assets/rc2-windows.bundle' not in z.namelist():
            raise ValueError('Package entry point missing')
    print('PACKAGED',output,'levels',len(catalog['levels']),'bytes',output.stat().st_size,flush=True)
    return output


def main():
    ap=argparse.ArgumentParser(); ap.add_argument('--unity',required=True); ap.add_argument('--compile-only',action='store_true'); ap.add_argument('--skip-shaders',action='store_true')
    ap.add_argument('--source-pack',default='_codex_work/rc2-level-mod/original-pack')
    ap.add_argument('--shaders-only',action='store_true')
    ap.add_argument('--package-only',action='store_true');ap.add_argument('--playable-package',action='store_true')
    args=ap.parse_args()
    project=pathlib.Path(__file__).resolve().parents[2]; mod=project/'Mods/RC2Levels'; work=project/'_codex_work/rc2-level-mod/build'; work.mkdir(parents=True,exist_ok=True)
    unity=pathlib.Path(args.unity).resolve()
    if args.package_only:
        package(project,mod,json.loads((mod/'catalog.json').read_text(encoding='utf-8')),args.playable_package);return
    compile_mod(project,unity,mod,work)
    if args.compile_only:
        return
    catalog=stage((project/args.source_pack).resolve(),mod)
    if not args.skip_shaders:
        shaders(project,unity,mod,work)
    if args.shaders_only:
        return
    package(project,mod,catalog,args.playable_package)


if __name__=='__main__':
    main()
