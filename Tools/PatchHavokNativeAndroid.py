#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Patch the Android (armeabi-v7a) HavokNative.so so the Havok solver actually steps.

The Android build of Havok Physics for Unity ships the same two gates as the Windows
dll (see Tools/PatchHavokNative.py):

1. `HP_CheckCompatibility(void*)` -- layout / edition check, returns bool.
2. `s_Unlocked` (a 1-byte BSS global at 0x3d2040) -- written once inside
   `UnityPluginLoad` from the result of a plugin-compatibility vtable call, and read by
   every step entry point:

       HP_SyncWorldIn           0x69060  ldrb + cmp/beq.w 0x691aa
       HP_SyncMotionsOut        0x691ec  ldrb + cbz
       HP_SyncSecondaryMotionsOut 0x6922c ldrb + cbz
       HP_StepWorld             0x69274  ldrb + cmp/beq
       HP_ProcessStep           0x693ba  ldrb + cmp/beq 0x694ae
       HP_StepVisualDebugger    0x694ee  ldrb + cmp/beq 0x695be
       HP_InjectContacts        0x695fe  ldrb + cmp/beq.w 0x69848

   With s_Unlocked == 0 each HP_* export returns immediately, so the managed side keeps
   integrating velocities while positions never move (or the app dies because nothing
   is ever stepped).  This patch mirrors the Windows one: kill the compatibility query
   in UnityPluginLoad and store a literal 1.

Both patches are length preserving and idempotent.

Usage:
    python Tools/PatchHavokNativeAndroid.py <in.so> [out.so]
"""
import hashlib
import io
import os
import struct
import sys

# Patched by virtual address; the file offset is resolved from the section table so the
# script keeps working after `llvm-strip`.
PATCHES = [
    # (vaddr, original bytes, patched bytes, name)
    (0x698A0, bytes.fromhex("2de9f041"), bytes.fromhex("01207047"),
     "HP_CheckCompatibility -> movs r0,#1; bx lr"),
    # 0x68a10: cbz r0,0x68a1c   } skip the s_Unlocked store when the interface lookup
    #                              returned null -- nop it so the store always runs
    # 0x68a12: ldr r0,[r4]      } the vtable[0]() compatibility query whose bool result
    # 0x68a14: blx r0           } is stored into s_Unlocked at 0x68a1a.  Replace it with
    #                              a literal 1, exactly like the Windows patch turns
    #                              `call qword ptr [rax]` into `mov al,1`.
    (0x68A10, bytes.fromhex("20b1"), bytes.fromhex("00bf"),
     "UnityPluginLoad -> keep going even if interface is null"),
    (0x68A12, bytes.fromhex("2068"), bytes.fromhex("0120"),
     "UnityPluginLoad -> r0 = 1 (compat query result)"),
    (0x68A14, bytes.fromhex("8047"), bytes.fromhex("00bf"),
     "UnityPluginLoad -> nop compat vtable call"),
]

EXPECTED_SIZE = 7260884


def md5(path):
    h = hashlib.md5()
    with io.open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load_sections(data):
    e_shoff, = struct.unpack_from("<I", data, 0x20)
    e_shentsize, e_shnum = struct.unpack_from("<HH", data, 0x2E)
    secs = []
    for i in range(e_shnum):
        o = e_shoff + i * e_shentsize
        fields = struct.unpack_from("<IIIIIIIIII", data, o)
        secs.append(dict(zip(
            "name typ flags addr off size link info align entsize".split(), fields)))
    return secs


def voff(secs, addr):
    for s in secs:
        if s["typ"] != 8 and s["addr"] and s["addr"] <= addr < s["addr"] + s["size"]:
            return s["off"] + (addr - s["addr"])
    return None


def main(argv):
    src = argv[1] if len(argv) > 1 else None
    if not src or not os.path.isfile(src):
        print("usage: PatchHavokNativeAndroid.py <in.so> [out.so]")
        return 2
    dst = argv[2] if len(argv) > 2 else src

    with io.open(src, "rb") as f:
        data = bytearray(f.read())
    if len(data) != EXPECTED_SIZE:
        print("WARNING: unexpected size %d (expected %d) -- section lookup will still be"
              " used, but verify the addresses" % (len(data), EXPECTED_SIZE))
    secs = load_sections(data)

    changed = 0
    for addr, original, patched, name in PATCHES:
        off = voff(secs, addr)
        if off is None:
            print("  [FAIL] %-52s no section maps vaddr 0x%X" % (name, addr))
            return 3
        cur = bytes(data[off:off + len(patched)])
        if cur == patched:
            print("  [skip] %-52s already patched at 0x%05X" % (name, off))
            continue
        if bytes(data[off:off + len(original)]) != original:
            print("  [FAIL] %-52s unexpected bytes at 0x%05X: %s"
                  % (name, off, bytes(data[off:off + 8]).hex(" ")))
            return 3
        data[off:off + len(patched)] = patched
        changed += 1
        print("  [ok]   %-52s vaddr 0x%05X (file 0x%05X)  %s -> %s"
              % (name, addr, off, original.hex(" "), patched.hex(" ")))

    if changed == 0:
        print("already patched, nothing written (%s)" % md5(src))
        return 0

    tmp = dst + ".patched"
    with io.open(tmp, "wb") as f:
        f.write(bytes(data))
    os.replace(tmp, dst)
    print("patched %d site(s); md5 -> %s" % (changed, md5(dst)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
