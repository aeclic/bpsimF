
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import ROOT, shoot

DST = os.path.join(ROOT, "Assets", "Resources", "BrandSplash")
mark = ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 260 250" width="520" height="500">'
        '<g fill="none" stroke="#f2f4f7" stroke-width="5.6" stroke-linejoin="round" stroke-linecap="round">'
        '<path d="M130 10 L212 52 L130 94 L48 52 Z"/>'
        '<path d="M48 52 L48 140 L130 182 L130 94 Z"/>'
        '<path d="M212 52 L212 140 L130 182 L130 94 Z"/></g>'
        '<text x="130" y="232" text-anchor="middle" font-family="Cascadia Mono, Consolas, monospace" '
        'font-size="19" letter-spacing="4.6" fill="#ccd1d9">MADE WITH UNITY</text></svg>')
shoot(mark, 520, 500, os.path.join(DST, "mwu.png"), bg="transparent", scale=1.0)
print(os.path.getsize(os.path.join(DST, "mwu.png")))
