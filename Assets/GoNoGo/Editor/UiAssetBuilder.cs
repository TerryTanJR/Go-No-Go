using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GoNoGo.Game;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 建立 UI Toolkit 需要的資產：字型（TextCore FontAsset）、PanelTextSettings、PanelSettings、主題 .tss、字體大小 FontScale.uss。
    /// 全部放在 Resources/UI，執行時用 Resources.Load 讀，所以場景不必手動指定。
    /// 選單 GoNoGo/Build/UI Assets，或 script eval 呼叫 Build()。可重複執行（已存在就更新）。
    /// </summary>
    public static class UiAssetBuilder
    {
        public const string UiRoot = "Assets/GoNoGo/UI/Resources/UI";
        const string FontSrc = "Assets/GoNoGo/Art/Fonts";
        const string FontDst = UiRoot + "/Fonts";

        [MenuItem("GoNoGo/Build/UI Assets")]
        public static string Build()
        {
            Directory.CreateDirectory(FontDst);
            AssetDatabase.Refresh();

            // 中文用 Noto Sans TC（只放 Regular；粗體由 TextCore 加粗，WebGL 少 5.8 MB）
            var cjk = MakeFont("NotoSansTC-Regular.otf", "CJK", 64, 7, 2048);
            // 英文標題字 Rajdhani：SemiBold 為一般、Bold 為粗體
            var latinBold = MakeFont("Rajdhani-Bold.ttf", "LatinBold", 72, 8, 1024);
            var latin = MakeFont("Rajdhani-SemiBold.ttf", "Latin", 72, 8, 1024);
            // 數字等寬字
            var mono = MakeFont("ShareTechMono-Regular.ttf", "Mono", 72, 8, 512);

            SetFallback(latin, cjk);
            SetFallback(latinBold, cjk);
            SetFallback(mono, cjk);
            latin.fontWeightTable[7].regularTypeface = latinBold; // 700 = bold
            latin.boldStyleWeight = 0.5f;
            cjk.boldStyleWeight = 0.6f;
            EditorUtility.SetDirty(latin);
            EditorUtility.SetDirty(cjk);

            var textSettings = LoadOrCreate<PanelTextSettings>(UiRoot + "/TextSettings.asset");
            // 預設字型改由 Theme.uss 的 :root { -unity-font-definition } 指定（TextSettings.defaultFontAsset 已淘汰）；
            // 這裡只設全域備援：任何字型缺字都會找 Noto Sans TC
            textSettings.fallbackFontAssets = new List<FontAsset> { cjk };
            EditorUtility.SetDirty(textSettings);

            // 主題：Unity 的基本控件樣式（ScrollView 的捲軸等需要）+ 本專案的 Theme.uss
            string tssPath = UiRoot + "/GoNoGoTheme.tss";
            File.WriteAllText(tssPath,
                "@import url(\"unity-theme://default\");\n" +
                "@import url(\"project://database/" + UiRoot + "/Theme.uss\");\n" +
                "@import url(\"project://database/" + UiRoot + "/FontScale.uss\");\n");
            BuildFontScale();
            AssetDatabase.ImportAsset(tssPath, ImportAssetOptions.ForceSynchronousImport);
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(tssPath);

            var panel = LoadOrCreate<PanelSettings>(UiRoot + "/PanelSettings.asset");
            panel.themeStyleSheet = theme;
            panel.textSettings = textSettings;
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 10;
            panel.clearColor = false;
            EditorUtility.SetDirty(panel);

            AssetDatabase.SaveAssets();
            return $"UI assets OK: fonts={latin.name},{latinBold.name},{cjk.name},{mono.name}; theme={(theme != null)}";
        }

        /// <summary>
        /// 產生 FontScale.uss：Theme.uss 裡的字級寫成 var(--fs-N)（N = 100% 時的像素），
        /// 這裡依 <see cref="GameSettings.TextScales"/> 為每個 N 算出各檔大小。
        /// :root 是預設值（世界螢幕也用它），UI 根元素掛 .fs-115 等 class 時換成對應大小。
        /// 40px 以上的大標題、大數字本來就夠大，Theme.uss 直接寫像素、不跟著縮放（放大會撐破固定大小的印章、時鐘）。
        /// 介面（.fs-*）整體再乘 <see cref="UiBaseScale"/>：Theme.uss 的字級是照 1080p 大螢幕排的，筆電上太小；
        /// :root（牆上螢幕用）維持原本大小，那些畫面是照 RenderTexture 的尺寸排的。
        /// </summary>
        public const float UiBaseScale = 1.2f;

        [MenuItem("GoNoGo/Build/UI Font Scale")]
        public static string BuildFontScale()
        {
            string themePath = UiRoot + "/Theme.uss";
            string theme = File.ReadAllText(themePath);
            var sizes = new SortedSet<int>();
            foreach (Match m in Regex.Matches(theme, @"var\(--fs-(\d+)\)")) sizes.Add(int.Parse(m.Groups[1].Value));
            foreach (Match m in Regex.Matches(theme, @"font-size:\s*(\d+)px"))
                if (int.Parse(m.Groups[1].Value) < 40)
                    Debug.LogWarning($"[GoNoGo] Theme.uss 有寫死的 font-size: {m.Groups[1].Value}px，請改成 var(--fs-{m.Groups[1].Value})，字體大小設定才調得到");

            var sb = new StringBuilder();
            sb.Append("/* 自動產生（GoNoGo/Build/UI Font Scale，UiAssetBuilder.BuildFontScale），不要手改。\n");
            sb.Append("   字體大小設定：UI 根元素掛 .fs-{百分比}，所有 var(--fs-N) 一起換大小。 */\n\n");
            Block(sb, ":root", sizes, GameSettings.DefaultTextScale);
            foreach (int pct in GameSettings.TextScales)
                Block(sb, "." + GameSettings.TextScaleClass(pct), sizes, pct * UiBaseScale);

            string path = UiRoot + "/FontScale.uss";
            File.WriteAllText(path, sb.ToString());
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return $"FontScale.uss: {sizes.Count} sizes x {GameSettings.TextScales.Length} steps";
        }

        static void Block(StringBuilder sb, string selector, IEnumerable<int> sizes, float pct)
        {
            sb.Append(selector).Append(" {\n");
            foreach (int n in sizes)
                sb.Append("    --fs-").Append(n).Append(": ").Append(Mathf.RoundToInt(n * pct / 100f)).Append("px;\n");
            sb.Append("}\n");
        }

        static FontAsset MakeFont(string file, string name, int pointSize, int padding, int atlas)
        {
            string dst = $"{FontDst}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<FontAsset>(dst);
            if (existing != null) return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>($"{FontSrc}/{file}");
            if (font == null) throw new System.Exception("找不到字型 " + file);
            var fa = FontAsset.CreateFontAsset(font, pointSize, padding, GlyphRenderMode.SDFAA, atlas, atlas,
                AtlasPopulationMode.Dynamic, true);
            if (fa == null) throw new System.Exception("建立字型資產失敗 " + file);
            fa.name = name;
            AssetDatabase.CreateAsset(fa, dst);
            // 動態字型的圖集與材質要存成子資產，否則重開 Editor 會遺失
            if (fa.atlasTextures != null)
                foreach (var tex in fa.atlasTextures)
                {
                    if (tex == null) continue;
                    tex.name = name + " Atlas";
                    AssetDatabase.AddObjectToAsset(tex, fa);
                }
            if (fa.material != null)
            {
                fa.material.name = name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
            return fa;
        }

        static void SetFallback(FontAsset fa, FontAsset fallback)
        {
            fa.fallbackFontAssetTable = new List<FontAsset> { fallback };
            EditorUtility.SetDirty(fa);
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a != null) return a;
            a = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(a, path);
            return a;
        }
    }
}
