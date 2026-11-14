"""電量異常（電力繞線）的美術。blender -b -P tools/minigame_art/power_load.py
電線貼圖畫成「深色外框 + 白色管身」，遊戲用 tint 上色（沒電＝鋼灰、有電＝綠／黃）。
朝向：直線＝上下（N-S）、彎頭＝上與右（N-E）。"""
import os, sys, math
sys.path.insert(0, os.path.dirname(__file__))
from lib import *

G = "power_load"
DK = 0x070C12

# ---- 背景
reset()
rect(0, 0, 16, 9, BG, z=0)
for i in range(-8, 9):
    line(i, -4.5, i, 4.5, 0.012, GRID, z=1)
for j in range(-4, 5):
    line(-8, j + 0.5, 8, j + 0.5, 0.012, GRID, z=1)
ob = rect(0, 0, 16, 9, BG, z=1.5)
ob.color = rgb((0.04, 0.06, 0.09, 0.5))
rrect(0, -0.3, 11.5, 7.6, 0.35, shade(PANEL, 0.6), z=2)
rrect(0, -0.3, 11.2, 7.3, 0.3, shade(PANEL, 0.4), z=3)
render("bg", G, (1600, 900), 16.0)

W_OUT, W_IN, W_HI = 0.5, 0.36, 0.06


def plate():
    rrect(0, 0, 1.94, 1.94, 0.22, shade(GRID, 0.7), z=0)
    rrect(0, 0, 1.84, 1.84, 0.18, shade(PANEL, 0.95), z=1)


# ---- 底板
reset()
plate()
for sx in (-1, 1):
    for sy in (-1, 1):
        circle(sx * 0.8, sy * 0.8, 0.05, shade(DIM, 0.8), z=2)
render("tile_plate", G, 128)


def pipe(x1, y1, x2, y2, z):
    line(x1, y1, x2, y2, W_OUT, DK, z=z)
    line(x1, y1, x2, y2, W_IN, WHITE, z=z + 0.3)
    dx, dy = x2 - x1, y2 - y1
    n = math.hypot(dx, dy) or 1
    ox, oy = -dy / n * 0.1, dx / n * 0.1
    line(x1 + ox, y1 + oy, x2 + ox, y2 + oy, W_HI, 0xBFC8D2, z=z + 0.4)


def joint(z):
    circle(0, 0, 0.3, DK, z=z)
    circle(0, 0, 0.21, WHITE, z=z + 0.3)
    circle(-0.05, 0.05, 0.06, 0xBFC8D2, z=z + 0.4)


# ---- 直線（N-S）
reset()
pipe(0, 1.0, 0, -1.0, 2)
render("wire_straight", G, 128)

# ---- 彎頭（N-E）
reset()
pipe(0, 1.0, 0, 0, 2)
pipe(0, 0, 1.0, 0, 2)
joint(3)
render("wire_corner", G, 128)

# ---- 破損的方塊（不能導電、不能轉）
reset()
plate()
tint = rrect(0, 0, 1.84, 1.84, 0.18, shade(RED, 0.25), z=1.5)
tint.color = rgb((0.35, 0.1, 0.1, 0.5))
line(-1.0, 0, -0.38, 0, W_OUT, DK, z=2)
line(-1.0, 0, -0.38, 0, W_IN, shade(DIM, 0.9), z=2.3)
line(1.0, 0, 0.45, 0, W_OUT, DK, z=2)
line(1.0, 0, 0.45, 0, W_IN, shade(DIM, 0.9), z=2.3)
crack = [(0.1, 0.9), (-0.12, 0.45), (0.15, 0.1), (-0.15, -0.3), (0.1, -0.6), (-0.05, -0.9)]
for a, b in zip(crack, crack[1:]):
    line(a[0], a[1], b[0], b[1], 0.1, DK, z=4)
    line(a[0], a[1], b[0], b[1], 0.04, RED, z=4.3)
star(-0.38, 0.0, 0.2, 0.08, 6, AMBER, z=5)
star(0.42, 0.0, 0.16, 0.06, 6, 0xFFF2B0, z=5)
render("tile_dead", G, 128)

# ---- 太陽能板（右側接出電線）
reset()
line(0.55, 0.0, 1.0, 0.0, 0.3, DK, z=0.5)
line(0.55, 0.0, 1.0, 0.0, 0.18, AMBER, z=0.6)
poly([(-0.9, -0.62), (-0.7, 0.7), (0.5, 0.7), (0.62, -0.62)], shade(CYAN, 0.35), z=1)
poly([(-0.82, -0.54), (-0.64, 0.62), (0.44, 0.62), (0.55, -0.54)], 0x1A4A8A, z=2)
for i in range(1, 3):
    t = i / 3.0
    line(-0.82 + 0.18 * t, -0.54 + 1.16 * t, 0.55 - 0.11 * t, -0.54 + 1.16 * t, 0.04, shade(CYAN, 0.7), z=3)
for i in range(1, 4):
    t = i / 4.0
    line(-0.82 + 1.37 * t, -0.54, -0.64 + 1.08 * t, 0.62, 0.04, shade(CYAN, 0.7), z=3)
circle(0.55, 0.0, 0.16, AMBER, z=4)
render("panel", G, 128)


def battery(full):
    reset()
    line(-1.0, 0, -0.84, 0, 0.3, DK, z=0.5)
    line(-1.0, 0, -0.84, 0, 0.18, AMBER if full else DIM, z=0.6)
    rrect(-0.06, 0, 1.55, 1.0, 0.18, shade(GREEN if full else DIM, 0.4), z=1)
    rrect(-0.06, 0, 1.43, 0.86, 0.14, shade(PANEL, 0.9), z=2)
    rrect(0.84, 0, 0.16, 0.38, 0.05, GREEN if full else DIM, z=1)
    for i in range(3):
        rrect(-0.55 + i * 0.46, 0, 0.36, 0.6, 0.06, GREEN if full else shade(GRID, 1.1), z=3)
        if full:
            rect(-0.55 + i * 0.46, 0.15, 0.3, 0.06, 0xC8FFD8, z=4)
    if full:
        poly([(0.05, 0.36), (-0.14, -0.04), (-0.02, -0.04), (-0.08, -0.36), (0.14, 0.06), (0.02, 0.06)], AMBER, z=5)
    render("battery_full" if full else "battery_empty", G, 128)


battery(False)
battery(True)
