param(
    [ValidateSet('Status', 'Compile', 'Refresh', 'LightLifecycle', 'Preview')][string]$Action = 'Status',
    [ValidatePattern('^[a-zA-Z0-9_-]+$')][string]$Label = 'current'
)
$ErrorActionPreference = 'Stop'
$workspace = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$payload = @'
import json, pathlib, sys
root = pathlib.Path(sys.argv[1]).resolve()
sys.path.insert(0, str(root / 'Tools/UnityMcp'))
from bridge import Bridge
meta = json.loads((root / 'Temp/DshBridge/instance.json').read_text(encoding='utf-8'))
if pathlib.Path(meta['project']).resolve() != root:
    raise RuntimeError('Bridge discovery refers to a different project')
b = Bridge(root, url='http://127.0.0.1:' + str(meta['port']))
state = b.status()
print(json.dumps(state, ensure_ascii=False))
action, label = sys.argv[2:4]
if action == 'Status':
    print(json.dumps(b.read('compile_status', {'count': 8}), ensure_ascii=False))
else:
    purpose = 'Temporary build-preview fixture; restore original scene/settings' if action == 'Preview' else 'Lifecycle validation or compilation; no scene/Play Mode changes'
    lease = b.acquire('bple-light-anvil-stutter-20261106', purpose, 600)
    token = lease['token']
    try:
        if action == 'Compile':
            result = b.write('compile', token, {})
        elif action == 'Refresh':
            path = 'Assets/Scripts/Assembly-CSharp/Bpsim/QA/BpleLightStutterQA.cs'
            result = b.write('write_script', token, {'path': path, 'content': (root / path).read_text(encoding='utf-8')})
        else:
            result = b.write('invoke_static', token, {
                'type': 'Bpsim.QA.BpleLightStutterQA', 'method': 'RunPreviewProbe' if action == 'Preview' else 'RunLifecycle', 'args': [label]})
        print(json.dumps(result, ensure_ascii=False))
    finally:
        if b.load().get('pending') is None:
            b.release(token)
        else:
            print('Uncertain write retained. Inspect Unity and the report before acknowledgement.', file=sys.stderr)
'@
& python -X utf8 -c $payload $workspace $Action $Label
if ($LASTEXITCODE -ne 0) { throw "Unity stutter command failed (exit $LASTEXITCODE)" }
