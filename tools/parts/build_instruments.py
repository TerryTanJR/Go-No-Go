"""
誘餌儀器的 3D 模型（Blender → GLB → Unity Resources/Presentation/Parts/<part id>.glb）。

單位：以衛星本體邊長 s = 1 為準（遊戲裡依本體大小縮放）。Blender Z 朝上：
  - 朝地的儀器（nadir）：原點在本體底面，模型往 -Z 長，開口朝 -Z。
  - 朝天的儀器（zenith，磁力計、X 光望遠鏡、粒子偵測器）：原點在本體頂面，往 +Z 長。
材質名稱必須和 SatMaterials 的欄位一樣（mliGold、mliSilver、paintWhite、frameDark、metal、lens、accent…），
Unity 載入時依名稱換成遊戲的材質；accent 會染成零件自己的顏色。

用法：
  在 Blender 裡：exec(open(r"F:/NASA_test/tools/parts/build_instruments.py").read())   → 只建模型（排成一列預覽）
  無介面輸出：blender -b -P tools/parts/build_instruments.py -- --export
"""
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))) if "__file__" in dir() else r"F:/NASA_test"
OUT = os.path.join(ROOT, "Assets", "GoNoGo", "Presentation", "Resources", "Presentation", "Parts")
COLL = "DecoyInstruments"

# 預覽用顏色（Unity 會換成遊戲材質）
MATS = {
    "mliGold": ((0.80, 0.58, 0.18, 1), 0.9, 0.35),
    "mliSilver": ((0.75, 0.76, 0.78, 1), 0.9, 0.3),
    "paintWhite": ((0.90, 0.90, 0.88, 1), 0.0, 0.5),
    "frameDark": ((0.06, 0.06, 0.07, 1), 0.3, 0.5),
    "metal": ((0.55, 0.56, 0.58, 1), 1.0, 0.3),
    "lens": ((0.02, 0.03, 0.06, 1), 0.2, 0.05),
    "accent": ((0.3, 0.8, 1.0, 1), 0.0, 0.4),
    "dish": ((0.85, 0.85, 0.82, 1), 0.2, 0.6),
    "solarCell": ((0.05, 0.08, 0.25, 1), 0.5, 0.2),
}


def mat(name):
    m = bpy.data.materials.get(name)
    if m is None:
        m = bpy.data.materials.new(name)
        m.use_nodes = True
        bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
        col, metal, rough = MATS[name]
        bsdf.inputs["Base Color"].default_value = col
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = rough
    return m


# ---------------------------------------------------------------- 幾何工具（都掛在 parent 底下）

def _finish(obj, parent, material, smooth=False):
    obj.data.materials.append(mat(material))
    obj.parent = parent
    if smooth:
        for p in obj.data.polygons:
            p.use_smooth = True
    return obj


def _new(name, bm, parent, material, smooth=False):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    obj = bpy.data.objects.new(name, me)
    bpy.data.collections[COLL].objects.link(obj)
    return _finish(obj, parent, material, smooth)


def box(parent, name, center, size, material):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], v.co.z * size[2])) + Vector(center)
    return _new(name, bm, parent, material)


def _orient(bm, center, axis):
    """把沿 Z 建好的幾何轉到 axis 方向、移到 center。"""
    rot = Vector((0, 0, 1)).rotation_difference(Vector(axis).normalized())
    for v in bm.verts:
        v.co = rot @ v.co + Vector(center)


def cyl(parent, name, center, radius, height, material, axis=(0, 0, 1), seg=32, r2=None):
    """圓柱（r2 = 另一端半徑時是圓錐台）。center 是中心點。"""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=seg, radius1=radius, radius2=radius if r2 is None else r2, depth=height)
    _orient(bm, center, axis)
    return _new(name, bm, parent, material, smooth=True)


