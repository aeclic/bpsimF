
import os, re, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import ROOT

P = os.path.join(ROOT, "Brand", "svg", "bpsim-wordmark-animated.svg")
s = open(P, encoding="utf-8").read()
orig = s

# 1) F group animation -> flash entrance + teammates
old_rule = ".F{fill:url(#amber);animation:popf .9s ease-out 1.7s both}"
new_rules = (".F{fill:url(#amber);transform-box:fill-box;transform-origin:center;"
             "animation:Fpop .5s cubic-bezier(.2,.9,.25,1) 1.5s both}\n"
             ".Fhot{fill:#ffffff;animation:Fhot 1.05s ease-out 1.5s both}\n"
             ".Fbloom{fill:url(#fbloom);opacity:0;animation:Fbloom 1.25s ease-out 1.44s both}\n"
             ".Fstreak rect{fill:url(#streak);opacity:0;animation:Fstreak .66s cubic-bezier(.3,0,.2,1) 1.56s 1 both}")
assert s.count(old_rule) == 1
s = s.replace(old_rule, new_rules)

# 2) keyframes
old_kf = "@keyframes popf{from{opacity:0;transform:translateY(10px)}to{opacity:1;transform:translateY(0)}}"
assert s.count(old_kf) == 1
new_kf = old_kf + "\n" + "\n".join([
    "@keyframes Fpop{0%{opacity:0;transform:scale(.9)}55%{opacity:1;transform:scale(1.05)}100%{opacity:1;transform:scale(1)}}",
    "@keyframes Fhot{0%{opacity:0}5%{opacity:1}18%{opacity:.95}42%{opacity:.35}100%{opacity:0}}",
    "@keyframes Fbloom{0%{opacity:0}4%{opacity:.95}22%{opacity:.5}100%{opacity:0}}",
    "@keyframes Fstreak{0%{transform:translateX(-300px);opacity:0}10%{opacity:1}100%{transform:translateX(330px);opacity:0}}",
])
s = s.replace(old_kf, new_kf)

# 3) gradients for bloom + streak
grads = ('<radialGradient id="fbloom" cx="50%" cy="50%" r="50%">'
         '<stop offset="0" stop-color="#fffbe8" stop-opacity=".95"/>'
         '<stop offset=".45" stop-color="#fcd34d" stop-opacity=".55"/>'
         '<stop offset="1" stop-color="#f59e0b" stop-opacity="0"/></radialGradient>'
         '<linearGradient id="streak" x1="0" y1="0" x2="1" y2="0">'
         '<stop offset="0" stop-color="#fff" stop-opacity="0"/>'
         '<stop offset=".5" stop-color="#fff" stop-opacity=".95"/>'
         '<stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient>')
assert s.count("</defs>") == 1
s = s.replace("</defs>", grads + "</defs>")

# 4) markup: bloom behind, hot overlay on top, streak inside the F clip
old_open = '<g class="F"><path d="M811.64 0.00'
assert s.count(old_open) == 1
bloom = ('<circle class="Fbloom" cx="843.64" cy="107.29" r="198"/>')
s = s.replace(old_open, '<g class="F">' + bloom + '<path class="Fface" d="M811.64 0.00')

fpath = re.search(r'<path class="Fface" d="([^"]+)"', s).group(1)
old_clip_g = '<g clip-path="url(#f)"><g class="shinef">'
assert s.count(old_clip_g) == 1
s = s.replace(old_clip_g, '<path class="Fhot" d="%s"/><g clip-path="url(#f)"><g class="shinef">' % fpath)
# streak: insert right after the shinef group closes
old_shinef_end = '<rect x="774" y="-54.0" width="120" height="420.0" transform="skewX(-20)"/></g>'
assert s.count(old_shinef_end) == 1
s = s.replace(old_shinef_end, old_shinef_end +
              '<g class="Fstreak"><rect x="774" y="-54.0" width="86" height="420.0" transform="skewX(-20)"/></g>')

open(P, "w", encoding="utf-8").write(s)
print("patched animated svg: %d -> %d bytes" % (len(orig), len(s)))
