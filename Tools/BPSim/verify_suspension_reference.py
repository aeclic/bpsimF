"""Read-only APK/BPLE suspension contract instrument.

Replay: python Tools/BPSim/verify_suspension_reference.py --out Logs/suspension-oracle.json
PASS: 24 balloon / 14 bag profiles, expected Exp defaults, compiled initialization
IL (clone, physics, spring, pop/drop) and ARM64 balloon method tokens resolve.
Boundary: data/IL/native instruction evidence; no engine execution or frame parity.
Invalidated by oracle binary/settings/prefab or metadata ABI changes; hashes included.
Only --out and --scratch are written. --profiles optionally emits a new host resource.
"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

from inspect_archive_elastic_parts import inspect
from verify_bple_lights_umbrella import unity_objects


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def profiles(root):
    data = {'balloons': [], 'sandbags': [], 'settings': {}}
    fields = {'BalloonForce': 'balloonForce', 'BalloonConnectionDistance': 'balloonConnectionDistance',
              'RotatableBalloon': 'rotatableBalloon', 'UnpoppableBalloon': 'unpoppableBalloon',
              'SandbagConnectionDistance': 'sandbagConnectionDistance', 'RotatableSandbag': 'rotatableSandbag'}
    hashes = {}
    for name in ['INDeclarationSettingsExp', 'INSettingsBExp']:
        path = root / f'Assets/TextAsset/{name}.json'
        hashes[str(path.relative_to(root))] = sha(path)
        for item in json.loads(path.read_text(encoding='utf-8-sig'))['items']:
            if item['name'] in fields:
                data['settings'][fields[item['name']]] = item['value']
    for path in sorted((root / 'Assets/GameObject').glob('Part_*.prefab')):
        if not path.name.startswith(('Part_Balloon', 'Part_Sandbag')):
            continue
        for _, (kind, values) in unity_objects(path).items():
            if kind != 'MonoBehaviour' or 'm_partType' not in values:
                continue
            # Family is the source slot group, not its runtime BasePart rewrite.
            family = path.name.split('_')[1]
            family = {'Sandbags2': 'Sandbag2', 'Sandbags3': 'Sandbag3'}.get(family, family)
            row = {'family': family, 'index': int(values['customPartIndex']),
                   'count': int(values['m_numberOfBalloons']), 'mass': float(values['m_mass'])}
            if family.startswith('Balloon'):
                row.update(force=float(values['m_force']), maxSpeed=float(values['m_maximumSpeed']),
                           ghost=bool(values['m_ghostBalloon']))
                data['balloons'].append(row)
            else:
                data['sandbags'].append(row)
            hashes[str(path.relative_to(root))] = sha(path)
    assert len(data['balloons']) == 24 and len(data['sandbags']) == 14
    assert data['settings'] == {'balloonForce': 2.0, 'balloonConnectionDistance': 10,
                               'rotatableBalloon': True, 'unpoppableBalloon': True,
                               'sandbagConnectionDistance': 10, 'rotatableSandbag': True}
    return data, hashes


def compiled_il(root, scratch, mono, helper, unity):
    assembly = root / 'Library/ScriptAssemblies/Assembly-CSharp.dll'
    resolve = [root / 'Library/ScriptAssemblies', root / 'Assets/Plugins',
               unity / 'Managed/UnityEngine', unity / 'Managed']
    evidence = {'assembly_sha256': sha(assembly), 'methods': {}}
    methods = [('Balloon', 'Initialize'), ('Balloon', 'LimitForceForSpeed'),
               ('Balloon', 'OnCollisionEnter'), ('Balloon', 'Pop'),
               ('Sandbag', 'Initialize'), ('Sandbag', 'Drop'), ('Sandbag', 'LateUpdate'),
               ('BalloonBalancer', 'AddBalloon'), ('BalloonBalancer', 'RemoveBalloon'),
               ('BalloonBalancer', 'Configure'), ('Balloon', 'ConfigureExtraBalanceJoint'), ('Contraption', 'StartContraption')]
    for typename, method in methods:
        args = [mono, helper, assembly, 'il', typename, method] + resolve
        text = subprocess.check_output([str(x) for x in args], encoding='utf-8-sig', errors='strict')
        assert 'IL_0000' in text and 'method not found' not in text, f'No compiled IL: {typename}.{method}'
        file = scratch / f'{typename}.{method}.il'
        file.write_text(text, encoding='utf-8')
        evidence['methods'][f'{typename}.{method}'] = {'sha256': sha(file), 'file': str(file.resolve())}
        if method == 'Initialize':
            assert 'Object.Instantiate' in text and 'set_mass' in text and 'set_spring' in text
        if typename == 'Balloon' and method == 'Pop':
            assert 'm_popped' in text and 'Object.Destroy' in text
        if typename == 'Sandbag' and method == 'Drop':
            assert 'm_dropped' in text and 'AddForce' in text and 'Object.Destroy' in text
        if typename == 'BalloonBalancer' and method == 'Configure':
            assert 'ldc.r4 20' in text
        if typename == 'Contraption' and method == 'StartContraption':
            assert 'ConfigureExtraBalanceJoint' in text
    return evidence


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--bple', type=Path, default=Path(r'D:/rpgvx + magpie/BPunity Project/BPLE 2022.1.9[工程文件]'))
    parser.add_argument('--apk', type=Path, default=Path('bpsimarchive.apk'))
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--scratch', type=Path, default=Path('_codex_work/balloon-sandbag-reference'))
    parser.add_argument('--profiles', type=Path)
    parser.add_argument('--mono', type=Path, default=Path('D:/folder/2022.2.23f1/Editor/Data/MonoBleedingEdge/bin/mono.exe'))
    parser.add_argument('--dump-helper', type=Path, default=Path('_work/taffy_decomp/dump_taffy.exe'))
    parser.add_argument('--unity-data', type=Path, default=Path('D:/folder/2022.2.23f1/Editor/Data'))
    args = parser.parse_args()
    args.scratch.mkdir(parents=True, exist_ok=True)
    data, hashes = profiles(args.bple)
    il = compiled_il(args.bple, args.scratch, args.mono, args.dump_helper, args.unity_data)
    native = inspect(args.apk, wanted={'Bpsim.Parts.Balloon', 'Bpsim.Parts.BalloonBody',
        'Bpsim.Parts.BalloonBalancer', 'Bpsim.Parts.BalloonSystem', 'Bpsim.Parts.BalloonCollisionSystem'},
        disassemble={'LimitForceForSpeed', 'FindTarget', 'EligibleTarget', 'InitializeBody',
                     'AddBalancer', 'ReleaseBalancer', 'RequestPop', 'SelectSingleVisualization'}, max_bytes=4096)
    output = {'verdict': 'PASS', 'profiles': data, 'source_files_sha256': hashes,
              'bple_compiled_il': il, 'apk': native, 'boundary': 'No APK/BPLE dynamic or render-parity assertion'}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(output, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    if args.profiles:
        args.profiles.parent.mkdir(parents=True, exist_ok=True)
        args.profiles.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
    print(f'PASS: 24 balloon / 14 bag profiles; {len(il["methods"])} BPLE compiled methods; 5 APK types; {args.out}')
