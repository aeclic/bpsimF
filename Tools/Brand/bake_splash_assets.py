
"""Bake the native splash textures from the SVG masters."""
import os, re, sys, math
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import ROOT, OUT, shoot, parse
from shapely.geometry import Polygon

SVG = os.path.join(ROOT, "Brand", "svg", "bpsim-wordmark-static.svg")
DST = os.path.join(ROOT, "Assets", "Resources", "BrandSplash")
os.makedirs(DST, exist_ok=True)

src = open(SVG, encoding="utf-8").read()
paths = re.findall(r'<path[^>]*d="([^"]+)"', src)
vb = re.search(r'viewBox="([^"]+)"', src).group(1)
print("paths:", len(paths), "viewBox:", vb)

W = 1858  # 2x of 929
vw, vh = 929.25, 296.0
H = round(W * vh / vw)

# split the i path into stem + dot (dot = the small ring near the top)
i_rings = parse(paths[3])
print("i rings:", [len(r) for r in i_rings])
rings = [Polygon(r).buffer(0) for r in i_rings]
rings.sort(key=lambda p: p.area)
dot = rings[0]          # smallest
stem = rings[-1]
def ring_d(p):
    return "M" + " L".join("%.2f %.2f" % xy for xy in list(p.exterior.coords)[:-1]) + " Z"

AMBER = ('<linearGradient id="am" x1="0" y1="0" x2="1" y2="0">'
         '<stop offset="0" stop-color="#fcd34d"/><stop offset=".55" stop-color="#f59e0b"/>'
         '<stop offset="1" stop-color="#f97316"/></linearGradient>')

letters = " ".join('<path d="%s" fill="#ffffff"/>' % p for p in paths[:3] + [ring_d(stem)] + [paths[4]])
amber = ('<defs>%s</defs><path d="%s" fill="url(#am)"/><path d="%s" fill="url(#am)"/>'
         % (AMBER, paths[5], ring_d(dot)))

def svg(inner, bg=None):
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="%s" width="%d" height="%d">%s</svg>'
            % (vb, W, H, inner))

shoot(svg(letters), W, H, os.path.join(DST, "letters.png"), bg="transparent", scale=1.0)
shoot(svg(amber), W, H, os.path.join(DST, "amber.png"), bg="transparent", scale=1.0)
# separate pieces: amber F, amber i-dot, and a white F for the flash overlay
f_only = '<defs>%s</defs><path d="%s" fill="url(#am)"/>' % (AMBER, paths[5])
dot_only = '<defs>%s</defs><path d="%s" fill="url(#am)"/>' % (AMBER, ring_d(dot))
f_white = '<path d="%s" fill="#ffffff"/>' % paths[5]
dot_white = '<path d="%s" fill="#ffffff"/>' % ring_d(dot)
shoot(svg(f_only), W, H, os.path.join(DST, "f.png"), bg="transparent", scale=1.0)
shoot(svg(dot_only), W, H, os.path.join(DST, "dot.png"), bg="transparent", scale=1.0)
shoot(svg(f_white), W, H, os.path.join(DST, "fwhite.png"), bg="transparent", scale=1.0)
shoot(svg(dot_white), W, H, os.path.join(DST, "dotwhite.png"), bg="transparent", scale=1.0)

# sea gradient (vertical)
sea = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 512" width="16" height="512">'
       '<defs><linearGradient id="s" x1="0" y1="0" x2="0" y2="1">'
       '<stop offset="0" stop-color="#7dd3fc"/><stop offset=".42" stop-color="#3b82f6"/>'
       '<stop offset="1" stop-color="#6366f1"/></linearGradient></defs>'
       '<rect width="16" height="512" fill="url(#s)"/></svg>')
shoot(sea, 16, 512, os.path.join(DST, "sea.png"), bg="transparent", scale=1.0)

# tileable wave band (period 512 in x)
def wave(y0, amp, phase, thick, opacity):
    pts = []
    n = 128
    for k in range(n + 1):
        x = 512.0 * k / n
        y = y0 + amp * math.sin(2 * math.pi * (x / 512.0) + phase)
        pts.append("%.2f %.2f" % (x, y))
    return ('<path d="M%s" fill="none" stroke="#eaf6ff" stroke-width="%s" stroke-opacity="%s" '
            'stroke-linecap="round"/>' % (" L".join(pts), thick, opacity))
wave_svg = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 256" width="512" height="256">'
            + wave(70, 14, 0.0, 7, .55) + wave(150, 20, 1.9, 11, .40) + wave(214, 12, 3.4, 6, .30)
            + '</svg>')
shoot(wave_svg, 512, 256, os.path.join(DST, "wave.png"), bg="transparent", scale=1.0)

# bubble + bloom
bubble = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64" width="64" height="64">'
          '<defs><radialGradient id="b"><stop offset="0" stop-color="#ffffff" stop-opacity=".95"/>'
          '<stop offset=".55" stop-color="#e0f2fe" stop-opacity=".55"/>'
          '<stop offset="1" stop-color="#e0f2fe" stop-opacity="0"/></radialGradient></defs>'
          '<circle cx="32" cy="32" r="30" fill="url(#b)"/></svg>')
shoot(bubble, 64, 64, os.path.join(DST, "bubble.png"), bg="transparent", scale=1.0)

bloom = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">'
         '<defs><radialGradient id="g"><stop offset="0" stop-color="#fffbe8" stop-opacity=".98"/>'
         '<stop offset=".4" stop-color="#fcd34d" stop-opacity=".6"/>'
         '<stop offset="1" stop-color="#f59e0b" stop-opacity="0"/></radialGradient></defs>'
         '<circle cx="128" cy="128" r="126" fill="url(#g)"/></svg>')
shoot(bloom, 256, 256, os.path.join(DST, "bloom.png"), bg="transparent", scale=1.0)

# Made with Unity mark (cube + caption) as one sprite
mark = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 360 200" width="720" height="400">'
        '<g fill="none" stroke="#f2f4f7" stroke-width="6.2" stroke-linejoin="round" stroke-linecap="round">'
        '<path d="M180 18 L300 80 L180 142 L60 80 Z"/><path d="M60 80 L60 160 L180 222" opacity="0"/>'
        '<path d="M60 80 L60 158 L180 220 L180 142 Z" transform="translate(0,-4)"/>'
        '<path d="M300 80 L300 158 L180 220 L180 142 Z" transform="translate(0,-4)"/></g>'
        '<text x="180" y="196" text-anchor="middle" font-family="Cascadia Mono, Consolas, monospace" '
        'font-size="30" letter-spacing="7" fill="#ccd1d9">MADE WITH UNITY</text></svg>')
shoot(mark, 720, 400, os.path.join(DST, "mwu.png"), bg="transparent", scale=1.0)

for f in sorted(os.listdir(DST)):
    if f.endswith(".png"):
        print("%-14s %7d bytes" % (f, os.path.getsize(os.path.join(DST, f))))
