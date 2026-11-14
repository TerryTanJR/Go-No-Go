"""Blender 5.x：全身 Q 版太空人（參考白殼 + 橘條紋 + 大黑面罩 + 天平徽章）。
用法：blender -b -P astro.py -- <輸出資料夾>
"""
import bpy, bmesh, math, sys, os
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = sys.argv[sys.argv.index("--") + 1]
LOGO = os.path.join(HERE, "badge_logo.png")
# 網格細分：預覽圖用高的，匯出給遊戲用低的（環境變數 AVATAR_LOW=1）
LOW = os.environ.get("AVATAR_LOW") == "1"
SEG, CYL, TOR_MAJ, TOR_MIN, BEV = (32, 28, 40, 12, 2) if LOW else (48, 48, 64, 24, 5)

# ---------------------------------------------------------------- 配色
SUITS = {
    "white":  (0.80, 0.79, 0.76),
    "cream":  (0.85, 0.68, 0.38),
    "mint":   (0.22, 0.70, 0.55),
    "sky":    (0.55, 0.72, 0.95),
    "pink":   (0.90, 0.30, 0.45),
    "charcoal": (0.07, 0.08, 0.11),
}
ACCENTS = {
    "orange": (1.0, 0.36, 0.06),
    "cyan":   (0.05, 0.70, 0.95),
    "lime":   (0.55, 0.85, 0.15),
    "violet": (0.55, 0.30, 0.95),
    "red":    (0.90, 0.12, 0.18),
    "gold":   (0.98, 0.75, 0.12),
}

def new_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _cache.clear()
    return bpy.context.scene

_cache = {}
def mat(name, color, rough=0.35, metal=0.0, coat=0.0, emit=None, es=4.0):
    key = (name, color, rough, metal, coat, emit)
    if key in _cache and _cache[key].name in bpy.data.materials:
        return _cache[key]
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    for k in ("Coat Weight", "Clearcoat"):
        if k in b.inputs: b.inputs[k].default_value = coat; break
    if emit:
        k = "Emission Color" if "Emission Color" in b.inputs else "Emission"
        b.inputs[k].default_value = (*emit, 1)
        if "Emission Strength" in b.inputs: b.inputs["Emission Strength"].default_value = es
    _cache[key] = m
    return m

def logo_mat(radius):
    m = bpy.data.materials.new("logo"); m.use_nodes = True
    nt = m.node_tree; b = nt.nodes["Principled BSDF"]
    b.inputs["Roughness"].default_value = 0.4
    tc = nt.nodes.new("ShaderNodeTexCoord")
    mp = nt.nodes.new("ShaderNodeMapping")
    # Object 座標 → UV；徽章外橘圈半徑佔圖寬 0.375
    s = 0.375 / radius
    mp.inputs["Scale"].default_value = (s, s, 1)
    mp.inputs["Location"].default_value = (0.5, 0.5, 0)
    tx = nt.nodes.new("ShaderNodeTexImage")
    tx.image = bpy.data.images.load(LOGO)
    tx.extension = "EXTEND"
    nt.links.new(tc.outputs["Object"], mp.inputs["Vector"])
    nt.links.new(mp.outputs["Vector"], tx.inputs["Vector"])
    nt.links.new(tx.outputs["Color"], b.inputs["Base Color"])
    return m

# ---------------------------------------------------------------- 基本積木
def _fin(o, m, smooth=True):
    o.data.materials.append(m)
    if smooth:
        for p in o.data.polygons: p.use_smooth = True
    return o

def sphere(loc, r, m, scale=(1, 1, 1), rot=(0, 0, 0), seg=SEG):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=seg, ring_count=seg // 2, radius=r, location=loc, rotation=rot)
    o = bpy.context.object; o.scale = scale
    return _fin(o, m)

def cyl(loc, r, d, m, rot=(0, 0, 0), scale=(1, 1, 1), bevel=0.3):
    bpy.ops.mesh.primitive_cylinder_add(vertices=CYL, radius=r, depth=d, location=loc, rotation=rot)
    o = bpy.context.object; o.scale = scale
    if bevel:
        bv = o.modifiers.new("b", "BEVEL"); bv.width = min(r, d / 2) * bevel; bv.segments = BEV
    return _fin(o, m)

def torus(loc, R, r, m, rot=(0, 0, 0), scale=(1, 1, 1)):
    bpy.ops.mesh.primitive_torus_add(major_segments=TOR_MAJ, minor_segments=TOR_MIN, major_radius=R, minor_radius=r, location=loc, rotation=rot)
    o = bpy.context.object; o.scale = scale
    return _fin(o, m)

