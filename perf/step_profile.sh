#!/bin/bash
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"

post() { curl -s -m 25 -X POST -H "Content-Type: application/json" -d "$1" "$API"; }

ok_or_retry() {
  local body=$1 tries=${2:-90}
  for i in $(seq 1 $tries); do
    R=$(post "$body")
    case "$R" in *'"ok":true'*) echo "OK $R"; return 0;; esac
    sleep 2
  done
  echo "FAILED $body"; return 1
}

echo "--- enter play"
ok_or_retry '{"method":"playmode","params":{"action":"enter"}}' || exit 1
sleep 25

echo "--- load angel_10k and simulate"
ok_or_retry '{"method":"invoke_static","params":{"type":"Bpsim.QA.SpinPerfProbe","method":"Run","args":["file=perf/angel_10k.csv;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=0.05"]}}' || exit 1

for j in $(seq 1 120); do
  sleep 3
  L=$(grep -a "^\[SpinPerf\]" "$LOG" | tail -1)
  case "$L" in *"=== done ==="*) echo "probe done"; break;; esac
done

echo "--- arm behavior profiler"
ok_or_retry '{"method":"invoke_static","params":{"type":"Bpsim.Perf.BehaviorTimingProbe","method":"Run","args":[""]}}' || exit 1

for j in $(seq 1 160); do
  sleep 3
  if grep -aq "BehaviorTiming\] pass done" "$LOG"; then echo "profile done"; break; fi
done

echo "=== result"
grep -a "\[BehaviorTiming\]" "$LOG" | tail -22
