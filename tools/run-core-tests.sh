#!/usr/bin/env bash
# 在 Unity 外跑 GoNoGo.Core 測試（優先用 Unity 內建的 .NET SDK，Windows 和 macOS 都可以）。
# 1. UnityApiCheck：用 Unity 附的 NUnit 3.5 編譯一次，抓 Unity 不支援的 API（只編譯，不執行）。
# 2. CoreTests：用 NUnit 3.14 執行全部測試。
# 用法：tools/run-core-tests.sh [dotnet test 的額外參數，例如 --filter Name~Power]
# 找 dotnet 的順序：環境變數 UNITY_DOTNET → Unity Editor 內建的 SDK（預設安裝位置）→ PATH 上的 dotnet（需 .NET 8 以上）。
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
root="$here/.."
ver="$(sed -n 's/^m_EditorVersion: *//p' "$root/ProjectSettings/ProjectVersion.txt" | tr -d '\r')"

dotnet=""
for cand in \
  "${UNITY_DOTNET:-}" \
  "/c/Program Files/Unity/Hub/Editor/$ver/Editor/Data/DotNetSdk/dotnet.exe" \
  "/Applications/Unity/Hub/Editor/$ver/Unity.app/Contents/Resources/Scripting/DotNetSdk/dotnet" \
  "/Applications/Unity/Hub/Editor/$ver/Unity.app/Contents/DotNetSdk/dotnet"; do
  if [ -n "$cand" ] && [ -x "$cand" ]; then dotnet="$cand"; break; fi
done
if [ -z "$dotnet" ]; then
  if command -v dotnet >/dev/null 2>&1; then
    dotnet="dotnet"
  else
    echo "找不到 dotnet：請設定 UNITY_DOTNET 指向 Unity 內建的 dotnet，或安裝 .NET 8 SDK 以上。" >&2
    exit 1
  fi
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if ls "$root/Library/PackageCache"/com.unity.ext.nunit@*/ >/dev/null 2>&1; then
  "$dotnet" build --nologo -v q "$here/UnityApiCheck/UnityApiCheck.csproj"
else
  echo "略過 UnityApiCheck：還沒用 Unity 開過專案（Library/PackageCache 裡沒有 NUnit）" >&2
fi

cd "$here/CoreTests"
"$dotnet" test --nologo -v q "$@"
