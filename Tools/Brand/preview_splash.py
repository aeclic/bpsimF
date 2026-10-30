
import os, re, subprocess, sys, tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import ROOT, OUT, CHROME
from PIL import Image

PAGE = os.path.join(ROOT, "Assets", "StreamingAssets", "WebPanels", "brand-splash.html")
src = open(PAGE, encoding="utf-8").read()
for t in (2600, 3400):
    seek = ('<script>window.onload=function(){document.getAnimations().forEach(function(a){a.pause();a.currentTime=%d;});};</script>' % t)
    tmp = tempfile.mkdtemp(); p = os.path.join(tmp, "s.html")
    open(p, "w", encoding="utf-8").write(src.replace("</body>", seek + "</body>"))
    raw = os.path.join(tmp, "s.png")
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    "--virtual-time-budget=1500", "--force-device-scale-factor=1",
                    "--window-size=1920,1080", "--screenshot=" + raw,
                    "file:///" + p.replace("\\", "/")], capture_output=True)
    im = Image.open(raw)
    im.save(os.path.join(OUT, "_splash_preview_%d.png" % t))
    print(t, im.size)
