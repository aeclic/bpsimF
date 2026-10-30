
from PIL import Image
import os
base=r"D:\rpgvx + magpie\BPRC 2026.3.2 proj\bpsim-main\Assets\Resources\BrandSplash"

# --- 1) sea.png: gradient over the SVG gradient span (-46..270), then pad to the sea body end (360)
old=Image.open(os.path.join(base,"sea.png")).convert("RGBA")
grad=old.resize((16,632), Image.LANCZOS)
body=Image.new("RGBA",(16,812),(0,0,0,255))
body.paste(grad,(0,0))
deep=grad.getpixel((8,631))
for y in range(632,812):
    for x in range(16):
        body.putpixel((x,y),deep)
body.save(os.path.join(base,"sea.png"))
print("sea.png ->",body.size,"gradient rows 0..631, pad colour",deep)

# --- 2) dot.png: the SVG amber gradient starts before the dot, so the dot is #fcd34d
d=Image.open(os.path.join(base,"dot.png")).convert("RGBA")
px=d.load(); w,h=d.size
n=0
for y in range(h):
    for x in range(w):
        r,g,b,a=px[x,y]
        if a>0:
            px[x,y]=(252,211,77,a); n+=1
d.save(os.path.join(base,"dot.png"))
print("dot.png recoloured px",n)
