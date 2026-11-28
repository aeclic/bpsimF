import json, os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "UnityMcp"))
import legacy_client

data = legacy_client.write("invoke_static", {
    "type": "Bpsim.EditorTools.WindowsPlayerBuild",
    "method": "Build",
    "args": [sys.argv[1]] if len(sys.argv) > 1 else [],
}, agent="bridge-build", ttl=600)
print(json.dumps(data)[:4000])
