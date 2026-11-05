# 生成离线编译用的 rsp（绕开/前置校验 Unity 编译）
# 用法：python Tools/AnvilBench/genrsp.py
import os

proj = os.getcwd()
dag = os.path.join(proj, "Library/Bee/artifacts/1300b0aE.dag")
out_dir = os.path.join(proj, "Temp/AnvilCheck")
os.makedirs(out_dir, exist_ok=True)

def read(path):
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        return [l.rstrip("\r\n") for l in f]

DROP = ("-out:", "-refout:", "-doc:", "-additionalfile:", "-analyzer:",
        "-errorlog:", "-ruleset:", "-keyfile:", "-delaysign", "-deterministic", "-pathmap:")

def strip(lines):
    out = []
    for l in lines:
        s = l.strip()
        if not s:
            continue
        if s.startswith("/"):
            continue
        if s.startswith("-") and any(s.startswith(d) for d in DROP):
            continue
        out.append(s)
    return out

ANVIL_IN_PACKAGE = [
    "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilNative.cs",
    "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilWorld.cs",
    "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilStepSystem.cs",
    "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilContactEvents.cs",
    "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilContactEventSystem.cs",
]

def gen_unityphysics():
    body = strip(read(os.path.join(dag, "Unity.Physics.rsp")))
    have = set(x.strip('"').replace("\\", "/").lower() for x in body)
    for a in ANVIL_IN_PACKAGE:
        if a.lower() not in have:
            body.append('"%s"' % a)
    head = [
        "-target:library",
        '-out:"%s"' % os.path.join(out_dir, "Unity.Physics.dll").replace("\\", "/"),
        "-unsafe",
        "-nostdlib+",
    ]
    p = os.path.join(out_dir, "unityphysics.rsp")
    with open(p, "w", encoding="utf-8") as f:
        f.write("\n".join(head + body) + "\n")
    return p

def gen_anvil():
    body = strip(read(os.path.join(dag, "Assembly-CSharp.rsp")))
    up = os.path.join(out_dir, "Unity.Physics.dll").replace("\\", "/")
    fixed, replaced = [], 0
    for l in body:
        if l.startswith("-r:") and "Unity.Physics" in l:
            fixed.append('-r:"%s"' % up)
            replaced += 1
            continue
        fixed.append(l)

    defines = [l for l in fixed if l.startswith("-define:")]
    refs = [l for l in fixed if l.startswith("-r:")]
    raw_sources = [l for l in fixed if not l.startswith("-define:") and not l.startswith("-r:")]

    sources = []
    for s in raw_sources:
        path = s.strip('"').replace("\\", "/")
        if path.lower().endswith(".cs") and not os.path.exists(path):
            continue
        sources.append(s)

    extra = [
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilBenchQA.cs",
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilBenchBatch.cs",
    ]
    have = set(s.strip('"').replace("\\", "/").lower() for s in sources)
    for e in extra:
        if os.path.exists(e) and e.lower() not in have:
            sources.append('"%s"' % e)

    head = [
        "-target:library",
        '-out:"%s"' % os.path.join(out_dir, "Assembly-CSharp.dll").replace("\\", "/"),
        "-unsafe",
        "-nostdlib+",
        "-nowarn:0649",
        "-nowarn:0169",
    ]
    if not any("ANVIL_PHYSICS_EXISTS" in d for d in defines):
        defines.append("-define:ANVIL_PHYSICS_EXISTS")

    p = os.path.join(out_dir, "anvil.rsp")
    with open(p, "w", encoding="utf-8") as f:
        f.write("\n".join(head + defines + refs + sources) + "\n")
    print("unity.Physics ref replaced:", replaced)
    print("defines:", len(defines), "refs:", len(refs), "sources:", len(sources))
    return p

print(gen_unityphysics())
print(gen_anvil())
