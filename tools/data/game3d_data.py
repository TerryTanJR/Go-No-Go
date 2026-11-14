# -*- coding: utf-8 -*-
"""
3D 版的遊戲資料：零件編號與白話功能、處室需求、難度、處室對話、核心字串。
可以重複執行（結果一樣）。用法：python tools/data/game3d_data.py
之後再跑 python tools/data/ui_strings.py 合併介面文字。
"""
import json
import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]
DATA = ROOT / "Assets/GoNoGo/Data/Resources/GameData"


def T(en, zh):
    return {"en": en, "zh-TW": zh}


# ---------------------------------------------------------------- 零件編號與白話功能

PARTS = {
    "inst_visible": ("P-1", T("Takes colour pictures of the ground in daylight.", "白天拍地面的彩色照片")),
    "inst_multispectral": ("P-2", T("Sees several colours of light, including near-infrared, to tell crops, water and soil apart.", "分好幾種顏色的光拍照，分得出作物、水和土壤")),
    "inst_thermal": ("P-3", T("Sees heat: finds fires and hot spots, day or night.", "看得到熱：日夜都能找出火災和高溫點")),
    "inst_hyperspectral": ("P-4", T("Splits light into hundreds of colours to identify materials.", "把光分成上百種顏色，辨認地面是什麼物質")),
    "inst_sar": ("P-5", T("A radar that images the ground through clouds and darkness.", "雷達，穿過雲層和黑夜拍地面")),
    "inst_co2_spectrometer": ("P-6", T("Measures how much carbon dioxide is in the air below.", "量測下方空氣裡的二氧化碳有多少")),
    "solar_body": ("S-1", T("Solar cells stuck on the body: makes power in sunlight.", "貼在機身上的太陽能電池：有陽光就發電")),
    "solar_deployable": ("S-2", T("Fold-out solar wings: more power in sunlight.", "展開式太陽能翼：日照時發更多電")),
    "solar_large": ("S-3", T("A big fold-out solar array for power-hungry spacecraft.", "大型展開太陽能陣列，給很耗電的太空船用")),
    "batt_small": ("B-1", T("Stores power for when the spacecraft is in Earth's shadow.", "儲存電力，衛星進到地球影子裡時用")),
    "batt_medium": ("B-2", T("Stores power for the shadow part of each orbit.", "儲存電力，給每一圈的地影時段用")),
    "batt_large": ("B-3", T("Stores a lot of power for long shadows and heavy loads.", "儲存大量電力，撐長時間地影和大負載")),
    "ant_sband": ("X-1", T("Sends data back to the ground.", "把資料傳回地面")),
    "ant_xband": ("X-2", T("Sends data back to the ground, faster.", "把資料傳回地面，比較快")),
    "ant_xband_hr": ("X-3", T("Sends data back to the ground, very fast.", "把資料傳回地面，非常快")),
    "obc_cubesat": ("C-1", T("The spacecraft's brain: runs it and stores data between ground passes.", "衛星的大腦：控制衛星、在兩次通聯之間儲存資料")),
    "obc_mass_memory": ("C-2", T("The spacecraft's brain with a large data store.", "衛星的大腦，附大容量資料儲存")),
    "obc_radhard": ("C-3", T("A radiation-tough, doubled-up spacecraft brain.", "抗輻射、雙套備援的衛星大腦")),
    "adcs_compact": ("G-1", T("Keeps the spacecraft pointed the right way.", "讓衛星維持正確的指向")),
    "adcs_wheels": ("G-2", T("Spinning wheels and a star camera that point the spacecraft steadily.", "用旋轉的飛輪和星象儀穩定指向")),
    "adcs_precision": ("G-3", T("Very steady, very precise pointing.", "非常穩、非常準的指向控制")),
    "prop_none": ("T-1", T("A cover plate: no engine, no fuel.", "封板：不裝引擎，也沒有燃料")),
    "prop_green": ("T-2", T("A small engine and fuel tank to keep the orbit up.", "小型引擎和燃料箱，用來維持軌道高度")),
    "prop_hydrazine": ("T-3", T("A bigger engine and fuel tank to keep the orbit up for longer.", "較大的引擎和燃料箱，可以維持軌道更久")),
    "shield_std": ("R-1", T("The standard metal box around the electronics.", "包住電子設備的標準金屬外殼")),
    "shield_vault": ("R-2", T("A thicker box that protects electronics from radiation.", "比較厚的外殼，保護電子設備不受輻射")),
    "shield_heavy": ("R-3", T("Heavy armour against radiation.", "很重的防輻射裝甲")),
}


