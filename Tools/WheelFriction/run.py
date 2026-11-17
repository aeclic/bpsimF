"""Compile production source and run a private Unity coasting instrument on D:.

python Tools/WheelFriction/run.py --prepare
python Tools/WheelFriction/run.py --label a
Private project owns its Worlds/colliders, never opens the user's scenes/editor.
Complete production PartPhysics is copied; full production source is compiled separately.
Existing AnvilParity dependency preparation is reused in a distinct scratch path.
"""
import argparse
import hashlib
import json
import os
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRATCH = ROOT / '_codex_work/wheel-friction'
PROJECT = SCRATCH / 'project'
UNITY = Path(json.loads((ROOT / 'Tools/dsh-slots/config.json').read_text(encoding='utf-8-sig'))['unity'])
DOTNET = UNITY.parent / 'Data/NetCoreRuntime/dotnet.exe'
CSC = UNITY.parent / 'Data/DotNetSdkRoslyn/csc.dll'


def compile_host():
    original = ROOT / 'Library/Bee/artifacts/1300b0aE.dag/Assembly-CSharp.rsp'
    options, sources = [], set()
    for line in original.read_text(encoding='utf-8-sig').splitlines():
        if line.endswith(('.cs', '.cs"')):
            sources.add((ROOT / line.strip('"')).resolve())
        elif line.strip() and not line.startswith(('-out:', '-refout:', '-pdb:', '-generatedfilesout:', '-errorlog:')):
            options.append(line)
    sources.update((ROOT / 'Assets/Scripts/Assembly-CSharp').rglob('*.cs'))
    sources = sorted(path for path in sources if path.is_file())
    fingerprints = {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    for player in (False, True):
        label = 'player' if player else 'host'
        settings = [line for line in options if not (player and line.startswith('-define:UNITY_EDITOR'))]
        output = SCRATCH / f'WheelFriction-{label}.dll'
        response = SCRATCH / f'{label}.rsp'
        response.write_text('\n'.join(settings + ['-out:"' + str(output) + '"']
                                       + ['"' + str(p) + '"' for p in sources]) + '\n', encoding='utf-8')
        log = SCRATCH / f'{label}-compile.log'
        result = subprocess.run([str(DOTNET), str(CSC), '@' + str(response)], cwd=ROOT, capture_output=True, text=True, encoding='utf-8')
        log.write_text(result.stdout + result.stderr, encoding='utf-8')
        if result.returncode:
            print(result.stdout[-8000:], result.stderr[-2000:])
            raise RuntimeError(f'Full {label} compile failed; see {log}')
        print(f'PASS: full {label} compile, {len(sources)} source files')
    (SCRATCH / 'compile-fingerprints.json').write_text(json.dumps(fingerprints, indent=2) + '\n', encoding='utf-8')


def prepare(skip_host=False):
    SCRATCH.mkdir(parents=True, exist_ok=True)
    if not skip_host:
        compile_host()
    preparer = ROOT / 'Tools/AnvilParity/prepare_project.py'
    code = preparer.read_text(encoding='utf-8-sig')
    assert '"_codex_work/anvil-parity/project"' in code and '"_codex_work/anvil-parity/compile/physics.rsp"' in code
    code = code.replace('"_codex_work/anvil-parity/project"', '"_codex_work/wheel-friction/project"')
    code = code.replace('"_codex_work/anvil-parity/compile/physics.rsp"', '"_codex_work/wheel-friction/compile/physics.rsp"')
    scope = dict(__file__=str(preparer), __name__='_wheel_dependency_preparer')
    exec(compile(code, str(preparer), 'exec'), scope)
    scope['main']()
    for relative in ['Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.WheelFriction.cs',
                     'Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Simulation/PartPhysics.cs']:
        target = PROJECT / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / relative, target)
    assert 'Value = PartPhysics.CreateCollider(component)' in (ROOT / 'Assets/Scripts/Assembly-CSharp/Bpsim/Parts/PartEntityFactory.cs').read_text(encoding='utf-8-sig')
    manifest = PROJECT / 'Packages/manifest.json'
    values = json.loads(manifest.read_text(encoding='utf-8'))
    values['dependencies']['com.unity.modules.physics'] = '1.0.0'
    manifest.write_text(json.dumps(values) + '\n', encoding='utf-8')
    (PROJECT / 'Assets/csc.rsp').write_text('/unsafe+\n', encoding='utf-8')
    shutil.copy2(ROOT / 'Tools/WheelFriction/ActualPrefabQA.cs', PROJECT / 'Assets/ActualPrefabQA.cs')
    for group in ['7_CartWheel', '8_NormalWheel', '9_SmallWheel', '10_MotorWheel', '38_StickyWheel', '6_MetalFrame']:
        target = PROJECT / 'Assets/GameObject'
        target.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / f'Assets/GameObject/PartGroup_{group}.prefab', target)
    target = PROJECT / 'Assets/PhysicsMaterials'
    target.mkdir(parents=True, exist_ok=True)
    for source in (ROOT / 'Assets/PhysicsMaterials').iterdir():
        if source.is_file(): shutil.copy2(source, target)
    print('Private project ready:', PROJECT)


def run(label, actual=False):
    if not label.isalnum():
        raise ValueError('Label must be alphanumeric')
    log = SCRATCH / f'unity-{label}.log'
    result = subprocess.run([str(UNITY), '-batchmode', '-nographics', '-quit', '-disable-assembly-updater',
                             '-projectPath', str(PROJECT), '-executeMethod', 'Bpsim.QA.AnvilParityQA.RunActualWheelPrefabsBatch' if actual else 'Bpsim.QA.AnvilParityQA.RunWheelFrictionBatch',
                             '-wheel-label', label, '-logFile', str(log)], cwd=ROOT, timeout=600,
                            creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    stem = 'wheel-actual' if actual else 'wheel-friction'
    report = PROJECT / f'Logs/{stem}-{label}.json'
    if result.returncode or not report.is_file():
        print(log.read_text(encoding='utf-8-sig', errors='replace')[-3500:])
        raise RuntimeError(f'Private Unity failed: exit={result.returncode}; {log}')
    destination = ROOT / f'Logs/{stem}-{label}.json'
    shutil.copy2(report, destination)
    data = json.loads(report.read_text(encoding='utf-8-sig'))
    print(f'{"PASS" if data["pass"] else "FAIL"}: {len(data["measurements"])} checks; {destination}')
    for item in data['measurements']:
        if not item['pass'] or item['metric'] in ('final-speed', 'distance'):
            print(item['backend'], item['scenario'], item['metric'], item['value'], item.get('detail', ''))
    if not data['pass']:
        raise RuntimeError('Wheel coasting contracts failed')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--prepare', action='store_true')
    parser.add_argument('--skip-host', action='store_true', help='Only prepare isolated physics while unrelated source edits are in progress')
    parser.add_argument('--label')
    parser.add_argument('--actual', action='store_true')
    args = parser.parse_args()
    if args.prepare: prepare(args.skip_host)
    if args.label: run(args.label, args.actual)
