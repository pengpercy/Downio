#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CSProj="$ROOT/src/Downio/Downio.csproj"
UpdateWindowAxaml="$ROOT/src/Downio/Views/UpdateWindow.axaml"
DefaultNotesFile="$ROOT/tools/local/release-notes.md"

print_usage() {
  cat <<'USAGE'
用法:
  tools/local/release.sh [--bump patch|minor|major] [--notes-file PATH] [--push] [--watch] [--remote NAME] [--branch NAME] [--dry-run]
  tools/local/release.sh --set X.Y.Z [--notes-file PATH] [--push] [--watch] [--remote NAME] [--branch NAME] [--dry-run]
  tools/local/release.sh --help

行为:
  1) 更新项目版本号 (Downio.csproj)；并同步 UpdateWindow.axaml 的设计时 TagName/CurrentVersion（仅用于设计器预览）
  2) git commit 版本变更
  3) 创建带注释的 tag vX.Y.Z
     - 如果提供了发布说明文件，则 tag 注释内容使用该文件
     - 如果没提供，则从上一个 tag 到当前 HEAD 的提交信息自动生成发布说明
  4) push 分支与 tag 到远端，从而触发 GitHub Actions 的 release.yml（tag push 触发）

为什么发布说明放在 Markdown 文件更好:
  - 你可以用熟悉的编辑器写长文、列表、链接
  - 该工作流会把“tag 注释内容”作为 Release notes（release.yml 内 git tag 读取）

重要说明:
  - tools/local/ 已加入 .gitignore，本脚本与 release-notes.md 默认不会被提交
  - 你需要已安装并登录 gh（GitHub CLI），并且 git remote 可 push

示例:
  # 默认 bump patch，自动生成发布说明，打 tag 并 push
  tools/local/release.sh --push

  # 手写发布说明（Markdown），优先使用你的内容
  $EDITOR tools/local/release-notes.md
  tools/local/release.sh --bump minor --notes-file tools/local/release-notes.md --push

  # 指定版本号
  tools/local/release.sh --set 1.2.3 --push

  # 仅预览将要做的事情（不会改文件/提交/tag/push）
  tools/local/release.sh --bump patch --dry-run
USAGE
}

die() {
  echo "错误: $*" >&2
  exit 1
}

require_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "缺少命令: $1"
}

git_is_clean() {
  git diff --quiet && git diff --cached --quiet
}

trim() {
  python3 - <<'PY' "$1"
import sys
print(sys.argv[1].strip())
PY
}

read_csproj_version() {
  python3 - <<'PY' "$CSProj"
import re, sys, pathlib
p = pathlib.Path(sys.argv[1])
s = p.read_text(encoding="utf-8")
m = re.search(r"<Version>([^<]+)</Version>", s)
if not m:
  raise SystemExit("Version not found in csproj")
print(m.group(1).strip())
PY
}

compute_next_version() {
  local current="$1"
  local bump="$2"
  python3 - <<'PY' "$current" "$bump"
import re, sys
cur, bump = sys.argv[1], sys.argv[2]
m = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)", cur.strip())
if not m:
  raise SystemExit(f"Invalid current version: {cur}")
maj, min_, pat = map(int, m.groups())
if bump == "major":
  maj += 1; min_ = 0; pat = 0
elif bump == "minor":
  min_ += 1; pat = 0
elif bump == "patch":
  pat += 1
else:
  raise SystemExit(f"Invalid bump: {bump}")
print(f"{maj}.{min_}.{pat}")
PY
}

validate_version() {
  local v="$1"
  python3 - <<'PY' "$v"
import re, sys
v = sys.argv[1].strip()
if not re.fullmatch(r"\d+\.\d+\.\d+", v):
  raise SystemExit(f"Invalid version: {v}")
PY
}