def update_parts():
    path = DATA / "parts.json"
    text = path.read_text(encoding="utf-8")
    for pid, (code, fn) in PARTS.items():
        start = text.index(f'"id": "{pid}"')
        end = text.find('"id": "', start + 5)
        if end < 0:
            end = len(text)
        block = text[start:end]
        # 移除舊的（可重複執行）
        block = re.sub(r'\n\s*"code": "[^"]*",', "", block)
        block = re.sub(r'\n\s*"function": \{[^\n]*\},', "", block)
        indent = re.search(r'\n(\s*)"tier"', block).group(1)
        block = re.sub(r'("tier": \d+,)', lambda m: m.group(1) + f'\n{indent}"code": "{code}",', block, count=1)
        name_line = re.search(r'\n(\s*)"name": \{[^\n]*\},', block)
        fn_json = json.dumps(fn, ensure_ascii=False).replace('{"en"', '{ "en"').replace('"}', '" }')
        block = block[:name_line.end()] + f'\n{indent}"function": {fn_json},' + block[name_line.end():]
        text = text[:start] + block + text[end:]
    path.write_text(text, encoding="utf-8", newline="\n")


# ---------------------------------------------------------------- 處室需求

def R(rid, typ, value, en, zh, group=0, part=None):
    r = {"id": rid, "type": typ, "value": value}
    if group:
        r["conflict_group"] = group
    if part:
        r["part_id"] = part
    r["text"] = T(en, zh)
    return r


REQUIREMENTS = {
    "earth_observation": {
        "mission_lead": [R("eo_years", "lifetime_target", 0, "Plan at least the 3-year target and make it to the end.", "任務至少規劃 3 年，而且要撐到最後。")],
        "budget_office": [R("eo_reserve", "budget_reserve", 0.15, "Keep at least 15% of the budget in reserve.", "預算至少保留 15% 準備金。")],
        "science": [
            R("eo_science", "science_threshold", 60, "Reach a science score of 60 or more.", "科學分數達到 60 分以上。"),
            R("eo_thermal", "instrument_required", 0, "Fire-watch partners need a thermal infrared imager on board.", "野火監測合作單位需要一台熱紅外線成像儀。", group=1, part="inst_thermal"),
        ],
        "engineering": [R("eo_margin", "mass_power_margin", 0, "Keep the required margin on both mass and power.", "質量和電力都要保留規定的餘裕。")],
        "comms": [
            R("eo_return", "data_return", 0.9, "Bring home at least 90% of the data collected.", "至少傳回 90% 收集到的資料。"),
            R("eo_return_high", "data_return", 0.97, "Lose almost nothing: return 97% of the data.", "幾乎不能掉資料：傳回 97% 的資料。", group=2),
        ],
    },
    "lunar_mapping": {
        "mission_lead": [R("lm_years", "lifetime_target", 0, "Map for the full 2 years.", "完整測繪 2 年。")],
        "budget_office": [R("lm_reserve", "budget_reserve", 0.1, "Keep at least 10% of the budget in reserve.", "預算至少保留 10% 準備金。")],
        "science": [
            R("lm_science", "science_threshold", 55, "Reach a science score of 55 or more.", "科學分數達到 55 分以上。"),
            R("lm_multispectral", "instrument_required", 0, "The mineral team needs a multispectral imager.", "礦物研究小組需要一台多光譜成像儀。", group=1, part="inst_multispectral"),
        ],
        "engineering": [R("lm_margin", "mass_power_margin", 0, "Keep the required margin on both mass and power.", "質量和電力都要保留規定的餘裕。")],
        "comms": [
            R("lm_return", "data_return", 0.85, "Return at least 85% of the data across 384,000 km.", "跨越 38 萬公里傳回至少 85% 的資料。"),
            R("lm_return_high", "data_return", 0.95, "Return 95% of the data: the map must have no holes.", "傳回 95% 的資料：地圖不能有破洞。", group=2),
        ],
    },
    "hist_oco": {
        "mission_lead": [R("oco_years", "lifetime_target", 0, "Operate for the full 2-year design life.", "完整運作 2 年設計壽命。")],
        "budget_office": [R("oco_budget", "budget_reserve", 0.0, "Stay within 110% of the original budget.", "不超過原始預算的 110%。")],
        "science": [
            R("oco_co2", "instrument_required", 0, "Keep the CO2 spectrometer: it is the whole point.", "保留二氧化碳光譜儀：這就是任務的意義。", part="inst_co2_spectrometer"),
            R("oco_science", "science_threshold", 50, "Reach a science score of 50 or more.", "科學分數達到 50 分以上。", group=1),
        ],
        "engineering": [R("oco_margin", "mass_power_margin", 0, "Keep the required margin on both mass and power.", "質量和電力都要保留規定的餘裕。")],
        "comms": [
            R("oco_return", "data_return", 0.95, "Return 95% of the carbon measurements.", "傳回 95% 的碳量測資料。"),
            R("oco_return_high", "data_return", 0.98, "Climate models need 98% of the soundings.", "氣候模型需要 98% 的量測資料。", group=2),
        ],
    },
}


