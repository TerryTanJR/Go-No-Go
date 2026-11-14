# -*- coding: utf-8 -*-
"""
窗外的景：地球上的太空中心發射場（Blender → GLB）。FacilityBuilder 把它放在組裝大廳的南窗外和大廳的北窗外。
用法：
  blender -b -P tools/room/build_launch_complex.py
  blender -b -P tools/room/build_launch_complex.py -- --render preview.png     （順便存一張預覽圖）
  blender -b -P tools/room/build_launch_complex.py -- --out some/other.glb      （輸出到別的位置）
會重建 tools/room/LaunchComplex.blend，並匯出 Assets/GoNoGo/Art/Models/Room/LaunchComplex.glb。

座標：原點 = 窗戶外側中央。Blender +X = 左右、+Y = 離窗戶越來越遠、+Z = 上。
Unity 端（FacilityBuilder.LaunchComplexView）把模型轉 180° 抵銷 glTFast 的軸向翻轉，Blender (x, y, z) = Unity (x, z, y)。

景物：灰白大廠房（直立的深色門槽、壁板、低樓）、移動式發射平台上的灰色鋼構發射塔和橘色火箭、避雷針塔、燃料球槽、
路燈桿、柏油地面與車道標線、履帶車道（碎石）。總部和發射台一帶本來就很空曠，所以沒有樹和草。
材質依名稱在 Unity 換成專案材質（M_Ground / M_Road 會貼程序貼圖）：
  M_Ground(柏油) M_Road M_Gravel M_Line M_Pad M_VabWhite M_VabGray M_VabDark M_VabGlass M_Steel M_SteelDark
  M_RocketWhite M_RocketOrange M_RocketBlack M_Tank M_Lamp
"""
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
BLEND = os.path.join(HERE, "LaunchComplex.blend")
GLB = os.path.join(ROOT, "Assets", "GoNoGo", "Art", "Models", "Room", "LaunchComplex.glb")
ARGV = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if "--out" in ARGV:
    GLB = ARGV[ARGV.index("--out") + 1]

rng = random.Random(11)

# ---------------------------------------------------------------- 材質

MATS = {}
SMOOTH = {"M_RocketWhite", "M_RocketOrange", "M_RocketBlack", "M_Tank"}


def material(name, color, rough=0.7, metal=0.0, emit=None):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit:
        b.inputs["Emission Color"].default_value = (*emit, 1)
        b.inputs["Emission Strength"].default_value = 3.0
    MATS[name] = m


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    MATS.clear()
    material("M_Ground", (0.2, 0.21, 0.23))
    material("M_Road", (0.3, 0.31, 0.33))
    material("M_Gravel", (0.5, 0.47, 0.42))
    material("M_Line", (0.9, 0.85, 0.3))
    material("M_Pad", (0.6, 0.6, 0.58))
    material("M_VabWhite", (0.86, 0.87, 0.88), 0.55)
    material("M_VabGray", (0.45, 0.47, 0.52), 0.55)
    material("M_VabDark", (0.12, 0.13, 0.16), 0.5)
    material("M_VabGlass", (0.18, 0.28, 0.4), 0.2, 0.2)
    material("M_Steel", (0.5, 0.53, 0.57), 0.5, 0.6)
    material("M_SteelDark", (0.25, 0.27, 0.3), 0.5, 0.6)
    material("M_RocketWhite", (0.92, 0.92, 0.93), 0.45)
    material("M_RocketOrange", (0.82, 0.4, 0.1), 0.6)
    material("M_RocketBlack", (0.05, 0.05, 0.06), 0.4)
    material("M_Tank", (0.88, 0.89, 0.9), 0.4, 0.2)
    material("M_Lamp", (1.0, 0.2, 0.15), 0.4, 0.0, (1.0, 0.15, 0.1))


# ---------------------------------------------------------------- 幾何累積（同材質合成一個物件）

ACC = {}


def acc(m):
    return ACC.setdefault(m, bmesh.new())


def rot_matrix(rx=0.0, ry=0.0, rz=0.0):
    from mathutils import Euler
    return Euler((math.radians(rx), math.radians(ry), math.radians(rz)), "XYZ").to_matrix().to_4x4()


