
import os, re, sys, subprocess, tempfile
from shapely.geometry import Polygon, Point
from shapely.ops import unary_union
from shapely import affinity
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SVG = os.path.join(ROOT, "Brand", "svg", "bpsim-wordmark-static.svg")
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
OUT = os.path.join(ROOT, "Tools", "_final")
os.makedirs(OUT, exist_ok=True)

TOK = re.compile(r"[A-Za-z]|-?\d+(?:\.\d+)?")

def parse(d):
    toks = TOK.findall(d)
    i = 0; rings = []; cur = None; cmd = None
    while i < len(toks):
        t = toks[i]
        if re.fullmatch(r"[A-Za-z]", t):
            cmd = t; i += 1
            if cmd in "Zz" and cur: rings.append(cur); cur = None
            continue
        if cur is None and cmd not in "Mm": i += 1; continue
        if cmd in "Mm":
            if cur: rings.append(cur)
            cur = [(float(toks[i]), float(toks[i+1]))]; i += 2
            cmd = "L" if cmd == "M" else "l"
        elif cmd in "Ll":
            cur.append((float(toks[i]), float(toks[i+1]))); i += 2
        elif cmd in "Cc": i += 6
        else: i += 2
    if cur: rings.append(cur)
    return rings

def fmt(rings, nd=2):
    f = "%% .%df" % nd
    out = []
    for r in rings:
        pts = r
        out.append("M" + " L".join(("%.2f %.2f" % p) for p in pts) + " Z")
    return " ".join(out)

def ell(cx, cy, rx, ry, deg=0.0, n=64):
    g = affinity.scale(Point(0, 0).buffer(1.0, quad_segs=n), rx, ry, origin=(0, 0))
    if deg: g = affinity.rotate(g, deg, origin=(0, 0))
    return affinity.translate(g, cx, cy)

src = open(SVG, encoding="utf-8").read()
D_OLD = re.findall(r'<path d="([^"]+)"', src)[0]
rings = parse(D_OLD)
polys = []
for r in rings:
    p = Polygon(r)
    if not p.is_valid: p = p.buffer(0)
    polys.append(p)
exterior, pighole, snout, nostrilA, nostrilB, eyeA, eyeB = polys
C = (73.76, 143.31); K = 0.94016
SMOOTH = 2.4 * K

def pig(ear_x, ear_y, ear_rx, ear_ry, ear_deg):
    head = Point(C).buffer(40.0 * K, quad_segs=64)
    parts = [head]
    for s in (-1, 1):
        parts.append(ell(C[0] + s * ear_x * 40 * K, C[1] + ear_y * 40 * K,
                         ear_rx * K, ear_ry * K, s * ear_deg))
    g = unary_union(parts).buffer(SMOOTH).buffer(-SMOOTH)
    return g

VARIANTS = {
    "old": None,
    "A": (0.66, -0.80, 11.5, 15.5, 30.0),
    "B": (0.70, -0.74, 12.0, 14.5, 38.0),
    "C": (0.66, -0.84, 11.0, 16.0, 24.0),
}

def b_path(piggeom):
    if piggeom is None:
        return D_OLD
    g = exterior.difference(piggeom).union(snout).union(eyeA).union(eyeB)
    g = g.difference(nostrilA).difference(nostrilB)
    # rebuild as ring list to control orientation: exterior CCW, holes CW
    rs = []
    polys2 = [g] if g.geom_type == "Polygon" else list(g.geoms)
    for p in polys2:
        e = list(p.exterior.coords)[:-1]
        if not p.exterior.is_ccw: e = e[::-1]
        rs.append(e)
        for it in p.interiors:
            h = list(it.coords)[:-1]
            if it.is_ccw: h = h[::-1]
            rs.append(h)
    return fmt(rs)

def shoot(svg, W, H, out, bg="#0a0d14", scale=2.0):
    css_w, css_h = W / scale, H / scale
    tmp = tempfile.mkdtemp()
    html = os.path.join(tmp, "p.html")
    open(html, "w", encoding="utf-8").write(
        "<html><head><style>html,body{margin:0;background:%s}svg{display:block;width:%dpx;height:%dpx}</style></head><body>%s</body></html>" % (bg, css_w, css_h, svg))
    raw = os.path.join(tmp, "shot.png")
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    "--force-device-scale-factor=%s" % scale,
                    "--window-size=%d,%d" % (round(css_w), round(css_h)),
                    "--screenshot=" + raw, "file:///" + html.replace("\\", "/")],
                   capture_output=True)
    im = Image.open(raw).convert("RGBA")
    if im.size != (W, H):
        im = im.resize((W, H), Image.LANCZOS)
    im.save(out)
    return im

# assemble a comparison sheet: rows old/A/B/C, three sizes each
cell_w, cell_h = 460, 300
sheet = Image.new("RGBA", (cell_w * 3 + 40, cell_h * len(VARIANTS) + 40), (10, 13, 20, 255))
from PIL import ImageDraw
dr = ImageDraw.Draw(sheet)
for row, (name, v) in enumerate(VARIANTS.items()):
    pg = pig(*v) if v else None
    d = b_path(pg)
    vb = "0 0 147.52 217.07"
    for col, px in enumerate((300, 190, 110)):
        svg = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="%s">'
               '<path d="%s" fill="#bfdbfe"/></svg>') % (vb, d)
        fn = os.path.join(OUT, "_pig_%s_%d.png" % (name, px))
        shoot(svg, px, round(px * 217.07 / 147.52), fn, bg="#0a0d14", scale=2.0)
        im = Image.open(fn)
        sheet.alpha_composite(im, (20 + col * cell_w, 20 + row * cell_h))
    dr.text((24, 20 + row * cell_h + cell_h - 22), "%s  %s" % (name, v if v else "original (jaw + upright ears)"), fill=(150, 168, 195))
    open(os.path.join(OUT, "_pig_%s.path" % name), "w", encoding="utf-8").write(d)

p = os.path.join(OUT, "_pig_cands.png"); sheet.convert("RGB").save(p); print(p)
print("path lengths:", {k: len(open(os.path.join(OUT, "_pig_%s.path" % k), encoding="utf-8").read()) for k in VARIANTS})
