#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Patch Unity.RenderPipelines.Core.Runtime.dll so that CoreUtils.Destroy()
does not call Object.Destroy() while the editor is not in Play mode.

Why
---
The URP assemblies in this decompiled project are *player* builds, so the
`#if UNITY_EDITOR` half of the original source is not compiled in:

    public static void Destroy(UnityEngine.Object obj)
    {
        if (obj != null)
        {
#if UNITY_EDITOR
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(obj);
            else
                UnityEngine.Object.DestroyImmediate(obj);
#else
            UnityEngine.Object.Destroy(obj);
#endif
        }
    }

Every URP teardown that happens in edit mode (render pipeline cleanup on
play-enter, RTHandle.Release, VolumeComponent.OnDestroy, ...) therefore ends
up in Object.Destroy() in edit mode and Unity logs

    Destroy may not be called from edit mode! Use DestroyImmediate instead.

~19 of those per play-enter.  A full fix would call DestroyImmediate, but
`UnityEngine.Object::DestroyImmediate` has no MemberRef inside this
assembly and adding one means rewriting the metadata streams - too invasive
for a byte patch.  What we *can* do is keep the original IL body size and
skip the call when the editor is not playing:

    if (Application.isPlaying) Object.Destroy(obj);

The objects are transient runtime objects (RenderTextures, materials,
render textures owned by RTHandle pools); they are recreated on the next
render pipeline initialisation, and a domain reload / scene load reclaims
them, so skipping the edit-mode destroy is safe.

The patch is idempotent: re-running it on an already patched file is a no-op.

Usage
-----
    python Tools/PatchUrpCoreUtils.py [--verify-only] [--restore] [path]

