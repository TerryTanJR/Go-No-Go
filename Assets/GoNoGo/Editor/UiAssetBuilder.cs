using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 建立 UI Toolkit 需要的資產：字型（TextCore FontAsset）、PanelTextSettings、PanelSettings、主題 .tss。
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
                "@import url(\"project://database/" + UiRoot + "/Theme.uss\");\n");
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
