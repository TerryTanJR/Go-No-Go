using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GoNoGo.EditorTools
{
    /// <summary>場景與資產建置共用的小工具（材質、貼圖、相機、後處理）。</summary>
    public static class BuildUtil
    {
        public const string MatDir = "Assets/GoNoGo/Art/Materials";
        public const string TexDir = "Assets/GoNoGo/Art/Textures/Generated";
        public const string SceneDir = "Assets/GoNoGo/Scenes";
        public const string ProfileDir = "Assets/GoNoGo/Art/PostProfiles";

        public static void EnsureDirs()
        {
            foreach (var d in new[] { MatDir, TexDir, SceneDir, ProfileDir, "Assets/GoNoGo/Presentation/Resources/Presentation", "Assets/GoNoGo/Launch/Resources/Launch", "Assets/GoNoGo/Audio/Resources/Audio" })
                Directory.CreateDirectory(d);
            AssetDatabase.Refresh();
        }

        // ---------------- 材質 ----------------

        public static Material Material(string name, string shader)
        {
            string path = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            var sh = Shader.Find(shader);
            if (sh == null) throw new System.Exception("找不到 shader " + shader);
            if (m == null)
            {
                m = new Material(sh);
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = sh;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP Lit。emission 開著但是黑色，讓程式可以用 MaterialPropertyBlock 讓它發光。</summary>
        public static Material Lit(string name, Color color, float metallic, float smoothness, Texture baseMap = null, Texture normal = null, float normalScale = 1f, Color? emission = null)
        {
            var m = Material(name, "Universal Render Pipeline/Lit");
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Metallic", metallic);
            m.SetFloat("_Smoothness", smoothness);
            m.SetTexture("_BaseMap", baseMap);
            if (normal != null)
            {
                m.SetTexture("_BumpMap", normal);
                m.SetFloat("_BumpScale", normalScale);
                m.EnableKeyword("_NORMALMAP");
            }
            else
            {
                m.SetTexture("_BumpMap", null);
                m.DisableKeyword("_NORMALMAP");
            }
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission ?? Color.black);
            // 必須是 RealtimeEmissive：URP 檢查材質時只看 GI 旗標決定要不要保留 _EMISSION
            // （BaseShaderGUI.SetMaterialKeywords），設成 None 會在別人重新匯入時被拿掉，執行期的發光就失效。
            // 專案沒開即時 GI，這個旗標沒有額外成本。
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP Unlit（螢幕、發光條）。</summary>
        public static Material Unlit(string name, Color color, Texture tex = null)
        {
            var m = Material(name, "Universal Render Pipeline/Unlit");
            m.SetColor("_BaseColor", color);
            m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>URP 粒子材質（加法或一般透明）。</summary>
        public static Material Particle(string name, Texture tex, Color color, bool additive)
        {
            var m = Material(name, "Universal Render Pipeline/Particles/Unlit");
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", additive ? 2f : 0f);
            m.SetFloat("_SrcBlend", (float)(additive ? BlendMode.SrcAlpha : BlendMode.SrcAlpha));
            m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive) m.EnableKeyword("_BLENDMODE_ADD"); else m.DisableKeyword("_BLENDMODE_ADD");
            m.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(m);
            return m;
        }

        // ---------------- 貼圖 ----------------

        /// <summary>程式產生貼圖並存成 PNG（存在就覆寫），回傳匯入後的貼圖。</summary>
        public static Texture2D SavePng(string name, int w, int h, System.Func<int, int, Color> f, bool normalMap = false, bool linear = false, TextureWrapMode wrap = TextureWrapMode.Repeat)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false, linear || normalMap);
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    px[y * w + x] = f(x, y);
            tex.SetPixels(px);
            tex.Apply();
            string path = $"{TexDir}/{name}.png";
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            ti.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            ti.sRGBTexture = !linear && !normalMap;
            ti.wrapMode = wrap;
            ti.mipmapEnabled = true;
            ti.alphaIsTransparency = !normalMap;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        public static Texture2D ConfigureTexture(string path, int maxSize, bool srgb = true, TextureWrapMode wrap = TextureWrapMode.Repeat, bool singleChannel = false)
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(path);
            if (ti == null) throw new System.Exception("找不到貼圖 " + path);
            ti.textureType = singleChannel ? TextureImporterType.SingleChannel : TextureImporterType.Default;
            ti.sRGBTexture = srgb;
            ti.maxTextureSize = maxSize;
            ti.wrapModeU = wrap;
            ti.wrapModeV = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Compressed;
            ti.npotScale = TextureImporterNPOTScale.ToNearest;
            if (!singleChannel) SetWebGLCrunch(ti, maxSize, ti.DoesSourceTextureHaveAlpha());
            ti.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// WebGL 專用：DXT Crunch 壓縮。下載大小約是一般 DXT 的 1/3～1/4（藍圖目標首次載入 30 MB 以內）；
        /// 桌機版維持原本的壓縮品質。
        /// </summary>
        public static void SetWebGLCrunch(TextureImporter ti, int maxSize, bool alpha, int quality = 80)
        {
            var ps = ti.GetPlatformTextureSettings("WebGL");
            ps.overridden = true;
            ps.maxTextureSize = maxSize;
            ps.format = alpha ? TextureImporterFormat.DXT5Crunched : TextureImporterFormat.DXT1Crunched;
            ps.compressionQuality = quality;
            ti.SetPlatformTextureSettings(ps);
        }

        /// <summary>網頁版發布設定（藍圖「發布用設定」）：Brotli、不要解壓縮後備、程式碼以大小優先 + LTO、剝除沒用到的網格通道。</summary>
        public static void ApplyWebPublishSettings()
        {
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.stripUnusedMeshComponents = true;
            PlayerSettings.WebGL.wasm2023 = true; // 藍圖：WebAssembly 2023（較新的瀏覽器）
            EditorUserBuildSettings.SetPlatformSettings("WebGL", "CodeOptimization", "DiskSizeLTO");
        }

        // ---------------- 相機與後處理 ----------------

        public static Camera Camera(string name, Vector3 pos, Quaternion rot, float fov, float near = 0.1f, float far = 2000f)
        {
            var go = new GameObject(name) { tag = "MainCamera" };
            go.transform.SetPositionAndRotation(pos, rot);
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = fov;
            cam.nearClipPlane = near;
            cam.farClipPlane = far;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.Medium;
            go.AddComponent<AudioListener>();
            return cam;
        }

        public struct PostSettings
        {
            public float bloom, bloomThreshold, exposure, contrast, saturation, vignette, grain;
            public Color tint;
            public bool dof;
            public float dofStart, dofEnd;
        }

        public static PostSettings DefaultPost => new PostSettings
        {
            bloom = 0.9f, bloomThreshold = 0.95f, exposure = 0f, contrast = 12f, saturation = 4f,
            vignette = 0.28f, grain = 0.12f, tint = Color.white, dof = false,
        };

        public static Volume PostVolume(string sceneName, PostSettings s)
        {
            string path = $"{ProfileDir}/PP_{sceneName}.asset";
            AssetDatabase.DeleteAsset(path); // 每次重建，避免舊的覆寫元件殘留
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tm = profile.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);

            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(s.bloom);
            bloom.threshold.Override(s.bloomThreshold);
            bloom.scatter.Override(0.72f);
            bloom.highQualityFiltering.Override(false);

            var ca = profile.Add<ColorAdjustments>(true);
            ca.postExposure.Override(s.exposure);
            ca.contrast.Override(s.contrast);
            ca.saturation.Override(s.saturation);
            ca.colorFilter.Override(s.tint);

            var vg = profile.Add<Vignette>(true);
            vg.intensity.Override(s.vignette);
            vg.smoothness.Override(0.45f);

            if (s.grain > 0)
            {
                var fg = profile.Add<FilmGrain>(true);
                fg.type.Override(FilmGrainLookup.Thin1);
                fg.intensity.Override(s.grain);
            }
            if (s.dof)
            {
                var dof = profile.Add<DepthOfField>(true);
                dof.mode.Override(DepthOfFieldMode.Gaussian);
                dof.gaussianStart.Override(s.dofStart);
                dof.gaussianEnd.Override(s.dofEnd);
                dof.gaussianMaxRadius.Override(1.2f);
            }
            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("PostProcess");
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;
            return vol;
        }

        // ---------------- 物件 ----------------

        public static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go;
        }

        public static Transform Group(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static Light Light(string name, LightType type, Color color, float intensity, Vector3 pos, Quaternion rot, float range = 10f, LightShadows shadows = LightShadows.None, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var l = go.AddComponent<Light>();
            l.type = type;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows;
            return l;
        }

        /// <summary>簡單的值雜訊（0–1），產生程序貼圖用。</summary>
        public static float Noise(float x, float y, int seed = 0)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float xf = x - xi, yf = y - yi;
            float a = Hash(xi, yi, seed), b = Hash(xi + 1, yi, seed), c = Hash(xi, yi + 1, seed), d = Hash(xi + 1, yi + 1, seed);
            float u = xf * xf * (3 - 2 * xf), v = yf * yf * (3 - 2 * yf);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 2147483647;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0x7fffffff) / (float)int.MaxValue;
            }
        }

        /// <summary>可無縫平鋪的 fbm（座標取模）。</summary>
        public static float TileFbm(float x, float y, int period, int octaves, int seed)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            int p = period;
            for (int o = 0; o < octaves; o++)
            {
                float fx = x * p, fy = y * p;
                int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                float tx = fx - x0, ty = fy - y0;
                float a = Hash(Mod(x0, p), Mod(y0, p), seed + o), b = Hash(Mod(x0 + 1, p), Mod(y0, p), seed + o);
                float c = Hash(Mod(x0, p), Mod(y0 + 1, p), seed + o), d = Hash(Mod(x0 + 1, p), Mod(y0 + 1, p), seed + o);
                float u = tx * tx * (3 - 2 * tx), v = ty * ty * (3 - 2 * ty);
                sum += Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v) * amp;
                norm += amp;
                amp *= 0.5f;
                p *= 2;
            }
            return sum / norm;
        }

        static int Mod(int a, int m) => ((a % m) + m) % m;
    }
}
