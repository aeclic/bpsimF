#!/bin/bash
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"

post() { curl -s -m 25 -X POST -H "Content-Type: application/json" -d "$1" "$API"; }
ok_or_retry() {
  local body=$1 tries=${2:-120}
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

echo "--- enter play"
ok_or_retry '{"method":"playmode","params":{"action":"enter"}}' || exit 1
sleep 22

echo "=== 10k load (cache ON) + behaviour breakdown"
run_probe "file=perf/angel_10k.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=0.1"
grep -a "^\[SpinPerf\]" "$LOG" | grep -a "single" | tail -1

ok_or_retry '{"method":"invoke_static","params":{"type":"Bpsim.Perf.BehaviorTimingProbe","method":"Run","args":[""]}}'
for j in $(seq 1 200); do
  sleep 3
  if grep -aq "BehaviorTiming\] pass done" "$LOG"; then break; fi
done
N=$(grep -an "BehaviorTiming\] pass done" "$LOG" | tail -1 | cut -d: -f1)
sed -n "${N},$((N+8))p" "$LOG"

echo "=== full save (20405 parts, cache ON)"
run_probe "file=perf/angel.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=0.1"
grep -a "^\[SpinPerf\]" "$LOG" | grep -a "single\|simulation:\|start:" | tail -3
