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
`C:\NASA\Go-no-go`: "Go/No-Go", a Unity 6 educational satellite-mission-design game for NASA Space Apps (a 3D facility you walk around in; UI Toolkit HUD; English by default, Traditional Chinese switchable in the menu; the language choice is NOT saved between launches). Shell: Git Bash. The Editor is open and in Edit mode right now.

Flow of a mission:
1. Title → main menu → mission briefing (panel; "Show all", then "Start the mission").
2. Design phase, in the control room. The DESIGN BOARD (kiosk object `BigScreen`, press E) lists parts by category. Nothing is ticked at the start: the player ticks parts (several instruments, one part in each other category). Some numbers of the parts are blank (`?`): they must be found in CLUES and typed in. Clues are scattered around both rooms (walls, boards, books, lockers, lecterns, sheets on the floor; objects named `Clue_<id>`); aim at one and press E to zoom in; many hold nothing useful. The dashboard on the right shows "Not enough data" for anything that depends on a blank value. When every category has a part, "CONFIRM DESIGN" unlocks.
3. Assembly, in the assembly bay (through the door on the west side of the control room). Component shelves on the north wall (`Bin<N>`, key C takes a component; solo players carry up to 4 in a basket). Two assembly benches (`AssemblyBench0`, `AssemblyBench1`): put the components of ONE part on the left side (E on a slot `AssemblyBench0/Slot<N>`); when the set matches a ticked part the right side (`AssemblyBench0/Jig`) starts a build order that describes what the NEXT component does; take the right component from the left with C and hold V at the jig (0.6 s) to fit it. A wrong fit costs nothing. A component taken by mistake can be put back on the left with E. When all are fitted, C takes the finished part; carry it to the integration stand (`IntegrationStand`, centre of the bay) and hold V (1.2 s) to install it.
4. Launch: when every ticked part is installed, the launch button in the control room (`LaunchButton`) starts the launch (in multiplayer, a Go/No-Go poll first). A failed launch sends everyone back to the design board with nothing ticked (typed values are kept).
5. Operations: several turns. Each turn: open the flight director desk, identify the event (on harder difficulties), play its treatment mini-game (the pass condition is shown in a yellow bar), then next turn. After the last turn: report screen.

