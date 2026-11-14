"""Refresh Assets/GoNoGo/Data/Resources/GameData/donki_events.json from NASA DONKI.

Usage:  python tools/data/fetch_donki.py [--key YOUR_API_KEY] [--start 2015-01-01] [--end 2025-12-31]

Keeps only notable events (geomagnetic storms with Kp >= 7, flares of class X5 or larger)
and maps them to the game's 1-3 severity scale:
    GST: Kp 9 -> 3, Kp 8 -> 2, Kp 7 -> 1
    FLR: X9+ -> 3, X5-X9 -> 2
The zh-TW summary is generated from a template; edit by hand if you want nicer wording.
"""
import argparse
import json
import pathlib
import sys
import urllib.request

OUT = pathlib.Path(__file__).resolve().parents[2] / "Assets/GoNoGo/Data/Resources/GameData/donki_events.json"


def get(kind, start, end, key):
    url = f"https://api.nasa.gov/DONKI/{kind}?startDate={start}&endDate={end}&api_key={key}"
    with urllib.request.urlopen(url, timeout=60) as r:
        return json.load(r)


def flare_size(cls):
    if not cls or cls[0] != "X":
        return 0.0
    try:
        return float(cls[1:])
    except ValueError:
        return 0.0


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--key", default="DEMO_KEY")
    ap.add_argument("--start", default="2015-01-01")
    ap.add_argument("--end", default="2025-12-31")
    a = ap.parse_args()

    events = []
    for g in get("GST", a.start, a.end, a.key):
        kp = max((k.get("kpIndex", 0) for k in g.get("allKpIndex", [])), default=0)
        if kp < 7:
            continue
        sev = 3 if kp >= 9 else 2 if kp >= 8 else 1
        day = g["startTime"][:10]
        events.append({
            "id": g["gstID"], "type": "GST", "date": day, "class_type": f"G{min(5, int(kp) - 4)}",
            "kp_index": kp, "severity": sev, "link": g.get("link", ""),
            "summary": {
                "en": f"Geomagnetic storm on {day}, Kp {kp:g}.",
                "zh-TW": f"{day} 地磁暴，Kp 指數 {kp:g}。",
            },
        })
    for f in get("FLR", a.start, a.end, a.key):
        size = flare_size(f.get("classType", ""))
        if size < 5:
            continue
        sev = 3 if size >= 9 else 2
        day = f["beginTime"][:10]
        events.append({
            "id": f["flrID"], "type": "FLR", "date": day, "class_type": f["classType"],
            "severity": sev, "link": f.get("link", ""),
            "summary": {
                "en": f"Class {f['classType']} solar flare on {day}.",
                "zh-TW": f"{day} {f['classType']} 級太陽閃焰。",
            },
        })
    if not events:
        sys.exit("DONKI returned no notable events; keeping the existing file.")
    events.sort(key=lambda e: e["date"])
    OUT.write_text(json.dumps(events, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"wrote {len(events)} events to {OUT}")


if __name__ == "__main__":
    main()