def stripe(loc, R, r, m, scale, keep):
    """YZ 平面的環，只保留 keep(世界座標) 為真的頂點 → 一段條紋。"""
    o = torus(loc, R, r, m, rot=(math.pi / 2, 0, math.pi / 2), scale=scale)
    bpy.context.view_layer.update()
    bm = bmesh.new(); bm.from_mesh(o.data)
    bad = [v for v in bm.verts if not keep(o.matrix_world @ v.co)]
    bmesh.ops.delete(bm, geom=bad, context="VERTS")
    bm.to_mesh(o.data); bm.free()
    return o

def cone(loc, r1, r2, d, m, rot=(0, 0, 0)):
    bpy.ops.mesh.primitive_cone_add(vertices=CYL, radius1=r1, radius2=r2, depth=d, location=loc, rotation=rot)
    return _fin(bpy.context.object, m)

def box(loc, size, m, rot=(0, 0, 0), bevel=0.25):
    bpy.ops.mesh.primitive_cube_add(location=loc, rotation=rot)
    o = bpy.context.object; o.scale = (size[0] / 2, size[1] / 2, size[2] / 2)
    bv = o.modifiers.new("b", "BEVEL"); bv.width = min(size) * bevel / 2 / max(0.001, 1); bv.segments = BEV
    bv.use_clamp_overlap = True
    return _fin(o, m)

def limb(p1, p2, r, m, r2=None):
    """p1→p2 的圓角桿（兩端球）。"""
    p1, p2 = Vector(p1), Vector(p2)
    d = p2 - p1
    mid = (p1 + p2) / 2
    rot = d.to_track_quat("Z", "Y").to_euler()
    cyl(mid, r, d.length, m, rot=rot, bevel=0)
    sphere(p1, r, m); sphere(p2, r2 or r, m)

# ---------------------------------------------------------------- 分組（匯出時每個部件是一個空物件，原點 = 動畫樞紐）
CUR = []

class group:
    def __init__(self, name, pivot=(0, 0, 0), origin=(0, 0, 0)):
        self.name, self.pivot, self.origin = name, pivot, origin
    def __enter__(self):
        e = bpy.data.objects.new(self.name, None)
        bpy.context.scene.collection.objects.link(e)
        e.location = (self.origin[0] + self.pivot[0], self.origin[1] + self.pivot[1], self.origin[2] + self.pivot[2])
        if CUR:
            e.parent = CUR[-1]; e.matrix_parent_inverse = CUR[-1].matrix_world.inverted()
        bpy.context.view_layer.update()
        CUR.append(e); return e
    def __exit__(self, *a):
        CUR.pop()

_orig_fin = _fin
def _fin(o, m, smooth=True):
    _orig_fin(o, m, smooth)
    if CUR:
        bpy.context.view_layer.update()
        o.parent = CUR[-1]; o.matrix_parent_inverse = CUR[-1].matrix_world.inverted()
    return o

# ---------------------------------------------------------------- 太空人部件
TZ = 0.64; TA, TB, TH = 0.25, 0.21, 0.27
HZ = TZ + TH + 0.24; HR = 0.31
NECK = (0, 0, TZ + TH)

class Look:
    def __init__(self, suit="white", accent="orange", visor="black", origin=(0, 0, 0)):
        S = SUITS[suit]; A = ACCENTS[accent]
        self.origin = origin
        self.suit = mat("suit", S, 0.38, coat=0.6)
        self.acc = mat("accent", A, 0.35, coat=0.5)
        self.glow = mat("accentglow", A, 0.3, emit=A, es=6.0)
        self.dark = mat("dark", (0.07, 0.08, 0.11), 0.3, coat=0.8)
        self.grey = mat("grey", (0.30, 0.32, 0.37), 0.45, metal=0.5)
        self.glove = mat("glove", (0.95, 0.94, 0.9), 0.4, coat=0.5)
        self.tire = mat("tire", (0.07, 0.07, 0.09), 0.7)
        vc = {"black": (0.01, 0.012, 0.025), "gold": (0.55, 0.38, 0.05),
              "blue": (0.02, 0.18, 0.55), "violet": (0.22, 0.05, 0.40)}[visor]
        self.visor = mat("visor", vc, 0.04, coat=1.0)
        self.hl = mat("hl", (0.95, 0.96, 1.0), 0.2, emit=(0.9, 0.93, 1.0), es=1.2)
        self.A = A
    def P(self, x, y, z): return (self.origin[0] + x, self.origin[1] + y, self.origin[2] + z)
    def G(self, name, pivot=(0, 0, 0)): return group(name, pivot, self.origin)

