#!/usr/bin/env bash
# discovery-ledger: 台账只增不减。
#   不变量 1  docs/facts.md 的 fact id 集合只增不减（行内改状态、划删除线放行）
#   不变量 2  tasks/*.md、docs/plans/*.md 里 `- [x]` 的行数只增不减（改写、重排放行）
# 用法：bash scripts/append-only-check.sh          暂存区 vs HEAD
#       bash scripts/append-only-check.sh HEAD~1   索引 vs 指定提交（工作区干净时即检查该提交）
set -uo pipefail

git rev-parse --git-dir >/dev/null 2>&1 || { echo "not a git repo — skipped"; exit 0; }

old_ref="${1:-HEAD}"
changed=$(git diff --cached --name-only "$old_ref" -- docs/facts.md 'tasks/*.md' 'docs/plans/*.md')
[ -z "$changed" ] && exit 0

tmpd=$(mktemp -d) || exit 1
trap 'rm -rf "$tmpd"' EXIT
status=0

# ---------- docs/facts.md: id 集合单调 ----------
extract_ids() {
  awk -F'|' '/^[[:space:]]*\|/ {
    id = $2; gsub(/^[[:space:]]+|[[:space:]]+$/, "", id)
    if (id ~ /^F[0-9]/) print id
  }' | sort -u
}
git show "$old_ref:docs/facts.md" 2>/dev/null | extract_ids > "$tmpd/old_ids"
git show ":docs/facts.md" 2>/dev/null | extract_ids > "$tmpd/new_ids"
missing=$(comm -23 "$tmpd/old_ids" "$tmpd/new_ids")
if [ -n "$missing" ]; then
  printf '❌ 台账 fact id 消失（只许改行，不许删行）：\n%s\n' "$missing"
  status=1
fi

# ---------- tasks/*.md: [x] 行数单调 ----------
count_done() {
  git show "$1" 2>/dev/null | grep -cE '^[[:space:]]*- \[[xX]\]'
}
git diff --cached --name-only "$old_ref" -- 'tasks/*.md' 'docs/plans/*.md' > "$tmpd/tasks"
while IFS= read -r f; do
  [ -z "$f" ] && continue
  old_count=$(count_done "$old_ref:$f"); old_count=${old_count:-0}
  new_count=$(count_done ":$f");         new_count=${new_count:-0}
  if [ "$new_count" -lt "$old_count" ]; then
    printf '❌ %s 已完成任务行减少：%s → %s（只许 [ ]→[x] 或追加）\n' "$f" "$old_count" "$new_count"
    status=1
  fi
done < "$tmpd/tasks"

[ "$status" -eq 0 ] && echo "✓ append-only 检查通过"
exit $status
