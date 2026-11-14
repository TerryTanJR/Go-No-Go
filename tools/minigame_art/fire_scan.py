"""大火爆發（火點資料優先）的美術。blender -b -P tools/minigame_art/fire_scan.py"""
import math, os, sys
sys.path.insert(0, os.path.dirname(__file__))
from lib import *

G = "fire_scan"

# ---- 背景（1600x900）：深色指揮室地板 + 細格線
reset()
rect(0, 0, 16, 9, 0x0B131C, z=0)
for i in range(-8, 9):
    rect(i * 1.0, 0, 0.02, 9, 0x142130, z=1)
for j in range(-4, 5):
    rect(0, j * 1.0, 16, 0.02, 0x142130, z=1)
rect(-6.0, 0, 4.0, 9, 0x101C28, z=2)           # 左邊下傳站的平台
rect(-4.0, 0, 0.06, 9, 0x35D0E8, z=3)
render("bg", G, (1600, 900), 16.0)

# ---- 輸送帶（800x100，每 100px 一個箭頭，可左右接）
reset()
rrect(0, 0, 16, 1.9, 0.0, 0x1A2735, z=0)
rect(0, 0.88, 16, 0.14, 0x2E4258, z=1)
rect(0, -0.88, 16, 0.14, 0x2E4258, z=1)
for i in range(8):
    cx = -7 + i * 2.0
    poly([(cx - 0.35, 0.4), (cx + 0.1, 0.0), (cx - 0.35, -0.4), (cx - 0.15, -0.4), (cx + 0.3, 0.0), (cx - 0.15, 0.4)], 0x30465C, z=2)
render("belt", G, (1600, 200), 16.0)

# ---- 下傳站大天線
reset()
ellipse(0, -0.85, 0.9, 0.16, 0x0B131C, z=0)
rrect(0, -0.78, 1.1, 0.2, 0.06, 0x3A4A5C, z=1)
rect(0, -0.45, 0.2, 0.7, 0x5C6F82, z=2)
ellipse(0, 0.12, 0.88, 0.72, shade(CYAN, 0.4), z=3, rot=0.45)
ellipse(0, 0.14, 0.8, 0.64, 0x2AA4BA, z=4, rot=0.45)
ellipse(-0.1, 0.22, 0.5, 0.34, 0x6FE0F0, z=5, rot=0.45)
ellipse(0.0, 0.1, 0.12, 0.1, shade(CYAN, 0.5), z=6, rot=0.45)
line(0.0, 0.1, 0.55, 0.78, 0.09, shade(CYAN, 0.45), z=7)
circle(0.58, 0.82, 0.12, AMBER, z=8)
render("dish", G, 256)

# ---- 資料卡（128，圓角方卡 + 圖案）
def card(name, rim, body, icon):
    reset()
    rrect(0, -0.04, 1.8, 1.8, 0.28, 0x05080C, z=0)
    rrect(0, 0, 1.8, 1.8, 0.28, rim, z=1)
    rrect(0, 0, 1.58, 1.58, 0.2, body, z=2)
    icon()
    render(name, G, 128)

def flame():
    f = [(0, 0.78), (0.2, 0.4), (0.4, 0.08), (0.48, -0.25), (0.34, -0.5), (0, -0.58)]
    poly(mirror_x([(-x, y) for x, y in reversed(f)]), 0xB52A12, z=3)
    g = [(0, 0.62), (0.15, 0.32), (0.32, 0.05), (0.38, -0.24), (0.26, -0.44), (0, -0.5)]
    poly(mirror_x([(-x, y) for x, y in reversed(g)]), 0xFF5A1F, z=4)
    h = [(0, 0.3), (0.11, 0.05), (0.2, -0.2), (0.12, -0.36), (0, -0.4)]
    poly(mirror_x([(-x, y) for x, y in reversed(h)]), AMBER, z=5)
    poly([(0, -0.02), (0.06, -0.22), (0, -0.34), (-0.06, -0.22)], 0xFFF2B0, z=6)