update_versions_in_files() {
  local old="$1"
  local new="$2"

  python3 - <<'PY' "$CSProj" "$old" "$new"
import re, sys, pathlib
p = pathlib.Path(sys.argv[1])
old, new = sys.argv[2], sys.argv[3]
s = p.read_text(encoding="utf-8")
ns, n = re.subn(rf"<Version>\s*{re.escape(old)}\s*</Version>", f"<Version>{new}</Version>", s, count=1)
if n != 1:
  ns, n = re.subn(r"<Version>[^<]+</Version>", f"<Version>{new}</Version>", s, count=1)
  if n != 1:
    raise SystemExit("Failed to update <Version> in csproj")
p.write_text(ns, encoding="utf-8")
PY

  python3 - <<'PY' "$UpdateWindowAxaml" "$old" "$new"
import re, sys, pathlib
p = pathlib.Path(sys.argv[1])
old, new = sys.argv[2], sys.argv[3]
s = p.read_text(encoding="utf-8")

# 设计时 TagName 仅用于预览
ns, _ = re.subn(r'TagName="v\d+\.\d+\.\d+"', f'TagName="v{new}"', s, count=1)

# 设计时 CurrentVersion 让 UI 看起来像“从旧版本升级到新版本”
ns, _ = re.subn(r"<views:UpdateViewModel\.CurrentVersion>\s*[\d\.]+\s*</views:UpdateViewModel\.CurrentVersion>",
                f"<views:UpdateViewModel.CurrentVersion>{old}</views:UpdateViewModel.CurrentVersion>", ns, count=1)

p.write_text(ns, encoding="utf-8")
PY
}

generate_notes() {
  local old_tag="$1"
  local new_version="$2"

  local range=""
  if [[ -n "$old_tag" ]]; then
    range="${old_tag}..HEAD"
  else
    range="HEAD"
  fi

  local changes
  changes="$(git log --no-merges --pretty=format:'- %s' $range || true)"
  if [[ -z "${changes// }" ]]; then
    changes="- （无提交信息可用）"
  fi

  if [[ -n "$old_tag" ]]; then
    cat <<EOF
🚀 Downio v${new_version}

✨ 更新内容
${changes}

🔗 完整对比
- https://github.com/pengpercy/Downio/compare/${old_tag}...v${new_version}
EOF
  else
    cat <<EOF
🚀 Downio v${new_version}

✨ 更新内容
${changes}
EOF
  fi
}

