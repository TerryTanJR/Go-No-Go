"""營運小遊戲的 2D 美術：用 Blender 的向量式小工具畫扁平插畫，渲染成透明背景的 PNG。

用法（無頭模式）：
    blender -b -P tools/minigame_art/alien.py

每個遊戲一支腳本（alien.py、solar_dodge.py…），都 `from lib import *`，
用 poly/circle/ellipse/rect/rrect/line/ring 疊圖層，最後 `render("alien_ship", ...)` 輸出到
Assets/GoNoGo/Resources/Minigames/<遊戲>/<名字>.png。

座標：單位是「格」，一張圖的範圍是 (-1,-1) 到 (1,1)，+X 向右、+Y 向上。z（圖層）越大越在上面。
顏色用 0xRRGGBB 或 (r,g,b[,a]) 0~1。畫的時候請一律把「朝向」畫成尖端朝上（+Y），遊戲會依 Rot 旋轉。
"""
import math
import os
import sys

import bpy
import bmesh

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "Assets", "GoNoGo", "Resources", "Minigames")

# 和 Core 的 MiniColor 一致的色票
BG, PANEL, GRID, DIM, TEXT = 0x0A1018, 0x14202E, 0x1E2E40, 0x5C6F82, 0xDCE6F0
CYAN, AMBER, GREEN, RED, VIOLET, WHITE = 0x35D0E8, 0xFFB020, 0x4ADE80, 0xFF5A5F, 0xB48CFF, 0xFFFFFF

_z = [0.0]
_mats = {}


def rgb(c):
    if isinstance(c, int):
        r, g, b = ((c >> 16) & 255) / 255.0, ((c >> 8) & 255) / 255.0, (c & 255) / 255.0
        a = 1.0
    else:
        r, g, b = c[0], c[1], c[2]
        a = c[3] if len(c) > 3 else 1.0
    # Workbench 的物件顏色是線性空間；用 Standard 檢視轉換時要先轉成線性，輸出才會是原本的 sRGB 色
    lin = lambda v: v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4
    return (lin(r), lin(g), lin(b), a)


def shade(c, k):
    """把顏色變亮（k>1）或變暗（k<1）。"""
    r, g, b, _ = [v for v in (((c >> 16) & 255) / 255.0, ((c >> 8) & 255) / 255.0, (c & 255) / 255.0, 1)]
    f = lambda v: max(0, min(255, int(round(v * k * 255))))
    return (f(r) << 16) | (f(g) << 8) | f(b)


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _z[0] = 0.0
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.view_settings.view_transform = "Standard"
    sc.display.shading.light = "FLAT"
    sc.display.shading.color_type = "OBJECT"
    sc.display.shading.show_object_outline = False
    sc.display.render_aa = "32"
    cam = bpy.data.cameras.new("cam")
    cam.type = "ORTHO"
    cam.ortho_scale = 2.0
    obj = bpy.data.objects.new("cam", cam)
    bpy.context.collection.objects.link(obj)
    obj.location = (0, 0, 20)
    sc.camera = obj


def _add(points, color, z, closed=True):
    z = _z[0] if z is None else z
    _z[0] = z + 0.001
    me = bpy.data.meshes.new("m")
    bm = bmesh.new()
    verts = [bm.verts.new((x, y, 0)) for x, y in points]
    bm.faces.new(verts)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("o", me)
    bpy.context.collection.objects.link(ob)
    ob.location = (0, 0, z)
    ob.color = rgb(color)
    return ob


def poly(points, color, z=None):
    """任意凸／凹多邊形（頂點依序）。"""
    return _add(points, color, z)


def ellipse(cx, cy, rx, ry, color, z=None, seg=64, rot=0.0):
    c, s = math.cos(rot), math.sin(rot)
    pts = []
    for i in range(seg):
        a = 2 * math.pi * i / seg
        x, y = rx * math.cos(a), ry * math.sin(a)
        pts.append((cx + x * c - y * s, cy + x * s + y * c))
    return _add(pts, color, z)


