
from PIL import Image, ImageDraw
import math
# canvas covers SVG x -8..1016 (1024 units) and y -8..288 (296 units) at 2 px/unit
PX_PER_UNIT = 2.0
X0, Y0 = -8.0, -8.0
W_UNITS, H_UNITS = 1024.0, 296.0
W, H = int(W_UNITS*PX_PER_UNIT), int(H_UNITS*PX_PER_UNIT)
im = Image.new("RGBA", (W, H), (0,0,0,0))
d = ImageDraw.Draw(im)

# (svg_y, wavelength, amplitude, rgb, stroke_width_units, stroke_opacity, phase_units)
ribbons = [
    (34.0, 150.0, 12.0, (224,242,254), 11.0, 0.34,   0.0),
    (96.0, 122.0, 10.0, (255,255,255),  7.0, 0.22,  40.0),
    (164.0,165.0, 11.0, (165,243,252), 13.0, 0.30,  70.0),
    (232.0,131.0,  8.0, (224,242,254),  8.0, 0.20,  20.0),
]
for (y, wl, amp, rgb, wid, op, ph) in ribbons:
    pts=[]
    x = -8.0
    while x <= X0 + W_UNITS + 8:
        yy = y - amp*math.sin(2*math.pi*(x - ph)/wl)
        pts.append(((x - X0)*PX_PER_UNIT, (yy - Y0)*PX_PER_UNIT))
        x += 1.0
    col = (rgb[0], rgb[1], rgb[2], int(round(op*255)))
    d.line(pts, fill=col, width=int(round(wid*PX_PER_UNIT)), joint="curve")
out = r"D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Assets\Resources\BrandSplash\currents.png"
im.save(out)
print("saved", out, im.size)
# report alpha profile
px=im.load()
for y in range(0,H,16):
    a=max(px[x,y][3] for x in range(0,W,8))
    if a: print("  row",y,"alpha",a)
