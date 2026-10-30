#!/bin/bash
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"

post() { curl -s -m 30 -X POST -H "Content-Type: application/json" -d "$1" "$API"; }
inv() { post "{\"method\":\"invoke_static\",\"params\":{\"type\":\"$1\",\"method\":\"$2\",\"args\":$3}}"; }
ok_or_retry() {
  local body=$1 tries=${2:-150}
  for i in $(seq 1 $tries); do
    R=$(post "$body")
    case "$R" in *'"ok":true'*) return 0;; esac
    sleep 2
  done
  echo "FAILED $body"; return 1
}
run_probe() {
  local spec=$1
  ok_or_retry "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.QA.SpinPerfProbe\",\"method\":\"Run\",\"args\":[\"$spec\"]}}" || return 1
  for j in $(seq 1 300); do
    sleep 3
    L=$(grep -a "^\[SpinPerf\]" "$LOG" | tail -1)
    case "$L" in *"=== done ==="*) return 0;; esac
  done
  return 1
}
report() { grep -a "^\[SpinPerf\]" "$LOG" | grep -a "single\|sweep\|spec:" | tail -3; }

echo "--- enter play"
ok_or_retry '{"method":"playmode","params":{"action":"enter"}}' || exit 1
sleep 20
inv Bpsim.Perf.BehaviorTimingProbe SetMaxDelta '[0.02]'; echo

echo "=== 20k baseline, one step/frame"
run_probe "file=perf/angel.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=1.0"
report

echo "=== 20k, nocollide=1 (no part-part collision)"
run_probe "file=perf/angel.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=1.0;nocollide=1"
report

echo "=== 20k, solver=1"
run_probe "file=perf/angel.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=1.0;solver=1"
report

echo "=== 20k, no joints created (createjoints=0)"
run_probe "file=perf/angel.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=1.0;createjoints=0"
report

echo "=== regression: 1648-part save, w=32 sweep, fix ON"
run_probe "file=perf/buhai.csv;dur=0.5;sweep=1;bisect=0;mode=rigid;gravity=0"
report

inv Bpsim.Perf.BehaviorTimingProbe SetMaxDelta '[0.33333334]'; echo
