
import os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import *
from shapely.geometry import Polygon, Point
from shapely import affinity as AF
from PIL import Image, ImageDraw
import numpy as np
from skimage import measure

REFD = open(os.path.join(HERE, "ref", "s_path.txt"), encoding="utf-8").read()

# ---- 1) trace the reference s (padded so the contour closes) ----
PAD = 8.0
bx, by, bw, bh = 500.74, 70.44, 122.93, 145.76
VB = (bx - PAD, by - PAD, bw + 2*PAD, bh + 2*PAD)
SCALE = 9.0
W = round(VB[2] * SCALE); H = round(VB[3] * SCALE)
svg = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="%f %f %f %f"><path d="%s" fill="#ffffff"/></svg>'
       % (VB[0], VB[1], VB[2], VB[3], REFD))
raw = os.path.join(OUT, "_ref_s_trace.png")
shoot(svg, W, H, raw, bg="transparent", scale=2.0)
al = np.array(Image.open(raw).convert("RGBA"))[:, :, 3]
cs = measure.find_contours(al.astype(float), 127.0)
cs.sort(key=len, reverse=True)
print("contours:", [len(c) for c in cs[:4]])
cont = cs[0]
pts = [(VB[0] + c[1] * VB[2] / W, VB[1] + c[0] * VB[3] / H) for c in cont]
ref = Polygon(pts).buffer(0)
print("traced bbox:", ["%.2f" % v for v in ref.bounds], "area %.1f (ref 10927.0)" % ref.area)

# ---- 2) map into our s slot (exact bbox match) ----
lp = re.findall(r'<path d="([^"]+)"', open(SVG, encoding="utf-8").read())
old_s = Polygon(parse(lp[2])[0]).buffer(0); sb = old_s.bounds
rb = ref.bounds
sx = (sb[2]-sb[0])/(rb[2]-rb[0]); sy = (sb[3]-sb[1])/(rb[3]-rb[1])
new_s = AF.translate(AF.scale(ref, xfact=sx, yfact=sy, origin=(rb[0], rb[1])), sb[0]-rb[0], sb[1]-rb[1])
print("new s bbox:", ["%.2f" % v for v in new_s.bounds], "scale %.4f/%.4f" % (sx, sy))

def emit(poly, simp):
    p = poly.buffer(0).simplify(simp)
    rings = []
    for q in ([p] if p.geom_type == "Polygon" else list(p.geoms)):
        e = list(q.exterior.coords)[:-1]
        if not q.exterior.is_ccw: e = e[::-1]
        rings.append(e)
        for it in q.interiors:
            h = list(it.coords)[:-1]
            if it.is_ccw: h = h[::-1]
            rings.append(h)
    return fmt(rings), sum(len(r) for r in rings)

d_s, n_s = emit(new_s, 0.04)
print("new s d: %d chars / %d pts" % (len(d_s), n_s))
open(os.path.join(HERE, "ref", "s_new.txt"), "w", encoding="utf-8").write(d_s)
open(os.path.join(HERE, "ref", "s_old.txt"), "w", encoding="utf-8").write(lp[2])

# ---- 3) hollow-nose b ----
D_B_OLD = lp[0]
exterior = Polygon(parse(D_B_OLD)[0])
C = (73.76, 143.31)

def stadium(w, h, r, cx, cy):
    # buffering a rect of half-height (h/2 - r) yields an OUTER size of w x h
    g = Polygon([(cx - w/2 + r, cy - (h/2 - r)), (cx + w/2 - r, cy - (h/2 - r)),
                 (cx + w/2 - r, cy + (h/2 - r)), (cx - w/2 + r, cy + (h/2 - r))])
    return g.buffer(r, quad_segs=64, join_style="round", cap_style="round")

def new_b(w=62.0, h=44.0, r=22.0, n_rx=5.5, n_ry=8.0, n_dx=13.0, dy=0.0, light=0.0):
    cx, cy = C[0], C[1] + dy
    void = stadium(w, h, r, cx, cy)
    if light > 0:
        void = void.buffer(light, join_style="round").buffer(-0.0)
    g = exterior.difference(void)
    for s in (-1, 1):
        g = g.union(ell(cx + s * n_dx, cy, n_rx, n_ry))
    return g

OPTIONS = {
    "A": dict(),
    "B": dict(w=58.0, h=42.0, r=21.0, n_rx=5.0, n_ry=7.4, n_dx=12.0),
    "C": dict(w=66.0, h=40.0, r=20.0, n_rx=6.0, n_ry=8.6, n_dx=13.5),
}
bs = {}
for k, kw in OPTIONS.items():
    g = new_b(**kw)
    d_b, n_b = emit(g, 0.02)
    bs[k] = d_b
    open(os.path.join(HERE, "ref", "b_%s.txt" % k), "w", encoding="utf-8").write(d_b)
    print("b %s: rings=%d chars=%d" % (k, len(parse(d_b)), len(d_b)))
open(os.path.join(HERE, "ref", "b_old.txt"), "w", encoding="utf-8").write(D_B_OLD)

# ---- 4) renders ----
for k, d in (("old", D_B_OLD),) + tuple(bs.items()):
    shoot('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 147.52 217.07"><path d="%s" fill="#bfdbfe"/></svg>' % d,
          300, 441, os.path.join(OUT, "_nb_%s.png" % k))
for k, d in (("old", lp[2]), ("new", d_s)):
    shoot('<svg xmlns="http://www.w3.org/2000/svg" viewBox="322 69.55 124 147.52"><path d="%s" fill="#bfdbfe"/></svg>' % d,
          300, 357, os.path.join(OUT, "_s_%s.png" % k))

sheet = Image.new("RGBA", (1740, 560), (16, 20, 27, 255))
dr = ImageDraw.Draw(sheet)
for i, name in enumerate(["old", "A", "B", "C"]):
    sheet.alpha_composite(Image.open(os.path.join(OUT, "_nb_%s.png" % name)), (30 + i * 420, 80))
    dr.text((30 + i * 420, 60), "b %s" % name, fill=(150, 168, 195))
sheet.convert("RGB").save(os.path.join(OUT, "_nb_cmp.png"))

sheet2 = Image.new("RGBA", (760, 470), (16, 20, 27, 255))
dr2 = ImageDraw.Draw(sheet2)
sheet2.alpha_composite(Image.open(os.path.join(OUT, "_s_old.png")), (20, 60))
sheet2.alpha_composite(Image.open(os.path.join(OUT, "_s_new.png")), (400, 60))
dr2.text((20, 30), "OLD s", fill=(150, 168, 195))
dr2.text((400, 30), "NEW s (reference outline)", fill=(150, 168, 195))
sheet2.convert("RGB").save(os.path.join(OUT, "_s_cmp.png"))

# wordmark patched: b=A, s=new
src = open(SVG, encoding="utf-8").read()
patched = src.replace(D_B_OLD, bs["A"]).replace(lp[2], d_s)
open(os.path.join(OUT, "_wm_patched.svg"), "w", encoding="utf-8").write(patched)
vb = re.search(r'viewBox="([^"]+)"', patched).group(1).split()
Wm = 1600; Hm = round(Wm * float(vb[3]) / float(vb[2]))
shoot(patched, Wm, Hm, os.path.join(OUT, "_wm_patched.png"), bg="#0a0d14")
print("wordmark", Wm, Hm)
