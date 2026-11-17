"""Strict 60-contract coverage, independent A/A floor and negative-control checker.

python Tools/WheelFriction/check_reports.py Logs/wheel-friction-a.json Logs/wheel-friction-b.json --self-test
Serialized/material coefficient checks are exact; independent trajectory floor <=1e-4 m/s or m.
Rejects missing/duplicate checks, exemptions, nonfinite data and a mislabeled passing baseline.
"""
import argparse
import copy
import json
import math
from pathlib import Path

BACKENDS = ['UnityPhysics', 'HavokPhysics', 'AnvilPhysics']
SCENARIOS = ['tread-0.025', 'tread-0.050', 'default-friction-control']
METRICS = ['speed-gain', 'ground-height-error', 'coasting-failure', 'final-speed', 'distance']
MATERIALS = ['null-material-default', *['material-policy-' + p for p in ['Average', 'Minimum', 'Multiply', 'Maximum']],
             *[p + k for k in ['BoxCollider', 'SphereCollider', 'CapsuleCollider'] for p in ['factory-', 'scaled-']],
             'compound-tread-support', 'scaled-compound-tread-support', 'authored-average-does-not-override-tread', 'zero-friction-surface']
EXPECTED = {(b, s, m) for b in BACKENDS for s in SCENARIOS for m in METRICS}
EXPECTED.update(('UnityPhysics', s, 'contract-error') for s in MATERIALS)


def validate(report):
    assert report['pass'] and report['failures'] == 0 and not report.get('referenceDeviations'), 'Reported failure/exemption'
    rows = {(r['backend'], r['scenario'], r['metric']): r for r in report['measurements']}
    assert len(rows) == len(report['measurements']) == 60 and set(rows) == EXPECTED, 'Missing, duplicate or unexpected checks'
    for key, row in rows.items():
        assert row['pass'] and not row.get('referenceOnly'), f'Failed/exempted check {key}'
        assert math.isfinite(row['value']) and math.isfinite(row['tolerance']), f'Nonfinite {key}'
        assert row['value'] <= row['tolerance'], f'Error exceeds tolerance {key}'
    for backend in BACKENDS:
        for scenario in SCENARIOS:
            speed = rows[backend, scenario, 'final-speed']['value']
            distance = rows[backend, scenario, 'distance']['value']
            assert (speed < .5 and distance < 5) if scenario == 'default-friction-control' else (3.3 < speed <= 5.002 and distance > 10), (backend, scenario, speed, distance)
            assert rows[backend, scenario, 'coasting-failure']['value'] == 0
            assert rows[backend, scenario, 'speed-gain']['value'] <= .002
            assert rows[backend, scenario, 'ground-height-error']['value'] <= .06
    return rows


def compare(a, b):
    first, second = validate(a), validate(b)
    assert a['runId'] != b['runId'], 'A/A must be independent executions'
    assert a['fingerprint'] == b['fingerprint'] and a['physicsAssemblySha256'] == b['physicsAssemblySha256'], 'Changed build fingerprint'
    floor = max(abs(first[k]['value'] - second[k]['value']) for k in first)
    assert floor <= 1e-4, f'A/A exceeds declared 1e-4 floor: {floor}'
    return floor


def self_test(a, b):
    for kind in ['missing', 'duplicate', 'exemption', 'failed', 'fake-baseline', 'same-run', 'trajectory-change']:
        changed = copy.deepcopy(b)
        if kind == 'missing': changed['measurements'].pop()
        if kind == 'duplicate': changed['measurements'].append(changed['measurements'][0])
        if kind == 'exemption': changed['measurements'][0]['referenceOnly'] = True
        if kind == 'failed': changed['measurements'][0]['pass'] = False
        if kind == 'same-run': changed['runId'] = a['runId']
        if kind in ('fake-baseline', 'trajectory-change'):
            for row in changed['measurements']:
                if row['metric'] == 'final-speed' and row['scenario'] == ('default-friction-control' if kind == 'fake-baseline' else 'tread-0.025'):
                    row['value'] = 4 if kind == 'fake-baseline' else row['value'] - .1
                    break
        try: compare(a, changed)
        except AssertionError: continue
        raise AssertionError('Checker failed negative self-test ' + kind)
    print('PASS: 7 checker negative self-tests')


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('first', type=Path)
    parser.add_argument('second', type=Path)
    parser.add_argument('--self-test', action='store_true')
    args = parser.parse_args()
    a, b = [json.loads(path.read_text(encoding='utf-8-sig')) for path in [args.first, args.second]]
    print(f'PASS: 60 contracts x 2 independent runs; A/A max floor={compare(a, b):.9g}')
    if args.self_test: self_test(a, b)
