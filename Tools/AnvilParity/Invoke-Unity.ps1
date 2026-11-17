param(
    [ValidateSet('Status', 'Compile', 'Run')][string]$Action = 'Status',
    [string]$Label = 'current'
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
    print(json.dumps(b.read('compile_status', {'count': 10}), ensure_ascii=False))
else:
    lease = b.acquire('anvil-physics-parity-20261106',
                      'Compile or run isolated physics parity tests; no scene/Play Mode changes', 600)
    token = lease['token']
    try:
        if action == 'Compile':
            result = b.write('compile', token, {})
        else:
            result = b.write('invoke_static', token, {
                'type': 'Bpsim.QA.AnvilParityQA', 'method': 'Run', 'args': [label]})
        print(json.dumps(result, ensure_ascii=False))
    finally:
        # A timed-out write must be inspected, not repeated or silently acknowledged.
        if b.load().get('pending') is None:
            b.release(token)
        else:
            print('Uncertain write preserved. Inspect Unity and the report before acknowledgement.', file=sys.stderr)
'@
& python -X utf8 -c $payload $workspace $Action $Label
if ($LASTEXITCODE -ne 0) { throw "Unity parity command failed (exit $LASTEXITCODE)" }
