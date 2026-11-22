"""Validate fixed-step real-save reports and compare independent Anvil runs."""

import argparse
import json
import math
import re
from collections import defaultdict
from pathlib import Path, PureWindowsPath


BACKENDS = ("AnvilPhysics", "UnityPhysics", "HavokPhysics")
TIMING = {"physicsP50Ms": 0.50, "physicsP95Ms": 0.95, "physicsP99Ms": 0.99, "physicsMaxMs": 1.0}
STATE_LIMITS = {
    "centroidX": 0.002, "centroidY": 0.002, "meanSpeed": 0.02, "maxSpeed": 0.05,
    "meanAngularSpeed": 0.02, "maxAngularSpeed": 0.10,
    "rmsFixedAnchorError": 0.0001, "maxFixedAnchorError": 0.0005,
}
TOPOLOGY = ("jointsBefore", "jointsAfter", "componentsBefore", "componentsAfter", "fixedJointCount")


def require(condition, message):
    if not condition:
        raise ValueError(message)


def spec(report):
    return dict(entry.split("=", 1) for entry in report["spec"].split(";") if entry and not entry.startswith("label="))


def fingerprint(report, cross_backend=False):
    settings = spec(report)
    if cross_backend:
        settings.pop("backend")
    return settings, tuple(report[field] for field in (
        "inputSha256", "unity", "startupSteps", "parts", "joints", "jointEntities", "components",
        "solverIterations", "workerCount", "fixedTimeStep", "gravityX", "gravityY",
    )) + (report.get("loadedJointEntities"), report.get("host", "Editor"))


def validate_phase(phase, backend, length):
    steps = phase["physicsSteps"]
    require(phase["completedPhysicsSteps"] == len(steps) == length, "wrong physics step count")
    delta = int(backend == "AnvilPhysics")
    require(phase["anvilStepDelta"] == delta * length, "backend did not complete expected steps")
    require(all(step["anvilStepDelta"] == delta for step in steps), "inconsistent per-step backend")
    times = sorted(step["totalMs"] for step in steps)
    require(all(math.isfinite(time) and time > 0 for time in times), "invalid physics samples")
    for field, quantile in TIMING.items():
        require(math.isclose(phase[field], times[math.ceil(length * quantile) - 1], abs_tol=1e-9), field)
    require(phase["nonFiniteParts"] == 0, "non-finite part state")
    require(all(math.isfinite(phase[field]) for field in STATE_LIMITS), "invalid physical metrics")
    require(re.fullmatch(r"[0-9a-f]{64}", phase["stateSha256"]), "missing state digest")


def compare_states(first, second, identity):
    require(fingerprint(first) == fingerprint(second), f"{identity}: inputs differ")
    for earlier, later in zip(first["phases"], second["phases"]):
        require(all(earlier[key] == later[key] for key in TOPOLOGY), f"{identity}: topology changed in {earlier['label']}")
        for key, tolerance in STATE_LIMITS.items():
            delta = abs(earlier[key] - later[key])
            require(delta <= tolerance, f"{identity}: {earlier['label']} {key}: {delta:g} > {tolerance:g}")


