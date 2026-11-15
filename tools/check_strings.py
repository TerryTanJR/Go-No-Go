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

    sandbox = load("sandbox")
    def req_list(sc):
        # requirements 可能是 {處室: [需求…]}，也可能直接是一個清單
        reqs = sc.get("requirements", [])
        groups = reqs.values() if isinstance(reqs, dict) else [reqs]
        return [r for grp in groups for r in grp]
    req_types = sorted({r["type"] for sc in scen + [sandbox.get("scenario", sandbox)] for r in req_list(sc)})
    fields = ["mass_kg", "power_w", "capacity_wh", "resolution_m", "data_gb_per_day", "downlink_mbps"]   # PartFields.All
    mediums = ["paper", "whiteboard", "blackboard", "book", "blueprint"]                                  # Clues.Spots
    chapters = ["rules", "diagnose", "procedures", "donki"]                                               # ManualPanels
    families = {
        "ui.cat.": ["instrument", "solar", "battery", "antenna", "groundstation", "computer", "adcs",
                    "propulsion", "shielding", "orbit", "launcher", "years"],
        "ui.difficulty.": [d["id"] for d in diffs],
        "ui.objective.": sorted({s["objective"] for s in scen}),
        "ui.ops.end.": ["missioncomplete", "propellant", "battery", "radiation", "launchfailure"],
        "ui.report.grade.": ["s", "a", "b", "c", "fail"],
        "ui.tier.": ["1", "2", "3"],
        "ui.dash.limit.": ["none", "propellant", "battery", "radiation"],
        "warn.lifetime.": ["propellant", "battery", "radiation"],
        "ui.manual.rule.": [k + t for k in ["power", "battery", "mass", "data", "comms", "budget", "lifetime", "instruments",
                                                   "fire", "latency", "falsefire"] for t in ["", ".t"]],
        "ui.manual.ch.": [c + t for c in chapters for t in ["", "_body"]],
        "ui.manual.name.": chapters,
        "ui.manual.intro.": chapters,
        "ui.fail.category.": ["power", "launcher", "mass"],
        "ui.area.": ["assembly", "control", "lobby"],
        "ui.hud.controls": ["", "_solo"],
        # 新手訓練（TutorialCoach.Steps）與一般局的「下一步」（MissionGuide）
        "ui.tut.": [k + t for k in ["move", "view", "clue", "fill", "confirm", "shelf", "bench", "assemble", "install", "launch",
                                    "console", "judge"] for t in ["", ".t"]],
        "ui.guide.": [k + t for k in ["pick", "values", "fix", "fix_goals", "confirm", "fetch", "put", "fit", "take", "install",
                                      "budget", "launch", "vote", "ops_act", "ops_end", "ops_next"] for t in ["", ".t"]],
        "ui.brief.speech.": [s["id"] for s in scen] + ["default", "sandbox"],
        "event.title.": ["solar_storm", "part_failure", "science_opportunity", "power_anomaly", "big_fire", "false_fire"],
        "event.diag.": ["solar_storm", "part_failure", "science_opportunity", "power_anomaly", "big_fire", "false_fire"],
        "ops.console.": ["payload", "power", "attitude", "comms"],
        "ui.avatar.helmet.": ["0", "1", "2"],
        "ui.avatar.lower.": ["0", "1", "2", "3", "4"],
        "ui.avatar.acc.": ["0", "1", "2", "3", "4"],
        "res.": ["mass", "power", "budget", "data", "comms"],
        # 選件、挖空的數值、線索
        "ui.field.": fields,
        "ui.clue.phrase.": fields,
        "ui.clue.medium.": mediums,
        "ui.clue.junk.": [m + "." + str(i) for m in mediums for i in range(3)],
        "ui.clue.note.": [str(i) for i in range(4)],
        "ui.clue.note.head.": [str(i) for i in range(4)],
        "ui.clue.note.sign.": [str(i) for i in range(4)],
        "ui.mspec.": ["science", "array", "area", "txpower", "link", "storage", "radiation", "pointing", "propellant", "isp", "dose", "shield"],
        # 手冊：症狀對照表的欄、格子，太空天氣的事件種類
        "ui.manual.sig.": ["radiation", "battery", "solar", "parts", "alert", "mwir", "lwir"],
        "ui.manual.mx.": ["up", "up2", "down", "down2", "half", "fault", "alert", "flat"],
        "ui.manual.sw.": ["FLR", "CME", "GST"],
        "ui.req.group.": ["mission_lead", "budget_office", "science", "engineering", "comms"],
        "ui.prog.type.": req_types,
        "ui.prog.stage.": ["select", "assemble", "launch", "ops", "eval"],
        # 組裝
        "ui.asm.state.": ["installed", "ready", "carried", "built", "building", "todo"],
        "ui.bench.status.": ["empty", "partial", "unknown", "notindesign", "waiting", "active", "done"],
        "ui.lobby.board.": ["select", "select.body", "assemble", "assemble.body"],
        "ui.wall.howto.": [str(i) for i in range(6)],
    }

    # Core 也掃：規則回傳的錯誤碼（err.*）、原因與建議（cause.*、suggest.*、warn.*）都是字串鍵
    code = ""
    for folder in ["Assets/GoNoGo/Core", "Assets/GoNoGo/UI", "Assets/GoNoGo/Game", "Assets/GoNoGo/Presentation", "Assets/GoNoGo/Launch",
                   "Assets/GoNoGo/Net", "Assets/GoNoGo/Editor"]:
        for f in (ROOT / folder).rglob("*.cs"):
            code += f.read_text(encoding="utf-8") + "\n"

    needed = set()
    for m in re.finditer(r'(StartsWith\()?"((?:ui|event|choice|opp|res|launch|nogo|warn|fix|cause|suggest|err)\.[a-z0-9_.]*)"', code):
        key = m.group(2)
        # StartsWith("warn.lifetime") 是在比對前綴，不是一個字串鍵
        if m.group(1) or key.endswith(".") or key in families:
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