card("card_flame", RED, 0x2A1218, flame)

def cloud():
    for dx, dy, r in ((-0.3, -0.12, 0.26), (0.0, 0.05, 0.34), (0.3, -0.1, 0.27)):
        ellipse(dx, dy, r, r * 0.9, 0xC4D2E0, z=3)
    rect(0, -0.28, 0.8, 0.22, 0xC4D2E0, z=3)
    ellipse(-0.05, 0.1, 0.16, 0.1, 0xF0F6FC, z=4)
card("card_cloud", 0x7F93A8, 0x1C2836, cloud)

def image():
    rect(0, 0, 1.0, 0.8, 0x9AA8B6, z=3)
    rect(0, 0, 0.88, 0.68, 0x34475A, z=4)
    poly([(-0.44, -0.34), (-0.12, 0.12), (0.12, -0.34)], 0x8DA0B2, z=5)
    poly([(0.0, -0.34), (0.22, -0.02), (0.44, -0.34)], 0xA9BACA, z=5)
    circle(0.26, 0.18, 0.09, 0xD8E2EC, z=5)
card("card_image", 0x8A96A4, 0x1E2733, image)

def noise():
    pts = [(-0.5, 0.0), (-0.36, 0.3), (-0.2, -0.3), (-0.04, 0.34), (0.12, -0.34), (0.28, 0.3), (0.5, 0.0)]
    for a, b in zip(pts, pts[1:]):
        line(a[0], a[1], b[0], b[1], 0.09, 0xA8A8B8, z=3)
    for x, y in ((-0.4, -0.45), (-0.1, 0.5), (0.3, -0.5), (0.5, 0.42), (-0.5, 0.45), (0.0, -0.5)):
        rect(x, y, 0.1, 0.1, 0x7C7C8C, z=3)
card("card_noise", 0x777788, 0x1F1F2A, noise)

def sunset():
    # 夕陽：霧橘色的半圓太陽 + 地平線，造型明顯不是火焰
    ellipse(0, -0.1, 0.46, 0.46, 0xC89466, z=3)
    rect(0, -0.34, 1.2, 0.5, 0x2A2622, z=4)
    for i, y in enumerate((-0.28, -0.4, -0.52)):
        rect(0, y, 0.9 - i * 0.2, 0.05, 0x8A7A6A, z=5)
card("card_sunset", 0x8F8070, 0x2A2622, sunset)

# ---- 送出特效環
reset()
ring(0, 0, 0.88, 0.2, 0xFFFFFF, z=0)
ring(0, 0, 0.8, 0.1, CYAN, z=1)
for i in range(8):
    t = i * math.pi / 4
    c, s_ = math.cos(t), math.sin(t)
    line(0.5 * c, 0.5 * s_, 0.98 * c, 0.98 * s_, 0.08, AMBER, z=2)
render("boost_ring", G, 128)

# ---- 失誤叉叉
reset()
line(-0.7, -0.7, 0.7, 0.7, 0.42, 0x3A0A0C, z=0)
line(-0.7, 0.7, 0.7, -0.7, 0.42, 0x3A0A0C, z=0)
line(-0.7, -0.7, 0.7, 0.7, 0.28, RED, z=1)
line(-0.7, 0.7, 0.7, -0.7, 0.28, RED, z=1)
render("strike_x", G, 128)

# ---- 游標（準星，白色，用 tint 變色）
reset()
ring(0, 0, 0.62, 0.16, 0x0A1018, z=0)
ring(0, 0, 0.58, 0.09, WHITE, z=1)
for sx, sy, ex, ey in ((0, 0.98, 0, 0.32), (0, -0.98, 0, -0.32), (0.98, 0, 0.32, 0), (-0.98, 0, -0.32, 0)):
    line(sx, sy, ex, ey, 0.2, 0x0A1018, z=2)
    line(sx, sy, ex, ey, 0.1, WHITE, z=3)
circle(0, 0, 0.07, WHITE, z=4)
render("cursor", G, 128)