def logo_disc(loc, r, rot, frame):
    """圓盤徽章：頂面有 UV，貼 logo；材質名 'logo'。"""
    bm = bmesh.new()
    bmesh.ops.create_circle(bm, cap_ends=True, radius=r, segments=64)
    bm.faces.ensure_lookup_table(); f = bm.faces[0]
    uv = bm.loops.layers.uv.new("UVMap")
    for l in f.loops:
        l[uv].uv = (l.vert.co.x / r * 0.375 + 0.5, l.vert.co.y / r * 0.375 + 0.5)
    me = bpy.data.meshes.new("logo"); bm.to_mesh(me); bm.free()
    o = bpy.data.objects.new("logo", me)
    bpy.context.scene.collection.objects.link(o)
    o.location = loc; o.rotation_euler = rot
    lm = bpy.data.materials.new("logo"); lm.use_nodes = True
    nt = lm.node_tree; b = nt.nodes["Principled BSDF"]; b.inputs["Roughness"].default_value = 0.4
    tx = nt.nodes.new("ShaderNodeTexImage"); tx.image = bpy.data.images.load(LOGO); tx.extension = "EXTEND"
    nt.links.new(tx.outputs["Color"], b.inputs["Base Color"])
    _fin(o, lm)
    return o

def part_torso(L, logo=True):
    P = L.P
    with L.G("Torso"):
        sphere(P(0, 0, TZ), 1, L.suit, (TA, TB, TH))
        stripe(P(0, 0, TZ), 0.99, 0.14, L.acc, (TB, TH, 0.30),
               lambda w: (TZ - 0.15) < (w.z - L.origin[2]) < (TZ + TH - 0.02))
        torus(P(0, 0, TZ + TH - 0.02), 0.145, 0.05, L.acc)
        torus(P(0, 0, TZ - TH + 0.07), 0.17, 0.028, L.dark, scale=(1.0, 0.88, 1.0))
        if logo:
            br = 0.085; bx, bz = -0.11, TZ + 0.02
            by = -TB * math.sqrt(max(0.0, 1 - (bx / TA) ** 2 - ((bz - TZ) / TH) ** 2)) - 0.004
            n = Vector((bx / TA ** 2, by / TB ** 2, (bz - TZ) / TH ** 2)).normalized()
            rot = Vector((0, 0, 1)).rotation_difference(n).to_euler()
            loc = P(bx, by, bz)
            cyl(loc, br, 0.012, L.acc, rot=rot, bevel=0)
            off = n * 0.0075
            logo_disc((loc[0] + off.x, loc[1] + off.y, loc[2] + off.z), br * 0.97, rot, L.acc)
            torus(loc, br, 0.006, L.acc, rot=rot)

def part_arm(L, side):
    P = L.P; sx = side
    sh = (sx * 0.235, 0, TZ + 0.1); el = (sx * 0.36, -0.02, TZ - 0.04)
    with L.G("ArmL" if sx < 0 else "ArmR", sh):
        limb(P(*sh), P(*el), 0.075, L.suit)
        sphere(P(sx * 0.39, -0.03, TZ - 0.1), 0.085, L.glove)
        torus(P(sx * 0.375, -0.025, TZ - 0.075), 0.075, 0.016, L.acc,
              rot=(Vector(el) - Vector(sh)).to_track_quat("Z", "Y").to_euler())

def part_head(L, kind):
    P = L.P
    with L.G("Head_" + kind, NECK):
        if kind == "dome":
            sphere(P(0, 0, HZ), HR, L.suit, (1.0, 0.98, 0.96)); vis = (P(0, -0.22, HZ - 0.01), (0.205, 0.12, 0.185))
        elif kind == "wide":
            sphere(P(0, 0, HZ), HR, L.suit, (1.12, 0.95, 0.92)); vis = (P(0, -0.21, HZ - 0.01), (0.27, 0.12, 0.17))
        else:
            sphere(P(0, 0, HZ), HR, L.suit, (0.97, 0.97, 1.0)); vis = (P(0, -0.22, HZ), (0.21, 0.12, 0.21))
        sphere(vis[0], 1, L.visor, vis[1])
        box((vis[0][0] - 0.05, vis[0][1] - vis[1][1] * 0.78, vis[0][2] + 0.075), (0.13, 0.008, 0.035), L.hl, rot=(0.12, 0, 0), bevel=0.2)
        stripe(P(0, 0, HZ), 0.99, 0.12, L.acc, (HR * 0.98, HR * 0.96, 0.35),
               lambda w: (w.z - L.origin[2]) > HZ - 0.8 * HR and ((w.y - L.origin[1]) > 0.02 or (w.z - L.origin[2]) > HZ + 0.17))
        for sx in (-1, 1):
            sphere(P(sx * HR, 0, HZ - 0.01), 0.085, L.suit, (0.55, 1.0, 1.0))
            sphere(P(sx * HR * 1.05, 0, HZ - 0.01), 0.06, L.acc, (0.4, 0.9, 0.9))

