param([ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label = 'preview-before', [switch]$VerifyLights)
$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$payload = @'
import json, pathlib, sys, time
root = pathlib.Path(sys.argv[1]).resolve()
label = sys.argv[2]
verify_lights = sys.argv[3] == 'True'
sys.path.insert(0, str(root / 'Tools/UnityMcp'))
from bridge import Bridge
meta = json.loads((root / 'Temp/DshBridge/instance.json').read_text(encoding='utf-8'))
assert pathlib.Path(meta['project']).resolve() == root
b = Bridge(root, url='http://127.0.0.1:' + str(meta['port']))
initial = b.status()
assert not initial['editor_busy'], 'Wait for stable Unity before starting'
lease = b.acquire('bple-preview-stutter-20261106', 'Controlled preview fixtures, original build/settings/Play Mode restored', 600)
token = lease['token']
entered = False
try:
    if not initial['playing']:
        print(json.dumps(b.write('playmode', token, {'action': 'enter'}), ensure_ascii=False), flush=True)
        entered = True
    deadline = time.monotonic() + 120
    while True:
        state = b.status()
        if state['playing'] and not state['editor_busy']:
            # FindObjectsOfType omits DontSave hosts such as [BPLE Lights]. Read actual
            # singleton/manager readiness rather than treating an invisible host as missing.
            ready = b.write('invoke_static', token, {'type': 'Bpsim.QA.BpleLightStutterQA', 'method': 'Readiness', 'args': []})
            if ready == 'ready': break
            if ready == 'simulating': raise RuntimeError('User simulation is active; no scene fixture was started')
        if time.monotonic() >= deadline:
            raise RuntimeError('Unity/light manager readiness deadline exceeded; no write retry issued')
        time.sleep(1)
    allocation_probe = b.write('invoke_static', token, {'type': 'Bpsim.QA.BpleLightStutterQA', 'method': 'AllocationCounterProbe', 'args': []})
    print(json.dumps(allocation_probe, ensure_ascii=False), flush=True)
    (root / ('Logs/allocation-counter-' + label + '.json')).write_text(json.dumps({'result': allocation_probe}) + '\n', encoding='utf-8')
    for suffix, method in [('live', 'RunLifecycle'), ('a', 'RunPreviewProbe'), ('b', 'RunPreviewProbe')]:
        run_label = label + '-' + suffix
        result = b.write('invoke_static', token, {'type': 'Bpsim.QA.BpleLightStutterQA', 'method': method, 'args': [run_label]})
        print(json.dumps(result, ensure_ascii=False), flush=True)
        path = root / 'Logs' / (('bple-light-lifecycle-' if method == 'RunLifecycle' else 'bple-preview-') + run_label + '.json')
        assert path.exists(), 'No report: ' + str(result)
        report = json.loads(path.read_text(encoding='utf-8-sig'))
        if method == 'RunLifecycle':
            assert report['failures'] == 0 and report['playing']
        else:
            assert len(report['cases']) == 3 and not report['errors'] and all(c['pass'] for c in report['checks']), report
    if verify_lights:
        started = time.time()
        result = b.write('invoke_static', token, {'type': 'Bpsim.QA.ScaleLightsUmbrellaQA', 'method': 'Run', 'args': ['lights']})
        print(json.dumps(result, ensure_ascii=False), flush=True)
        report_path = root / 'Logs/bpsim-scale-lights-umbrella.log'
        deadline = time.monotonic() + 180
        while not report_path.exists() or report_path.stat().st_mtime < started:
            if time.monotonic() >= deadline:
                raise RuntimeError('Light regression deadline exceeded; inspect runner before another invocation')
            time.sleep(1)
        text = report_path.read_text(encoding='utf-8-sig')
        (root / ('Logs/bple-light-regression-' + label + '.log')).write_text(text, encoding='utf-8')
        print(text.splitlines()[0], flush=True)
        assert text.startswith('done:PASS'), text
finally:
    if b.load().get('pending') is None:
        if entered:
            print(json.dumps(b.write('playmode', token, {'action': 'exit'}), ensure_ascii=False), flush=True)
        if b.load().get('pending') is None:
            b.release(token)
    else:
        print('Uncertain write retained. Inspect Unity/reports before acknowledgement; Play Mode was not blindly changed.', file=sys.stderr)
'@
& python -X utf8 -c $payload $workspace $Label $VerifyLights.IsPresent.ToString()
if ($LASTEXITCODE -ne 0) { throw 'Preview run failed; inspect before starting another run' }
