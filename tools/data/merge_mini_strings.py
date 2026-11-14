#!/usr/bin/env python3
"""把 tools/data/mini/*.json（各小遊戲的字串）合併進 strings.json。

只動 mini.* 的那幾行：已存在的 key 就地換成新內容，沒有的 key 加在檔案最後；其他行逐字不動。
（不要跑 ui_strings.py：它會把 strings.json 約 400 行洗掉。）
用法：python3 tools/data/merge_mini_strings.py
"""
import glob
import json
import os
import re

root = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
target = os.path.join(root, "Assets", "GoNoGo", "Data", "Resources", "GameData", "strings.json")
frag = {}
for f in sorted(glob.glob(os.path.join(root, "tools", "data", "mini", "*.json"))):
    frag.update(json.load(open(f, encoding="utf-8")))

lines = open(target, encoding="utf-8").read().split("\n")
pat = re.compile(r'^\s*"((?:[^"\\]|\\.)*)"\s*:')
index = {}
for i, ln in enumerate(lines):
    m = pat.match(ln)
    if m:
        index[json.loads('"' + m.group(1) + '"')] = i

def render(k, v):
    return "  %s: %s" % (json.dumps(k, ensure_ascii=False), json.dumps(v, ensure_ascii=False).replace('{"en"', '{ "en"').replace('"}', '" }'))

updated = added = 0
new_lines = []
for k, v in frag.items():
    if k in index:
        old = lines[index[k]]
        comma = "," if old.rstrip().endswith(",") else ""
        lines[index[k]] = render(k, v) + comma
        updated += 1
    else:
        new_lines.append(render(k, v))
        added += 1
if new_lines:
    # 最後一個 } 之前加進去；前一行要補逗號
    end = max(i for i, ln in enumerate(lines) if ln.strip() == "}")
    last = max(i for i in range(end) if lines[i].strip())
    if not lines[last].rstrip().endswith(","):
        lines[last] = lines[last].rstrip() + ","
    lines[end:end] = [ln + "," for ln in new_lines[:-1]] + [new_lines[-1]]
text = "\n".join(lines)
json.loads(text)  # 一定要是合法 JSON
open(target, "w", encoding="utf-8").write(text)
print("updated", updated, "added", added)
