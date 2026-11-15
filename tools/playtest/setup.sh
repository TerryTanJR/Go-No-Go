#!/usr/bin/env bash
# 實機測試的開場：進 Play、開一局個人任務、看完簡報、在設計主螢幕勾一套完整的設計、
# 把挖空的數值都填上、確定設計。跑完人在中控室，可以直接去組裝區組裝。
# 用法：tools/playtest/setup.sh [情境 id，預設 earth_observation] [零件 id…（不給就用這個情境的預設設計）]
here="$(cd "$(dirname "$0")" && pwd)"
pt="$here/pt.sh"
r() { "$pt" flow "$@" | grep '"result"' | cut -c1-600; }
scn="${1:-earth_observation}"; shift
parts=("$@")
[ ${#parts[@]} -eq 0 ] && parts=(inst_visible solar_body batt_small ant_xband obc_cubesat adcs_compact prop_none shield_std)
"$pt" play >/dev/null; sleep 6
r Start "\"$scn\""
r Click '"Show all"' >/dev/null
r Click '"Start the mission"' >/dev/null
r Wait 30 >/dev/null
for p in "${parts[@]}"; do r Send "\"SelectPart\",\"$p\",1" >/dev/null; done
r FillAll >/dev/null
r Send '"Confirm","",1' >/dev/null
r Wait 10 >/dev/null
r Mission