def load_matrix(directory, label, expected_backends, run_ids):
    matrix = defaultdict(list)
    for path in sorted(directory.glob(f"spinperf-{label}-[0-9]*.json")):
        try:
            report = json.loads(path.read_text(encoding="utf-8-sig"))
            require(report["schema"] == 2 and not report["error"], report.get("error") or "wrong schema")
            backend = report["actualBackend"]
            if report["requestedBackend"] not in expected_backends:
                continue
            require(backend == report["requestedBackend"], "wrong actual backend")
            require(report["runId"] and report["runId"] not in run_ids, "duplicate run ID")
            run_ids.add(report["runId"])
            require(re.fullmatch(r"[0-9a-f]{32}", report["physicsAssemblyId"]), "missing managed assembly ID")
            require(re.fullmatch(r"[0-9a-f]{64}", report["inputSha256"]), "missing input digest")
            if backend != "UnityPhysics":
                require(re.fullmatch(r"[0-9a-f]{64}", report["nativeSha256"]), "missing native DLL digest")
            require(report["startupSteps"] == 4 and report["parts"] > 0 and report["jointEntities"] > 0, "invalid load")
            phases = report["phases"]
            settings = spec(report)
            require(len(phases) == 2 and phases[0]["label"] == "warmup" and phases[1]["label"].startswith("single"), "wrong phases")
            for phase, field in zip(phases, ("warmupsteps", "steps")):
                length = int(settings[field])
                require(length > 0, "missing fixed-step window")
                validate_phase(phase, backend, length)
            matrix[PureWindowsPath(report["inputPath"]).name, backend].append(report)
        except (TypeError, KeyError, ValueError) as error:
            raise ValueError(f"{path.name}: {error}") from error
    require(matrix, f"no reports for {label}")
    require(len({report["physicsAssemblyId"] for runs in matrix.values() for report in runs}) == 1, f"{label}: mixed managed builds")
    for save, _ in matrix:
        require(all((save, backend) in matrix for backend in expected_backends), f"{label}/{save}: missing backend")
        reference = fingerprint(matrix[save, expected_backends[0]][0], cross_backend=True)
        for backend in expected_backends:
            require(fingerprint(matrix[save, backend][0], cross_backend=True) == reference, f"{label}/{save}: backend inputs differ")
    for key, runs in matrix.items():
        require(len(runs) == 2, f"{label}/{key}: expected two independent runs")
        require(len({(run.get("nativeBridgeVersion", 0), run.get("nativeSha256")) for run in runs}) == 1, f"{label}/{key}: mixed native builds")
        compare_states(*runs, f"{label}/{key} A/A")
    return matrix


def p95(runs):
    return [report["phases"][1]["physicsP95Ms"] for report in runs]


def check_timing_scope(matrix, label, require_comparable_topology, comparing_candidate):
    topologies = defaultdict(set)
    for (save, _), runs in matrix.items():
        report = runs[0]
        topologies[save].add(tuple(tuple(phase[key] for key in TOPOLOGY) for phase in report["phases"]))
        if int(spec(report).get("tracesteps", 0)) > 0:
            message = f"{label}/{save}: traced steps are diagnostic only"
            require(not (comparing_candidate or require_comparable_topology), message)
            print(f"WARNING: {message}")
    for save, signatures in topologies.items():
        if len(signatures) > 1:
            message = f"{label}/{save}: topology diverges between backends; do not rank timings"
            require(not require_comparable_topology, message)
            print(f"WARNING: {message}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", required=True)
    parser.add_argument("--candidate")
    parser.add_argument("--directory", type=Path, default=Path("Logs"))
    parser.add_argument("--backends", nargs="+", choices=BACKENDS, default=BACKENDS)
    parser.add_argument("--minimum-bridge", type=int, default=1)
    parser.add_argument("--require-comparable-topology", action="store_true",
                        help="fail if the measured joint or component counts diverge between backends")
    args = parser.parse_args()
    try:
        run_ids = set()
        baseline = load_matrix(args.directory, args.baseline, args.backends, run_ids)
        candidate = load_matrix(args.directory, args.candidate, args.backends, run_ids) if args.candidate else None
        require(candidate is None or baseline.keys() == candidate.keys(), "different save/backend coverage")
        check_timing_scope(baseline, args.baseline, args.require_comparable_topology, candidate is not None)
        if candidate:
            check_timing_scope(candidate, args.candidate, args.require_comparable_topology, True)
        improvements = []
        for (save, backend), before in baseline.items():
            timings = p95(before)
            text = f"{save} / {backend}: {timings[0]:.3f}/{timings[1]:.3f} ms"
            if candidate:
                after = candidate[save, backend]
                for baseline_run in before:
                    for candidate_run in after:
                        compare_states(baseline_run, candidate_run, f"{save}/{backend} before/after")
                new_timings = p95(after)
                text += f" -> {new_timings[0]:.3f}/{new_timings[1]:.3f} ms"
                if backend == "AnvilPhysics":
                    require(after[0].get("nativeBridgeVersion", 0) >= args.minimum_bridge, "candidate native bridge did not load")
                    floor = max(abs(timings[0] - timings[1]), abs(new_timings[0] - new_timings[1]))
                    gain = min(timings) - max(new_timings)
                    improvements.append(gain > floor)
                    text += f" (conservative gain {gain:.3f}; A/A {floor:.3f} ms)"
            print(text)
        require(all(improvements), "valid reports, but at least one Anvil p95 gain did not exceed A/A drift")
        print("PASS: report coverage, backend identity, same-backend topology and state tolerances" + ("; Anvil p95 gain exceeds A/A drift" if improvements else ""))
        return 0
    except ValueError as error:
        print(f"FAIL: {error}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
