"""零件故障（接線小遊戲）的美術。blender -b -P tools/minigame_art/rewire.py
可染色的圖（plug_*、marker、light）用白／灰畫，遊戲用 tint 上色。"""
import os, sys, math
sys.path.insert(0, os.path.dirname(__file__))
from lib import *

G = "rewire"
DARK = 0x05090F

# ---- 背景：電路板（細走線 + 焊點 + 晶片輪廓，亮度很低，不搶前景）
reset()
rect(0, 0, 16, 9, 0x080E17, z=0)
for i in range(-7, 8):
    line(i * 1.15, -4.5, i * 1.15, 4.5, 0.02, 0x0D1722, z=0.1)
for j in range(-4, 5):
    line(-8, j * 1.1, 8, j * 1.1, 0.02, 0x0D1722, z=0.1)
traces = [
    [(-8, 3.6), (-6.2, 3.6), (-5.6, 3.0), (-3.5, 3.0)],
    [(8, -3.4), (6.0, -3.4), (5.4, -2.8), (3.0, -2.8)],
    [(-8, -3.3), (-6.5, -3.3), (-6.0, -3.8), (-4.0, -3.8)],
    [(8, 3.7), (5.5, 3.7), (5.0, 4.2)],
    [(-8, 0.2), (-7.2, 0.2), (-6.8, 0.6), (-6.8, 2.2)],
    [(8, 0.0), (7.2, 0.0), (6.8, -0.4), (6.8, -2.0)],
]
for t in traces:
    for a, b in zip(t, t[1:]):
        line(a[0], a[1], b[0], b[1], 0.07, 0x12263A, z=0.2)
    circle(t[-1][0], t[-1][1], 0.1, 0x1B3A55, z=0.3)
    circle(t[-1][0], t[-1][1], 0.05, 0x0A1018, z=0.4)
for cx, cy in ((-7.2, -1.6), (7.2, 1.6)):
    rrect(cx, cy, 1.0, 1.4, 0.08, 0x0E1A28, z=0.3)
    rrect(cx, cy, 0.9, 1.3, 0.06, 0x0B1420, z=0.4)
    for k in range(-3, 4):
        rect(cx - 0.6 if cx < 0 else cx + 0.6, cy + k * 0.18, 0.16, 0.07, 0x1E3248, z=0.35)
render("bg", G, (1600, 900), 16.0)

# ---- 面板角框（左上角的 L 形，遊戲用 rot 轉到四個角）
reset()
poly([(-0.9, 0.9), (0.2, 0.9), (0.2, 0.62), (-0.62, 0.62), (-0.62, -0.2), (-0.9, -0.2)], shade(CYAN, 0.45), z=0)
poly([(-0.9, 0.9), (0.15, 0.9), (0.15, 0.68), (-0.68, 0.68), (-0.68, -0.15), (-0.9, -0.15)], CYAN, z=1)
rect(-0.45, 0.8, 0.5, 0.05, 0xBFF4FF, z=2)
circle(-0.45, 0.45, 0.07, AMBER, z=2)
render("corner", G, 128)

# ---- 插頭（白色，可染色）
def plug_base(shape):
    reset()
    shape(1.0, DARK, 0)
    shape(0.88, 0xBBBBBB, 1)
    shape(0.74, WHITE, 2)
    shape(0.38, 0x8A8A8A, 3)
    shape(0.26, 0xDDDDDD, 4)
def sh_circle(k, c, z): circle(0, 0, 0.82 * k, c, z=z)
def sh_square(k, c, z): rrect(0, 0, 1.5 * k, 1.5 * k, 0.16 * k, c, z=z)
def sh_tri(k, c, z):
    poly([(0, 0.95 * k + 0.0), (0.92 * k, -0.7 * k), (-0.92 * k, -0.7 * k)], c, z=z)
plug_base(sh_circle); circle(-0.3, 0.3, 0.1, WHITE, z=5); render("plug_circle", G, 128)
plug_base(sh_square); rect(-0.35, 0.4, 0.3, 0.07, WHITE, z=5); render("plug_square", G, 128)
plug_base(sh_tri); circle(-0.12, 0.05, 0.07, WHITE, z=5); render("plug_tri", G, 128)

# ---- 插孔外殼（深色凹槽 + 螺絲）
reset()
rrect(0, 0, 1.9, 1.9, 0.25, shade(DIM, 0.5), z=0)
rrect(0, 0, 1.78, 1.78, 0.2, 0x1B2A3C, z=1)
rrect(0, 0, 1.4, 1.4, 0.15, DARK, z=2)
rrect(0, 0, 1.3, 1.3, 0.12, 0x0E1824, z=3)
for sx in (-1, 1):
    for sy in (-1, 1):
        circle(sx * 0.78, sy * 0.78, 0.07, DIM, z=4)
        line(sx * 0.78 - 0.04, sy * 0.78 + 0.04, sx * 0.78 + 0.04, sy * 0.78 - 0.04, 0.025, DARK, z=5)
render("socket", G, 128)

# ---- 按鈕（4:1，白色可染色）
reset()
rrect(0, -0.03, 1.96, 0.46, 0.12, DARK, z=0)
rrect(0, 0, 1.92, 0.46, 0.12, 0xAAAAAA, z=1)
rrect(0, 0.02, 1.84, 0.38, 0.1, WHITE, z=2)
rrect(0, 0.1, 1.7, 0.1, 0.05, 0xFFFFFF, z=3)
rrect(0, -0.1, 1.7, 0.08, 0.04, 0xCCCCCC, z=3)
render("button", G, (512, 128), 2.0)

# ---- 時序條外框（16:1）
reset()
rrect(0, 0, 1.98, 0.118, 0.04, shade(CYAN, 0.45), z=0)
rrect(0, 0, 1.95, 0.098, 0.035, DARK, z=1)
for i in range(-15, 16):
    rect(i * 0.063, 0.0, 0.004, 0.03 if i % 5 else 0.06, 0x2A4058, z=2)
render("bar_frame", G, (1024, 64), 2.0)

# ---- 游標標記（尖端朝上、白色可染色；遊戲轉 180 度指向下）
reset()
poly([(0, 0.92), (0.62, -0.5), (0.24, -0.5), (0.24, -0.95), (-0.24, -0.95), (-0.24, -0.5), (-0.62, -0.5)], DARK, z=0)
poly([(0, 0.78), (0.5, -0.4), (0.15, -0.4), (0.15, -0.82), (-0.15, -0.82), (-0.15, -0.4), (-0.5, -0.4)], WHITE, z=1)
poly([(0, 0.6), (0.18, 0.1), (0, 0.1)], 0xFFFFFF, z=2)
render("marker", G, 64)

# ---- 指示燈（白色可染色）
reset()
circle(0, 0, 0.95, DARK, z=0)
circle(0, 0, 0.8, 0x6A6A6A, z=1)
circle(0, 0, 0.62, WHITE, z=2)
ellipse(-0.2, 0.22, 0.2, 0.14, 0xFFFFFF, z=3, rot=0.6)
render("light", G, 64)
