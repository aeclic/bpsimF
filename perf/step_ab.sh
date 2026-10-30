#!/bin/bash
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
SPEC="file=perf/angel_10k.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=0.1"

post() { curl -s -m 25 -X POST -H "Content-Type: application/json" -d "$1" "$API"; }
ok_or_retry() {
  local body=$1 tries=${2:-90}
  for i in $(seq 1 $tries); do
    R=$(post "$body")
    case "$R" in *'"ok":true'*) return 0;; esac
    sleep 2
  done
  echo "FAILED $body"; return 1
}
run_test() {
  ok_or_retry "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.QA.SpinPerfProbe\",\"method\":\"Run\",\"args\":[\"$SPEC\"]}}" || return 1
  for j in $(seq 1 250); do
    sleep 3
    L=$(grep -a "^\[SpinPerf\]" "$LOG" | tail -1)
    case "$L" in *"=== done ==="*) return 0;; esac
  done
  return 1
}

echo "--- enter play"
ok_or_retry '{"method":"playmode","params":{"action":"enter"}}' || exit 1
sleep 22

echo "=== A: power cache OFF (legacy per-umbrella rescan)"
ok_or_retry '{"method":"invoke_static","params":{"type":"Bpsim.Perf.BehaviorTimingProbe","method":"SetPowerCache","args":[false]}}'
run_test && grep -a "^\[SpinPerf\]" "$LOG" | grep -a "single\|simulation:\|start:" | tail -4

echo "=== B: power cache ON (one pass per fixed step)"
ok_or_retry '{"method":"invoke_static","params":{"type":"Bpsim.Perf.BehaviorTimingProbe","method":"SetPowerCache","args":[true]}}'
run_test && grep -a "^\[SpinPerf\]" "$LOG" | grep -a "single\|simulation:\|start:" | tail -4