def circle(cx, cy, r, color, z=None, seg=64):
    return ellipse(cx, cy, r, r, color, z, seg)


def rect(cx, cy, w, h, color, z=None, rot=0.0):
    c, s = math.cos(rot), math.sin(rot)
    pts = []
    for dx, dy in ((-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)):
        pts.append((cx + dx * c - dy * s, cy + dx * s + dy * c))
    return _add(pts, color, z)


def rrect(cx, cy, w, h, r, color, z=None, seg=10):
    """圓角矩形。"""
    r = min(r, w / 2, h / 2)
    pts = []
    for (ox, oy, a0) in ((w / 2 - r, h / 2 - r, 0), (-w / 2 + r, h / 2 - r, 90), (-w / 2 + r, -h / 2 + r, 180), (w / 2 - r, -h / 2 + r, 270)):
        for i in range(seg + 1):
            a = math.radians(a0 + 90.0 * i / seg)
            pts.append((cx + ox + r * math.cos(a), cy + oy + r * math.sin(a)))
    return _add(pts, color, z)


def line(x1, y1, x2, y2, width, color, z=None):
    dx, dy = x2 - x1, y2 - y1
    n = math.hypot(dx, dy) or 1
    nx, ny = -dy / n * width / 2, dx / n * width / 2
    return _add([(x1 + nx, y1 + ny), (x2 + nx, y2 + ny), (x2 - nx, y2 - ny), (x1 - nx, y1 - ny)], color, z)


def ring(cx, cy, r, width, color, z=None, seg=64):
    """圓環：外圈減內圈，用一圈四邊形拼。"""
    z = _z[0] if z is None else z
    _z[0] = z + 0.001
    me = bpy.data.meshes.new("ring")
    bm = bmesh.new()
    ro, ri = r + width / 2, max(0.0001, r - width / 2)
    outer = [bm.verts.new((cx + ro * math.cos(2 * math.pi * i / seg), cy + ro * math.sin(2 * math.pi * i / seg), 0)) for i in range(seg)]
    inner = [bm.verts.new((cx + ri * math.cos(2 * math.pi * i / seg), cy + ri * math.sin(2 * math.pi * i / seg), 0)) for i in range(seg)]
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((outer[i], outer[j], inner[j], inner[i]))
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new("ring", me)
    bpy.context.collection.objects.link(ob)
    ob.location = (0, 0, z)
    ob.color = rgb(color)
    return ob


def star(cx, cy, r_out, r_in, n, color, z=None, rot=0.0):
    pts = []
    for i in range(n * 2):
        a = rot + math.pi / 2 + math.pi * i / n
        r = r_out if i % 2 == 0 else r_in
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return _add(pts, color, z)


def mirror_x(points):
    """左半邊的點 → 完整的左右對稱輪廓（點要由上往下排）。"""
    return list(points) + [(-x, y) for x, y in reversed(points)]


def with_outline(ob_fn, color_outline, grow):
    """（保留）需要描邊時，先用深色畫大一點的同形狀，再畫本體。"""
    raise NotImplementedError


def render(name, game, size=256, bounds=2.0):
    """渲染目前場景成 PNG。size = 邊長，或 (寬, 高)；bounds = 畫面涵蓋的長邊（格），預設 -1~1。
    bounds 是套用在「較長的那一邊」：寬圖 = 寬度的格數，高圖 = 高度的格數。"""
    sc = bpy.context.scene
    w, h = (size, size) if isinstance(size, int) else size
    sc.camera.data.ortho_scale = bounds
    sc.render.resolution_x, sc.render.resolution_y = w, h
    d = os.path.join(OUT, game)
    os.makedirs(d, exist_ok=True)
    path = os.path.join(d, name + ".png")
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)
    print("rendered", path)
    return path
