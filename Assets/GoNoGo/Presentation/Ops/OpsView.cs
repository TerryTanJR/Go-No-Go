using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 營運場景的 3D：行星（地球或月球）、軌道線、太空船繞行、地面站與通訊連線、事件特效。
    /// 時間由營運畫面的 OpsClock 推進；繞行速度是「看得清楚」的展示速度，不是真實比例。
    /// </summary>
    public sealed class OpsView : MonoBehaviour
    {
        [Header("場景物件（由 SceneBuilder 指定）")]
        public Transform planet;
        public Renderer planetRenderer;
        public Material earthMaterial, moonMaterial;
        public GameObject atmosphere;
        public Transform satelliteAnchor;
        public LineRenderer orbitLine;
        public LineRenderer linkLine;
        public Transform stationRoot;
        public GameObject stationMarkerPrefab;
        public ParticleSystem stormParticles;
        public Light sun;
        public Transform cameraPivot;
        public Camera cam;

        [Header("展示")]
        public float secondsPerOrbit = 14f;
        public float satelliteScale = 0.035f;

        float _radius;          // 軌道半徑（行星半徑 = 1）
        float _phase;
        Vector3 _orbitNormal;
        readonly List<Transform> _stations = new List<Transform>();
        float _flash;
        GameObject _sat;
        Renderer[] _satRenderers;
        MaterialPropertyBlock _mpb;

        // 地面站位置（緯度、經度）：極區站對極軌道最重要
        static readonly Vector2[] StationSites =
        {
            new Vector2(78.23f, 15.40f),    // 斯瓦巴（挪威）
            new Vector2(64.86f, -147.85f),  // 費爾班克斯（阿拉斯加）
            new Vector2(-77.85f, 166.67f),  // 麥克默多（南極）
            new Vector2(37.94f, -75.46f),   // 瓦勒普斯（維吉尼亞）
            new Vector2(-25.89f, 27.69f),   // 哈特比斯胡克（南非）
            new Vector2(-33.15f, -70.67f),  // 聖地牙哥（智利）
        };

        bool _setup;

        void Start()
        {
            OpsClock.EventTriggered += OnEvent;
            if (_setup) return;
            // 單獨的營運場景（或沒有人呼叫 Setup）：用目前這一局最後一次發射的設計
            GameSession.EnsureMission();
            var m = GameSession.Mission;
            var l = m.S.LastLaunch;
            Setup(l?.Design ?? m.S.Bench, m.Data, l != null && l.Outcome == LaunchOutcome.LowerOrbit);
        }

        /// <summary>依設計重建行星、軌道、地面站和太空船（中控室主螢幕在發射後呼叫）。</summary>
        public void Setup(Design design, GameData data, bool lowerOrbit)
        {
            _setup = true;
            foreach (var st in _stations) if (st != null) Destroy(st.gameObject);
            _stations.Clear();
            if (_sat != null) Destroy(_sat);

            var orbit = data.Orbit(design.OrbitId);
            bool moon = orbit != null && orbit.Body == "moon";
            if (planetRenderer != null) planetRenderer.sharedMaterial = moon ? moonMaterial : earthMaterial;
            if (atmosphere != null) atmosphere.SetActive(!moon);

            float bodyR = moon ? (float)data.Constants.MoonRadiusKm : (float)data.Constants.EarthRadiusKm;
            double alt = orbit?.AltitudeKm ?? 700;
            // 低軌道的高度在畫面上太小，看起來像貼著地面：放大 2.5 倍（只影響視覺）
            bool geo = alt > 20000;
            _radius = geo ? 1f + (float)alt / bodyR : 1f + Mathf.Max(0.06f, (float)alt / bodyR * 2.5f);
            if (lowerOrbit) _radius = 1f + (_radius - 1f) * 0.7f;
            float incl = orbit != null && orbit.Id.StartsWith("sso") ? 98f : orbit != null && orbit.Id == "geo" ? 0f : orbit != null && orbit.Body == "moon" ? 90f : 51.6f;
            _orbitNormal = Quaternion.Euler(0, 0, incl) * Vector3.up;

            secondsPerOrbit = geo ? 40f : 14f;
            float scale = satelliteScale * (geo ? 3.5f : 1f);
            if (cameraPivot != null) cameraPivot.localScale = Vector3.one * (geo ? 4.2f : 1f);

            BuildOrbitLine();
            BuildStations(moon ? 0 : StationCount(design.Get(PartCategory.GroundStation)), moon);

            _sat = SatelliteBuilder.Build(design, data, Evaluator.EvaluateDesign(design, data), satelliteAnchor);
            _sat.transform.localScale = Vector3.one * scale;
            _satRenderers = _sat.GetComponentsInChildren<Renderer>();
            _mpb = new MaterialPropertyBlock();
        }

        void OnDestroy() => OpsClock.EventTriggered -= OnEvent;

        /// <summary>Unity 內建球體半徑 0.5，所以世界半徑 = 0.5 × 縮放。</summary>
        float PlanetRadius => 0.5f * planet.lossyScale.x;

        static int StationCount(string gsId) => gsId switch { "gs_6" => 6, "gs_3" => 3, _ => 1 };

        void BuildOrbitLine()
        {
            if (orbitLine == null) return;
            const int n = 180;
            orbitLine.positionCount = n;
            orbitLine.loop = true;
            for (int i = 0; i < n; i++) orbitLine.SetPosition(i, OrbitPoint(i / (float)n * 360f));
        }

        Vector3 OrbitPoint(float deg)
        {
            var axis = _orbitNormal;
            var start = Vector3.Cross(axis, Vector3.forward).normalized;
            if (start.sqrMagnitude < 1e-4f) start = Vector3.right;
            return planet.position + Quaternion.AngleAxis(deg, axis) * start * _radius * PlanetRadius;
        }

        void BuildStations(int count, bool moon)
        {
            if (stationRoot == null || stationMarkerPrefab == null || moon) return;
            for (int i = 0; i < count && i < StationSites.Length; i++)
            {
                var m = Instantiate(stationMarkerPrefab, stationRoot);
                m.name = "Station" + i;
                m.transform.localPosition = GeoMath.LatLonToLocal(StationSites[i].x, StationSites[i].y, 0.5f * 1.004f);
                m.transform.localRotation = Quaternion.FromToRotation(Vector3.up, m.transform.localPosition.normalized);
                m.SetActive(true);
                _stations.Add(m.transform);
            }
        }

        void OnEvent(MissionEvent e)
        {
            if (e.Kind == EventKind.SolarStorm && stormParticles != null)
            {
                stormParticles.Clear();
                var em = stormParticles.emission;
                em.rateOverTimeMultiplier = 120f * e.Severity;
                stormParticles.Play();
                Invoke(nameof(StopStorm), 3.5f);
            }
            _flash = 1f;
        }

        void StopStorm() { if (stormParticles != null) stormParticles.Stop(); }

        void Update()
        {
            float dt = Time.deltaTime;
            bool paused = OpsClock.Paused;
            // 暫停時（事件視窗）放慢但不完全停，畫面才不會死掉
            _phase += 360f / secondsPerOrbit * dt * (paused ? 0.15f : 1f);
            var pos = OrbitPoint(_phase);
            var next = OrbitPoint(_phase + 1f);
            if (satelliteAnchor != null)
            {
                satelliteAnchor.position = pos;
                var up = (pos - planet.position).normalized;
                // 前進方向沿速度、+Y 指離行星，所以 -Y（儀器面）永遠朝下對地
                satelliteAnchor.rotation = Quaternion.LookRotation((next - pos).normalized, up);
            }

            // 行星自轉：營運中依天數轉（只為了讓地表在動）
            if (planet != null) planet.localRotation = Quaternion.Euler(0, (float)(OpsClock.Day * -0.9), 0);

            UpdateLink(pos);
            UpdateSatelliteLights(dt);
        }

        void UpdateLink(Vector3 satPos)
        {
            if (linkLine == null) return;
            Transform best = null;
            float bestDot = 0.15f; // 仰角門檻（太低看不到）
            foreach (var st in _stations)
            {
                var up = (st.position - planet.position).normalized;
                var to = (satPos - st.position).normalized;
                float d = Vector3.Dot(up, to);
                if (d > bestDot) { bestDot = d; best = st; }
            }
            bool on = best != null && !(OpsClock.Current?.SafeMode ?? false);
            linkLine.enabled = on;
            if (on)
            {
                linkLine.positionCount = 2;
                linkLine.SetPosition(0, best.position);
                linkLine.SetPosition(1, satPos);
                float w = 0.004f + 0.002f * Mathf.Sin(Time.time * 20f);
                linkLine.widthMultiplier = w * PlanetRadius * 2f;
            }
        }

        void UpdateSatelliteLights(float dt)
        {
            if (_satRenderers == null) return;
            _flash = Mathf.Max(0, _flash - dt * 0.8f);
            var cur = OpsClock.Current;
            bool safe = cur != null && cur.SafeMode;
            // 安全模式：琥珀色慢閃；事件：紅色閃一下
            Color c = Color.black;
            if (safe) c = new Color(1f, 0.6f, 0.1f) * (0.6f + 0.6f * Mathf.Sin(Time.time * 3f));
            if (_flash > 0) c += new Color(1f, 0.2f, 0.25f) * _flash * 2f;
            foreach (var r in _satRenderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor("_EmissionColor", c);
                r.SetPropertyBlock(_mpb);
            }
        }
    }
}
