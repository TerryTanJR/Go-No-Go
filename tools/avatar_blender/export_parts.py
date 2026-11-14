"""把所有部件匯出成一個 glb：blender -b -P export_parts.py -- <輸出.glb>
每個部件是一個空物件（原點 = 動畫樞紐），名稱就是 Unity 端查找用的 key。"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import astro as A, bpy

OUTFILE = sys.argv[sys.argv.index("--") + 1]
A.new_scene()
L = A.Look("white", "orange", "black")
A.part_torso(L); A.part_arm(L, -1); A.part_arm(L, 1)
for h in ("dome", "wide", "round"): A.part_head(L, h)
for lo in ("legs", "wheels", "hover", "tracks", "spring"): A.part_lower(L, lo)
for ac in ("backpack", "antenna", "jetpack", "pads"): A.part_acc(L, ac)
for o in bpy.data.objects:
    if o.type == "MESH":
        o.data.materials  # keep
os.makedirs(os.path.dirname(OUTFILE), exist_ok=True)
bpy.ops.export_scene.gltf(filepath=OUTFILE, export_format="GLB", export_apply=True, export_yup=True,
                          export_cameras=False, export_lights=False, export_extras=False)
print("EXPORTED", OUTFILE, [o.name for o in bpy.data.objects if o.type == "EMPTY"])
