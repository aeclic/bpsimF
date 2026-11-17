"""Verify SAI styling leaves the original repository UI layout intact.

Run: python Tools/BPSim/verify_original_ui_layout.py
PASS: all original numeric constants match Git HEAD, prefab bytes are unchanged,
and no replacement desktop/card graphics are created. Writes Temp/UiThemeQA.
Covers source geometry/element identity contracts, not runtime/pixel equivalence.
Invalid when HEAD intentionally changes the reference layout; review that baseline.
"""
import hashlib
import json
from pathlib import Path
import re
import subprocess


ROOT = Path(__file__).resolve().parents[2]
UI = Path("Assets/Scripts/Assembly-CSharp/Bpsim/UI")
CONSTANTS = {
    "BuildToolbar.cs": ("ButtonSize", "ButtonStep", "GroupGap", "FileStartX", "IconSize", "HintOffsetY"),
    "BuildHud.cs": ("PlaySize", "PlayX", "PlayY", "PlayIconSize", "SliderWidth", "SliderHeight",
                    "SliderTrackLength", "SliderKnobSize", "SliderX"),
    "LayerPanelUI.cs": ("StripHeight", "SquareSize", "SquareSpacing", "CornerRadius", "StripPadding", "SmallSize"),
}


def git(*args):
    return subprocess.check_output(["git", *args], cwd=ROOT)


def main():
    checks = []
    fingerprints = {}

    def check(label, passed):
        checks.append({"name": label, "passed": bool(passed)})
        print(("PASS " if passed else "FAIL ") + label)

    for name, constants in CONSTANTS.items():
        path = UI / name
        current_bytes = (ROOT / path).read_bytes()
        current = current_bytes.decode("utf-8-sig")
        original = git("show", "HEAD:" + path.as_posix()).decode("utf-8-sig")
        fingerprints[path.as_posix()] = hashlib.sha256(current_bytes).hexdigest()
        for constant in constants:
            regex = re.compile(r"private const (?:float|int) " + constant + r" = ([^;]+);")
            old, new = regex.search(original), regex.search(current)
            check(name + ":" + constant, old and new and old[1] == new[1])

    prefab = "Assets/GameObject/UserInterface.prefab"
    check("original prefab unchanged", git("hash-object", prefab).strip() == git("rev-parse", "HEAD:" + prefab).strip())
    check("no replacement desktop", not (ROOT / UI / "UiBuildDesktop.cs").exists())
    card = (ROOT / UI / "UiThemeToggleCard.cs").read_text(encoding="utf-8-sig")
    check("card creates no replacement graphics", "new GameObject" not in card and "AddComponent<Image>" not in card)
    check("card does not replace targetGraphic", not re.search(r"\.targetGraphic\s*=", card))
    check("card does not replace selection graphic", not re.search(r"\.graphic\s*=", card))
    check("card does not reposition any UI", "RectTransform" not in card and "anchoredPosition" not in card)
    selector = (ROOT / UI / "PartSelector.cs").read_text(encoding="utf-8-sig")
    check("original part icon/label layout retained", "StylePartItem" not in selector)
    tabs = (ROOT / UI / "SceneTabView.cs").read_text(encoding="utf-8-sig")
    check("original scene tab geometry retained", "sizeDelta" not in tabs and "offsetMin" not in tabs)
    layer = (ROOT / UI / "LayerPanelUI.cs").read_text(encoding="utf-8-sig")
    check("original horizontal layer drag", "eventData.position.x - m_startPointer.x" in layer)
    check("original layer numeric labels", "CreateEntry(layer, layer.ToString(), x)" in layer)
    report = {"reference": git("rev-parse", "HEAD").decode().strip(),
              "passed": sum(c["passed"] for c in checks), "failed": sum(not c["passed"] for c in checks),
              "checks": checks, "fingerprints": fingerprints}
    out = ROOT / "Temp/UiThemeQA/sai-original-source.json"
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(f"RESULT passed={report['passed']} failed={report['failed']}")
    return bool(report["failed"])


if __name__ == "__main__":
    raise SystemExit(main())
