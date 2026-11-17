"""Native RC2 shader-bundle build/load A/B without touching the open Unity project.

Command: python Tools/RC2Levels/probe_bundle.py --unity D:/.../Editor/Unity.exe
PASS: old bundle failure is recorded; newly built native AssetBundle loads, contains
its AssetBundle manifest object and both supported expected Shader assets; duplicate
LoadFromMemory is not used to manage shared scenes. No APK pixel/parallax claim.
Invalidation: Unity/module dependencies/shader sources/platform or bundle changes.
"""
import argparse
import hashlib
import json
import os
import pathlib
import shutil
import subprocess


def main():
    p = argparse.ArgumentParser(); p.add_argument('--unity', required=True); args = p.parse_args()
    project = pathlib.Path(__file__).resolve().parents[2]; work = project / '_codex_work/rc2-level-mod/build'
    private = work / 'bundle-probe-project'
    for directory in ('Assets/Editor', 'Assets/RC2', 'Packages', 'ProjectSettings'):
        (private / directory).mkdir(parents=True, exist_ok=True)
    shutil.copy2(project / 'ProjectSettings/ProjectVersion.txt', private / 'ProjectSettings/ProjectVersion.txt')
    dependencies = {'com.unity.modules.assetbundle':'1.0.0','com.unity.modules.imageconversion':'1.0.0'}
    (private / 'Packages/manifest.json').write_text(json.dumps({'dependencies':dependencies}), encoding='utf-8')
    for file in (project / 'Mods/RC2Levels/Shaders').glob('*.shader'):
        shutil.copy2(file, private / 'Assets/RC2' / file.name)
    fixture=work/'invalid-shader-fixture.bundle'
    if not fixture.exists():shutil.copy2(project/'Mods/RC2Levels/Assets/rc2-windows.bundle',fixture)
    config = {'old_bundle': str(fixture).replace('\\','/'),
        'output':str(work/'bundle-shaders').replace('\\','/'), 'report':str(work/'bundle-probe.json').replace('\\','/')}
    (private / 'probe-config.json').write_text(json.dumps(config), encoding='utf-8')
    shutil.copy2(project/'Tools/RC2Levels/bundle_probe.cs', private/'Assets/Editor/BundleProbe.cs')
    log = work/'bundle-probe.log'; env=dict(os.environ);env['TEMP']=env['TMP']=str(work)
    command=[args.unity,'-batchmode','-disable-assembly-updater','-force-d3d11','-projectPath',str(private),'-executeMethod','Bpsim.RC2BundleProbe.Probe.Run','-logFile',str(log)]
    result=subprocess.run(command,cwd=project,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,
        creationflags=getattr(subprocess,'CREATE_NO_WINDOW',0),timeout=600)
    report=json.loads(pathlib.Path(config['report']).read_text(encoding='utf-8')) if pathlib.Path(config['report']).exists() else {'pass':False,'error':'No native report; inspect '+str(log)}
    import UnityPy
    native=pathlib.Path(config['output'])/'rc2-windows.bundle'
    if native.exists():
        types=[o.type.name for o in UnityPy.load(str(native)).objects]
        report['serialized_object_types']=types;report['sha256']=hashlib.sha256(native.read_bytes()).hexdigest()
        report['pass']=report.get('pass',False) and 'AssetBundle' in types
    pathlib.Path(config['report']).write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'exit':result.returncode,**report},ensure_ascii=True))
    if result.returncode or not report.get('pass'): raise SystemExit(1)


if __name__=='__main__':main()
