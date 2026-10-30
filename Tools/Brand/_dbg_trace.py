
import numpy as np
from PIL import Image
from skimage import measure
a = np.array(Image.open(r"Tools/_final/_ref_s_trace.png").convert("RGBA"))
print("shape", a.shape)
al = a[:, :, 3]
print("alpha min/max", al.min(), al.max(), "ink px", (al > 127).sum())
cs = measure.find_contours(al.astype(float), 127.0)
print("contours:", len(cs), "lengths:", sorted((len(c) for c in cs), reverse=True)[:5])
for c in sorted(cs, key=len, reverse=True)[:3]:
    print(" len", len(c), "rows", c[:, 0].min(), c[:, 0].max(), "cols", c[:, 1].min(), c[:, 1].max())
