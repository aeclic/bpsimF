#!/usr/bin/env bash
# 退 Play -> 强制编译 -> 校验错误。用法: perf/build.sh [超时秒]
set -u
cd "$(dirname "$0")/.."
LOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
API=http://127.0.0.1:8791/api
DLL=Library/ScriptAssemblies/Assembly-CSharp.dll

post() { curl -s -m 25 -X POST -H "Content-Type: application/json" -d "$1" $API; }
ping_now() { curl -s -m 10 http://127.0.0.1:8791/ping; }

# 1) 若在 Play，先退出（绝不能在 Play 里刷新资产）
if ping_now | grep -q '"playing":true'; then
  echo "exiting play mode..."
  for i in $(seq 1 40); do
    post '{"method":"playmode","params":{"action":"exit"}}' > /dev/null 2>&1
    p=$(ping_now)
    echo "$p" | grep -q '"playing":false' && echo "$p" | grep -q '"busy":false' && break
    sleep 5
  done
fi
ping_now | grep -q '"playing":false' || { echo "FAILED: still playing"; exit 1; }

# 2) 强制重编译（AssetDatabase.Refresh 在本工程不一定触发编译）
BEFORE=$(stat -c %Y "$DLL")
MARK=$(wc -l < "$LOG")
post '{"method":"invoke_static","params":{"type":"UnityEditor.Compilation.CompilationPipeline","method":"RequestScriptCompilation","args":[]}}' > /dev/null

# 3) 等 DLL 重建
for i in $(seq 1 40); do
  sleep 3
  if [ "$(stat -c %Y "$DLL")" != "$BEFORE" ]; then
    echo "rebuilt: $(stat -c '%y' "$DLL")"
    break
  fi
done
sleep 3
echo "--- error CS ---"
tail -n +$((MARK + 1)) "$LOG" | grep -a "error CS" | sort -u | head -10
echo "--- end ---"
