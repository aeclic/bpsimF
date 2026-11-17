"""Compiled-host structural regression; not a physical or screenshot oracle.

Run: python Tools/BPSim/verify_suspension_baseline.py --out Logs/suspension-baseline.json --record-baseline
PASS after implementation: no proximity-lift/toggle IL, balloon body state exists,
Sandbag Bind creates a real tether, and corrected search defaults match BPLE.
--record-baseline captures expected pre-fix FAIL without a failing process exit.
Boundary: code/data structure only. Runtime QA must independently prove effects.
Invalidated by host source/assembly changes; SHA256 and retained IL are included.
"""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path


def dump(mode, typename='', method=''):
    command = ['D:/folder/2022.2.23f1/Editor/Data/MonoBleedingEdge/bin/mono.exe',
               '_work/taffy_decomp/dump_taffy.exe', 'Library/ScriptAssemblies/Assembly-CSharp.dll', mode]
    if typename:
        command.append(typename)
    if method:
        command.append(method)
    command += ['Library/ScriptAssemblies', 'Assets/Plugins',
                'D:/folder/2022.2.23f1/Editor/Data/Managed/UnityEngine',
                'D:/folder/2022.2.23f1/Editor/Data/Managed']
    return subprocess.check_output(command, encoding='utf-8-sig')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--record-baseline', action='store_true')
    parser.add_argument('--scratch', type=Path, default=Path('_codex_work/balloon-sandbag-reference'))
    args = parser.parse_args()
    args.scratch.mkdir(parents=True, exist_ok=True)
    methods = {}
    for typename, method in [('Bpsim.Parts.Gameplay.BalloonBehavior', 'Tick'),
                             ('Bpsim.Parts.Gameplay.BalloonBehavior', 'Trigger'),
                             ('Bpsim.Parts.Simulation.SandbagSystem', 'Bind')]:
        value = dump('il', typename, method)
        (args.scratch / f'host-{typename}-{method}.il').write_text(value, encoding='utf-8')
        methods[method] = value
    names = dump('list')
    bag = Path('Assets/Scripts/Assembly-CSharp/Bpsim/Parts/Sandbag.cs').read_text(encoding='utf-8-sig')
    checks = {
        'balloon-no-nearby-lift': 'LiftNearby' not in methods['Tick'],
        'balloon-no-reinflate-toggle': not ('ceq' in methods['Trigger'] and 'PartBehavior.Enabled' in methods['Trigger']),
        'balloon-real-body-state': 'TYPE Bpsim.Parts.BalloonBody' in names,
        'sandbag-real-tether': 'CreateTether' in methods['Bind'],
        'sandbag-search-defaults': bool(re.search(r'DefaultConnectionDistance\s*=\s*10\s*;', bag))
            and bool(re.search(r'DefaultRotatable\s*=\s*true\s*;', bag))
    }
    result = {'verdict': 'PASS' if all(checks.values()) else 'FAIL', 'checks': checks,
              'host_assembly_sha256': hashlib.sha256(Path('Library/ScriptAssemblies/Assembly-CSharp.dll').read_bytes()).hexdigest(),
              'sandbag_source_sha256': hashlib.sha256(bag.encode()).hexdigest(),
              'boundary': 'Compiled structure/data; not runtime physics or visual acceptance'}
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(result['verdict'], '; '.join(f'{name}={passed}' for name, passed in checks.items()))
    if not all(checks.values()) and not args.record_baseline:
        raise SystemExit(1)
