using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 一鍵打包要給別人玩的版本，輸出到 <c>Builds/Release/</c>：
    /// <list type="bullet">
    /// <item>瀏覽器版 zip：直接上傳 Unity Play（index.html 在 zip 最外層）。</item>
    /// <item>Windows 版 zip：解壓縮後執行 GoNoGo.exe。</item>
    /// </list>
    /// 瀏覽器版改用 Gzip＋備用解壓縮（Decompression Fallback），放在哪個網站都能開，不用設定伺服器標頭；
    /// 專案原本的 Brotli 設定在打包完會改回來，ProjectSettings 不會有變動。
    /// 結果寫在 <c>Builds/Release/last_release.txt</c>。
    /// </summary>
    public static class ReleaseBuilder
    {
        const string Root = "Builds/Release";

        [MenuItem("GoNoGo/Release/Web + Windows (zip)")]
        static void BuildAllMenu() => Report(BuildAll());

        [MenuItem("GoNoGo/Release/Web only (zip for Unity Play)")]
        static void BuildWebMenu() => Report(BuildWeb());

        [MenuItem("GoNoGo/Release/Windows only (zip)")]
        static void BuildWindowsMenu() => Report(BuildWindows());

        /// <summary>先建目前平台，少切換一次平台（切平台要重新匯入素材，很久）。</summary>
        public static string BuildAll()
        {
            bool webFirst = EditorUserBuildSettings.activeBuildTarget == BuildTarget.WebGL;
            var a = webFirst ? BuildWeb() : BuildWindows();
            var b = webFirst ? BuildWindows() : BuildWeb();
            return a + "\n" + b;
        }

        public static string BuildWeb()
        {
            var compression = PlayerSettings.WebGL.compressionFormat;
            var fallback = PlayerSettings.WebGL.decompressionFallback;
            try
            {
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
                PlayerSettings.WebGL.decompressionFallback = true;
                return Build(BuildTarget.WebGL, "Web", "index.html");
            }
            finally
            {
                PlayerSettings.WebGL.compressionFormat = compression;
                PlayerSettings.WebGL.decompressionFallback = fallback;
                AssetDatabase.SaveAssets();
            }
        }

        public static string BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Windows", "GoNoGo.exe");

        static string Build(BuildTarget target, string name, string mainFile)
        {
            var dir = Path.Combine(Root, name);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
                locationPathName = target == BuildTarget.WebGL ? dir : Path.Combine(dir, mainFile),
                target = target,
                options = BuildOptions.None,
            });
            var s = report.summary;
            if (s.result != BuildResult.Succeeded)
                return $"{name}: FAILED ({s.result}, {s.totalErrors} errors) — see the Console";

            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmm");
            var zip = Path.Combine(Root, $"GoNoGo_{name}_{stamp}.zip");
            Zip(dir, zip);
            var mb = new FileInfo(zip).Length / (1024f * 1024f);
            return $"{name}: OK {Path.GetFullPath(zip)} ({mb:F1} MB, build {s.totalTime:mm\\:ss})";
        }

        /// <summary>壓縮整個資料夾（內容放在 zip 最外層），略過 Unity 的除錯符號資料夾。</summary>
        static void Zip(string dir, string zipPath)
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            var full = Path.GetFullPath(dir);
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var file in Directory.GetFiles(full, "*", SearchOption.AllDirectories))
            {
                var rel = file.Substring(full.Length + 1).Replace('\\', '/');
                if (rel.Contains("_BackUpThisFolder_ButDontShipItWithYourGame") || rel.Contains("_BurstDebugInformation_DoNotShip")) continue;
                archive.CreateEntryFromFile(file, rel, System.IO.Compression.CompressionLevel.Optimal);
            }
        }

        /// <summary>命令列用（不跳對話框）：<c>unity command script eval "return GoNoGo.EditorTools.ReleaseBuilder.BuildAllQuiet();"</c>，
        /// 或關掉編輯器時 <c>Unity.exe -batchmode -quit -projectPath . -executeMethod GoNoGo.EditorTools.ReleaseBuilder.BuildAllQuiet</c>。</summary>
        public static string BuildAllQuiet() => Report(BuildAll(), false);

        static string Report(string result, bool dialog = true)
        {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "last_release.txt"), DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "\n" + result + "\n");
            Debug.Log("[Release]\n" + result);
            if (dialog && !Application.isBatchMode)
                EditorUtility.DisplayDialog("GoNoGo Release", result, "OK");
            return result;
        }
    }
}
