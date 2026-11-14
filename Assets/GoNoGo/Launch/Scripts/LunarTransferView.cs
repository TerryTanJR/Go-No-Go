using System;
using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Presentation;
using Unity.Cinemachine;
using UnityEngine;

namespace GoNoGo.Launch
{
    /// <summary>
    /// 月球任務發射演出的後半段（衛星分離之後）：
    /// <list type="number">
    /// <item>地月轉移俯瞰圖：地球、月球、轉移軌道（虛線）、已經飛過的路徑與衛星光點。位置全部由 <see cref="LunarTransfer"/>
    /// 的克卜勒方程式算，月球照公轉速度往前走，抵達那一刻剛好和衛星會合。約 5 天的滑行用縮時播完。</item>
    /// <item>抵達月球的近景：衛星在月面上方，引擎朝前方噴（反向點火減速），月面往後流過去。</item>
    /// </list>
    /// 俯瞰圖的比例：1 單位 = 100 km。地球放大 3 倍、月球放大 5 倍，不然在 38 萬公里的畫面裡只剩一個點；
    /// 軌道離地心的距離跟著重新對應（地表 → 放大後的地表、月球距離不變），近地點才不會埋進放大的地球裡。
    /// </summary>
    sealed class LunarTransferView
    {
        const float Km = 0.01f, EarthScale = 3f, MoonScale = 5f;
        // 放在其他場景遠處（發射台在原點、近地太空在 y = 20000）
        static readonly Vector3 Origin = new Vector3(0f, 60000f, 0f);
        // 近景的月球：和近地太空場景的地球同一套比例（半徑 6000 單位 = 6371 km）
        const float CloseScale = 6000f / 6371f, CloseAlt = 120f, CapDeg = 40f;

        readonly LunarTransfer _tr;
        readonly Transform _root, _earth, _moon;
        readonly LineRenderer _planned, _flown, _moonArc, _dot;
        readonly float _earthR, _moonR, _moonDist;
        readonly Light _sun;

        public CinemachineCamera Wide { get; }
        public CinemachineCamera Approach { get; }

        // 近景
        Renderer _closeMoon;
        MaterialPropertyBlock _closeMpb;
        Transform _payload;
        Renderer _plume;
        Vector3 _travel;
        float _surfaceDeg;
        Matrix4x4 _texBase;
        Quaternion _yaw = Quaternion.identity;
        Quaternion _wideSun;

        static readonly int TexRotX = Shader.PropertyToID("_TexRotX");
        static readonly int TexRotY = Shader.PropertyToID("_TexRotY");
        static readonly int TexRotZ = Shader.PropertyToID("_TexRotZ");
        static readonly int ThrottleId = Shader.PropertyToID("_Throttle");

