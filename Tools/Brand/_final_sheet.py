
import os
from PIL import Image, ImageDraw
BR = os.path.abspath(os.path.join("..", "..", "Brand", "png"))
OUT = os.path.abspath(os.path.join("..", "_final"))
os.makedirs(OUT, exist_ok=True)
INK = (10, 13, 20, 255)
LIGHT = (244, 246, 251, 255)

def load(n): return Image.open(os.path.join(BR, n)).convert("RGBA")

sheet = Image.new("RGBA", (1760, 2000), (26, 30, 38, 255))
d = ImageDraw.Draw(sheet)

def panel(img, x, y, w, bg=None, label=""):
    h = round(w * img.size[1] / img.size[0])
    frame = Image.new("RGBA", (w, h), bg if bg else (0, 0, 0, 0))
    frame.alpha_composite(img.resize((w, h), Image.LANCZOS))
    sheet.alpha_composite(frame, (x, y))
    if label:
        d.text((x, y - 18), label, fill=(150, 168, 195))
    return h

panel(load("bpsim-hero-2048x1152.png"), 30, 40, 800, label="hero plate / dark")
panel(load("bpsim-still-rise-1600.png"), 30, 560, 800, bg=INK, label="animation frame t=0.55s - rising sea")
panel(load("bpsim-wordmark-static-1600.png"), 30, 1020, 800, bg=INK, label="wordmark / colour on dark")
panel(load("bpsim-wordmark-mono-ink-1600.png"), 30, 1420, 800, bg=LIGHT, label="wordmark / mono ink on light")

panel(load("bpsim-wordmark-mono-white-1600.png"), 880, 40, 800, bg=INK, label="wordmark / mono white")
panel(load("bpsim-still-settled-1600.png"), 880, 560, 800, bg=INK, label="animation frame / settled")

ic = load("bpsim-icon-512.png")
strip = Image.new("RGBA", (800, 300), INK)
x = 20
for s in (256, 128, 64, 32, 16):
    im = ic.resize((s, s), Image.LANCZOS)
    strip.alpha_composite(im, (x, 30))
    d.text((x, 300 - 20), str(s), fill=(150, 168, 195))
    x += s + 34
sheet.alpha_composite(strip, (880, 1040))
d.text((880, 1022), "app icon / detail + flat", fill=(150, 168, 195))

p = os.path.join(OUT, "_final_sheet.png")
sheet.save(p)
print(p)