def update_scenarios():
    path = DATA / "scenarios.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    for s in data:
        s["requirements"] = REQUIREMENTS[s["id"]]
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


# ---------------------------------------------------------------- 難度

def D(did, en, zh, den, dzh, **kw):
    d = {"id": did, "name": T(en, zh), "description": T(den, dzh)}
    d.update(kw)
    return d


DIFFICULTY = [
    D("engineer", "Engineer", "工程師",
      "The dashboard shows only numbers. Calls are written down word for word. You diagnose events yourself, 3 minutes a turn.",
      "儀表板只有數字，電話內容逐字記下。營運事件要自己判讀，每回合 3 分鐘。",
      budget_multiplier=1.0, required_margin=0.15, tiers_per_category=3, dashboard_colors=False, notes_mode="transcript",
      requirement_conflicts=1, failure_detail="chain", free_first_failure=False, event_reveal=False, turns=6,
      event_types=["solar_storm", "part_failure", "science_opportunity"], turn_seconds=180, guaranteed_launch=False),
    D("cadet", "Cadet", "學員",
      "Bigger budget, fewer parts, a coloured dashboard and a launch checklist. Events are named for you; the first failed launch is free.",
      "預算多一半、零件少一些，儀表板有紅黃綠，電話內容自動整理成發射條件清單。營運事件直接告訴你名稱，第一次發射失敗不扣分。",
      budget_multiplier=1.5, required_margin=0.0, tiers_per_category=2, dashboard_colors=True, notes_mode="checklist",
      requirement_conflicts=0, failure_detail="advice", free_first_failure=True, event_reveal=True, turns=6,
      event_types=["solar_storm", "science_opportunity"], turn_seconds=0, guaranteed_launch=False),
    D("director", "Mission Director", "任務主管",
      "Tight budget and conflicting demands. Nothing is written down for you, failures are only named, and events look alike. 90 seconds a turn.",
      "預算更緊、處室需求互相衝突。電話內容不會自動記錄，失敗只說大類，事件症狀相似，每回合 90 秒。",
      budget_multiplier=0.8, required_margin=0.25, tiers_per_category=3, dashboard_colors=False, notes_mode="none",
      requirement_conflicts=2, failure_detail="category", free_first_failure=False, event_reveal=False, turns=6,
      event_types=["solar_storm", "part_failure", "science_opportunity", "power_anomaly"], turn_seconds=90, guaranteed_launch=False),
    D("tutorial", "Training", "新手訓練",
      "Guided training mission.", "引導式訓練任務。",
      hidden=True, budget_multiplier=1.5, required_margin=0.0, tiers_per_category=2, dashboard_colors=True, notes_mode="checklist",
      requirement_conflicts=0, failure_detail="advice", free_first_failure=True, event_reveal=True, turns=1,
      event_types=["power_anomaly"], turn_seconds=0, guaranteed_launch=True),
]


