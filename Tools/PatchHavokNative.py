#!/usr/bin/env python3
"""Patch HavokNative.dll so the Havok solver actually steps.

Two independent gates in HavokNative.dll (Unity China 2022.2.23f1c1, 5 249 536 bytes,
build tag 0x358558c9-0x73280ca9:2024-06-30.Physics.UnityTechnologies_UnityIntegration):

1. RVA 0x16BE0  HP_CheckCompatibility  -- layout/edition check, returns bool.
2. RVA 0x177D5  inside UnityPluginLoad -- `call qword ptr [rax]` (IUnityInterfaces
   vtable[0]()) whose bool result is stored one instruction later into the global
   gate byte `g_flag` (RVA 0x4C6C00).  When g_flag == 0 *every* step export
   (HP_SyncWorldIn / HP_StepWorld / HP_ProcessStep / HP_SyncMotionsOut /
   HP_SyncSecondaryMotionsOut / HP_InjectContacts / HP_StepVisualDebugger) returns
   immediately -- velocities keep integrating on the managed side (that is
   Solver::ApplyGravityAndCopyInputVelocities inside StepImmediate) while positions
   never move.  That is the "Havok integrates velocity but never writes MotionData"
   symptom.

Patch 1: HP_CheckCompatibility entry -> `mov al,1; ret` (B0 01 C3).
Patch 2: the vtable call -> `mov al,1` (B0 01), same 2-byte length, so the following
         `mov byte ptr [rip+0x4af423], al` stores 1 into g_flag.

Both patches are idempotent: running this script twice is a no-op.

Usage:
    python Tools/PatchHavokNative.py                      # patches Assets/Plugins/x86_64/HavokNative.dll
    python Tools/PatchHavokNative.py <in.dll> [out.dll]
"""
import hashlib
import os
import shutil
import sys

# file offsets (RVA - 0x1000 + 0x400 for this .text section)
PATCHES = [
    # (file offset, original bytes, patched bytes, name)
    (0x15FE0, bytes.fromhex("405748 81EC".replace(" ", "")), bytes.fromhex("B001C3"), "HP_CheckCompatibility -> true"),
    (0x16BD5, bytes.fromhex("FF10"), bytes.fromhex("B001"), "UnityPluginLoad -> g_flag = 1"),
]

DEFAULT_DLL = os.path.join("Assets", "Plugins", "x86_64", "HavokNative.dll")
EXPECTED_SIZE = 5249536


def md5(path):
    h = hashlib.md5()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def main(argv):
    src = argv[1] if len(argv) > 1 else DEFAULT_DLL
    dst = argv[2] if len(argv) > 2 else src
    if not os.path.isfile(src):
        print("input not found: " + src)
        return 2

    size = os.path.getsize(src)
    if size != EXPECTED_SIZE:
        print("WARNING: unexpected size %d (expected %d) -- offsets may be wrong" % (size, EXPECTED_SIZE))

    with open(src, "rb") as f:
        data = bytearray(f.read())

    changed = 0
    for offset, original, patched, name in PATCHES:
        cur = bytes(data[offset:offset + len(patched)])
        if cur == patched:
            print("  [skip] %-34s already patched at 0x%05X" % (name, offset))
            continue
        if bytes(data[offset:offset + len(original)]) != original:
            print("  [FAIL] %-34s unexpected bytes at 0x%05X: %s" % (name, offset, bytes(data[offset:offset + 8]).hex(" ")))
            return 3
        data[offset:offset + len(patched)] = patched
        changed += 1
        print("  [ok]   %-34s 0x%05X  %s -> %s" % (name, offset, original.hex(" "), patched.hex(" ")))

    if changed == 0:
        print("already patched, nothing written (%s)" % md5(src))
        return 0

    # write to a temp file first: on Windows the dll may be locked by a running editor
    tmp = dst + ".patched"
    with open(tmp, "wb") as f:
        f.write(bytes(data))
    if os.path.abspath(tmp) != os.path.abspath(dst):
        if os.path.exists(dst):
            backup = dst + ".orig"
            if not os.path.exists(backup):
                shutil.copy2(dst, backup)
                print("backed up original -> " + backup)
        os.replace(tmp, dst)
    print("patched %d site(s); md5 -> %s" % (changed, md5(dst)))
    if os.path.abspath(tmp) == os.path.abspath(dst):
        print("in place: " + dst)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
