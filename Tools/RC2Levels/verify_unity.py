"""Run the real mod scene in an isolated Unity/URP project, not the open game.

Command: python Tools/RC2Levels/verify_unity.py --unity D:/.../Editor/Unity.exe
PASS: Editor probe confirms all documents, meshes/materials and screenshots; no
exception logs. Host DLLs and render dependencies are read-only, frozen snapshots.
Boundary: runtime admission/renderability, not APK frame/animation/fluid equivalence.
Invalidation: host/Unity/mod DLL, source catalog, shaders or URP changes.
"""
import argparse
import hashlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import uuid


def copy(source, target):
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)


def setup(project, work):
    private = work / 'unity-qa-project'
    (private / 'Assets/Editor').mkdir(parents=True, exist_ok=True)
    (private / 'ProjectSettings').mkdir(exist_ok=True)
    (private / 'Packages').mkdir(exist_ok=True)
    copy(project / 'ProjectSettings/ProjectVersion.txt', private / 'ProjectSettings/ProjectVersion.txt')
    # Only shader-library packages; runtime URP is the exact host plugin DLL.
    dependencies = {name: 'file:' + str(project / 'Packages' / name).replace('\\', '/') for name in ('com.unity.render-pipelines.core', 'com.unity.render-pipelines.universal')}
    dependencies.update({name: '1.0.0' for name in ('com.unity.modules.animation', 'com.unity.modules.assetbundle', 'com.unity.modules.imageconversion', 'com.unity.modules.particlesystem', 'com.unity.modules.physics', 'com.unity.modules.physics2d', 'com.unity.modules.ui')})
    (private / 'Packages/manifest.json').write_text(json.dumps({'dependencies': dependencies}), encoding='utf-8')
    (private / 'ProjectSettings/ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  companyName: RC2Verification\n  productName: RC2IsolatedQA\n  m_ActiveColorSpace: 0\n', encoding='utf-8')
    snapshot = work / 'host-assemblies'; snapshot.mkdir(exist_ok=True)
    response = project / '_codex_work/rc2-level-mod/build/mod-compile.rsp'
    references = [pathlib.Path(line[3:].strip('"')) for line in response.read_text(encoding='utf-8-sig').splitlines() if line.startswith('-r:')]
    hashes = {}
    for source in references + [project / 'Mods/RC2Levels/BpsimRC2Levels.dll']:
        if source.is_file() and source.is_relative_to(project) and source.suffix == '.dll':
            target = snapshot / source.name
            copy(source, target)
            hashes[source.name] = hashlib.sha256(target.read_bytes()).hexdigest()
    # These assemblies must be registered for the serialized pipeline and Editor
    # probe compiler. The host/game/mod assemblies are loaded only after Play starts.
    plugins = [project / 'Assets/Plugins/Newtonsoft.Json.dll']
    plugins += list((project / 'Assets/Plugins').glob('*RenderPipeline*.dll'))
    plugins.append(project / 'Library/ScriptAssemblies/Unity.TextMeshPro.dll')
    for source in plugins:
        target = private / 'Assets/Plugins' / source.name; copy(source, target)
        meta = source.with_name(source.name + '.meta')
        if meta.exists(): copy(meta, target.with_name(target.name + '.meta'))
    # Freeze the host URP resource dependency closure. Do not copy any C# gameplay
    # scripts or scene objects. The host outline renderer feature is excluded.
    result = subprocess.run(['rg', '-n', '--no-heading', '^guid: [0-9a-f]{32}', 'Assets', '-g', '*.meta'], cwd=project, stdout=subprocess.PIPE, check=True)
    guids = {}
    for line in result.stdout.decode('utf-8', errors='replace').splitlines():
        match = re.match(r'^(.*\.meta):\d+:guid: ([0-9a-f]{32})', line)
        if match: guids[match[2]] = project / match[1][:-5]
    queue = [project / 'Assets/MonoBehaviour/URP_2D_Asset.asset', project / 'Assets/MonoBehaviour/URP_2D_Asset_Renderer.asset']
    seen = set(); missing = set()
    while queue:
        source = queue.pop()
        if source in seen: continue
        seen.add(source)
        if not source.exists() or source.suffix == '.cs':
            missing.add(str(source)); continue
        target = private / source.relative_to(project)
        copy(source, target)
        if source.with_name(source.name + '.meta').exists(): copy(source.with_name(source.name + '.meta'), target.with_name(target.name + '.meta'))
        if source.suffix in ('.asset', '.mat', '.shader', '.shadergraph'):
            text = source.read_text(encoding='utf-8', errors='replace')
            if source.name == 'URP_2D_Asset_Renderer.asset':
                text = text.split('--- !u!114 &3422574630169659380')[0]
                text = text.replace('  m_RendererFeatures:\n  - {fileID: 3422574630169659380}', '  m_RendererFeatures: []')
                target.write_text(text, encoding='utf-8')
            for guid in set(re.findall(r'guid: ([0-9a-f]{32})', text)):
                if guid in guids: queue.append(guids[guid])
                elif guid != '0' * 32: missing.add(guid)
    for source in (project / 'Assets').rglob('*'):
        if source.is_file() and source.suffix in ('.cginc', '.hlsl'):
            copy(source, private / source.relative_to(project))
    # Original source fonts are loaded by the mod. Only their Shader programs are
    # needed from TMP; no replacement glyph atlases or terrain textures are used.
    for source in (project / 'Assets/Shader').glob('*'):
        if source.is_file() and ('TMP' in source.name or 'TextMesh' in source.name): copy(source, private / source.relative_to(project))
    config = {'run_id': uuid.uuid4().hex, 'mod': str(project / 'Mods/RC2Levels').replace('\\', '/'), 'snapshot': str(snapshot).replace('\\', '/'),
              'output': str(work).replace('\\', '/'), 'assembly_hashes': hashes, 'missing_render_dependencies': sorted(missing),
              'pipeline_scope': 'host URP 2D renderer; outline feature disabled in private copy only'}
    (private / 'rc2-qa-config.json').write_text(json.dumps(config), encoding='utf-8')
    copy(project / 'Tools/RC2Levels/unity_probe.cs', private / 'Assets/Editor/RC2UnityProbe.cs')
    return private, config


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--unity', required=True); parser.add_argument('--prepare-only', action='store_true')
    args = parser.parse_args(); project = pathlib.Path(__file__).resolve().parents[2]
    work = project / '_codex_work/rc2-level-mod/build'; work.mkdir(parents=True, exist_ok=True)
    private, config = setup(project, work)
    print('RC2_UNITY_QA_PREPARED', private, 'render deps unresolved', len(config['missing_render_dependencies']), flush=True)
    if args.prepare_only: return
    log = work / 'unity-qa.log'; env = dict(os.environ); env['TMP'] = env['TEMP'] = str(work)
    command = [str(pathlib.Path(args.unity).resolve()), '-batchmode', '-disable-assembly-updater', '-force-d3d11', '-projectPath', str(private), '-executeMethod', 'Bpsim.RC2Qa.Probe.Run', '-logFile', str(log)]
    try:
        result = subprocess.run(command, cwd=project, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0), timeout=900)
    except subprocess.TimeoutExpired:
        raise RuntimeError('Isolated Unity QA exceeded 900s; inspect ' + str(log))
    report_path = work / 'unity-verification.json'
    report = json.loads(report_path.read_text(encoding='utf-8')) if report_path.exists() else {}
    if report.get('configuration',{}).get('run_id') != config['run_id']:
        report={'pass':False,'first_failure':'Editor probe did not produce a fresh report','configuration':config,'exit':result.returncode,
                'boundary':'failed isolated render probe; not a passed Unity/DOTS or APK-effect gate'}
        report_path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'exit': result.returncode, 'report': str(report_path), **{k: report[k] for k in ('pass', 'first_failure', 'levels') if k in report}}, ensure_ascii=True))
    if result.returncode or not report.get('pass'): raise SystemExit(1)


if __name__ == '__main__': main()
