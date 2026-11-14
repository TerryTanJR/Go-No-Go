"""假火點（fire_verify，雙頻道交叉驗證）的美術。blender -b -P tools/minigame_art/fire_verify.py"""
import os, sys, math
sys.path.insert(0, os.path.dirname(__file__))
from lib import *

G = "fire_verify"

# ---- 背景（1600x900，1 格 = 50 畫布像素）
reset()
rect(0, 0, 16, 9, BG, z=0)
for i in range(-8, 9):
    line(i, -4.5, i, 4.5, 0.012, shade(GRID, 0.6), z=0.1)
for j in range(-4, 5):
    line(-8, j, 8, j, 0.012, shade(GRID, 0.6), z=0.1)
def panel(px, py, pw, ph, accent=CYAN):
    cx, cy = (px - 400) / 50.0, (225 - py) / 50.0
    w, h = pw / 50.0, ph / 50.0
    rrect(cx, cy, w + 0.06, h + 0.06, 0.14, shade(accent, 0.35), z=1)
    rrect(cx, cy, w, h, 0.12, PANEL, z=2)
panel(192, 154, 322, 254)    # 左圖
panel(608, 154, 322, 254)    # 右圖
panel(400, 142, 82, 196)     # 中間數字讀數
panel(192, 328, 322, 76, AMBER)   # 左圖例（MWIR）
panel(608, 328, 322, 76, AMBER)   # 右圖例（LWIR）
render("bg", G, (1600, 900), 16.0)

# ---- 熱影像邊框（320x244 畫布像素；內部開口 304x228，透明）
reset()
W2, H2 = 1.0, 244 / 320.0
iw, ih = 0.95, 228 / 320.0
col = shade(CYAN, 0.6)
rect(0, (H2 + ih) / 2, 2 * W2, H2 - ih, col, z=0)
rect(0, -(H2 + ih) / 2, 2 * W2, H2 - ih, col, z=0)
rect(-(W2 + iw) / 2, 0, W2 - iw, 2 * ih, col, z=0)
rect((W2 + iw) / 2, 0, W2 - iw, 2 * ih, col, z=0)
for sx in (-1, 1):
    for sy in (-1, 1):
        rect(sx * (W2 - 0.12), sy * H2, 0.24, 0.045, CYAN, z=1)
        rect(sx * W2, sy * (H2 - 0.12), 0.045, 0.24, CYAN, z=1)
render("grid_frame", G, (640, 488), 2.0)

# ---- 標記圈（白色，用 tint 上色）
reset()
ring(0, 0, 0.8, 0.28, 0x707070, z=0)
ring(0, 0, 0.8, 0.16, WHITE, z=1)
render("ring", G, 64)

# ---- 送出按鈕（180x64；淺色底板 + 深色勾勾，用 tint 上色）
reset()
rrect(0, 0, 1.98, 0.7, 0.2, shade(0xDDDDDD, 0.55), z=0)
rrect(0, 0.02, 1.9, 0.62, 0.17, 0xF2F2F2, z=1)
rrect(0, 0.2, 1.7, 0.15, 0.07, WHITE, z=1.5)
D = 0x1A1010
line(-0.78, 0.0, -0.64, -0.17, 0.1, D, z=3)
line(-0.64, -0.17, -0.38, 0.17, 0.1, D, z=3)
render("btn_submit", G, (576, 205), 2.0)
