"""追蹤鎖定（科學機會）的美術。blender -b -P tools/minigame_art/track_lock.py"""
import os, sys, math, random
sys.path.insert(0, os.path.dirname(__file__))
from lib import *

G = "track_lock"

# ---- 地面：俯瞰地圖（田地、道路、河、小房子）。1600x900，上下可接（週期 9：每個東西在 y±9 各複製一份）
reset()
rnd = random.Random(7)
rect(0, 0, 16, 9, 0x142A1E, z=0)
fields = [0x1F3D2A, 0x2A4A2C, 0x3A4A26, 0x4A4426, 0x2A3A30, 0x34503A]
for i in range(30):
    x, y = rnd.uniform(-7.5, 7.5), rnd.uniform(-4.5, 4.5)
    w, h = rnd.uniform(1.4, 2.8), rnd.uniform(1.0, 2.0)
    c = rnd.choice(fields)
    horiz = rnd.random() < 0.5
    for dy in (-9, 0, 9):
        rect(x, y + dy, w + 0.08, h + 0.08, shade(c, 0.6), z=None)
        rect(x, y + dy, w, h, c, z=None)
        n = int((h if horiz else w) / 0.2)
        for k in range(n):
            o = -(h if horiz else w) / 2 + 0.1 + k * 0.2
            if horiz:
                line(x - w / 2 + 0.03, y + dy + o, x + w / 2 - 0.03, y + dy + o, 0.05, shade(c, 0.82), z=None)
            else:
                line(x + o, y + dy - h / 2 + 0.03, x + o, y + dy + h / 2 - 0.03, 0.05, shade(c, 0.82), z=None)
# 道路（直線，週期內接合）
for dy in (-9, 0, 9):
    line(3.0, -4.5 + dy, 3.0, 4.5 + dy, 0.3, 0x3A322A, z=None)
    line(3.0, -4.5 + dy, 3.0, 4.5 + dy, 0.22, 0x8A7C62, z=None)
    line(3.0, -4.5 + dy, 3.0, 4.5 + dy, 0.03, 0xD8C890, z=None)
for yy in (-2.0, 2.6):
    line(-8, yy, 8, yy, 0.3, 0x3A322A, z=None)
    line(-8, yy, 8, yy, 0.22, 0x8A7C62, z=None)
# 河
for dy in (-9, 0, 9):
    pts = [(-4.6 + 0.5 * math.sin(((t - dy) / 9.0 + 0.5) * 2 * math.pi), t) for t in [-4.5 + dy + 9 * k / 30 for k in range(31)]]
    for hw, c in ((0.5, 0x1B4A74), (0.4, 0x2C6FA8), (0.07, 0x6FB4E0)):
        for (xa, ya), (xb, yb) in zip(pts, pts[1:]):
            line(xa, ya, xb, yb, hw * 2, c, z=None)
            circle(xb, yb, hw, c, z=None, seg=16)
# 橋（道路過河處）
for dy in (-9, 0, 9):
    rect(-4.6 + 0.5 * math.sin((( 2.6 - 0 ) / 9.0 + 0.5) * 2 * math.pi), 2.6 + dy, 1.3, 0.34, 0x8A7C62, z=None)
# 小房子與聚落
for (bx, by) in [(4.4, 3.4), (5.6, 3.0), (4.8, 1.2), (6.4, 0.2), (-1.4, 3.4), (-2.2, 0.4), (-1.0, -3.2), (6.0, -3.4), (1.2, -0.6)]:
    for dy in (-9, 0, 9):
        rect(bx + 0.05, by + dy - 0.05, 0.5, 0.4, 0x0A1018, z=None)
        rect(bx, by + dy, 0.46, 0.36, rnd.choice([0xB0A090, 0x8A8F9A, 0xA8685A]), z=None)
        rect(bx, by + dy + 0.08, 0.46, 0.14, 0x5C6F82, z=None)
render("ground", G, (1600, 900), 16.0)

# ---- 野火：火焰（尖端朝上）
reset()
ellipse(0, -0.55, 0.85, 0.28, 0x2A0D0D, z=0)                      # 焦黑地
ellipse(0, -0.5, 0.7, 0.2, shade(RED, 0.5), z=1)
def flame(cx, w, h, base, c1, c2, z):
    pts = [(cx - w, base), (cx - w * 0.9, base + h * 0.4), (cx - w * 0.2, base + h * 0.7), (cx, base + h),
           (cx + w * 0.25, base + h * 0.65), (cx + w * 0.9, base + h * 0.35), (cx + w, base)]
    poly(pts, c1, z=z)
    poly([(cx - w * 0.6, base), (cx - w * 0.2, base + h * 0.45), (cx, base + h * 0.75), (cx + w * 0.3, base + h * 0.4), (cx + w * 0.6, base)], c2, z=z + 0.1)
