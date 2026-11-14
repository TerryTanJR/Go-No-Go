# -*- coding: utf-8 -*-
"""
介面文字（ui.*）的唯一來源。執行後合併進 Assets/GoNoGo/Data/Resources/GameData/strings.json。
用法：python tools/data/ui_strings.py
"""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from ui_strings_3d import UI3D, OBSOLETE_PREFIXES, OBSOLETE_KEYS  # noqa: E402

UI = {
    # 共用
    "ui.lang": ("Language", "語言"),
    "ui.role.director": ("Flight Director", "飛行總監"),
    "ui.role.engineer": ("Systems Engineer", "系統工程師"),
    "ui.role.scientist": ("Mission Scientist", "任務科學家"),
    "ui.role.comms": ("Comms & Ground", "通訊與地面站"),
    "ui.tier.1": ("BASIC", "基本"),
    "ui.tier.2": ("STANDARD", "標準"),
    "ui.tier.3": ("ADVANCED", "進階"),
    "ui.objective.earth_observation": ("EARTH OBSERVATION", "地球觀測"),
    "ui.objective.lunar_mapping": ("LUNAR MAPPING", "月球測繪"),
    "ui.objective.carbon_monitoring": ("CARBON MONITORING", "碳監測"),
    "ui.difficulty.engineer": ("Full information and suggested fixes. Department requirements ask for a 15% design margin.",
                               "所有資訊都看得到，警告會附上建議解法。部門要求 15% 的設計餘裕。"),
    "ui.difficulty.cadet": ("Bigger budget, fewer parts, gentler operations. Warnings come with fixes.",
                            "預算多一半、零件選擇少、營運事件較少，警告附解法。適合第一次玩。"),
    "ui.difficulty.director": ("Tight budget, more events, and only red lights: you diagnose the problems yourself.",
                               "預算更緊、事件更多，警告只亮紅燈：問題要自己找出來。"),

    # 標題
    "ui.title.eyebrow": ("NASA SPACE APPS · SATELLITE MISSION DESIGN", "NASA SPACE APPS · 衛星任務設計"),
    "ui.title.tagline": ("Design a satellite on real NASA numbers, poll your team, launch it, and live with the consequences.",
                         "用 NASA 的真實數據設計一顆衛星，召開 Go/No-Go 點名，發射，然後面對你的每一個決定。"),
    "ui.title.choose_mission": ("CHOOSE A MISSION", "選擇任務"),
    "ui.title.history_badge": ("REWRITE HISTORY", "改寫歷史"),
    "ui.title.difficulty": ("DIFFICULTY", "難度"),
    "ui.title.training": ("Ground training", "地面訓練"),
    "ui.title.training_desc": ("Step-by-step tips in the design room.", "在設計室一步一步提示操作。"),
    "ui.title.start": ("START MISSION", "開始任務"),
    "ui.title.credits": ("Data: NASA DONKI, NASA 3D Resources, NASA Earth Observatory, NASA audio, published mission specs (sources in each data file). Fan-made educational game: no NASA endorsement implied.",
                         "資料：NASA DONKI、NASA 3D Resources、NASA Earth Observatory、NASA 錄音與公開任務規格（每筆資料都附來源）。教育用途的非官方作品，不代表 NASA 背書。"),

    # 簡報
    "ui.brief.title": ("MISSION BRIEFING", "任務簡報"),
    "ui.brief.budget": ("Budget", "預算"),
    "ui.brief.lifetime": ("Target lifetime", "目標壽命"),
    "ui.brief.window": ("Launch window", "發射窗口"),
    "ui.brief.instruments": ("Max instruments", "儀器上限"),
    "ui.brief.difficulty": ("Difficulty", "難度"),
    "ui.brief.requirements": ("DEPARTMENT REQUIREMENTS", "部門要求"),
    "ui.brief.requirements_desc": ("Each department has its own goal. Meeting them raises your teamwork score.",
                                   "每個部門都有自己的目標，全部達成可以提高團隊分數。"),
    "ui.brief.back": ("Back to title", "回標題"),
    "ui.brief.enter_design": ("ENTER DESIGN ROOM", "進入設計室"),
    "ui.brief.review_design": ("REVIEW THE DESIGN AS FLOWN", "檢視當年的設計"),

    # 歷史
    "ui.history.background": ("HISTORICAL MISSION", "真實歷史任務"),
    "ui.history.locked": ("First you fly it exactly as it was flown. The design is locked until you find out what happened.",
                          "先照當年的設計原樣發射一次。知道發生什麼事之前，設計是鎖住的。"),
    "ui.history.reveal": ("WHAT REALLY HAPPENED", "真實發生的事"),
    "ui.history.real_fix": ("HOW NASA FIXED IT", "NASA 後來怎麼解決"),
    "ui.history.you_rewrote": ("You rewrote history: your spacecraft reached orbit.", "你改寫了歷史：你的衛星成功進入軌道。"),
    "ui.history.still_failed": ("History repeated itself. Look at the launcher.", "歷史重演了。看看火箭的選擇。"),

    # 設計室
    "ui.cat.instrument": ("SCIENCE INSTRUMENTS", "科學儀器"),
    "ui.cat.solar": ("SOLAR ARRAY", "太陽能板"),
    "ui.cat.battery": ("BATTERY", "電池"),
    "ui.cat.antenna": ("ANTENNA", "天線"),
    "ui.cat.groundstation": ("GROUND STATIONS", "地面站"),
    "ui.cat.computer": ("COMPUTER & RECORDER", "電腦與記錄器"),
    "ui.cat.adcs": ("ATTITUDE CONTROL", "姿態控制"),
    "ui.cat.propulsion": ("PROPULSION", "推進系統"),
    "ui.cat.shielding": ("RADIATION SHIELDING", "輻射屏蔽"),
    "ui.cat.orbit": ("ORBIT", "軌道"),
    "ui.cat.launcher": ("LAUNCH VEHICLE", "運載火箭"),
    "ui.cat.years": ("MISSION LENGTH", "任務年限"),
    "ui.design.instrument_hint": ("up to {max}", "最多 {max} 台"),
    "ui.design.instrument_full": ("Instrument slots are full. Remove one first.", "儀器位置滿了，先移除一台。"),
    "ui.design.dept_requirement": ("YOUR DEPARTMENT WANTS", "本部門要求"),
    "ui.design.resources": ("RESOURCES", "資源"),
    "ui.design.warnings": ("FLIGHT RULES", "飛行規則檢查"),
    "ui.design.all_clear": ("All flight rules satisfied. Ready for the Go/No-Go poll.", "所有飛行規則都通過了，可以召開 Go/No-Go 點名。"),
    "ui.design.lights_only": ("{n} red light(s). Director difficulty: find the cause yourself.", "{n} 個紅燈。總監難度：原因要自己找。"),
    "ui.design.call_poll": ("CALL GO / NO-GO POLL", "召開 GO / NO-GO 點名"),
    "ui.design.briefing": ("Briefing", "簡報"),
    "ui.design.years_hint": ("Longer missions return more science but cost more to operate and wear the spacecraft.",
                             "任務越長科學成果越多，但營運成本更高，太空船也更耗損。"),
    "ui.design.historical": ("HISTORICAL", "歷史機型"),
    "ui.design.cannot_reach": ("cannot reach this orbit", "到不了這個軌道"),
    "ui.design.sunlit_only": ("Only works in sunlight: it is idle during eclipses.", "只能在日照下工作：進入地球影子時停機。"),
    "ui.design.source": ("Source:", "資料來源："),
    "ui.design.locked_title": ("HISTORY REPLAY", "重演歷史"),
    "ui.design.locked_desc": ("This is the design as it was flown. Call the poll to relive the launch.", "這是當年實際飛行的設計。召開點名，重新經歷那次發射。"),
    "ui.design.locked_toast": ("The design is locked during the history replay.", "重演歷史時設計是鎖住的。"),
    "ui.design.rewrite_title": ("REWRITE HISTORY", "改寫歷史"),
    "ui.design.rewrite_desc": ("Now you know what went wrong. Change anything you like and fly again.", "現在你知道問題出在哪了。任意修改設計，再飛一次。"),
    "ui.pill.budget_left": ("Budget left", "剩餘預算"),
    "ui.pill.lifetime": ("Lifetime", "預估壽命"),
    "ui.pill.science": ("Science/yr", "科學/年"),
    "ui.pill.mass": ("Launch mass", "發射質量"),
    "ui.contrib.bus": ("Spacecraft bus", "衛星平台"),
    "ui.contrib.structure": ("Structure & harness", "結構與線材"),
    "ui.contrib.propellant": ("Propellant", "推進劑"),
    "ui.contrib.operations": ("Mission operations", "任務營運"),
    "ui.contrib.integration": ("Integration & test", "整合與測試"),

    # 地面訓練
    "ui.training.title": ("GROUND TRAINING", "地面訓練"),
    "ui.training.brief": ("Read the department requirements on the right: they are the private goals of your four teammates. Then enter the design room.",
                          "右邊是四個部門的要求，也就是四位隊友各自的目標。看完就進入設計室。"),
    "ui.training.step1": ("These six bars are your budget of physics: mass, power, money, storage, downlink and schedule. Green is fine, amber is tight, red will get a NO-GO.",
                          "右邊六條是你的物理預算：質量、電力、金錢、儲存、下傳與時程。綠色沒問題、橘色很緊、紅色在點名時會被喊 NO-GO。"),
    "ui.training.step2": ("Hover over any part on the left: a white marker shows where each bar would move before you commit. Click to install it.",
                          "把滑鼠移到左邊的零件上：白色標記會先告訴你每條資源條會變到哪裡。點一下才會裝上去。"),
    "ui.training.step3": ("The tabs switch between the four departments. Each controls different systems: the scientist picks instruments, comms picks the orbit and antenna.",
                          "上面的分頁切換四個部門。每個部門管不同系統：科學家選儀器，通訊負責軌道與天線。"),
    "ui.training.step4": ("Flight rules explain every red light. The buttons under each one are fixes the computer has verified: hover to preview, click to apply.",
                          "飛行規則會解釋每一個紅燈。下面的按鈕是電腦驗證過的解法：滑鼠移上去預覽，點一下套用。"),
    "ui.training.step5": ("When the bars look healthy, call the Go/No-Go poll. Every department must answer GO before you can launch.",
                          "資源條都健康之後，召開 Go/No-Go 點名。每個部門都回答 GO 才能發射。"),
    "ui.training.next": ("Next", "下一步"),
    "ui.training.done": ("Got it", "了解"),
    "ui.training.skip": ("Skip training", "跳過訓練"),

    # 點名
    "ui.rollcall.eyebrow": ("MISSION CONTROL · FLIGHT DIRECTOR LOOP", "任務控制中心 · 飛行總監頻道"),
    "ui.rollcall.title": ("GO / NO-GO POLL", "GO / NO-GO 點名"),
    "ui.rollcall.desc": ("Each console checks its own systems against the flight rules and calls GO or NO-GO.",
                         "每個席位依飛行規則檢查自己負責的系統，回答 GO 或 NO-GO。"),
    "ui.rollcall.station.director": ("Budget · Schedule", "預算 · 時程"),
    "ui.rollcall.station.engineer": ("Mass · Power · Lifetime", "質量 · 電力 · 壽命"),
    "ui.rollcall.station.scientist": ("Science return", "科學成果"),
    "ui.rollcall.station.comms": ("Storage · Downlink", "儲存 · 下傳"),
    "ui.rollcall.go_line": ("Systems nominal.", "系統正常。"),
    "ui.rollcall.req_unmet": ("GO, but our department goal is not met.", "GO，但本部門的要求沒有達成。"),
    "ui.rollcall.all_go": ("ALL STATIONS GO", "全員 GO"),
    "ui.rollcall.first_call_bonus": ("All GO on the first poll: teamwork bonus.", "第一次點名就全員 GO：團隊加分。"),
    "ui.rollcall.unmet_warning": ("Some department goals are not met. You can still launch, but the teamwork score will suffer.",
                                  "有部門的要求沒達成。還是可以發射，但團隊分數會被扣。"),
    "ui.rollcall.hold": ("HOLD HOLD HOLD", "暫停倒數"),
    "ui.rollcall.hold_desc": ("At least one station called NO-GO. Fix the red lights and poll again.", "至少一個席位喊了 NO-GO。修好紅燈再點名一次。"),
    "ui.rollcall.back": ("Back to design", "回設計室"),
    "ui.rollcall.launch": ("LAUNCH", "發射"),

    # 發射
    "ui.launch.eyebrow": ("LAUNCH", "發射"),
    "ui.launch.alt": ("ALTITUDE", "高度"),
    "ui.launch.vel": ("VELOCITY", "速度"),
    "ui.launch.thr": ("THROTTLE", "推力"),
    "ui.launch.skip": ("Skip launch", "跳過發射"),
    "ui.launch.stage.countdown": ("TERMINAL COUNT", "最後倒數"),
    "ui.launch.stage.ignition": ("IGNITION", "點火"),
    "ui.launch.stage.ascent": ("ASCENT", "爬升"),
    "ui.launch.stage.maxq": ("MAX-Q", "最大動壓"),
    "ui.launch.stage.staging": ("STAGING", "節間分離"),
    "ui.launch.stage.fairing": ("FAIRING SEPARATION", "整流罩分離"),
    "ui.launch.stage.orbit": ("ORBIT INSERTION", "入軌"),
    "ui.launch.stage.deploy": ("SPACECRAFT DEPLOY", "衛星展開"),
    "ui.launch.stage.anomaly": ("ANOMALY", "異常"),
    "ui.launch.call.go": ("Range is green. We are GO for launch.", "靶場淨空，發射 GO。"),
    "ui.launch.call.ignition": ("Ignition sequence start.", "點火程序開始。"),
    "ui.launch.call.liftoff": ("Liftoff!", "升空！"),
    "ui.launch.call.tower": ("Tower cleared.", "脫離發射塔。"),
    "ui.launch.call.pitch": ("Vehicle has started its pitch and roll program.", "火箭開始俯仰與滾轉程序。"),
    "ui.launch.call.maxq": ("Max-Q: maximum aerodynamic pressure.", "通過最大動壓。"),
    "ui.launch.call.meco": ("Main engine cutoff.", "主引擎關機。"),
    "ui.launch.call.stagesep": ("Stage separation confirmed.", "確認節間分離。"),
    "ui.launch.call.fairing": ("Fairing separation.", "整流罩分離。"),
    "ui.launch.call.fairing_fail": ("No fairing separation indication...", "沒有整流罩分離的訊號……"),
    "ui.launch.call.underspeed": ("Vehicle is under speed. It will not make orbit.", "速度不足，無法進入軌道。"),
    "ui.launch.call.anomaly": ("We have lost telemetry. Flight controllers are assessing.", "遙測中斷，飛控人員正在評估。"),
    "ui.launch.call.orbit": ("Engine cutoff. We are in orbit.", "引擎關機，已進入軌道。"),
    "ui.launch.call.lower": ("Insertion is lower than planned, but we are in orbit.", "入軌高度比預期低，但已進入軌道。"),
    "ui.launch.call.deploy": ("Spacecraft separation. Solar arrays deploying.", "衛星分離，太陽能板展開中。"),
    "ui.launch.result.eyebrow": ("LAUNCH RESULT", "發射結果"),
    "ui.launch.result.nominal": ("ORBIT ACHIEVED", "成功入軌"),
    "ui.launch.result.lower": ("LOWER ORBIT", "低軌入軌"),
    "ui.launch.result.fairing": ("FAIRING FAILURE", "整流罩故障"),
    "ui.launch.result.failure": ("LAUNCH FAILURE", "發射失敗"),
    "ui.launch.to_ops": ("BEGIN OPERATIONS", "開始營運"),
    "ui.launch.to_report": ("MISSION REPORT", "任務報告"),

    # 營運
    "ui.ops.eyebrow": ("MISSION ELAPSED TIME", "任務經過時間"),
    "ui.ops.year_of": ("Year {y} of {t}", "第 {y} 年 / 共 {t} 年"),
    "ui.ops.mode.nominal": ("NOMINAL OPERATIONS", "正常運作"),
    "ui.ops.mode.limited": ("POWER-LIMITED", "電力受限"),
    "ui.ops.mode.safe": ("SAFE MODE", "安全模式"),
    "ui.ops.play": ("Play", "播放"),
    "ui.ops.pause": ("Pause", "暫停"),
    "ui.ops.telemetry": ("TELEMETRY", "遙測"),
    "ui.ops.science": ("Science score", "科學分數"),
    "ui.ops.data": ("Data home", "已傳回資料"),
    "ui.ops.duty": ("Instrument duty", "儀器運轉率"),
    "ui.ops.chart.science": ("Science returned", "科學成果累積"),
    "ui.ops.chart.data": ("Data downlinked", "資料下傳累積"),
    "ui.ops.chart.battery": ("Battery charge at end of eclipse", "出影子時的電池電量"),
    "ui.ops.chart.storage": ("Recorder fill", "記錄器使用量"),
    "ui.ops.log": ("EVENT LOG", "事件紀錄"),
    "ui.ops.day_n": ("DAY {d}", "第 {d} 天"),
    "ui.ops.donki_source": ("Event from NASA DONKI (Space Weather Database Of Notifications, Knowledge, Information).",
                            "事件取自 NASA DONKI 太空天氣資料庫。"),
    "ui.ops.skip": ("Skip to the end", "跳到結尾"),
    "ui.ops.to_report": ("MISSION REPORT", "任務報告"),
    "ui.ops.end.eyebrow": ("END OF MISSION", "任務結束"),
    "ui.ops.end.missioncomplete": ("MISSION COMPLETE", "任務圓滿完成"),
    "ui.ops.end.propellant": ("ORBIT DECAYED: OUT OF PROPELLANT", "推進劑耗盡，軌道衰減"),
    "ui.ops.end.battery": ("BATTERY WORN OUT", "電池壽命耗盡"),
    "ui.ops.end.radiation": ("COMPUTER KILLED BY RADIATION", "電腦被輻射損毀"),
    "ui.ops.end.launchfailure": ("LOST AT LAUNCH", "發射時損失"),
    "ui.ops.end.summary": ("{years} years in orbit · {data} TB downlinked · {pct} of the data made it home.",
                           "在軌 {years} 年 · 下傳 {data} TB · {pct} 的資料成功傳回。"),

    # 報告
    "ui.report.title": ("MISSION REPORT", "任務報告"),
    "ui.report.total": ("TOTAL SCORE", "總分"),
    "ui.report.vs_last": ("{d} vs last attempt", "比上次 {d}"),
    "ui.report.grade.s": ("Outstanding: a mission NASA would be proud of.", "傑出：NASA 會引以為傲的任務。"),
    "ui.report.grade.a": ("Excellent mission design.", "優秀的任務設計。"),
    "ui.report.grade.b": ("A solid mission with room to improve.", "穩健的任務，還有進步空間。"),
    "ui.report.grade.c": ("The mission flew, but returned little.", "任務有飛，但成果有限。"),
    "ui.report.grade.fail": ("Mission failure. Read why, then try again.", "任務失敗。看看原因，再試一次。"),
    "ui.report.science": ("Science", "科學"),
    "ui.report.efficiency": ("Efficiency", "效率"),
    "ui.report.lifetime": ("Lifetime", "壽命"),
    "ui.report.teamwork": ("Teamwork", "團隊"),
    "ui.report.requirements": ("DEPARTMENT GOALS", "部門要求"),
    "ui.report.met": ("MET", "達成"),
    "ui.report.unmet": ("NOT MET", "未達成"),
    "ui.report.first_call": ("+ All GO on the first poll", "+ 第一次點名就全員 GO"),
    "ui.report.why": ("WHY: CAUSE AND EFFECT", "為什麼：因果關係"),
    "ui.report.no_causes": ("Nothing notable happened: the design held up everywhere.", "沒有特別的事件：設計各方面都撐住了。"),
    "ui.report.suggestion": ("NEXT TIME, TRY", "下次可以試試"),
    "ui.report.your_mission": ("YOUR MISSION", "你的任務"),
    "ui.report.real_missions": ("REAL NASA MISSIONS", "真實任務對照"),
    "ui.report.f.launch": ("Launch", "發射"),
    "ui.report.f.years": ("In orbit", "在軌時間"),
    "ui.report.f.data": ("Data home", "傳回資料"),
    "ui.report.f.cost": ("Cost", "成本"),
    "ui.report.f.mass": ("Launch mass", "發射質量"),
    "ui.report.redesign": ("REDESIGN & FLY AGAIN", "修改設計再飛一次"),
    "ui.report.rewrite": ("REWRITE HISTORY", "改寫歷史"),
    "ui.report.new_mission": ("New mission", "新任務"),
}


def main():
    root = pathlib.Path(__file__).resolve().parents[2]
    path = root / "Assets/GoNoGo/Data/Resources/GameData/strings.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    # 先移除舊的 ui.*，確保刪掉的鍵不會殘留
    data = {k: v for k, v in data.items() if not k.startswith("ui.")}
    merged = {k: v for k, v in UI.items() if not k.startswith(OBSOLETE_PREFIXES)}
    merged.update(UI3D)
    merged = {k: v for k, v in merged.items() if k not in OBSOLETE_KEYS}
    for k, (en, zh) in merged.items():
        data[k] = {"en": en, "zh-TW": zh}
    lines = ["{"]
    items = list(data.items())
    for i, (k, v) in enumerate(items):
        sep = "," if i < len(items) - 1 else ""
        assert set(v.keys()) == {"en", "zh-TW"}, k
        en = json.dumps(v["en"], ensure_ascii=False)
        zh = json.dumps(v["zh-TW"], ensure_ascii=False)
        lines.append(f'  {json.dumps(k, ensure_ascii=False)}: {{ "en": {en}, "zh-TW": {zh} }}{sep}')
    lines.append("}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    print(f"strings.json: {len(data)} keys ({len(merged)} ui.*)")


if __name__ == "__main__":
    main()
