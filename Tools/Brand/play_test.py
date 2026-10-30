
import os, sys, time
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import ubridge
from PIL import ImageGrab

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "_final")
OUT = os.path.abspath(OUT)

def state():
    try:
        r = ubridge.call("get_property", {"target": "BrandSplash", "component": "Bpsim.UI.BrandSplashController", "path": "State"})
        res = r.get("result")
        return res if isinstance(res, str) else str(res)
    except Exception as e:
        return "bridge-down(%s)" % type(e).__name__

def grab(name):
    im = ImageGrab.grab()
    p = os.path.join(OUT, "_play_%s.png" % name)
    im.save(p)
    return p

print("enter:", ubridge.call("playmode", {"action": "enter"}).get("result"))
t0 = time.time()
shots = {2.0: "t2", 4.0: "t4", 6.5: "t65", 9.0: "t9"}
done = set()
while time.time() - t0 < 20:
    time.sleep(0.9)
    el = time.time() - t0
    st = state()
    print("%5.1fs  %s" % (el, st[:90]))
    for k, name in shots.items():
        if name not in done and el >= k:
            print("   shot ->", grab(name))
            done.add(name)
print("final:", state())
