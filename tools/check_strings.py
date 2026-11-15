# -*- coding: utf-8 -*-
"""
檢查程式裡用到的每個字串鍵都在 strings.json 裡，而且中英文都不是空的。
動態組出來的鍵（"ui.cat." + 類別）用下面的 FAMILIES 展開。
用法：python tools/check_strings.py   （有缺就 exit 1）
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
GD = ROOT / "Assets/GoNoGo/Data/Resources/GameData"


def load(name):
    return json.loads((GD / f"{name}.json").read_text(encoding="utf-8"))


def main():
    strings = load("strings")
    scenarios = load("scenarios")
    difficulty = load("difficulty")
    scen = scenarios if isinstance(scenarios, list) else scenarios.get("scenarios", [])
    diffs = difficulty if isinstance(difficulty, list) else difficulty.get("difficulties", [])

    families = {
        "ui.cat.": ["instrument", "solar", "battery", "antenna", "groundstation", "computer", "adcs",
                    "propulsion", "shielding", "orbit", "launcher", "years"],
        "ui.contrib.": ["bus", "structure", "propellant", "operations", "integration", "procurement"],
        "ui.difficulty.": [d["id"] for d in diffs],
        "ui.objective.": sorted({s["objective"] for s in scen}),
        "ui.ops.end.": ["missioncomplete", "propellant", "battery", "radiation", "launchfailure"],
        "ui.report.grade.": ["s", "a", "b", "c", "fail"],
        "ui.tier.": ["1", "2", "3"],
        "ui.dash.limit.": ["none", "propellant", "battery", "radiation"],
        "ui.manual.rule.": [k + t for k in ["power", "battery", "mass", "data", "comms", "budget", "lifetime", "instruments",
                                                   "fire", "latency", "falsefire"] for t in ["", ".t"]],
        "ui.fail.category.": ["power", "launcher", "mass"],
        "ui.phone.notes_": sorted({d.get("notes_mode", "transcript") for d in diffs}),
        "ui.area.": ["assembly", "control", "lobby"],
        "ui.hud.controls": ["", "_solo"],
        "ui.tut.": [k + t for k in ["move", "view", "procure", "pick", "bench", "assemble", "install", "dashboard", "call", "notes", "manual", "launch", "console", "judge"] for t in ["", ".t"]],
        "ui.guide.": [k + t for k in ["call", "procure", "pick", "assemble", "fit", "take", "install", "fix", "fix_ctl", "fix_goals", "launch", "ready_asm", "review",
                                      "wait_review", "vote", "poll_go", "poll_wait", "ops_act", "ops_end", "ops_next"] for t in ["", ".t"]],
        "ui.lobby.team_": ["assembly", "control"],
        "ui.intro.": ["assembly", "control", "assembly.t", "control.t"],
        "ui.brief.speech.": [s["id"] for s in scen] + ["default", "sandbox"],
        "event.title.": ["solar_storm", "part_failure", "science_opportunity", "power_anomaly", "big_fire", "false_fire"],
        "event.diag.": ["solar_storm", "part_failure", "science_opportunity", "power_anomaly", "big_fire", "false_fire"],
        "ops.console.": ["payload", "power", "attitude", "comms"],
        "ui.avatar.helmet.": ["0", "1", "2"],
        "ui.avatar.lower.": ["0", "1", "2", "3", "4"],
        "ui.avatar.acc.": ["0", "1", "2", "3", "4"],
        "res.": ["mass", "power", "budget", "data", "comms", "time"],
        # 發射前點名的六個項目：讀數是什麼、GO 的意思、HOLD 之後去哪裡修
        "ui.poll.what.": ["mass", "power", "budget", "data", "comms", "time"],
        "ui.poll.go.": ["mass", "power", "budget", "data", "comms", "time"],
        "ui.poll.fix.": ["mass", "power", "budget", "data", "comms", "time"],
    }

    code = ""
    for folder in ["Assets/GoNoGo/UI", "Assets/GoNoGo/Game", "Assets/GoNoGo/Presentation", "Assets/GoNoGo/Launch", "Assets/GoNoGo/Net", "Assets/GoNoGo/Editor"]:
        for f in (ROOT / folder).rglob("*.cs"):
            code += f.read_text(encoding="utf-8") + "\n"

    needed = set()
    for m in re.finditer(r'"((?:ui|event|choice|opp|res|launch|nogo|warn|fix|cause|suggest|err)\.[a-z0-9_.]*)"', code):
        key = m.group(1)
        if key.endswith(".") or key in families:
            continue
        needed.add(key)
    for prefix, items in families.items():
        if f'"{prefix}"' in code:
            needed.update(prefix + i for i in items)

    missing = sorted(k for k in needed if k not in strings)
    empty = sorted(k for k, v in strings.items() if not v.get("en") or not v.get("zh-TW"))
    unused_ui = sorted(k for k in strings if k.startswith("ui.") and k not in needed)

    ok = True
    if missing:
        ok = False
        print("MISSING:", *missing, sep="\n  ")
    if empty:
        ok = False
        print("EMPTY:", *empty, sep="\n  ")
    if unused_ui:
        print("unused ui.* (not an error):", *unused_ui, sep="\n  ")
    print(f"checked {len(needed)} keys: {'OK' if ok else 'FAIL'}")
    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
