using System.IO;
using GoNoGo.Core;
using GoNoGo.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 不進 Play 模式就把 avatar 的各種外型、動作逐幀渲染成圖，用來檢查模型和動畫。
    /// Editor 選單 GoNoGo/Avatar，或 `unity command script eval 'GoNoGo.EditorTools.AvatarPreviewTool.Sheet("/path/out.png")'`。
    /// </summary>
    public static class AvatarPreviewTool
    {
        const int Cell = 320;

        [MenuItem("GoNoGo/Avatar/Render Motion Sheet")]
        public static void SheetMenu() => Sheet(Path.Combine(Application.dataPath, "../Screenshots/avatar_motion.png"));

        /// <summary>列 = 5 種下半身；欄 = 待機、走路 0.0/0.15/0.3 s、跑、抱零件、安裝。</summary>
        public static string Sheet(string path)
        {
            string[] cols = { "idle", "walk a", "walk b", "walk c", "run", "hold", "install", "hold side", "install side" };
            int rows = AvatarLook.Lowers;
            var rt = new RenderTexture(Cell * cols.Length, Cell * rows, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            var root = new GameObject("__AvatarSheet");
            root.transform.position = new Vector3(0, 5000, 0); // 離使用者場景遠一點，免得拍到場景物件
            var oldMode = RenderSettings.ambientMode; var oldAmb = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.55f);
            try
            {
                var camGo = new GameObject("cam", typeof(Camera));
                camGo.transform.SetParent(root.transform);
                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.05f, 0.07f, 0.12f);
                cam.orthographic = true;
                cam.targetTexture = rt;
                cam.nearClipPlane = 0.1f; cam.farClipPlane = 60f;
                var lg = new GameObject("light", typeof(Light)); lg.transform.SetParent(root.transform);
                lg.transform.rotation = Quaternion.Euler(35, 160, 0); lg.GetComponent<Light>().intensity = 1.1f; lg.GetComponent<Light>().shadows = LightShadows.None;
                // 每格一隻 avatar；整張圖用一台正交相機分格渲染（viewport rect）
                for (int r = 0; r < rows; r++)
                {
                    for (int c = 0; c < cols.Length; c++)
                    {
                        var look = new AvatarLook { Name = "T", Suit = (r * 2) % 8, Accent = r, Helmet = r % 3, Visor = r % 4, Accessory = 1 + r % 4, Lower = r };
                        var rig = GoNoGo.Presentation.AvatarBuilder.Build(look, root.transform);
                        rig.transform.localPosition = Vector3.zero;
                        rig.transform.localRotation = Quaternion.Euler(0, c >= 7 ? 90 : 25, 0); // 稍微側前方看（avatar 面向 +Z，鏡頭在 +Z 側）
                        Drive(rig, c);
                        cam.orthographicSize = 0.95f;
                        camGo.transform.localPosition = new Vector3(0, 0.7f, 5);
                        camGo.transform.rotation = Quaternion.Euler(0, 180, 0);
                        cam.rect = new Rect((float)c / cols.Length, 1f - (float)(r + 1) / rows, 1f / cols.Length, 1f / rows);
                        cam.aspect = 1f;
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.Render();
                        Object.DestroyImmediate(rig.gameObject);
                    }
                }
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmb;
                Object.DestroyImmediate(root);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
            return path;
        }

        /// <summary>單隻大圖（正面偏 25 度），檢查細節與徽章。</summary>
        public static string Portrait(string path, int lower = 0, int suit = 0, int accent = 0, int helmet = 0, int visor = 2, int acc = 1)
        {
            var rt = new RenderTexture(900, 1100, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            var root = new GameObject("__AvatarPortrait");
            root.transform.position = new Vector3(0, 5000, 0);
            var oldMode = RenderSettings.ambientMode; var oldAmb = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.45f, 0.48f, 0.55f);
            try
            {
                var camGo = new GameObject("cam", typeof(Camera)); camGo.transform.SetParent(root.transform);
                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.05f, 0.07f, 0.12f);
                cam.orthographic = true; cam.orthographicSize = 0.78f; cam.targetTexture = rt;
                camGo.transform.localPosition = new Vector3(0, 0.62f, 5); camGo.transform.rotation = Quaternion.Euler(0, 180, 0);
                var lg = new GameObject("light", typeof(Light)); lg.transform.SetParent(root.transform);
                lg.transform.rotation = Quaternion.Euler(35, 160, 0); lg.GetComponent<Light>().intensity = 1.1f; lg.GetComponent<Light>().shadows = LightShadows.None;
                var look = new AvatarLook { Name = "T", Suit = suit, Accent = accent, Helmet = helmet, Visor = visor, Accessory = acc, Lower = lower };
                var rig = GoNoGo.Presentation.AvatarBuilder.Build(look, root.transform);
                rig.transform.localRotation = Quaternion.Euler(0, 25, 0);
                Run(rig, 0.5f);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
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

        /// <summary>第一人稱視角（和遊戲裡同樣的相機位置、近裁切面 0.05）：每種下半身 × 走動中的 6 個時間點，平視和低頭各一列。</summary>
        public static string FirstPerson(string path, int lower, float pitch)
        {
            const int n = 6, W = 360, H = 240;
            var rt = new RenderTexture(W * n, H, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
            var root = new GameObject("__AvatarFP");
            root.transform.position = new Vector3(0, 5000, 0);
            var oldMode = RenderSettings.ambientMode; var oldAmb = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.58f);
            try
            {
                var camGo = new GameObject("cam", typeof(Camera)); camGo.transform.SetParent(root.transform);
                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.3f, 0.33f, 0.4f);
                cam.fieldOfView = 70; cam.nearClipPlane = 0.05f; cam.farClipPlane = 50; cam.targetTexture = rt;
                camGo.transform.localPosition = new Vector3(0, 1.0f, 0.12f);
                camGo.transform.localRotation = Quaternion.Euler(pitch, 0, 0);
                var lg = new GameObject("light", typeof(Light)); lg.transform.SetParent(root.transform);
                lg.transform.rotation = Quaternion.Euler(50, 20, 0); lg.GetComponent<Light>().intensity = 1.1f; lg.GetComponent<Light>().shadows = LightShadows.None;
                var floor = GameObject.CreatePrimitive(PrimitiveType.Plane); floor.transform.SetParent(root.transform);
                floor.transform.localPosition = Vector3.zero; floor.transform.localScale = Vector3.one * 3;
                floor.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = new Color(0.2f, 0.22f, 0.3f) };
                Object.DestroyImmediate(floor.GetComponent<Collider>());
                var rig = GoNoGo.Presentation.AvatarBuilder.Build(new AvatarLook { Lower = lower, Accessory = 1 }, root.transform);
                rig.hideHead = true; rig.speed = 2.4f;
                Run(rig, 0.4f);
                for (int i = 0; i < n; i++)
                {
                    cam.rect = new Rect((float)i / n, 0, 1f / n, 1f); cam.aspect = (float)W / H;
                    cam.Render();
                    Run(rig, 0.11f);
                }
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
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

        static void Shot(string path, AvatarLook look, float yaw, float centerY, float size, int px, bool transparent)
        {
            var rt = new RenderTexture(px, px, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(px, px, TextureFormat.RGBA32, false);
            var root = new GameObject("__AvatarShot");
            root.transform.position = new Vector3(0, 5000, 0);
            var oldMode = RenderSettings.ambientMode; var oldAmb = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.5f, 0.52f, 0.58f);
            try
            {
                var camGo = new GameObject("cam", typeof(Camera)); camGo.transform.SetParent(root.transform);
                var cam = camGo.GetComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = transparent ? new Color(0, 0, 0, 0) : new Color(0.05f, 0.07f, 0.12f);
                cam.orthographic = true; cam.orthographicSize = size; cam.targetTexture = rt;
                camGo.transform.localPosition = new Vector3(0, centerY, 5); camGo.transform.rotation = Quaternion.Euler(0, 180, 0);
                var lg = new GameObject("light", typeof(Light)); lg.transform.SetParent(root.transform);
                lg.transform.rotation = Quaternion.Euler(35, 160, 0); lg.GetComponent<Light>().intensity = 1.1f; lg.GetComponent<Light>().shadows = LightShadows.None;
                var rig = GoNoGo.Presentation.AvatarBuilder.Build(look, root.transform);
                rig.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                Run(rig, 0.5f);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, px, px), 0, 0); tex.Apply();
                RenderTexture.active = null;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderSettings.ambientMode = oldMode; RenderSettings.ambientLight = oldAmb;
                Object.DestroyImmediate(root); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
            }
        }

        [MenuItem("GoNoGo/Avatar/Render Option Thumbnails")]
        public static void ThumbsMenu() => Thumbnails(Path.Combine(Application.dataPath, "GoNoGo/UI/Resources/UI/AvatarOptions"));

        /// <summary>編輯 avatar 面板上每個選項的縮圖（透明背景）：頭盔、下半身、配件。</summary>
        public static string Thumbnails(string dir)
        {
            for (int h = 0; h < AvatarLook.Helmets; h++)
                Shot(Path.Combine(dir, $"helmet_{h}.png"), new AvatarLook { Helmet = h, Accessory = 0 }, 22, 1.02f, 0.36f, 256, true);
            for (int l = 0; l < AvatarLook.Lowers; l++)
                Shot(Path.Combine(dir, $"lower_{l}.png"), new AvatarLook { Lower = l, Accessory = 0 }, 22, 0.34f, 0.42f, 256, true);
            for (int a = 0; a < AvatarLook.Accessories; a++)
                Shot(Path.Combine(dir, $"acc_{a}.png"), new AvatarLook { Accessory = a, Accent = 1 }, a == 0 ? 22 : 150, 0.78f, 0.62f, 256, true);
            AssetDatabase.Refresh();
            return dir;
        }

        static void Drive(AvatarRig rig, int col)
        {
            switch (col)
            {
                case 0: rig.speed = 0; Run(rig, 0.8f); break;
                case 1: rig.speed = 2.4f; Run(rig, 0.6f); break;
                case 2: rig.speed = 2.4f; Run(rig, 0.75f); break;
                case 3: rig.speed = 2.4f; Run(rig, 0.9f); break;
                case 4: rig.speed = 4.4f; Run(rig, 1.2f); break;
                case 5: case 7: rig.speed = 0; rig.holding = true; Run(rig, 0.8f); break;
                case 6: case 8: rig.speed = 0; rig.installing = true; Run(rig, 0.8f); break;
            }
        }

        static void Run(AvatarRig rig, float seconds)
        {
            for (float t = 0; t < seconds; t += 1f / 60f) rig.Tick(1f / 60f);
        }
    }
}