def box(m, c, s, rot=None):
    mat4 = Matrix.Translation(Vector(c)) @ (rot or Matrix.Identity(4)) @ Matrix.Diagonal((s[0], s[1], s[2], 1.0))
    bmesh.ops.create_cube(acc(m), size=1.0, matrix=mat4)


def strut(m, p0, p1, t):
    p0, p1 = Vector(p0), Vector(p1)
    d = p1 - p0
    length = d.length
    q = Vector((0, 0, 1)).rotation_difference(d)
    mat4 = Matrix.Translation((p0 + p1) / 2) @ q.to_matrix().to_4x4() @ Matrix.Diagonal((t, t, length, 1.0))
    bmesh.ops.create_cube(acc(m), size=1.0, matrix=mat4)


def cyl(m, c, r, h, r2=None, seg=28, rot=None):
    mat4 = Matrix.Translation(Vector(c)) @ (rot or Matrix.Identity(4))
    bmesh.ops.create_cone(acc(m), cap_ends=True, cap_tris=False, segments=seg, radius1=r, radius2=r if r2 is None else r2, depth=h, matrix=mat4)


def ogive(m, c, r, h, seg=28):
    """尖頭：幾段漸縮的圓台疊起來（c = 底面中心）。"""
    steps = 7
    z = 0.0
    for i in range(steps):
        t0, t1 = i / steps, (i + 1) / steps
        r0 = r * (1 - t0 ** 2.2) if i else r
        r1 = r * (1 - t1 ** 2.2)
        dz = h / steps
        cyl(m, (c[0], c[1], c[2] + z + dz / 2), max(r0, 0.02), dz, max(r1, 0.02), seg)
        z += dz


