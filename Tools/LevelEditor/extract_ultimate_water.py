"""Extract the demo's actual water programs, material values and texture pixels.

Unity 6 stores GLES vertex/fragment programs together. UnityPy's legacy shader
exporter drops these player subprograms, so extract the length-prefixed GLSL
payload from the decompressed GPU program blob instead.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct

import UnityPy
from UnityPy.export.Texture2DConverter import parse_image_data
from UnityPy.helpers import CompressionHelper


SHADERS = ("WaterSurface", "Underwater", "LightRays", "FakePerspectiveSurface")
TEXTURES = (
    "WaterNormals_01", "Foam_01", "Foam_02", "Foam_03", "Splash", "Mist",
    "Large01", "Large02", "Medium01", "Medium02", "Medium03", "Medium04",
    "Medium05", "Medium06", "Thin01", "Thin02",
)


def write_json(path: Path, value: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")


def texture_name(pointer: object) -> str | None:
    if not pointer.path_id:
        return None
    value = pointer.read()
    return getattr(value, "m_Name", None) if pointer.deref().type.name == "Texture2D" else None


def material_data(material: object) -> dict:
    values = material.m_SavedProperties
    return {
        "floats": dict(values.m_Floats),
        "vectors": {key: [color.r, color.g, color.b, color.a] for key, color in values.m_Colors},
        "textures": {
            key: {
                "name": texture_name(value.m_Texture),
                "scale": [value.m_Scale.x, value.m_Scale.y],
                "offset": [value.m_Offset.x, value.m_Offset.y],
            }
            for key, value in values.m_TexEnvs
        },
    }


def glsl_program(shader: object) -> str:
    blob = bytes(shader.compressedBlob)
    for offsets, sizes, raw_sizes in zip(
        shader.offsets, shader.compressedLengths, shader.decompressedLengths
    ):
        for offset, size, raw_size in zip(offsets, sizes, raw_sizes):
            raw = CompressionHelper.decompress_lz4(blob[offset:offset + size], raw_size)
            for match in re.finditer(rb"#ifdef VERTEX\r?\n#version 300 es", raw):
                start = match.start()
                size = struct.unpack_from("<I", raw, start - 4)[0]
                payload = raw[start:start + size]
                # The payload includes both stages, not a nul-terminated source file.
                if size > len(raw) - start or size < 100:
                    continue
                source = payload.rstrip(b"\0").decode("utf-8")
                if "#ifdef VERTEX" in source and "#ifdef FRAGMENT" in source:
                    return source
    raise ValueError("No complete GLES3 program in " + shader.m_ParsedForm.m_Name)


def transform_of(game_object: object) -> object:
    for component in game_object.m_Component:
        pointer = component.component
        if pointer.deref().type.name == "Transform":
            return pointer.read()
    raise ValueError("Missing Transform")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--demo", required=True, type=Path)
    parser.add_argument("--project", type=Path, default=Path(__file__).resolve().parents[2])
    args = parser.parse_args()
    bundle = args.demo / "extract" / "data.unity3d"
    environment = UnityPy.load(str(bundle))
    mod = args.project / "Mods" / "LevelEditor"
    target = mod / "Web" / "bpsim" / "ultimate-water"
    target.mkdir(parents=True, exist_ok=True)
    texture_target = mod / "Textures" / "UltimateWater"
    texture_target.mkdir(parents=True, exist_ok=True)
    report = {
        "source": "Ultimate Water 2D / DW Demo Scene",
        "unity": "6000.2.9f1",
        "source_sha256": hashlib.sha256(bundle.read_bytes()).hexdigest(),
        "shaders": {},
        "textures": {},
        "materials": {},
    }
    for obj in environment.objects:
        if obj.type.name == "Shader":
            shader = obj.read()
            name = shader.m_ParsedForm.m_Name.rsplit("/", 1)[-1]
            if name not in SHADERS:
                continue
            source = glsl_program(shader)
            path = target / (name + ".glsl")
            path.write_text(source, encoding="utf-8", newline="\n")
            properties = {}
            for prop in shader.m_ParsedForm.m_PropInfo.m_Props:
                if prop.m_Type in (0, 1):
                    properties[prop.m_Name] = [
                        prop.m_DefValue_0_, prop.m_DefValue_1_,
                        prop.m_DefValue_2_, prop.m_DefValue_3_,
                    ]
                elif prop.m_Type in (2, 3):
                    properties[prop.m_Name] = prop.m_DefValue_0_
            report["shaders"][name] = {
                "sha256": hashlib.sha256(source.encode("utf-8")).hexdigest(),
                "properties": properties,
            }
        elif obj.type.name == "Texture2D":
            texture = obj.read()
            if texture.m_Name not in TEXTURES:
                continue
            path = texture_target / (texture.m_Name + ".png")
            texture.image.save(path, optimize=True)
            web_path = target / path.name
            web_path.write_bytes(path.read_bytes())
            # Preserve the imported, compressed asset's mip pixels. Regenerating
            # mipmaps from the top PNG changes distant foam and normal sampling.
            raw, offset, pixels = texture.get_image_data(), 0, bytearray()
            mip_count = max(1, texture.m_MipCount)
            for level in range(mip_count):
                width, height = max(1, texture.m_Width >> level), max(1, texture.m_Height >> level)
                fmt = texture.m_TextureFormat
                size = max(1, (width+3)//4)*max(1, (height+3)//4)*(8 if fmt == 10 else 16) if fmt in (10,12) else width*height*(1 if fmt == 1 else 4)
                image = parse_image_data(raw[offset:offset+size], width, height, fmt,
                    texture.object_reader.version, texture.object_reader.platform,
                    getattr(texture, 'm_PlatformBlob', None), False)
                pixels.extend(image.convert('RGBA').tobytes())
                offset += size
            (target / (texture.m_Name + '.mips.bin')).write_bytes(pixels)
            report["textures"][texture.m_Name] = {
                "width": texture.m_Width, "height": texture.m_Height,
                "format": texture.m_TextureFormat, "color_space": texture.m_ColorSpace,
                "mip_count": mip_count,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
            }
        elif obj.type.name == "MeshRenderer":
            renderer = obj.read()
            if not renderer.m_GameObject.path_id:
                continue
            game_object = renderer.m_GameObject.read()
            if game_object.m_Name not in ("SurfaceWater", "Underwater"):
                continue
            transform = transform_of(game_object)
            parent = transform.m_Father.read().m_GameObject.read()
            if parent.m_Name != "Ultimate Water Body 2.5D":
                continue
            for pointer in renderer.m_Materials:
                if pointer.path_id:
                    report["materials"][game_object.m_Name] = material_data(pointer.read())
    missing = set(SHADERS) - set(report["shaders"])
    if missing or len(report["materials"]) != 2:
        raise SystemExit(f"Incomplete extraction: shaders={missing}, materials={list(report['materials'])}")
    write_json(target / "demo-profile.json", report)
    write_json(mod / "Assets" / "ultimate-water-profile.json", report)
    print(json.dumps({
        "shaders": list(report["shaders"]),
        "textures": len(report["textures"]),
        "materials": list(report["materials"]),
        "output": str(target),
    }))


if __name__ == "__main__":
    main()
