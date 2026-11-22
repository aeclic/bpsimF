"""Validate coverage, numeric contracts and independent A/A runs (nonzero exit on failure)."""
import argparse
import hashlib
import json
import math
from pathlib import Path


BACKENDS = {"UnityPhysics", "HavokPhysics", "AnvilPhysics"}
CORE_CASES = {
    "sparse-upload", "rotated-spin", "offset-com", "gravity", "mass-change",
    "welded-chain", "body-replacement", "threshold-linear-below", "threshold-linear-above",
    "joint-mapping",
    "threshold-diagonal-below", "threshold-zero-unloaded", "threshold-zero-loaded",
    "threshold-angular-above", "threshold-angular-below",
    "small-impulses", "scaled-compound", "scaled-joint", "body-identity", "moved-static",
}
EXTENDED_CASES = {
    "damping-0.3", "damping-4", "damping-60", "body-type-change", "partial-infinite-inertia",
    "high-speed", "nested-compound", "convex-blob", "cylinder", "static-mesh", "closest-compound-hit",
    "mass-ratio-100", "mass-ratio-100-high-signal", "off-centre-chain", "no-collision-response", "assembly-drop",
    *(f"signed-hinge-{sign}-{edge}" for sign in ("negative", "positive") for edge in ("lower", "upper", "wrap")),
    *("restitution-" + name for name in ("GeometricMean", "Minimum", "Maximum", "ArithmeticMean")),
    *("threshold-grid-" + side + "-dt" + dt + "-sub" + str(sub)
      for side in ("below", "above") for dt in ("0.01", "0.02", "0.04") for sub in (1, 2, 4)),
}
NATIVE_CASES = {"sleep-wake", "swept-dynamic-pair", "swept-static-contact", "slow-weld-path", "collision-tags", "dynamic-teleport",
                *(f"contact-{kind}-sub{sub}" for kind in ("sum", "box") for sub in (1, 2, 4))}
REFERENCE_LIMITS = {
    ("HavokPhysics", "small-impulses", "accumulated-velocity-error"),
    ("HavokPhysics", "mass-ratio-100", "momentum-relative-error"),
    ("HavokPhysics", "mass-ratio-100", "shared-speed-error"),
    ("HavokPhysics", "restitution-Maximum", "momentum-relative-error"),
}
FINGERPRINT_FILES = (
    "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilWorld.cs",
    "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilStepSystem.cs",
    "Packages/com.unity.physics/Unity.Physics/Anvil/AnvilNative.cs",
    "Assets/Plugins/x86_64/box3d.dll", "Assets/Plugins/Havok.Physics.dll",
    "Assets/Plugins/x86_64/HavokNative.dll",
    "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.cs",
)


def load(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def index(report):
    result = {}
    for item in report["measurements"]:
        key = (item["backend"], item["scenario"], item["metric"])
        if key in result:
            raise ValueError(f"Duplicate measurement: {key}")
        result[key] = item
    return result


def verify(report, require_pass=True, extended=True, baseline=False):
    issues = []
    data = index(report)
    for backend in BACKENDS:
        actual = {key[1] for key in data if key[0] == backend}
        required = CORE_CASES | EXTENDED_CASES if extended else CORE_CASES - {"joint-mapping"}
        if extended and backend == "AnvilPhysics":
            required |= NATIVE_CASES
        if not required <= actual:
            issues.append(f"{backend}: missing scenarios {sorted(required - actual)}")
        if extended:
            count = sum(key[0] == backend for key in data)
            expected_count = 139 if backend == "AnvilPhysics" else 114
            if count != expected_count:
                issues.append(f"{backend}: expected {expected_count} distinct measurements, found {count}")
    for key, item in data.items():
        value, tolerance = item["value"], item["tolerance"]
        if not math.isfinite(value) or not math.isfinite(tolerance) or tolerance < 0:
            issues.append(f"{key}: nonfinite value or invalid tolerance")
        expected = math.isfinite(value) and value <= tolerance
        if item["pass"] != expected:
            issues.append(f"{key}: result flag disagrees with its numeric criterion")
        reference_only = item.get("referenceOnly", False)
        if reference_only and key not in REFERENCE_LIMITS:
            issues.append(f"{key}: unauthorized reference-only exemption")
        allowed_reference = reference_only or (baseline and key in REFERENCE_LIMITS)
        if not item["pass"] and not allowed_reference and (require_pass or key[0] != "AnvilPhysics"):
            issues.append(f"{key}: {value} {item['unit']} exceeds {tolerance}; {item.get('detail') or ''}")
    modern = "referenceDeviations" in report
    failures = sum(not item["pass"] and (not modern or not item.get("referenceOnly", False)) for item in data.values())
    if report["failures"] != failures or report["pass"] != (failures == 0):
        issues.append("Report summary is inconsistent with measurements")
    if not report.get("havokApi"):
        issues.append("Havok factory was not verified")
    if modern and report["referenceDeviations"] != sum(not item["pass"] and item.get("referenceOnly", False) for item in data.values()):
        issues.append("Reference-deviation summary is inconsistent")
    return data, issues


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("first")
    parser.add_argument("second")
    parser.add_argument("--baseline")
    parser.add_argument("--native-dll", type=Path)
    args = parser.parse_args()
    a, b = load(args.first), load(args.second)
    first, issues = verify(a)
    second, errors = verify(b)
    issues.extend(errors)
    if not a.get("runId") or a["runId"] == b.get("runId") or a["label"] == b["label"]:
        issues.append("A/A floor requires independently collected runs and different labels")
    if a["fingerprint"] != b["fingerprint"]:
        issues.append("A/A reports belong to different builds")
    root = Path(__file__).resolve().parents[2]
    digest = hashlib.sha256()
    for relative in FINGERPRINT_FILES:
        source = args.native_dll if relative == "Assets/Plugins/x86_64/box3d.dll" and args.native_dll else root / relative
        digest.update(source.read_bytes())
    if a["fingerprint"] != digest.hexdigest():
        issues.append("Reports are stale: fingerprint does not match current source and native DLLs")
    if first.keys() != second.keys():
        issues.append("A/A runs cover different measurements")
    floors = {}
    for key in first.keys() & second.keys():
        item = first[key]
        delta = abs(item["value"] - second[key]["value"])
        floors[item["unit"]] = max(floors.get(item["unit"], 0), delta)
        if delta > max(1e-5, item["tolerance"] * 0.1):
            issues.append(f"{key}: unstable A/A floor {delta} {item['unit']}")
    if args.baseline:
        baseline = load(args.baseline)
        _, errors = verify(baseline, require_pass=False, extended=False, baseline=True)
        issues.extend(errors)
        if not any(not m["pass"] and m["backend"] == "AnvilPhysics" for m in baseline["measurements"]):
            issues.append("Baseline did not reproduce an Anvil failure")
    print(json.dumps({"pass": not issues, "measurements": len(first),
                      "required_measurements": sum(not m.get("referenceOnly", False) for m in first.values()),
                      "reference_deviations": a.get("referenceDeviations", 0),
                      "scenarios_per_backend": len({k[1] for k in first if k[0] == "AnvilPhysics"}),
                      "aa_floor_by_unit": floors, "fingerprint": a["fingerprint"], "issues": issues},
                     ensure_ascii=False, indent=2))
    return 1 if issues else 0


if __name__ == "__main__":
    raise SystemExit(main())
