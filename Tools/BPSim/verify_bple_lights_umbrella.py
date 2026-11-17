"""Read-only BPLE oracle for part22/44/45. No Unity project or save writes.

Contract: original settings + serialized prefab fields -> deterministic JSON.
Replay: --oracle <BPLE root> --output <D: scratch JSON>; run twice independently.
Floor: exact equality for data/IL/collision outputs, 1e-4 for runtime geometry.
Boundary: prefab/compiled-method contracts do not prove rendered pixel identity.
PyYAML is only used to parse Unity YAML; native collision runs use Unity's bundled .NET.
"""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path

import yaml


def unity_objects(path):
    text = path.read_text(encoding="utf-8-sig")
    result = {}
    for match in re.finditer(r"^--- !u!(\d+) &(-?\d+).*\n([\s\S]*?)(?=^--- !u!|\Z)", text, re.M):
        obj = yaml.safe_load(match.group(3))
        key, value = next(iter(obj.items()))
        result[int(match.group(2))] = (key, value)
    return result


def dump_contract(oracle):
    scripts = oracle / "Assets/Scripts/Assembly-CSharp"
    guids = {}
    for meta in scripts.glob("*.cs.meta"):
        match = re.search(r"^guid: (\w+)", meta.read_text(encoding="utf-8-sig"), re.M)
        if match:
            guids[match.group(1)] = meta.name[:-8]
    settings = {}
    # INSettings.Initialize loads the Exp tables even for the normal B preset.
    for name in ["INDeclarationSettingsExp", "INSettingsBExp"]:
        for item in json.loads((oracle / f"Assets/TextAsset/{name}.json").read_text(encoding="utf-8-sig"))["items"]:
            settings[item["name"]] = item["value"]
    contract = {"lights": [], "umbrellas": [], "settings": {name: value for name, value in settings.items()
                if any(s in name for s in ("Light", "Umbrella"))}}
    for kind, count in [("PointLight", 7), ("SpotLight", 4), ("Umbrella", 7)]:
        for index in range(count):
            path = oracle / f"Assets/GameObject/Part_{kind}_{index + 1:02d}_SET.prefab"
            objs = unity_objects(path)
            components = [(guids.get(value.get("m_Script", {}).get("guid")), value)
                          for key, value in objs.values() if key == "MonoBehaviour"]
            part = next(value for name, value in components if "m_mass" in value)
            source = next((value for name, value in components if name == "PointLightSource"), {})
            nodes = {value["m_Name"]: identifier for identifier, (key, value) in objs.items() if key == "GameObject"}
            transforms = {}
            for key, value in objs.values():
                if key != "Transform":
                    continue
                gameobject = objs[value["m_GameObject"]["fileID"]][1]
                transforms[gameobject["m_Name"]] = {
                    "position": value["m_LocalPosition"], "rotation": value["m_LocalRotation"],
                    "scale": value["m_LocalScale"]}
            profile = {"kind": kind, "index": index, "tier": part["m_partTier"], "mass": part["m_mass"],
                       "sourceSize": source.get("size", 0), "sourceType": source.get("lightType", 0),
                       "beamAngle": source.get("beamAngle", 0), "beamCut": source.get("beamCut", 0),
                       "borderWidth": source.get("borderWidth", 0), "flicker": part.get("flicker", 0),
                       "mainSinSpeed": part.get("mainSinSpeed", 0), "subSinSpeed": part.get("subSinSpeed", 0),
                       "force": part.get("m_force", 0)}
            if kind == "Umbrella":
                profile["initialVisualization"] = transforms["UmbrellaVisualization"]
                profile["initialOpenSprite"] = transforms["OpenSprite"]
                profile["colliders"] = [{"type": key, **{k: v for k, v in value.items() if k in
                                         ("m_Center", "m_Size", "m_Radius", "m_Enabled", "m_IsTrigger")}}
                                        for key, value in objs.values() if key in ("BoxCollider", "SphereCollider")]
                contract["umbrellas"].append(profile)
            else:
                profile["entityType"] = (2, 2, 3, 3, 4)[index] if kind == "PointLight" and index < 5 else (
                    (0, 0, 1, 1)[index] if kind == "SpotLight" else -1)
                profile["length"] = settings["CircleLightRadius"] if kind == "PointLight" else settings[
                    "LinearLightLength2" if index % 2 == 0 else "LinearLightLength1"]
                profile["halfWidth"] = settings["CircleLightWidth"] * .5 if kind == "PointLight" else settings[
                    "LinearLightWidth1" if index < 2 else "LinearLightWidth2"] * .5
                profile["angle"] = (360 if index == 4 else settings["CircleLightAngle2" if index % 2 == 0
                                                                 else "CircleLightAngle1"]) if kind == "PointLight" else 0
                for name in ["turnOnCurve", "turnOffCurve"]:
                    profile[name] = source.get(name, {}).get("m_Curve", [])
                contract["lights"].append(profile)
    contract["fingerprints"] = {name: hashlib.sha256((scripts / f"{name}.cs").read_bytes()).hexdigest()
                                for name in ("Umbrella", "PointLight", "SpotLight", "EntityLight", "EntityLightManager")}
    return contract