flame(-0.4, 0.28, 0.9, -0.5, shade(RED, 0.8), RED, 2)
flame(0.42, 0.26, 0.8, -0.5, shade(RED, 0.8), RED, 2)
flame(0.0, 0.42, 1.4, -0.5, RED, AMBER, 3)
flame(0.0, 0.24, 0.8, -0.5, AMBER, 0xFFF2B0, 4)
render("target_fire", G, 256)

# ---- 火山噴發
reset()
ellipse(0, -0.8, 0.95, 0.18, 0x1A120E, z=0)
poly([(-0.9, -0.8), (-0.2, 0.05), (0.2, 0.05), (0.9, -0.8)], shade(0x6B4A3A, 0.6), z=1)
poly([(-0.78, -0.78), (-0.17, 0.0), (0.17, 0.0), (0.78, -0.78)], 0x6B4A3A, z=2)
poly([(-0.2, 0.03), (0.0, -0.12), (0.2, 0.03), (0.0, 0.1)], shade(RED, 0.7), z=3)
poly([(-0.16, 0.0), (-0.04, -0.4), (0.06, -0.1), (0.12, -0.5), (0.2, -0.8), (-0.3, -0.8)], RED, z=3.5)
for (x, y, r, c) in [(0.0, 0.5, 0.3, 0x4A4A55), (-0.28, 0.72, 0.26, 0x5C6F82), (0.26, 0.78, 0.28, 0x4A4A55), (0.0, 0.95, 0.24, 0x7A8796)]:
    circle(x, y, r, shade(c, 0.7), z=4); circle(x - 0.02, y + 0.02, r * 0.85, c, z=4.1)
for (x, y, r) in [(-0.45, 0.35, 0.07), (0.48, 0.3, 0.08), (-0.2, 0.2, 0.06), (0.25, 0.22, 0.06)]:
    circle(x, y, r, AMBER, z=5); circle(x, y, r * 0.5, 0xFFF2B0, z=5.1)
render("target_volcano", G, 256)

# ---- 洪水：水域
reset()
blob = [(-0.9, 0.05), (-0.75, 0.4), (-0.3, 0.55), (0.1, 0.45), (0.55, 0.62), (0.9, 0.25), (0.85, -0.2), (0.5, -0.5), (0.0, -0.6), (-0.5, -0.5), (-0.85, -0.3)]
poly([(x * 1.08, y * 1.1) for x, y in blob], shade(0x2D7FD6, 0.45), z=0)
poly(blob, 0x2D7FD6, z=1)
poly([(x * 0.7, y * 0.7) for x, y in blob], shade(0x2D7FD6, 1.25), z=2)
for (cx, cy, w) in [(-0.3, 0.15, 0.35), (0.25, -0.1, 0.45), (0.1, 0.35, 0.25), (-0.4, -0.25, 0.3)]:
    for k in (0, 1):
        ring(cx, cy, w * (0.5 + 0.4 * k), 0.035, 0xBFEFFF, z=3 + k)
poly([(-0.55, 0.35), (-0.3, 0.45), (-0.4, 0.38)], WHITE, z=5)
render("target_flood", G, 256)

# ---- 颶風：旋渦（對稱，遊戲用 rot 旋轉）
reset()
circle(0, 0, 0.98, 0x1B2B44, z=0)
for arm in range(3):
    for i in range(24):
        t = i / 23.0
        a = arm * 2.0944 + t * 3.6
        r = 0.14 + 0.6 * t
        circle(r * math.cos(a), r * math.sin(a), 0.07 + 0.11 * t, shade(TEXT, 0.75 + 0.25 * t), z=1 + t)
for arm in range(3):
    for i in range(24):
        t = i / 23.0
        a = arm * 2.0944 + t * 3.6 + 0.14
        r = 0.14 + 0.6 * t
        circle(r * math.cos(a), r * math.sin(a), 0.035 + 0.03 * t, WHITE, z=3 + t)
circle(0, 0, 0.17, shade(CYAN, 0.35), z=5)
circle(0, 0, 0.11, 0x0A1018, z=5.1)
render("target_hurricane", G, 256)

# ---- 雲團（2:1，白色，可用 tint 與 alpha）
reset()
puffs = [(-0.55, -0.1, 0.4), (-0.15, 0.1, 0.5), (0.35, 0.05, 0.45), (0.65, -0.12, 0.3), (0.05, -0.2, 0.4), (-0.35, -0.22, 0.28)]
for (x, y, r) in puffs:
    ellipse(x, y - 0.04, r * 1.05, r * 0.95, 0xB8C6D6, z=0)
for (x, y, r) in puffs:
    ellipse(x, y, r, r * 0.9, 0xF0F5FA, z=1)
for (x, y, r) in puffs[:3]:
    ellipse(x - r * 0.25, y + r * 0.3, r * 0.4, r * 0.22, WHITE, z=2)
render("cloud", G, (256, 128))
