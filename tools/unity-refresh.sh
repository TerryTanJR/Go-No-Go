#!/usr/bin/env bash
# 讓開著的 Unity Editor 匯入新檔並重新編譯，然後列出編譯錯誤（沒有錯誤就印 OK）。
# 用法：tools/unity-refresh.sh   （Windows 用 Git Bash；macOS 用內建終端機）
here="$(cd "$(dirname "$0")/.." && pwd)"

# Windows：Unity CLI 裝在 %LOCALAPPDATA%\Unity\bin，Git Bash 預設找不到
if [ -n "${LOCALAPPDATA:-}" ] && command -v cygpath >/dev/null 2>&1; then
  export PATH="$(cygpath -u "$LOCALAPPDATA")/Unity/bin:$PATH"
fi
if ! command -v unity >/dev/null 2>&1; then
  echo "找不到 unity 指令：請先安裝 Unity CLI，並確認它在 PATH 上。" >&2
  exit 1
fi

# Unity CLI 在 Windows 要 Windows 格式的專案路徑
if command -v cygpath >/dev/null 2>&1; then proj="$(cygpath -w "$here")"; else proj="$here"; fi

# macOS 預設沒有 timeout（Homebrew coreutils 叫 gtimeout）；都沒有就直接執行
with_timeout() {
  local secs="$1"; shift
  if command -v timeout >/dev/null 2>&1; then timeout "$secs" "$@"
  elif command -v gtimeout >/dev/null 2>&1; then gtimeout "$secs" "$@"
  else "$@"; fi
}

log="$here/Logs/Editor.log"
[ -f "$log" ] || echo "注意：找不到 $log，無法從記錄檔檢查編譯錯誤。" >&2
before=$(wc -l < "$log" 2>/dev/null || echo 0)
with_timeout 120 unity command script eval --timeout 90 --caller plugin --skill unity-cli --project-path "$proj" \
  'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); return "refreshed";' >/dev/null 2>&1
# 編譯和 domain reload 期間 Editor 會暫時連不上，輪詢到 ready 為止（最多約 6 分鐘）
st=""
for i in $(seq 1 36); do
  sleep 10
  st=$(with_timeout 30 unity status --project-path "$proj" --format json 2>/dev/null | grep -o '"state": *"[a-z]*"' | head -1)
  case "$st" in *ready*) break;; esac
done
errs=$(tail -n +"$((before+1))" "$log" 2>/dev/null | grep -E "error CS[0-9]+" | sed 's/^.*Assets/Assets/' | sort -u)
if [ -n "$errs" ]; then echo "$errs"; exit 1; fi
echo "OK ${st}"
