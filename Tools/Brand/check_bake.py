
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from repig_core import ROOT, OUT
from PIL import Image
D = os.path.join(ROOT, "Assets", "Resources", "BrandSplash")
L = Image.open(os.path.join(D, "letters.png")).convert("RGBA")
A = Image.open(os.path.join(D, "amber.png")).convert("RGBA")
print("letters", L.size, "alpha bbox", L.getchannel("A").getbbox())
print("amber  ", A.size, "alpha bbox", A.getchannel("A").getbbox())
base = Image.new("RGBA", L.size, (35, 31, 32, 255))
base.alpha_composite(L)
base.alpha_composite(A)
w = 1400
prev = base.resize((w, round(w * L.size[1] / L.size[0])), Image.LANCZOS)
prev.convert("RGB").save(os.path.join(OUT, "_bake_check.png"))
print("preview saved", prev.size)
