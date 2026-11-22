"""Prepare a small, private Unity project from this checkout's actual physics dependencies."""
import argparse
import hashlib
import json
import shutil
import subprocess
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-dll", type=Path, help="stage a candidate Box3D DLL in the private project")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    destination = root / "_codex_work/anvil-parity/project"
    destination.mkdir(parents=True, exist_ok=True)
    unity = Path(json.loads((root / "Tools/dsh-slots/config.json").read_text(encoding="utf-8-sig"))["unity"])
    compiler = unity.parent / "Data/DotNetSdkRoslyn/csc.dll"
    runtime = compiler.parent.parent / "NetCoreRuntime/dotnet.exe"
    plugins = destination / "Assets/Plugins"
    plugins.mkdir(parents=True, exist_ok=True)

    # Copy only the transitive physics dependency closure, not the game's assemblies/scenes.
    assemblies = (
        "Unity.Burst", "Unity.Collections", "Unity.Entities", "Unity.Mathematics",
        "Unity.Mathematics.Extensions", "Unity.Profiling.Core", "Unity.Serialization", "Unity.Transforms",
    )
    for name in assemblies:
        shutil.copy2(root / "Library/ScriptAssemblies" / (name + ".dll"), plugins / (name + ".dll"))
    for name in ("Havok.Physics.dll", "Unity.Burst.Unsafe.dll"):
        shutil.copy2(root / "Assets/Plugins" / name, plugins / name)
    il_support = next((root / "Library/PackageCache").glob("**/Unity.Collections.LowLevel.ILSupport.dll"), None)
    if il_support is None:
        il_support = next((root / "Packages").glob("**/Unity.Collections.LowLevel.ILSupport.dll"))
    shutil.copy2(il_support, plugins / il_support.name)

    # The actual core assemblies contain Burst direct-call initializers. Their matching
    # native compiler runtime is required even when test simulation jobs run managed.
    burst_source = root / "Packages/com.unity.burst/.Runtime"
    burst_target = destination / "Packages/com.unity.burst/.Runtime"
    for source in burst_source.rglob("*"):
        if not source.is_file():
            continue
        target = burst_target / source.relative_to(burst_source)
        target.parent.mkdir(parents=True, exist_ok=True)
        if not target.exists() or (target.stat().st_size, target.stat().st_mtime_ns) != (source.stat().st_size, source.stat().st_mtime_ns):
            shutil.copy2(source, target)

    for relative in (
        "Assets/Plugins/x86_64/box3d.dll", "Assets/Plugins/x86_64/box3d.dll.meta",
        "Assets/Plugins/x86_64/HavokNative.dll", "Assets/Plugins/x86_64/HavokNative.dll.meta",
        "Assets/Plugins/x86_64/Havok.Vdb.dll", "Assets/Plugins/x86_64/Havok.Vdb.dll.meta",
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.cs",
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.cs.meta",
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.Performance.cs",
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.Performance.cs.meta",
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.SamplingProbe.cs",
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.SamplingProbe.cs.meta",
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.MetadataContracts.cs",
        "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.MetadataContracts.cs.meta",
        "ProjectSettings/ProjectVersion.txt",
        "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilWorld.cs",
        "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilStepSystem.cs",
        "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilNative.cs",
    ):
        target = destination / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        source = args.native_dll if args.native_dll and relative == "Assets/Plugins/x86_64/box3d.dll" else root / relative
        shutil.copy2(source, target)
    builtin = {"com.unity.modules." + name: "1.0.0" for name in
               ("jsonserialize", "unitywebrequest", "assetbundle", "unityanalytics")}
    (destination / "Packages/manifest.json").write_text(json.dumps({"dependencies": builtin}) + "\n", encoding="utf-8")

    # Compile the exact working-tree package, including existing adapter changes, into the private project.
    # No code or assemblies under the production Assets/Library directories are replaced.
    original = root / "Library/Bee/artifacts/1300b0aE.dag/Unity.Physics.rsp"
    lines = original.read_text(encoding="utf-8-sig").splitlines()
    drop = ("-out:", "-refout:", "-analyzer:", "-additionalfile:", "-generatedfilesout:",
            "-errorlog:", "-doc:")
    lines = [line for line in lines if line.strip() and not line.startswith(drop)]
    sources = {line.strip('"').replace('\\', '/').lower() for line in lines if line.endswith('.cs"') or line.endswith('.cs')}
    for path in (root / "Packages/com.unity.physics/Unity.Physics/Anvil").glob("*.cs"):
        relative = path.relative_to(root).as_posix()
        if relative.lower() not in sources:
            lines.append('"' + relative + '"')
    lines.append('-out:"' + str(plugins / "Unity.Physics.dll") + '"')
    response = root / "_codex_work/anvil-parity/compile/physics.rsp"
    response.parent.mkdir(parents=True, exist_ok=True)
    response.write_text("\n".join(lines) + "\n", encoding="utf-8")
    subprocess.run([str(runtime), str(compiler), "@" + str(response)], cwd=root, check=True)
    qa_options = []
    for line in (root / "Library/Bee/artifacts/1300b0aE.dag/Assembly-CSharp.rsp").read_text(encoding="utf-8-sig").splitlines():
        if not line.startswith(("-r:", "-define:", "-unsafe", "-nostdlib", "-langversion:", "-nowarn:")):
            continue
        if line.startswith("-r:") and Path(line[3:].strip('"')).name in ("Unity.Physics.dll", "Unity.Physics.ref.dll"):
            line = '-r:"' + str(plugins / "Unity.Physics.dll") + '"'
        qa_options.append(line)
    qa_response = response.with_name("qa.rsp")
    qa_response.write_text("\n".join(["-target:library", '-out:"' + str(response.parent / "AnvilParityPreflight.dll") + '"',
                                      *qa_options, '"Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.cs"',
                                      '"Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.Performance.cs"',
                                      '"Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.SamplingProbe.cs"',
                                      '"Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.MetadataContracts.cs"']) + "\n", encoding="utf-8")
    subprocess.run([str(runtime), str(compiler), "@" + str(qa_response)], cwd=root, check=True)
    manifest = {"project": str(destination), "physicsAssemblySha256": hashlib.sha256((plugins / "Unity.Physics.dll").read_bytes()).hexdigest()}
    (destination / "prepared.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2))


if __name__ == "__main__":
    main()
