"""Read-only APK and IL2CPP typetree probe; do not execute source game code.

Command: python probe_source.py SOURCE.apk OUTPUT.json
PASS: critical SpriteMeshInstance/SpriteMeshData/WaterfallController/terrain trees
consume the complete serialized byte payload and all source pointers resolve.
Boundary: source serialized layout, not runtime animation or shader parity.
Invalidation: APK, UnityPy/TypeTreeGeneratorAPI or IL2CPP layout/version changes.
"""
import collections
import hashlib
import json
import pathlib
import re
import struct
import sys
import zipfile

import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator
from UnityPy.helpers.TypeTreeNode import TypeTreeNode


def basename(value):
    return pathlib.PurePosixPath(value.replace('\\', '/')).name


def asset_key(obj):
    return f'{basename(obj.assets_file.name)}_{obj.path_id}'


class ApkSource:
    def __init__(self, apk, generate_trees=False):
        self.apk = pathlib.Path(apk)
        self.env = UnityPy.Environment()
        self.members = {}
        with zipfile.ZipFile(self.apk) as archive:
            grouped = collections.defaultdict(list)
            for entry in archive.namelist():
                prefix = 'assets/bin/Data/'
                if not entry.startswith(prefix) or '/' in entry[len(prefix):]:
                    continue
                grouped[re.sub(r'\.split\d+$', '', entry[len(prefix):])].append(entry)
            for stem, entries in sorted(grouped.items()):
                entries.sort(key=lambda n: int(re.search(r'\.split(\d+)$', n).group(1)) if '.split' in n else -1)
                blob = b''.join(archive.read(entry) for entry in entries)
                self.members[stem] = {'sha256': hashlib.sha256(blob).hexdigest(), 'size': len(blob)}
                self.env.load_file(blob, name=stem)
            self.readers = {(basename(o.assets_file.name), o.path_id): o for o in self.env.objects}
            self.version = next(o.assets_file.unity_version for o in self.env.objects)
            if generate_trees:
                self.generator = TypeTreeGenerator(self.version)
                self.generator.load_il2cpp(archive.read('lib/arm64-v8a/libil2cpp.so'),
                                          archive.read('assets/bin/Data/Managed/Metadata/global-metadata.dat'))
                self.env.typetree_generator = self.generator
        self.data_cache = {}
        self.tree_cache = {}

    def deref(self, pointer, file):
        if isinstance(pointer, dict):
            fid, pid = pointer['m_FileID'], pointer['m_PathID']
        else:
            fid, pid = pointer.m_FileID, pointer.m_PathID
        if not pid:
            return None
        name = basename(file.name) if fid == 0 else basename(file.externals[fid - 1].path)
        reader = self.readers.get((name, pid))
        if reader is None:
            raise ValueError(f'Unresolved source pointer {name}:{pid} from {file.name}')
        return reader

    def script(self, reader):
        fid, pid = struct.unpack_from('<iq', reader.get_raw_data(), 16)
        script = self.deref({'m_FileID': fid, 'm_PathID': pid}, reader.assets_file)
        return script.read() if script is not None else None

    def data(self, reader):
        k = asset_key(reader)
        if k not in self.data_cache:
            nodes = None
            if reader.type.name == 'MonoBehaviour':
                script = self.script(reader)
                if script is None:
                    raise ValueError('MonoBehaviour has no script reference')
                namespace = script.m_Namespace
                fullname = namespace + '.' + script.m_ClassName if namespace else script.m_ClassName
                # AssetsTools' IL2CPP loader uses extensionless assembly names, unlike
                # its Mono loader and UnityPy's automatic wrapper (upstream issue #9).
                assembly = script.m_AssemblyName.removesuffix('.dll')
                tree_key = assembly, fullname
                if tree_key not in self.tree_cache:
                    generated = self.generator.get_nodes(assembly, fullname)
                    self.tree_cache[tree_key] = TypeTreeNode.from_list([
                        TypeTreeNode(n.m_Level, n.m_Type, n.m_Name, 0, 0,
                                     m_MetaFlag=((n.m_MetaFlag or 0) | 0x4000)
                                     if n.m_Name == 'm_Enabled' and n.m_Level == 1 else n.m_MetaFlag)
                        for n in generated
                    ])
                    # The IL2CPP generator omits the alignment on Unity's byte-sized
                    # m_Enabled. Correct against the native serialized header, not by
                    # accepting a tree merely because its final byte count happens to fit.
                nodes = self.tree_cache[tree_key]
            self.data_cache[k] = reader.read_typetree(nodes=nodes, check_read=True)
            if reader.type.name == 'MonoBehaviour':
                expected = struct.unpack_from('<iq', reader.get_raw_data(), 16)
                actual = self.data_cache[k]['m_Script']
                if expected != (actual['m_FileID'], actual['m_PathID']):
                    raise ValueError(f'Typetree header mismatch for {k}')
        return self.data_cache[k]