## How to drive it
Read `tools/playtest/pt.sh` first, and skim `tools/playtest/Flow.cs` for the entry points and their doc comments.
- `tools/playtest/pt.sh play` / `stop` / `status`
- `tools/playtest/pt.sh flow <Entry> '<json args, ONE comma-separated string>'`, e.g. `flow Click '"Solo mission"'`, `flow AimFront '"AssemblyBench0/Slot2",1.2,true'`, `flow Hold '"v",400'`, `flow Send '"SelectPart","batt_medium",1'`
- `tools/playtest/pt.sh shot <name>` saves `Screenshots/<name>.png`. LOOK at screenshots with the Read tool: layout and visual problems only show there.
- `tools/playtest/pt.sh errors`: recent console errors and exceptions.
- `tools/playtest/pt.sh evalf <file.cs>`: run a C# snippet (script mode, `return` a string). Use it only to read state or open a panel (e.g. `GoNoGo.Game.Hud.Open("codex");`), never to change game data.
- Entries:
  - Read state: `State` (scene, phase, visible buttons), `Mission` (design, blanks, built/installed, launch blockers), `Parts`, `Bench`, `Target` (what the crosshair is on and its key hints), `Panel`, `Coach`, `Texts`, `Leaks` (untranslated keys or leftover `[[ ]]` markup; "clean" is good), `BlankCells`, `ClueMap` (which clue is where this game), `BinOf` (which shelf bin holds a component).
  - Input: `Click` (button text; use the text `State` lists), `Key`, `Hold` (key, frames), `Look`, `Wait` (frames).
  - Move and aim: `AimAt` (control-room consoles such as `LaunchButton`), `AimFrom` (name prefix, distance; e.g. `"BigScreen",1.6`, `"Clue_wall_1",1.6`), `AimFront` (name, distance, firstPerson; e.g. `"AssemblyBench0/Slot2"`, `"AssemblyBench1/Jig"`, `"Bin34"`), `AimPart`, `Goto` (x, z, yaw, pitch, firstPerson).
  - Setup and shortcuts: `Start` ("earth_observation" | "wildfire" | "lunar_mapping" | "hist_oco" | "tutorial" | "sandbox"; skips the menu), `Send` (a game command: type, string arg, int arg), `Fill` / `FillAll` (type blank values in), `BuildPart` / `BuildAll` (assemble and install following the player's rules).
- `tools/playtest/setup.sh [scenario]` enters Play, starts a solo mission, finishes the briefing, ticks a complete design, fills every blank and confirms the design, so you start at the assembly step.
- Gotchas:
  - The Editor is unfocused, so the frame rate is high (about 150 fps) and `Hold` counts frames, not milliseconds: the 0.6 s fit needs about `Hold '"v",200'`, the 1.2 s install about `Hold '"v",400'`.
  - Commands sent with `Send` apply on the next frame: `Wait 5` before reading state.
  - If `Key` returns "keyboard still disabled", call it again.
  - If a `flow` call prints nothing, run it without filtering to see the error JSON.
  - If the pause menu (Tab / Esc) is open, nothing can be targeted: close it first.
  - `AimAt`, `AimFrom` and `AimFront` with `firstPerson=true` switch to first person (the default view). To test third person, use `AimFront …,false` or `Goto …,false`; both views pick with the crosshair.
  - Never run `tools/unity-refresh.sh` while in Play mode: the reload breaks the running session and logs fake NullReference errors. `stop` first.
  - Launch success or failure follows from the design; to see a failure, build a design that cannot work (for example an orbit the rocket cannot reach).

## What changed in this round (test this first)
{{CHANGES}}

## Test plan (adapt as needed; go beyond it if something looks off)
A. **This round's changes**: exercise every item above with real keys (`Key`, `Hold`), in FIRST person (the default) and in THIRD person. After every action, check `State`/`Mission`/`Target` and take a screenshot.
B. **Edge cases for this round**:
{{FOCUS}}
   Also try the generic ones: the wrong key at the wrong place, empty hands vs full hands, opening and closing panels (K codex, L design library, B briefing, T tablet, Tab menu) in the middle of an action, and doing the action during the wrong phase.
C. **Full solo mission in English, from the title screen**: do not use `Start` here; click through the real menu. Go from the briefing through selection (tick parts on the design board, find at least two clues by walking to them and zooming in, then `FillAll` for the rest), confirm, assemble at least two parts by hand with real keys (then `BuildAll` for the rest), launch, all operations turns (identify the event, play or skip the mini-game) to the report screen. Check that the report reflects what you did.
D. **Tutorial** (`Start '"tutorial"'`): walk through every step as far as practical. Confirm that each coach text matches what the player must actually do (keys, where things are) and that each step ticks when its action is done.
E. **Traditional Chinese**: switch language in the Tab menu, repeat a short part of A, run `Leaks`, and look at screenshots for overflow, clipping and overlapping HUD elements.
F. **Sandbox** (`Start '"sandbox"'`) and one **history level** (`Start '"hist_oco"'`): check that they start, that the design board and launch work, and that this round's changes behave as intended there.
G. After each session, run `tools/playtest/pt.sh errors`. Compare timestamps: an error that appeared before your session is not yours.

## When you finish
- Run `tools/playtest/pt.sh stop` so the Editor is in Edit mode.
- Report:
  - **Problems**: a numbered list of confirmed problems, most serious first. For each, give what you did (the exact commands), what happened (quote the harness output and/or name the screenshot file), what you expected, and whether it reproduces.
  - **Bugs vs harness**: separate real game bugs from harness limitations.
  - **Suspected**: list suspected but unconfirmed problems separately.
  - **Coverage**: list what you tested that worked (one line each is enough) and what you could not test.
  - **Precision**: be precise about anything you did not actually verify.
````

## 寫 `{{CHANGES}}` 的方式

照這個格式，一項一段：玩家做什麼、按什麼鍵、會看到什麼、做錯會怎樣。例如：

```text
1. A component taken off the bench by mistake can be put back. While a part is being fitted (the jig shows a build order), take a component from the left side with C, then aim at a left slot: the hint shows "E: Put on the left side"; pressing E returns it and does not count as a wrong fit. A component that does not belong to the part is refused.
2. Panels keep their scroll position: on the design board, scroll the part list down, tick a part or type a value; the list must stay where it was.
```

`{{FOCUS}}` 寫這一輪特別容易出錯的地方，一行一個，例如：

```text
   - reopen the design after confirming, swap one part, check only that part needs rebuilding
   - launch with a part still on a bench, then check nothing is stuck
   - check that HUD hints do not overlap in first and third person, in both languages
```

## 這份範本的限制

- 測不到兩台電腦實際連線。多人只能看核心測試，或自己用 `evalf` 在這一局加一個模擬隊友，再用他的玩家編號送指令。
  這會改遊戲狀態，所以不寫進給 agent 的指示：`Send` 一律用本機玩家送。
- 測不到 WebGL 建置。
- 聽不到聲音：音效只能確認有沒有報錯。
- 發射成功或失敗由設計決定，不能直接指定結果。要測失敗流程，得刻意做一個到不了軌道的設計。
