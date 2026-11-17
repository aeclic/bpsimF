"""Make a pixel-upload oracle from source-verified, read-only original PNGs.

Command: python Tools/RC2Levels/texture_reference.py --pack PACK_DIR --out REFERENCE.json
PASS: every file hash agrees with the catalog; record exact RGBA at nine PNG pixels.
Boundary: upload orientation/bytes, not filtering, blending or APK rendered-frame parity.
Invalidation: regenerate after a source verification, PNG, catalog or decoder change.
"""
import argparse
import hashlib
import json
import pathlib
from PIL import Image


def main():
    parser = argparse.ArgumentParser(); parser.add_argument('--pack', required=True); parser.add_argument('--out', required=True)
    args = parser.parse_args(); root = pathlib.Path(args.pack).resolve()
    catalog = json.loads((root / 'catalog.json').read_text(encoding='utf-8'))
    result = {'apk_sha256': catalog['apk_sha256'], 'textures': {}}
    for key, meta in catalog['assets']['textures'].items():
        path = (root / meta['path']).resolve()
        assert path.is_relative_to(root)
        assert hashlib.sha256(path.read_bytes()).hexdigest() == meta['sha256']
        image = Image.open(path).convert('RGBA'); width, height = image.size
        result['textures'][key] = {'width': width, 'height': height,
            'samples': [[x, y, list(image.getpixel((x, y)))] for y in (0, height // 2, height - 1) for x in (0, width // 2, width - 1)]}
    out = pathlib.Path(args.out); out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(result, separators=(',', ':')) + '\n', encoding='utf-8')
    print(json.dumps({'pass': True, 'textures': len(result['textures'])}))


if __name__ == '__main__': main()