def native_cases(oracle, unity, output, compare=None):
    scratch = output.parent
    scratch.mkdir(parents=True, exist_ok=True)
    source = Path(__file__).with_name("BpleLightOracle.cs")
    runtime = next((unity / "Data/NetCoreRuntime/shared/Microsoft.NETCore.App").iterdir())
    dotnet = unity / "Data/NetCoreRuntime/dotnet.exe"
    compiler = unity / "Data/DotNetSdkRoslyn/csc.dll"
    assembly = scratch / "BpleLightOracle.dll"
    refs = [f'-r:"{path}"' for path in runtime.glob("*.dll") if
            (path.name.startswith("System.") and ".Native." not in path.name)
            or path.name in ("mscorlib.dll", "netstandard.dll", "Microsoft.CSharp.dll")]
    response = scratch / "oracle-compile.rsp"
    response.write_text("\n".join(["-nologo", "-target:exe", "-langversion:latest", f'-out:"{assembly}"',
                                  *refs, f'"{source.resolve()}"']), encoding="utf-8")
    subprocess.run([str(dotnet), str(compiler), "@" + str(response)], check=True)
    assembly.with_suffix(".runtimeconfig.json").write_text(json.dumps({"runtimeOptions": {
        "tfm": "net6.0", "framework": {"name": "Microsoft.NETCore.App", "version": runtime.name}}}), encoding="utf-8")
    command = [str(dotnet), str(assembly), str(oracle), str(unity), str(output)]
    if compare:
        command.append(str(compare.resolve()))
    subprocess.run(command, check=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--oracle", type=Path, required=True)
    parser.add_argument("--output", type=Path)
    parser.add_argument("--check-profiles", type=Path)
    parser.add_argument("--native-cases", type=Path)
    parser.add_argument("--compare-assembly", type=Path)
    parser.add_argument("--unity", type=Path, default=Path("D:/folder/2022.2.23f1/Editor"))
    args = parser.parse_args()
    contract = dump_contract(args.oracle.resolve())
    text = json.dumps(contract, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(text, encoding="utf-8")
    if args.check_profiles:
        current = json.loads(args.check_profiles.read_text(encoding="utf-8-sig"))
        assert current == contract, "Production profiles differ from read-only BPLE serialized data"
        project = args.check_profiles.resolve().parents[3]
        seen = set()
        for path in (project / "Assets/TextAsset").glob("*TemplateGroup.json"):
            for root in json.loads(path.read_text(encoding="utf-8-sig")):
                for component in root.get("components", []):
                    if component.get("type") == "Part":
                        key = (component.get("partType"), component.get("partIndex"))
                        assert key not in seen, f"Duplicate template key {key}: {path}"
                        seen.add(key)
        print(f"PASS template keys unique: {len(seen)}")
    if args.native_cases:
        native_cases(args.oracle.resolve(), args.unity.resolve(), args.native_cases.resolve(), args.compare_assembly)
    print(f"PASS BPLE contracts: lights={len(contract['lights'])} umbrellas={len(contract['umbrellas'])} "
          f"lightSystem={contract['settings']['LightSystem']} specialUmbrellas={contract['settings']['SpecialUmbrellas']}")
    print("DATA SHA256=" + hashlib.sha256(text.encode("utf-8")).hexdigest())
    for profile in contract["lights"]:
        print(f"  {profile['kind']}#{profile['index']}: type={profile['entityType']} "
              f"length={profile['length']} halfWidth={profile['halfWidth']} angle={profile['angle']} "
              f"illumination={profile['sourceSize']} mass={profile['mass']}")


if __name__ == "__main__":
    main()
