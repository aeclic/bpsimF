
import re, os
HERE = os.path.dirname(os.path.abspath(__file__))
src = open(os.path.join(HERE, "ref", "logo-ghost-wave-effect.svg"), encoding="utf-8").read()
m = re.search(r'<clipPath id="cut">(.*?)</clipPath>', src, re.S)
body = m.group(1)
kids = re.findall(r'<(path|rect)\b[^>]*>', body)
print("children tags:", kids)
paths = re.findall(r'<path[^>]*\bd="([^"]+)"', body)
print("plain paths:", len(paths))
# also paths with transform or other ordering
elems = re.findall(r'<(?:path|rect)\b[^>]*/?>', body)
for i, e in enumerate(elems):
    d = re.search(r'\bd="([^"]+)"', e)
    print(i, "rect" if e.startswith("<rect") else "path", "len=%d" % (len(d.group(1)) if d else 0))
    if d and i == 3:
        open(os.path.join(HERE, "ref", "s_path.txt"), "w", encoding="utf-8").write(d.group(1))
        print("  s head:", d.group(1)[:220])
        print("  s cmds:", sorted(set(re.findall(r"[A-Za-z]", d.group(1)))))
