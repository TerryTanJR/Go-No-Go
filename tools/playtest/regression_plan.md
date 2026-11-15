# 實機回歸測試：交給獨立 agent 的指示範本

改完一輪之後，請一個**沒參與修改的** agent（例如 Sonnet）照這份指示操作開著的 Unity Editor 玩一遍，只觀察和回報，不改東西。
寫程式的人自己測，容易只測自己想到的路徑；換一個人照計畫玩，比較會撞到沒想到的問題。

## 怎麼用

1. 確認 Unity Editor 開著、在 Edit 模式、沒有編譯錯誤（`tools/unity-refresh.sh`）。
2. 把下面「指示範本」整段複製，填好兩個地方：
   - `{{CHANGES}}`：這一輪改了什麼。寫玩家看得到的行為，不寫程式細節。
   - `{{FOCUS}}`：這一輪要特別測的情境，例如邊界情況、多人顯示。沒有就刪掉。
3. 交給 agent，它跑完會交回一份報告。跑的期間不要碰 Unity：它會進出 Play 模式、操作畫面。
4. 拿到報告後：
   - **逐條確認**：確認是遊戲 bug 還是測試工具的限制。
   - **修正並記錄**：修掉之後寫進 `friction_log.md`；測試工具的問題也記。
   - **自己重測**：修過的地方自己進 Play 再看一次。

在 Claude Code 裡可以直接說：「照 `tools/playtest/regression_plan.md` 開一個 Sonnet 跑回歸，這一輪改了…」。

## 指示範本

這段用英文寫，是因為實際跑過、報告品質穩定。報告要中文的話，把第一段最後一句改掉即可。

````text
You are doing an independent hands-on regression test of a Unity game by driving the already-open Unity Editor from the command line. DO NOT edit any game code, data or docs, do not commit, do not close the Editor. Your only job is to play, observe and report bugs with evidence. Write your final report in English.

## Project
`C:\NASA\Go-no-go`: "Go/No-Go", a Unity 6 educational satellite-mission-design game for NASA Space Apps (a 3D facility you walk around in; UI Toolkit HUD; Traditional Chinese and English). Flow of a mission: title menu → mission briefing → design phase (phone the offices for requirements, buy parts at the procurement terminal, assemble kits at the assembly bench, install parts at the integration stand, check the dashboards) → launch → operations (4 turns: judge each event at the flight director desk, then play its minigame) → report screen. Shell: Git Bash. The Editor is open and in Edit mode right now.

## How to drive it
Read `tools/playtest/pt.sh` first, and skim `tools/playtest/Flow.cs` for the entry points and their doc comments.
- `tools/playtest/pt.sh play` / `stop` / `status`
- `tools/playtest/pt.sh flow <Entry> '<json args, ONE comma-separated string>'`, e.g. `flow Click '"個人任務"'`, `flow AimFront '"Tray2",1.2,false'`, `flow Hold '"v",500'`, `flow Send '"CartAdd","batt_medium",0'`
- `tools/playtest/pt.sh shot <name>` saves `Screenshots/<name>.png`. LOOK at screenshots with the Read tool: layout and visual problems only show there.
- `tools/playtest/pt.sh errors`: recent console errors and exceptions.
- `tools/playtest/pt.sh evalf <file.cs>`: run a C# snippet (script mode, `return` a string). Use it only to read state or open a panel (e.g. `GoNoGo.Game.Hud.Open("goals");`), never to change game data.
- Entries:
  - Read state: `State`, `Mission`, `Parts`, `Bench`, `Target`, `Panel`, `Coach`, `Texts`, `Leaks` (untranslated keys or leftover `[[ ]]` markup; "clean" is good).
  - Input: `Click` (button text), `Key`, `Hold` (key, frames), `Look`, `Wait` (frames).
  - Move and aim: `AimAt` (control-room consoles), `AimFrom` (prefix, dist), `AimFront` (name, dist, firstPerson; e.g. "AssemblyBench", "Tray0".."Tray5"), `AimPart`.
  - Setup: `Start` ("earth_observation" | "tutorial" | "sandbox"; skips the menu), `Send` (a game command: type, string arg, int arg).
