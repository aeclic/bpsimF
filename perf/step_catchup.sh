#!/bin/bash
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"

post() { curl -s -m 25 -X POST -H "Content-Type: application/json" -d "$1" "$API"; }
inv() { post "{\"method\":\"invoke_static\",\"params\":{\"type\":\"$1\",\"method\":\"$2\",\"args\":$3}}"; }
ok_or_retry() {
  local body=$1 tries=${2:-120}
  for i in $(seq 1 $tries); do
    R=$(post "$body")
    case "$R" in *'"ok":true'*) return 0;; esac
    sleep 2
  done
  echo "FAILED $body"; return 1
}
wait_done() {
  for j in $(seq 1 300); do
    sleep 3
    L=$(grep -a "^\[SpinPerf\]" "$LOG" | tail -1)
    case "$L" in *"=== done ==="*) return 0;; esac
  done
  return 1
}

echo "=== verify new API"
inv Bpsim.Perf.BehaviorTimingProbe SetMaxDelta '[0.33333334]'; echo

echo "--- enter play"
ok_or_retry '{"method":"playmode","params":{"action":"enter"}}' || exit 1
sleep 20

echo "=== A: full save, default catch-up (maximumDeltaTime=0.333) + sched"
ok_or_retry '{"method":"invoke_static","params":{"type":"Bpsim.QA.SpinPerfProbe","method":"Run","args":["file=perf/angel.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=1.0"]}}'
inv Bpsim.Perf.BehaviorTimingProbe Run '["sched"]'; echo
wait_done
grep -a "\[BehaviorTiming\] sched" "$LOG" | tail -6
grep -a "^\[SpinPerf\]" "$LOG" | grep -a single | tail -1

echo "=== B: full save, catch-up capped to one step (maximumDeltaTime=0.02)"
inv Bpsim.Perf.BehaviorTimingProbe SetMaxDelta '[0.02]'; echo
ok_or_retry '{"method":"invoke_static","params":{"type":"Bpsim.QA.SpinPerfProbe","method":"Run","args":["file=perf/angel.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=1.0"]}}'
inv Bpsim.Perf.BehaviorTimingProbe Run '["sched"]'; echo
wait_done
grep -a "\[BehaviorTiming\] sched" "$LOG" | tail -6
grep -a "^\[SpinPerf\]" "$LOG" | grep -a single | tail -1

echo "=== restore"
inv Bpsim.Perf.BehaviorTimingProbe SetMaxDelta '[0.33333334]'; echo