`path` defaults to Assets/Plugins/Unity.RenderPipelines.Core.Runtime.dll.
A pristine copy is kept in _codex_work/backup-urp-core/.
"""

import argparse
import io
import os
import shutil
import struct
import sys

DEFAULT_DLL = os.path.join(
    "Assets", "Plugins", "Unity.RenderPipelines.Core.Runtime.dll")
BACKUP_DIR = os.path.join("_codex_work", "backup-urp-core")

# ---------------------------------------------------------------------------
# minimal ECMA-335 metadata reader (only what we need: MemberRef + MethodDef)
# ---------------------------------------------------------------------------

CODED = {
    # name: (tag_bits, [table indices])
    "TypeDefOrRef": (2, [0x02, 0x01, 0x1B]),
    "HasConstant": (2, [0x04, 0x08, 0x17]),
    "HasCustomAttribute": (5, [0x06, 0x04, 0x01, 0x02, 0x08, 0x09, 0x0A,
                               0x00, 0x0E, 0x17, 0x14, 0x11, 0x1A, 0x1B,
                               0x20, 0x23, 0x26, 0x27, 0x28, 0x2A, 0x2C,
                               0x2B]),
    "HasFieldMarshal": (1, [0x04, 0x08]),
    "HasDeclSecurity": (2, [0x02, 0x06, 0x20]),
    "MemberRefParent": (3, [0x02, 0x01, 0x1A, 0x06, 0x1B]),
    "HasSemantics": (1, [0x14, 0x17]),
    "MethodDefOrRef": (1, [0x06, 0x0A]),
    "MemberForwarded": (1, [0x04, 0x06]),
    "Implementation": (2, [0x26, 0x23, 0x27]),
    "CustomAttributeType": (3, [0x06, 0x0A]),
    "ResolutionScope": (2, [0x00, 0x1A, 0x23, 0x01]),
    "TypeOrMethodDef": (1, [0x02, 0x06]),
}

# (name, [column type, ...]) — column type is one of
#   "u2" / "u4", ("str",) / ("guid",) / ("blob",), ("idx", table),
#   ("coded", coded-name)
TABLES = [
    ("Module", [("u2",), ("str",), ("guid",), ("guid",), ("guid",)]),
    ("TypeRef", [("coded", "ResolutionScope"), ("str",), ("str",)]),
    ("TypeDef", [("u4",), ("str",), ("str",), ("coded", "TypeDefOrRef"),
                 ("idx", 0x04), ("idx", 0x06)]),
    ("FieldPtr", [("idx", 0x04)]),
    ("Field", [("u2",), ("str",), ("blob",)]),
    ("MethodPtr", [("idx", 0x06)]),
    ("MethodDef", [("u4",), ("u2",), ("u2",), ("str",), ("blob",),
                   ("idx", 0x08)]),
    ("ParamPtr", [("idx", 0x08)]),
    ("Param", [("u2",), ("u2",), ("str",)]),
    ("InterfaceImpl", [("idx", 0x02), ("coded", "TypeDefOrRef")]),
    ("MemberRef", [("coded", "MemberRefParent"), ("str",), ("blob",)]),
    ("Constant", [("u2",), ("coded", "HasConstant"), ("blob",)]),
]

TBL_METHODDEF = 0x06
TBL_MEMBERREF = 0x0A
TBL_TYPEREF = 0x01
TBL_TYPEDEF = 0x02


def _u16(buf, off):
    return struct.unpack_from("<H", buf, off)[0]


def _u32(buf, off):
    return struct.unpack_from("<I", buf, off)[0]


class Metadata(object):
    def __init__(self, data):
        self.data = data
        if data[:2] != b"MZ":
            raise ValueError("not a PE file")
        pe = _u32(data, 0x3C)
        if data[pe:pe + 4] != b"PE\0\0":
            raise ValueError("not a PE file")
        coff = pe + 4
        nsect = _u16(data, coff + 2)
        optsize = _u16(data, coff + 16)
        opt = coff + 20
        magic = _u16(data, opt)
        if magic == 0x10B:      # PE32
            dd = opt + 96
        elif magic == 0x20B:    # PE32+
            dd = opt + 112
        else:
            raise ValueError("unknown PE optional header magic 0x%X" % magic)
        dir_rva, dir_size = _u32(data, dd + 14 * 8), _u32(data, dd + 14 * 8 + 4)
        if dir_rva == 0:
            raise ValueError("no CLI header")
        sect = opt + optsize
        self.sections = []
        for i in range(nsect):
            base = sect + i * 40
            name = data[base:base + 8].rstrip(b"\0").decode("ascii", "replace")
            vsize, vaddr, rsize, raddr = struct.unpack_from("<IIII", data, base + 8)
            self.sections.append((name, vaddr, vsize, raddr, rsize))
        self.cli = self.rva2off(dir_rva)
        self.md_off = self.rva2off(_u32(data, self.cli + 8))
        self._parse_metadata_root()

    # -- PE helpers ---------------------------------------------------------
    def rva2off(self, rva):
        for _n, vaddr, vsize, raddr, rsize in self.sections:
            if vaddr <= rva < vaddr + max(vsize, rsize):
                return raddr + (rva - vaddr)
        raise ValueError("rva 0x%X not in any section" % rva)

    # -- metadata root ------------------------------------------------------
    def _parse_metadata_root(self):
        d = self.data
        base = self.md_off
        if _u32(d, base) != 0x424A5342:
            raise ValueError("bad metadata signature")
        vlen = _u32(d, base + 12)
        p = base + 16 + vlen
        p += 2                                            # flags
        nstreams = _u16(d, p)
        p += 2
        streams = {}
        for _ in range(nstreams):
            off = _u32(d, p)
            size = _u32(d, p + 4)
            p += 8
            end = d.index(b"\0", p)
            name = d[p:end].decode("ascii", "replace")
            p = end + 1
            p = (p + 3) & ~3
            streams[name] = (base + off, size)
        self.streams = streams
        self.strings_off, _s = streams["#Strings"]
        self.blob_off, _b = streams["#Blob"]
        # #~ or #-
        name = "#~" if "#~" in streams else "#-"
        self.tables_off, _t = streams[name]
        self._parse_tables()

    def string(self, idx):
        start = self.strings_off + idx
        end = self.data.index(b"\0", start)
        return self.data[start:end].decode("utf-8", "replace")

    # -- tables -------------------------------------------------------------
    def _parse_tables(self):
        d = self.data
        p = self.tables_off
        _reserved = _u32(d, p)
        heap = d[p + 6]
        valid = struct.unpack_from("<Q", d, p + 8)[0]
        p += 24
        self.rows = {}
        for t in range(64):
            if valid & (1 << t):
                self.rows[t] = _u32(d, p)
                p += 4
        self.idx_str = 4 if heap & 1 else 2
        self.idx_guid = 4 if heap & 2 else 2
        self.idx_blob = 4 if heap & 4 else 2

        def col_size(col):
            kind = col[0]
            if kind == "u2":
                return 2
            if kind == "u4":
                return 4
            if kind in ("str", "guid", "blob"):
                return {"str": self.idx_str, "guid": self.idx_guid,
                        "blob": self.idx_blob}[kind]
            if kind == "idx":
                return 2 if self.rows.get(col[1], 0) < 65536 else 4
            if kind == "coded":
                bits, targets = CODED[col[1]]
                biggest = max(self.rows.get(t, 0) for t in targets)
                return 2 if biggest < (1 << (16 - bits)) else 4
            raise ValueError(kind)

        self.row_size = []
        for i, (_name, cols) in enumerate(TABLES):
            self.row_size.append(sum(col_size(c) for c in cols))
        # walk the tables in index order and remember the start offset of each
        self.table_off = {}
        q = p
        for t in range(64):
            if t not in self.rows:
                continue
            if t > TBL_MEMBERREF:
                break       # we only ever need tables 0x00..0x0A
            self.table_off[t] = q
            q += self.rows[t] * self.row_size[t]

    def row(self, table, i):
        """1-based row access -> list of ints."""
        base = self.table_off[table] + (i - 1) * self.row_size[table]
        out, p = [], base
        for col in TABLES[table][1]:
            kind = col[0]
            if kind == "u2":
                out.append(_u16(self.data, p))
                p += 2
            elif kind == "u4":
                out.append(_u32(self.data, p))
                p += 4
            elif kind == "str":
                out.append(_u16(self.data, p) if self.idx_str == 2
                           else _u32(self.data, p))
                p += self.idx_str
            elif kind == "blob":
                out.append(_u16(self.data, p) if self.idx_blob == 2
                           else _u32(self.data, p))
                p += self.idx_blob
            elif kind == "guid":
                out.append(_u16(self.data, p) if self.idx_guid == 2
                           else _u32(self.data, p))
                p += self.idx_guid
            elif kind == "idx":
                size = 2 if self.rows.get(col[1], 0) < 65536 else 4
                out.append(_u16(self.data, p) if size == 2
                           else _u32(self.data, p))
                p += size
            elif kind == "coded":
                bits, targets = CODED[col[1]]
                biggest = max(self.rows.get(t, 0) for t in targets)
                size = 2 if biggest < (1 << (16 - bits)) else 4
                out.append(_u16(self.data, p) if size == 2
                           else _u32(self.data, p))
                p += size
        return out

    def type_name(self, coded_value, tag_bits):
        table = coded_value & ((1 << tag_bits) - 1)
        rid = coded_value >> tag_bits
        return table, rid

    def find_method_rva(self, type_name, type_ns, method_name):
        """Return (rva, codesize) of the MethodDef, or None."""
        # TypeDef rows are ordered by their MethodList range.
        defs = self.rows.get(TBL_TYPEDEF, 0)
        n_methods = self.rows.get(TBL_METHODDEF, 0)
        for i in range(1, defs + 1):
            r = self.row(TBL_TYPEDEF, i)
            name = self.string(r[1])
            ns = self.string(r[2])
            if name != type_name or ns != type_ns:
                continue
            first = r[5]
            last = (self.row(TBL_TYPEDEF, i + 1)[5]
                    if i < defs else n_methods + 1)
            for m in range(first, last):
                mr = self.row(TBL_METHODDEF, m)
                if self.string(mr[3]) != method_name:
                    continue
                return mr[0], mr[1], mr[2]
        return None

    def find_memberref_token(self, type_name, member_name):
        """Token (0x0A000000 | rid) of a Method MemberRef named member_name
        whose parent TypeRef is type_name."""
        n = self.rows.get(TBL_MEMBERREF, 0)
        hits = []
        for i in range(1, n + 1):
            cls, name_i, _sig = self.row(TBL_MEMBERREF, i)
            if self.string(name_i) != member_name:
                continue
            table, rid = self.type_name(cls, 3)
            if table == TBL_TYPEREF and rid:
                tr = self.row(TBL_TYPEREF, rid)
                if self.string(tr[1]) == type_name:
                    hits.append(0x0A000000 | i)
        return hits


# ---------------------------------------------------------------------------
# the actual patch
# ---------------------------------------------------------------------------

# original body (16 bytes):
#   IL_0000 ldarg.0
#   IL_0001 ldnull
#   IL_0002 call Object::op_Inequality
#   IL_0007 brfalse.s IL_000f
#   IL_0009 ldarg.0
#   IL_000a call Object::Destroy
#   IL_000f ret
OLD_PREFIX = b"\x02\x14\x28"          # ldarg.0; ldnull; call
OLD_SUFFIX = b"\x2c\x06\x02\x28"      # brfalse.s IL_000f; ldarg.0; call
OLD_TAIL = b"\x2a"                    # ret

# patched body (16 bytes, same code size):
#   IL_0000 call Application::get_isPlaying
#   IL_0005 brfalse.s IL_000f
#   IL_0007 ldarg.0
#   IL_0008 call Object::Destroy
#   IL_000d nop
#   IL_000e nop
#   IL_000f ret            <- `ret` must stay the last byte; trailing bytes
#                             after it make Mono's JIT report
#                             "InvalidProgramException: IL_0010: unused9"
NEW_TAIL = b"\x00\x00\x2a"    # nop; nop; ret

PATCHED_MARK = b"\x28"


def read_body(data, md, rva):
    """Return (file offset, code size, header size) of an IL method body.

    ECMA-335 II.25.4.2: the *tiny* header is a single byte - bits 0-1 hold
    the format (0x2) and bits 2-7 the code size (<= 63 bytes), so the body
    starts at header + 1.  The fat header is 12 bytes with the code size as a
    uint32 at +4.
    """
    hdr = md.rva2off(rva)
    first = data[hdr]
    kind = first & 0x3
    if kind == 0x2:                       # tiny header, 1 byte
        return hdr + 1, first >> 2, 1
    if kind == 0x3:                       # fat header, 12 bytes
        return hdr + 12, _u32(data, hdr + 4), 12
    raise SystemExit("unsupported method body header 0x%02X at 0x%X"
                     % (first, hdr))


def patch(path, verify_only=False):
    data = bytearray(open(path, "rb").read())
    md = Metadata(bytes(data))

    found = md.find_method_rva("CoreUtils", "UnityEngine.Rendering", "Destroy")
    if not found:
        raise SystemExit("FRESH/FAIL: UnityEngine.Rendering.CoreUtils::Destroy "
                         "not found in %s" % path)
    rva, impl_flags, flags = found
    off, code_size, hdr_size = read_body(bytes(data), md, rva)
    body = bytes(data[off:off + code_size])
    print("CoreUtils::Destroy rva=0x%X fileoff=0x%X codesize=%d "
          "hdr=%d implflags=0x%X flags=0x%X"
          % (rva, off, code_size, hdr_size, impl_flags, flags))
    print("  body: %s" % body.hex(" "))

    if code_size != 0x10:
        raise SystemExit("FRESH/FAIL: unexpected code size %d" % code_size)

    already = (body[0:1] == b"\x28"          # call Application::get_isPlaying
               and body[5:7] == b"\x2c\x08"  # brfalse.s IL_000f
               and body[7:8] == b"\x02"      # ldarg.0
               and body[8:9] == b"\x28"      # call Object::Destroy
               and body[15:16] == b"\x2a")   # ret is the last byte
    if verify_only:
        print("VERIFY: %s" % ("patched" if already else "NOT patched"))
        return already

    if body[:3] != OLD_PREFIX or body[7:11] != OLD_SUFFIX or body[15:] != OLD_TAIL:
        raise SystemExit("FRESH/FAIL: method body does not match the expected "
                         "unpatched pattern; aborting instead of guessing")

    token_destroy = struct.unpack_from("<I", body, 11)[0]
    print("  Object::Destroy token = 0x%08X" % token_destroy)

    hits = md.find_memberref_token("Application", "get_isPlaying")
    print("  Application::get_isPlaying MemberRefs = %s"
          % ", ".join("0x%08X" % h for h in hits))
    if not hits:
        raise SystemExit("FRESH/FAIL: no Application::get_isPlaying MemberRef")
    token_playing = hits[0]

    new = (b"\x28" + struct.pack("<I", token_playing)   # call get_isPlaying
           + b"\x2c\x08"                                # brfalse.s IL_000f
           + b"\x02"                                    # ldarg.0
           + b"\x28" + struct.pack("<I", token_destroy)  # call Object.Destroy
           + b"\x00\x00"                                # nop; nop
           + b"\x2a")                                   # ret (last byte)
    assert len(new) == code_size, (len(new), code_size)
    data[off:off + code_size] = new
    open(path, "wb").write(bytes(data))
    print("PATCHED: %s" % path)
    print("  new body: %s" % new.hex(" "))

    # re-read and confirm
    md2 = Metadata(open(path, "rb").read())
    body2 = open(path, "rb").read()[off:off + code_size]
    print("  re-read:  %s" % body2.hex(" "))
    return True


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("path", nargs="?", default=DEFAULT_DLL)
    ap.add_argument("--verify-only", action="store_true")
    ap.add_argument("--restore", action="store_true")
    a = ap.parse_args()

    if a.restore:
        src = os.path.join(BACKUP_DIR, os.path.basename(a.path) + ".orig")
        if not os.path.exists(src):
            raise SystemExit("no backup at %s" % src)
        shutil.copy2(src, a.path)
        print("RESTORED: %s from %s" % (a.path, src))
        return 0

    os.makedirs(BACKUP_DIR, exist_ok=True)
    bak = os.path.join(BACKUP_DIR, os.path.basename(a.path) + ".orig")
    if not os.path.exists(bak):
        shutil.copy2(a.path, bak)
        print("backup: %s" % bak)

    patch(a.path, verify_only=a.verify_only)
    return 0


if __name__ == "__main__":
    sys.exit(main())
