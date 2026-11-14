using System.Linq;
using UnityEditor;
using UnityEngine.TextCore.Text;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// UI 字型（Assets/GoNoGo/UI/Resources/UI/Fonts）是動態的：顯示文字時才把字畫進圖集，存檔時會連圖集一起寫進 .asset，
    /// 光 CJK 就有十幾 MB，每次 Play 完 git 都會看到改動。存檔前先清空，檔案裡永遠是空的版本；執行時照常動態畫字。
    /// Play 中存檔（自動存檔）先略過這些字型，離開 Play 時清空，之後的存檔寫進去的就是空的。
    /// </summary>
    [InitializeOnLoad]
    public sealed class ClearDynamicFonts : AssetModificationProcessor
    {
        static ClearDynamicFonts()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredEditMode) return;
                foreach (var guid in AssetDatabase.FindAssets("t:FontAsset", new[] { "Assets" }))
                    Clear(AssetDatabase.LoadAssetAtPath<FontAsset>(AssetDatabase.GUIDToAssetPath(guid)));
            };
        }

        static string[] OnWillSaveAssets(string[] paths)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return paths.Where(p => !IsDynamic(AssetDatabase.LoadMainAssetAtPath(p) as FontAsset)).ToArray();
            foreach (var p in paths) Clear(AssetDatabase.LoadMainAssetAtPath(p) as FontAsset);
            return paths;
        }

        static bool IsDynamic(FontAsset font) => font != null && font.atlasPopulationMode == AtlasPopulationMode.Dynamic;

        static void Clear(FontAsset font)
        {
            // 圖集縮回 1×1（和 git 裡存的一樣），要用時再自動長大；比 1×1 大就是畫過字
            if (IsDynamic(font) && font.atlasTexture != null && font.atlasTexture.width > 1)
                font.ClearFontAssetData(setAtlasSizeToZero: true);
        }
    }
}