def part_acc(L, acc):
    P = L.P
    if acc == "backpack":
        with L.G("Acc_backpack"):
            box(P(0, 0.2, TZ + 0.03), (0.34, 0.14, 0.36), L.suit)
            box(P(0, 0.275, TZ + 0.1), (0.2, 0.02, 0.05), L.glow, bevel=0.5)
            for sx in (-1, 1): cyl(P(sx * 0.1, 0.22, TZ - 0.2), 0.04, 0.12, L.grey, bevel=0.2)
    elif acc == "antenna":
        with L.G("Acc_antenna", NECK):
            cyl(P(0.2, 0.02, HZ + HR * 0.9), 0.012, 0.18, L.dark)
            sphere(P(0.2, 0.02, HZ + HR * 0.9 + 0.11), 0.04, L.glow)
    elif acc == "jetpack":
        with L.G("Acc_jetpack"):
            for sx in (-1, 1):
                cyl(P(sx * 0.11, 0.2, TZ), 0.075, 0.38, L.suit, bevel=0.35)
                cone(P(sx * 0.11, 0.2, TZ - 0.24), 0.07, 0.04, 0.09, L.grey)
                sphere(P(sx * 0.11, 0.2, TZ - 0.3), 0.04, L.glow, (1, 1, 1.4))
    elif acc == "pads":
        with L.G("Acc_pads"):
            for sx in (-1, 1):
                sphere(P(sx * 0.27, 0, TZ + 0.2), 0.11, L.acc, (1.2, 1.1, 0.55), rot=(0, sx * 0.4, 0))

def part_lower(L, lower):
    P = L.P; X90 = (0, math.pi / 2, 0)
    with L.G("Lower_" + lower):
        if lower == "legs":
            for sx in (-1, 1):
                hip = (sx * 0.1, 0, TZ - TH + 0.05)
                with L.G("LegL" if sx < 0 else "LegR", hip):
                    limb(P(*hip), P(sx * 0.1, 0, 0.14), 0.085, L.suit)
                    sphere(P(sx * 0.1, -0.05, 0.075), 0.1, L.dark, (1.0, 1.35, 0.8))
                    torus(P(sx * 0.1, 0, 0.2), 0.085, 0.016, L.acc)
        elif lower == "wheels":
            with L.G("Hub"):
                sphere(P(0, 0, 0.36), 0.2, L.suit, (1.0, 0.85, 0.7))
                cyl(P(0, 0, 0.2), 0.04, 0.5, L.grey, rot=X90, bevel=0.3)
            for sx in (-1, 1):
                with L.G("Spin_WheelL" if sx < 0 else "Spin_WheelR", (sx * 0.26, 0, 0.2)):
                    cyl(P(sx * 0.26, 0, 0.2), 0.2, 0.13, L.tire, rot=X90, bevel=0.45)
                    cyl(P(sx * 0.33, 0, 0.2), 0.12, 0.03, L.acc, rot=X90, bevel=0.3)
        elif lower == "hover":
            with L.G("Hub"):
                sphere(P(0, 0, 0.36), 0.2, L.suit, (1.0, 0.9, 0.7))
                cone(P(0, 0, 0.23), 0.19, 0.1, 0.2, L.grey)
                torus(P(0, 0, 0.3), 0.16, 0.025, L.acc)
            with L.G("Flame", (0, 0, 0.12)):
                cyl(P(0, 0, 0.1), 0.13, 0.03, L.glow, bevel=0.3)
                cone(P(0, 0, 0.05), 0.12, 0.02, 0.12, mat("flame", L.A, 0.5, emit=L.A, es=3.0))
        elif lower == "tracks":
            with L.G("Hub"):
                sphere(P(0, 0, 0.36), 0.22, L.suit, (1.0, 0.85, 0.65))
            for sx in (-1, 1):
                nm = "L" if sx < 0 else "R"
                with L.G("Tread" + nm, (sx * 0.2, 0, 0.14)):
                    box(P(sx * 0.2, 0, 0.14), (0.14, 0.46, 0.24), L.tire, bevel=0.9)
                    for i, yy in enumerate((-0.22, -0.11, 0.0, 0.11, 0.22)):
                        with L.G(f"Spin_Roller{nm}{i}", (sx * 0.2, yy, 0.14)):
                            cyl(P(sx * 0.2, yy, 0.14), 0.07 if i not in (0, 4) else 0.115, 0.16, L.grey if i not in (0, 4) else L.tire,
                                rot=X90, bevel=0.2)
        elif lower == "spring":
            with L.G("Hub"):
                sphere(P(0, 0, 0.36), 0.2, L.suit, (1.0, 0.9, 0.7))
            with L.G("Spring", (0, 0, 0.32)):
                for i in range(6):
                    torus(P(0, 0, 0.3 - i * 0.045), 0.1 - 0.004 * i, 0.016, L.grey)
                sphere(P(0, 0, 0.03), 0.13, L.acc, (1, 1, 0.45))

