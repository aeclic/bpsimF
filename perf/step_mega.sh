#!/bin/bash
# 巨构优化（mega）验证 + A/B：同一存档分别用 mega=0 / mega=1 跑，比较
#   1) StartSimulation 耗时（启动卡顿）  2) 每帧/每步物理开销（运行时）
# 用法： bash perf/step_mega.sh [small|angel|both]
API=http://127.0.0.1:8791/api
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
WHICH=${1:-small}

post() { curl -s -m 25 -X POST -H "Content-Type: application/json" -d "$1" "$API"; }

ok_or_retry() {
  local body=$1 tries=${2:-120}
  for i in $(seq 1 $tries); do
    R=$(post "$body")
    case "$R" in *'"ok":true'*) echo "OK $R"; return 0;; esac
    sleep 2
  done
  echo "FAILED $body"; return 1
}

run_probe() {   # $1 = spec, $2 = 标记
  local spec=$1 tag=$2
  echo "--- probe $tag: $spec"
  local mark
  mark=$(grep -ac "^\[SpinPerf\]" "$LOG")
  ok_or_retry "{\"method\":\"invoke_static\",\"params\":{\"type\":\"Bpsim.QA.SpinPerfProbe\",\"method\":\"Run\",\"args\":[\"$spec\"]}}" || return 1
  local j
  for j in $(seq 1 200); do
    sleep 3
    if [ "$(grep -ac '^\[SpinPerf\]' "$LOG")" -gt "$mark" ] && grep -aq "^\[SpinPerf\] === done ===" "$LOG"; then
      echo "probe $tag done"; break
    fi
  done
  echo "=== $tag 关键行"
  grep -a "^\[SpinPerf\]" "$LOG" | tail -40 | grep -a \
    "settings:\|mega:\|start: StartSimulation\|simulation: parts=\|diag:\|^\[SpinPerf\] base\|frames=\|===" | tail -20
  # 每次跑完清掉 done 标记，方便下一个 probe 判断
  echo "" >> "$LOG"
}

echo "--- enter play"
ok_or_retry '{"method":"playmode","params":{"action":"enter"}}' || exit 1
sleep 25

if [ "$WHICH" = "small" ] || [ "$WHICH" = "both" ]; then
  run_probe "file=perf/buhai.csv;mega=0;sweep=0;bisect=0;mode=rigid;spin=0;dur=1.5" buhai-normal
  run_probe "file=perf/buhai.csv;mega=1;sweep=0;bisect=0;mode=rigid;spin=0;dur=1.5" buhai-mega
fi

if [ "$WHICH" = "angel" ] || [ "$WHICH" = "both" ]; then
  run_probe "file=perf/angel.csv;mega=0;sweep=0;bisect=0;mode=rigid;spin=0;dur=0.5;gravity=0" angel-normal
  run_probe "file=perf/angel.csv;mega=1;sweep=0;bisect=0;mode=rigid;spin=0;dur=0.5;gravity=0" angel-mega
fi

echo "--- exit play"
ok_or_retry '{"method":"playmode","params":{"action":"exit"}}' || echo "exit play 失败（长帧时正常，稍后重试）"
echo "=== all done ==="
