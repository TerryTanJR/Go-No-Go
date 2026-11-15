<p align="center">
  <img src=".github/assets/logo.png" alt="Houston, We Have a Trade-off team logo" width="260">
</p>

<h1 align="center">Go/No-Go</h1>

<p align="center">
  <b>A co-op satellite mission game built on real NASA data.</b><br>
  Find the numbers, choose the parts, build the satellite, call Go or No-Go, launch, then run the mission.
</p>

<p align="center">
  NASA Space Apps Challenge · Team <b>Houston, We Have a Trade-off</b>
</p>

<p align="center">
  <b><a href="https://terrytanjr.github.io/Go-No-Go/">Play in your browser</a></b>
</p>

<p align="center">
  <b>English</b> · <a href="README.zh-TW.md">繁體中文</a>
</p>

---

## Presentation

**[View the slides (PowerPoint)](https://ncu365-my.sharepoint.com/:p:/g/personal/114522111_office365_ncu_edu_tw/IQCL2htBh3GOTZID3ydWQO3sAWHsiZlr9Sfn_UA_37T2mls?e=pBN0PD)**

## What the game is

Real mission design is a chain of trade-offs. A bigger battery adds mass, more mass needs a bigger rocket, and a bigger rocket eats the budget. **Go/No-Go** puts 1–4 players in a satellite facility and makes them work through those trade-offs as a team.

**The game never tells you whether your design will work.** You read the requirements, work out the numbers, and decide for yourselves. If you are wrong, the launch tells you, and the failure report shows the real values.

## How a mission plays

| Step | Where | What you do |
| --- | --- | --- |
| 1. Briefing | Control room | The flight director gives you the mission goal. |
| 2. Select | Control room big screen | Pick the instruments, power, comms, avionics, orbit, rocket and ground station. Some part values are blanked out: find them in clues around the facility (research wall, whiteboards, books, blueprints) and type them in. The dashboard only knows what you fill in. |
| 3. Build | Assembly bay | Each part arrives as a kit of small components. Fetch them from the shelves, fit them at the bench in the right order, and install the part on the satellite. |
| 4. Go/No-Go | Control room launch pad | Nobody tells you the design is ready. Solo, you make the call yourself. In co-op, pressing launch starts a poll: every player calls GO or NO-GO, any NO-GO means HOLD, and only an all-GO poll launches. |
| 5. Launch | Launch view | Reach orbit, or fail and redesign. |
| 6. Operations | Control room | Four turns of events: solar storms from real DONKI records, part failures, power anomalies, science opportunities, and fire alerts in the wildfire mission. Read the telemetry, check the manual, and handle each event in a short minigame. |
| 7. Report | Report screen | A score broken down into science, lifetime, efficiency, requirements and operations, a review of every event call, and a comparison with real missions (Landsat 9, PRISMA, Planet Dove). |

## Screenshots

<table>
  <tr>
    <td width="50%"><img src=".github/assets/screenshots/assembly-bay.png" alt="Assembly bay"></td>
    <td width="50%"><img src=".github/assets/screenshots/control-room.png" alt="Control room big screen"></td>
  </tr>
  <tr>
    <td align="center">Your astronaut in the assembly bay</td>
    <td align="center">Choosing parts on the control room big screen</td>
  </tr>
  <tr>
    <td><img src=".github/assets/screenshots/clue.png" alt="A clue with part values"></td>
    <td><img src=".github/assets/screenshots/bench.png" alt="Building a part at the bench"></td>
  </tr>
  <tr>
    <td align="center">A clue reveals blanked part values</td>
    <td align="center">Building a camera from its components</td>
  </tr>
  <tr>
    <td><img src=".github/assets/screenshots/launch.png" alt="Launch countdown"></td>
    <td><img src=".github/assets/screenshots/report.png" alt="Mission report"></td>
  </tr>
  <tr>
    <td align="center">Final countdown</td>
    <td align="center">Mission report and real-mission comparison</td>
  </tr>
</table>

<sub>The game switches between English and Traditional Chinese.</sub>

## Modes and missions

| Mode | Players | Description |
| --- | --- | --- |
| Training | 1 | Guided first mission. |
| Solo mission | 1 | The full loop on your own, with a tablet for the dashboard and manual. |
| Co-op room | 2–4 | Join with a room code, talk over built-in voice chat, split up to find clues and build. |
| Sandbox | 1 | No budget cap, no requirements. Every part, orbit and rocket is open. |

| Mission | Difficulty | Budget | Notes |
| --- | --- | --- | --- |
| Eyes on Earth | Cadet | $210M | Earth observation |
| Map the Moon | Engineer | $250M | Lunar mapping with a transfer and orbit insertion cinematic |
| Wildfire Watch | Mission Director | $40M | Small-satellite budget; the orbit you pick decides how long the eclipse is and how big the battery must be |
| Rewrite History: OCO (2009) | Engineer | $273.4M | Replays the Orbiting Carbon Observatory launch failure, then shows how NASA fixed it with OCO-2 |

Each mission has a fixed difficulty. **Cadet** colors the dashboard and names events for you; **Engineer** shows numbers only and gives 3 minutes per operations turn; **Mission Director** starts every value blank and gives 90 seconds per turn.

## NASA data inside

Every number in the game data carries a source entry (title, URL, and whether it is sourced, derived or estimated).

| NASA resource | Used for |
| --- | --- |
| DONKI space weather database | Solar flare and geomagnetic storm events during operations |
| Small Spacecraft Technology (SST) report | Part specs (mass, power, downlink, lifetime) and the components in each assembly kit |
| Mission press kits (OCO, OCO-2), JPL, NSSDC, NTRS | Historical mission, orbit and eclipse constants |
| LRO mission data, NSSDCA Moon Fact Sheet | Lunar transfer timing |
| Earth Observatory, SVS (Blue Marble, Black Marble, LROC) | Earth and Moon textures |
| NASA 3D Resources | ISS and Space Shuttle models on the title screen |
| NASA audio (CLPS IM-2 launch recordings, Juno Ganymede flyby) | Countdown, pad and liftoff sound in the launch scene; space ambience during operations |
| JPL Concept Maturity Levels | The end-of-tutorial message (CML 1–4: sketch, feasibility, trade space, point design) |

Full list with terms of use: [docs/DATA_SOURCES.md](docs/DATA_SOURCES.md). Per-file art credits: [Assets/GoNoGo/Art/CREDITS.md](Assets/GoNoGo/Art/CREDITS.md).

## Controls

| Key | Action |
| --- | --- |
| W A S D / Shift / Mouse | Move / run / look |
| Q | First- or third-person view |
| E | Use the design board, clues and consoles |
| C | Pick up or put back |
| V (hold) | Install a part or fit a component |
| B | Mission briefing |
| K / L | Knowledge cards / design library (save, share, compare) |
| T | Tablet (solo only) |
| X (hold) | Emote wheel: wave, cheer, laugh, nod |
| M | Mute or unmute mic (co-op) |
| G / N | Vote in the Go/No-Go poll (co-op) |
| H | Show or hide tutorial details (tutorial only) |
| Tab / Esc | Menu |

## Built with

| | |
| --- | --- |
| Engine | Unity 6000.6.8f1, URP |
| Multiplayer | Netcode for GameObjects, Unity Multiplayer Services (room codes), Vivox voice chat |
| Assets | glTFast + Draco for NASA `.glb` models; Blender for the facility and smoke effects |
| Targets | WebGL (browser) and Windows |
| Languages | English and Traditional Chinese, switchable in game |

## Repository layout

```
Assets/GoNoGo/
  Core/           Game rules with no Unity dependency: evaluator, simulator, scoring, orbit math, minigames
  Core.Tests/     Unit tests for Core
  Data/           JSON game data (parts, assembly kits, missions, orbits, launchers, DONKI events, strings)
  Game/           Session, settings, audio, localization
  Net/            Multiplayer and voice chat
  Presentation/   3D facility, player, avatars, satellite builder, operations view
  UI/             UI Toolkit screens and panels
  Launch/         Launch cinematic
  Editor/         Scene builders and release builds
  Art/, Audio/    Models, textures, fonts and NASA audio (credits in Art/CREDITS.md)
  Resources/      Minigame sprites
tools/            Data, art and test scripts (DONKI fetch, minigame art, Blender room builders)
docs/             Setup guide and data sources
```

## Run it locally

You need Git LFS and Unity **6000.6.8f1** with Web Build Support.

```bash
git clone https://github.com/TerryTanJR/Go-No-Go.git
cd Go-No-Go
git lfs pull
```

Open the folder in Unity and press Play: the game always starts from the title screen. Run the core tests with `bash tools/run-core-tests.sh`. To package a build, use the editor menu **GoNoGo → Release**.

The full environment guide (Unity CLI, Blender, Claude Code, team workflow) is in [docs/SETUP.md](docs/SETUP.md), written in Traditional Chinese.

## Use of AI tools

Much of the code, data cleanup and documentation was written with help from Claude Code (Anthropic). Commits it helped with carry a `Co-Authored-By: Claude` line. Game data values come from the sources cited in each `source` entry, and the audio is original NASA recordings.

## Disclaimer

Fan-made educational game: no NASA endorsement implied. The NASA insignia is not used anywhere. Rocket and company names are used only to identify real products.
