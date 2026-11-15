using System.IO;
using GoNoGo.Core;
using GoNoGo.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 任務簡報的飛行主任 Stephen：用玩家同一套角色模型，加上長官的特徵（簷帽、金色肩章），
    /// 渲染成去背半身像，存成 Resources/UI/Portraits/director.png。
    /// Editor 選單 GoNoGo/Avatar/Render Director Portrait。
    /// </summary>
    public static class DirectorPortraitTool
    {
        const string OutPath = "Assets/GoNoGo/UI/Resources/UI/Portraits/director.png";

        [MenuItem("GoNoGo/Avatar/Render Director Portrait")]
        public static void RenderMenu() { Render(OutPath); AssetDatabase.ImportAsset(OutPath); }

        static Material M(Color c, float smooth = 0.4f, float metal = 0f, bool glow = false)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = c };
            m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", metal);
            if (glow) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 1.4f); }
            return m;
        }

        static GameObject Prim(PrimitiveType t, Transform parent, Vector3 worldPos, Vector3 worldScale, Material mat, Vector3? euler = null)
        {
            var g = GameObject.CreatePrimitive(t);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, true);  // parent 一律用 root（縮放 1），位置、大小都是世界座標
            g.transform.position = worldPos;
            g.transform.rotation = Quaternion.Euler(0, YawNow, 0) * Quaternion.Euler(euler ?? Vector3.zero);
            g.transform.localScale = worldScale;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        static float YawNow;
        static Bounds BoundsOf(Transform t)
        {
            Bounds b = default; bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            { if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
            return b;
        }

        public static string Render(string path, float yaw = 14f)
        {
            const int W = 900, H = 800;
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            var root = new GameObject("__DirectorPortrait");
            root.transform.position = new Vector3(0, 5000, 0);
            var oldMode = RenderSettings.ambientMode; var oldAmb = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.5f, 0.53f, 0.6f);
            try
            {
                // 深藍制服 + 青色點綴（和簡報畫面、舊肖像同色系）；藍色面罩
                var look = new AvatarLook { Name = "Stephen", Suit = 0, Accent = 5, Helmet = 0, Visor = 1, Accessory = 0, Lower = 0 };
                var rig = GoNoGo.Presentation.AvatarBuilder.Build(look, root.transform);
                YawNow = yaw; rig.transform.localRotation = Quaternion.Euler(0, yaw, 0);

                // 面罩上的白色高光（頭部模型裡名叫 Cube 的小方塊）不要
                foreach (var r in rig.head.GetComponentsInChildren<Renderer>(true))
                    if (r.name.StartsWith("Cube")) r.enabled = false;
                var head = BoundsOf(rig.head);
                var all = BoundsOf(rig.transform);
                float hr = head.extents.x;                       // 頭半寬
                Vector3 hc = head.center;
                var dark = M(new Color(0.06f, 0.09f, 0.15f), 0.5f, 0.3f);
                var cyan = M(new Color(0.22f, 0.78f, 0.96f), 0.5f, 0f, true);
                var gold = M(new Color(0.96f, 0.78f, 0.25f), 0.7f, 0.8f);
                var navy = M(new Color(0.10f, 0.19f, 0.32f), 0.45f);

                // --- 軍官帽（簷帽）：藏青帽身 + 金色帽帶 + 帽簷 + 帽徽 ---
                Vector3 side = rig.transform.right, up = Vector3.up, fwd = rig.transform.forward;
                Vector3 capC = hc + up * (hr * 0.64f) - fwd * (hr * 0.02f);
                Prim(PrimitiveType.Cylinder, root.transform, capC, new Vector3(hr * 1.42f, hr * 0.17f, hr * 1.42f), navy, new Vector3(-4, 0, 0));
                Prim(PrimitiveType.Cylinder, root.transform, capC + up * (hr * 0.15f), new Vector3(hr * 1.62f, hr * 0.045f, hr * 1.62f), navy, new Vector3(-4, 0, 0));
                Prim(PrimitiveType.Cylinder, root.transform, capC - up * (hr * 0.10f), new Vector3(hr * 1.47f, hr * 0.05f, hr * 1.47f), gold, new Vector3(-4, 0, 0));
                Prim(PrimitiveType.Sphere, root.transform, capC - up * (hr * 0.14f) + fwd * (hr * 0.52f), new Vector3(hr * 1.15f, hr * 0.05f, hr * 0.72f), dark);
                Prim(PrimitiveType.Sphere, root.transform, capC + fwd * (hr * 0.74f) + up * (hr * 0.02f), new Vector3(hr * 0.26f, hr * 0.26f, hr * 0.06f), gold);
                Prim(PrimitiveType.Sphere, root.transform, capC + fwd * (hr * 0.78f) + up * (hr * 0.02f), new Vector3(hr * 0.13f, hr * 0.13f, hr * 0.05f), navy);
                // --- 肩章（金色橫條）+ 名牌 + 胸口階級條 ---
                var torso = BoundsOf(rig.body.Find("Torso"));
                var armL = BoundsOf(rig.armL); var armR = BoundsOf(rig.armR);
                // 肩章：蓋在兩隻手臂的肩頭上
                foreach (var ab in new[] { armL, armR })
                {
                    int s = ab.center.x < torso.center.x ? -1 : 1;
                    Vector3 p = new Vector3(ab.center.x, ab.max.y - ab.size.y * 0.12f, ab.center.z);
                    var ep = Prim(PrimitiveType.Cube, root.transform, p, new Vector3(ab.size.x * 0.7f, ab.size.y * 0.08f, ab.size.z * 0.6f), navy, new Vector3(0, 0, s * -28));
                    for (int k = 0; k < 3; k++)
                        Prim(PrimitiveType.Cube, root.transform, p + ep.transform.up * (ab.size.y * 0.04f) + fwd * ((k - 1) * ab.size.z * 0.2f),
                            new Vector3(ab.size.x * 0.64f, ab.size.y * 0.03f, ab.size.z * 0.07f), gold, new Vector3(0, 0, s * -28));
                }
                // --- 相機：半身像，背景透明 ---
                var camGo = new GameObject("cam", typeof(Camera)); camGo.transform.SetParent(root.transform);
                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
                cam.orthographic = true; cam.targetTexture = rt;
                float top = head.max.y + hr * 0.5f;
                float bottom = torso.center.y - torso.size.y * 0.35f;
                float h = top - bottom;
                cam.orthographicSize = Mathf.Max(h * 0.5f, all.size.x * 0.5f * H / W * 1.05f);
                float cy = (top + bottom) * 0.5f; cy = top - cam.orthographicSize;
                camGo.transform.position = new Vector3(hc.x, cy, hc.z + 5);
                camGo.transform.rotation = Quaternion.Euler(0, 180, 0);
                var lg = new GameObject("light", typeof(Light)); lg.transform.SetParent(root.transform);
                lg.transform.rotation = Quaternion.Euler(30, 160, 0); var l = lg.GetComponent<Light>(); l.intensity = 1.15f; l.shadows = LightShadows.None;

                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, W, H), 0, 0); tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmb;
                Object.DestroyImmediate(root); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            return path;
        }
    }
}