def tube(parent, name, center, r_out, r_in, height, material, axis=(0, 0, 1), seg=32):
    """空心管（遮光罩、鏡筒）：內外兩面加上兩端的環。"""
    bm = bmesh.new()
    h = height / 2
    rings = []
    for r, z in ((r_out, -h), (r_out, h), (r_in, h), (r_in, -h)):
        rings.append([bm.verts.new((r * math.cos(a), r * math.sin(a), z)) for a in (i / seg * 2 * math.pi for i in range(seg))])
    for k in range(4):
        a, b = rings[k], rings[(k + 1) % 4]
        for i in range(seg):
            j = (i + 1) % seg
            bm.faces.new((a[i], a[j], b[j], b[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    _orient(bm, center, axis)
    return _new(name, bm, parent, material, smooth=True)


def dish(parent, name, center, radius, depth, material, axis=(0, 0, -1), seg=40, rings=8):
    """拋物面碟，凹面朝 axis 方向。雙面（加一點厚度）。"""
    bm = bmesh.new()
    t = radius * 0.02
    front, back = [], []
    for layer, off in ((front, 0.0), (back, -t)):
        layer.append([bm.verts.new((0, 0, off))])
        for r in range(1, rings + 1):
            rr = r / rings
            layer.append([bm.verts.new((radius * rr * math.cos(a), radius * rr * math.sin(a), depth * rr * rr + off))
                          for a in (i / seg * 2 * math.pi for i in range(seg))])
    for layer, flip in ((front, False), (back, True)):
        c = layer[0][0]
        for i in range(seg):
            f = (c, layer[1][i], layer[1][(i + 1) % seg])
            bm.faces.new(f[::-1] if flip else f)
        for r in range(1, rings):
            a, b = layer[r], layer[r + 1]
            for i in range(seg):
                j = (i + 1) % seg
                f = (a[i], b[i], b[j], a[j])
                bm.faces.new(f[::-1] if flip else f)
    fa, ba = front[-1], back[-1]
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new((fa[i], fa[j], ba[j], ba[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    _orient(bm, center, axis)
    return _new(name, bm, parent, material, smooth=True)


def lens(parent, name, center, radius, axis=(0, 0, -1)):
    """鏡頭：深色玻璃 + 一圈零件顏色的環（和程式產生的儀器一致）。"""
    n = Vector(axis).normalized()
    cyl(parent, name, center, radius, 0.012, "lens", axis)
    tube(parent, name + "_ring", Vector(center) - n * 0.004, radius * 1.3, radius * 1.02, 0.016, "accent", axis)


def strut(parent, name, a, b, radius, material="metal"):
    a, b = Vector(a), Vector(b)
    return cyl(parent, name, (a + b) / 2, radius, (b - a).length, material, axis=b - a, seg=12)


def root(name):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = "PLAIN_AXES"
    e.empty_display_size = 0.15
    bpy.data.collections[COLL].objects.link(e)
    return e


# ---------------------------------------------------------------- 八台誘餌儀器

def magnetometer():
    """磁通門磁力計：小底座 + 伸出去的折疊支桿，感測頭在末端（遠離衛星本身的磁場）。朝天。"""
    r = root("inst_magnetometer")
    box(r, "base", (0, 0, 0.03), (0.09, 0.09, 0.06), "paintWhite")
    cyl(r, "hinge", (0, 0, 0.07), 0.025, 0.04, "frameDark", axis=(1, 0, 0), seg=16)
    tip = Vector((0.42, 0, 0.42))
    mid = Vector((0.16, 0, 0.30))
    strut(r, "boom1", (0, 0, 0.07), mid, 0.012, "frameDark")
    cyl(r, "knee", mid, 0.018, 0.035, "metal", axis=(0, 1, 0), seg=16)
    strut(r, "boom2", mid, tip, 0.012, "frameDark")
    cyl(r, "sensor", tip + Vector((0.035, 0, 0.035)), 0.035, 0.09, "paintWhite", axis=(1, 0, 1))
    tube(r, "sensor_band", tip + Vector((0.035, 0, 0.035)), 0.04, 0.034, 0.02, "accent", axis=(1, 0, 1))
    return r


def uv_ozone():
    """紫外線臭氧光譜儀（OMI 型）：大箱子 + 橫向很寬的狹縫望遠鏡 + 側面散熱板。朝地。"""
    r = root("inst_uv_ozone")
    box(r, "body", (0, 0, -0.17), (0.46, 0.34, 0.30), "mliSilver")
    box(r, "radiator", (0, 0.175, -0.15), (0.40, 0.012, 0.24), "paintWhite")
    # 寬視野：一條很寬的狹縫和外擴的擋板
    box(r, "slit_baffle_l", (0, -0.075, -0.36), (0.40, 0.012, 0.10), "frameDark")
    box(r, "slit_baffle_r", (0, 0.035, -0.36), (0.40, 0.012, 0.10), "frameDark")
    box(r, "slit_end_a", (-0.195, -0.02, -0.36), (0.012, 0.12, 0.10), "frameDark")
    box(r, "slit_end_b", (0.195, -0.02, -0.36), (0.012, 0.12, 0.10), "frameDark")
    box(r, "slit", (0, -0.02, -0.33), (0.37, 0.10, 0.01), "lens")
    box(r, "slit_trim", (0, -0.02, -0.322), (0.39, 0.12, 0.008), "accent")
    # 太陽校正擴散板（OMI 每天看一次太陽）
    box(r, "diffuser", (0.16, -0.12, -0.02), (0.12, 0.10, 0.04), "paintWhite")
    return r


def lightning():
    """閃電成像儀（LIS 型）：電子箱 + 很大的錐形遮光罩，凝視雲頂。朝地。"""
    r = root("inst_lightning")
    box(r, "electronics", (0, 0, -0.07), (0.24, 0.20, 0.14), "mliGold")
    cyl(r, "camera", (0, 0, -0.18), 0.075, 0.10, "frameDark")
    # 外擴的錐形遮光罩（擋白天雲頂的反光）
    cone = tube(r, "hood", (0, 0, -0.30), 0.14, 0.13, 0.16, "paintWhite")
    for v in cone.data.vertices:  # 上端（靠本體）縮小，下端維持大口徑 → 喇叭口
        if v.co.z > -0.30:
            v.co.x *= 0.6
            v.co.y *= 0.6
    lens(r, "lens", (0, 0, -0.235), 0.07)
    return r


def microwave_radiometer():
    """微波輻射計（SMAP 型）：伸出去的大網狀反射碟 + 饋源喇叭。朝地。"""
    r = root("inst_microwave_radiometer")
    box(r, "electronics", (0, 0, -0.06), (0.22, 0.22, 0.12), "mliGold")
    cyl(r, "spin", (0, 0, -0.14), 0.06, 0.05, "metal")
    # 饋源喇叭（往下開口）
    cyl(r, "horn", (0, 0, -0.22), 0.035, 0.12, "paintWhite", r2=0.075)
    tube(r, "horn_band", (0, 0, -0.20), 0.07, 0.06, 0.02, "accent")
    # 支臂和偏置的大碟子
    arm_end = Vector((0.45, 0, -0.52))
    strut(r, "arm", (0, 0, -0.16), arm_end, 0.014, "frameDark")
    strut(r, "arm2", (0, 0.05, -0.16), arm_end + Vector((0, 0.05, 0)), 0.01, "frameDark")
    # 碟子的凹面朝向饋源喇叭
    face = (Vector((0, 0, -0.24)) - arm_end).normalized()
    dish(r, "reflector", arm_end, 0.36, 0.09, "dish", axis=face)
    tube(r, "rim", arm_end + face * 0.09, 0.37, 0.355, 0.02, "metal", axis=face)
    return r


def xray_telescope():
    """X 光望遠鏡：細長鏡筒朝天（掠射式鏡片要很長的焦距），頂端有打開的防塵蓋。朝天。"""
    r = root("inst_xray_telescope")
    box(r, "detector", (0, 0, 0.05), (0.20, 0.20, 0.10), "mliGold")
    cyl(r, "tube", (0, 0, 0.40), 0.085, 0.60, "mliSilver")
    tube(r, "mirror_module", (0, 0, 0.66), 0.10, 0.07, 0.10, "paintWhite")
    for i, z in enumerate((0.20, 0.42)):
        tube(r, f"band{i}", (0, 0, z), 0.092, 0.084, 0.02, "frameDark")
    lens(r, "aperture", (0, 0, 0.715), 0.07, axis=(0, 0, 1))
    # 打開的防塵蓋（鉸鏈在一側，翻開約 110°）
    door = cyl(r, "door", (0, 0, 0), 0.10, 0.012, "paintWhite")
    door.location = (0.10 + 0.10 * math.cos(math.radians(70)), 0, 0.72 + 0.10 * math.sin(math.radians(70)))
    door.rotation_euler = (0, math.radians(-70), 0)
    return r


def ais():
    """船舶 AIS 接收器：小盒子 + 兩根 VHF 鞭狀天線。朝地。"""
    r = root("inst_ais")
    box(r, "receiver", (0, 0, -0.035), (0.14, 0.10, 0.07), "paintWhite")
    box(r, "label", (0, -0.051, -0.035), (0.08, 0.004, 0.03), "accent")
    for i, x in enumerate((-0.05, 0.05)):
        cyl(r, f"base{i}", (x, 0, -0.08), 0.014, 0.02, "frameDark", seg=12)
        tip = Vector((x + (0.12 if x > 0 else -0.12), 0, -0.42))
        strut(r, f"whip{i}", (x, 0, -0.09), tip, 0.004, "metal")
        cyl(r, f"tip{i}", tip, 0.008, 0.012, "frameDark", seg=12)
    return r


def particle():
    """高能粒子偵測器（REPTile 型）：一疊偵測器 + 往外斜的準直管（像小望遠鏡）。朝天。"""
    r = root("inst_particle")
    box(r, "stack", (0, 0, 0.05), (0.14, 0.14, 0.10), "frameDark")
    box(r, "lid", (0, 0, 0.105), (0.15, 0.15, 0.01), "paintWhite")
    axis = Vector((0.45, 0, 1)).normalized()
    c = Vector((0, 0, 0.11)) + axis * 0.10
    cyl(r, "collimator", c, 0.04, 0.20, "metal", axis=axis)
    for i, k in enumerate((-0.06, 0.0, 0.06)):
        tube(r, f"fin{i}", c + axis * k, 0.05, 0.04, 0.012, "frameDark", axis=axis)
    lens(r, "aperture", c + axis * 0.105, 0.03, axis=axis)
    return r


def laser_altimeter():
    """雷射測高儀（LOLA 型）：中間大口徑接收望遠鏡 + 旁邊的雷射發射筒與擴束器。朝地。"""
    r = root("inst_laser_altimeter")
    box(r, "bench", (0, 0, -0.04), (0.30, 0.22, 0.08), "mliGold")
    # 接收望遠鏡
    cyl(r, "receiver", (-0.04, 0, -0.16), 0.085, 0.16, "paintWhite")
    tube(r, "receiver_hood", (-0.04, 0, -0.27), 0.09, 0.08, 0.06, "frameDark")
    lens(r, "receiver_lens", (-0.04, 0, -0.245), 0.075)
    # 雷射發射筒（細長）+ 繞射光學元件（分成五道光束）
    cyl(r, "laser", (0.11, 0.04, -0.13), 0.03, 0.10, "metal")
    cyl(r, "expander", (0.11, 0.04, -0.20), 0.022, 0.06, "frameDark", r2=0.032)
    lens(r, "laser_out", (0.11, 0.04, -0.235), 0.026)
    box(r, "radiator", (0, -0.115, -0.05), (0.26, 0.01, 0.10), "paintWhite")
    return r


BUILDERS = [magnetometer, uv_ozone, lightning, microwave_radiometer, xray_telescope, ais, particle, laser_altimeter]


def build():
    coll = bpy.data.collections.get(COLL)
    if coll is not None:
        for o in list(coll.objects):
            bpy.data.objects.remove(o, do_unlink=True)
    else:
        coll = bpy.data.collections.new(COLL)
        bpy.context.scene.collection.children.link(coll)
    roots = []
    for i, f in enumerate(BUILDERS):
        r = f()
        r.location = ((i % 4) * 1.2, -(i // 4) * 1.6, 0)  # 預覽：排成兩列
        roots.append(r)
    return roots


def export(roots):
    os.makedirs(OUT, exist_ok=True)
    for r in roots:
        loc = r.location.copy()
        r.location = (0, 0, 0)
        bpy.context.view_layer.update()
        bpy.ops.object.select_all(action="DESELECT")
        r.select_set(True)
        for c in r.children_recursive:
            c.select_set(True)
        path = os.path.join(OUT, r.name + ".glb")
        bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_format="GLB", export_yup=True,
                                  export_apply=True, export_materials="EXPORT")
        r.location = loc
        print("exported", path)


if __name__ == "__main__":
    roots = build()
    if "--export" in sys.argv:
        export(roots)