        public LunarTransferView(LunarTransfer tr, LaunchKit kit, Material earthMat, Material atmoMat, Light sun, Quaternion yaw)
        {
            _tr = tr;
            _sun = sun;
            _yaw = yaw;
            _earthR = (float)LunarTransfer.EarthRadiusKm * Km * EarthScale;
            _moonR = (float)LunarTransfer.MoonRadiusKm * Km * MoonScale;
            _moonDist = (float)LunarTransfer.MoonDistanceKm * Km;

            _root = new GameObject("CislunarSet").transform;
            _root.position = Origin;
            _earth = Sphere("Earth", _root, earthMat, _earthR);
            if (atmoMat != null) Sphere("Earth_Atmosphere", _earth, atmoMat, 0.5125f); // 掛在地球底下：局部 1.025 倍
            _moon = Sphere("Moon", _root, kit != null ? kit.moon : null, _moonR);

            var lineMat = kit != null ? kit.line : null;
            var solidMat = kit != null ? kit.trail : null;
            _moonArc = Line("MoonPath", lineMat, 20f, new Color(0.6f, 0.6f, 0.65f, 0.7f), LineTextureMode.RepeatPerSegment);
            _planned = Line("PlannedPath", lineMat, 24f, new Color(1f, 1f, 1f, 0.85f), LineTextureMode.RepeatPerSegment);
            _flown = Line("FlownPath", solidMat, 16f, new Color(0.45f, 0.9f, 1f, 1f), LineTextureMode.Stretch);
            _dot = Line("Spacecraft", lineMat, 60f, new Color(1f, 1f, 1f, 1f), LineTextureMode.Stretch);
            _dot.positionCount = 2;

            BuildPlannedPath();
            BuildMoonArc();

            // 太陽從鏡頭左前方照過來：地球、月球都看得到亮面和晨昏線
            _wideSun = Quaternion.LookRotation(new Vector3(0.75f, -0.35f, 0.55f));
            Wide = Cam("CM_Cislunar", 36f);
            Approach = Cam("CM_LunarApproach", 34f);
            _root.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- 俯瞰圖

        /// <summary>真實距離（km，離地心）→ 俯瞰圖上離地心的距離。</summary>
        float R(double rKm) => _earthR + (float)((rKm - LunarTransfer.EarthRadiusKm) / (LunarTransfer.MoonDistanceKm - LunarTransfer.EarthRadiusKm)) * (_moonDist - _earthR);

        /// <summary>轉移軌道平面（XZ）上的點：角度 0 是近地點方向（+X），往 +Z 繞。</summary>
        Vector3 P(double angle, float r) => _root.position + new Vector3(Mathf.Cos((float)angle), 0f, Mathf.Sin((float)angle)) * r;

        void BuildPlannedPath()
        {
            // 先細細取樣，再每隔固定距離取一點：每一段一個虛線點，間距才會平均
            var fine = new List<Vector3>();
            for (int i = 0; i <= 600; i++)
            {
                _tr.AtEccentricAnomaly(Math.PI * i / 600, out var r, out var nu);
                fine.Add(P(nu, R(r)));
            }
            var pts = Resample(fine, 55f);
            _planned.positionCount = pts.Count;
            _planned.SetPositions(pts.ToArray());
        }

        void BuildMoonArc()
        {
            double a0 = _tr.MoonAngle(_tr.TliT) - 0.15, a1 = _tr.MoonAngle(_tr.ArriveT) + 0.25;
            var fine = new List<Vector3>();
            for (int i = 0; i <= 200; i++) fine.Add(P(a0 + (a1 - a0) * i / 200, _moonDist));
            var pts = Resample(fine, 70f);
            _moonArc.positionCount = pts.Count;
            _moonArc.SetPositions(pts.ToArray());
        }

        static List<Vector3> Resample(List<Vector3> fine, float step)
        {
            var pts = new List<Vector3> { fine[0] };
            float acc = 0f;
            for (int i = 1; i < fine.Count; i++)
            {
                acc += Vector3.Distance(fine[i - 1], fine[i]);
                if (acc >= step) { pts.Add(fine[i]); acc = 0f; }
            }
            return pts;
        }

        public void ShowTransfer()
        {
            _root.gameObject.SetActive(true);
            if (_payload != null) _payload.gameObject.SetActive(false);
            if (_closeMoon != null) _closeMoon.gameObject.SetActive(false);
            if (_sun != null) _sun.transform.rotation = _wideSun;
            foreach (var c in new[] { Wide, Approach }) c.PreviousStateIsValid = false;
        }

        /// <summary>每一幀：衛星、月球、已飛路徑、鏡頭都由任務時間 t 決定（快轉、跳過都一樣）。</summary>
        public void Tick(double t)
        {
            if (!_root.gameObject.activeSelf) return;
            double tau = Math.Max(0, Math.Min(t, _tr.ArriveT) - _tr.TliT);
            double eNow = _tr.EccentricAnomaly(tau);
            _tr.AtEccentricAnomaly(eNow, out var rNow, out var nuNow);
            var sat = P(nuNow, R(rNow));

            // 已經飛過的路徑
            const int n = 90;
            _flown.positionCount = n + 1;
            for (int i = 0; i <= n; i++)
            {
                _tr.AtEccentricAnomaly(eNow * i / n, out var r, out var nu);
                _flown.SetPosition(i, P(nu, R(r)));
            }
            // 光點：一段跟寬度一樣長的線，柔和圓點貼圖剛好貼滿成一個發光的點
            _tr.AtEccentricAnomaly(Math.Max(0, eNow - 0.002), out var rb, out var nub);
            var dir = (sat - P(nub, R(rb))).normalized;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            _dot.SetPosition(0, sat - dir * 30f);
            _dot.SetPosition(1, sat + dir * 30f);

            // 月球公轉，近側永遠朝地球（潮汐鎖定）；地球自轉一天一圈
            double moonA = _tr.MoonAngle(t);
            _moon.position = P(moonA, _moonDist);
            _moon.rotation = Quaternion.Euler(0f, -(float)((moonA + Math.PI) * Mathf.Rad2Deg), 0f);
            _earth.rotation = Quaternion.Euler(0f, -(float)((t / 86164.0 * 360.0) % 360.0), 0f);

            // 鏡頭：俯瞰整段轉移（慢慢繞一點角度）；最後一段改從月球旁邊看衛星飛進來
            float p = (float)Math.Max(0, Math.Min(1, (t - _tr.TliT) / (_tr.ArriveT - _tr.TliT)));
            // 接近正上方往下看（約 72°），從月球那一側看過去：地球在左上、抵達點在右上，
            // 月球從畫面下方繞上來會合（避開左下的遙測和右下的播報），讀起來像一張地圖
            var center = _root.position + new Vector3(-_moonDist * 0.5f, 0f, _moonDist * 0.27f);
            var off = Quaternion.Euler(0f, 8f * p, 0f) * new Vector3(0f, 5200f, 1700f);
            Wide.transform.position = center + off;
            Wide.transform.rotation = Quaternion.LookRotation(center - Wide.transform.position, Vector3.up);

            var moon = _moon.position;
            var toSat = sat - moon;
            var look = moon + Vector3.ClampMagnitude(toSat, _moonR * 6f) * 0.45f;
            Approach.transform.position = moon + new Vector3(_moonR * 9f, _moonR * 7f, -_moonR * 10f);
            Approach.transform.rotation = Quaternion.LookRotation(look - Approach.transform.position, Vector3.up);
        }

        /// <summary>改用近景鏡頭的時間：衛星離月球不到 6 萬公里（縮時的最後幾秒）。</summary>
        public double ApproachT()
        {
            double lo = _tr.TliT, hi = _tr.ArriveT;
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2;
                _tr.State(mid - _tr.TliT, out var r, out _, out var nu);
                double dx = r * Math.Cos(nu) - LunarTransfer.MoonDistanceKm * Math.Cos(_tr.MoonAngle(mid));
                double dz = r * Math.Sin(nu) - LunarTransfer.MoonDistanceKm * Math.Sin(_tr.MoonAngle(mid));
                if (Math.Sqrt(dx * dx + dz * dz) > 60000) lo = mid; else hi = mid;
            }
            return lo;
        }

