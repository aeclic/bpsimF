"""Invoke real mod validators without the unsafe reflected DOTS/render environment.

Command: python Tools/RC2Levels/verify_managed.py --unity D:/.../Editor/Unity.exe
PASS: actual DLL parses 17 original documents, validates 433 meshes, rejects 7 bad inputs and records an 8th failed-callback control.
Boundary: pure managed document/mesh admission only; not a Unity scene/render test.
Invalidation: Unity SDK, host/mod DLLs, catalog or source document changes.
"""
import argparse
import pathlib
import subprocess
import xml.etree.ElementTree as ET


def main():
    parser=argparse.ArgumentParser();parser.add_argument('--unity',required=True);args=parser.parse_args()
    project=pathlib.Path(__file__).resolve().parents[2];work=project/'_codex_work/rc2-level-mod/build';work.mkdir(parents=True,exist_ok=True)
    editor=pathlib.Path(args.unity).resolve().parent;runtime=editor/'Data/MonoBleedingEdge';mono=runtime/'bin/mono.exe';compiler=runtime/'lib/mono/4.5/mcs.exe'
    exe=work/'validate-managed.exe';newtonsoft=project/'Assets/Plugins/Newtonsoft.Json.dll'
    retained=work/'mod-compile.rsp'
    references=[line for line in retained.read_text(encoding='utf-8-sig').splitlines() if line.startswith('-r:')]
    quote=lambda p:'"'+str(p.resolve()).replace('\\','/')+'"'
    response=work/'validate-managed.rsp'
    response.write_text('\n'.join(['-target:exe','-langversion:latest','-nostdlib+','-noconfig','-out:'+quote(exe)]+references+[quote(project/'Tools/RC2Levels/validate_managed.cs')])+'\n',encoding='utf-8')
    subprocess.run([str(mono),str(compiler),'@'+str(response)],check=True)
    # Resolve the same Newtonsoft identity used by the frozen SDK/mod assemblies.
    dll=work/'Newtonsoft.Json.dll';dll.write_bytes(newtonsoft.read_bytes())
    directories=[project/'Library/ScriptAssemblies',project/'Assets/Plugins',project/'Library/PackageCache/com.unity.collections@2.1.0-pre.11/Unity.Collections.LowLevel.ILSupport',
                 editor/'Data/PlaybackEngines/WindowsStandaloneSupport/Variations/mono/Managed',editor/'Data/Managed/UnityEngine',editor/'Data/Managed']
    assembly=ET.parse(project/'Mods/LevelEditor/Mod.xml').find('./Assemblies/Assembly').attrib['path']
    result=subprocess.run([str(mono),str(exe),str(project/'_codex_work/rc2-level-mod/original-pack'),str(project/'Mods/LevelEditor'/assembly),str(work/'managed-verification.json')]+[str(p) for p in directories],cwd=project)
    raise SystemExit(result.returncode)


if __name__=='__main__':main()
