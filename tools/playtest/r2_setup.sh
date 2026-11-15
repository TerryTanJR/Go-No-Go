#!/usr/bin/env bash
# 實機測試的開場：進 Play、開一局、看完簡報、買幾箱零件、等零件從收貨口送出來。
# 用法：tools/playtest/r2_setup.sh [情境 id，預設 earth_observation] [零件 id…]
here="$(cd "$(dirname "$0")" && pwd)"
pt="$here/pt.sh"
r() { "$pt" flow "$@" | grep '"result"' | cut -c1-600; }
scn="${1:-earth_observation}"; shift
parts=("$@")
[ ${#parts[@]} -eq 0 ] && parts=(batt_medium solar_deployable inst_multispectral adcs_wheels)
"$pt" play >/dev/null; sleep 6
r Start "\"$scn\""
r Click '"全部顯示"' >/dev/null
r Click '"開始任務"' >/dev/null
for p in "${parts[@]}"; do r Send "\"CartAdd\",\"$p\",0" >/dev/null; done
r Send '"PlaceOrder","",0' >/dev/null
r Wait 900 >/dev/null
r Mission