# ---------------------------------------------------------------- 處室對話

def L(lid, q_en, q_zh, a_en, a_zh, reveals=None, none_en="", none_zh=""):
    line = {"id": lid, "question": T(q_en, q_zh), "answer": T(a_en, a_zh)}
    if none_en:
        line["answer_none"] = T(none_en, none_zh)
    if reveals:
        line["reveals"] = reveals
    return line


DEPARTMENTS = [
    {
        "id": "mission_lead",
        "name": T("Mission Lead", "任務主管"),
        "contact": T("Avery Lin, Mission Lead", "林艾芙・任務主管"),
        "greeting": T("Mission Lead's office. What do you need to confirm?", "這裡是任務主管辦公室。有什麼要確認的？"),
        "color": "#39C6F4",
        "lines": [
            L("goal", "What is this mission for?", "這次任務的目標是什麼？", "{briefing}", "{briefing}"),
            L("lifetime", "How long does it have to last?", "任務要撐多久？",
              "It has to work for {years} years, all the way to the end.", "這顆衛星要運作 {years} 年，而且要撐到最後。",
              reveals=["lifetime_target"]),
            L("deadline", "When do we have to launch?", "什麼時候要發射？",
              "We launch within {deadline} months. Parts and rockets have lead times, so slow deliveries can make us miss the window.",
              "{deadline} 個月內一定要發射。零件和火箭都有交期，選了太慢交貨的會趕不上。"),
        ],
    },
    {
        "id": "budget_office",
        "name": T("Budget Office", "預算單位"),
        "contact": T("Jordan Park, Budget Office", "朴喬登・預算單位"),
        "greeting": T("Budget Office. Make it quick, the auditors are here.", "預算單位。請長話短說，稽核的人在這。"),
        "color": "#FFB547",
        "lines": [
            L("budget", "What is the budget?", "預算上限多少？",
              "The total budget is {budget}: development, the rocket and every year of operations.",
              "總預算是 {budget}，包含開發、火箭，還有每一年的營運費。"),
            L("reserve", "How much do we have to keep in reserve?", "要留多少準備金？",
              "This is not negotiable.", "這條沒得商量。", reveals=["budget_reserve"],
              none_en="No fixed rule, but if we overspend the mission gets cancelled.", none_zh="沒有硬性規定，但超支的話任務會被砍。"),
            L("costs", "What costs the most?", "哪裡最花錢？",
              "The rocket is usually the biggest bill. Ground stations charge rent every year, so long missions add up. Big instruments like hyperspectral imagers and radar are expensive too.",
              "火箭通常是最大的一筆。地面站每年都要付租金，任務越長越貴。高光譜和雷達這類大儀器也很貴。"),
        ],
    },
    {
        "id": "science",
        "name": T("Science Team", "科學團隊"),
        "contact": T("Dr. Sam Okafor, Science Team", "歐卡佛博士・科學團隊"),
        "greeting": T("Science Team here! Tell me you are adding instruments.", "科學團隊！你是要告訴我們可以多裝儀器嗎？"),
        "color": "#3DDC84",
        "lines": [
            L("target", "What are we observing?", "最重要的觀測目標是什麼？",
              "Our objective is {objective}. And the science has to be worth it:", "我們的目標是{objective}。科學成果要夠：",
              reveals=["science_threshold"],
              none_en="Our objective is {objective}. The more good data, the better.", none_zh="我們的目標是{objective}，好資料越多越好。"),
            L("instrument", "Is there an instrument we must fly?", "有指定一定要裝的儀器嗎？",
              "Yes:", "有：", reveals=["instrument_required"],
              none_en="Nothing specific, but the instruments must match the objective or the data is useless. You can fit up to {max_instruments}.",
              none_zh="沒有指定，但儀器要和任務目標對得上，不然拍了也沒用。最多可以裝 {max_instruments} 台。"),
            L("orbit", "Which orbit is best?", "哪種軌道最適合？",
              "A sun-synchronous orbit passes each place at the same local time every day, so the light is consistent. Geostationary is too far away for sharp pictures. For the Moon, low lunar orbit is the only option.",
              "太陽同步軌道每天在同一個當地時間經過，光線一致，最適合拍地面；地球同步軌道太遠，拍不清楚。月球任務只能用低月球軌道。"),
        ],
    },
    {
        "id": "engineering",
        "name": T("Engineering", "工程部"),
        "contact": T("Riley Chen, Engineering", "陳睿理・工程部"),
        "greeting": T("Engineering. Is the spacecraft on fire?", "工程部。衛星沒著火吧？"),
        "color": "#FF4D5E",
        "lines": [
            L("margin", "How much margin do we need?", "設計餘裕要留多少？",
              "Keep {margin_pct}% margin on both mass and power.", "質量和電力都要留 {margin_pct}% 的餘裕。",
              reveals=["mass_power_margin"],
              none_en="Leave some margin. If the rocket is loaded to the limit we might end up in a lower orbit.",
              none_zh="盡量留一些餘裕。火箭運力抓太緊，可能會進到比較低的軌道。"),
            L("battery", "How big does the battery have to be?", "電池要多大？",
              "The battery has to carry everything through the longest shadow. In the orbit you reported, each shadow lasts about {eclipse_min} minutes. If the battery can't make it, the spacecraft dies in its first shadow.",
              "電池要撐過最長的地影。以目前回報的軌道，每圈地影大約 {eclipse_min} 分鐘，這段時間所有東西都靠電池。電池撐不過，衛星第一次進地影就會斷電。"),
            L("lifetime", "What makes a spacecraft die early?", "什麼會讓衛星提早壞掉？",
              "Running out of propellant drops it out of orbit, worn-out batteries lose power, and radiation beyond the computer's tolerance crashes it. Higher orbits get more radiation.",
              "推進劑用完會掉出軌道，電池循環用完會失去電力，輻射超過電腦耐受度就會當機。軌道越高輻射越強。"),
        ],
    },
    {
        "id": "comms",
        "name": T("Comms & Ground", "通訊與地面站"),
        "contact": T("Morgan Yu, Comms & Ground", "余摩根・通訊與地面站"),
        "greeting": T("Comms and Ground, go ahead.", "通訊與地面站，請講。"),
        "color": "#9E8CFF",
        "lines": [
            L("return", "How much data has to come home?", "資料要傳回多少？",
              "Here is what the users signed up for:", "使用者要求的是：", reveals=["data_return"]),
            L("stations", "How do we choose ground stations?", "地面站怎麼選？",
              "More stations mean more time to download each day, but each one charges rent every year. Sun-synchronous orbits pass over the poles, so polar stations help a lot. You pick stations on the control room console.",
              "地面站越多，每天能傳資料的時間越長，但每座每年都要租金。太陽同步軌道會經過極區，極區站特別有用。地面站在中控室的控制台選。"),
            L("storage", "How much storage do we need?", "儲存空間要多大？",
              "Everything collected between two passes waits in the computer. Storage must hold a day's data times the longest gap, plus 50% safety.",
              "兩次通聯之間收集的資料都要先存在電腦裡。儲存至少要裝得下『每天的資料量 × 最長的空檔』，再多留 50% 保險。"),
        ],
    },
]