        // ---------------------------------------------------------------- 抵達月球

        /// <summary>
        /// 近景：月球表面放在衛星下方（和近地太空場景的地球同樣的比例與相對位置），
        /// 衛星轉成引擎朝前（反向點火），尾焰接在推進器的位置。
        /// </summary>
        public void ShowArrival(Transform payload, Transform spaceSet, LaunchKit kit, float payloadSize)
        {
            _root.gameObject.SetActive(false);
            _payload = payload;
            if (_payload == null) return;
            _payload.gameObject.SetActive(true);
            var up = Vector3.up;
            // 飛行方向朝畫面左邊（展開鏡頭在衛星右後方），尾焰往左噴、看得到整條
            _travel = _yaw * Vector3.left;
            _payload.rotation = Quaternion.LookRotation(_travel, up);

            if (_closeMoon == null && kit != null && kit.moon != null)
            {
                float R = 6000f * (float)(LunarTransfer.MoonRadiusKm / LunarTransfer.EarthRadiusKm);
                var go = new GameObject("MoonCap_Arrival");
                go.AddComponent<MeshFilter>().sharedMesh = SphereCap(CapDeg, 120, 256);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = kit.moon;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                if (spaceSet != null) go.transform.SetParent(spaceSet, true);
                go.transform.localScale = Vector3.one * R;
                go.transform.position = _payload.position + new Vector3(0f, -(R + CloseAlt), 0f) + _yaw * new Vector3(0f, 0f, 900f * R / 6000f);
                go.transform.rotation = Quaternion.identity;
                _closeMoon = mr;
                _closeMpb = new MaterialPropertyBlock();
                // 球冠的頂端（物體 +Y）對到月球近側的赤道（經度 0、緯度 0），而不是被拉伸的極區
                _texBase = Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, -90f));
            }
            if (_closeMoon != null) _closeMoon.gameObject.SetActive(true);

