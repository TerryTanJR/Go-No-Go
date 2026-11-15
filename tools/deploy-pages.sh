#!/usr/bin/env bash
# 把網頁版部署到 GitHub Pages（gh-pages 分支）。
# 1. 用開著的 Unity 編輯器打包網頁版（和選單 GoNoGo → Release → Web only 同一套設定：Gzip＋備用解壓縮，
#    GitHub Pages 不用設定伺服器標頭就能開）。
# 2. 把 Builds/Release/Web 的內容整個換到 gh-pages 的工作資料夾（預設 ../Go-no-go-pages），第一次會自動建立。
# 3. commit 並 push gh-pages。GitHub Pages 約 1–3 分鐘後更新。
# 用法：tools/deploy-pages.sh [--skip-build] [--no-push]
#   --skip-build  不重新打包，直接部署現有的 Builds/Release/Web
#   --no-push     只 commit 不 push，可以先在本機檢查
# 需要：Unity CLI（unity），專案在 Unity 編輯器裡開著、沒有在 Play 模式。
# gh-pages 是獨立的分支（跟 main 沒有共同歷史、沒有 LFS），只放打包結果。
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
pages="${PAGES_DIR:-$(cd "$root/.." && pwd)/Go-no-go-pages}"
web="$root/Builds/Release/Web"
proj="$(cygpath -w "$root" 2>/dev/null || echo "$root")"

build=1; push=1
for arg in "$@"; do
  case "$arg" in
    --skip-build) build=0 ;;
    --no-push) push=0 ;;
    -h|--help) sed -n '2,11p' "$0" | sed 's/^# \{0,1\}//'; exit 0 ;;
    *) echo "不認得的參數：$arg（用 --help 看用法）" >&2; exit 2 ;;
  esac
done

# ---------- 1. 打包 ----------
if [ "$build" = 1 ]; then
  command -v unity >/dev/null 2>&1 || { echo "找不到 unity 指令：請先安裝 Unity CLI（docs/SETUP.md）。" >&2; exit 1; }
  state="$(unity command script eval 'return UnityEditor.EditorApplication.isPlaying ? "playing" : "idle";' \
    --project-path "$proj" --format json 2>/dev/null || true)"
  case "$state" in
    *'"result":"idle"'*) ;;
    *'"result":"playing"'*) echo "Unity 編輯器在 Play 模式：先停止再部署。" >&2; exit 1 ;;
    *) echo "連不到這個專案的 Unity 編輯器：請先在 Unity 打開 $proj，等它載入完成。" >&2; exit 1 ;;
  esac
  echo "打包網頁版中（要切換平台時約 10–20 分鐘）……"
  out="$(unity command script eval 'return GoNoGo.EditorTools.ReleaseBuilder.BuildWeb();' \
    --project-path "$proj" --timeout 3600 --format json 2>/dev/null || true)"
  case "$out" in
    *'"result":"Web: OK'*) echo "打包完成。" ;;
    *) echo "打包失敗，請看 Unity 的 Console。CLI 回傳：" >&2; echo "$out" >&2; exit 1 ;;
  esac
fi
[ -f "$web/index.html" ] || { echo "找不到 $web/index.html：請先打包（拿掉 --skip-build）。" >&2; exit 1; }

big="$(find "$web" -type f -size +95M)"
if [ -n "$big" ]; then
  echo "下面的檔案接近或超過 GitHub 單檔 100 MB 上限，無法 push：" >&2; echo "$big" >&2; exit 1
fi

# ---------- 2. 準備 gh-pages 工作資料夾 ----------
if [ ! -e "$pages/.git" ]; then
  echo "建立 gh-pages 工作資料夾：$pages"
  if git -C "$root" fetch -q origin gh-pages 2>/dev/null; then
    git -C "$root" worktree add -B gh-pages "$pages" FETCH_HEAD
    git -C "$pages" branch -q -u origin/gh-pages
  elif git -C "$root" show-ref -q --verify refs/heads/gh-pages; then
    git -C "$root" worktree add "$pages" gh-pages
  else
    git -C "$root" worktree add --orphan -b gh-pages "$pages"
  fi
fi
branch="$(git -C "$pages" symbolic-ref --short HEAD 2>/dev/null || true)"
if [ "$branch" != "gh-pages" ]; then
  echo "$pages 不在 gh-pages 分支（目前：${branch:-未知}），為了安全不動它。" >&2; exit 1
fi

# ---------- 3. 換上新的打包結果 ----------
find "$pages" -mindepth 1 -maxdepth 1 ! -name .git -exec rm -rf {} +
cp -R "$web"/. "$pages"/
rm -rf "$pages"/*_DoNotShip "$pages"/*_ButDontShipItWithYourGame
touch "$pages/.nojekyll"                                # 不經過 Jekyll，檔案原樣提供
printf '* -text\n' > "$pages/.gitattributes"            # 不做換行轉換、不走 LFS，位元組原樣存

# ---------- 4. commit、push ----------
git -C "$pages" add -A
if git -C "$pages" diff --cached --quiet 2>/dev/null && git -C "$pages" rev-parse -q --verify HEAD >/dev/null; then
  echo "打包結果和上次部署一樣，不用更新。"; exit 0
fi
src="$(git -C "$root" rev-parse --short HEAD)"
[ -n "$(git -C "$root" status --porcelain)" ] && src="$src+未commit的修改"
git -C "$pages" commit -q -m "deploy: web build from main $src"
echo "已 commit：$(git -C "$pages" log -1 --format='%h %s')"

remote="$(git -C "$root" remote get-url origin)"
owner="$(echo "$remote" | sed -E 's#.*github\.com[:/]([^/]+)/.*#\1#' | tr '[:upper:]' '[:lower:]')"
repo="$(echo "$remote" | sed -E 's#.*github\.com[:/][^/]+/([^/]+)$#\1#; s#\.git$##')"
url="https://$owner.github.io/$repo/"

if [ "$push" = 0 ]; then
  echo "沒有 push（--no-push）。要上線時執行：git -C \"$pages\" push -u origin gh-pages"; exit 0
fi
git -C "$pages" push -q -u origin gh-pages
echo "已推到 gh-pages。"

if command -v gh >/dev/null 2>&1 && ! gh api "repos/$owner/$repo/pages" >/dev/null 2>&1; then
  echo "注意：這個 repo 還沒開 GitHub Pages。repo 公開後到 Settings → Pages 選 gh-pages / (root)，或執行："
  echo "  gh api -X POST repos/$owner/$repo/pages -f \"source[branch]=gh-pages\" -f \"source[path]=/\""
else
  echo "約 1–3 分鐘後更新：$url（瀏覽器用 Ctrl+Shift+R 強制重新整理）"
fi
