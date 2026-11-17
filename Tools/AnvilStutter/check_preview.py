"""Validate independent BPLE CPU-preview reports and semantic/GC equivalence.

Command: python -X utf8 Tools/AnvilStutter/check_preview.py --before A.json B.json [--after C.json D.json]
PASS: all three workloads/raw samples/restoration checks; identical semantic geometry/pose/color hashes;
      after caching, each workload's allocation falls >=80% and CPU p95 does not regress beyond A/A noise.
Scope: synchronous build-preview CPU calls, not GPU/night lighting or physical light contacts.
Invalidated by probe/input changes, incomplete samples, failed scene restoration, or source A/A drift.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path


SCENARIOS = {"empty", "spot-256", "point-256"}


def load(path):
    r = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    assert not r["errors"] and all(c["pass"] for c in r["checks"]), f"Preview errors/checks: {path}"
    assert len(r["checks"]) == 15, f"Restoration/membership/pose coverage changed: {path}"
    cases = {c["scenario"]: c for c in r["cases"]}
    assert len(cases) == len(r["cases"]) and set(cases) == SCENARIOS
    for scenario, c in cases.items():
        assert c["sources"] == c["visible"] == (0 if scenario == "empty" else 256)
        assert c["warmup"] == 16 and c["sampleCount"] == 128 and len(c["samples"]) == 128
        assert len(c["stateSha256"]) == 64
        values = sorted(s["milliseconds"] for s in c["samples"])
        assert all(math.isfinite(v) and v > 0 for v in values)
        assert all(s["allocatedBytes"] >= 0 for s in c["samples"])
        for field, index in [("p50Ms", 63), ("p95Ms", 121), ("p99Ms", 126), ("maxMs", 127)]:
            assert abs(c[field] - values[index]) < 1e-5
        allocation = sum(s["allocatedBytes"] for s in c["samples"]) / 128
        assert abs(allocation - c["meanAllocatedBytes"]) < 1e-5
    r["index"] = cases
    return r


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--before", nargs=2, required=True)
    p.add_argument("--after", nargs=2)
    args = p.parse_args()
    before = [load(path) for path in args.before]
    after = [load(path) for path in args.after] if args.after else []
    reports = before + after
    root = Path(__file__).resolve().parents[2]
    expected = hashlib.sha256((root / "Assets/Scripts/Assembly-CSharp/Bpsim/QA/BpleLightStutterQA.PreviewProbe.cs").read_bytes()).hexdigest()
    assert all(r["probeSha256"] == expected for r in reports), "Probe source changed"
    assert len({r["runId"] for r in reports}) == len(reports), "Reports are not independent"
    for pair in (before, after):
        if pair: assert pair[0]["renderingSha256"] == pair[1]["renderingSha256"], "A/A rendering sources differ"
    for scenario in sorted(SCENARIOS):
        assert len({r["index"][scenario]["stateSha256"] for r in reports}) == 1, f"Geometry/pose/color changed: {scenario}"
        b = [r["index"][scenario] for r in before]
        floor = abs(b[0]["p95Ms"] - b[1]["p95Ms"])
        print(scenario, "before p95(ms)", [round(c["p95Ms"], 4) for c in b], "allocation(bytes)", [c["meanAllocatedBytes"] for c in b], end="")
        if after:
            a = [r["index"][scenario] for r in after]
            floor = max(floor, abs(a[0]["p95Ms"] - a[1]["p95Ms"]))
            assert min(c["meanAllocatedBytes"] for c in b) > 0, "Allocation positive control missing/unsupported; zero-to-zero cannot prove GC improvement"
            assert max(c["meanAllocatedBytes"] for c in a) <= min(c["meanAllocatedBytes"] for c in b) * .2, f"Allocation not reduced >=80%: {scenario}"
            assert max(c["p95Ms"] for c in a) <= max(c["p95Ms"] for c in b) * 1.1 + floor, f"CPU regression: {scenario}"
            print(" ->", [round(c["p95Ms"], 4) for c in a], "allocation", [c["meanAllocatedBytes"] for c in a], "AA floor(ms)", round(floor,4), end="")
        print()
    print("PASS: independent preview geometry/pose/color/restoration" + (" and allocation/CPU gates" if after else " baseline"))


if __name__ == '__main__':
    main()
