"""Require actual prefab coverage, coasting controls, welded chassis and independent A/A."""
import argparse
import copy
import json
import math
from pathlib import Path

BACKENDS = ['UnityPhysics', 'HavokPhysics', 'AnvilPhysics']
WHEELS = ['7_CartWheel/Part_CartWheel_0', '8_NormalWheel/Part_NormalWheel_0', '9_SmallWheel/Part_SmallWheel_0',
          '10_MotorWheel/Part_MotorWheel_0', '10_MotorWheel/Part_MotorWheel_7', '38_StickyWheel/Part_StickyWheel_0']
GROUPS = [('7_CartWheel', 22), ('8_NormalWheel', 14), ('9_SmallWheel', 17), ('10_MotorWheel', 15), ('38_StickyWheel', 8)]
EXPECTED = {('UnityPhysics', 'actual-prefab-collider-count', 'contract-error')}
index = 0
for group, count in GROUPS:
    for _ in range(count):
        index += 1
        EXPECTED.add(('UnityPhysics', f'prefab-material-{group}-{index}', 'contract-error'))
for backend in BACKENDS:
    for scenario in [w + suffix for w in WHEELS for suffix in ['-fixed', '-old-control']] + ['welded-chassis-fixed']:
        for metric in ['coast-error', 'speed', 'distance']: EXPECTED.add((backend, scenario, metric))


def validate(report):
    assert report['pass'] and report['failures'] == 0 and not report.get('referenceDeviations')
    rows = {(r['backend'], r['scenario'], r['metric']): r for r in report['measurements']}
    assert len(rows) == len(report['measurements']) == 194 and set(rows) == EXPECTED, 'Actual coverage differs'
    for key, row in rows.items():
        assert row['pass'] and not row.get('referenceOnly') and math.isfinite(row['value']), key
        assert row['value'] <= row['tolerance'], key
        if key[2] == 'speed':
            distance = rows[key[0], key[1], 'distance']['value']
            assert (row['value'] < .5 and distance < 3) if key[1].endswith('old-control') else (row['value'] > 1.4 and distance > 7), key
    return rows


def compare(a, b):
    first, second = validate(a), validate(b)
    assert a['runId'] != b['runId'] and a['fingerprint'] == b['fingerprint'], 'Not independent same-build runs'
    assert a['physicsAssemblySha256'] == b['physicsAssemblySha256']
    floor = max(abs(first[k]['value'] - second[k]['value']) for k in first)
    assert floor <= 1e-4, f'A/A floor too high: {floor}'
    return floor


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('first', type=Path)
    parser.add_argument('second', type=Path)
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    a, b = [json.loads(p.read_text(encoding='utf-8-sig')) for p in [args.first, args.second]]
    print(f'PASS: 194 contracts x 2 independent actual-prefab runs; A/A floor={compare(a,b):.9g}')
    if args.self_test:
        for kind in ['missing', 'duplicate', 'exemption', 'same-run', 'fake-control', 'bad-chassis', 'trajectory-change']:
            changed = copy.deepcopy(b)
            if kind == 'missing': changed['measurements'].pop()
            if kind == 'duplicate': changed['measurements'].append(changed['measurements'][0])
            if kind == 'exemption': changed['measurements'][0]['referenceOnly'] = True
            if kind == 'same-run': changed['runId'] = a['runId']
            if kind in ['fake-control', 'bad-chassis', 'trajectory-change']:
                suffix = 'old-control' if kind == 'fake-control' else 'chassis-fixed' if kind == 'bad-chassis' else 'Wheel_0-fixed'
                row = next(r for r in changed['measurements'] if r['metric'] == 'speed' and r['scenario'].endswith(suffix))
                row['value'] = 4 if kind == 'fake-control' else 0 if kind == 'bad-chassis' else row['value'] - .1
            try: compare(a, changed)
            except AssertionError: continue
            raise AssertionError('Negative self-test not rejected: ' + kind)
        print('PASS: 7 actual-report checker negative self-tests')