# ---------------------------------------------------------------- 核心字串（非 ui.*）

CORE_STRINGS = {
    "launch.dead_on_orbit": T("The spacecraft reached orbit and deployed, then lost power in its first pass through Earth's shadow.",
                              "太空船順利入軌、展開，卻在第一次進入地影時斷電失聯。"),
    "cause.battery_capacity": T("The battery holds {wh} Wh ({part}).", "電池只有 {wh} Wh（{part}）。"),
    "cause.eclipse_need": T("Each {min}-minute shadow needs about {need} Wh.", "每次 {min} 分鐘的地影需要大約 {need} Wh。"),
    "cause.dead_on_orbit": T("The battery ran flat in the first shadow and the spacecraft went silent.", "電池在第一次地影就用光，衛星失聯。"),
    "cause.power_anomaly": T("A power anomaly drained the battery.", "電量異常讓電池快速放電。"),
    "cause.not_handled": T("Only {done} of {total} procedure steps were done.", "處置手冊的 {total} 個步驟只做了 {done} 個。"),
    "cause.kept_running": T("The storm procedure was not finished.", "太陽風暴的處置沒有做完。"),
    "suggest.battery_eclipse": T("Ask Engineering how long the shadow is, then pick a battery that lasts through it.", "問工程部地影有多長，再選撐得過的電池。"),
    "suggest.power_procedure": T("When the battery drops fast with normal radiation, run the power-anomaly procedure.", "電量掉得快、輻射卻正常時，照電量異常的處置手冊操作。"),
    "suggest.storm_procedure": T("When radiation spikes, shut the instruments, go to safe mode and point at the Sun.", "輻射暴增時，關閉儀器、切換安全模式、太陽能板對日。"),
    "event.storm.safe": T("Procedure complete: the spacecraft rode out the storm in safe mode.", "處置完成：太空船在安全模式下撐過風暴。"),
    "event.storm.ok": T("The storm passed without damage, but the procedure was not finished.", "風暴過去了，沒有損壞，但處置沒有做完。"),
    "event.storm.damaged": T("The storm damaged an instrument.", "風暴損壞了一台儀器。"),
    "event.power.handled": T("Procedure complete: load shed and battery protected.", "處置完成：降低負載，電池受到保護。"),
    "event.power.drained": T("The battery was drained deeply and lost part of its life.", "電池被深度放電，壽命減少。"),
    "event.failure.instrument_contained": T("The failed instrument was isolated and partly recovered.", "故障的儀器被隔離，部分功能恢復。"),
    "event.failure.computer_contained": T("The computer was rebooted on backup; most storage recovered.", "電腦切換備援重新開機，大部分儲存空間恢復。"),
    "event.failure.adcs_contained": T("Attitude control was reset; pointing mostly recovered.", "姿態控制重新設定，指向大致恢復。"),
    "event.failure.antenna_contained": T("The radio was switched to backup mode; most of the downlink recovered.", "通訊切換到備援模式，大部分下傳速率恢復。"),
    "event.title.solar_storm": T("Solar storm", "太陽風暴"),
    "event.title.part_failure": T("Part failure", "零件故障"),
    "event.title.science_opportunity": T("Science opportunity", "科學機會"),
    "event.title.power_anomaly": T("Power anomaly", "電量異常"),
    "event.diag.solar_storm": T("Radiation dose rate and proton flux jump; solar array output dips a little. Compare with past DONKI storm records.",
                                "輻射劑量率和質子通量暴增，太陽能板輸出略降。可以對照 DONKI 過去的太陽風暴紀錄。"),
    "event.diag.part_failure": T("One part reads FAULT, NO SIGNAL, ERR or DEGRADED while everything else looks normal.",
                                 "某個零件的讀數顯示 FAULT、NO SIGNAL、ERR 或 DEGRADED，其他都正常。"),
    "event.diag.science_opportunity": T("The payload console shows a ground alert (wildfire, volcano, flood or hurricane). All other readings are normal.",
                                        "酬載台出現地面通報（野火、火山、洪水或颶風），其他讀數都正常。"),
    "event.diag.power_anomaly": T("Battery charge falls fast and solar output halves, but radiation is normal.",
                                  "電池電量快速下降、太陽能板輸出掉到一半，但輻射正常。"),
    "ops.console.payload": T("Payload console", "酬載台"),
    "ops.console.power": T("Power console", "電力台"),
    "ops.console.attitude": T("Attitude console", "姿態台"),
    "ops.console.comms": T("Comms console", "通訊台"),
    "ops.action.instruments_off": T("Instruments off", "關閉儀器"),
    "ops.action.high_res_mode": T("High-resolution mode", "高解析模式"),
    "ops.action.reschedule": T("Reschedule observations", "重新排程觀測"),
    "ops.action.isolate_part": T("Isolate faulty part", "隔離故障零件"),
    "ops.action.battery_protect": T("Battery protection mode", "電池保護模式"),
    "ops.action.safe_mode": T("Safe mode", "切換安全模式"),
    "ops.action.sun_point": T("Point arrays at the Sun", "太陽能板對日"),
    "ops.action.point_target": T("Point at target", "指向目標"),
    "ops.action.priority_downlink": T("Priority downlink", "優先下傳"),
    "ops.action.switch_backup": T("Switch to backup / reboot", "切換備援／重新開機"),
    "ops.read.battery": T("Battery charge", "電池電量"),
    "ops.read.array": T("Solar array output", "太陽能板輸出"),
    "ops.read.load": T("Bus load", "負載"),
    "ops.read.bus": T("Power bus", "電源匯流排"),
    "ops.read.temp": T("Payload temperature", "酬載溫度"),
    "ops.read.ground_alert": T("Ground alert", "地面通報"),
    "ops.read.pointing": T("Pointing error", "指向誤差"),
    "ops.read.dose_rate": T("Radiation dose rate", "輻射劑量率"),
    "ops.read.proton_flux": T("Proton flux", "質子通量"),
    "ops.read.wheels": T("Reaction wheels", "反應輪"),
    "ops.read.storage": T("Storage used", "儲存使用量"),
    "ops.read.link": T("Radio link", "通訊鏈路"),
    "ops.read.downlink": T("Downlink rate", "下傳速率"),
    "ops.read.memory": T("Memory", "記憶體"),
    "objective.earth_observation": T("watching Earth's land: crops, fires and water", "觀測地表：農作物、火災和水資源"),
    "objective.lunar_mapping": T("mapping the Moon's surface and minerals", "測繪月球表面與礦物"),
    "objective.carbon_monitoring": T("measuring carbon dioxide in the air around the world", "量測全球大氣中的二氧化碳"),
    "err.phase": T("Not now.", "現在不能這樣做。"),
    "err.locked": T("This is the original 2009 design: launch it first to see what happened.", "這是 2009 年的原始設計：先發射，看看當年發生了什麼。"),
    "err.not_allowed": T("That option isn't available for this mission.", "這個任務不能選這個。"),
    "err.hands_full": T("Your hands are full. Install or put down what you are holding (C).", "手上已經有東西了。先安裝，或按 C 放下。"),
    "err.nothing_held": T("You aren't holding anything.", "手上沒有零件。"),
    "err.taken": T("Someone else is holding that part.", "這個零件在別人手上。"),
    "err.instruments_full": T("No room for another instrument. Remove one first.", "儀器已經裝滿了，先拆一台下來。"),
    "err.room_full": T("The room is full (4 players).", "房間已滿（4 人）。"),
    "err.no_player": T("Player not found.", "找不到這位玩家。"),
    "err.unknown": T("That didn't work.", "操作無效。"),
}

