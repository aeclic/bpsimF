#!/bin/bash
# 只跑一个 probe，用行号标记严格区分本次输出。
# 用法： bash perf/step_one.sh "<spec>" [tag]
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
SPEC=${1:?spec required}
TAG=${2:-probe}

post() { curl -s -m 30 -X POST -H "Content-Type: application/json" -d "$1" "$API"; }

ok_or_retry() {
  local body=$1 tries=${2:-150}
  for i in $(seq 1 $tries); do
    R=$(post "$body")
    case "$R" in *'"ok":true'*) return 0;; esac
    sleep 2
  done
  return 1
}

PING=$(curl -s "$API/../ping" 2>/dev/null || true)
echo "ping: $(curl -s http://127.0.0.1:8791/ping)"

if ! curl -s http://127.0.0.1:8791/ping | grep -q '"playing":true'; then
  echo "--- enter play"
  ok_or_retry '{"method":"playmode","params":{"action":"enter"}}' || { echo "enter play failed"; exit 1; }
  sleep 25
fi

MARK=$(wc -l < "$LOG")
echo "--- probe $TAG: $SPEC (log mark=$MARK)"
R=$(post "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.QA.SpinPerfProbe\",\"method\":\"Run\",\"args\":[\"$SPEC\"]}}")
echo "Run → $R"
MARK=$(wc -l < "$LOG")   # Run 自己会打一行，从这之后算本次输出

for j in $(seq 1 400); do
  sleep 3
  if tail -n +$((MARK+1)) "$LOG" | grep -aq "^\[SpinPerf\] === done ==="; then
    echo "done after $((j*3))s"; break
  fi
done

echo "=== $TAG 全部输出（本次新增）"
tail -n +$((MARK+1)) "$LOG" | grep -a "^\[SpinPerf\]"
echo "=== end $TAG"
