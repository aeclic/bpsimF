#!/bin/bash
# mega 模式 steady-state 测量：把 Time.maximumDeltaTime 压到 fixedDeltaTime，
# 使「一帧 = 一固定步」，与 BPSIM_GAMEPLAY_PERF_FIX.md 里的 508.8ms/步 基线同口径。
# 用法： bash perf/step_mega_step.sh <spec> [tag]
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
SPEC=${1:?spec required}
TAG=${2:-probe}

post() { curl -s -m 30 -X POST -H "Content-Type: application/json" -d "$1" "$API"; }
ok_or_retry() {
  local body=$1 tries=${2:-150}
  for i in $(seq 1 $tries); do
    R=$(post "$body"); case "$R" in *'"ok":true'*) return 0;; esac; sleep 2
  done; return 1
}

if ! curl -s http://127.0.0.1:8791/ping | grep -q '"playing":true'; then
  echo "--- enter play"; ok_or_retry '{"method":"playmode","params":{"action":"enter"}}' || exit 1; sleep 25
fi

echo "--- set maximumDeltaTime=0.02（一帧一步）"
post '{"method":"invoke_static","params":{"type":"Bpsim.Perf.BehaviorTimingProbe","method":"SetMaxDelta","args":["0.02"]}}'; echo

MARK=$(wc -l < "$LOG")
echo "--- probe $TAG: $SPEC"
post "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.QA.SpinPerfProbe\",\"method\":\"Run\",\"args\":[\"$SPEC\"]}}"; echo
MARK=$(wc -l < "$LOG")

for j in $(seq 1 500); do
  sleep 3
  if tail -n +$((MARK+1)) "$LOG" | grep -aq "^\[SpinPerf\] === done ==="; then echo "done after $((j*3))s"; break; fi
done

echo "=== $TAG"
tail -n +$((MARK+1)) "$LOG" | grep -a "^\[SpinPerf\]" | grep -a "settings:\|start: Start\|simulation: parts\|mega: active\|idle-nj\|rigid-nj\|single w=\|frames=\|done ==="
echo "=== end $TAG"

echo "--- restore maximumDeltaTime=0.33333334"
post '{"method":"invoke_static","params":{"type":"Bpsim.Perf.BehaviorTimingProbe","method":"SetMaxDelta","args":["0.33333334"]}}'; echo
