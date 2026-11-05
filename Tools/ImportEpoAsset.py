"""Import the user's EPO 3.7 core/URP assets without Demo, HDRP or package replacement.

Run while holding the project's Unity editor lease. GUIDs and vendor payloads are
preserved, except asmdef references adapted to this decompilation's URP DLLs.
"""
import hashlib
import json
import pathlib
import sys
import tarfile


def main():
    workspace = pathlib.Path(__file__).resolve().parents[1]
    package = pathlib.Path(sys.argv[1]).resolve()
    prefix = "Assets/Plugins/Easy performant outline"
    imported = []
    adapted = []
    with tarfile.open(package) as archive:
        members = {item.name: item for item in archive.getmembers()}
        for name in members:
            if not name.endswith("/pathname"):
                continue
            path = archive.extractfile(name).read().decode("utf-8-sig").splitlines()[0]
            if path != prefix and not path.startswith(prefix + "/"):
                continue
            if any(part in path for part in ("/Demo", "/HDRP support", "/DOTween support")):
                continue
            if path.endswith(("/FinalFullScreenBlit.shader", "/_FinalFullScreenBlit.mat")):
                continue  # HDRP/Unity 6-only final blit, not used by the URP 14 direct-blit path.
            target = (workspace / path).resolve()
            target.relative_to(workspace / "Assets")
            key = name.rsplit("/", 1)[0]
            if key + "/asset" in members:
                data = archive.extractfile(key + "/asset").read()
                if path.endswith("/PipelineAssetUtility.cs"):
                    text = data.decode("utf-8-sig").replace("\r\n", "\n").replace("\r", "")
                    text = text.replace("            var pipeline = UniversalRenderPipelineAsset.Create(data);",
                                        "            // bpsim uses restored URP runtime DLLs without the editor-only Create factory.\n"
                                        "            var pipeline = ScriptableObject.CreateInstance<UniversalRenderPipelineAsset>();\n"
                                        "            var serializedPipeline = new SerializedObject(pipeline);\n"
                                        "            var rendererList = serializedPipeline.FindProperty(\"m_RendererDataList\");\n"
                                        "            rendererList.arraySize = 1;\n"
                                        "            rendererList.GetArrayElementAtIndex(0).objectReferenceValue = data;\n"
                                        "            serializedPipeline.ApplyModifiedPropertiesWithoutUndo();")
                    data = text.encode("utf-8-sig")
                if path.endswith(".asmdef"):
                    definition = json.loads(data.decode("utf-8-sig"))
                    # Core/URP runtime is provided by auto-referenced Plugins DLLs, not asmdefs.
                    definition["references"] = [ref for ref in definition["references"]
                                                if not ref.startswith("Unity.RenderPipelines.") and ref != "EPOHDRP"]
                    data = (json.dumps(definition, indent=4) + "\n").encode("utf-8")
                    adapted.append(path)
                if target.exists() and target.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"").rstrip(b"\n") != data.replace(b"\r\n", b"\n").replace(b"\r", b"").rstrip(b"\n"):
                    raise RuntimeError("Refusing to overwrite a different existing asset: " + path)
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(data)
                imported.append(path)
            else:
                target.mkdir(parents=True, exist_ok=True)
            if key + "/asset.meta" in members:
                meta = target.with_name(target.name + ".meta")
                data = archive.extractfile(key + "/asset.meta").read()
                if meta.exists() and meta.read_bytes() != data:
                    raise RuntimeError("Conflicting asset GUID metadata: " + str(meta))
                meta.write_bytes(data)
    manifest = {"source": package.name, "sha256": hashlib.sha256(package.read_bytes()).hexdigest(),
                "version": "3.7", "excluded": ["Demo", "HDRP support", "DOTween support", "HDRP FinalFullScreenBlit shader/material", "Packages/manifest.json"],
                "adapted_sources": ["Scripts/Editor/PipelineAssetUtility.cs: URP editor-only asset factory replacement"],
                "adapted_asmdefs": adapted, "imported": imported}
    (workspace / "Tools/EpoAssetImport.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print("Imported", len(imported), "EPO core/URP assets; adapted", len(adapted), "asmdefs")


if __name__ == "__main__":
    main()
