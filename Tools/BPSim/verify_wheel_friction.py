"""Read-only BPLE wheel-material / local template contract instrument.

Replay: python Tools/BPSim/verify_wheel_friction.py --out Logs/wheel-oracle.json
PASS: each of the five local wheel families has low-friction Multiply tread;
BPLE wheel materials are 0.025/0.05 with zero static friction and Multiply.
Floor: two independent processes must produce identical JSON/hashes.
Boundary: serialized data only, not full BPLE gameplay equivalence.
Invalidated by source prefab, material or template changes (hashes recorded).
"""
import argparse
import hashlib
import json
import re
from pathlib import Path

from verify_bple_lights_umbrella import unity_objects


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def contract(oracle, root):
    hashes, materials, treads = {}, {}, []
    for path in (oracle / 'Assets/PhysicMaterial').glob('*.physicMaterial'):
        values = next(iter(unity_objects(path).values()))[1]
        meta = path.with_suffix(path.suffix + '.meta')
        guid = re.search(r'^guid: (\w+)', meta.read_text(encoding='utf-8-sig'), re.M)[1]
        materials[guid] = dict(name=values['m_Name'], friction=values['dynamicFriction'],
                               staticFriction=values['staticFriction'], policy=values['frictionCombine'])
        hashes[str(path.relative_to(oracle))] = digest(path)
    for family in ['CartWheel', 'NormalWheel', 'SmallWheel', 'MotorWheel', 'StickyWheel']:
        templates = root / f'Assets/TextAsset/{family}_TemplateGroup.json'
        hashes[str(templates.relative_to(root))] = digest(templates)
        def visit(node, index):
            for component in node.get('components', []):
                if component.get('type') == 'Collider' and component['frictionCombine'] == 'Multiply':
                    assert component['dynamicFriction'] in (0.025, 0.05)
                    assert component['staticFriction'] == 0
                    treads.append(dict(family=family, index=index, node=node['name'],
                                       shape=component['colliderType'], friction=component['dynamicFriction']))
            for child in node.get('children', []):
                visit(child, index)
        for index, item in enumerate(json.loads(templates.read_text(encoding='utf-8-sig'))):
            visit(item, index)
        assert any(t['family'] == family for t in treads)
        for path in (oracle / 'Assets/GameObject').glob(f'Part_{family}_*_SET.prefab'):
            for _, (kind, values) in unity_objects(path).items():
                if kind not in ('SphereCollider', 'BoxCollider', 'CapsuleCollider'):
                    continue
                material = materials.get(values.get('m_Material', {}).get('guid'))
                if material and material['policy'] == 2:
                    assert material['friction'] in (0.025, 0.05) and material['staticFriction'] == 0
            hashes[str(path.relative_to(oracle))] = digest(path)
    wheels = [m for m in materials.values() if 'WheelFriction' in m['name']]
    assert sorted(m['friction'] for m in wheels) == [0.025, 0.05]
    assert all(m['staticFriction'] == 0 and m['policy'] == 2 for m in wheels)
    return dict(materials=sorted(wheels, key=lambda m: m['name']), treads=treads,
                hashes=hashes, boundary='Serialized BPLE/local data; no dynamic parity claim')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--oracle', type=Path, default=Path(r'D:/rpgvx + magpie/BPunity Project/BPLE 2022.1.9[工程文件]'))
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    data = contract(args.oracle, Path.cwd())
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
    print(f'PASS: {len(data["treads"])} tread colliders across 5 families; BPLE friction=0.025/0.05, static=0, Multiply')
