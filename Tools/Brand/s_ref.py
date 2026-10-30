
import os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import *
from shapely.geometry import Polygon
from shapely import affinity as AF
from PIL import Image, ImageDraw

REF = open(os.path.join(HERE, "ref", "s_path.txt"), encoding="utf-8").read()
toks = re.findall(r"[A-Za-z]|-?\d+(?:\.\d+)?", REF)
pts = []; cmd = None; i = 0; pos = (0.0, 0.0)

def cubic(p0, p1, p2, p3, n=48):
    out = []
    for k in range(1, n + 1):
        t = k / n; mt = 1 - t
        out.append((mt**3*p0[0] + 3*mt*mt*t*p1[0] + 3*mt*t*t*p2[0] + t**3*p3[0],
                    mt**3*p0[1] + 3*mt*mt*t*p1[1] + 3*mt*t*t*p2[1] + t**3*p3[1]))
    return out

while i < len(toks):
    t = toks[i]
    if re.fullmatch(r"[A-Za-z]", t):
        cmd = t; i += 1
        if cmd in "Zz": pts.append(pts[0])
        continue
    if cmd == "M":
        pos = (float(toks[i]), float(toks[i+1])); pts.append(pos); i += 2; cmd = "l"
    elif cmd == "c":
        x0, y0 = pos
        p1 = (x0 + float(toks[i]), y0 + float(toks[i+1]))
        p2 = (x0 + float(toks[i+2]), y0 + float(toks[i+3]))
        p3 = (x0 + float(toks[i+4]), y0 + float(toks[i+5]))
        pts.extend(cubic(pos, p1, p2, p3)); pos = p3; i += 6
    elif cmd == "l":
        pos = (pos[0] + float(toks[i]), pos[1] + float(toks[i+1])); pts.append(pos); i += 2
    elif cmd == "h":
        pos = (pos[0] + float(toks[i]), pos[1]); pts.append(pos); i += 1
    else:
        i += 1

ref_poly = Polygon(pts).buffer(0)
rb = ref_poly.bounds
srctxt = open(SVG, encoding="utf-8").read()
lp = re.findall(r'<path d="([^"]+)"', srctxt)
old_s = Polygon(parse(lp[2])[0]).buffer(0); sb = old_s.bounds
sx = (sb[2]-sb[0])/(rb[2]-rb[0]); sy = (sb[3]-sb[1])/(rb[3]-rb[1])
scaled = AF.scale(ref_poly, xfact=sx, yfact=sy, origin=(rb[0], rb[1]))
new_s = AF.translate(scaled, sb[0]-rb[0], sb[1]-rb[1])
print("mapped bbox:", ["%.2f" % v for v in new_s.bounds], " target:", ["%.2f" % v for v in sb])

def emit(poly, simp=0.02):
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

d_new, npts = emit(new_s)
print("new s: %d chars / %d pts" % (len(d_new), npts))
open(os.path.join(HERE, "ref", "s_new.txt"), "w", encoding="utf-8").write(d_new)
open(os.path.join(HERE, "ref", "s_old.txt"), "w", encoding="utf-8").write(lp[2])

for name, d in (("old", lp[2]), ("new", d_new)):
    shoot('<svg xmlns="http://www.w3.org/2000/svg" viewBox="322 69.55 124 147.52"><path d="%s" fill="#bfdbfe"/></svg>' % d,
          340, 405, os.path.join(OUT, "_s_%s.png" % name))
sheet = Image.new("RGBA", (760, 470), (16, 20, 27, 255))
sheet.alpha_composite(Image.open(os.path.join(OUT, "_s_old.png")), (20, 40))
sheet.alpha_composite(Image.open(os.path.join(OUT, "_s_new.png")), (400, 40))
dr = ImageDraw.Draw(sheet)
dr.text((20, 16), "OLD s - spur at both terminals", fill=(150, 168, 195))
dr.text((400, 16), "REFERENCE s - clean blades", fill=(150, 168, 195))
p = os.path.join(OUT, "_s_cmp.png"); sheet.convert("RGB").save(p); print(p)
