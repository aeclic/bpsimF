#!/usr/bin/env bash
# Anvil vs Havok vs Unity.Physics 对比矩阵
#
#   ./run_matrix.sh            # 标准矩阵（contacts=0，纯物理）
#   ./run_matrix.sh contacts   # 额外跑 contacts=1（含事件采集）
#
# 每个规模输出到 Logs/bench-matrix-<n>[-c].log
set -u
PROJ="D:\\rpgvx + magpie\\BPRC 2026.3.2 proj\\bpsim-main"
cd "/d/rpgvx + magpie/BPRC 2026.3.2 proj/bpsim-main" || exit 1

run() {
  local n="$1" contacts="$2" suffix="$3"
  env -u https_proxy -u http_proxy -u no_proxy \
    ANVIL_BENCH_SPEC="bodies=$n;steps=200;warmup=60;tree=0;weld=1;contacts=$contacts" \
    "/d/folder/2022.2.23f1/Editor/Unity.exe" -batchmode -quit \
    -projectPath "$PROJ" -executeMethod Bpsim.QA.AnvilBenchBatch.Run \
    -logFile "Logs/bench-matrix-$n$suffix.log" >/dev/null 2>&1
  echo "=== bodies=$n contacts=$contacts ==="
  grep -E "^--- backend|step ms:|anvil breakdown median|weld drift|sanity:|box3d counters" \
    "Logs/bench-matrix-$n$suffix.log"
}

MODE="${1:-plain}"
for n in 1000 2000 5000 10000; do
  run "$n" 0 ""
done
if [ "$MODE" = "contacts" ]; then
  for n in 1000 2000 5000 10000; do
    run "$n" 1 "-c"
  done
fi
echo "ALL DONE"
