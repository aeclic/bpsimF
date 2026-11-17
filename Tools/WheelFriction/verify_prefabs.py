"""Actual PartResources prefab material contract, not only unused JSON templates.

--out <JSON> records every actual collider profile. --expect-fixed requires all to match.
--patch <patch file> produces (does not apply) missing-asset/reference repair.
Floor: two independent prefab reads must produce identical profile rows/hashes.
Boundary: serialized live-provider data; runtime/assembled coasting is separate.
"""
import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'BPSim'))
from verify_bple_lights_umbrella import unity_objects

ROOT = Path(__file__).resolve().parents[2]
MATERIALS = {
    (.025, 0, 0, 'Multiply', 'Multiply'): ('WheelTread', 'c2166d0f29084325aa004f3e24c55bcf'),
    (.05, 0, 0, 'Multiply', 'Multiply'): ('WoodenWheelTread', '68dcd4efbf41428490497b57f2085336'),
    (.7, .5, 0, 'Average', 'Average'): ('WheelSupport', 'c1e5fa876a094a7db889dcf2c4b2ffcf'),
}
POLICIES = {'Average': 0, 'Minimum': 1, 'Multiply': 2, 'Maximum': 3}


def profile(data, template=False):
    keys = ['dynamicFriction', 'staticFriction', 'bounciness', 'frictionCombine', 'bounceCombine']
    result = tuple(data[k] for k in keys)
    if not template:
        reverse = {v: k for k, v in POLICIES.items()}
        result = (*result[:3], reverse[result[3]], reverse[result[4]])
    return result


def inspect():
    rows, changes, hashes = [], {}, {}
    for family, group in [('CartWheel', 7), ('NormalWheel', 8), ('SmallWheel', 9), ('MotorWheel', 10), ('StickyWheel', 38)]:
        templates = ROOT / f'Assets/TextAsset/{family}_TemplateGroup.json'
        expected = {}
        def walk(node, parent=''):
            path = parent + '/' + node['name'] if parent else node['name']
            for c in node.get('components', []):
                if c.get('type') == 'Collider': expected[path] = profile(c, True)
            for child in node.get('children', []): walk(child, path)
        for node in json.loads(templates.read_text(encoding='utf-8-sig')): walk(node)
        for node in json.loads((ROOT / 'Assets/TextAsset/Innovation_TemplateGroup.json').read_text(encoding='utf-8-sig')):
            if any(c.get('type') == 'Part' and c.get('partType') == family for c in node.get('components', [])):
                walk(node)
        prefab = ROOT / f'Assets/GameObject/PartGroup_{group}_{family}.prefab'
        objects = unity_objects(prefab)
        transforms = {v['m_GameObject']['fileID']: (obj_id, v) for obj_id, (kind, v) in objects.items() if kind == 'Transform'}
        def path(go):
            name = objects[go][1]['m_Name']
            father = transforms[go][1]['m_Father']['fileID']
            if not father: return name
            return path(objects[father][1]['m_GameObject']['fileID']) + '/' + name
        found = set()
        for obj_id, (kind, data) in objects.items():
            if kind not in ('SphereCollider', 'BoxCollider', 'CapsuleCollider'): continue
            relative = path(data['m_GameObject']['fileID']).split('/', 1)[1]
            assert relative in expected, f'Collider missing template correspondence: {relative}'
            found.add(relative)
            source = expected[relative]
            name, guid = MATERIALS[source]
            actual_guid = data['m_Material'].get('guid')
            asset = ROOT / f'Assets/PhysicsMaterials/{name}.physicMaterial'
            matches = actual_guid == guid and asset.is_file() and profile(next(iter(unity_objects(asset).values()))[1]) == source
            rows.append(dict(family=family, node=relative, kind=kind, profile=source, actualGuid=actual_guid, match=matches))
            if actual_guid != guid:
                assert not actual_guid, f'Explicit existing material must not be overwritten: {relative}'
                owner = data['m_GameObject']['fileID']
                changes.setdefault(str(prefab.relative_to(ROOT)).replace('\\', '/'), []).append(
                    '@@\n   m_GameObject: {fileID: ' + str(owner) + '}\n-  m_Material: {fileID: 0}\n'
                    '+  m_Material: {fileID: 13400000, guid: ' + guid + ', type: 2}\n')
        assert found == set(expected), f'Prefab/template collider coverage differs for {family}'
        hashes[str(prefab.relative_to(ROOT))] = hashlib.sha256(prefab.read_bytes()).hexdigest()
    return dict(rows=rows, count=len(rows), matches=sum(r['match'] for r in rows), hashes=hashes), changes


def patch_text(changes):
    text = '*** Begin Patch\n'
    text += '*** Add File: Assets/PhysicsMaterials.meta\n+fileFormatVersion: 2\n+guid: e6cf6b030a1646b88b62692f5aa24819\n+folderAsset: yes\n'
    for values, (name, guid) in MATERIALS.items():
        data = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!134 &13400000\nPhysicMaterial:\n'
        data += '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n'
        data += f'  m_Name: {name}\n  dynamicFriction: {values[0]}\n  staticFriction: {values[1]}\n  bounciness: {values[2]}\n  frictionCombine: {POLICIES[values[3]]}\n  bounceCombine: {POLICIES[values[4]]}\n'
        text += f'*** Add File: Assets/PhysicsMaterials/{name}.physicMaterial\n' + ''.join('+' + line + '\n' for line in data.splitlines())
        text += f'*** Add File: Assets/PhysicsMaterials/{name}.physicMaterial.meta\n+fileFormatVersion: 2\n+guid: {guid}\n'
    for filename, hunks in changes.items(): text += '*** Update File: ' + filename + '\n' + ''.join(hunks)
    return text + '*** End Patch\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', type=Path, required=True)
    parser.add_argument('--patch', type=Path)
    parser.add_argument('--expect-fixed', action='store_true')
    args = parser.parse_args()
    data, changes = inspect()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
    if args.patch:
        args.patch.parent.mkdir(parents=True, exist_ok=True)
        args.patch.write_text(patch_text(changes), encoding='utf-8')
    print(f'Actual wheel prefabs: {data["matches"]}/{data["count"]} materials match BPLE/template contracts')
    if args.expect_fixed and data['matches'] != data['count']: raise SystemExit(1)
