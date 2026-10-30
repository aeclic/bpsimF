#!/bin/bash
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
SPEC="file=perf/angel.csv;sweep=0;bisect=1;mode=rigid;spin=0;gravity=0;dur=0.05"

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

echo "--- enter play"
ok_or_retry '{"method":"playmode","params":{"action":"enter"}}' || exit 1
sleep 22

echo "--- bisect on full save"
ok_or_retry "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.QA.SpinPerfProbe\",\"method\":\"Run\",\"args\":[\"$SPEC\"]}}" || exit 1
for j in $(seq 1 400); do
  sleep 3
  L=$(grep -a "^\[SpinPerf\]" "$LOG" | tail -1)
  case "$L" in *"=== done ==="*) echo "done"; break;; esac
done
grep -a "^\[SpinPerf\]" "$LOG" | grep -a "bisect\|no-\|single\|simulation:" | tail -20
