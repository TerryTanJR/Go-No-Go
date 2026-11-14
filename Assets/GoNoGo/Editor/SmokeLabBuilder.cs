using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace GoNoGo.Launch.EditorTools
{
    /// <summary>
    /// 煙霧序列圖實驗：建立 SmokeLab 場景（發射台、火光、排氣煙雲粒子），方便截圖比較效果。
    /// 從選單 GoNoGo/Experiments/Build Smoke Lab 或 script eval 呼叫 Build()。
    /// </summary>
    public static class SmokeLabBuilder
    {
        const string Root = "Assets/GoNoGo/Launch/Experiments";
        const string AtlasPath = Root + "/Smoke_Billow_8x8.png";
        const string MaterialPath = Root + "/M_Smoke_Billow.mat";
        const string ScenePath = Root + "/SmokeLab.unity";

        [MenuItem("GoNoGo/Experiments/Build Smoke Lab")]
        public static string Build()
        {
            ConfigureAtlasImport();
            var mat = CreateMaterial();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // 黃昏天色 + 環境光
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.16f, 0.19f, 0.26f);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.07f, 0.12f);
            cam.fieldOfView = 35f;
            cam.farClipPlane = 500f;
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 9f, -62f), Quaternion.Euler(4f, 0f, 0f));
            // 軟粒子需要深度貼圖；只在這支攝影機開，不改專案全域的 URP 設定
            camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().requiresDepthTexture = true;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.82f, 0.62f);
            sun.intensity = 1.1f;
            sunGo.transform.rotation = Quaternion.Euler(18f, -35f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(20f, 1f, 20f);
            ground.GetComponent<Renderer>().sharedMaterial = LitColor("M_Ground", new Color(0.22f, 0.22f, 0.21f));

            // 發射台 + 簡易火箭（佔位幾何）
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "LaunchPad";
            pad.transform.position = new Vector3(0f, 1f, 0f);
            pad.transform.localScale = new Vector3(10f, 2f, 10f);
            pad.GetComponent<Renderer>().sharedMaterial = LitColor("M_Pad", new Color(0.35f, 0.35f, 0.36f));

            var rocket = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            rocket.name = "Rocket_Placeholder";
            rocket.transform.position = new Vector3(0f, 14f, 0f);
            rocket.transform.localScale = new Vector3(2.4f, 11f, 2.4f);
            rocket.GetComponent<Renderer>().sharedMaterial = LitColor("M_Rocket", new Color(0.9f, 0.9f, 0.88f));

            // 噴口火光：照亮煙霧底部與發射台
            var fireLightGo = new GameObject("EngineFireLight");
            fireLightGo.transform.position = new Vector3(0f, 2.5f, -2f);
            var fireLight = fireLightGo.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.55f, 0.2f);
            fireLight.range = 30f;
            fireLight.intensity = 40f;

            CreateExhaustCloud(mat, new Vector3(0f, 2f, 0f));

            EditorSceneManager.SaveScene(scene, ScenePath);
            return ScenePath;
        }

        /// <summary>在編輯模式把粒子模擬到指定秒數，方便截圖。</summary>
        public static string SimulateTo(float seconds)
        {
            int n = 0;
            foreach (var ps in Object.FindObjectsByType<ParticleSystem>(FindObjectsInactive.Exclude))
            {
                ps.Simulate(seconds, true, true, true);
                n++;
            }
            SceneView.RepaintAll();
            return $"simulated {n} systems to {seconds}s";
        }

        static void CreateExhaustCloud(Material mat, Vector3 at)
        {
            var go = new GameObject("ExhaustCloud");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 8f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 22f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.025f; // 熱煙慢慢上浮
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var emission = ps.emission;
            emission.rateOverTime = 70f;

            // 從導焰槽往左右兩側沿地面噴出：扁平的錐，左右各一個方向
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 1.2f;
            shape.arc = 360f;
            go.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // 錐口朝 +X
            shape.randomDirectionAmount = 0.15f;

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1.5f;
            limit.dampen = 0.12f; // 噴出後很快減速，堆成翻滾的煙牆

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.35f), new Keyframe(0.35f, 0.85f), new Keyframe(1f, 1.6f)));

            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            // 顏色：一開始被火光染成暖橘，之後變冷灰；alpha 淡入淡出
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] {
                    new GradientColorKey(new Color(1f, 0.62f, 0.32f), 0f),
                    new GradientColorKey(new Color(0.9f, 0.82f, 0.74f), 0.25f),
                    new GradientColorKey(new Color(0.72f, 0.74f, 0.78f), 1f) },
                new[] {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.08f),
                    new GradientAlphaKey(0.85f, 0.6f),
                    new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Grid;
            tsa.numTilesX = 8;
            tsa.numTilesY = 8;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.cycleCount = 1;
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.15f); // 錯開起始格，避免同步

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = 5f;

            ps.Play();

            // 另一半往 -X：所有模組都設好後，複製一份鏡像發射器
            var mirror = Object.Instantiate(go, at, Quaternion.Euler(0f, -90f, 0f));
            mirror.name = "ExhaustCloud_Mirror";
            var mps = mirror.GetComponent<ParticleSystem>();
            mps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            mps.useAutoRandomSeed = false;
            mps.randomSeed = 1234; // 和原本的不同步
            mps.Play();
        }

        static void ConfigureAtlasImport()
        {
            AssetDatabase.ImportAsset(AtlasPath, ImportAssetOptions.ForceSynchronousImport);
            var ti = (TextureImporter)AssetImporter.GetAtPath(AtlasPath);
            if (ti == null) throw new System.Exception("找不到序列圖: " + AtlasPath);
            ti.textureType = TextureImporterType.Default;
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SaveAndReimport();
        }

        static Material CreateMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }
            mat.shader = shader;
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath));
            mat.SetColor("_BaseColor", Color.white);

            // 透明、Alpha Blend、不寫深度
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;

            // 軟粒子：和地面、發射台交接處不會出現硬邊
            mat.SetFloat("_SoftParticlesEnabled", 1f);
            mat.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 3f, 0f, 0f));
            mat.EnableKeyword("_SOFTPARTICLES_ON");

            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Material LitColor(string name, Color c)
        {
            string path = $"{Root}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.15f);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
