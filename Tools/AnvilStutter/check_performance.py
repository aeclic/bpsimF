"""Validate fixed-input CPU benchmark reports before claiming a stutter improvement.

Command: python -X utf8 Tools/AnvilStutter/check_performance.py --before A.json B.json [--after C.json D.json]
PASS: full coverage/raw statistics, independent A/A builds, exact state/contact trajectories,
      unchanged quality/workloads/instrument/native binaries, and measured improvement beyond A/A noise.
Scope: private physics adapter CPU window; not player/GPU/frame pacing or gameplay lighting.
Invalidated by benchmark/workload/native changes, missing samples, or unreproducible trajectories.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path


BACKENDS = {"UnityPhysics", "HavokPhysics", "AnvilPhysics"}
SCENARIOS = {"free-flight-512-q8", "welded-1024-q4", "welded-1024-q8",
             "welded-unlimited-1024-q8", "contact-stack-512-q8"}
EXPECTED = {(backend, scenario) for backend in BACKENDS for scenario in SCENARIOS}
STATS = {"total": "totalMs", "sync": "syncMs", "step": "stepMs", "read": "readMs",
         "events": "eventsMs", "native": "nativeMs"}


def load(path):
    report = json.loads(Path(path).read_text(encoding="utf-8-sig"))
    assert report["schema"] == 1 and not report["errors"], f"Instrument errors: {path}"
    cases = {(case["backend"], case["scenario"]): case for case in report["cases"]}
    assert len(cases) == len(report["cases"]) and set(cases) == EXPECTED, f"Coverage mismatch: {path}"
    for (backend, scenario), case in cases.items():
        assert case["sampleCount"] == 256 and case["warmup"] == 64 and len(case["samples"]) == 256
        assert case["bodies"] == (512 if scenario.startswith(("free-flight", "contact-stack")) else 1024)
        assert case["joints"] == (0 if case["bodies"] == 512 else 992)
        assert case["iterations"] == (4 if scenario.endswith("q4") else 8)
        assert abs(case["timeStep"] - .02) < 1e-8 and not case["sleeping"]
        assert case["finiteThresholds"] == ("unlimited" not in scenario)
        assert len(case["trajectorySha256"]) == 64
        for sample in case["samples"]:
            for field in (*STATS.values(), "prepareMs"):
                assert math.isfinite(sample[field]) and sample[field] >= 0, f"Invalid timing: {path} {scenario}"
            assert sample["totalMs"] > 0 and sample["stepMs"] > 0 and sample["allocatedBytes"] >= 0
            assert abs(sample["totalMs"] - sum(sample[f] for f in ("prepareMs", "syncMs", "stepMs", "readMs", "eventsMs"))) < 1e-5
        for stat, field in STATS.items():
            values = sorted(sample[field] for sample in case["samples"])
            expected = {"mean": sum(values) / len(values), "p50": values[math.ceil(len(values) * .5) - 1],
                        "p95": values[math.ceil(len(values) * .95) - 1], "p99": values[math.ceil(len(values) * .99) - 1],
                        "max": values[-1], "over16ms": sum(v > 1000 / 60 for v in values),
                        "over33ms": sum(v > 1000 / 30 for v in values)}
            for key, value in expected.items():
                assert abs(case[stat][key] - value) <= max(1e-5, abs(value) * 1e-6), f"Statistics mismatch: {path} {scenario} {stat}.{key}"
        allocation = sum(sample["allocatedBytes"] for sample in case["samples"]) / len(case["samples"])
        assert abs(allocation - case["meanAllocatedBytes"]) < 1e-4
        if backend == "AnvilPhysics":
            assert case["native"]["mean"] > 0, "Native timer positive control failed"
            if scenario.startswith("contact-stack"):
                assert max(s["contacts"] for s in case["samples"]) > 0, "Contact workload positive control failed"
    report["index"] = cases
    return report


def compatible(reports):
    root = Path(__file__).resolve().parents[2]
    benchmark = root / "Assets/Scripts/Assembly-CSharp/Bpsim/QA/AnvilParityQA.Performance.cs"
    expected = hashlib.sha256(benchmark.read_bytes()).hexdigest()
    for report in reports:
        assert report["benchmarkSha256"] == expected, "Benchmark source changed"
        for field in ("unity", "nativeSha256", "havokSha256", "benchmarkSha256"):
            assert report[field] == reports[0][field], f"Environment changed: {field}"
    assert len({r["runId"] for r in reports}) == len(reports), "Reports are not independent runs"
    assert len({r["label"] for r in reports}) == len(reports), "Run labels must be independent"
    for pair in (reports[:2], reports[2:]):
        if pair:
            assert pair[0]["fingerprint"] == pair[1]["fingerprint"], "A/A source builds differ"
    for key in EXPECTED:
        states = [r["index"][key]["trajectorySha256"] for r in reports]
        assert len(set(states)) == 1, f"Physical trajectory changed: {key}"
        contact_counts = [[s["contacts"] for s in r["index"][key]["samples"]] for r in reports]
        assert all(c == contact_counts[0] for c in contact_counts), f"Contact trajectory changed: {key}"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--before", nargs=2, required=True)
    parser.add_argument("--after", nargs=2)
    args = parser.parse_args()
    before = [load(p) for p in args.before]
    after = [load(p) for p in args.after] if args.after else []
    compatible(before + after)
    improvements = []
    for key in sorted(EXPECTED):
        base = [r["index"][key] for r in before]
        floor = abs(base[0]["total"]["p95"] - base[1]["total"]["p95"])
        print(f"{key[0]:13} {key[1]:27} before p95={base[0]['total']['p95']:.3f}/{base[1]['total']['p95']:.3f} ms A/A={floor:.3f} ms", end="")
        if after:
            current = [r["index"][key] for r in after]
            floor = max(floor, abs(current[0]["total"]["p95"] - current[1]["total"]["p95"]))
            gain = min(c["total"]["p95"] for c in base) - max(c["total"]["p95"] for c in current)
            print(f" -> {current[0]['total']['p95']:.3f}/{current[1]['total']['p95']:.3f} ms conservative gain={gain:.3f} ms", end="")
            if key[0] == "AnvilPhysics":
                assert max(c["total"]["p95"] for c in current) <= max(c["total"]["p95"] for c in base) * 1.1 + floor, f"CPU regression: {key}"
                assert max(c["meanAllocatedBytes"] for c in current) <= max(c["meanAllocatedBytes"] for c in base), f"Allocation regression: {key}"
                if key[1] in ("welded-1024-q4", "welded-1024-q8"):
                    improvements.append(gain > floor and gain >= min(c["total"]["p95"] for c in base) * .1)
        print()
    if after:
        assert all(improvements) and len(improvements) == 2, "Both assembly quality levels must improve >10% and beyond A/A noise"
        print("PASS: unchanged exact physical/contact trajectories; measured assembly CPU improvement beyond A/A noise")
    else:
        print("PASS: valid baseline and independent A/A physical/contact trajectories; no improvement claimed yet")


if __name__ == "__main__":
    main()
