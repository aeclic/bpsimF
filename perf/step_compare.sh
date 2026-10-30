#!/usr/bin/env bash
# 巨构模式性能对比：同一存档、同一口径（一帧 = 一固定步）下跑 mega=0 / mega=1。
# 用法: perf/step_compare.sh <csv相对路径> <标签>
set -u
cd "$(dirname "$0")/.."
CSV="${1:-perf/angel.csv}"
TAG="${2:-angel}"
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
API=http://127.0.0.1:8791/api

post() { curl -s -m 20 -X POST -H "Content-Type: application/json" -d "$1" $API; }

wait_idle() {
  for i in $(seq 1 60); do
    p=$(curl -s -m 10 http://127.0.0.1:8791/ping)
    echo "$p" | grep -q '"playing":true' && echo "$p" | grep -q '"busy":false' && return 0
    sleep 5
  done
  return 1
}

run_probe() {
  local spec="$1"
  local mark
  mark=$(wc -l < "$LOG")
  post "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.QA.SpinPerfProbe\",\"method\":\"Run\",\"args\":[\"$spec\"]}}"
  echo
  for j in $(seq 1 90); do
    sleep 6
    if tail -n +$((mark + 1)) "$LOG" | grep -aq "=== done ==="; then
      tail -n +$((mark + 1)) "$LOG" | grep -a "^\[SpinPerf\]" > "perf/out_${TAG}_$(echo "$spec" | md5sum | cut -c1-6).txt"
      tail -n +$((mark + 1)) "$LOG" | grep -a "^\[SpinPerf\]" | grep -a "mega:\|StartSimulation\|parts=\|single w=\|idle\|rigid\|circuit:" | sed 's/joints=0->0 comp=1->1 //' | cut -c1-160
      return 0
    fi
  done
  echo "TIMEOUT waiting for $spec"
  return 1
}

post '{"method":"playmode","params":{"action":"enter"}}' > /dev/null
sleep 25
post '{"method":"invoke_static","params":{"type":"Bpsim.Perf.BehaviorTimingProbe","method":"SetMaxDelta","args":["0.02"]}}'
echo
COMMON="file=$CSV;sweep=0;bisect=0;sysbisect=0;forcebisect=0;mode=rigid;spin=0;dur=1.5;gravity=0"

echo "=== $TAG mega=0 ==="
run_probe "$COMMON;mega=0"
echo "=== $TAG mega=1 ==="
run_probe "$COMMON;mega=1"

post '{"method":"playmode","params":{"action":"exit"}}' > /dev/null
for i in $(seq 1 40); do
  p=$(curl -s -m 10 http://127.0.0.1:8791/ping)
  echo "$p" | grep -q '"playing":false' && echo "$p" | grep -q '"busy":false' && break
  sleep 5
done
post '{"method":"invoke_static","params":{"type":"Bpsim.Perf.BehaviorTimingProbe","method":"SetMaxDelta","args":["0.33333334"]}}'
echo
echo "=== done ==="
