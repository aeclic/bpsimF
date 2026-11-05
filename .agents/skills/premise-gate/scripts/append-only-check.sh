#!/usr/bin/env bash
# premise-gate: 台账只增不减。
#   不变量 1  台账里的 fact id 集合只增不减（行内改状态、划删除线放行）
#   不变量 2  计划文件里的 `- [x]` 行数只增不减（改写、重排放行）
# 路径（按仓库约定覆盖）：
#   FACTS_FILE   台账路径，默认 docs/facts.md
#   PLAN_GLOBS   计划文件 glob（空格分隔），默认同时含 *.md 与 *.MD
# 用法：bash scripts/append-only-check.sh                索引 vs HEAD（pre-commit 口径）
#       bash scripts/append-only-check.sh --worktree     工作树 vs HEAD（含未跟踪文件）
#       bash scripts/append-only-check.sh HEAD~1         索引 vs 指定提交
# 退出码：0 = 通过；1 = 违反只增不减；2 = 有未暂存改动（索引模式拒绝静默通过）
set -uo pipefail

git rev-parse --git-dir >/dev/null 2>&1 || { echo "not a git repo — skipped"; exit 0; }

mode=index
if [ "${1:-}" = "--worktree" ]; then
  mode=worktree
  shift
fi
old_ref="${1:-HEAD}"
facts_file="${FACTS_FILE:-docs/facts.md}"
read -r -a plan_globs <<< "${PLAN_GLOBS:-tasks/*.md tasks/*.MD docs/plans/*.md docs/plans/*.MD}"
paths=("$facts_file" "${plan_globs[@]}")

if [ "$mode" = index ]; then
  untracked=$(git ls-files --others --exclude-standard -- "${paths[@]}")
  if [ -n "$untracked" ]; then
    printf '⚠ 未跟踪文件不参与检查（git add 后才纳入）：\n%s\n' "$untracked" >&2
  fi
  changed=$(git diff --cached --name-only "$old_ref" -- "${paths[@]}")
  if [ -z "$changed" ]; then
    unstaged=$(git diff --name-only -- "${paths[@]}")
    if [ -n "$unstaged" ]; then
      printf '⚠ 有未暂存改动：本检查默认看索引（pre-commit 口径）。\n' >&2
      printf '  先 git add，或改用：bash scripts/append-only-check.sh --worktree\n' >&2
      exit 2
    fi
    exit 0
  fi
  unstaged=$(git diff --name-only -- "${paths[@]}")
  if [ -n "$unstaged" ]; then
    printf '⚠ 另有未暂存改动未参与本次索引检查（--worktree 可检查工作树）。\n' >&2
  fi
  changed_plans=$(git diff --cached --name-only "$old_ref" -- "${plan_globs[@]}")
else
  changed=$( { git diff --name-only "$old_ref" -- "${paths[@]}"
               git ls-files --others --exclude-standard -- "${paths[@]}"; } | sort -u)
  [ -z "$changed" ] && exit 0
  changed_plans=$( { git diff --name-only "$old_ref" -- "${plan_globs[@]}"
                     git ls-files --others --exclude-standard -- "${plan_globs[@]}"; } | sort -u)
fi

tmpd=$(mktemp -d) || exit 1
trap 'rm -rf "$tmpd"' EXIT
status=0

# ---------- 台账: id 集合单调 ----------
extract_ids() {
  awk -F'|' '/^[[:space:]]*\|/ {
    id = $2; gsub(/^[[:space:]]+|[[:space:]]+$/, "", id)
    if (id ~ /^F[0-9]/) print id
  }' | sort -u
}
new_facts() {
  if [ "$mode" = index ]; then
    git show ":$facts_file" 2>/dev/null
  else
    cat "$facts_file" 2>/dev/null
  fi
}
git show "$old_ref:$facts_file" 2>/dev/null | extract_ids > "$tmpd/old_ids"
new_facts | extract_ids > "$tmpd/new_ids"
missing=$(comm -23 "$tmpd/old_ids" "$tmpd/new_ids")
if [ -n "$missing" ]; then
  printf '❌ 台账 fact id 消失（只许改行，不许删行）：\n%s\n' "$missing"
  status=1
fi

# ---------- 计划文件: [x] 行数单调 ----------
count_done_stdin() {
  grep -cE '^[[:space:]]*- \[[xX]\]' || true
}
count_done_new() {
  if [ "$mode" = index ]; then
    git show ":$1" 2>/dev/null | count_done_stdin
  else
    count_done_stdin < "$1" 2>/dev/null
  fi
}
printf '%s\n' "$changed_plans" > "$tmpd/tasks"
while IFS= read -r f; do
  [ -z "$f" ] && continue
  old_count=$(git show "$old_ref:$f" 2>/dev/null | count_done_stdin)
  old_count=${old_count:-0}
  new_count=$(count_done_new "$f")
  new_count=${new_count:-0}
  if [ "$new_count" -lt "$old_count" ]; then
    printf '❌ %s 已完成任务行减少：%s → %s（只许 [ ]→[x] 或追加）\n' \
      "$f" "$old_count" "$new_count"
    status=1
  fi
done < "$tmpd/tasks"

[ "$status" -eq 0 ] && echo "✓ append-only 检查通过"
exit $status
