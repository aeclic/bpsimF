#!/bin/bash
# 物理步分阶段计时：build / broadphase / contacts / jacobians / solve
# 用法: bash perf/step_phase.sh [csv] [spec-extra]
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
CSV=${1:-perf/angel_10k.csv}
EXTRA=${2:-}

post() { curl -s -m 25 -X POST -H "Content-Type: application/json" -d "$1" "$API"; }

# ok_when <json> <期望子串> [次数]
ok_when() {
  local body=$1 want=$2 tries=${3:-120}
  for i in $(seq 1 $tries); do
    R=$(post "$body")
    case "$R" in *"$want"*) echo "OK $R"; return 0;; esac
    sleep 2
  done
  echo "FAILED($want) $body"; return 1
}

echo "--- enter play"
ok_when '{"method":"playmode","params":{"action":"enter"}}' '"ok":true' || exit 1
echo "--- wait for world + PartManager"
sleep 35

echo "--- enable phase markers"
ok_when '{"method":"invoke_static","params":{"type":"Bpsim.Perf.PhysicsPhaseProbe","method":"Enable","args":[]}}' '"enabled"' 40 || exit 1

echo "--- load $CSV and simulate"
ok_when "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.QA.SpinPerfProbe\",\"method\":\"Run\",\"args\":[\"file=$CSV;sweep=0;bisect=0;mode=rigid;spin=0;gravity=0;dur=1.5;$EXTRA\"]}}" 'started' 60 || exit 1

echo "--- waiting for steady state, then reset"
for j in $(seq 1 150); do
  sleep 2
  if grep -aq "^\[SpinPerf\] \[warmup\]" "$LOG"; then echo "steady -> reset"; post '{"method":"invoke_static","params":{"type":"Bpsim.Perf.PhysicsPhaseProbe","method":"Reset","args":[]}}' >/dev/null; break; fi
done

echo "--- collecting 30 frames"
for j in $(seq 1 150); do
  sleep 3
  if grep -aq "frames=30 ms/frame" "$LOG"; then echo "collected"; break; fi
done

echo "=== result"
grep -a "\[PhysicsPhase\]" "$LOG" | tail -6
echo "=== spinperf tail"
grep -a "^\[SpinPerf\]" "$LOG" | tail -6
