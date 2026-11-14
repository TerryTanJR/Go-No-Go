"""太陽風暴閃避的美術。blender -b -P tools/minigame_art/solar_dodge.py"""
import os, sys
sys.path.insert(0, os.path.dirname(__file__))
from lib import *

G = "solar_dodge"


def satellite(name, body, panel, glow):
    """衛星（尖端朝上）：天線 + 機身 + 兩片太陽能板。"""
    reset()
    rim = shade(body, 0.45)
    # 太陽能板
    for sx in (-1, 1):
        rect(sx * 0.66, -0.02, 0.62, 0.34, shade(panel, 0.4), z=0)
        rect(sx * 0.66, -0.02, 0.56, 0.28, panel, z=1)
        for i in (-1, 0, 1):
            line(sx * 0.66 + i * 0.185, -0.15, sx * 0.66 + i * 0.185, 0.11, 0.025, shade(panel, 0.5), z=2)
        line(sx * 0.4, -0.02, sx * 0.9, -0.02, 0.025, shade(panel, 0.5), z=2)
        line(sx * 0.3, -0.02, sx * 0.38, -0.02, 0.05, DIM, z=1)
    # 天線
    line(0, 0.3, 0, 0.62, 0.04, DIM, z=0.5)
    circle(0, 0.7, 0.1, glow, z=1)
    circle(0, 0.72, 0.045, WHITE, z=2)
    # 機身
    rrect(0, -0.02, 0.5, 0.68, 0.1, rim, z=3)
    rrect(0, -0.02, 0.42, 0.6, 0.08, body, z=4)
    rrect(0, 0.18, 0.34, 0.14, 0.04, shade(body, 1.25), z=5)
    # 儀器窗
    circle(0, -0.02, 0.12, shade(BG, 1.0), z=6)
    circle(0, -0.02, 0.08, glow, z=7)
    circle(-0.03, 0.01, 0.025, WHITE, z=8)
    # 底部噴口
    rect(0, -0.4, 0.2, 0.1, rim, z=3)
    render(name, G, 256)


satellite("sat", CYAN, VIOLET, AMBER)
satellite("sat_safe", 0x7C8CA0, 0x58607A, 0x5C6F82)

# ---- 質子：頭（亮點）在上，拖尾在下（128 高、64 寬 → 比例 1:2）
reset()
poly([(-0.34, 0.1), (0.34, 0.1), (0.0, -1.0)], shade(AMBER, 0.6), z=0)
poly([(-0.2, 0.1), (0.2, 0.1), (0.0, -0.8)], AMBER, z=1)
poly([(-0.09, 0.1), (0.09, 0.1), (0.0, -0.55)], 0xFFF2B0, z=2)
circle(0, 0.45, 0.46, shade(AMBER, 0.55), z=3)
circle(0, 0.45, 0.36, AMBER, z=4)
circle(0, 0.45, 0.2, 0xFFF2B0, z=5)
circle(-0.05, 0.5, 0.07, WHITE, z=6)
render("proton", G, (64, 128), 2.0)

# ---- 護盾能量球
reset()
circle(0, 0, 0.98, shade(GREEN, 0.35), z=0)
circle(0, 0, 0.8, shade(GREEN, 0.6), z=1)
circle(0, 0, 0.62, GREEN, z=2)
circle(0, 0, 0.4, shade(GREEN, 1.25), z=3)
# 盾牌圖示
poly([(-0.22, 0.2), (0.22, 0.2), (0.22, -0.02), (0, -0.28), (-0.22, -0.02)], shade(GREEN, 0.4), z=4)
poly([(-0.15, 0.14), (0.15, 0.14), (0.15, -0.01), (0, -0.19), (-0.15, -0.01)], 0xE6FFF0, z=5)
circle(-0.38, 0.4, 0.09, WHITE, z=6)
render("orb", G, 128)

# ---- 護盾泡泡（衛星外圍）
reset()
ring(0, 0, 0.9, 0.16, shade(GREEN, 0.55), z=0)
ring(0, 0, 0.9, 0.08, GREEN, z=1)
ring(0, 0, 0.78, 0.04, shade(GREEN, 1.2), z=2)
# 六角形反光
import math as _m
for a in (0.6, 2.0, 3.9, 5.2):
    circle(0.62 * _m.cos(a), 0.62 * _m.sin(a), 0.05, WHITE, z=3)
render("shield", G, 128)

# ---- 背景：風暴紅暗太空 + 左上方太陽光暈
reset()
rect(0, 0, 16, 9, 0x0B0A12, z=0)
for i in range(16):
    k = 1 - i / 16.0
    r = (0x0C + int(i * 2.6)) << 16
    g = (0x0A + int(i * 0.7)) << 8
    b = 0x14 + int(i * 0.2)
    ellipse(-5.2, 3.6, 7.5 * k + 0.4, 5.0 * k + 0.3, r | g | b, z=0.1 + i * 0.01)
circle(-5.2, 3.6, 1.15, 0x7A3A1A, z=1)
circle(-5.2, 3.6, 0.95, 0xC8641E, z=1.1)
circle(-5.2, 3.6, 0.72, AMBER, z=1.2)
circle(-5.2, 3.6, 0.5, 0xFFF2B0, z=1.3)
# 太陽噴發的放射線
for i in range(10):
    a = -0.1 - i * 0.16
    line(-5.2, 3.6, -5.2 + 14 * _m.cos(a), 3.6 + 14 * _m.sin(a), 0.05, 0x2A1512, z=0.9)
render("bg", G, (1600, 900), 16.0)
