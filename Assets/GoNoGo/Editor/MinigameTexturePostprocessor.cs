using UnityEditor;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 營運小遊戲的 2D 貼圖（Resources/Minigames/，Blender 畫的）一律用同一組匯入設定：
    /// 不要 mipmap、不壓縮（邊緣才不會糊）、邊緣夾住、雙線性過濾。
    /// </summary>
    public sealed class MinigameTexturePostprocessor : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Minigames/")) return;
            var t = (TextureImporter)assetImporter;
            t.textureType = TextureImporterType.Default;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = false;
            t.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            t.filterMode = UnityEngine.FilterMode.Bilinear;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.npotScale = TextureImporterNPOTScale.None;
            t.maxTextureSize = 1024;
            t.isReadable = false;
        }
    }
}