def sphere(m, c, r, scale=(1, 1, 1), seg=24):
    mat4 = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
    bmesh.ops.create_uvsphere(acc(m), u_segments=seg, v_segments=max(8, seg // 2), radius=r, matrix=mat4)


def blob(m, c, r, scale=(1, 1, 1), jitter=0.18, seed=0):
    """圓潤但不規則的樹冠 / 灌木：細分兩次的 icosphere，頂點加一點隨機位移。"""
    bm = acc(m)
    before = {v.index for v in bm.verts}
    mat4 = Matrix.Translation(Vector(c)) @ Matrix.Diagonal((scale[0], scale[1], scale[2], 1.0))
    res = bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r, matrix=mat4)
    r2 = random.Random(seed)
    for v in res["verts"]:
        d = (v.co - Vector(c))
        v.co += d.normalized() * (r2.random() - 0.5) * 2 * jitter * r


def flush(smooth_all=False):
    objs = []
    for name, bm in ACC.items():
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
        if name in SMOOTH:
            for f in bm.faces:
                f.smooth = True
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        me.materials.append(MATS[name])
        ob = bpy.data.objects.new(name[2:], me)
        bpy.context.scene.collection.objects.link(ob)
        objs.append(ob)
    ACC.clear()
    return objs


# ---------------------------------------------------------------- 地面

def quad(m, x0, x1, y0, y1, z, uv_tiles):
    bm = acc(m)
    uv = bm.loops.layers.uv.verify()
    v = [bm.verts.new((x0, y0, z)), bm.verts.new((x1, y0, z)), bm.verts.new((x1, y1, z)), bm.verts.new((x0, y1, z))]
    f = bm.faces.new(v)
    for loop, uvv in zip(f.loops, ((0, 0), (uv_tiles[0], 0), (uv_tiles[0], uv_tiles[1]), (0, uv_tiles[1]))):
        loop[uv].uv = uvv


def ground():
    """柏油地面（平的）、混凝土車道、履帶車道（碎石）、車道標線、路緣。"""
    quad("M_Ground", -330.0, 330.0, 0.0, 330.0, -0.3, (110, 55))     # 一路鋪到天空球的邊緣，看出去是一望無際的地平線
    quad("M_Road", -2.0, 14.0, 0.0, 100.0, -0.2, (2, 12))                     # 往發射台的主路
    quad("M_Road", -100.0, 14.0, 24.0, 33.0, -0.19, (14, 2))                    # 橫向的路
    quad("M_Gravel", -42.0, -2.0, 74.0, 90.0, -0.18, (6, 3))                    # 履帶車道：廠房 → 發射台（碎石）
    for k in range(34):                                                          # 主路中線（虛線）
        quad("M_Line", 5.85, 6.15, 3.0 + k * 3.0, 4.8 + k * 3.0, -0.19, (1, 1))
    for x in (-2.0, 14.0):                                                       # 路緣線
        quad("M_Line", x - 0.12, x + 0.12, 0.0, 100.0, -0.19, (1, 8))
    for k in range(10):                                                          # 橫向路的斑馬標線
        quad("M_Line", -30.0 + k * 1.4, -29.1 + k * 1.4, 24.0, 33.0, -0.18, (1, 1))


# ---------------------------------------------------------------- 大廠房（參考太空中心的總裝大樓）

def vab():
    cx, cy = -22.0, 100.0
    W, D = 30.0, 30.0
    y_front = cy - D / 2
    box("M_VabWhite", (cx, cy, 36), (W, D, 72))                                   # 白色主樓
    box("M_VabWhite", (cx, cy, 72.6), (W - 8, D - 10, 1.2))                      # 屋頂女兒牆
    # 左側深灰高牆
    box("M_VabGray", (cx - 19, cy, 32), (8.0, D, 64))
    box("M_VabWhite", (cx - 19, y_front - 0.15, 56), (7.4, 0.3, 4.0))
    # 右側兩座高塔，外側有巨大的直立門槽
    box("M_VabGray", (cx + 20.5, cy, 30), (11.0, D, 60))
    box("M_VabGray", (cx + 27.5, cy, 24), (4.0, D, 48))
    box("M_VabDark", (cx + 20.0, y_front - 0.2, 26), (4.6, 0.5, 48))              # 門槽
    for k in range(9):                                                              # 門板分段
        box("M_VabGray", (cx + 20.0, y_front - 0.3, 5.0 + k * 5.4), (4.8, 0.3, 0.35))
    box("M_VabGray", (cx + 20.0, y_front - 0.3, 26), (0.3, 0.3, 48))
    # 主樓正面：淺色壁板框（隊徽放在這一塊上，Unity 端貼）
    box("M_VabGray", (cx - 4.0, y_front - 0.1, 40), (11.6, 0.25, 54))
    box("M_VabWhite", (cx - 4.0, y_front - 0.25, 40), (10.6, 0.25, 52))
    box("M_VabGray", (cx + 7.0, y_front - 0.1, 30), (3.0, 0.25, 36))
    # 低樓：西翼白色大廠房、前方的灰色辦公棟（有窗）
    box("M_VabWhite", (cx - 38, cy - 5, 13), (40, 22, 26))
    box("M_VabGray", (cx - 38, cy - 16.2, 21), (40, 0.4, 1.4))
    box("M_VabDark", (cx - 38, cy - 16.2, 9), (14, 0.5, 18))                       # 大門
    ann_y = cy - D / 2 - 11
    box("M_VabWhite", (cx + 24, ann_y, 8), (30, 16, 16))
    box("M_VabGray", (cx + 24, ann_y - 8.2, 15), (30, 0.4, 1.2))
    for k in range(6):
        box("M_VabGlass", (cx + 12.5 + k * 4.6, ann_y - 8.15, 8.5), (3.2, 0.2, 5.5))
    # 前方的小樓（白色）
    box("M_VabWhite", (cx - 2, cy - 30, 4.5), (22, 12, 9))
    for k in range(5):
        box("M_VabGlass", (cx - 9 + k * 3.8, cy - 36.1, 4.5), (2.6, 0.2, 3.4))
    box("M_VabGray", (cx - 2, cy - 36.2, 9.2), (22, 0.4, 0.8))


# ---------------------------------------------------------------- 發射塔與火箭

def launch_pad():
    px, py = 22.0, 70.0
    box("M_Pad", (px, py, 0.0), (52, 40, 0.5))                                      # 台面
    box("M_SteelDark", (px, py, 0.9), (24, 18, 1.4))
    box("M_Steel", (px, py, 4.0), (26, 20, 5.0))                                    # 移動式發射平台
    box("M_SteelDark", (px, py, 6.8), (24, 18, 0.6))
    for sx in (-1, 1):                                                              # 履帶
        for sy in (-1, 1):
            box("M_SteelDark", (px + sx * 10.5, py + sy * 8.5, 1.2), (4.5, 3.2, 2.2))
    # 火箭：橘色主級 + 兩側白色助推器 + 上面級 + 太空艙 + 逃生塔
    rx, ry, rz = px - 4.5, py, 7.0
    cyl("M_RocketOrange", (rx, ry, rz + 15), 2.3, 30)
    cyl("M_RocketWhite", (rx, ry, rz + 32.5), 1.8, 5)
    cyl("M_RocketBlack", (rx, ry, rz + 30.2), 2.35, 0.4)
    cyl("M_RocketWhite", (rx, ry, rz + 34.9), 1.8, 0.4)
    ogive("M_RocketWhite", (rx, ry, rz + 35.1), 1.8, 5.2)
    strut("M_RocketWhite", (rx, ry, rz + 40), (rx, ry, rz + 44), 0.18)
    for s in (-1, 1):
        cyl("M_RocketWhite", (rx + s * 3.7, ry, rz + 11), 1.35, 22)
        cyl("M_RocketBlack", (rx + s * 3.7, ry, rz + 5.0), 1.38, 0.4)
        ogive("M_RocketWhite", (rx + s * 3.7, ry, rz + 22), 1.35, 3.6)
        cyl("M_RocketBlack", (rx + s * 3.7, ry, rz + 0.5), 1.1, 1.0, 0.7)
    cyl("M_RocketBlack", (rx, ry, rz + 0.6), 1.9, 1.2, 1.3)
    # 發射塔：四根主柱、每 4 公尺一層橫梁與 X 型斜撐、頂端避雷針、伸向火箭的服務臂
    tx, ty, tz = px + 6.5, py, 6.8
    TW, TH, n = 6.5, 56.0, 14
    h = TW / 2
    for sx in (-1, 1):
        for sy in (-1, 1):
            box("M_Steel", (tx + sx * h, ty + sy * h, tz + TH / 2), (0.7, 0.7, TH))
    for lv in range(n + 1):
        z = tz + lv * 4.0
        for sx in (-1, 1):
            strut("M_Steel", (tx + sx * h, ty - h, z), (tx + sx * h, ty + h, z), 0.34)
        for sy in (-1, 1):
            strut("M_Steel", (tx - h, ty + sy * h, z), (tx + h, ty + sy * h, z), 0.34)
        if lv == n:
            break
        z2 = z + 4.0
        flip = lv % 2 == 0
        a, b = (-h, h) if flip else (h, -h)
        for sy in (-1, 1):                         # 前後兩面的 X
            strut("M_SteelDark", (tx + a, ty + sy * h, z), (tx + b, ty + sy * h, z2), 0.24)
            strut("M_SteelDark", (tx + b, ty + sy * h, z), (tx + a, ty + sy * h, z2), 0.24)
        for sx in (-1, 1):                         # 左右兩面的 X
            strut("M_SteelDark", (tx + sx * h, ty + a, z), (tx + sx * h, ty + b, z2), 0.24)
            strut("M_SteelDark", (tx + sx * h, ty + b, z), (tx + sx * h, ty + a, z2), 0.24)
    for z in (22, 30, 38, 46):                    # 服務臂
        box("M_Steel", (tx - h - 5.0, ty, tz + z), (10.5, 1.5, 0.6))
        box("M_SteelDark", (tx - h - 10.2, ty, tz + z), (0.8, 2.2, 1.6))
    box("M_SteelDark", (tx, ty, tz + TH + 1.0), (TW + 1.2, TW + 1.2, 1.2))
    strut("M_Steel", (tx, ty, tz + TH + 1.5), (tx, ty, tz + TH + 12), 0.28)
    sphere("M_Lamp", (tx, ty, tz + TH + 12.3), 0.45, seg=10)


def masts():
    for (x, y) in ((-6.0, 54.0), (52.0, 62.0), (40.0, 96.0), (-34.0, 58.0)):
        h = 52.0
        strut("M_Steel", (x, y, 0), (x, y, h), 0.45)
        for k in range(1, 9):
            strut("M_Steel", (x - 1.3, y, k * 6.0), (x + 1.3, y, k * 6.0), 0.16)
        strut("M_Steel", (x, y, h), (x, y, h + 4), 0.18)
        sphere("M_Lamp", (x, y, h + 4.3), 0.4, seg=10)


def tanks():
    x, y = -62.0, 70.0
    sphere("M_Tank", (x, y, 12.0), 8.0, seg=36)
    for k in range(6):
        a = k * math.pi / 3
        strut("M_SteelDark", (x + math.cos(a) * 6.5, y + math.sin(a) * 6.5, 6.0), (x + math.cos(a) * 7.5, y + math.sin(a) * 7.5, 0), 0.5)
    box("M_SteelDark", (x, y, 0.2), (4, 4, 0.4))
    for k, yy in enumerate((54.0, 86.0)):
        cyl("M_Tank", (-76.0, yy, 5.0), 3.2, 10.0)
        box("M_SteelDark", (-76.0, yy, 10.2), (1.4, 1.4, 0.5))


# ---------------------------------------------------------------- 路燈

def lamp_posts():
    for k in range(7):
        y = 12.0 + k * 13.0
        for x in (-4.5, 16.5):
            strut("M_SteelDark", (x, y, -0.2), (x, y, 9.0), 0.22)
            strut("M_SteelDark", (x, y, 9.0), (x + (0.0 if x > 5 else 0.0), y, 9.0), 0.22)
            box("M_SteelDark", (x + (-1.0 if x > 5 else 1.0), y, 9.0), (2.2, 0.5, 0.18))
            box("M_VabWhite", (x + (-1.9 if x > 5 else 1.9), y, 8.85), (0.7, 0.5, 0.14))
    # 發射台的圍欄（低矮的護欄）與路邊的警示柱
    for k in range(12):
        box("M_Line", (-3.2, 10.0 + k * 8.0, 0.3), (0.2, 0.2, 1.0))
        box("M_Line", (15.2, 10.0 + k * 8.0, 0.3), (0.2, 0.2, 1.0))


# ---------------------------------------------------------------- 主程式

def build():
    reset()
    ground()
    vab()
    launch_pad()
    masts()
    tanks()
    lamp_posts()
    return flush()


def export():
    os.makedirs(os.path.dirname(GLB), exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for o in bpy.context.scene.objects:
        o.select_set(o.type == "MESH")
    bpy.ops.export_scene.gltf(filepath=GLB, export_format="GLB", use_selection=True,
                              export_yup=True, export_apply=True, export_cameras=False, export_lights=False)


def render_preview(path):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    cam = bpy.data.objects.new("Cam", bpy.data.cameras.new("Cam"))
    cam.data.angle = math.radians(65)
    cam.location = (0, -8, 1.7)
    cam.rotation_euler = (math.radians(90), 0, 0)
    bpy.context.scene.collection.objects.link(cam)
    sc.camera = cam
    sun = bpy.data.objects.new("Sun", bpy.data.lights.new("Sun", "SUN"))
    sun.data.energy = 4.0
    sun.rotation_euler = (math.radians(55), math.radians(15), math.radians(200))
    bpy.context.scene.collection.objects.link(sun)
    w = bpy.data.worlds.new("W")
    w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.55, 0.72, 0.95, 1)
    bg.inputs["Strength"].default_value = 1.2
    sc.world = w
    sc.render.filepath = path
    bpy.ops.render.render(write_still=True)


def main():
    objs = build()
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    export()
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    print("BUILT %d objects, %d faces -> %s" % (len(meshes), sum(len(o.data.polygons) for o in meshes), GLB))
    if "--render" in ARGV:
        render_preview(ARGV[ARGV.index("--render") + 1])


main()
