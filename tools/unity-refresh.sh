#!/usr/bin/env bash
# 讓開著的 Unity Editor 匯入新檔並重新編譯，然後列出編譯錯誤（沒有錯誤就印 OK）。
# 用法：tools/unity-refresh.sh
export PATH="/c/Users/sheng/AppData/Local/Unity/bin:$PATH"
proj='C:\NASA\NASA_test'
here="$(cd "$(dirname "$0")/.." && pwd)"
before=$(wc -l < "$here/Logs/Editor.log" 2>/dev/null || echo 0)
timeout 120 unity command script eval --timeout 90 --caller plugin --skill unity-cli --project-path "$proj" \
  'UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport); UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation(); return "refreshed";' >/dev/null 2>&1
# 編譯和 domain reload 期間 Editor 會暫時連不上，輪詢到 ready 為止（最多約 6 分鐘）
for i in $(seq 1 36); do
  sleep 10
  st=$(timeout 30 unity status --project-path "$proj" --format json 2>/dev/null | grep -o '"state": *"[a-z]*"' | head -1)
  case "$st" in *ready*) break;; esac
done
errs=$(tail -n +"$((before+1))" "$here/Logs/Editor.log" | grep -E "error CS[0-9]+" | sed 's/^.*Assets/Assets/' | sort -u)
if [ -n "$errs" ]; then echo "$errs"; exit 1; fi
echo "OK ${st}"
