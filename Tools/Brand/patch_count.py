
import os, re, sys, json
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import ROOT

SVGD = os.path.join(ROOT, "Brand", "svg")
REF = os.path.join(ROOT, "Tools", "Brand", "ref")
b_old = open(os.path.join(REF, "b_old.txt"), encoding="utf-8").read().strip()
s_old = open(os.path.join(REF, "s_old.txt"), encoding="utf-8").read().strip()
b_new = open(os.path.join(REF, "b_A.txt"), encoding="utf-8").read().strip()
s_new = open(os.path.join(REF, "s_new.txt"), encoding="utf-8").read().strip()
print("lengths:", len(b_old), len(s_old), "->", len(b_new), len(s_new))
counts = {}
for fn in sorted(os.listdir(SVGD)):
    if not fn.endswith(".svg"): continue
    t = open(os.path.join(SVGD, fn), encoding="utf-8").read()
    counts[fn] = (t.count(b_old), t.count(s_old))
print(json.dumps(counts, indent=1))