            if (_plume == null && kit != null && kit.plume != null)
            {
                // 推進器在衛星的 +Z 面（SatelliteBuilder）；沒有推進器的設計就從同一面噴
                var nozzle = new GameObject("LoiNozzle").transform;
                nozzle.SetParent(_payload, false);
                var prop = _payload.GetComponentsInChildren<SatPart>().FirstOrDefault(p => p.category == PartCategory.Propulsion);
                var rs = (prop != null ? prop.GetComponentsInChildren<Renderer>() : null) ?? Array.Empty<Renderer>();
                if (rs.Length == 0) rs = _payload.GetComponentsInChildren<Renderer>();
                var b = rs.Length > 0 ? rs[0].bounds : new Bounds(_payload.position, Vector3.one);
                foreach (var r in rs) b.Encapsulate(r.bounds);
                nozzle.position = b.center + _payload.forward * Vector3.Dot(b.extents, Abs(_payload.forward));
                nozzle.rotation = Quaternion.FromToRotation(Vector3.down, _payload.forward);
                var s = _payload.lossyScale.x;
                nozzle.localScale = Vector3.one / Mathf.Max(1e-4f, s);
                _plume = RocketBuilder.Plume(nozzle, Vector3.zero, Mathf.Max(0.04f, payloadSize * 0.06f), kit.plume);
            }
            SetBurn(false);
            if (_sun != null)
            {
                // 太陽從斜後方低角度照：月面有明暗漸層，衛星也是亮的
                _sun.transform.rotation = Quaternion.LookRotation(_yaw * new Vector3(0.55f, -0.45f, 0.7f));
            }
            ApplySurface();
        }

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        public void SetBurn(bool on)
        {
            if (_plume != null) _plume.enabled = on;
        }

        /// <summary>近景每一幀：月面照軌道角速度往後流（任務時間；縮時時流得快），尾焰閃爍。</summary>
        public void TickArrival(float missionDt, double speedKms)
        {
            if (_closeMoon == null || !_closeMoon.gameObject.activeSelf) return;
            double w = speedKms / (LunarTransfer.MoonRadiusKm + _tr.OrbitAltKm); // rad/s
            _surfaceDeg = (float)((_surfaceDeg + w * missionDt * Mathf.Rad2Deg) % 360.0);
            ApplySurface();
            if (_plume != null && _plume.enabled)
            {
                float flicker = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 18f, 0.7f);
                var sc = _plume.transform.localScale;
                _plume.transform.localScale = new Vector3(sc.x, sc.x * 6f * flicker, sc.z);
                var mpb = new MaterialPropertyBlock();
                _plume.GetPropertyBlock(mpb);
                mpb.SetFloat(ThrottleId, 1f);
                _plume.SetPropertyBlock(mpb);
            }
        }

        void ApplySurface()
        {
            if (_closeMoon == null) return;
            // 月面上衛星正下方的點往「飛行的反方向」走：整顆月球繞 (上 × 後方) 轉
            var axis = Vector3.Cross(Vector3.up, -_travel);
            var m = _closeMoon.transform.rotation;
            var spin = Quaternion.AngleAxis(_surfaceDeg, axis);
            var tex = _texBase * Matrix4x4.Rotate(Quaternion.Inverse(m) * Quaternion.Inverse(spin) * m);
            _closeMoon.GetPropertyBlock(_closeMpb);
            _closeMpb.SetVector(TexRotX, tex.GetRow(0));
            _closeMpb.SetVector(TexRotY, tex.GetRow(1));
            _closeMpb.SetVector(TexRotZ, tex.GetRow(2));
            _closeMoon.SetPropertyBlock(_closeMpb);
        }

        // ---------------------------------------------------------------- 建立物件

        static Transform Sphere(string name, Transform parent, Material mat, float radius)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * radius * 2f;
            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go.transform;
        }

        LineRenderer Line(string name, Material mat, float width, Color c, LineTextureMode mode)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            if (mat != null) lr.sharedMaterial = mat;
            lr.widthMultiplier = width;
            lr.startColor = lr.endColor = c;
            lr.textureMode = mode;
            lr.alignment = LineAlignment.View;
            lr.numCapVertices = 0;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.positionCount = 0;
            return lr;
        }

        CinemachineCamera Cam(string name, float fov)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var vc = go.AddComponent<CinemachineCamera>();
            var lens = LensSettings.Default;
            lens.FieldOfView = fov;
            lens.NearClipPlane = 5f;
            lens.FarClipPlane = 20000f;
            vc.Lens = lens;
            return vc;
        }

        /// <summary>以 +Y 為中心、半角 capDeg 的單位球冠（只做看得到的那一塊，地平線才不會有稜角）。</summary>
        static Mesh SphereCap(float capDeg, int rings, int segs)
        {
            var verts = new List<Vector3>((rings + 1) * (segs + 1));
            var tris = new List<int>(rings * segs * 6);
            for (int i = 0; i <= rings; i++)
            {
                float th = capDeg * Mathf.Deg2Rad * i / rings;
                for (int j = 0; j <= segs; j++)
                {
                    float ph = 2f * Mathf.PI * j / segs;
                    verts.Add(new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph)));
                }
            }
            int row = segs + 1;
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < segs; j++)
                {
                    int a = i * row + j, b = a + row;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            var mesh = new Mesh { name = "MoonCap" };
            mesh.SetVertices(verts);
            mesh.SetNormals(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
