#!/usr/bin/env bash
# 进 Play -> 设口径 -> 跑一个 SpinPerfProbe spec -> 等结束 -> 退 Play -> 打印关键行。
# 用法: perf/run.sh "<spec>" [maxDelta]
set -u
cd "$(dirname "$0")/.."
SPEC="${1:?need spec}"
MAXDELTA="${2:-0.02}"
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
API=http://127.0.0.1:8791/api

post() { curl -s -m 25 -X POST -H "Content-Type: application/json" -d "$1" $API; }
ping_now() { curl -s -m 10 http://127.0.0.1:8791/ping; }

post '{"method":"playmode","params":{"action":"enter"}}' > /dev/null
for i in $(seq 1 40); do
  p=$(ping_now)
  if echo "$p" | grep -q '"playing":true' && echo "$p" | grep -q '"busy":false'; then break; fi
  sleep 3
done
sleep 3
[ "$MAXDELTA" != "-" ] && post "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.Perf.BehaviorTimingProbe\",\"method\":\"SetMaxDelta\",\"args\":[\"$MAXDELTA\"]}}" > /dev/null

MARK=$(wc -l < "$LOG")
post "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.QA.SpinPerfProbe\",\"method\":\"Run\",\"args\":[\"$SPEC\"]}}"
echo
for j in $(seq 1 100); do
  sleep 5
  if tail -n +$((MARK + 1)) "$LOG" | grep -aq "=== done ==="; then
    tail -n +$((MARK + 1)) "$LOG" | grep -a "^\[SpinPerf\]" | grep -av "^\[SpinPerf\] spec:"
    break
  fi
done

post '{"method":"playmode","params":{"action":"exit"}}' > /dev/null
for i in $(seq 1 40); do
  p=$(ping_now)
  echo "$p" | grep -q '"playing":false' && echo "$p" | grep -q '"busy":false' && break
  sleep 5
done
[ "$MAXDELTA" != "-" ] && post '{"method":"invoke_static","params":{"type":"Bpsim.Perf.BehaviorTimingProbe","method":"SetMaxDelta","args":["0.33333334"]}}' > /dev/null
echo "=== finished ==="