def build(suit="white", accent="orange", lower="legs", helmet="dome", visor="black",
          acc="backpack", origin=(0, 0, 0), logo=True):
    L = Look(suit, accent, visor, origin)
    part_torso(L, logo); part_arm(L, -1); part_arm(L, 1)
    part_head(L, helmet); part_lower(L, lower)
    if acc != "none": part_acc(L, acc)

def platform(origin, glow=(0.1, 0.7, 1.0)):
    ox, oy, oz = origin
    cyl((ox, oy, oz - 0.04), 0.46, 0.08, mat("plat", (0.03, 0.05, 0.09), 0.3, metal=0.6), bevel=0.2)
    torus((ox, oy, oz + 0.002), 0.43, 0.012, mat("ring", glow, 0.3, emit=glow, es=8.0))

def setup(sc, cam_loc, target, lens, res, samples=64, ortho=None):
    sc.render.engine = "CYCLES"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    w = bpy.data.worlds.new("w"); sc.world = w; w.use_nodes = True
    bg = w.node_tree.nodes["Background"]
    bg.inputs[0].default_value = (0.012, 0.02, 0.05, 1); bg.inputs[1].default_value = 1.0
    cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam)
    cam.location = cam_loc; cam.data.lens = lens
    d = Vector(target) - Vector(cam_loc)
    cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    sc.camera = cam
    def light(name, kind, loc, energy, color=(1, 1, 1), size=2.0):
        ld = bpy.data.lights.new(name, kind); ld.energy = energy; ld.color = color
        if kind == "AREA": ld.size = size
        o = bpy.data.objects.new(name, ld); sc.collection.objects.link(o); o.location = loc
        o.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    light("key", "AREA", (-2.2, -3.2, 3.2), 380, (1.0, 0.96, 0.92), 3.0)
    light("rim", "AREA", (2.6, 2.5, 2.0), 420, (0.35, 0.65, 1.0), 2.5)
    light("fill", "AREA", (3.0, -2.5, 1.0), 110, (1.0, 0.85, 0.75), 3.0)

def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)

if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    # ------------- 1) 英雄圖
    sc = new_scene()
    build("white", "orange", "legs", "dome", "black", "backpack")
    platform((0, 0, 0))
    setup(sc, (-1.7, -3.6, 1.1), (0, 0, 0.7), 62, (1100, 1300), 96)
    render(os.path.join(OUT, "hero_front.png"))
    # ------------- 2) 配色 + 下半身陣容
    sc = new_scene()
    combos = [
        ("white",    "orange", "legs",   "dome",  "black"),
        ("mint",     "cyan",   "wheels", "wide",  "blue"),
        ("pink",     "violet", "hover",  "round", "violet"),
        ("cream",    "red",    "tracks", "dome",  "gold"),
        ("charcoal", "lime",   "spring", "wide",  "black"),
    ]
    accs = ["backpack", "antenna", "none", "pads", "jetpack"]
    for i, (s, a, l, h, v) in enumerate(combos):
        o = ((i - 2) * 1.05, 0, 0)
        build(s, a, l, h, v, accs[i], origin=o)
        platform(o, glow=ACCENTS[a])
    setup(sc, (0, -7.6, 1.15), (0, 0, 0.72), 50, (2000, 800), 72)
    render(os.path.join(OUT, "lineup.png"))
    # ------------- 3) 背面
    sc = new_scene()
    build("white", "orange", "legs", "dome", "black", "jetpack")
    platform((0, 0, 0))
    setup(sc, (1.7, 3.6, 1.1), (0, 0, 0.7), 62, (1100, 1300), 64)
    render(os.path.join(OUT, "hero_back.png"))
