
import os, sys, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import ROOT
import xml.etree.ElementTree as ET

SVGD = os.path.join(ROOT, "Brand", "svg")
REF = os.path.join(ROOT, "Tools", "Brand", "ref")
b_old = open(os.path.join(REF, "b_old.txt"), encoding="utf-8").read().strip()
s_old = open(os.path.join(REF, "s_old.txt"), encoding="utf-8").read().strip()
b_new = open(os.path.join(REF, "b_A.txt"), encoding="utf-8").read().strip()
s_new = open(os.path.join(REF, "s_new.txt"), encoding="utf-8").read().strip()

report = {}
for fn in sorted(os.listdir(SVGD)):
    if not fn.endswith(".svg"): continue
    p = os.path.join(SVGD, fn)
    t = open(p, encoding="utf-8").read()
    nb = t.count(b_old); ns = t.count(s_old)
    if nb == 0 and ns == 0:
        report[fn] = "untouched"; continue
    t2 = t.replace(b_old, b_new).replace(s_old, s_new)
    try:
        ET.fromstring(t2.encode("utf-8"))
    except Exception as e:
        report[fn] = "XML FAIL: %s" % e; continue
    open(p, "w", encoding="utf-8").write(t2)
    report[fn] = "b x%d s x%d -> %d bytes" % (nb, ns, len(t2))
print(json.dumps(report, indent=1, ensure_ascii=False))
