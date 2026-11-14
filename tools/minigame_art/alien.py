"""外星人彩蛋（射擊小遊戲）的美術。blender -b -P tools/minigame_art/alien.py"""
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from lib import *

G = "alien"

# ---- 玩家戰機（尖端朝上）
reset()
rim = shade(CYAN, 0.45)
# 引擎火焰
poly([(-0.14, -0.62), (0, -0.98), (0.14, -0.62)], AMBER, z=0)
poly([(-0.07, -0.62), (0, -0.82), (0.07, -0.62)], 0xFFF2B0, z=0.5)
# 機翼（深色底 + 亮面）
wing = [(0.1, 0.0), (0.82, -0.52), (0.88, -0.74), (0.5, -0.66), (0.1, -0.5)]
poly(mirror_x([(-x, y) for x, y in reversed(wing)]), rim, z=1)
poly(mirror_x([(-x * 0.9, y * 0.96 + 0.015) for x, y in reversed(wing)]), shade(CYAN, 0.8), z=2)
# 機身
body = [(0, 0.95), (0.16, 0.55), (0.2, -0.1), (0.24, -0.62), (0.12, -0.7)]
poly(mirror_x([(-x, y) for x, y in reversed(body)]), rim, z=3)
poly(mirror_x([(-x * 0.82, y * 0.97) for x, y in reversed(body)]), CYAN, z=4)
# 座艙
ellipse(0, 0.32, 0.075, 0.2, shade(0x0A1018, 1.0), z=5)
ellipse(0, 0.36, 0.045, 0.12, 0x9FE8FF, z=6)
# 尾翼小燈
circle(0.62, -0.68, 0.035, RED, z=5)
circle(-0.62, -0.68, 0.035, GREEN, z=5)
render("ship", G, 256)

# ---- 飛碟（寬 2 : 高 1，頭朝上）
def ufo(name, body, dome, light, extra=None):
    reset()
    ellipse(0, -0.08, 0.96, 0.3, shade(body, 0.5), z=0)         # 底部陰影
    ellipse(0, 0.0, 0.92, 0.26, body, z=1)                       # 盤身
    ellipse(0, 0.04, 0.9, 0.12, shade(body, 1.25), z=2)          # 盤面高光
    ellipse(0, 0.12, 0.36, 0.3, shade(dome, 0.55), z=3)          # 玻璃罩邊
    ellipse(0, 0.14, 0.31, 0.26, dome, z=4)
    ellipse(-0.09, 0.2, 0.09, 0.07, 0xE8FFF0, z=5)               # 反光
    for i in range(-3, 4):
        circle(i * 0.26, -0.01, 0.045, light, z=6)
    if extra: extra()
    render(name, G, (256, 128))

ufo("ufo_small", AMBER, GREEN, 0xFFF2B0)
def cannon():
    rect(0, -0.26, 0.12, 0.2, 0x3A2A55, z=0.5)
    circle(0, -0.36, 0.07, RED, z=0.6)
ufo("ufo_gunner", VIOLET, 0x7DF3FF, 0xFFFFFF, cannon)

# 母艦：更大、多層
reset()
ellipse(0, -0.1, 0.98, 0.34, shade(RED, 0.35), z=0)
ellipse(0, -0.02, 0.94, 0.28, shade(RED, 0.7), z=1)
ellipse(0, 0.04, 0.9, 0.2, RED, z=2)
ellipse(0, 0.1, 0.62, 0.22, shade(RED, 1.2), z=3)
ellipse(0, 0.2, 0.34, 0.26, shade(0x7DF3FF, 0.5), z=4)
ellipse(0, 0.22, 0.29, 0.22, 0x7DF3FF, z=5)
ellipse(-0.08, 0.3, 0.08, 0.06, WHITE, z=6)
for i in range(-4, 5):
    circle(i * 0.2, -0.02, 0.05, AMBER if i % 2 else 0xFFF2B0, z=7)
for sx in (-1, 1):
    rect(sx * 0.7, -0.27, 0.12, 0.2, shade(RED, 0.5), z=0.5)
    circle(sx * 0.7, -0.38, 0.06, AMBER, z=0.6)
rect(0, -0.3, 0.16, 0.22, shade(RED, 0.5), z=0.5)
circle(0, -0.41, 0.08, AMBER, z=0.6)
render("ufo_boss", G, (256, 128))

# ---- 子彈
reset()
circle(0, 0, 0.5, shade(RED, 0.5), z=0)
circle(0, 0, 0.36, RED, z=1)
circle(0, 0, 0.18, 0xFFE0E0, z=2)
render("shot_enemy", G, 64, 1.0)
reset()
rrect(0, 0, 0.22, 0.9, 0.11, shade(CYAN, 0.6), z=0)
rrect(0, 0, 0.12, 0.8, 0.06, CYAN, z=1)
rrect(0, 0.05, 0.05, 0.5, 0.025, WHITE, z=2)
render("shot_player", G, (32, 64), 0.5)

# ---- 背景（800×450 的畫布，用 1600×900 渲染再縮；用同心橢圓假裝漸層）
reset()
rect(0, 0, 16, 9, 0x070C14, z=0)
for i in range(14):
    k = 1 - i / 14.0
    ellipse(-3.0, 1.2, 6.5 * k + 0.5, 3.0 * k + 0.3, (0x0A + int(i * 1.2), 0x12 + int(i * 1.6), 0x22 + int(i * 3)) and (((0x0A + int(i * 1.2)) << 16) | ((0x12 + int(i * 1.6)) << 8) | (0x22 + int(i * 3))), z=0.1 + i * 0.01)
# 遠方的行星：本體 + 大氣光圈 + 一顆小月亮
ring(5.6, -2.9, 2.3, 0.12, 0x1B3552, z=0.9)
circle(5.6, -2.9, 2.15, 0x1A2F48, z=1)
circle(5.6, -2.9, 2.0, 0x21405F, z=1.1)
ellipse(5.0, -2.2, 1.3, 0.2, 0x2B5278, z=1.2, rot=0.4)
ellipse(5.9, -3.3, 1.5, 0.16, 0x1A3552, z=1.2, rot=0.3)
circle(2.6, -3.4, 0.28, 0x2A3F58, z=1)
render("bg", G, (1600, 900), 16.0)