REMOVED_PREFIXES = ("nogo.", "choice.", "event.prompt.")
REMOVED_KEYS = {"event.title.solarstorm", "event.title.partfailure", "event.title.scienceopportunity", "suggest.safe_mode"}


def write_strings_core():
    path = DATA / "strings.json"
    data = json.loads(path.read_text(encoding="utf-8"))
    data = {k: v for k, v in data.items() if not k.startswith(REMOVED_PREFIXES) and k not in REMOVED_KEYS}
    data.update(CORE_STRINGS)
    lines = ["{"]
    items = list(data.items())
    for i, (k, v) in enumerate(items):
        sep = "," if i < len(items) - 1 else ""
        en = json.dumps(v["en"], ensure_ascii=False)
        zh = json.dumps(v["zh-TW"], ensure_ascii=False)
        lines.append(f'  {json.dumps(k, ensure_ascii=False)}: {{ "en": {en}, "zh-TW": {zh} }}{sep}')
    lines.append("}")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def main():
    update_parts()
    update_scenarios()
    (DATA / "difficulty.json").write_text(json.dumps(DIFFICULTY, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    (DATA / "departments.json").write_text(json.dumps(DEPARTMENTS, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    write_strings_core()
    print("game data updated")


if __name__ == "__main__":
    main()
