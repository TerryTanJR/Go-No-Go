#!/usr/bin/env bash
# 在 Unity 外跑 GoNoGo.Core 測試（用 Unity 內建的 .NET SDK）。
# 1. UnityApiCheck：用 Unity 附的 NUnit 3.5 編譯一次，抓 Unity 不支援的 API（只編譯，不執行）。
# 2. CoreTests：用 NUnit 3.14 執行全部測試。
# 用法：tools/run-core-tests.sh [dotnet test 的額外參數，例如 --filter Name~Power]
set -euo pipefail
here="$(cd "$(dirname "$0")" && pwd)"
dotnet="/c/Program Files/Unity/Hub/Editor/6000.6.8f1/Editor/Data/DotNetSdk/dotnet.exe"
[ -x "$dotnet" ] || dotnet="dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

if ls "$here/../Library/PackageCache"/com.unity.ext.nunit@*/ >/dev/null 2>&1; then
  "$dotnet" build --nologo -v q "$here/UnityApiCheck/UnityApiCheck.csproj"
else
  echo "略過 UnityApiCheck：還沒用 Unity 開過專案（Library/PackageCache 裡沒有 NUnit）" >&2
fi

cd "$here/CoreTests"
"$dotnet" test --nologo -v q "$@"
