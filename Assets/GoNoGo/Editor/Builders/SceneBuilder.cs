using System.Collections.Generic;
using System.Linq;
using GoNoGo.Game;
using GoNoGo.Launch;
using GoNoGo.Presentation;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 用程式從零建出六個遊戲場景（可重複執行，每次整個重建）：
    /// Boot（標題）、DesignRoom（無塵室）、MissionControl（控制中心）、Launch（發射）、Operations（在軌營運）、Report（任務報告）。
    /// 介面由 UIRoot 依場景名稱自動掛上，場景裡只放 3D。
    /// </summary>
    public static class SceneBuilder
    {
        const string MeshDir = "Assets/GoNoGo/Art/Meshes";
        const string ModelDir = "Assets/GoNoGo/Art/Models/NASA/";

        [MenuItem("GoNoGo/Build/All Scenes")]
        public static string BuildAll()
        {
            var art = ArtBuilder.Build();
            var sphere = HiSphere();
            var paths = new List<string>
            {
                Boot(art, sphere),
                DesignRoom(art),
                MissionControl(art),
                Launch(art, sphere),
                Operations(art, sphere),
                Report(art, sphere),
            };
            EditorBuildSettings.scenes = paths.Select(p => new EditorBuildSettingsScene(p, true)).ToArray();
            SetWebGLDefaultQuality(1);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(paths[0]);
            return "built " + string.Join(", ", paths);
        }

        // ================= 共用 =================

        static UnityEngine.SceneManagement.Scene NewScene() =>
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        static string Save(UnityEngine.SceneManagement.Scene scene, string name)
        {
            string path = $"{BuildUtil.SceneDir}/{name}.unity";
            EditorSceneManager.SaveScene(scene, path);
            return path;
        }

        /// <summary>環境光用三色漸層（不需要烘焙光照，WebGL 上結果固定）。</summary>
        static void Env(Material sky, Color ambSky, Color ambEq, Color ambGround, bool fog = false, Color fogColor = default, float fogDensity = 0f)
        {
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambSky;
            RenderSettings.ambientEquatorColor = ambEq;
            RenderSettings.ambientGroundColor = ambGround;
            RenderSettings.fog = fog;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;
        }

        static Light Sun(Vector3 forward, Color color, float intensity, LightShadows shadows = LightShadows.Soft)
        {
            var l = BuildUtil.Light("Sun", LightType.Directional, color, intensity, Vector3.zero, Quaternion.LookRotation(forward.normalized), 10f, shadows);
            l.shadowStrength = 0.92f;
            RenderSettings.sun = l;
            return l;
        }

        /// <summary>即時反射探針，開場時拍一次（金屬才有東西可以反射）。</summary>
        static void Probe(Transform parent, Vector3 pos, Vector3 size, float intensity = 1f)
        {
            var go = new GameObject("ReflectionProbe");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var p = go.AddComponent<ReflectionProbe>();
            p.mode = ReflectionProbeMode.Realtime;
            p.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            p.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            p.size = size;
            p.resolution = 128;
            p.intensity = intensity;
            p.farClipPlane = Mathf.Max(size.x, size.y, size.z) * 1.5f;
            go.AddComponent<ProbeOnStart>();
        }

        static GameObject MeshObj(string name, Transform parent, Mesh mesh, Material mat, Vector3 pos, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go;
        }

        /// <summary>地球（或月球）：行星球體 + 大氣殼。回傳行星本體。</summary>
        static GameObject Planet(string name, Transform parent, Mesh sphere, Material planet, Material atmo, Vector3 pos, float diameter, out GameObject atmosphere)
        {
            var body = MeshObj(name, parent, sphere, planet, pos, diameter);
            atmosphere = atmo != null ? MeshObj(name + "_Atmosphere", parent, sphere, atmo, pos, diameter * 1.025f) : null;
            return body;
        }

        /// <summary>高解析度 UV 球（半徑 0.5，與內建球體一致）。內建球體只有 24 段，放大到地球尺寸時邊緣看得到稜角。</summary>
        static Mesh HiSphere()
        {
            System.IO.Directory.CreateDirectory(MeshDir);
            string path = MeshDir + "/HiSphere.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh { name = "HiSphere" };
            const int lon = 192, lat = 96;
            var v = new Vector3[(lon + 1) * (lat + 1)];
            var n = new Vector3[v.Length];
            var uv = new Vector2[v.Length];
            for (int y = 0; y <= lat; y++)
            {
                float phi = Mathf.PI * y / lat - Mathf.PI / 2f;
                for (int x = 0; x <= lon; x++)
                {
                    float th = 2f * Mathf.PI * x / lon;
                    var d = new Vector3(Mathf.Cos(phi) * Mathf.Cos(th), Mathf.Sin(phi), Mathf.Cos(phi) * Mathf.Sin(th));
                    int i = y * (lon + 1) + x;
                    v[i] = d * 0.5f;
                    n[i] = d;
                    uv[i] = new Vector2(x / (float)lon, y / (float)lat);
                }
            }
            var tris = new int[lon * lat * 6];
            int t = 0;
            for (int y = 0; y < lat; y++)
                for (int x = 0; x < lon; x++)
                {
                    int a = y * (lon + 1) + x, b = a + 1, c = a + lon + 1, d = c + 1;
                    // 從外面看是逆時針 → Unity 的正面是順時針，所以 a-c-b
                    tris[t++] = a; tris[t++] = c; tris[t++] = b;
                    tris[t++] = b; tris[t++] = c; tris[t++] = d;
                }
            mesh.Clear();
            mesh.vertices = v;
            mesh.normals = n;
            mesh.uv = uv;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>
        /// 球冠網格（半徑 0.5）：以 centerDir 為中心、半角 capDeg 的一塊球面。物件空間的方向就是經緯度，
        /// 所以 Planet shader 照樣能取貼圖；只是把頂點集中在看得到的區域。
        /// </summary>
        static Mesh SphereCap(string name, Vector3 centerDir, float capDeg, int rings, int segs)
        {
            var q = Quaternion.FromToRotation(Vector3.up, centerDir.normalized);
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int r = 0; r <= rings; r++)
            {
                float th = capDeg * Mathf.Deg2Rad * r / rings;
                for (int s = 0; s <= segs; s++)
                {
                    float ph = 2f * Mathf.PI * s / segs;
                    var d = q * new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                    v.Add(d * 0.5f);
                    n.Add(d);
                }
            }
            // 和 HiSphere 同樣的繞向：r 越大緯度越低，所以 (r+1) 扮演「下一圈的下方」
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int A = (r + 1) * (segs + 1) + s, C = r * (segs + 1) + s, B = A + 1, D = C + 1;
                    t.AddRange(new[] { A, C, B, B, C, D });
                }
            return SaveMesh(name, v, n, t);
        }

        /// <summary>高細分圓柱（半徑 0.5、高 2，與內建圓柱一致），96 段，含上下蓋。</summary>
        static Mesh HiCylinder()
        {
            const int seg = 96;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * 0.5f + Vector3.down); n.Add(d);
                v.Add(d * 0.5f + Vector3.up); n.Add(d);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                t.AddRange(new[] { a, b, c, c, b, d });
            }
            foreach (var y in new[] { -1f, 1f })
            {
                int center = v.Count;
                v.Add(new Vector3(0, y, 0)); n.Add(new Vector3(0, y, 0));
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * Mathf.PI * 2f / seg;
                    v.Add(new Vector3(Mathf.Cos(a) * 0.5f, y, Mathf.Sin(a) * 0.5f)); n.Add(new Vector3(0, y, 0));
                }
                for (int i = 0; i < seg; i++)
                {
                    if (y > 0) t.AddRange(new[] { center, center + 2 + i, center + 1 + i });
                    else t.AddRange(new[] { center, center + 1 + i, center + 2 + i });
                }
            }
            return SaveMesh("HiCylinder", v, n, t);
        }

        /// <summary>平放的圓環（朝上），inner = 0 時是實心圓盤。</summary>
        static Mesh Annulus(string name, float inner, float outer)
        {
            const int seg = 160;
            var v = new List<Vector3>(); var n = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v.Add(d * inner); n.Add(Vector3.up);
                v.Add(d * outer); n.Add(Vector3.up);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                t.AddRange(new[] { a, c, b, b, c, d });
            }
            return SaveMesh(name, v, n, t);
        }

        static Mesh SaveMesh(string name, List<Vector3> v, List<Vector3> n, List<int> t)
        {
            System.IO.Directory.CreateDirectory(MeshDir);
            string path = $"{MeshDir}/{name}.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create = mesh == null;
            if (create) mesh = new Mesh { name = name };
            mesh.Clear();
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            if (create) AssetDatabase.CreateAsset(mesh, path);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        /// <summary>放一個 NASA 模型，依 Renderer 範圍縮放到指定大小並置中（模型單位不一，不手調 Transform）。</summary>
        static GameObject Model(string file, Transform parent, float size)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelDir + file);
            if (prefab == null) throw new System.Exception("找不到模型 " + file);
            var wrap = new GameObject(System.IO.Path.GetFileNameWithoutExtension(file)).transform;
            var inner = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inner.transform.SetParent(wrap, false);
            var b = WorldBounds(inner);
            float k = size / Mathf.Max(1e-4f, Mathf.Max(b.size.x, b.size.y, b.size.z));
            inner.transform.localScale *= k;
            b = WorldBounds(inner);
            inner.transform.localPosition -= b.center;
            wrap.SetParent(parent, false);
            return wrap.gameObject;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 size, Material mat, Quaternion? rot = null, bool shadows = true)
        {
            var go = BuildUtil.Prim(PrimitiveType.Cube, name, parent, pos, size, mat);
            if (rot != null) go.transform.localRotation = rot.Value;
            if (!shadows) go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static GameObject Quad(string name, Transform parent, Vector3 pos, Quaternion rot, Vector2 size, Material mat)
        {
            var go = BuildUtil.Prim(PrimitiveType.Quad, name, parent, pos, new Vector3(size.x, size.y, 1f), mat);
            go.transform.localRotation = rot;
            var r = go.GetComponent<Renderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        static Camera MainCamera(Vector3 pos, Vector3 lookAt, float fov, float near = 0.1f, float far = 2000f)
        {
            var cam = BuildUtil.Camera("Main Camera", pos, Quaternion.LookRotation(lookAt - pos), fov, near, far);
            return cam;
        }

        static void SetWebGLDefaultQuality(int level)
        {
            var qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
            var so = new SerializedObject(qs);
            var arr = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                if (e.FindPropertyRelative("first").stringValue == "WebGL")
                    e.FindPropertyRelative("second").intValue = level;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ================= Boot：標題畫面 =================

        static string Boot(ArtBuilder.Art art, Mesh sphere)
        {
            var scene = NewScene();
            Env(art.starfield, new Color(0.02f, 0.025f, 0.035f), new Color(0.015f, 0.017f, 0.02f), Color.black);
            Sun(new Vector3(0.8f, -0.25f, 0.55f), new Color(1f, 0.96f, 0.9f), 2.2f, LightShadows.None);

            // 地球在右邊、只露出一部分；自轉軸傾斜 23.4°
            var tilt = BuildUtil.Group("EarthTilt");
            tilt.position = new Vector3(5.6f, -2.2f, 6f);
            tilt.rotation = Quaternion.Euler(0, 0, -23.4f);
            var earth = Planet("Earth", tilt, sphere, art.earth, art.atmosphere, Vector3.zero, 9f, out _);
            var spin = earth.AddComponent<Spin>();
            spin.axis = Vector3.up;
            spin.degreesPerSecond = 1.4f;

            // Landsat 8 繞著地球飛過
            var satOrbit = BuildUtil.Group("Landsat8_Orbit");
            var landsat = Model("Sat_Landsat8.glb", satOrbit, 0.55f);
            var orbit = satOrbit.gameObject.AddComponent<OrbitAround>();
            orbit.center = tilt;
            orbit.radius = 5.3f;
            orbit.degreesPerSecond = 3.2f;
            orbit.axis = new Vector3(0.35f, 1f, 0.2f);
            orbit.phase = 200f;
            landsat.transform.localRotation = Quaternion.Euler(0, 90, 0);

            var cam = MainCamera(new Vector3(0, 0, -10), new Vector3(0, 0, 0), 35f, 0.1f, 500f);
            cam.gameObject.AddComponent<CameraDrift>();

            var post = BuildUtil.DefaultPost;
            post.bloom = 1.1f; post.bloomThreshold = 0.9f; post.vignette = 0.34f; post.contrast = 14f;
            BuildUtil.PostVolume("Boot", post);
            return Save(scene, SceneFlow.Boot);
        }

        // ================= DesignRoom：無塵室 =================

        static string DesignRoom(ArtBuilder.Art art)
        {
            var scene = NewScene();
            Env(null, new Color(0.34f, 0.37f, 0.42f), new Color(0.24f, 0.26f, 0.3f), new Color(0.1f, 0.1f, 0.11f));
            var sun = Sun(new Vector3(-0.45f, -0.8f, 0.4f), new Color(1f, 0.97f, 0.92f), 1.25f);
            sun.shadowStrength = 0.7f;

            var room = BuildUtil.Group("CleanRoom");
            const float W = 20f, H = 9f;
            Box("Floor", room, new Vector3(0, -0.1f, 0), new Vector3(W * 2, 0.2f, W * 2), art.floor);
            Box("Ceiling", room, new Vector3(0, H + 0.1f, 0), new Vector3(W * 2, 0.2f, W * 2), art.wallDark, shadows: false);
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0, side * 90f, 0);
                var wall = BuildUtil.Group("Wall" + side, room);
                wall.localRotation = rot;
                Box("Panel", wall, new Vector3(0, H / 2, W), new Vector3(W * 2, H, 0.3f), art.wallPanel, shadows: false);
                Box("Skirting", wall, new Vector3(0, 0.6f, W - 0.2f), new Vector3(W * 2, 1.2f, 0.12f), art.wallDark, shadows: false);
                Box("GlowStrip", wall, new Vector3(0, 1.26f, W - 0.24f), new Vector3(W * 2, 0.05f, 0.04f), art.accentGlow, shadows: false);
                Box("UpperBand", wall, new Vector3(0, H - 0.8f, W - 0.2f), new Vector3(W * 2, 1.6f, 0.12f), art.trim, shadows: false);
                for (int i = -4; i <= 4; i++)
                    Box("Rib" + i, wall, new Vector3(i * 4.4f, H / 2, W - 0.25f), new Vector3(0.22f, H, 0.2f), art.trim, shadows: false);
            }

            // 牆上螢幕：一面是地球、兩側是遙測畫面
            Quad("EarthScreen", room, new Vector3(0, 4.6f, W - 0.4f), Quaternion.identity, new Vector2(11f, 5.5f), art.screenEarth);
            Box("EarthScreenFrame", room, new Vector3(0, 4.6f, W - 0.33f), new Vector3(11.4f, 5.9f, 0.05f), art.wallDark, shadows: false);
            for (int i = 0; i < 4; i++)
            {
                var rot = Quaternion.Euler(0, 90f * (i < 2 ? 1 : -1), 0);
                float x = i < 2 ? W - 0.4f : -(W - 0.4f);
                float z = (i % 2 == 0 ? -1 : 1) * 6f;
                var q = Quad("Telemetry" + i, room, new Vector3(x, 4.2f, z), rot, new Vector2(6f, 3.75f), art.consoleScreens[i]);
                q.transform.localRotation = Quaternion.LookRotation(new Vector3(x > 0 ? 1 : -1, 0, 0));
            }

            // 天花板燈板
            for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 2; z++)
                    Quad("LightPanel", room, new Vector3(x * 6.5f, H - 0.02f, z * 6.5f), Quaternion.Euler(-90, 0, 0), new Vector2(3.8f, 1.1f), art.lightPanel); // Quad 的正面朝 -Z：轉成朝下

            // 展示台：深色圓台 + 一圈青色光環
            // 展示台：深色圓台 + 青色光環。內建圓柱只有 24 段，近看是多邊形，所以用自己產生的高細分網格
            var ped = BuildUtil.Group("Pedestal", room);
            var cyl = HiCylinder();
            MeshObj("Base", ped, cyl, art.trim, new Vector3(0, 0.3f, 0), 1f).transform.localScale = new Vector3(3.6f, 0.3f, 3.6f);
            MeshObj("Top", ped, Annulus("Disk_0_1.85", 0f, 1.85f), art.consoleBody, new Vector3(0, 0.602f, 0), 1f);
            MeshObj("Ring", ped, Annulus("Ring_1.85_2.0", 1.85f, 2.0f), art.accentGlow, new Vector3(0, 0.603f, 0), 1f);
            MeshObj("FloorRing", ped, Annulus("Ring_3.6_3.7", 3.6f, 3.7f), art.accentGlow, new Vector3(0, 0.004f, 0), 1f);
            MeshObj("FloorRingOuter", ped, Annulus("Ring_5.2_5.24", 5.2f, 5.24f), art.accentGlow, new Vector3(0, 0.004f, 0), 1f);
            foreach (var r in ped.GetComponentsInChildren<MeshRenderer>())
            {
                r.receiveShadows = true;
                r.shadowCastingMode = r.name == "Base" ? ShadowCastingMode.On : ShadowCastingMode.Off;
            }

            // 燈：主光（上面的方向光）+ 四盞聚光燈打在太空船上 + 展示台青色光
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                var p = Quaternion.Euler(0, a, 0) * new Vector3(0, 7.8f, -7.5f);
                var target = new Vector3(0, 2.6f, 0);
                var l = BuildUtil.Light("Spot" + i, LightType.Spot, i % 2 == 0 ? new Color(1f, 0.95f, 0.88f) : new Color(0.8f, 0.9f, 1f), 70f, p, Quaternion.LookRotation(target - p), 20f, LightShadows.None, room);
                l.spotAngle = 38f;
                l.innerSpotAngle = 20f;
            }
            // 從台面往上打的淡青色光，照亮太空船底部（點光源放在台面上會把台面打到過曝）
            var up = BuildUtil.Light("PedestalUplight", LightType.Spot, new Color(0.35f, 0.85f, 1f), 3f, new Vector3(0, 0.62f, 0), Quaternion.LookRotation(Vector3.up, Vector3.forward), 5f, LightShadows.None, room);
            up.spotAngle = 50f;
            up.innerSpotAngle = 20f;

            var rigGo = new GameObject("Satellite");
            rigGo.transform.position = new Vector3(0, 2.6f, 0);
            var rig = rigGo.AddComponent<SatelliteRig>();

            Probe(room, new Vector3(0, 3f, 0), new Vector3(W * 2, H + 1, W * 2));

            var cam = MainCamera(new Vector3(0, 4.4f, -7.5f), new Vector3(0, 2.6f, 0), 38f, 0.1f, 200f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.03f, 0.035f, 0.045f);
            var orbit = cam.gameObject.AddComponent<OrbitCameraRig>();
            orbit.target = rigGo.transform;
            orbit.satellite = rig;

            var post = BuildUtil.DefaultPost;
            post.bloom = 0.7f; post.bloomThreshold = 1.0f; post.vignette = 0.26f; post.contrast = 10f; post.grain = 0.08f;
            BuildUtil.PostVolume("DesignRoom", post);
            return Save(scene, SceneFlow.DesignRoom);
        }

        // ================= MissionControl：控制中心 =================

        static string MissionControl(ArtBuilder.Art art)
        {
            var scene = NewScene();
            Env(null, new Color(0.07f, 0.09f, 0.13f), new Color(0.05f, 0.06f, 0.08f), new Color(0.02f, 0.02f, 0.025f));
            var sun = Sun(new Vector3(0.2f, -0.9f, 0.3f), new Color(0.6f, 0.7f, 0.9f), 0.25f, LightShadows.None);

            var room = BuildUtil.Group("ControlRoom");
            const float X = 16f, Z0 = -13f, Z1 = 15f, H = 8.5f;
            // 霧面地板：高光澤地板會把點光源反射成一條拉長的亮線（在控制台之間看起來像一根發光柱）
            Box("Floor", room, new Vector3(0, -0.1f, (Z0 + Z1) / 2), new Vector3(X * 2, 0.2f, Z1 - Z0), art.floorMatte);
            Box("Ceiling", room, new Vector3(0, H, (Z0 + Z1) / 2), new Vector3(X * 2, 0.2f, Z1 - Z0), art.wallDark, shadows: false);
            Box("FrontWall", room, new Vector3(0, H / 2, Z1), new Vector3(X * 2, H, 0.3f), art.wallDark, shadows: false);
            Box("BackWall", room, new Vector3(0, H / 2, Z0), new Vector3(X * 2, H, 0.3f), art.wallDark, shadows: false);
            Box("LeftWall", room, new Vector3(-X, H / 2, (Z0 + Z1) / 2), new Vector3(0.3f, H, Z1 - Z0), art.wallDark, shadows: false);
            Box("RightWall", room, new Vector3(X, H / 2, (Z0 + Z1) / 2), new Vector3(0.3f, H, Z1 - Z0), art.wallDark, shadows: false);
            for (int i = 0; i < 7; i++)
            {
                float z = Z0 + 2f + i * 4f;
                Box("LeftRib" + i, room, new Vector3(-X + 0.25f, H / 2, z), new Vector3(0.2f, H, 0.5f), art.trim, shadows: false);
                Box("RightRib" + i, room, new Vector3(X - 0.25f, H / 2, z), new Vector3(0.2f, H, 0.5f), art.trim, shadows: false);
            }
            Box("LeftGlow", room, new Vector3(-X + 0.2f, 0.9f, (Z0 + Z1) / 2), new Vector3(0.04f, 0.05f, Z1 - Z0), art.accentGlow, shadows: false);
            Box("RightGlow", room, new Vector3(X - 0.2f, 0.9f, (Z0 + Z1) / 2), new Vector3(0.04f, 0.05f, Z1 - Z0), art.accentGlow, shadows: false);

            // 大螢幕牆：中間地球、兩側遙測
            Box("ScreenWall", room, new Vector3(0, 4.6f, Z1 - 0.25f), new Vector3(30f, 6.6f, 0.2f), art.trim, shadows: false);
            Quad("MainScreen", room, new Vector3(0, 4.6f, Z1 - 0.4f), Quaternion.identity, new Vector2(14f, 6f), art.screenEarth);
            for (int i = 0; i < 4; i++)
            {
                float x = (i < 2 ? -1 : 1) * (i % 2 == 0 ? 8.6f : 12.4f);
                Quad("SideScreen" + i, room, new Vector3(x, 4.6f, Z1 - 0.4f), Quaternion.identity, new Vector2(3.4f, 6f), art.consoleScreens[i]);
            }
            // 螢幕上方的跑馬燈條
            Box("Ticker", room, new Vector3(0, 8.05f, Z1 - 0.4f), new Vector3(30f, 0.35f, 0.05f), art.accentGlow, shadows: false);

            // 三排階梯式控制台，越後排越高
            for (int row = 0; row < 3; row++)
            {
                float z = 6f - row * 4.4f;
                float y = row * 0.45f;
                if (row > 0) Box("Tier" + row, room, new Vector3(0, y / 2, z - 1.1f), new Vector3(X * 2 - 1, y, 4.4f), art.floorMatte);
                int count = 6 - (row == 0 ? 1 : 0);
                for (int c = 0; c < count; c++)
                {
                    float x = (c - (count - 1) / 2f) * 4.4f;
                    var con = BuildUtil.Group($"Console_{row}_{c}", room);
                    con.localPosition = new Vector3(x, y, z);
                    Box("Desk", con, new Vector3(0, 0.75f, 0), new Vector3(4.0f, 0.08f, 1.1f), art.consoleBody);
                    Box("Body", con, new Vector3(0, 0.37f, 0.25f), new Vector3(3.9f, 0.74f, 0.6f), art.consoleBody);
                    Box("Riser", con, new Vector3(0, 1.05f, 0.42f), new Vector3(3.9f, 0.55f, 0.25f), art.trim);
                    for (int s = 0; s < 2; s++)
                    {
                        // 螢幕面向後方的操作員（鏡頭在後面），上緣往前傾 12°
                        Quad("Screen" + s, con, new Vector3((s - 0.5f) * 1.9f, 1.4f, 0.3f), Quaternion.Euler(12f, 0, 0), new Vector2(1.75f, 1.05f), art.consoleScreens[(row * 3 + c + s) % 4]);
                    }
                    Box("Lip", con, new Vector3(0, 0.79f, -0.5f), new Vector3(4.0f, 0.02f, 0.05f), art.accentGlow, shadows: false);
                }
                var l = BuildUtil.Light("RowLight" + row, LightType.Spot, new Color(1f, 0.85f, 0.65f), 45f, new Vector3(0, H - 0.3f, z - 0.5f), Quaternion.Euler(80, 0, 0), 14f, LightShadows.None, room);
                l.spotAngle = 95f;
                l.innerSpotAngle = 60f;
            }
            BuildUtil.Light("ScreenSpill", LightType.Point, new Color(0.35f, 0.6f, 1f), 30f, new Vector3(0, 4.5f, Z1 - 3f), Quaternion.identity, 22f, LightShadows.None, room);

            var cam = MainCamera(new Vector3(0, 5.3f, -10.8f), new Vector3(0, 3.0f, Z1), 46f, 0.1f, 200f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            var drift = cam.gameObject.AddComponent<CameraDrift>();
            drift.positionAmplitude = new Vector3(0.4f, 0.15f, 0.2f);

            var post = BuildUtil.DefaultPost;
            post.bloom = 1.2f; post.bloomThreshold = 0.85f; post.vignette = 0.36f; post.contrast = 16f; post.saturation = 0f;
            post.dof = true; post.dofStart = 12f; post.dofEnd = 34f;
            BuildUtil.PostVolume("MissionControl", post);
            return Save(scene, SceneFlow.MissionControl);
        }

        // ================= Launch：發射 =================

        static string Launch(ArtBuilder.Art art, Mesh sphere)
        {
            var scene = NewScene();
            var kit = LaunchKit.Load();
            var dusk = new Color(0.62f, 0.5f, 0.44f);
            Env(art.daySky, new Color(0.42f, 0.43f, 0.55f), new Color(0.5f, 0.39f, 0.33f), new Color(0.12f, 0.1f, 0.09f), true, dusk, 0.00042f);
            var sun = Sun(Quaternion.Euler(8f, -60f, 0f) * Vector3.forward, new Color(1f, 0.8f, 0.6f), 2.0f);

            // ---------- 發射台 ----------
            var pad = BuildUtil.Group("PadSet");
            const float G = -14f; // 周圍地面高度（發射台是墊高的）
            BuildUtil.Prim(PrimitiveType.Plane, "Ground", pad, new Vector3(0, G, 0), new Vector3(600f, 1f, 600f), art.ground);
            BuildUtil.Prim(PrimitiveType.Plane, "Ocean", pad, new Vector3(0, G - 0.4f, 0), new Vector3(4000f, 1f, 4000f), art.ocean)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Box("Hardstand", pad, new Vector3(0, (G - 1.5f) / 2f, 0), new Vector3(110f, -G - 1.5f, 110f), art.concrete);
            Box("Trench", pad, new Vector3(0, -1.49f, 0), new Vector3(70f, 0.02f, 9f), art.wallDark, shadows: false);

            var scaled = BuildUtil.Group("PadScaled", pad);
            BuildLaunchMount(scaled, art, kit);
            BuildTower(scaled, art);
            var smoke = new[]
            {
                Smoke("Smoke_East", scaled, kit.smoke, new Vector3(9f, -1.2f, 0), Quaternion.Euler(0, 90f, 0), 1),
                Smoke("Smoke_West", scaled, kit.smoke, new Vector3(-9f, -1.2f, 0), Quaternion.Euler(0, -90f, 0), 2),
                Smoke("Smoke_Deluge", scaled, kit.smoke, new Vector3(0, -1.2f, 0), Quaternion.Euler(-90f, 0, 0), 3),
            };
            var deluge = smoke[2];
            {
                // 中央的蒸汽只在發射台附近翻滾，不能往上把鏡頭整個蓋住
                var m = deluge.main;
                m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
                m.startSize = new ParticleSystem.MinMaxCurve(5f, 9f);
                m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
                var sh = deluge.shape;
                sh.angle = 25f;
                sh.radius = 4f;
            }

            // 避雷塔與信標（放在 PadScaled 底下：小火箭的發射台整組比較小）
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(i % 2 == 0 ? -46 : 46, -1.5f, i < 2 ? -46 : 46);
                BuildUtil.Prim(PrimitiveType.Cylinder, "LightningMast" + i, scaled, p + Vector3.up * 48f, new Vector3(0.7f, 48f, 0.7f), art.towerSteel);
                BuildUtil.Prim(PrimitiveType.Sphere, "Beacon" + i, scaled, p + Vector3.up * 96.5f, Vector3.one * 1.1f, art.beacon);
            }
            // 水塔（地標，讓人感覺到尺度）
            var wt = BuildUtil.Group("WaterTower", pad);
            wt.localPosition = new Vector3(-150f, G, 120f);
            for (int i = 0; i < 4; i++)
                Box("Leg" + i, wt, new Vector3(i % 2 == 0 ? -5 : 5, 25f, i < 2 ? -5 : 5), new Vector3(0.8f, 50f, 0.8f), art.towerSteel);
            BuildUtil.Prim(PrimitiveType.Sphere, "Tank", wt, new Vector3(0, 56f, 0), new Vector3(18f, 14f, 18f), art.wallPanel);
            // 遠方的組裝大樓
            Box("AssemblyBuilding", pad, new Vector3(-1700f, G + 80f, 2300f), new Vector3(160f, 160f, 200f), art.wallPanel);
            Box("AssemblyBuildingDoor", pad, new Vector3(-1700f, G + 70f, 2199f), new Vector3(40f, 140f, 1f), art.wallDark, shadows: false);
            for (int i = 0; i < 6; i++)
                Box("Shed" + i, pad, new Vector3(-700f + i * 260f, G + 6f, 900f + (i % 3) * 120f), new Vector3(40f + i * 7f, 12f, 30f), art.concrete);

            // 探照燈：黃昏時把火箭打亮
            var lights = new[] { new Vector3(-45f, 0, -45f), new Vector3(45f, 0, -40f), new Vector3(-30f, 0, 50f) };
            for (int i = 0; i < lights.Length; i++)
            {
                var basePos = lights[i] + new Vector3(0, -1.5f, 0);
                BuildUtil.Prim(PrimitiveType.Cylinder, "LightPole" + i, pad, basePos + Vector3.up * 11f, new Vector3(0.6f, 11f, 0.6f), art.towerSteel);
                var head = basePos + Vector3.up * 22.5f;
                Box("LampHead" + i, pad, head, new Vector3(3f, 1.2f, 0.6f), art.lightPanel, Quaternion.LookRotation(new Vector3(0, 30f, 0) - head));
                // 強度不能太高：9000 時整個發射台過曝，ACES 會把偏藍的高光推成青色
                var l = BuildUtil.Light("Floodlight" + i, LightType.Spot, new Color(1f, 0.94f, 0.85f), 1200f, head, Quaternion.LookRotation(new Vector3(0, 32f, 0) - head), 220f, LightShadows.None, pad);
                l.spotAngle = 26f;
                l.innerSpotAngle = 12f;
            }
            Probe(pad, new Vector3(0, 30f, 0), new Vector3(3000f, 2000f, 3000f), 0.8f);

            var rocketAnchor = BuildUtil.Group("RocketAnchor");
            rocketAnchor.position = Vector3.zero;

            // ---------- 太空 ----------
            var space = BuildUtil.Group("SpaceSet");
            space.position = new Vector3(0, 20000f, 0);
            var env = space.gameObject.AddComponent<EnvironmentOverride>();
            env.skybox = art.starfield;
            env.fog = false;
            const float R = 6000f, alt = 150f;
            // 用高細分的球冠（只做看得到的那一塊）：整顆球 192 段在離地 150 單位時地平線看得到稜角。
            // 火箭下方對準佛羅里達外海（北緯 26°、西經 74°），避開北極與經度 180° 的貼圖接縫。
            var site = GeoMath.LatLonToLocal(26f, -74f);
            var cap = SphereCap("EarthCap_Launch", site, 34f, 160, 256);
            var faceUp = Quaternion.FromToRotation(site, Vector3.up);
            var earthCenter = new Vector3(0, -R - alt, 900f);
            MeshObj("Earth", space, cap, art.earth, earthCenter, R * 2f).transform.localRotation = faceUp;
            MeshObj("Earth_Atmosphere", space, cap, art.atmosphere, earthCenter, R * 2f * 1.025f).transform.localRotation = faceUp;
            var spaceAnchor = BuildUtil.Group("SpaceAnchor", space);
            Probe(space, Vector3.zero, new Vector3(600f, 600f, 600f), 1f);
            space.gameObject.SetActive(false);

            // ---------- 特效、燈、聲音 ----------
            var fx = BuildUtil.Group("FX");
            var fireball = Fireball(fx, kit);
            var debris = Debris(fx, kit);
            var engineLight = BuildUtil.Light("EngineLight", LightType.Point, new Color(1f, 0.68f, 0.38f), 0f, Vector3.zero, Quaternion.identity, 120f, LightShadows.None, fx);
            var flashLight = BuildUtil.Light("FlashLight", LightType.Point, new Color(1f, 0.82f, 0.55f), 0f, Vector3.zero, Quaternion.identity, 300f, LightShadows.None, fx);

            var bank = AudioBank.Load();
            var audio = BuildUtil.Group("Audio");
            var countdown = Source("Countdown", audio, bank != null ? bank.countdownNet : null, 0.95f);
            var roar = Source("Roar", audio, bank != null ? bank.padRoar : null, 1f);
            var rumble = Source("Rumble", audio, bank != null ? bank.distantRumble : null, 0.8f);

            // ---------- 鏡頭 ----------
            var cam = MainCamera(new Vector3(-160f, -6f, -230f), new Vector3(0, 30f, 0), 24f, 0.3f, 30000f);
            var brain = cam.gameObject.AddComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            var noise = AssetDatabase.LoadAssetAtPath<NoiseSettings>("Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset");
            if (noise == null) throw new System.Exception("找不到 Cinemachine 手持雜訊設定");

            var cams = BuildUtil.Group("Cameras");
            // ScreenPosition：0 是中心，正 Y 往下（Cinemachine 3）。火箭的錨點在底部，所以放在偏下方，箭身往上延伸
            var camWide = VCam("CM_Wide", cams, new Vector3(-170f, 2f, -240f), 22f, 0.5f, 9000f, new Vector2(0.05f, 0.28f), 0.4f, noise);
            var camEngine = VCam("CM_Engine", cams, new Vector3(11f, 1.2f, -15f), 42f, 0.2f, 6000f, new Vector2(-0.05f, 0.12f), 0f, noise);
            // 從塔的斜後方低角度仰拍箭身中段（導演依火箭高度設定瞄準點）；鏡頭太貼近箭身時整個畫面只剩一根白柱子
            var camTower = VCam("CM_Tower", cams, new Vector3(-16f, 14f, 24f), 46f, 0.2f, 6000f, new Vector2(0f, 0.1f), 0.12f, noise);
            // 長焦追蹤：瞄準點是火箭底部，所以放在畫面中線偏下，箭身和尾跡才都在畫面裡；阻尼太大火箭會加速衝出畫面上緣
            var camLong = VCam("CM_Long", cams, new Vector3(-950f, G + 4f, -1350f), 6.5f, 1f, 12000f, new Vector2(0f, 0.2f), 0.25f, noise);
            // 側後方要拉開一點：第一節有 44 m 長，鏡頭太靠近軸線會被箭身擋住
            var camSpace = VCam("CM_Space", cams, new Vector3(0, 20000f, -22f), 42f, 0.3f, 30000f, new Vector2(0f, -0.05f), 0.3f, null, new Vector3(15f, 6f, -20f));
            var camSide = VCam("CM_Side", cams, new Vector3(28f, 20000f, 6f), 36f, 0.3f, 30000f, new Vector2(0.1f, 0f), 0.4f, null, new Vector3(30f, 4.5f, 8f));
            // 衛星後側方。上節在切換鏡頭的同時往後退到鏡頭後面（導演處理），否則前三秒畫面一半是白色箭體
            var camDeploy = VCam("CM_Deploy", cams, new Vector3(4f, 20000f, -6f), 38f, 0.1f, 30000f, new Vector2(0f, 0f), 0.5f, null, new Vector3(3.8f, 1.6f, -5.5f));

            var dirGo = new GameObject("LaunchDirector");
            var d = dirGo.AddComponent<LaunchDirector>();
            d.rocketAnchor = rocketAnchor;
            d.spaceAnchor = spaceAnchor;
            d.sun = sun;
            d.engineLight = engineLight;
            d.flashLight = flashLight;
            d.padSmoke = smoke;
            d.fireball = fireball;
            d.debris = debris;
            d.padSet = pad.gameObject;
            d.spaceSet = space.gameObject;
            d.padScaled = scaled;
            d.brain = brain;
            d.camWide = camWide; d.camEngine = camEngine; d.camTower = camTower; d.camLong = camLong;
            d.camSpace = camSpace; d.camSide = camSide; d.camDeploy = camDeploy;
            d.countdown = countdown; d.roar = roar; d.rumble = rumble;

            var post = BuildUtil.DefaultPost;
            post.bloom = 1.3f; post.bloomThreshold = 1.0f; post.vignette = 0.3f; post.contrast = 15f; post.grain = 0.16f;
            post.tint = new Color(1f, 0.97f, 0.93f);
            BuildUtil.PostVolume("Launch", post);
            return Save(scene, SceneFlow.Launch);
        }

        static CinemachineCamera VCam(string name, Transform parent, Vector3 pos, float fov, float near, float far, Vector2 screen, float damping, NoiseSettings noise, Vector3? followOffset = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var vc = go.AddComponent<CinemachineCamera>();
            vc.Lens = LensSettings.Default;
            vc.Lens.FieldOfView = fov;
            vc.Lens.NearClipPlane = near;
            vc.Lens.FarClipPlane = far;
            if (followOffset != null)
            {
                var f = go.AddComponent<CinemachineFollow>();
                f.FollowOffset = followOffset.Value;
                f.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace;
                f.TrackerSettings.PositionDamping = Vector3.one * 0.2f;
            }
            var rc = go.AddComponent<CinemachineRotationComposer>();
            rc.Composition.ScreenPosition = screen;
            rc.Damping = new Vector2(damping, damping);
            if (noise != null)
            {
                var n = go.AddComponent<CinemachineBasicMultiChannelPerlin>();
                n.NoiseProfile = noise;
                n.AmplitudeGain = 0f;
                n.FrequencyGain = 1.2f;
            }
            return vc;
        }

        static AudioSource Source(string name, Transform parent, AudioClip clip, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioSource>();
            a.clip = clip;
            a.volume = volume;
            a.playOnAwake = false;
            a.spatialBlend = 0f;
            a.loop = false;
            return a;
        }

        /// <summary>發射架：四根支柱、頂部框架（火箭底部在原點，噴嘴往下伸進導焰槽）。</summary>
        static void BuildLaunchMount(Transform parent, ArtBuilder.Art art, LaunchKit kit)
        {
            var mount = BuildUtil.Group("LaunchMount", parent);
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(i % 2 == 0 ? -5.5f : 5.5f, -0.8f, i < 2 ? -5.5f : 5.5f);
                Box("Pillar" + i, mount, p, new Vector3(1.4f, 1.6f, 1.4f), art.trim);
            }
            Box("FrameN", mount, new Vector3(0, -0.15f, 5.5f), new Vector3(12.4f, 0.5f, 0.9f), art.towerSteel);
            Box("FrameS", mount, new Vector3(0, -0.15f, -5.5f), new Vector3(12.4f, 0.5f, 0.9f), art.towerSteel);
            Box("FrameE", mount, new Vector3(5.5f, -0.15f, 0), new Vector3(0.9f, 0.5f, 10.2f), art.towerSteel);
            Box("FrameW", mount, new Vector3(-5.5f, -0.15f, 0), new Vector3(0.9f, 0.5f, 10.2f), art.towerSteel);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                var p = Quaternion.Euler(0, a, 0) * new Vector3(0, 0.6f, 2.6f);
                Box("HoldDown" + i, mount, p, new Vector3(0.5f, 1.2f, 0.5f), kit.metal);
            }
        }

        /// <summary>鋼架勤務塔（+Z 側，火箭往 +X 傾斜升空，不會撞到）。</summary>
        static void BuildTower(Transform parent, ArtBuilder.Art art)
        {
            var tower = BuildUtil.Group("ServiceTower", parent);
            const float zc = 12f, half = 3f, top = 82f, bay = 6f;
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector3(i % 2 == 0 ? -half : half, (top - 1.5f) / 2f - 1.5f, zc + (i < 2 ? -half : half));
                Box("Column" + i, tower, p, new Vector3(0.55f, top + 1.5f, 0.55f), art.towerSteel);
            }
            int bays = Mathf.FloorToInt(top / bay);
            float diag = Mathf.Sqrt(half * 2 * half * 2 + bay * bay);
            float ang = Mathf.Atan2(bay, half * 2) * Mathf.Rad2Deg;
            for (int b = 0; b <= bays; b++)
            {
                float y = b * bay;
                for (int s = 0; s < 4; s++)
                {
                    var rot = Quaternion.Euler(0, s * 90f, 0);
                    var face = rot * new Vector3(0, 0, half);
                    var c = new Vector3(0, y, zc) + face;
                    Box($"Beam{b}_{s}", tower, c, new Vector3(half * 2, 0.32f, 0.32f), art.towerSteel, rot);
                    if (b < bays)
                    {
                        var dc = c + Vector3.up * bay / 2f;
                        float sign = (b + s) % 2 == 0 ? 1 : -1;
                        Box($"Brace{b}_{s}", tower, dc, new Vector3(diag, 0.2f, 0.2f), art.towerSteel, rot * Quaternion.Euler(0, 0, sign * ang));
                    }
                }
            }
            // 頂部避雷針、勤務臂、信標
            BuildUtil.Prim(PrimitiveType.Cylinder, "Mast", tower, new Vector3(0, top + 8f, zc), new Vector3(0.35f, 8f, 0.35f), art.towerSteel);
            BuildUtil.Prim(PrimitiveType.Sphere, "TopBeacon", tower, new Vector3(0, top + 16.4f, zc), Vector3.one * 0.8f, art.beacon);
            Box("CrewArm", tower, new Vector3(0, 62f, (zc - half + 2.3f) / 2f), new Vector3(1.6f, 2.2f, zc - half - 2.3f), art.towerSteel);
            Box("CrewArmWindow", tower, new Vector3(0.81f, 62.2f, (zc - half + 2.3f) / 2f), new Vector3(0.02f, 0.6f, zc - half - 2.6f), art.lightPanel, shadows: false);
            for (int i = 0; i < 4; i++)
                BuildUtil.Prim(PrimitiveType.Sphere, "WorkLight" + i, tower, new Vector3(i % 2 == 0 ? -half - 0.3f : half + 0.3f, 15f + i * 18f, zc - half - 0.3f), Vector3.one * 0.5f, art.lightPanel);
        }

        /// <summary>導焰槽排出的煙（翻滾的煙牆）。發射導演控制開關與強度。</summary>
        static ParticleSystem Smoke(string name, Transform parent, Material mat, Vector3 pos, Quaternion rot, int seed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.useAutoRandomSeed = false;
            ps.randomSeed = (uint)(seed * 7919);

            var main = ps.main;
            main.duration = 8f;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 22f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f, 8f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = -0.025f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 600;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var em = ps.emission;
            em.rateOverTime = 0f;
            em.enabled = false;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 1.2f;
            shape.randomDirectionAmount = 0.15f;

            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 1.5f;
            limit.dampen = 0.12f;

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.35f), new Keyframe(0.35f, 0.85f), new Keyframe(1f, 1.6f)));

            var rotM = ps.rotationOverLifetime;
            rotM.enabled = true;
            rotM.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.7f, 0.42f), 0f), new GradientColorKey(new Color(1f, 0.94f, 0.88f), 0.25f), new GradientColorKey(new Color(0.9f, 0.9f, 0.94f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.85f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;

            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Grid;
            tsa.numTilesX = 8;
            tsa.numTilesY = 8;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.cycleCount = 1;
            tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, 0.15f);

            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.minParticleSize = 0f;
            r.maxParticleSize = 5f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static ParticleSystem Fireball(Transform parent, LaunchKit kit)
        {
            var go = new GameObject("Fireball");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 32f);
            main.startSize = new ParticleSystem.MinMaxCurve(16f, 38f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 90), new ParticleSystem.Burst(0.15f, 40) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 5f;
            var lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.limit = 4f;
            lim.dampen = 0.08f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.3f, 1.1f), new Keyframe(1, 1.5f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.95f, 0.8f), 0f), new GradientColorKey(new Color(1f, 0.55f, 0.15f), 0.25f), new GradientColorKey(new Color(0.5f, 0.12f, 0.04f), 0.7f), new GradientColorKey(Color.black, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = kit.fireball;
            r.maxParticleSize = 5f;
            r.shadowCastingMode = ShadowCastingMode.Off;

            // 火球後面接一團黑煙（子物件，跟著父系統一起播放）
            var smokeGo = new GameObject("FireballSmoke");
            smokeGo.transform.SetParent(go.transform, false);
            var sp = smokeGo.AddComponent<ParticleSystem>();
            sp.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = sp.main;
            sm.duration = 1f;
            sm.loop = false;
            sm.playOnAwake = false;
            sm.startDelay = 0.4f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(3f, 12f);
            sm.startSize = new ParticleSystem.MinMaxCurve(20f, 40f);
            sm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            sm.startColor = new Color(0.22f, 0.2f, 0.19f);
            sm.gravityModifier = -0.03f;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            var sem = sp.emission;
            sem.rateOverTime = 0f;
            sem.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });
            var ssh = sp.shape;
            ssh.shapeType = ParticleSystemShapeType.Sphere;
            ssh.radius = 8f;
            var ssz = sp.sizeOverLifetime;
            ssz.enabled = true;
            ssz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.6f), new Keyframe(1, 1.8f)));
            var scol = sp.colorOverLifetime;
            scol.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.1f), new GradientAlphaKey(0f, 1f) });
            scol.color = sg;
            var tsa = sp.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = 8;
            tsa.numTilesY = 8;
            tsa.cycleCount = 1;
            var sr = smokeGo.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = kit.smoke;
            sr.maxParticleSize = 5f;
            sr.sortMode = ParticleSystemSortMode.Distance;
            sr.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        static ParticleSystem Debris(Transform parent, LaunchKit kit)
        {
            var go = new GameObject("Debris");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 1f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(20f, 70f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.4f);
            main.gravityModifier = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 160) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 3f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.9f, 0.6f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.1f), 0.5f), new GradientColorKey(new Color(0.3f, 0.08f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = kit.debris;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 2f;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        // ================= Operations：在軌營運 =================

        static string Operations(ArtBuilder.Art art, Mesh sphere)
        {
            var scene = NewScene();
            Env(art.starfield, new Color(0.03f, 0.035f, 0.05f), new Color(0.02f, 0.022f, 0.03f), Color.black);
            var sunFwd = Quaternion.Euler(14f, -62f, 0f) * Vector3.forward;
            var sun = Sun(sunFwd, new Color(1f, 0.97f, 0.92f), 2.4f, LightShadows.None);

            var planet = MeshObj("Planet", null, sphere, art.earth, Vector3.zero, 2f);
            var atmo = MeshObj("Atmosphere", null, sphere, art.atmosphere, Vector3.zero, 2f * 1.025f);
            var stationRoot = BuildUtil.Group("Stations", planet.transform);

            // 地面站標記（模板，關閉；OpsView 依天線數量複製）
            var marker = BuildUtil.Group("StationMarker").gameObject;
            BuildUtil.Prim(PrimitiveType.Cylinder, "Pad", marker.transform, new Vector3(0, 0, 0), new Vector3(0.02f, 0.002f, 0.02f), art.stationGlow);
            BuildUtil.Prim(PrimitiveType.Cylinder, "Beam", marker.transform, new Vector3(0, 0.014f, 0), new Vector3(0.0025f, 0.014f, 0.0025f), art.stationGlow);
            marker.SetActive(false);

            var satAnchor = BuildUtil.Group("SatelliteAnchor");
            var orbitLine = Line("OrbitLine", art.orbitLine, 0.006f);
            var linkLine = Line("LinkLine", art.linkLine, 0.004f);

            var storm = new GameObject("SolarStorm");
            storm.transform.position = -sunFwd * 7f;
            storm.transform.rotation = Quaternion.LookRotation(sunFwd);
            var ps = storm.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.5f, 5.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.04f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.35f), new Color(1f, 0.45f, 0.2f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 800;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(3.5f, 3.5f, 0.1f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var pr = storm.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = art.storm;
            pr.renderMode = ParticleSystemRenderMode.Stretch;
            pr.lengthScale = 8f;
            pr.velocityScale = 0.05f;
            pr.shadowCastingMode = ShadowCastingMode.Off;

            var pivot = BuildUtil.Group("CameraPivot");
            var pivotSpin = pivot.gameObject.AddComponent<Spin>();
            pivotSpin.axis = Vector3.up;
            pivotSpin.degreesPerSecond = 1.2f;
            var cam = BuildUtil.Camera("Main Camera", Vector3.zero, Quaternion.identity, 40f, 0.02f, 500f);
            cam.transform.SetParent(pivot, false);
            cam.transform.localPosition = new Vector3(0f, 1.05f, -3.7f);
            cam.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, 0.1f, 0) - cam.transform.localPosition);
            var drift = cam.gameObject.AddComponent<CameraDrift>();
            drift.positionAmplitude = new Vector3(0.05f, 0.03f, 0.04f);
            drift.rotationAmplitude = new Vector3(0.3f, 0.4f, 0.2f);

            var view = new GameObject("OpsView").AddComponent<OpsView>();
            view.planet = planet.transform;
            view.planetRenderer = planet.GetComponent<Renderer>();
            view.earthMaterial = art.earth;
            view.moonMaterial = art.moon;
            view.atmosphere = atmo;
            view.satelliteAnchor = satAnchor;
            view.orbitLine = orbitLine;
            view.linkLine = linkLine;
            view.stationRoot = stationRoot;
            view.stationMarkerPrefab = marker;
            view.stormParticles = ps;
            view.sun = sun;
            view.cameraPivot = pivot;
            view.cam = cam;

            var post = BuildUtil.DefaultPost;
            post.bloom = 1.0f; post.bloomThreshold = 0.9f; post.vignette = 0.3f;
            BuildUtil.PostVolume("Operations", post);
            return Save(scene, SceneFlow.Operations);
        }

        static LineRenderer Line(string name, Material mat, float width)
        {
            var go = new GameObject(name);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.positionCount = 0;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.textureMode = LineTextureMode.Stretch;
            return lr;
        }

        // ================= Report：任務報告 =================

        static string Report(ArtBuilder.Art art, Mesh sphere)
        {
            var scene = NewScene();
            Env(art.starfield, new Color(0.03f, 0.035f, 0.05f), new Color(0.02f, 0.022f, 0.03f), Color.black);
            // 太陽在地球邊緣後面剛要升起：地球面向鏡頭這面是夜晚（城市燈光），邊緣有一圈大氣光
            Sun(new Vector3(0.1f, -0.22f, -1f), new Color(1f, 0.92f, 0.8f), 2.6f, LightShadows.None);
            var root = BuildUtil.Group("Limb");
            var earth = Planet("Earth", root, sphere, art.earth, art.atmosphere, new Vector3(0, -62f, 48f), 120f, out _);
            var spin = earth.AddComponent<Spin>();
            spin.axis = new Vector3(0.2f, 1f, 0f);
            spin.degreesPerSecond = 0.25f;
            BuildUtil.Prim(PrimitiveType.Sphere, "SunGlow", root, new Vector3(-6f, 1.2f, 260f), Vector3.one * 2.4f, art.lightPanel)
                .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

            var cam = MainCamera(new Vector3(0, 0, 0), new Vector3(0, 4f, 40f), 42f, 0.1f, 1000f); // 地平線落在畫面下方三分之一
            var drift = cam.gameObject.AddComponent<CameraDrift>();
            drift.positionAmplitude = new Vector3(0.3f, 0.1f, 0.2f);

            var post = BuildUtil.DefaultPost;
            post.bloom = 1.5f; post.bloomThreshold = 0.85f; post.vignette = 0.38f; post.contrast = 14f;
            BuildUtil.PostVolume("Report", post);
            return Save(scene, SceneFlow.Report);
        }
    }
}
