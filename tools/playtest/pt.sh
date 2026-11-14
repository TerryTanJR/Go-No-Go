#!/usr/bin/env bash
# 從命令列操作開著的 Unity Editor 做實機測試（Windows 用 Git Bash）。
#   tools/playtest/pt.sh play | stop | status
#   tools/playtest/pt.sh flow State                 # tools/playtest/Flow.cs 的進入點
#   tools/playtest/pt.sh flow Click '"個人任務"'     # 參數是 JSON（字串要帶雙引號），多個用逗號隔開
#   tools/playtest/pt.sh evalf path/to/snippet.cs   # 執行一段 C#（Roslyn script 模式，最後 return 字串）
#   tools/playtest/pt.sh shot name                  # 截圖到 Screenshots/name.png（Game 視窗，含 UI）
#   tools/playtest/pt.sh errors                     # 最近的錯誤與例外
here="$(cd "$(dirname "$0")/../.." && pwd)"
if [ -n "${LOCALAPPDATA:-}" ] && command -v cygpath >/dev/null 2>&1; then
  export PATH="$(cygpath -u "$LOCALAPPDATA")/Unity/bin:$PATH"
fi
if command -v cygpath >/dev/null 2>&1; then proj="$(cygpath -w "$here")"; else proj="$here"; fi
U=(unity command --caller plugin --skill unity-cli --project-path "$proj")
cmd="$1"; shift
case "$cmd" in
  play)   "${U[@]}" editor.playmode start --result-only 2>/dev/null ;;
  stop)   "${U[@]}" editor.playmode stop --result-only 2>/dev/null ;;
  status) unity status --project-path "$proj" --format json 2>/dev/null | grep -o '"state": *"[a-z]*"' | head -1 ;;
  flow)
    entry="$1"; shift
    args="[$*]"
    "${U[@]}" --timeout 60 script run --file tools/playtest/Flow.cs --entry "Flow.$entry" --args "$args" --result-only 2>/dev/null ;;
  evalf)  "${U[@]}" --timeout 90 script eval --file "$1" --result-only 2>/dev/null ;;
  shot)   mkdir -p "$here/Screenshots"; "${U[@]}" capture game_view --source screen --output-path "Screenshots/$1.png" --result-only 2>/dev/null | head -c 300 ;;
  errors) "${U[@]}" console get --level error --tail 15 --result-only 2>/dev/null ;;
  *) echo "usage: pt.sh play|stop|status|flow <Entry> [json args]|evalf <file>|shot <name>|errors" >&2; exit 2 ;;
esac
