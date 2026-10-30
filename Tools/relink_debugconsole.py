# -*- coding: utf-8 -*-
"""Rewrite uGUI m_Script references in the ported IngameDebugConsole prefabs.

bpsim-main still ships uGUI and TextMeshPro as precompiled DLLs
(Assets/Plugins/UnityEngine.UI.dll / Unity.TextMeshPro.dll), so a prefab that
references the *source* script guid (com.unity.ugui package form,
"{fileID: 11500000, guid: <script guid>}") resolves to a Missing Script here.
A component inside a DLL serialises as {fileID: <name hash>, guid: <dll guid>}.

fileID = int32_le(MD4("s\0\0\0" + namespace + className)[0:4]), verified against
16 uGUI/TMP component fileIDs already present in this project.
"""
import hashlib
import io
import struct
import sys

UI_DLL_GUID = "d3e719b59ab71ba3f6b398058c866280"
TMP_DLL_GUID = "67dfb1fdfb2b407222eda8e23ac8b724"
# DebugLogItem.prefab / CommandSuggestion.prefab point at the TextMeshPro
# package's own LiberationSans SDF, which does not exist in this project.
MISSING_TMP_FONT_GUID = "8f586378b4e144a9851e7b34d9b748ee"
LOCAL_TMP_FONT_GUID = "a3db0803fb2e0c4479d6378e35af2bd7"

UGUI_SOURCE_GUIDS = {
    "fe87c0e1cc204ed48ad3b37840f39efc": ("UnityEngine.UI", "Image"),
    "4e29b1a8efbd4b44bb3f3716e73f07ff": ("UnityEngine.UI", "Button"),
    "1aa08ab6e0800fa44ae55d278d1423e3": ("UnityEngine.UI", "ScrollRect"),
    "2a4db7a114972834c8e4117be1d82ba3": ("UnityEngine.UI", "Scrollbar"),
    "0cd44c1031e13a943bb63640046fad76": ("UnityEngine.UI", "CanvasScaler"),
    "dc42784cf147c0c48a680349fa168899": ("UnityEngine.UI", "GraphicRaycaster"),
    "306cc8c2b49d7114eaa3623786fc2126": ("UnityEngine.UI", "LayoutElement"),
    "3312d7739989d2b4e91e6319e9a96d76": ("UnityEngine.UI", "RectMask2D"),
    "30649d3a9faa99c48a7b1166b86bf2a0": ("UnityEngine.UI", "HorizontalLayoutGroup"),
    "59f8146938fff824cb5fd77236b75775": ("UnityEngine.UI", "VerticalLayoutGroup"),
    "76c392e42b5098c458856cdf6ecaaaa1": ("UnityEngine.EventSystems", "EventSystem"),
    "4f231c4fb786f3946a6b90b886c48677": ("UnityEngine.EventSystems", "StandaloneInputModule"),
    "d0b148fe25e99eb48b9724523833bab1": ("UnityEngine.EventSystems", "EventTrigger"),
}

# Values cross-checked against prefabs that already exist in this project.
KNOWN = {
    "UnityEngine.UI.Image": -765806418,
    "UnityEngine.UI.Text": 708705254,
    "UnityEngine.UI.Button": 1392445389,
    "UnityEngine.UI.ScrollRect": 1367256648,
    "UnityEngine.UI.Scrollbar": -2061169968,
    "UnityEngine.UI.CanvasScaler": 1980459831,
    "UnityEngine.UI.GraphicRaycaster": 1301386320,
    "UnityEngine.UI.LayoutElement": 1679637790,
    "UnityEngine.UI.RectMask2D": -146154839,
    "UnityEngine.UI.HorizontalLayoutGroup": -405508275,
    "UnityEngine.UI.VerticalLayoutGroup": 1297475563,
    "UnityEngine.EventSystems.EventSystem": -619905303,
    "UnityEngine.EventSystems.StandaloneInputModule": 1077351063,
    "UnityEngine.EventSystems.EventTrigger": -1862395651,
    "TMPro.TextMeshProUGUI": 1453722849,
    "TMPro.TMP_InputField": -1620774994,
    "TMPro.TMP_Settings": -395462249,
}


def _rol(value, count):
    value &= 0xFFFFFFFF
    return ((value << count) | (value >> (32 - count))) & 0xFFFFFFFF


