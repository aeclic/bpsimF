
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import *
from PIL import Image, ImageDraw

VAR = {
    "old": dict(ear_y=-0.92, ear_rx=11.0, ear_ry=17.0, ear_deg=18.0, jaw=(24.0, 13.0, 0.90)),
    "A":   dict(ear_x=0.66, ear_y=-0.80, ear_rx=11.5, ear_ry=15.5, ear_deg=30.0),
    "B":   dict(ear_x=0.70, ear_y=-0.74, ear_rx=12.0, ear_ry=14.5, ear_deg=38.0),
    "C":   dict(ear_x=0.66, ear_y=-0.84, ear_rx=11.0, ear_ry=16.0, ear_deg=24.0),
}
rows = []
for name, kw in VAR.items():
    pg = pig(**kw)
    big = os.path.join(OUT, "_v_%s_b.png" % name)
    shoot('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 147.52 217.07"><path d="%s" fill="#bfdbfe"/></svg>' % b_path(pg), 420, 618, big)
    # pig void alone, dark on light
    pv = os.path.join(OUT, "_v_%s_pig.png" % name)
    shoot('<svg xmlns="http://www.w3.org/2000/svg" viewBox="40 100 70 90"><rect x="40" y="100" width="70" height="90" fill="#f4f6fb"/><path d="%s" fill="#0a0d14" fill-rule="evenodd"/></svg>' % fmt([
        list(pg.buffer(0).exterior.coords)[:-1]]), 280, 300, pv, bg="#f4f6fb")
    small = os.path.join(OUT, "_v_%s_s.png" % name)
    shoot('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 147.52 217.07"><path d="%s" fill="#bfdbfe"/></svg>' % b_path(pg), 140, 206, small)
    rows.append((name, kw, big, pv, small))

W = 1120
H = 40 + len(rows) * 640
sheet = Image.new("RGBA", (W, H), (16, 20, 27, 255))
d = ImageDraw.Draw(sheet)
for i, (name, kw, big, pv, small) in enumerate(rows):
    y = 30 + i * 640
    sheet.alpha_composite(Image.open(pv), (20, y + 150))
    sheet.alpha_composite(Image.open(big), (320, y))
    sheet.alpha_composite(Image.open(small), (770, y + 120))
    d.text((320, y + 620), "%s  %s" % (name, kw), fill=(150, 168, 195))
    d.text((20, y + 130), "pig void", fill=(120, 138, 160))
    d.text((770, y + 100), "small", fill=(120, 138, 160))
p = os.path.join(OUT, "_pig_cands2.png"); sheet.convert("RGB").save(p); print(p)
