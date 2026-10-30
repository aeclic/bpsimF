import json, sys, urllib.request
BASE="http://127.0.0.1:8791"
data=json.dumps({"method":"invoke_static","params":{"type":"Bpsim.EditorTools.WindowsPlayerBuild","method":"Build","args":[sys.argv[1]]}}).encode("utf-8")
req=urllib.request.Request(BASE+"/api", data=data, headers={"Content-Type":"application/json"})
try:
    with urllib.request.urlopen(req, timeout=900) as r:
        print(r.read().decode("utf-8")[:4000])
except Exception as e:
    print("ERR", type(e).__name__, e)
