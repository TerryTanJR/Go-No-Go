using GoNoGo.Game;
using GoNoGo.Launch;
using GoNoGo.Presentation;
using UnityEditor;
using UnityEngine;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 程序貼圖（太陽能電池、隔熱毯皺褶、地板格線、控制台螢幕、柔光點）與所有材質、
    /// 以及 Resources 裡的 SatMaterials / LaunchKit / AudioBank。
    /// </summary>
    public static class ArtBuilder
    {
        public sealed class Art
        {
            public Material earth, moon, atmosphere, starfield, mli, mliSilver;
            public Material floor, floorMatte, wallPanel, wallDark, trim, lightPanel, accentGlow, screenEarth, consoleBody;
            public Material[] consoleScreens;
            public Material concrete, ground, ocean, towerSteel, beacon;
            public Material orbitLine, linkLine, stationGlow, storm, sparkle;
            public Material daySky;
            public Texture2D softDot;
        }

        public static Art Build()
        {
            BuildUtil.EnsureDirs();
            var a = new Art();

            // ---- 貼圖匯入設定（NASA 原圖） ----
            const string E = "Assets/GoNoGo/Art/Textures/Earth/";
            var day = BuildUtil.ConfigureTexture(E + "earth_day_bmng_topo_bathy_200407_5400x2700.jpg", 4096);
            var night = BuildUtil.ConfigureTexture(E + "earth_night_blackmarble_2016_3600x1800.jpg", 2048);
            // 雲：shader 只讀 .r，但用一般貼圖類型，WebGL 才能用 DXT1 Crunch（單通道類型不支援 Crunch；R8 經 Brotli 約 1.8 MB → 約 0.3 MB）
            var clouds = BuildUtil.ConfigureTexture(E + "earth_clouds_svs4439_4096x2048.jpg", 2048, false, TextureWrapMode.Repeat);
            var water = BuildUtil.ConfigureTexture(E + "earth_water_mask_5400x2700.png", 1024, false, TextureWrapMode.Repeat, true);
            var moonTex = BuildUtil.ConfigureTexture("Assets/GoNoGo/Art/Textures/Moon/moon_lroc_color_svs4720_2048x1024.jpg", 2048);
            // 地形圖目前沒有用到，限制大小避免浪費匯入時間
            BuildUtil.ConfigureTexture(E + "earth_topography_gebco08_5400x2700.jpg", 1024);

            // ---- 程序貼圖 ----
            a.softDot = BuildUtil.SavePng("T_SoftDot", 64, 64, (x, y) =>
            {
                float dx = (x + 0.5f) / 32f - 1f, dy = (y + 0.5f) / 32f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float v = Mathf.Clamp01(1f - d);
                return new Color(1, 1, 1, v * v * (3 - 2 * v));
            }, wrap: TextureWrapMode.Clamp);

            var cell = BuildUtil.SavePng("T_SolarCell", 256, 256, (x, y) =>
            {
                // 8×8 片電池，銀色細格線 + 每片兩條匯流排
                int cx = x % 32, cy = y % 32;
                bool gap = cx < 2 || cy < 2;
                bool bus = (cx == 11 || cx == 22) && !gap;
                float n = BuildUtil.Noise(x * 0.15f, y * 0.15f, 3) * 0.06f;
                if (gap) return new Color(0.55f, 0.57f, 0.6f);
                if (bus) return new Color(0.62f, 0.64f, 0.68f);
                return new Color(0.04f + n, 0.07f + n, 0.20f + n * 2f);
            });

            var mliN = BuildUtil.SavePng("T_MLI_Normal", 256, 256, (x, y) =>
            {
                // 隔熱毯的皺褶：可平鋪的 fbm 高度轉法線
                float u = x / 256f, v = y / 256f, e = 1f / 256f;
                float h = BuildUtil.TileFbm(u, v, 6, 4, 11);
                float hx = BuildUtil.TileFbm(u + e, v, 6, 4, 11);
                float hy = BuildUtil.TileFbm(u, v + e, 6, 4, 11);
                var n = new Vector3((h - hx) * 18f, (h - hy) * 18f, 1f).normalized;
                return new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1);
            }, normalMap: true);

            var grid = BuildUtil.SavePng("T_FloorGrid", 256, 256, (x, y) =>
            {
                bool line = x % 128 < 2 || y % 128 < 2;
                bool sub = x % 32 == 0 || y % 32 == 0;
                float n = BuildUtil.TileFbm(x / 256f, y / 256f, 8, 3, 5) * 0.05f;
                float b = 0.16f + n + (line ? 0.12f : sub ? 0.03f : 0f);
                return new Color(b, b * 1.02f, b * 1.06f);
            });

            var concreteTex = BuildUtil.SavePng("T_Concrete", 256, 256, (x, y) =>
            {
                float n = BuildUtil.TileFbm(x / 256f, y / 256f, 4, 5, 21);
                float seam = (x % 128 < 2 || y % 128 < 2) ? -0.08f : 0f;
                float b = 0.42f + (n - 0.5f) * 0.25f + seam;
                return new Color(b, b * 0.98f, b * 0.94f);
            });

            // 控制台螢幕：幾種假的遙測畫面（折線、長條、文字列）
            a.consoleScreens = new Material[4];
            for (int k = 0; k < 4; k++)
            {
                int seed = k * 17 + 3;
                var hue = k switch { 0 => new Color(0.25f, 0.85f, 1f), 1 => new Color(0.3f, 1f, 0.55f), 2 => new Color(1f, 0.72f, 0.3f), _ => new Color(0.6f, 0.75f, 1f) };
                var tex = BuildUtil.SavePng("T_Console" + k, 256, 160, (x, y) =>
                {
                    var bg = new Color(0.02f, 0.04f, 0.07f);
                    if (y > 146) return hue * 0.35f;                                   // 標題列
                    if (k % 2 == 0 && x < 120 && y > 30 && y < 130)
                    {
                        // 折線圖
                        float fx = x / 120f;
                        float val = 80 + 30 * Mathf.Sin(fx * 9f + seed) + 12 * BuildUtil.Noise(fx * 20f, seed, seed);
                        if (Mathf.Abs(y - val) < 1.5f) return hue;
                        if (y % 25 == 5 || x % 30 == 0) return hue * 0.12f;
                        return bg;
                    }
                    if (x >= 130 && y > 20 && y < 140)
                    {
                        // 文字列
                        int row = (y - 20) / 9;
                        if ((y - 20) % 9 < 5)
                        {
                            float len = 40 + 70 * BuildUtil.Noise(row * 1.7f, seed, seed + 1);
                            if (x - 130 < len && BuildUtil.Noise(x * 0.5f, row, seed + 2) > 0.3f) return hue * 0.7f;
                        }
                        return bg;
                    }
                    if (x < 120 && y <= 30 && y > 8)
                    {
                        // 長條
                        int bar = x / 12;
                        float hgt = 8 + 20 * BuildUtil.Noise(bar * 1.3f, seed, seed + 3);
                        if (x % 12 < 8 && y - 8 < hgt) return hue * 0.8f;
                    }
                    return bg;
                }, wrap: TextureWrapMode.Clamp);
                a.consoleScreens[k] = BuildUtil.Unlit("M_ConsoleScreen" + k, Color.white * 1.6f, tex);
            }

            // ---- 行星 ----
            a.earth = BuildUtil.Material("M_Earth", "GoNoGo/Planet");
            a.earth.SetTexture("_DayTex", day);
            a.earth.SetTexture("_NightTex", night);
            a.earth.SetTexture("_CloudTex", clouds);
            a.earth.SetTexture("_WaterMask", water);
            a.earth.SetFloat("_NightIntensity", 1.6f);
            a.earth.SetFloat("_CloudOpacity", 0.85f);
            a.earth.SetFloat("_AtmoStrength", 1.4f);
            a.moon = BuildUtil.Material("M_Moon", "GoNoGo/Planet");
            a.moon.SetTexture("_DayTex", moonTex);
            a.moon.SetTexture("_NightTex", Texture2D.blackTexture);
            a.moon.SetTexture("_CloudTex", Texture2D.blackTexture);
            a.moon.SetTexture("_WaterMask", Texture2D.blackTexture);
            a.moon.SetFloat("_NightIntensity", 0f);
            a.moon.SetFloat("_CloudOpacity", 0f);
            a.moon.SetFloat("_SpecStrength", 0f);
            // 海面反光：水域遮罩改成 R8 後才看得到，太強會在 Bloom 下變成一團白光
            a.earth.SetFloat("_SpecStrength", 0.22f);
            a.moon.SetFloat("_AtmoStrength", 0f);
            a.moon.SetFloat("_Ambient", 0.015f);
            a.atmosphere = BuildUtil.Material("M_Atmosphere", "GoNoGo/Atmosphere");
            a.starfield = BuildUtil.Material("M_Starfield", "GoNoGo/Starfield");

            // ---- 太空船 ----
            a.mli = BuildUtil.Lit("M_Sat_MLI_Gold", new Color(0.93f, 0.68f, 0.28f), 1f, 0.72f, null, mliN, 0.6f);
            a.mliSilver = BuildUtil.Lit("M_Sat_MLI_Silver", new Color(0.82f, 0.84f, 0.86f), 1f, 0.78f, null, mliN, 0.5f);
            var sat = LoadOrCreate<SatMaterials>("Assets/GoNoGo/Presentation/Resources/Presentation/SatMaterials.asset");
            sat.mliGold = a.mli;
            sat.mliSilver = a.mliSilver;
            sat.paintWhite = BuildUtil.Lit("M_Sat_White", new Color(0.88f, 0.88f, 0.86f), 0f, 0.35f);
            sat.frameDark = BuildUtil.Lit("M_Sat_Frame", new Color(0.07f, 0.07f, 0.08f), 0.4f, 0.45f);
            sat.solarCell = BuildUtil.Lit("M_Sat_SolarCell", Color.white, 0.35f, 0.88f, cell);
            sat.solarCell.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            sat.dish = BuildUtil.Lit("M_Sat_Dish", new Color(0.9f, 0.9f, 0.9f), 0.1f, 0.3f);
            sat.lens = BuildUtil.Lit("M_Sat_Lens", new Color(0.02f, 0.03f, 0.06f), 0.3f, 0.96f);
            sat.metal = BuildUtil.Lit("M_Sat_Metal", new Color(0.6f, 0.6f, 0.63f), 1f, 0.55f);
            sat.accent = BuildUtil.Lit("M_Sat_Accent", Color.white, 0f, 0.5f);
            EditorUtility.SetDirty(sat);

            // ---- 室內 ----
            a.floor = BuildUtil.Lit("M_Floor", new Color(0.55f, 0.58f, 0.62f), 0.2f, 0.82f, grid);
            a.floor.SetTextureScale("_BaseMap", new Vector2(14f, 14f));
            a.floorMatte = BuildUtil.Lit("M_FloorMatte", new Color(0.4f, 0.42f, 0.46f), 0f, 0.35f, grid);
            a.floorMatte.SetTextureScale("_BaseMap", new Vector2(10f, 10f));
            a.wallPanel = BuildUtil.Lit("M_WallPanel", new Color(0.78f, 0.8f, 0.83f), 0f, 0.4f);
            a.wallDark = BuildUtil.Lit("M_WallDark", new Color(0.05f, 0.06f, 0.08f), 0f, 0.3f);
            a.trim = BuildUtil.Lit("M_Trim", new Color(0.12f, 0.13f, 0.15f), 0.6f, 0.5f);
            a.lightPanel = BuildUtil.Unlit("M_LightPanel", new Color(2.4f, 2.5f, 2.7f));
            a.accentGlow = BuildUtil.Unlit("M_AccentGlow", new Color(0.35f, 1.5f, 2.6f));
            a.screenEarth = BuildUtil.Unlit("M_ScreenEarth", new Color(0.5f, 0.75f, 1.1f), day);
            a.consoleBody = BuildUtil.Lit("M_Console", new Color(0.13f, 0.14f, 0.17f), 0.3f, 0.45f);

            // ---- 發射場 ----
            a.concrete = BuildUtil.Lit("M_Concrete", Color.white, 0f, 0.15f, concreteTex);
            a.concrete.SetTextureScale("_BaseMap", new Vector2(4f, 4f));
            a.ground = BuildUtil.Lit("M_Ground", new Color(0.36f, 0.34f, 0.28f), 0f, 0.1f, concreteTex);
            a.ground.SetTextureScale("_BaseMap", new Vector2(60f, 60f));
            a.ocean = BuildUtil.Lit("M_Ocean", new Color(0.05f, 0.11f, 0.16f), 0f, 0.92f);
            a.towerSteel = BuildUtil.Lit("M_TowerSteel", new Color(0.32f, 0.33f, 0.35f), 0.7f, 0.35f);
            a.beacon = BuildUtil.Unlit("M_Beacon", new Color(3f, 0.25f, 0.2f));
            a.daySky = BuildUtil.Material("M_DuskSky", "Skybox/Procedural");
            a.daySky.SetFloat("_SunSize", 0.035f);
            a.daySky.SetFloat("_SunSizeConvergence", 6f);
            // 太陽仰角低時，偏藍的 SkyTint 或太厚的大氣會讓天空中段變成橄欖綠：用中性色調、大氣只稍微加厚
            a.daySky.SetFloat("_AtmosphereThickness", 1.12f);
            a.daySky.SetColor("_SkyTint", new Color(0.5f, 0.5f, 0.5f));
            a.daySky.SetColor("_GroundColor", new Color(0.2f, 0.19f, 0.18f));
            a.daySky.SetFloat("_Exposure", 1.2f);

            var kit = LoadOrCreate<LaunchKit>("Assets/GoNoGo/Launch/Resources/Launch/LaunchKit.asset");
            kit.paintWhite = BuildUtil.Lit("M_Rocket_White", new Color(0.86f, 0.86f, 0.84f), 0.05f, 0.45f);
            kit.paintBlack = BuildUtil.Lit("M_Rocket_Black", new Color(0.04f, 0.04f, 0.045f), 0.3f, 0.55f);
            kit.paintTeal = BuildUtil.Lit("M_Rocket_Teal", new Color(0.22f, 0.42f, 0.45f), 0.05f, 0.4f);
            kit.metal = BuildUtil.Lit("M_Rocket_Metal", new Color(0.35f, 0.33f, 0.32f), 1f, 0.45f);
            kit.interstage = BuildUtil.Lit("M_Rocket_Interstage", new Color(0.08f, 0.08f, 0.09f), 0.2f, 0.4f);
            kit.plume = BuildUtil.Material("M_Plume", "GoNoGo/Plume");
            kit.smoke = AssetDatabase.LoadAssetAtPath<Material>("Assets/GoNoGo/Launch/Experiments/M_Smoke_Billow.mat");
            // 煙霧序列圖 2048² 是網頁版最大的貼圖之一：WebGL 用 Crunch
            var smokeTi = (TextureImporter)AssetImporter.GetAtPath("Assets/GoNoGo/Launch/Experiments/Smoke_Billow_8x8.png");
            if (smokeTi != null)
            {
                BuildUtil.SetWebGLCrunch(smokeTi, 2048, true, 85);
                smokeTi.SaveAndReimport();
            }
            kit.fireball = BuildUtil.Particle("M_Fireball", a.softDot, new Color(1f, 0.6f, 0.25f, 1f), true);
            kit.debris = BuildUtil.Particle("M_Debris", a.softDot, new Color(1f, 0.75f, 0.4f, 1f), true);
            // 尾跡貼圖：寬度方向（V）兩側柔和淡出，長度方向（U）帶一點雜訊讓煙看起來不是一條塑膠管
            var trailTex = BuildUtil.SavePng("T_TrailSoft", 256, 32, (x, y) =>
            {
                float v = Mathf.Abs((y + 0.5f) / 16f - 1f);
                float edge = Mathf.Clamp01(1f - v);
                edge = edge * edge * (3 - 2 * edge);
                float n = 0.75f + 0.25f * Mathf.PerlinNoise(x * 0.09f, y * 0.35f);
                return new Color(1, 1, 1, edge * n);
            }, wrap: TextureWrapMode.Clamp);
            kit.trail = BuildUtil.Particle("M_Trail", trailTex, new Color(0.96f, 0.93f, 0.9f, 0.8f), false);
            EditorUtility.SetDirty(kit);

            // ---- 營運 ----
            a.orbitLine = BuildUtil.Particle("M_OrbitLine", a.softDot, new Color(0.3f, 0.8f, 1f, 0.55f), true);
            a.linkLine = BuildUtil.Particle("M_LinkLine", a.softDot, new Color(0.35f, 1f, 0.55f, 0.9f), true);
            a.stationGlow = BuildUtil.Unlit("M_StationGlow", new Color(0.4f, 2.2f, 1.0f));
            a.storm = BuildUtil.Particle("M_Storm", a.softDot, new Color(1f, 0.7f, 0.3f, 0.8f), true);
            a.sparkle = BuildUtil.Particle("M_Sparkle", a.softDot, new Color(1f, 1f, 1f, 1f), true);

            BuildAudioBank();
            AssetDatabase.SaveAssets();
            return a;
        }

        static void BuildAudioBank()
        {
            const string A = "Assets/GoNoGo/Audio/NASA/";
            var bank = LoadOrCreate<AudioBank>("Assets/GoNoGo/Audio/Resources/Audio/AudioBank.asset");
            bank.countdownNet = AssetDatabase.LoadAssetAtPath<AudioClip>(A + "NASA_IM2_CountdownNet_TerminalCount.wav");
            bank.padRoar = AssetDatabase.LoadAssetAtPath<AudioClip>(A + "NASA_IM2_PadMics_LiftoffRoar.wav");
            bank.distantRumble = AssetDatabase.LoadAssetAtPath<AudioClip>(A + "NASA_IM2_Beach_DistantLaunchRumble.wav");
            bank.spaceAmbience = AssetDatabase.LoadAssetAtPath<AudioClip>(A + "NASA_Juno_GanymedeFlyby_RadioWaves.wav");
            // Artemis I 的播報提到 Artemis 和 SLS，和玩家的任務對不上，所以不放進遊戲（檔案保留在專案裡）
            bank.liftoffCommentary = null;
            bank.weRiseTogether = null;
            EditorUtility.SetDirty(bank);

            foreach (var f in new[] { "NASA_IM2_CountdownNet_TerminalCount.wav", "NASA_IM2_PadMics_LiftoffRoar.wav", "NASA_IM2_Beach_DistantLaunchRumble.wav", "NASA_Juno_GanymedeFlyby_RadioWaves.wav" })
            {
                var imp = (AudioImporter)AssetImporter.GetAtPath(A + f);
                if (imp == null) continue;
                var s = imp.defaultSampleSettings;
                s.loadType = AudioClipLoadType.CompressedInMemory;
                s.compressionFormat = AudioCompressionFormat.Vorbis;
                // 22.05 kHz、品質 0.45：低頻的轟鳴和無線電語音聽不出差別，WebGL 省約 3 MB，守住 30 MB 預算。
                // （Unity 6.6 的 SetOverrideSampleSettings 對每個平台名稱都回傳 false，所以直接改預設值）
                s.quality = 0.45f;
                s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                s.sampleRateOverride = 22050;
                imp.defaultSampleSettings = s;
                imp.forceToMono = true;
                imp.SaveAndReimport();
            }
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
