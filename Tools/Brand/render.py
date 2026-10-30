# -*- coding: utf-8 -*-
"""Re-rasterise the committed BPSim brand SVGs into Brand/png.

The SVGs in Brand/svg are the masters (hand-authored outlines + CSS animation).
This script only re-renders them with headless Chrome so every raster in
Brand/png can be regenerated or added at a new size:

    cd Tools/Brand
    python render.py            # all
    python render.py icon-512   # one target

"""
import os, re, subprocess, sys, tempfile
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, '..', '..'))
SVG = os.path.join(ROOT, 'Brand', 'svg')
PNG = os.path.join(ROOT, 'Brand', 'png')
TMP = tempfile.mkdtemp(prefix='bpsim-render-')
CHROME = r'C:/Program Files/Google/Chrome/Application/chrome.exe'
INK = '#0a0d14'

# name -> (svg file, output file, width in px, supersample, background)
TARGETS = {
    'wordmark-static-1600': ('bpsim-wordmark-static.svg', 'bpsim-wordmark-static-1600.png', 1600, 2, 'transparent'),
    'wordmark-static-800':  ('bpsim-wordmark-static.svg', 'bpsim-wordmark-static-800.png', 800, 2, 'transparent'),
    'wordmark-static-400':  ('bpsim-wordmark-static.svg', 'bpsim-wordmark-static-400.png', 400, 3, 'transparent'),
    'wordmark-mono-white-1600': ('bpsim-wordmark-mono-white.svg', 'bpsim-wordmark-mono-white-1600.png', 1600, 2, 'transparent'),
    'wordmark-mono-white-800':  ('bpsim-wordmark-mono-white.svg', 'bpsim-wordmark-mono-white-800.png', 800, 2, 'transparent'),
    'wordmark-mono-ink-1600':   ('bpsim-wordmark-mono-ink.svg', 'bpsim-wordmark-mono-ink-1600.png', 1600, 2, 'transparent'),
    'wordmark-mono-ink-800':    ('bpsim-wordmark-mono-ink.svg', 'bpsim-wordmark-mono-ink-800.png', 800, 2, 'transparent'),
    'icon-512': ('bpsim-icon-static.svg', 'bpsim-icon-512.png', 512, 2, 'transparent'),
    'icon-256': ('bpsim-icon-static.svg', 'bpsim-icon-256.png', 256, 3, 'transparent'),
    'icon-128': ('bpsim-icon-static.svg', 'bpsim-icon-128.png', 128, 4, 'transparent'),
    'icon-64': ('bpsim-icon-flat.svg', 'bpsim-icon-64.png', 64, 8, 'transparent'),
    'icon-48': ('bpsim-icon-flat.svg', 'bpsim-icon-48.png', 48, 8, 'transparent'),
    'icon-32': ('bpsim-icon-flat.svg', 'bpsim-icon-32.png', 32, 12, 'transparent'),
    'icon-16': ('bpsim-icon-flat.svg', 'bpsim-icon-16.png', 16, 16, 'transparent'),
    'icon-mono': ('bpsim-icon-mono-white.svg', 'bpsim-icon-mono-white-512.png', 512, 2, 'transparent'),
    'watermark-2048': ('bpsim-watermark-2048x1152.svg', 'bpsim-watermark-2048x1152.png', 2048, 1, 'transparent'),
    'watermark-1024': ('bpsim-watermark-2048x1152.svg', 'bpsim-watermark-1024x576.png', 1024, 1, 'transparent'),
    'hero-2048': ('bpsim-hero-2048x1152.svg', 'bpsim-hero-2048x1152.png', 2048, 1, 'transparent'),
}


def viewbox(src):
    m = re.search(r'viewBox="([-\d.\s]+)"', src)
    return [float(v) for v in m.group(1).split()]


def render(svg_name, out_name, width, scale=2.0, bg='transparent'):
    src = open(os.path.join(SVG, svg_name), encoding='utf-8').read()
    _, _, vw, vh = viewbox(src)
    css_w = width / float(scale)
    css_h = css_w * vh / vw
    os.makedirs(TMP, exist_ok=True)
    page = os.path.join(TMP, 'page.html')
    open(page, 'w', encoding='utf-8').write(
        '<!doctype html><style>html,body{margin:0;background:%s}svg{display:block;width:%spx;height:%spx}</style>'
        % (bg, css_w, css_h) + src)
    raw = os.path.join(TMP, 'shot.png')
    subprocess.run([CHROME, '--headless=new', '--disable-gpu', '--hide-scrollbars',
                    '--default-background-color=00000000',
                    '--force-device-scale-factor=%s' % scale,
                    '--window-size=%d,%d' % (round(css_w), round(css_h)),
                    '--screenshot=' + raw, 'file:///' + page.replace(os.sep, '/')],
                   capture_output=True)
    im = Image.open(raw).convert('RGBA')
    want = (width, max(1, round(width * vh / vw)))
    if im.size != want:
        im = im.resize(want, Image.LANCZOS)
    os.makedirs(PNG, exist_ok=True)
    im.save(os.path.join(PNG, out_name))
    return out_name, im.size


def main(argv):
    names = argv[1:] or sorted(TARGETS)
    for n in names:
        if n not in TARGETS:
            print('unknown target: ' + n)
            continue
        out, size = render(*TARGETS[n])
        print('%-32s %s' % (out, size))


if __name__ == '__main__':
    main(sys.argv)