main() {
  require_cmd git
  require_cmd python3
  require_cmd gh

  local bump="patch"
  local set_version=""
  local notes_file=""
  local do_push="false"
  local do_watch="false"
  local dry_run="false"
  local remote="origin"
  local branch="master"

  while [[ $# -gt 0 ]]; do
    case "$1" in
      --bump)
        bump="${2:-}"; shift 2;;
      --set)
        set_version="${2:-}"; shift 2;;
      --notes-file)
        notes_file="${2:-}"; shift 2;;
      --push)
        do_push="true"; shift;;
      --watch)
        do_watch="true"; shift;;
      --dry-run)
        dry_run="true"; shift;;
      --remote)
        remote="${2:-}"; shift 2;;
      --branch)
        branch="${2:-}"; shift 2;;
      -h|--help)
        print_usage; exit 0;;
      *)
        die "未知参数: $1";;
    esac
  done

  cd "$ROOT"

  git rev-parse --is-inside-work-tree >/dev/null 2>&1 || die "当前目录不是 git 仓库: $ROOT"

  if [[ "$dry_run" != "true" ]] && ! git_is_clean; then
    die "工作区有未提交改动，请先提交/暂存/清理后再执行（避免把无关改动带入发布提交）"
  fi

  local current_branch
  current_branch="$(git branch --show-current)"
  if [[ -z "$current_branch" ]]; then
    die "当前不在分支上（detached HEAD），请切回分支后再执行"
  fi

  if [[ "$current_branch" != "$branch" ]]; then
    echo "提示: 当前分支为 '$current_branch'，脚本默认以 '$branch' 为发布分支。继续将会在当前分支上发布。"
  fi

  local old_version
  old_version="$(read_csproj_version)"
  validate_version "$old_version"

  local old_tag=""
  old_tag="$(git describe --tags --abbrev=0 --match 'v*' 2>/dev/null || true)"

  local new_version=""
  if [[ -n "$set_version" ]]; then
    new_version="$(trim "$set_version")"
    validate_version "$new_version"
  else
    case "$bump" in
      patch|minor|major) ;;
      *) die "--bump 只能是 patch|minor|major";;
    esac
    new_version="$(compute_next_version "$old_version" "$bump")"
  fi

  if [[ "$new_version" == "$old_version" ]]; then
    die "新版本号与当前版本相同: $new_version"
  fi

  local tag="v${new_version}"
  if git rev-parse -q --verify "refs/tags/${tag}" >/dev/null; then
    die "tag 已存在: ${tag}"
  fi

  if [[ "$dry_run" == "true" ]]; then
    echo "（dry-run）将发布版本: ${new_version}（当前: ${old_version}）"
    echo "（dry-run）将创建 tag: ${tag}"
    echo
    echo "（dry-run）发布说明预览（tag 注释内容）:"
    echo "------------------------------------------------------------"
    if [[ -n "$notes_file" ]]; then
      if [[ ! -f "$notes_file" ]]; then
        die "发布说明文件不存在: $notes_file"
      fi
      cat "$notes_file"
    else
      generate_notes "$old_tag" "$new_version"
    fi
    echo
    echo "------------------------------------------------------------"
    echo "（dry-run）实际执行将会:"
    echo "  - 修改: $CSProj"
    echo "  - 修改: $UpdateWindowAxaml"
    echo "  - 提交: git commit -m \"Release ${tag}\""
    echo "  - 打 tag: git tag -a ${tag} -F <notes>"
    echo "  - 推送: git push ${remote} HEAD && git push ${remote} ${tag}"
    exit 0
  fi

  echo "将发布版本: ${new_version}（当前: ${old_version}）"

  update_versions_in_files "$old_version" "$new_version"

  git add "$CSProj" "$UpdateWindowAxaml"
  git commit -m "Release ${tag}"

  local tmp_notes=""
  if [[ -n "$notes_file" ]]; then
    if [[ ! -f "$notes_file" ]]; then
      die "发布说明文件不存在: $notes_file"
    fi
    if [[ ! -s "$notes_file" ]]; then
      die "发布说明文件为空: $notes_file"
    fi
    tmp_notes="$notes_file"
  else
    tmp_notes="$(mktemp)"
    generate_notes "$old_tag" "$new_version" > "$tmp_notes"
  fi

  echo "创建 tag: ${tag}（注释将作为 Release notes）"
  git tag -a "$tag" -F "$tmp_notes"

  if [[ "$tmp_notes" != "$notes_file" ]]; then
    rm -f "$tmp_notes" || true
  fi

  if [[ "$do_push" == "true" ]]; then
    echo "推送到远端: ${remote}（分支与 tag）"
    git push "$remote" HEAD
    git push "$remote" "$tag"

    echo "已推送 tag，GitHub Actions 会触发 Release 工作流（.github/workflows/release.yml）。"

    if [[ "$do_watch" == "true" ]]; then
      echo "等待 CI 完成（如果 gh 权限不足，这一步会失败，但不影响 tag push）。"
      local repo
      repo="$(gh repo view --json nameWithOwner --jq .nameWithOwner 2>/dev/null || true)"
      if [[ -n "$repo" ]]; then
        local run_id
        run_id="$(gh run list --repo "$repo" --workflow Release --limit 1 --json databaseId --jq '.[0].databaseId' 2>/dev/null || true)"
        if [[ -n "$run_id" ]]; then
          gh run watch "$run_id" --repo "$repo" --exit-status || true
        else
          gh run list --repo "$repo" --limit 5 || true
        fi
      else
        gh run list --limit 5 || true
      fi
    fi
  else
    echo "未执行 push（未传 --push）。你可以手动执行:"
    echo "  git push ${remote} HEAD"
    echo "  git push ${remote} ${tag}"
  fi
}

main "$@"
