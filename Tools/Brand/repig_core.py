
"""Shared helpers for the BPSim brand geometry work.

The SVG masters in Brand/svg are authoritative; this module only provides
parsing / emitting / rendering utilities plus the *historical* b and s paths
(kept in Tools/Brand/ref/) so geometry can be rebuilt from scratch.
"""
import os, re, subprocess, tempfile
from shapely.geometry import Polygon, Point
from shapely.ops import unary_union
from shapely import affinity
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SVG = os.path.join(ROOT, "Brand", "svg", "bpsim-wordmark-static.svg")
REF = os.path.join(HERE, "ref")
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
OUT = os.path.join(ROOT, "Tools", "_final")
os.makedirs(OUT, exist_ok=True)

TOKEN = re.compile(r"[A-Za-z]|-?\d+(?:\.\d+)?")


def parse(d):
    """Parse an 'M/L/Z' (absolute, implicit repeats allowed) path into rings."""
    toks = TOKEN.findall(d)
    i = 0; rings = []; cur = None; cmd = None
    while i < len(toks):
        t = toks[i]
        if re.fullmatch(r"[A-Za-z]", t):
            cmd = t; i += 1
            if cmd in "Zz" and cur:
                rings.append(cur); cur = None
            continue
        if cur is None and cmd not in "Mm":
            i += 1; continue
        if cmd in "Mm":
            if cur: rings.append(cur)
            cur = [(float(toks[i]), float(toks[i + 1]))]; i += 2
            cmd = "L" if cmd == "M" else "l"
        elif cmd in "Ll":
            cur.append((float(toks[i]), float(toks[i + 1]))); i += 2
        elif cmd in "Cc":
            i += 6
        else:
            i += 2
    if cur: rings.append(cur)
    return rings


def fmt(rings):
    return " ".join("M" + " L".join("%.2f %.2f" % p for p in r) + " Z" for r in rings)


def ell(cx, cy, rx, ry, deg=0.0, n=64):
    g = affinity.scale(Point(0, 0).buffer(1.0, quad_segs=n), rx, ry, origin=(0, 0))
    if deg:
        g = affinity.rotate(g, deg, origin=(0, 0))
    return affinity.translate(g, cx, cy)


def rings_of(geom):
    """Polygon -> ring list with SVG-nonzero-friendly orientation."""
    out = []
    for p in ([geom] if geom.geom_type == "Polygon" else list(geom.geoms)):
        e = list(p.exterior.coords)[:-1]
        if not p.exterior.is_ccw: e = e[::-1]
        out.append(e)
        for it in p.interiors:
            h = list(it.coords)[:-1]
            if it.is_ccw: h = h[::-1]
            out.append(h)
    return out


def path_of(geom):
    return fmt(rings_of(geom))


def shoot(svg, W, H, out, bg="#0a0d14", scale=2.0, settle_ms=None):
    """Rasterise a standalone SVG string through headless Chrome."""
    css_w, css_h = W / scale, H / scale
    tmp = tempfile.mkdtemp()
    html = os.path.join(tmp, "p.html")
    cssbg = "transparent" if bg in (None, "transparent") else bg
    open(html, "w", encoding="utf-8").write(
        "<html><head><style>html,body{margin:0;background:%s}svg{display:block;width:%spx;height:%spx}</style>"
        "</head><body>%s</body></html>" % (cssbg, css_w, css_h, svg))
    raw = os.path.join(tmp, "shot.png")
    args = [CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
            "--force-device-scale-factor=%s" % scale,
            "--window-size=%d,%d" % (round(css_w), round(css_h)),
            "--screenshot=" + raw, "file:///" + html.replace("\\", "/")]
    if cssbg == "transparent":
        args.insert(3, "--default-background-color=00000000")
    subprocess.run(args, capture_output=True)
    im = Image.open(raw).convert("RGBA")
    if im.size != (W, H):
        im = im.resize((W, H), Image.LANCZOS)
    im.save(out)
    return im
