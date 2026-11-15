using System.Collections.Generic;
using GoNoGo.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// Q 版太空人：用 Blender 做的部件（Resources/Avatar/AvatarParts.glb，原始碼在 tools/avatar_blender）組裝，
    /// 顏色在這裡依 <see cref="AvatarLook"/> 動態上色，不用骨架（動畫見 <see cref="AvatarRig"/>）。
    /// 腳底在原點，身高約 1.3 m，面向 +Z。設施裡再放大（<see cref="WorldScale"/>）。
    /// </summary>
    public static class AvatarBuilder
    {
        /// <summary>glb 部件的尺寸（頭頂約 1.46 m）縮到約 1.3 m。</summary>
        public const float ModelScale = 0.89f;

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

        public static readonly Color[] AccentColors =
        {
            new Color(1.00f, 0.36f, 0.06f), // 國際橘
            new Color(0.05f, 0.70f, 0.95f), // 青
            new Color(0.55f, 0.85f, 0.15f), // 萊姆綠
            new Color(0.55f, 0.30f, 0.95f), // 紫
            new Color(0.90f, 0.12f, 0.18f), // 紅
            new Color(0.98f, 0.75f, 0.12f), // 金
            new Color(1.00f, 0.45f, 0.70f), // 粉
            new Color(0.92f, 0.93f, 0.96f), // 白
        };

        public static readonly string[] HelmetKeys = { "dome", "wide", "round" };
        public static readonly string[] AccessoryKeys = { null, "backpack", "antenna", "pads", "jetpack" };
        public static readonly string[] LowerKeys = { "legs", "wheels", "hover", "tracks", "spring" };

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        static GameObject _parts;
        static Material _logo;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Mats.Clear(); _parts = null; _logo = null; }

        static Material Mat(Color c, float smooth = 0.35f, float metal = 0f, bool glow = false)
        {
            string key = ColorUtility.ToHtmlStringRGBA(c) + smooth + metal + glow;
            if (Mats.TryGetValue(key, out var m) && m != null) return m;
            m = NewMat("Avatar_" + key);
            m.SetColor("_BaseColor", c);
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

        static Material NewMat(string name)
        {
            var baseMat = SatMaterials.Load()?.paintWhite;
            var m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.name = name;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            return m;
        }

        static Material LogoMat()
        {
            if (_logo != null) return _logo;
            _logo = NewMat("Avatar_Logo");
            _logo.SetColor("_BaseColor", Color.white);
            _logo.SetTexture("_BaseMap", Resources.Load<Texture2D>("Avatar/badge_logo"));
            if (_logo.HasProperty("_Smoothness")) _logo.SetFloat("_Smoothness", 0.4f);
            return _logo;
        }

        static GameObject Parts => _parts != null ? _parts : (_parts = Resources.Load<GameObject>("Avatar/AvatarParts"));

        /// <summary>glb 材質名稱（Blender 裡的部位名）→ 這個外型用的材質。</summary>
        static Material RoleMat(string role, AvatarLook look)
        {
            var accent = AccentColors[look.Accent];
            switch (role)
            {
                case "suit": return Mat(SuitColors[look.Suit], 0.5f);
                case "accent": return Mat(accent, 0.45f);
                case "accentglow": case "flame": return Mat(accent, 0.5f, 0f, true);
                case "dark": return Mat(new Color(0.07f, 0.08f, 0.11f), 0.6f, 0.4f);
                case "grey": return Mat(new Color(0.30f, 0.32f, 0.37f), 0.5f, 0.5f);
                case "glove": return Mat(new Color(0.95f, 0.94f, 0.90f), 0.45f);
                case "tire": return Mat(new Color(0.07f, 0.07f, 0.09f), 0.15f);
                case "visor": return Mat(VisorColors[look.Visor], 0.95f, 0.6f);
                case "hl": return Mat(new Color(0.95f, 0.96f, 1f), 0.9f, 0f, true);
                case "logo": return LogoMat();
            }
            return null;
        }

        static void Paint(GameObject go, AvatarLook look)
        {
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                var ms = r.sharedMaterials;
                for (int i = 0; i < ms.Length; i++)
                {
                    string n = ms[i] != null ? ms[i].name : "";
                    int dot = n.IndexOf('.'); if (dot > 0) n = n.Substring(0, dot);
                    var m = RoleMat(n, look);
                    if (m != null) ms[i] = m;
                }
                r.sharedMaterials = ms;
                r.shadowCastingMode = ShadowCastingMode.On;
            }
        }

        /// <summary>從 glb 複製一個部件掛到 parent 底下（保留它在 glb 裡的位置，原點 = 動畫樞紐）。</summary>
        static Transform Take(string name, Transform parent, AvatarLook look)
        {
            var src = Parts != null ? Parts.transform.Find(name) : null;
            if (src == null) { Debug.LogError("[Avatar] glb 裡找不到部件：" + name); return new GameObject(name).transform; }
            var go = Object.Instantiate(src.gameObject, parent);
            go.name = name;
            go.transform.localPosition = src.localPosition;
            go.transform.localRotation = src.localRotation;
            go.transform.localScale = src.localScale;
            Paint(go, look);
            return go.transform;
        }

        static void Reparent(Transform t, Transform parent) => t.SetParent(parent, true);

        /// <summary>
        /// 設施裡的角色比選單裡的模型大多少：模型站起來約 1.2 m，設施裡放大到約 1.5 m
        /// （中控台、螢幕、架子是照真人尺寸做的，角色太矮看起來像小孩）。
        /// </summary>
        public const float WorldScale = 1.25f;
        /// <summary>設施裡角色的身高（m）：碰撞體、頭上的名牌用。</summary>
        public const float WorldHeight = 1.5f;

        /// <summary>把角色放大到設施裡的尺寸；手上拿的東西維持原本的大小。</summary>
        public static void ApplyWorldScale(AvatarRig rig)
        {
            rig.transform.localScale = Vector3.one * WorldScale;
            if (rig.hold != null) rig.hold.localScale = Vector3.one / (ModelScale * WorldScale);
        }

        public static AvatarRig Build(AvatarLook look, Transform parent)
        {
            look = (look ?? new AvatarLook()).Sanitized();

            var root = new GameObject("Avatar");
            root.transform.SetParent(parent, false);
            var rig = root.AddComponent<AvatarRig>();

            // 所有部件放在 Model 底下（縮到遊戲尺寸；外面的 root 的縮放留給彈跳、選單縮放用）
            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);
            model.localScale = Vector3.one * ModelScale;

            // 身體樞紐在髖部（走路時上下彈、前傾）
            var body = new GameObject("Body").transform;
            body.SetParent(model, false);
            body.localPosition = new Vector3(0, 0.4f, 0);
            rig.body = body;

            Reparent(Take("Torso", model, look), body);
            rig.head = Take("Head_" + HelmetKeys[look.Helmet], model, look); Reparent(rig.head, body);
            rig.armL = Take("ArmL", model, look); Reparent(rig.armL, body);
            rig.armR = Take("ArmR", model, look); Reparent(rig.armR, body);

            string acc = AccessoryKeys[look.Accessory];
            if (acc != null)
            {
                var a = Take("Acc_" + acc, model, look);
                Reparent(a, acc == "antenna" ? rig.head : body);
            }

            // 下半身：懸浮、彈簧整個跟著身體動；腳、輪子、履帶留在地上
            rig.lower = look.Lower;
            var lower = Take("Lower_" + LowerKeys[look.Lower], model, look);
            bool follows = look.Lower == 2 || look.Lower == 4;
            Reparent(lower, follows ? body : model);
            rig.legL = lower.Find("LegL");
            rig.legR = lower.Find("LegR");
            rig.flame = lower.Find("Flame");
            rig.spring = lower.Find("Spring");
            var spin = new List<Transform>();
            foreach (var t in lower.GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("Spin_")) spin.Add(t);
            rig.spinners = spin.ToArray();
            rig.spinRadius = look.Lower == 1 ? 0.2f : 0.09f;

            // 手上拿東西的位置：胸前（hold 在縮放過的 Model 底下，所以除回來，零件大小才和以前一樣）
            var hold = new GameObject("Hold").transform;
            hold.SetParent(body, false);
            hold.localPosition = new Vector3(0, 0.26f, 0.45f);
            hold.localScale = Vector3.one / ModelScale;
            rig.hold = hold;

            rig.Init();
            return rig;
        }
    }
}
