#!/usr/bin/env python3
"""Rewrite m_Script refs from the decompiled uGUI/TMP plugin DLLs to the UPM package scripts.

Mirrors the parent project's _codex_work/migrate_refs.py (see _codex_work/uGUI-PLUGIN-MIGRATION.md).
Mapping source: Logs/UnityEngine.UI.dll.map.txt / Logs/Unity.TextMeshPro.dll.map.txt produced by
Assets/Editor/BpsimUiPluginAudit.cs (DumpMap).

usage:  python Tools/bpsim_migrate_ui_refs.py --dry
        python Tools/bpsim_migrate_ui_refs.py --apply
"""
import os, re, shutil, sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PLUGINS = {
    "d3e719b59ab71ba3f6b398058c866280": "Logs/UnityEngine.UI.dll.map.txt",
    "67dfb1fdfb2b407222eda8e23ac8b724": "Logs/Unity.TextMeshPro.dll.map.txt",
}
BACKUP = os.path.join(ROOT, "Tools", "BPSim", "backup", "ui-refs")
SKIP_EXT = {".meta", ".cs", ".dll", ".png", ".jpg", ".jpeg", ".psd", ".tga", ".wav", ".mp3",
            ".ogg", ".ttf", ".otf", ".fbx", ".blend", ".asset_bundle", ".zip", ".exe"}
TEXT_ASSET_EXT = {".prefab", ".unity", ".anim", ".asset", ".controller", ".mat", ".overrideController",
                  ".playable", ".mask", ".physicMaterial", ".guiskin", ".fontsettings", ".preset"}

def load_map(path):
    m = {}
    with open(os.path.join(ROOT, path), encoding="utf-8") as f:
        for line in f:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            parts = line.split("|")
            if len(parts) < 4 or parts[2] == "<unmapped>":
                continue
            m[int(parts[0])] = (parts[1], parts[3])
    return m

def main():
    apply_ = "--apply" in sys.argv
    maps = {guid: load_map(p) for guid, p in PLUGINS.items()}
    for guid, m in maps.items():
        print("map", guid, "entries", len(m))

    # fileID: <id>, guid: <dllguid>   (serialized YAML, may be inline or block)
    pat = re.compile(r"fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]{32})")

    touched, unknown = [], []
    refs = [0]
    for dirpath, dirnames, filenames in os.walk(os.path.join(ROOT, "Assets")):
        for fn in filenames:
            ext = os.path.splitext(fn)[1].lower()
            if ext in SKIP_EXT:
                continue
            full = os.path.join(dirpath, fn)
            try:
                if os.path.getsize(full) > 8 * 1024 * 1024:
                    continue
                with open(full, "rb") as f:
                    raw = f.read()
            except OSError:
                continue
            if b"guid: d3e719b59ab71ba3f6b398058c866280" not in raw and \
               b"guid: 67dfb1fdfb2b407222eda8e23ac8b724" not in raw:
                continue
            text = raw.decode("utf-8")
            local_unknown = []

            def repl(mo):
                fid, guid = int(mo.group(1)), mo.group(2)
                if guid not in maps:
                    return mo.group(0)   # not a plugin-DLL reference, leave alone
                if fid not in maps[guid]:
                    local_unknown.append((fid, guid))
                    return mo.group(0)
                refs[0] += 1
                return "fileID: 11500000, guid: %s" % maps[guid][fid][1]

            new = pat.sub(repl, text)
            if local_unknown:
                unknown.append((os.path.relpath(full, ROOT), local_unknown))
            if new != text:
                touched.append(os.path.relpath(full, ROOT))
                if apply_:
                    rel = os.path.relpath(full, ROOT)
                    dst = os.path.join(BACKUP, rel)
                    os.makedirs(os.path.dirname(dst), exist_ok=True)
                    if not os.path.exists(dst):
                        shutil.copy2(full, dst)
                    with open(full, "w", encoding="utf-8", newline="") as f:
                        f.write(new)

    print("refs rewritten:", refs[0])
    print("files changed:", len(touched))
    for t in touched[:200]:
        print("   ", t)
    if unknown:
        print("UNKNOWN refs (need manual mapping):")
        for f, lst in unknown:
            print("   ", f, lst)
    print("APPLIED" if apply_ else "DRY RUN")

if __name__ == "__main__":
    main()