- `tools/playtest/r2_setup.sh [scenario] [part ids…]` enters Play, starts a solo mission, finishes the briefing, orders the parts and waits for delivery.
- Gotchas:
  - A newly ordered part rides the conveyor for about 12 s: run `flow Wait 900` before aiming at `Item_<id>`.
  - The Editor is unfocused, so the frame rate is high and `Hold` counts frames, not milliseconds: the 0.6 s fit needs about `Hold '"v",500'`, the 1.2 s install about `900`.
  - If `Key` returns "keyboard still disabled", call it again.
  - If a `flow` call prints nothing, run it without filtering to see the error JSON.
  - `AimFrom` only works once the player is already in the assembly bay: call `AimFront '"AssemblyBench",3,false'` first.
  - If the pause menu (繼續遊戲 / 離開 / …) is open, nothing can be targeted: close it with `flow Click '"繼續遊戲"'`.
  - `AimAt`, `AimFrom` and `AimFront` with `firstPerson=true` switch to first person. To test third person, use `AimFront …,false`.
  - Never run `tools/unity-refresh.sh` while in Play mode: the reload breaks the running session and logs fake NullReference errors. `stop` first.

## What changed in this round (test this first)
{{CHANGES}}

## Test plan (adapt as needed; go beyond it if something looks off)
A. **This round's changes**: exercise every item above with real keys (`Key`, `Hold`), in THIRD person (the default) and in FIRST person. After every action, check `State`/`Mission`/`Target` and take a screenshot.
B. **Edge cases for this round**:
{{FOCUS}}
   Also try the generic ones: the wrong key at the wrong place, empty hands vs full hands, opening and closing panels (K codex, L manual, B briefing, Tab menu) in the middle of an action, and doing the action during the wrong phase.
C. **Full solo mission, Traditional Chinese, from the title screen**: do not use `Start` here; click through the real menu. Go from the briefing through design, launch, all 4 operations turns (judge the event, play or skip the minigame) to the report screen. Check that the report reflects what you did.
D. **Tutorial** (`Start '"tutorial"'`): walk through every step as far as practical. Confirm that each coach text matches what the player must actually do (keys, where things are) and that each step ticks when its action is done.
E. **English**: switch language in the menu's 基本設定, repeat a short part of A, run `Leaks`, and look at screenshots for overflow, clipping and overlapping HUD elements. Switch back to Traditional Chinese at the end.
F. **Sandbox** (`Start '"sandbox"'`): check that this round's changes behave as intended without limits.
G. After each session, run `tools/playtest/pt.sh errors`. Compare timestamps: an error that appeared before your session is not yours.

## When you finish
- Make sure the language is back to Traditional Chinese (PlayerPrefs `gonogo.lang` = `zh-TW`), and run `tools/playtest/pt.sh stop` so the Editor is in Edit mode.
- Report:
  - **Problems**: a numbered list of confirmed problems, most serious first. For each, give what you did (the exact commands), what happened (quote the harness output and/or name the screenshot file), what you expected, and whether it reproduces.
  - **Bugs vs harness**: separate real game bugs from harness limitations.
  - **Suspected**: list suspected but unconfirmed problems separately.
  - **Coverage**: list what you tested that worked (one line each is enough) and what you could not test.
  - **Precision**: be precise about anything you did not actually verify.
````

## 寫 `{{CHANGES}}` 的方式

照這個格式，一項一段：玩家做什麼、按什麼鍵、會看到什麼、做錯會怎樣。舉 2026-11-15 那一輪為例：

```text
1. Hands-on assembly replaced a card-clicking panel. A bought part arrives on the conveyor as a kit. Carry it to the assembly workbench (AssemblyBench) and press E. Six small components appear on the parts trays (Tray0–Tray5) to the left of the bench. The build order card (HUD top right, and the bench's screen) describes what the NEXT component does; the trays show only names. Take one with C, walk back and hold V to fit it. Right → next step. Wrong → +0.25 month on the Time resource and a toast. When all are fitted, C picks up the finished part; install it at the integration stand as before.
2. The mission briefing can be reopened: HUD chip "[B] 任務說明", key B, and a button in the Tab menu.
```

`{{FOCUS}}` 寫這一輪特別容易出錯的地方，一行一個，例如：

```text
   - place a second kit while one is on the bench
   - launch while a kit is half-built, then check nothing is stuck
   - check that the B chip does not overlap other HUD elements in third and first person
```

## 這份範本的限制

- 測不到兩台電腦實際連線。多人只能看核心測試，或自己用 `evalf` 在這一局加一個模擬隊友，再用他的玩家編號送指令。
  這會改遊戲狀態，所以不寫進給 agent 的指示：`Send` 一律用本機玩家送。
- 測不到 WebGL 建置。
- 發射成功或失敗由設計決定，不能直接指定結果。要測失敗流程，得刻意做一個超重的設計。
