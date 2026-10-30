# -*- coding: utf-8 -*-
"""Stage the debug console prefabs into bpsim-main.

The parent project already relinked and trimmed Assets/Resources/INDebugConsole.prefab;
that is the file the runtime loads via Resources.Load<GameObject>("INDebugConsole").
This script copies it verbatim (its serialised settings are the tuned ones) and then
rewrites the uGUI m_Script slots from com.unity.ugui source-guid form into the
precompiled-DLL form this project needs (Assets/Plugins/UnityEngine.UI.dll), because
bpsim-main ships uGUI as a DLL instead of the package.
"""
import os
import shutil
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
SRC = r"D:\rpgvx + magpie\BPRC 2026.3.2 proj\Assets\Resources\INDebugConsole.prefab"
DST = os.path.join(PROJECT, "Assets", "Resources", "INDebugConsole.prefab")
PLUGIN_PREFABS = os.path.join(PROJECT, "Assets", "Plugins", "IngameDebugConsole", "Prefabs")

sys.path.insert(0, HERE)
import relink_debugconsole  # noqa: E402


def main():
    shutil.copyfile(SRC, DST)
    print("staged " + DST)
    relink_debugconsole.main([
        DST,
        os.path.join(PLUGIN_PREFABS, "DebugLogItem.prefab"),
        os.path.join(PLUGIN_PREFABS, "CommandSuggestion.prefab"),
    ])


if __name__ == "__main__":
    main()
