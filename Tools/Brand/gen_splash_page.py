
"""Compose the in-engine splash page (StreamingAssets/WebPanels/brand-splash.html).

The page puts a Unity "Made with Unity" mark on the left and the animated
bpsimF wordmark on its right, matching the engine splash background colour so
the hand-off is seamless.
"""
import os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import ROOT

SVG = os.path.join(ROOT, "Brand", "svg", "bpsim-wordmark-animated.svg")
OUTP = os.path.join(ROOT, "Assets", "StreamingAssets", "WebPanels", "brand-splash.html")

svg = open(SVG, encoding="utf-8").read()
# strip xml declaration / comments, keep as inline <svg>
svg = re.sub(r"<\?xml[^>]*\?>", "", svg).strip()

CUBE = """<svg class="cube" viewBox="0 0 100 110" fill="none" stroke="#f2f4f7" stroke-width="4.4"
     stroke-linejoin="round" stroke-linecap="round">
  <path d="M50 5 L92 27 L50 49 L8 27 Z"/>
  <path d="M8 27 L8 83 L50 105 L50 49 Z"/>
  <path d="M92 27 L92 83 L50 105 L50 49 Z"/>
</svg>"""

HTML = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>bpsimF</title>
<style>
  html, body { margin: 0; height: 100%%; background: #231f20; overflow: hidden; }
  body {
    display: flex; align-items: center; justify-content: center;
    gap: clamp(26px, 5.2vw, 132px);
    font-family: "Cascadia Mono", Consolas, "DejaVu Sans Mono", monospace;
    transition: opacity .42s ease-out;
    opacity: 1;
  }
  body.out { opacity: 0; }
  .unity { display: flex; flex-direction: column; align-items: center; gap: clamp(12px, 1.7vh, 24px);
           opacity: 0; animation: unityin .7s ease-out .12s both; }
  .cube { width: clamp(64px, 7vw, 124px); height: auto; filter: drop-shadow(0 0 18px rgba(242,244,247,.20)); }
  .mwu {
    font-size: clamp(11px, 1.05vw, 18px); letter-spacing: .38em; color: #ccd1d9;
    white-space: nowrap; padding-left: .38em;
  }
  .brand { width: min(45vw, 900px); margin-top: -1.4vh; opacity: 0; animation: brandin .8s ease-out .1s both; }
  .brand svg { display: block; width: 100%%; height: auto; }
  @keyframes unityin { from { opacity: 0; transform: translateY(6px); } to { opacity: 1; transform: none; } }
  @keyframes brandin { from { opacity: 0; } to { opacity: 1; } }
  @media (prefers-reduced-motion: reduce) { .unity, .brand { animation: none; opacity: 1; } }
</style>
</head>
<body>
  <div class="unity">
    __CUBE__
    <div class="mwu">MADE WITH UNITY</div>
  </div>
  <div class="brand">__WORDMARK__</div>
<script>
  (function () {
    function post(obj) {
      var text = JSON.stringify(obj);
      if (window.vuplex && window.vuplex.postMessage) window.vuplex.postMessage(text);
      else console.log('[splash] ' + text);
    }
    post({ t: 'ready' });
    // the wordmark intro (rise 2.0 s + flash ~1.1 s) is over at ~3.2 s
    setTimeout(function () { post({ t: 'playing' }); }, 2600);
    setTimeout(function () {
      document.body.classList.add('out');
      post({ t: 'done' });
    }, 3600);
  })();
</script>
</body>
</html>
""" % {}
HTML = HTML.replace("__CUBE__", CUBE).replace("__WORDMARK__", svg)

os.makedirs(os.path.dirname(OUTP), exist_ok=True)
open(OUTP, "w", encoding="utf-8").write(HTML)
print("wrote", OUTP, len(HTML), "bytes")
