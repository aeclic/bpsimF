"""Read-only APK metadata/native instrument (no APK execution or modification).

Run: python Tools/BPSim/inspect_archive_elastic_parts.py --out Logs/archive-elastic-parts.json
PASS: metadata v31, Assembly-CSharp code module, requested type/method tokens and
native BellowsForce instructions resolve; nonzero exit means FAIL, not a fallback.
Coverage: ARM64 metadata/symbols/instructions only, not APK runtime/visual parity.
Invalidated by metadata/ELF ABI changes; fingerprints are included in every output.
"""
import argparse
import hashlib
import json
import struct
import zipfile
from pathlib import Path

import capstone


def inspect(apk, wanted=None, disassemble=None, max_bytes=512):
    with zipfile.ZipFile(apk) as archive:
        metadata = archive.read('assets/bin/Data/Managed/Metadata/global-metadata.dat')
        binary = archive.read('lib/arm64-v8a/libil2cpp.so')
    assert struct.unpack_from('<II', metadata) == (0xFAB11BAF, 31), 'Unsupported metadata ABI'
    strings = struct.unpack_from('<I', metadata, 24)[0]
    fields = struct.unpack_from('<I', metadata, 96)[0]
    methods = struct.unpack_from('<I', metadata, 48)[0]
    types, type_bytes = struct.unpack_from('<II', metadata, 160)
    assert type_bytes % 88 == 0

    def string(index):
        assert index >= 0
        start = strings + index
        return metadata[start:metadata.index(b'\0', start)].decode('utf-8')

    if wanted is None:
        wanted = {'Bpsim.Parts.Spring', 'Bpsim.Parts.SpringEndpoint',
                  'Bpsim.Parts.SpringSystem', 'Bpsim.Parts.ModeBAerodynamicsMath'}
    if disassemble is None:
        disassemble = {'BellowsForce', 'BellowsCompressionScale', 'CreateSpringJoint'}
    result = {}
    for offset in range(types, types + type_bytes, 88):
        definition = struct.unpack_from('<16i8H2I', metadata, offset)
        name = string(definition[1]) + '.' + string(definition[0])
        if name not in wanted:
            continue
        fs, ms, mc, fc = definition[8], definition[9], definition[16], definition[18]
        rows = []
        for i in range(ms, ms + mc):
            method_name = string(struct.unpack_from('<i', metadata, methods + 36 * i)[0])
            token = struct.unpack_from('<I', metadata, methods + 36 * i + 24)[0]
            assert token >> 24 == 6
            rows.append({'name': method_name, 'token': token})
        result[name] = {
            'fields': [string(struct.unpack_from('<i', metadata, fields + 12 * i)[0])
                       for i in range(fs, fs + fc)], 'methods': rows}
    assert set(result) == wanted, 'Reference types missing'
    assert binary[:6] == b'\x7fELF\x02\x01', 'Expected little-endian ELF64'
    assert struct.unpack_from('<H', binary, 18)[0] == 183, 'Expected ARM64'
    ph = struct.unpack_from('<Q', binary, 32)[0]
    ph_size, ph_count = struct.unpack_from('<HH', binary, 54)
    segments = []
    for i in range(ph_count):
        p = struct.unpack_from('<IIQQQQQQ', binary, ph + i * ph_size)
        if p[0] == 1:
            segments.append((p[2], p[3], p[5]))

    def file_offset(address):
        return next(o + address - v for o, v, n in segments if v <= address < v + n)

    sh = struct.unpack_from('<Q', binary, 40)[0]
    sh_size, sh_count = struct.unpack_from('<HH', binary, 58)
    relocations = {}
    for i in range(sh_count):
        section = struct.unpack_from('<IIQQQQIIQQ', binary, sh + i * sh_size)
        if section[1] == 4:  # SHT_RELA / R_AARCH64_RELATIVE
            for j in range(section[4], section[4] + section[5], 24):
                address, info, addend = struct.unpack_from('<QQq', binary, j)
                if info & 0xffffffff == 1027:
                    relocations[address] = addend

    def pointer(address):
        if address in relocations:
            return relocations[address]
        return struct.unpack_from('<Q', binary, file_offset(address))[0]

    pos = binary.index(b'Assembly-CSharp.dll\0')
    string_address = next(v + pos - o for o, v, n in segments if o <= pos < o + n)
    candidates = [a for a, value in relocations.items() if value == string_address
                  and 0 < pointer(a + 8) < 100000]
    assert len(candidates) == 1, 'Ambiguous code module'
    module = candidates[0]
    count, method_pointers = pointer(module + 8), pointer(module + 16)
    disassembler = capstone.Cs(capstone.CS_ARCH_ARM64, capstone.CS_MODE_ARM)
    for type_info in result.values():
        for row in type_info['methods']:
            index = (row['token'] & 0xffffff) - 1
            assert index < count
            row['address'] = pointer(method_pointers + index * 8)
            if row['name'] not in disassemble:
                continue
            address = row['address']
            offset = file_offset(address)
            row['instructions'] = []
            for ins in disassembler.disasm(binary[offset:offset + max_bytes], address):
                row['instructions'].append(f'{ins.address:x}: {ins.mnemonic} {ins.op_str}')
                if ins.mnemonic == 'ret':
                    break
    if 'Bpsim.Parts.ModeBAerodynamicsMath' in result:
        force = next(m for m in result['Bpsim.Parts.ModeBAerodynamicsMath']['methods']
                     if m['name'] == 'BellowsForce')
        assert any('0.50000000' in x for x in force['instructions'])
        assert any('fmul s0, s0, s0' in x for x in force['instructions'])
    return {'apk': str(apk.resolve()), 'metadata_sha256': hashlib.sha256(metadata).hexdigest(),
            'libil2cpp_sha256': hashlib.sha256(binary).hexdigest(),
            'abi': 'metadata31 / ARM64 / method36 / type88', 'types': result,
            'verdict': 'PASS', 'boundary': 'No APK gameplay, timing or render equivalence claim'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--apk', type=Path, default=Path('bpsimarchive.apk'))
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    output = inspect(args.apk)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(output, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('PASS: archive elastic-part metadata and native instructions;', args.out)
