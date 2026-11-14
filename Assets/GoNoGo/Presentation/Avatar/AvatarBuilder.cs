using System.Collections.Generic;
using GoNoGo.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// Q 版太空人：大頭（約佔身高 2/5）、短身體，全部用基本幾何體組成，不匯入模型、不用骨架。
    /// 腳底在原點，身高約 1.3 m，面向 +Z。
    /// </summary>
    public static class AvatarBuilder
    {
        public const float Height = 1.3f;

        public static readonly Color[] SuitColors =
        {
            new Color(0.93f, 0.94f, 0.96f), // 白
            new Color(1.00f, 0.50f, 0.13f), // 國際橘
            new Color(0.16f, 0.45f, 0.95f), // 藍
            new Color(0.20f, 0.75f, 0.45f), // 綠
            new Color(0.95f, 0.78f, 0.15f), // 黃
            new Color(0.62f, 0.38f, 0.92f), // 紫
            new Color(0.92f, 0.28f, 0.36f), // 紅
            new Color(0.22f, 0.24f, 0.30f), // 深灰
        };

        public static readonly Color[] VisorColors =
        {
            new Color(0.95f, 0.70f, 0.20f), // 金
            new Color(0.10f, 0.55f, 0.95f), // 藍
            new Color(0.06f, 0.07f, 0.09f), // 黑
            new Color(0.75f, 0.30f, 0.85f), // 彩虹紫
        };

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static Mesh _sphere, _capsule, _cube, _cylinder;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Mats.Clear();

        static Material Mat(Color c, float smooth = 0.35f, float metal = 0f, bool glow = false)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c) + smooth + metal + glow;
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            var baseMat = SatMaterials.Load()?.paintWhite;
            m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = "Avatar_" + key;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            if (glow && m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", c * 2f);
            }
            Mats[key] = m;
            return m;
        }

        // 用 CreatePrimitive 的網格（球直徑 1、膠囊 1×2、圓柱直徑 1 高 2）。
        // Resources.GetBuiltinResource<Mesh>("Sphere.fbx") 在 Unity 6.6 回傳的是直徑 2 的原始 FBX（膠囊 2×4），尺寸全部會大一倍。
        static Mesh Builtin(ref Mesh cache, string name)
        {
            if (cache != null) return cache;
            var type = name.StartsWith("Sphere") ? PrimitiveType.Sphere
                : name.StartsWith("Capsule") ? PrimitiveType.Capsule
                : name.StartsWith("Cylinder") ? PrimitiveType.Cylinder : PrimitiveType.Cube;
            var go = GameObject.CreatePrimitive(type);
            cache = go.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Object.Destroy(go); else Object.DestroyImmediate(go);
            return cache;
        }

        static GameObject Part(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            if (rot != null) go.transform.localRotation = rot.Value;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        public static AvatarRig Build(AvatarLook look, Transform parent)
        {
            look = (look ?? new AvatarLook()).Sanitized();
            var sphere = Builtin(ref _sphere, "Sphere.fbx");
            var capsule = Builtin(ref _capsule, "Capsule.fbx");
            var cube = Builtin(ref _cube, "Cube.fbx");
            var cyl = Builtin(ref _cylinder, "Cylinder.fbx");

            var suit = Mat(SuitColors[look.Suit]);
            var white = Mat(new Color(0.95f, 0.95f, 0.97f));
            var dark = Mat(new Color(0.16f, 0.17f, 0.2f), 0.5f, 0.4f);
            var visor = Mat(VisorColors[look.Visor], 0.95f, 0.6f);
            var accent = Mat(new Color(0.22f, 0.78f, 0.96f), 0.5f, 0f, true);

            var root = new GameObject("Avatar");
            root.transform.SetParent(parent, false);
            var rig = root.AddComponent<AvatarRig>();

            // 身體：髖部為樞紐（走路時整個上下彈）
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            body.localPosition = new Vector3(0, 0.27f, 0);
            rig.body = body;

            Part("Torso", body, sphere, suit, new Vector3(0, 0.2f, 0), new Vector3(0.46f, 0.44f, 0.38f));
            Part("Belt", body, cyl, dark, new Vector3(0, 0.05f, 0), new Vector3(0.44f, 0.03f, 0.36f));
            Part("ChestPanel", body, cube, white, new Vector3(0, 0.22f, 0.17f), new Vector3(0.18f, 0.12f, 0.04f));
            Part("ChestLight", body, sphere, accent, new Vector3(0.05f, 0.24f, 0.195f), Vector3.one * 0.03f);

            // 腿
            rig.legL = Limb("LegL", body, new Vector3(-0.11f, 0.02f, 0), capsule, suit, dark, 0.26f, 0.14f, true);
            rig.legR = Limb("LegR", body, new Vector3(0.11f, 0.02f, 0), capsule, suit, dark, 0.26f, 0.14f, true);
            // 手
            rig.armL = Limb("ArmL", body, new Vector3(-0.25f, 0.3f, 0), capsule, suit, white, 0.26f, 0.11f, false);
            rig.armR = Limb("ArmR", body, new Vector3(0.25f, 0.3f, 0), capsule, suit, white, 0.26f, 0.11f, false);

            // 頭盔（頭約佔身高 2/5）
            var head = new GameObject("Head").transform;
            head.SetParent(body, false);
            head.localPosition = new Vector3(0, 0.66f, 0);
            rig.head = head;
            switch (look.Helmet)
            {
                case 1: // 圓頭盔 + 頂鰭
                    Part("Helmet", head, sphere, white, Vector3.zero, new Vector3(0.56f, 0.54f, 0.54f));
                    Part("Fin", head, cube, suit, new Vector3(0, 0.25f, -0.04f), new Vector3(0.05f, 0.14f, 0.3f));
                    break;
                case 2: // 方頭盔
                    Part("Helmet", head, cube, white, Vector3.zero, new Vector3(0.52f, 0.48f, 0.5f));
                    Part("HelmetTop", head, cube, suit, new Vector3(0, 0.25f, 0), new Vector3(0.44f, 0.04f, 0.42f));
                    break;
                default: // 圓頭盔
                    Part("Helmet", head, sphere, white, Vector3.zero, new Vector3(0.56f, 0.54f, 0.54f));
                    Part("Ring", head, cyl, suit, new Vector3(0, -0.24f, 0), new Vector3(0.42f, 0.03f, 0.42f));
                    break;
            }
            // 面罩要凸出頭盔前緣才看得到（頭盔前緣在 z≈0.27）
            Part("Visor", head, sphere, visor, new Vector3(0, -0.01f, look.Helmet == 2 ? 0.2f : 0.17f), look.Helmet == 2 ? new Vector3(0.42f, 0.28f, 0.16f) : new Vector3(0.44f, 0.32f, 0.24f));
            Part("Ear", head, cyl, suit, new Vector3(-0.275f, 0, 0), new Vector3(0.09f, 0.02f, 0.09f), Quaternion.Euler(0, 0, 90));
            Part("Ear2", head, cyl, suit, new Vector3(0.275f, 0, 0), new Vector3(0.09f, 0.02f, 0.09f), Quaternion.Euler(0, 0, 90));

            // 配件
            switch (look.Accessory)
            {
                case 1: // 背包
                    Part("Backpack", body, cube, white, new Vector3(0, 0.24f, -0.22f), new Vector3(0.36f, 0.36f, 0.16f));
                    Part("BackpackLight", body, cube, accent, new Vector3(0, 0.3f, -0.305f), new Vector3(0.2f, 0.03f, 0.01f));
                    break;
                case 2: // 天線
                    Part("Antenna", head, cyl, dark, new Vector3(0.17f, 0.33f, -0.05f), new Vector3(0.015f, 0.12f, 0.015f));
                    Part("AntennaTip", head, sphere, accent, new Vector3(0.17f, 0.46f, -0.05f), Vector3.one * 0.05f);
                    break;
                case 3: // 肩章
                    Part("PadL", body, cube, suit, new Vector3(-0.24f, 0.36f, 0), new Vector3(0.16f, 0.05f, 0.18f), Quaternion.Euler(0, 0, 18));
                    Part("PadR", body, cube, suit, new Vector3(0.24f, 0.36f, 0), new Vector3(0.16f, 0.05f, 0.18f), Quaternion.Euler(0, 0, -18));
                    Part("StarL", body, cube, accent, new Vector3(-0.25f, 0.39f, 0.05f), new Vector3(0.05f, 0.01f, 0.05f), Quaternion.Euler(0, 45, 18));
                    break;
            }

            // 手上拿東西的位置：胸前
            var hold = new GameObject("Hold").transform;
            hold.SetParent(body, false);
            hold.localPosition = new Vector3(0, 0.26f, 0.42f);
            rig.hold = hold;

            // 影子：腳下一個淡淡的圓
            rig.Init();
            return rig;
        }

        static Transform Limb(string name, Transform parent, Vector3 pivot, Mesh capsule, Material suit, Material end, float len, float thick, bool leg)
        {
            var p = new GameObject(name).transform;
            p.SetParent(parent, false);
            p.localPosition = pivot;
            // 膠囊預設高 2：縮放 y = 長度 / 2，往下掛
            Part(name + "Mesh", p, capsule, suit, new Vector3(0, -len / 2, 0), new Vector3(thick, len / 2, thick));
            if (leg) Part(name + "Boot", p, Builtin(ref _cube, "Cube.fbx"), end, new Vector3(0, -len + 0.02f, 0.03f), new Vector3(thick * 1.15f, 0.08f, thick * 1.5f));
            else Part(name + "Glove", p, Builtin(ref _sphere, "Sphere.fbx"), end, new Vector3(0, -len, 0), Vector3.one * thick * 1.15f);
            return p;
        }
    }
}
