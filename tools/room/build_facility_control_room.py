# -*- coding: utf-8 -*-
"""
Facility 中控室模型（Blender → GLB）。
用法（Git Bash）：
  "/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b -P tools/room/build_facility_control_room.py
會重建 tools/room/FacilityControlRoom.blend，並匯出 Assets/GoNoGo/Art/Models/Room/FacilityControlRoom.glb。
加 -- --render 會再存預覽圖 tools/room/FacilityControlRoom_preview*.png。

座標：原點 = 中控室中心（Unity 世界 (23, 0, 0)）。Blender +X = Unity +X（東、影像牆），
Blender +Y = Unity +Z（北），Blender +Z = 上。SceneBuilder 會把模型轉 180° 抵銷 glTFast 的軸向翻轉。
房間 x -11…11、y -9…9；西牆（x -11 以西）是 SceneBuilder 的隔牆和門，模型只補 6 m 以上那一段。

流程改版後的中控室：只留營運用得到的東西——前方的大屏幕（影像牆）、發射台、四台營運席和飛行總監台。
中控台分三座、左右對稱：北邊營運席（酬載、電力，綠）、中央飛行總監（琥珀，站在升起的平台上）、
南邊營運席（姿態、通訊，綠），中間留縫。原本設計組的席位（電話、筆記、手冊目錄、任務控制台、任務目標與預算）
和後排的手冊席都拿掉了：房間西半邊空出來，由 Unity 的 FacilityBuilder 擺線索區（研究牆、白板、黑板、書堆）。

Unity 依物件名稱掛元件：
  Screen_<panel>  中控台螢幕（panel 的 ':' 寫成 '-'），掛 ConsoleTarget
  Glow_<panel>    該席位的桌緣發光條（準星對到時變亮）
  Anchor_launch   發射台頂面中心
  Wall_Main       影像牆中央的大屏幕：選件、組裝時畫選件的畫面，營運時是地球（RT_OpsScreen）
  Wall_reqs       大屏幕左邊：任務需求（營運時是酬載、電力的遙測）
  Wall_asm        大屏幕右邊：流程說明／失敗原因（營運時是姿態、通訊的遙測）
  Deco_<組>_*     裝飾螢幕（程序貼圖）；組 = F 總監、O／P 營運、W 影像牆
  Deco_<組>_Top_<panel>  席位上方的第二台螢幕（標籤放在它上面）
其他物件依材質名稱換成專案材質：M_Floor、M_Dark、M_Trim、M_Console、M_Panel、M_Paper、M_Glow、M_GlowAmber、M_GlowGreen、M_GlowRed。
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND = os.path.join(HERE, "FacilityControlRoom.blend")
PREVIEW = os.path.join(HERE, "FacilityControlRoom_preview")
GLB = os.path.join(ROOT, "Assets", "GoNoGo", "Art", "Models", "Room", "FacilityControlRoom.glb")

X0, X1, Y0, Y1 = -11.0, 11.0, -9.0, 9.0
H = 7.0                      # 中控室天花板（組裝區 6 m）
DIVIDER_H = 6.0              # SceneBuilder 隔牆的高度

# 影像牆：圓弧，圓心 V，觀眾在圓內
V = Vector((-4.0, 0.0))
RW = 14.5
# 中控台：圓弧，圓心 C，操作員站在外側面向圓心
C = Vector((5.0, 0.0))
RC = 7.0
Z = Vector((0, 0, 1))

# 三座中控台（由北到南 = 操作員面向影像牆時由左到右）：四台營運席分在飛行總監兩邊，左右對稱
PODS = [
    ("O", ["ops-payload", "ops-power"]),
    ("F", ["ops_desk"]),
    ("P", ["ops-attitude", "ops-comms"]),
]
STATION_DEG = 9.4            # 兩翼每席的角度（半徑 7 m 時約 1.15 m）
FD_DEG = 18.0                # 飛行總監席
GAP_DEG = 7.4                # 座與座之間的縫（約 0.9 m）
FD_RISE = 0.2                # 總監平台高度
GLOW = {"F": "M_GlowAmber", "O": "M_GlowGreen", "P": "M_GlowGreen"}
LAUNCH = Vector((4.0, 0.0))  # 發射台：中控台前方正中央

# 桌子的剖面（dr = 離中控台半徑的距離，正 = 朝操作員；z；這一段邊的材質）
DESK = [
    (-0.42, 0.00, "M_Console"),   # 底
    (0.08, 0.00, "M_Console"),    # 踢腳內凹
    (0.08, 0.12, "M_Console"),    # 外擴
    (0.30, 0.22, "M_Console"),    # 正面
    (0.36, 0.70, "M_Trim"),       # 桌緣
    (0.43, 0.75, "M_Trim"),
    (0.40, 0.79, "M_Panel"),      # 斜的操作面
    (0.00, 0.87, "M_Panel"),
    (-0.12, 0.88, "M_Trim"),      # 螢幕架的台階
    (-0.16, 0.97, "M_Trim"),      # 螢幕架
    (-0.44, 0.99, "M_Console"),   # 背面
]
FIN = [(-0.48, 0.0, "M_Trim"), (0.47, 0.0, "M_Trim"), (0.47, 0.8, "M_Trim"), (-0.05, 1.12, "M_Trim"), (-0.48, 1.12, "M_Trim")]

# ---------------------------------------------------------------- 場景與材質

MATS = {}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    MATS.clear()


def mat(name, color, emission=None, strength=0.0, metallic=0.0, rough=0.5):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = (*color, 1)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = rough
    if emission is not None:
        bsdf.inputs["Emission Color"].default_value = (*emission, 1)
        bsdf.inputs["Emission Strength"].default_value = strength
    MATS[name] = m


def materials():
    mat("M_Floor", (0.10, 0.11, 0.13), rough=0.4)
    mat("M_Dark", (0.035, 0.04, 0.055), rough=0.6)
    mat("M_Trim", (0.12, 0.13, 0.15), metallic=0.6, rough=0.45)
    mat("M_Console", (0.13, 0.14, 0.17), metallic=0.3, rough=0.45)
    mat("M_Panel", (0.04, 0.045, 0.055), metallic=0.2, rough=0.25)
    mat("M_Glow", (0.1, 0.4, 0.7), emission=(0.2, 0.65, 1.0), strength=6)
    mat("M_GlowAmber", (0.7, 0.4, 0.1), emission=(1.0, 0.55, 0.15), strength=6)
    mat("M_GlowGreen", (0.1, 0.6, 0.3), emission=(0.2, 1.0, 0.45), strength=6)
    mat("M_GlowRed", (0.7, 0.08, 0.05), emission=(1.0, 0.12, 0.06), strength=6)
    mat("M_Screen", (0.02, 0.05, 0.08), emission=(0.15, 0.45, 0.75), strength=2)
    mat("M_Earth", (0.05, 0.1, 0.2), emission=(0.25, 0.5, 0.95), strength=3)
    mat("M_Paper", (0.92, 0.9, 0.84), rough=0.8)


def obj_from_bm(name, bm, materials_):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in ([materials_] if isinstance(materials_, str) else materials_):
        me.materials.append(MATS[m])
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def solid(bm):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    return bm

# ---------------------------------------------------------------- 幾何小工具


def box(name, x0, x1, y0, y1, z0, z1, material):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co.x = x0 if v.co.x < 0 else x1
        v.co.y = y0 if v.co.y < 0 else y1
        v.co.z = z0 if v.co.z < 0 else z1
    return obj_from_bm(name, bm, material)


def polar(center, r, deg):
    a = math.radians(deg)
    return Vector((center.x + r * math.cos(a), center.y + r * math.sin(a)))


def frame(deg):
    """中控台上角度 deg 處：out = 從圓心往外（朝操作員），right = 操作員面向圓心時的右手邊。"""
    a = math.radians(deg)
    out = Vector((math.cos(a), math.sin(a), 0))
    return out, Vector((-out.y, out.x, 0))


def arc_box(name, center, r0, r1, a0, a1, z0, z1, material, segs=None, closed=False):
    """圓弧形的實心塊（r0 內徑、r1 外徑、a0→a1 度）。closed = 整圈。"""
    return sweep(name, center, 0.0, a0, a1,
                 [(r0, z0, material), (r1, z0, material), (r1, z1, material), (r0, z1, material)],
                 0.0, segs, closed)


def sweep(name, center, radius, a0, a1, profile, zoff=0.0, segs=None, closed=False):
    """把剖面（dr, z, 材質）沿圓弧掃出來。每段邊用自己的材質，兩端封口。"""
    if segs is None:
        segs = max(2, int(abs(a1 - a0) / 2.0))
    names = []
    for p in profile:
        if p[2] not in names:
            names.append(p[2])
    bm = bmesh.new()
    rings = []
    n = segs if closed else segs + 1
    for i in range(n):
        a = a0 + (a1 - a0) * i / segs
        ring = []
        for dr, z, _ in profile:
            p = polar(center, radius + dr, a)
            ring.append(bm.verts.new((p.x, p.y, z + zoff)))
        rings.append(ring)
    k = len(profile)
    for i in range(segs):
        ra, rb = rings[i], rings[(i + 1) % n]
        for j in range(k):
            f = bm.faces.new((ra[j], ra[(j + 1) % k], rb[(j + 1) % k], rb[j]))
            f.material_index = names.index(profile[j][2])
    if not closed:
        bm.faces.new(rings[0]).material_index = 0
        bm.faces.new(list(reversed(rings[-1]))).material_index = 0
    return obj_from_bm(name, solid(bm), names)


def face_towards(bm, viewer):
    """讓每個面朝向觀看點（螢幕只有正面）。"""
    flip = [f for f in bm.faces if f.normal.dot(viewer - f.calc_center_median()) < 0]
    if flip:
        bmesh.ops.reverse_faces(bm, faces=flip)


def uv_from(bm, fn):
    uv = bm.loops.layers.uv.new("UVMap")
    for f in bm.faces:
        for lp in f.loops:
            lp[uv].uv = fn(lp.vert.co)


def arc_screen(name, center, r, a0, a1, z0, z1, material, viewer, segs=None):
    """弧形螢幕，觀眾在圓內（面朝圓心那側）。U：觀眾的右手方向，V：向上。"""
    if segs is None:
        segs = max(2, int(abs(a1 - a0) / 1.5))
    bm = bmesh.new()
    cols = []
    for i in range(segs + 1):
        a = a0 + (a1 - a0) * i / segs
        p = polar(center, r, a)
        cols.append((bm.verts.new((p.x, p.y, z0)), bm.verts.new((p.x, p.y, z1))))
    for i in range(segs):
        (a, b), (c, d) = cols[i], cols[i + 1]
        bm.faces.new((a, c, d, b))
    bm.normal_update()
    face_towards(bm, viewer)
    lo, hi = min(a0, a1), max(a0, a1)
    mid = (lo + hi) / 2

    def fn(co):
        ang = math.degrees(math.atan2(co.y - center.y, co.x - center.x))
        while ang - mid > 180:
            ang -= 360
        while ang - mid < -180:
            ang += 360
        # 觀眾在圓內：角度大（逆時針）在左邊
        return ((hi - ang) / (hi - lo), (co.z - z0) / (z1 - z0))
    uv_from(bm, fn)
    return obj_from_bm(name, bm, material)


def flat_screen(name, mid, right, up, w, h, material, viewer):
    """平面螢幕：mid 中心、right/up 單位向量（從觀眾看）。"""
    bm = bmesh.new()
    hw, hh = right * (w / 2), up * (h / 2)
    vs = [bm.verts.new(mid - hw - hh), bm.verts.new(mid + hw - hh),
          bm.verts.new(mid + hw + hh), bm.verts.new(mid - hw + hh)]
    bm.faces.new(vs)
    bm.normal_update()
    face_towards(bm, viewer)
    uv_from(bm, lambda co: ((co - mid).dot(right) / w + 0.5, (co - mid).dot(up) / h + 0.5))
    return obj_from_bm(name, bm, material)


def add_obox(bm, mid, right, up, fwd, sx, sy, sz):
    """在 bm 裡加一個以 right/up/fwd 為軸的方塊：sx 沿 right、sy 沿 fwd、sz 沿 up。"""
    verts = bmesh.ops.create_cube(bm, size=1.0)["verts"]
    for v in verts:
        c = v.co.copy()
        v.co = mid + right * (c.x * sx) + up * (c.z * sz) + fwd * (c.y * sy)


def oriented_box(name, mid, right, up, fwd, sx, sy, sz, material):
    bm = bmesh.new()
    add_obox(bm, mid, right, up, fwd, sx, sy, sz)
    return obj_from_bm(name, solid(bm), material)


def empty(name, pos):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.2
    e.location = pos
    bpy.context.scene.collection.objects.link(e)
    return e


def monitor(screen_name, tag, mid, right, up, w, h, glow, screen_mat="M_Screen"):
    """一台螢幕：外框、畫面、上緣一條組別顏色的光。"""
    fwd = right.cross(up).normalized()     # 從畫面朝觀眾
    oriented_box(f"Bezel_{tag}", mid - fwd * 0.03, right, up, fwd, w + 0.06, 0.04, h + 0.06, "M_Trim")
    oriented_box(f"MonGlow_{tag}", mid + up * (h / 2 + 0.032) - fwd * 0.012, right, up, fwd, w + 0.02, 0.03, 0.01, glow)
    return flat_screen(screen_name, mid + fwd * 0.002, right, up, w, h, screen_mat, mid + fwd * 1.5)


def tilted(out, deg):
    """往後仰 deg 度的「上」方向（頂端往中控台圓心倒）。"""
    t = math.radians(deg)
    return Z * math.cos(t) - out * math.sin(t)


def slope_point(center, a, dr, zoff):
    """桌子斜操作面上 dr 處的點與「沿斜面往後」的方向。"""
    out, right = frame(a)
    z = 0.79 + (0.40 - dr) / 0.40 * 0.08 + zoff
    p = polar(center, RC + dr, a)
    back = (-out * 0.40 + Z * 0.08).normalized()
    return Vector((p.x, p.y, z)), right, back

# ---------------------------------------------------------------- 房間外殼


def shell():
    box("Floor", X0, X1, Y0, Y1, -0.2, 0.0, "M_Floor")
    box("WallE", X1, X1 + 0.3, Y0 - 0.3, Y1 + 0.3, 0, H, "M_Dark")
    box("WallN", X0, X1 + 0.3, Y1, Y1 + 0.3, 0, H, "M_Dark")
    box("WallS", X0, X1 + 0.3, Y0 - 0.3, Y0, 0, H, "M_Dark")
    # 西牆：隔牆（6 m）以上補到天花板
    box("WallW_Upper", X0 - 0.3, X0, Y0, Y1, DIVIDER_H, H, "M_Dark")
    box("Ceiling", X0 - 0.3, X1 + 0.3, Y0 - 0.3, Y1 + 0.3, H, H + 0.2, "M_Dark")

    # 南北牆：肋條、腳邊和上緣發光條
    for side, y in (("N", Y1), ("S", Y0)):
        s = -1 if y > 0 else 1
        for i in range(7):
            x = X0 + 1.4 + i * 3.2
            box(f"Rib{side}{i}", x - 0.12, x + 0.12, *sorted((y, y + s * 0.3)), 0, H, "M_Trim")
        box(f"Glow{side}_Low", X0, X1, *sorted((y, y + s * 0.03)), 0.85, 0.9, "M_Glow")
        box(f"Glow{side}_High", X0, X1, *sorted((y, y + s * 0.03)), 6.2, 6.25, "M_Glow")
        box(f"Band{side}", X0, X1, *sorted((y, y + s * 0.12)), 6.3, H, "M_Trim")
    box("GlowW_High", X0, X0 + 0.03, Y0, Y1, DIVIDER_H + 0.05, DIVIDER_H + 0.1, "M_Glow")

    # 天花板：同心圓的階梯穹頂（照片那種環形燈槽）
    cc = Vector((1.0, 0.0))
    rings = [(8.2, 8.9, H - 0.25), (5.6, 6.3, H - 0.5), (3.0, 3.7, H - 0.75)]
    for i, (r0, r1, z) in enumerate(rings):
        arc_box(f"CeilRing{i}", cc, r0, r1, 0, 360, z, H, "M_Trim", segs=96, closed=True)
        arc_box(f"CeilGlow{i}", cc, r0 - 0.06, r0, 0, 360, z + 0.02, z + 0.08, "M_Glow", segs=96, closed=True)
    for i in range(12):
        a = i * 30.0 + 15.0
        p0, p1 = polar(cc, 3.7, a), polar(cc, 8.2, a)
        mid = (p0 + p1) / 2
        d = (p1 - p0).normalized()
        n = Vector((-d.y, d.x))
        oriented_box(f"CeilSpoke{i}", Vector((mid.x, mid.y, H - 0.2)), Vector((d.x, d.y, 0)),
                     Z, Vector((n.x, n.y, 0)), (p1 - p0).length, 0.18, 0.4, "M_Trim")

    # 地板：中控台後方的發光弧線、影像牆前的弧線、走道兩側的導引線
    arc_box("FloorGlow_Back", C, RC + 2.35, RC + 2.4, 180 - 42, 180 + 42, 0.0, 0.01, "M_Glow")
    arc_box("FloorGlow_Wall", V, RW - 2.6, RW - 2.55, -34, 34, 0.0, 0.01, "M_Glow")
    for y in (-1.25, 1.25):
        box(f"FloorGlow_Aisle{'N' if y > 0 else 'S'}", X0, -5.2, y - 0.025, y + 0.025, 0.0, 0.01, "M_Glow")

# ---------------------------------------------------------------- 影像牆


def video_wall():
    viewer = Vector((-3.0, 0.0, 1.6))
    z0, z1 = 1.3, 5.75
    # 外框（實心，擋住後面的空間）與底座
    arc_box("WallFrame", V, RW + 0.04, RW + 0.4, -38.5, 38.5, 0.0, H, "M_Trim", segs=60)
    arc_box("WallPlinth", V, RW - 0.55, RW + 0.04, -38.5, 38.5, 0.0, z0 - 0.05, "M_Console", segs=60)
    arc_box("WallPlinthGlow", V, RW - 0.57, RW - 0.53, -38.5, 38.5, z0 - 0.12, z0 - 0.07, "M_Glow", segs=60)
    arc_box("WallTicker", V, RW - 0.06, RW + 0.04, -34, 34, z1 + 0.12, z1 + 0.2, "M_Glow", segs=60)
    arc_box("WallHeader", V, RW - 0.1, RW + 0.04, -38.5, 38.5, z1 + 0.35, H, "M_Trim", segs=60)

    g = 0.25  # 螢幕之間的縫（度）
    r = RW - 0.03
    lo, hi = 1.55, 5.40
    # 中央：大屏幕（16:9）。選件、組裝時是選件的畫面，營運時是地球
    arc_screen("Wall_Main", V, r, 13.5, -13.5, lo, hi, "M_Earth", viewer, segs=36)
    # 兩側各一整塊：左（北）任務需求、右（南）流程說明／失敗原因；營運時改投各子系統的遙測
    arc_screen("Wall_reqs", V, r, 27.5, 13.5 + g, lo, hi, "M_Screen", viewer)
    arc_screen("Wall_asm", V, r, -13.5 - g, -27.5, lo, hi, "M_Screen", viewer)
    # 最外側：直條遙測（像照片右邊的接觸時刻表）
    arc_screen("Deco_W_StripN", V, r, 33.5, 27.5 + g, lo, hi, "M_Screen", viewer)
    arc_screen("Deco_W_StripS", V, r, -27.5 - g, -33.5, lo, hi, "M_Screen", viewer)

# ---------------------------------------------------------------- 中控台


def pod_ranges():
    """每座的角度範圍與每席的中心角。"""
    total = sum(FD_DEG if grp == "F" else STATION_DEG * len(ids) for grp, ids in PODS) + GAP_DEG * (len(PODS) - 1)
    a = 180 - total / 2
    out = []
    for grp, ids in PODS:
        w = FD_DEG if grp == "F" else STATION_DEG
        a0, a1 = a, a + w * len(ids)
        out.append((grp, ids, a0, a1, [a0 + w * (i + 0.5) for i in range(len(ids))]))
        a = a1 + GAP_DEG
    return out


def fin(name, a, side, zoff, glow):
    """座的端板：上緣斜切，正面一條直的光。side = -1 在座的北端、+1 在南端。"""
    w = 0.7
    a0, a1 = (a - w, a) if side < 0 else (a, a + w)
    sweep(f"Fin_{name}", C, RC, a0, a1, FIN, zoff, segs=1)
    m0, m1 = (a0 + 0.2, a1 - 0.2)
    arc_box(f"FinGlow_{name}", C, RC + 0.47, RC + 0.49, m0, m1, zoff + 0.12, zoff + 0.76, glow, segs=1)


def desk_details(grp, sid, a, zoff, btns, buttons=True, pad=True):
    glow = GLOW[grp]
    w = FD_DEG if grp == "F" else STATION_DEG
    # 桌緣發光條（每席一段，準星對到時變亮）
    arc_box(f"Glow_{sid}", C, RC + 0.425, RC + 0.445, a - w / 2 + 0.45, a + w / 2 - 0.45,
            zoff + 0.735, zoff + 0.765, glow, segs=4)
    # 斜操作面：觸控板、兩排小按鈕
    if pad:
        p, right, back = slope_point(C, a, 0.17, zoff)
        normal = back.cross(right).normalized()
        if normal.z < 0:
            normal = -normal
        flat_screen(f"Deco_{grp}_Pad_{sid}", p + normal * 0.004 + right * 0.08, right, back, 0.46, 0.17, "M_Screen",
                    p + normal * 1.0)
    if buttons:
        for row, dr in enumerate((0.31, 0.23)):
            for col in range(3):
                p, right, back = slope_point(C, a, dr, zoff)
                normal = back.cross(right).normalized()
                if normal.z < 0:
                    normal = -normal
                add_obox(btns, p + right * (-0.44 + col * 0.075) + normal * 0.008, right, normal, back, 0.05, 0.045, 0.016)
    # 錨點：桌面前緣中心
    p, _, _ = slope_point(C, a, 0.30, zoff)
    empty(f"Anchor_{sid}", (p.x, p.y, p.z))


def wing_station(grp, sid, a, k, n, btns):
    """兩翼的一席：主螢幕＋依位置變化的第二台螢幕（上方疊一台、狀態條、側邊直立螢幕）。"""
    glow = GLOW[grp]
    out, right = frame(a)
    w, h = 0.82, 0.48
    end = -1 if k == 0 else (1 if k == n - 1 else 0)      # 座的端點：側邊直立螢幕放在外側
    shift = -end * 0.12
    up = tilted(out, 10)
    base = polar(C, RC - 0.27, a)
    mid = Vector((base.x, base.y, 0.97 + 0.07 + h / 2)) + right * shift
    monitor(f"Screen_{sid}", sid, mid, right, up, w, h, glow)
    post = polar(C, RC - 0.34, a)
    oriented_box(f"Post_{sid}", Vector((post.x, post.y, 0.97 + 0.15)) + right * shift, right, Z, out, 0.07, 0.04, 0.3, "M_Console")

    if k % 2 == 1:
        # 上方再疊一台，往下傾
        w2, h2 = 0.70, 0.34
        up2 = tilted(out, 22)
        b2 = polar(C, RC - 0.36, a)
        mid2 = Vector((b2.x, b2.y, mid.z + h / 2 + 0.08 + h2 / 2))
        monitor(f"Deco_{grp}_Top_{sid}", sid + "_top", mid2, right, up2, w2, h2, glow)
        p2 = polar(C, RC - 0.42, a)
        oriented_box(f"Mast_{sid}", Vector((p2.x, p2.y, (0.97 + mid2.z) / 2)), right, Z, out, 0.05, 0.04, mid2.z - 0.97, "M_Console")
    elif end == 0:
        # 中間那席：一條細長的狀態條
        up2 = tilted(out, 15)
        b2 = polar(C, RC - 0.33, a)
        mid2 = Vector((b2.x, b2.y, mid.z + h / 2 + 0.16))
        monitor(f"Deco_{grp}_Top_{sid}", sid + "_top", mid2, right, up2, 0.95, 0.13, glow)
    if end != 0:
        # 座的端點：直立的側螢幕，往操作員轉 28°
        t = math.radians(28)
        out2 = out * math.cos(t) - right * math.sin(t) * end
        right2 = (right * math.cos(t) + out * math.sin(t) * end).normalized()
        sp = mid + right * end * 0.6 + out * 0.08
        monitor(f"Deco_{grp}_Side_{sid}", sid + "_side", sp, right2, tilted(out2, 6), 0.30, 0.46, glow)

    desk_details(grp, sid, a, 0.0, btns)


def fd_station(sid, a, btns):
    """中央飛行總監席：升起的平台、內凹的弧形主螢幕、兩側往內轉的副螢幕。"""
    glow = GLOW["F"]
    z = FD_RISE
    op = polar(C, RC + 1.1, a)                    # 操作員站的位置（弧形螢幕的圓心）
    look = a + 180.0
    eye = Vector((op.x, op.y, z + 1.55))
    # 弧形主螢幕
    rs, half = 1.35, 26.0
    z0, z1 = z + 1.03, z + 1.63
    arc_screen(f"Screen_{sid}", op, rs, look + half, look - half, z0, z1, "M_Screen", eye, segs=16)
    arc_box("Bezel_ops_desk", op, rs + 0.004, rs + 0.06, look - half - 0.8, look + half + 0.8, z0 - 0.035, z1 + 0.035, "M_Trim", segs=16)
    arc_box("MonGlow_ops_desk", op, rs - 0.01, rs + 0.02, look - half - 0.5, look + half + 0.5, z1 + 0.035, z1 + 0.05, glow, segs=16)
    for s in (-1, 1):
        pp = polar(op, rs + 0.06, look + s * 15)
        out, right = frame(a)
        oriented_box(f"Post_ops_desk_{'N' if s > 0 else 'S'}", Vector((pp.x, pp.y, (z + 0.97 + z0) / 2)), right, Z, out,
                     0.07, 0.04, z0 - z - 0.97, "M_Console")
    # 兩側副螢幕：面向操作員
    for s, tag in ((1, "N"), (-1, "S")):
        p = polar(op, 1.28, look + s * 36)
        f = (Vector((op.x, op.y)) - p).normalized()          # 從螢幕朝操作員
        right = Vector((-f.y, f.x, 0))                       # 操作員看過去的右手邊
        monitor(f"Deco_F_Side{tag}", f"fd_side{tag}", Vector((p.x, p.y, z + 1.38)), right, Z, 0.50, 0.36, glow)
    desk_details("F", sid, a, z, btns, buttons=False, pad=True)
    for s in (-1, 1):
        # 總監席的按鈕放兩邊（中間留給發射鈕）
        for row, dr in enumerate((0.31, 0.23)):
            for col in range(3):
                p, right, back = slope_point(C, a, dr, z)
                normal = back.cross(right).normalized()
                if normal.z < 0:
                    normal = -normal
                add_obox(btns, p + right * s * (0.62 + col * 0.075) + normal * 0.008, right, normal, back, 0.05, 0.045, 0.016)


def console():
    pods = pod_ranges()
    for grp, ids, a0, a1, centers in pods:
        glow = GLOW[grp]
        z = FD_RISE if grp == "F" else 0.0
        if grp == "F":
            # 升起的總監平台（前緣、兩側都有光）
            arc_box("Podium_F", C, RC - 0.6, RC + 1.55, a0 - 2.2, a1 + 2.2, 0.0, z, "M_Trim", segs=24)
            arc_box("PodiumGlow_F", C, RC + 1.55, RC + 1.58, a0 - 2.2, a1 + 2.2, 0.04, 0.16, glow, segs=24)
            for s, a in ((-1, a0 - 2.2), (1, a1 + 2.2)):
                arc_box(f"PodiumEdge_F{'N' if s < 0 else 'S'}", C, RC - 0.6, RC + 1.55, a - 0.15 * s - 0.15, a - 0.15 * s + 0.15,
                        0.04, 0.16, glow, segs=1)
        sweep(f"Pod_{grp}", C, RC, a0, a1, DESK, z, segs=int((a1 - a0) / 1.2))
        arc_box(f"Under_{grp}", C, RC + 0.082, RC + 0.1, a0 + 0.3, a1 - 0.3, z + 0.025, z + 0.1, glow)
        arc_box(f"Rail_{grp}", C, RC - 0.125, RC - 0.105, a0 + 0.3, a1 - 0.3, z + 0.905, z + 0.935, glow)
        fin(f"{grp}N", a0, -1, z, glow)
        fin(f"{grp}S", a1, 1, z, glow)
        btns = bmesh.new()
        if grp == "F":
            fd_station(ids[0], centers[0], btns)
        else:
            for k, (sid, a) in enumerate(zip(ids, centers)):
                wing_station(grp, sid, a, k, len(ids), btns)
                if k > 0:
                    ab = a0 + STATION_DEG * k
                    arc_box(f"Divider_{sid}", C, RC + 0.02, RC + 0.38, ab - 0.12, ab + 0.12, 0.79, 0.86, "M_Console", segs=1)
        obj_from_bm(f"Btns_{grp}", solid(btns), glow)

    # （原本這裡有兩條地板光線從縫穿過去匯到發射台；線太細，視角一動就斷成虛線閃爍，已拿掉）


def launch_pedestal():
    """發射台：中控台前方正中央的獨立圓台，每一席都面向它（Unity 在 Anchor_launch 上放按鈕）。"""
    P = LAUNCH
    # 兩層底座、往上收的柱身、外緣發紅光的頂盤
    sweep("Launch_Base", P, 0.0, 0, 360, [(0.0, 0.0, "M_Trim"), (1.0, 0.0, "M_Trim"), (1.0, 0.08, "M_Trim"),
                                          (0.9, 0.14, "M_Trim"), (0.0, 0.14, "M_Trim")], segs=72, closed=True)
    sweep("Launch_Ped", P, 0.0, 0, 360, [(0.0, 0.14, "M_Console"), (0.5, 0.14, "M_Console"), (0.36, 0.92, "M_Console"),
                                         (0.0, 0.92, "M_Console")], segs=64, closed=True)
    sweep("Launch_Top", P, 0.0, 0, 360, [(0.0, 0.92, "M_Trim"), (0.56, 0.92, "M_Trim"), (0.62, 0.98, "M_Trim"),
                                         (0.58, 1.03, "M_Panel"), (0.0, 1.03, "M_Panel")], segs=72, closed=True)
    arc_box("Launch_TopGlow", P, 0.62, 0.64, 0, 360, 0.93, 0.98, "M_GlowRed", segs=72, closed=True)
    # 柱身上四條直的紅光
    for i in range(4):
        a = i * 90.0 + 45.0
        p0 = polar(P, 0.49, a)
        p1 = polar(P, 0.37, a)
        out = Vector((p0.x - P.x, p0.y - P.y, 0)).normalized()
        side = out.cross(Z).normalized()
        bot, top = Vector((p0.x, p0.y, 0.2)), Vector((p1.x, p1.y, 0.86))
        up = (top - bot).normalized()
        oriented_box(f"Launch_Stripe{i}", (bot + top) / 2 + out * 0.006, side, up, out, 0.05, 0.012, (top - bot).length, "M_GlowRed")
    # 地板：紅色光環兩圈
    arc_box("Launch_FloorRing0", P, 1.18, 1.23, 0, 360, 0.0, 0.01, "M_GlowRed", segs=72, closed=True)
    arc_box("Launch_FloorRing1", P, 1.55, 1.57, 0, 360, 0.0, 0.01, "M_GlowRed", segs=72, closed=True)
    empty("Anchor_launch", (P.x, P.y, 1.03))

# ---------------------------------------------------------------- 預覽與匯出


def preview_rig():
    for i, (x, y) in enumerate(((-2, 0), (5, 0), (-7, 0))):
        ld = bpy.data.lights.new(f"PreviewLight{i}", "POINT")
        ld.energy = 600
        ld.color = (1.0, 0.9, 0.78)
        lo = bpy.data.objects.new(f"PreviewLight{i}", ld)
        lo.location = (x, y, 6.0)
        bpy.context.scene.collection.objects.link(lo)


def render_preview():
    sc = bpy.context.scene
    try:
        sc.render.engine = "BLENDER_EEVEE"
    except TypeError:
        pass
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    cam = bpy.data.cameras.new("PreviewCam")
    co = bpy.data.objects.new("PreviewCam", cam)
    sc.collection.objects.link(co)
    sc.camera = co
    shots = {"wide": ((-9.8, -4.5, 3.4), (8.0, 0.5, 2.0), 16),
             "console": ((-5.6, -3.2, 2.4), (1.0, 0.8, 0.9), 22),
             "fd": ((-4.6, 0.9, 1.9), (0.0, -0.2, 1.1), 24),
             "launch": ((0.2, -3.2, 1.9), (4.0, 0.0, 0.8), 28)}
    for key, (loc, target, lens) in shots.items():
        cam.lens = lens
        co.location = loc
        co.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = f"{PREVIEW}_{key}.png"
        bpy.ops.render.render(write_still=True)


def export():
    os.makedirs(os.path.dirname(GLB), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.context.scene.objects:
        o.select_set(o.type in {"MESH", "EMPTY"})
    bpy.ops.export_scene.gltf(filepath=GLB, export_format="GLB", use_selection=True,
                              export_yup=True, export_apply=True, export_cameras=False, export_lights=False)


def main():
    reset()
    materials()
    shell()
    video_wall()
    console()
    launch_pedestal()
    preview_rig()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    export()
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    print(f"BUILT {len(meshes)} meshes, {sum(len(o.data.polygons) for o in meshes)} faces -> {GLB}")
    if "--render" in sys.argv:
        render_preview()


main()