def _md4(data):
    """Self-contained MD4 (hashlib drops it on modern OpenSSL builds)."""
    msg = bytearray(data)
    bit_len = (8 * len(data)) & 0xFFFFFFFFFFFFFFFF
    msg.append(0x80)
    while len(msg) % 64 != 56:
        msg.append(0)
    msg += struct.pack("<Q", bit_len)

    a0, b0, c0, d0 = 0x67452301, 0xEFCDAB89, 0x98BADCFE, 0x10325476
    for off in range(0, len(msg), 64):
        x = list(struct.unpack("<16I", bytes(msg[off:off + 64])))
        a, b, c, d = a0, b0, c0, d0

        def f(u, v, w):
            return (u & v) | (~u & w)

        def g(u, v, w):
            return (u & v) | (u & w) | (v & w)

        def h(u, v, w):
            return u ^ v ^ w

        # Round 1: FF(a,b,c,d,k,s) -> a = rol(a + F(b,c,d) + X[k], s)
        for i in range(16):
            k = i
            s = [3, 7, 11, 19][i % 4]
            if i % 4 == 0:
                a = _rol(a + f(b, c, d) + x[k], s)
            elif i % 4 == 1:
                d = _rol(d + f(a, b, c) + x[k], s)
            elif i % 4 == 2:
                c = _rol(c + f(d, a, b) + x[k], s)
            else:
                b = _rol(b + f(c, d, a) + x[k], s)
        # Round 2: GG with k order 0,4,8,12,1,5,9,13,2,6,10,14,3,7,11,15
        order2 = [0, 4, 8, 12, 1, 5, 9, 13, 2, 6, 10, 14, 3, 7, 11, 15]
        for i, k in enumerate(order2):
            s = [3, 5, 9, 13][i % 4]
            if i % 4 == 0:
                a = _rol(a + g(b, c, d) + x[k] + 0x5A827999, s)
            elif i % 4 == 1:
                d = _rol(d + g(a, b, c) + x[k] + 0x5A827999, s)
            elif i % 4 == 2:
                c = _rol(c + g(d, a, b) + x[k] + 0x5A827999, s)
            else:
                b = _rol(b + g(c, d, a) + x[k] + 0x5A827999, s)
        # Round 3: HH with k order 0,8,4,12,2,10,6,14,1,9,5,13,3,11,7,15
        order3 = [0, 8, 4, 12, 2, 10, 6, 14, 1, 9, 5, 13, 3, 11, 7, 15]
        for i, k in enumerate(order3):
            s = [3, 9, 11, 15][i % 4]
            if i % 4 == 0:
                a = _rol(a + h(b, c, d) + x[k] + 0x6ED9EBA1, s)
            elif i % 4 == 1:
                d = _rol(d + h(a, b, c) + x[k] + 0x6ED9EBA1, s)
            elif i % 4 == 2:
                c = _rol(c + h(d, a, b) + x[k] + 0x6ED9EBA1, s)
            else:
                b = _rol(b + h(c, d, a) + x[k] + 0x6ED9EBA1, s)

        a0 = (a0 + a) & 0xFFFFFFFF
        b0 = (b0 + b) & 0xFFFFFFFF
        c0 = (c0 + c) & 0xFFFFFFFF
        d0 = (d0 + d) & 0xFFFFFFFF
    return struct.pack("<4I", a0, b0, c0, d0)


def _selftest():
    assert _md4(b"").hex() == "31d6cfe0d16ae931b73c59d7e0c089c0", _md4(b"").hex()
    assert _md4(b"abc").hex() == "a448017aaf21d8525fc10ae87aa6729d", _md4(b"abc").hex()
    assert _md4(b"message digest").hex() == "d9130a8164549fe818874806e1c7014b"


def script_file_id(namespace, class_name):
    digest = _md4(("s\0\0\0" + namespace + class_name).encode("ascii"))
    return struct.unpack("<i", digest[:4])[0]


def main(paths):
    _selftest()
    problems = 0
    for name, expected in KNOWN.items():
        ns, cls = name.rsplit(".", 1)
        got = script_file_id(ns, cls)
        if got != expected:
            problems += 1
            print("HASH MISMATCH %s -> %d (expected %d)" % (name, got, expected))
    if problems:
        raise SystemExit("refusing to relink: %d hash mismatches" % problems)

    for path in paths:
        with io.open(path, encoding="utf-8", newline="") as handle:
            text = handle.read()
        before = text
        hits = 0
        for src_guid, (ns, cls) in UGUI_SOURCE_GUIDS.items():
            fid = script_file_id(ns, cls)
            old = "{fileID: 11500000, guid: %s" % src_guid
            new = "{fileID: %d, guid: %s" % (fid, UI_DLL_GUID)
            hits += text.count(old)
            text = text.replace(old, new)
        font_hits = text.count(MISSING_TMP_FONT_GUID)
        text = text.replace(MISSING_TMP_FONT_GUID, LOCAL_TMP_FONT_GUID)
        if text != before:
            with io.open(path, "w", encoding="utf-8", newline="") as handle:
                handle.write(text)
        print("%s: %d uGUI slots relinked, %d font refs fixed" % (path, hits, font_hits))
        for line in text.splitlines():
            if "{fileID: 11500000" in line:
                print("  WARNING leftover source-form script: " + line.strip())


if __name__ == "__main__":
    main(sys.argv[1:])
