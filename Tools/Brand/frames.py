
"""Deterministic frames of the animated wordmark.

Headless Chrome cannot advance CSS animations, but it CAN be told to seek:
the page pauses every animation and sets animation.currentTime, which is exact
and covers per-element inline delays (bubbles) that a CSS-only seek would miss.
"""
import os, re, subprocess, sys, tempfile
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import ROOT, OUT, CHROME
from PIL import Image

ANIM = os.path.join(ROOT, "Brand", "svg", "bpsim-wordmark-animated.svg")


def viewbox(src):
    m = re.search(r'viewBox="([-\d.\s]+)"', src)
    return [float(v) for v in m.group(1).split()]


def frame(t, W=1600, bg="#0a0d14", scale=2.0, out=None, extra_css=""):
    src = open(ANIM, encoding="utf-8").read()
    _, _, vw, vh = viewbox(src)
    H = round(W * vh / vw)
    css_w, css_h = W / scale, H / scale
    tmp = tempfile.mkdtemp()
    html = os.path.join(tmp, "f.html")
    open(html, "w", encoding="utf-8").write(
        '<!doctype html><style>html,body{margin:0;background:%s}svg{display:block;width:%spx;height:%spx}%s</style>%s'
        '<script>document.getAnimations().forEach(function(a){a.pause();a.currentTime=%f;});</script>'
        % (bg, css_w, css_h, extra_css, src, t * 1000.0))
    raw = os.path.join(tmp, "a.png")
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    "--virtual-time-budget=900", "--run-all-compositor-stages-before-draw",
                    "--force-device-scale-factor=%s" % scale,
                    "--window-size=%d,%d" % (round(css_w), round(css_h)),
                    "--screenshot=" + raw, "file:///" + html.replace("\\", "/")],
                   capture_output=True)
    im = Image.open(raw).convert("RGBA")
    if im.size != (W, H):
        im = im.resize((W, H), Image.LANCZOS)
    if out:
        im.save(out)
    return im


def strip(times, W=760, cols=4, out=None, labels=True):
    from PIL import ImageDraw
    shots = [frame(t, W=W).convert("RGB") for t in times]
    h = shots[0].height
    rows = (len(shots) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * (W + 16) + 16, rows * (h + 40) + 16), (24, 28, 36))
    d = ImageDraw.Draw(sheet)
    for i, (t, im) in enumerate(zip(times, shots)):
        x = 16 + (i % cols) * (W + 16)
        y = 16 + (i // cols) * (h + 40)
        sheet.paste(im, (x, y))
        if labels:
            d.text((x, y + h + 8), "t=%.2fs" % t, fill=(150, 168, 195))
    if out:
        sheet.save(out)
    return sheet


def extras(width=520, fps=14, t0=1.35, t1=6.35, gif_name="bpsim-wordmark-loop.gif"):
    """Two stills + the looping preview GIF, written into Brand/png."""
    import glob
    png = os.path.join(ROOT, "Brand", "png")
    rise = frame(0.62, W=1600); rise.save(os.path.join(png, "bpsim-still-rise-1600.png"))
    settle = frame(4.60, W=1600); settle.save(os.path.join(png, "bpsim-still-settled-1600.png"))
    n = int(round((t1 - t0) * fps))
    times = [t0 + i / float(fps) for i in range(n)]
    shots = [frame(t, W=width).convert("RGB") for t in times]
    base = shots[len(shots) // 2].quantize(colors=128, method=Image.MEDIANCUT)
    frames = [im.quantize(palette=base, dither=Image.Dither.NONE) for im in shots]
    out = os.path.join(png, gif_name)
    frames[0].save(out, save_all=True, append_images=frames[1:], duration=int(1000 / fps),
                   loop=0, optimize=True, disposal=2)
    return rise, settle, out, n


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "strip"
    if cmd == "strip":
        ts = [0.35, 0.8, 1.3, 1.5, 1.6, 1.72, 1.9, 2.4, 3.2, 4.6, 6.0, 7.4]
        p = os.path.join(OUT, "_flash_strip.png")
        strip(ts, W=620, cols=3, out=p)
        print(p)
    elif cmd == "extras":
        _, _, out, n = extras()
        print("%s  (%d frames)" % (out, n))
    elif cmd == "one":
        p = frame(float(sys.argv[2]), W=1600, out=os.path.join(OUT, "_frame_%s.png" % sys.argv[2]))
        print(os.path.join(OUT, "_frame_%s.png" % sys.argv[2]))
