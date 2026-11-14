using System.Collections.Generic;
using UnityEngine;

namespace GoNoGo.Launch
{
    /// <summary>程序化火箭的各個部位（發射導演用來分離、點火、拋整流罩）。</summary>
    public sealed class RocketParts
    {
        public Transform root;
        public Transform firstStage;
        public Transform upperStage;
        public Transform fairingLeft, fairingRight;
        public Transform payloadMount;
        public readonly List<Renderer> firstStagePlumes = new List<Renderer>();
        public readonly List<Renderer> upperPlumes = new List<Renderer>();
        /// <summary>
        /// 上面的節中途會丟掉的部分（由下往上；最後一節不算）：Taurus XL 的第 1、2 節、Electron 的第二節（踢昇級在上面）。
        /// 一般的兩節火箭是空的。每一節有自己的尾焰。
        /// </summary>
        public readonly List<Transform> upperSegments = new List<Transform>();
        public readonly List<Renderer> segmentPlumes = new List<Renderer>();
        /// <summary>每一節（含最後一節）的底部高度（upperStage 區域座標），最後一個是整流罩底部。</summary>
        public readonly List<float> segmentBase = new List<float>();
        public Transform firstStageNozzles;
        public float height;
        public float diameter;
        public float fairingBase;
        public float fairingRadius;
        /// <summary>共乘（Transporter）：整流罩裡除了玩家的衛星，還有一整疊別人的衛星。</summary>
        public bool rideshare;
    }

    /// <summary>
    /// 依火箭 id 組出外形比例接近真實的通用火箭（不使用任何公司塗裝）：
    /// 長度與直徑取自公開規格，單位公尺。Falcon Heavy 有兩支側推進器、Delta II 有固體助推器。
    /// </summary>
    public static class RocketBuilder
    {
        struct Spec
        {
            public float height, diameter, fairingD, fairingLen, upperLen, boosters, boosterLen, boosterD;
            /// <summary>上面各節的長度（由下往上，最後一個是帶著衛星的那一節）；null = 只有一節（upperLen）。</summary>
            public float[] upperSegs;
            public int engines;
            public string body; // white / black / teal
            public bool rideshare;
        }

        static Spec SpecFor(string id)
        {
            switch (id)
            {
                // 第二節 2.4 m，上面是 0.45 m 的踢昇級（Curie 引擎），裝在整流罩底部
                case "electron": return new Spec { height = 18f, diameter = 1.2f, fairingD = 1.2f, fairingLen = 2.5f, upperSegs = new[] { 2.4f, 0.45f }, engines = 9, body = "black" };
                case "falcon_heavy": return new Spec { height = 70f, diameter = 3.66f, fairingD = 5.2f, fairingLen = 13f, upperLen = 12.6f, engines = 9, boosters = 2, boosterLen = 44f, boosterD = 3.66f, body = "white" };
                // 四節固體火箭：第 0 節 Castor 120，上面是 Orion 50SXLG、Orion 50XLG、Orion 38（直徑比第 0 節細）
                case "taurus_xl": return new Spec { height = 31.5f, diameter = 2.35f, fairingD = 1.6f, fairingLen = 5.1f, upperSegs = new[] { 8.9f, 3.1f, 1.6f }, engines = 1, body = "white" };
                // Delta II 7320（OCO-2 用的構型）：三支 GEM-40 固體助推器
                case "delta_ii": return new Spec { height = 38.9f, diameter = 2.44f, fairingD = 2.9f, fairingLen = 8.5f, upperLen = 6f, engines = 1, boosters = 3, boosterLen = 12.8f, boosterD = 1.0f, body = "teal" };
                case "falcon9_rideshare": return new Spec { height = 70f, diameter = 3.66f, fairingD = 5.2f, fairingLen = 13f, upperLen = 12.6f, engines = 9, body = "white", rideshare = true };
                default: return new Spec { height = 70f, diameter = 3.66f, fairingD = 5.2f, fairingLen = 13f, upperLen = 12.6f, engines = 9, body = "white" }; // falcon9
            }
        }

        public static RocketParts Build(string launcherId, LaunchKit kit, Transform parent)
        {
            var sp = SpecFor(launcherId);
            var p = new RocketParts { height = sp.height, diameter = sp.diameter, rideshare = sp.rideshare };
            var root = new GameObject("Rocket_" + launcherId).transform;
            root.SetParent(parent, false);
            p.root = root;

            var bodyMat = sp.body == "black" ? kit.paintBlack : sp.body == "teal" ? kit.paintTeal : kit.paintWhite;
            float r = sp.diameter / 2f;
            var segs = sp.upperSegs ?? new[] { sp.upperLen };
            float upperLen = 0;
            foreach (var l in segs) upperLen += l;
            float firstLen = sp.height - upperLen - sp.fairingLen;
            // 上面的節不比整流罩粗（Taurus 的上節只有 1.3 m 粗）
            float ur = Mathf.Min(r, sp.fairingD / 2f);

            // ---- 第一節 ----
            p.firstStage = new GameObject("FirstStage").transform;
            p.firstStage.SetParent(root, false);
            Cyl(p.firstStage, new Vector3(0, firstLen / 2, 0), r, firstLen, bodyMat);
            // 段間環（上節比較細的話做成往上收的錐形）
            if (ur < r - 0.05f) Cone(p.firstStage, new Vector3(0, firstLen - 1.2f, 0), r, ur, 1.2f, kit.interstage);
            else Cyl(p.firstStage, new Vector3(0, firstLen - 0.6f, 0), r * 1.01f, 1.2f, kit.interstage);
            // 引擎段與噴嘴
            p.firstStageNozzles = new GameObject("Engines").transform;
            p.firstStageNozzles.SetParent(p.firstStage, false);
            Cyl(p.firstStage, new Vector3(0, 0.3f, 0), r * 0.98f, 0.6f, kit.metal);
            foreach (var pos in EnginePattern(sp.engines, r * 0.62f))
            {
                float nr = sp.engines > 1 ? r * 0.2f : r * 0.55f;
                Nozzle(p.firstStageNozzles, new Vector3(pos.x, -nr * 0.9f, pos.y), nr, nr * 1.8f, kit.metal);
                p.firstStagePlumes.Add(Plume(p.firstStageNozzles, new Vector3(pos.x, -nr * 1.8f, pos.y), nr * 0.95f, kit.plume));
            }
            // 助推器（Falcon Heavy 側核心 / Delta II 固體）
            if (sp.boosters > 0)
            {
                int n = Mathf.RoundToInt(sp.boosters);
                for (int i = 0; i < n; i++)
                {
                    float a = (n == 2 ? (i == 0 ? 0 : 180) : i * 360f / n) * Mathf.Deg2Rad;
                    float br = sp.boosterD / 2f;
                    var c = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (r + br + 0.05f);
                    var b = new GameObject("Booster" + i).transform;
                    b.SetParent(p.firstStage, false);
                    b.localPosition = c;
                    Cyl(b, new Vector3(0, sp.boosterLen / 2, 0), br, sp.boosterLen, sp.boosters == 2 ? bodyMat : kit.paintWhite);
                    Nose(b, new Vector3(0, sp.boosterLen, 0), br, br * (sp.boosters == 2 ? 0.2f : 2.2f), sp.boosters == 2 ? kit.interstage : kit.paintWhite);
                    Nozzle(b, new Vector3(0, -br * 0.4f, 0), br * 0.6f, br * 1.1f, kit.metal);
                    p.firstStagePlumes.Add(Plume(b, new Vector3(0, -br * 1.0f, 0), br * 0.6f, kit.plume));
                }
            }

            // ---- 上節 ----
            p.upperStage = new GameObject("UpperStage").transform;
            p.upperStage.SetParent(root, false);
            p.upperStage.localPosition = new Vector3(0, firstLen, 0);
            float y = 0;
            for (int i = 0; i < segs.Length; i++)
            {
                bool last = i == segs.Length - 1;
                float len = segs[i];
                // 越上面越細一點（多節固體火箭）；踢昇級是一個扁平的圓盤
                float sr = segs.Length > 1 ? ur * (last && len < 1f ? 0.8f : 1f - 0.08f * i) : ur;
                var seg = new GameObject(last ? "FinalStage" : "UpperSegment" + i).transform;
                seg.SetParent(p.upperStage, false);
                seg.localPosition = new Vector3(0, y, 0);
                Cyl(seg, new Vector3(0, len / 2, 0), sr, len, last && len < 1f ? kit.metal : bodyMat);
                if (!last) Cyl(seg, new Vector3(0, len - 0.25f, 0), sr * 1.01f, 0.5f, kit.interstage);
                float nr = len < 1f ? sr * 0.35f : sr * 0.55f;
                Nozzle(seg, new Vector3(0, -nr * 0.9f, 0), nr, nr * 2.2f, kit.metal);
                var plume = Plume(seg, new Vector3(0, -nr * 2f, 0), nr * 1.1f, kit.plume);
                p.segmentBase.Add(y);
                if (last) p.upperPlumes.Add(plume);
                else { p.upperSegments.Add(seg); p.segmentPlumes.Add(plume); }
                y += len;
            }

            // ---- 整流罩（兩半，可以分開） ----
            float fr = sp.fairingD / 2f;
            p.fairingRadius = fr;
            float fBase = upperLen;
            p.segmentBase.Add(fBase);
            p.fairingBase = firstLen + fBase;
            p.payloadMount = new GameObject("PayloadMount").transform;
            p.payloadMount.SetParent(p.upperStage, false);
            p.payloadMount.localPosition = new Vector3(0, fBase + 0.3f, 0);
            // 擴張段：整流罩比上節粗時（Falcon 9 是 3.66 → 5.2 m）做成斜的轉接段，太短的話看起來像一片圓盤
            float flare = Mathf.Max(0.6f, (fr - ur) * 1.6f);
            if (fr > ur + 0.05f) Cone(p.upperStage, new Vector3(0, fBase - flare, 0), ur, fr, flare, bodyMat);
            p.fairingLeft = Half(p.upperStage, fBase, fr, sp.fairingLen, kit.paintWhite, true);
            p.fairingRight = Half(p.upperStage, fBase, fr, sp.fairingLen, kit.paintWhite, false);

            foreach (var pr in p.firstStagePlumes) pr.enabled = false;
            foreach (var pr in p.upperPlumes) pr.enabled = false;
            foreach (var pr in p.segmentPlumes) pr.enabled = false;
            return p;
        }

        static IEnumerable<Vector2> EnginePattern(int n, float ring)
        {
            if (n <= 1) { yield return Vector2.zero; yield break; }
            yield return Vector2.zero;
            for (int i = 0; i < n - 1; i++)
            {
                float a = i / (float)(n - 1) * Mathf.PI * 2;
                yield return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
            }
        }

        // ---------------- 幾何 ----------------

        static Mesh _cyl;
        static Mesh CylMesh => _cyl ??= Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

        static GameObject Obj(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            return go;
        }

        static GameObject Cyl(Transform parent, Vector3 center, float radius, float height, Material mat)
        {
            var go = Obj(parent, "cyl", CylMesh, mat);
            go.transform.localPosition = center;
            // 照網格實際大小縮放：Resources.GetBuiltinResource 拿到的 Cylinder.fbx 直徑是 2（不是 CreatePrimitive 的 1），
            // 寫死「直徑 1」的話每一節都會粗一倍（Falcon 9 變成 7.3 m，比 5.2 m 的整流罩還粗）
            var m = CylMesh.bounds.size;
            go.transform.localScale = new Vector3(radius * 2 / m.x, height / m.y, radius * 2 / m.z);
            return go;
        }

        static GameObject Nozzle(Transform parent, Vector3 center, float radius, float length, Material mat)
        {
            var go = Obj(parent, "nozzle", Lathe("Nozzle", new[] { new Vector2(0.35f, 1f), new Vector2(0.55f, 0.6f), new Vector2(0.85f, 0.25f), new Vector2(1f, 0f) }, 20), mat);
            go.transform.localPosition = center - new Vector3(0, length / 2, 0);
            go.transform.localScale = new Vector3(radius, length, radius);
            return go;
        }

        static Renderer Plume(Transform parent, Vector3 top, float radius, Material mat)
        {
            // 尾焰：旋轉體 y=0 接噴口、y=1 是尾端；繞 X 轉 180° 讓它往下長。長度由導演依推力與高度調整（縮放 Y）
            var go = Obj(parent, "plume", Lathe("Plume", new[] { new Vector2(0.9f, 0f), new Vector2(1.25f, 0.25f), new Vector2(1.5f, 0.65f), new Vector2(1.2f, 1f) }, 20, true), mat);
            go.transform.localPosition = top;
            go.transform.localScale = new Vector3(radius, radius * 9f, radius);
            go.transform.localRotation = Quaternion.Euler(180, 0, 0);
            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return mr;
        }

        static void Nose(Transform parent, Vector3 baseCenter, float radius, float length, Material mat)
        {
            var go = Obj(parent, "nose", Lathe("Nose", new[] { new Vector2(1f, 0f), new Vector2(0.92f, 0.35f), new Vector2(0.65f, 0.7f), new Vector2(0.25f, 0.95f), new Vector2(0.02f, 1f) }, 20), mat);
            go.transform.localPosition = baseCenter;
            go.transform.localScale = new Vector3(radius, length, radius);
        }

        static void Cone(Transform parent, Vector3 baseCenter, float r0, float r1, float length, Material mat)
        {
            var go = Obj(parent, "flare", Lathe("Flare", new[] { new Vector2(r0 / r1, 0f), new Vector2(1f, 1f) }, 20), mat);
            go.transform.localPosition = baseCenter;
            go.transform.localScale = new Vector3(r1, length, r1);
        }

        /// <summary>整流罩的半邊：圓柱段 + 尖拱，鉸鏈在底部外側，分離時繞鉸鏈往外倒。</summary>
        static Transform Half(Transform parent, float baseY, float radius, float length, Material mat, bool left)
        {
            float side = left ? -1 : 1;
            var hinge = new GameObject(left ? "FairingL" : "FairingR").transform;
            hinge.SetParent(parent, false);
            hinge.localPosition = new Vector3(side * radius, baseY, 0);
            var prof = new[] { new Vector2(1f, 0f), new Vector2(1f, 0.45f), new Vector2(0.93f, 0.62f), new Vector2(0.76f, 0.78f), new Vector2(0.5f, 0.91f), new Vector2(0.2f, 0.985f), new Vector2(0.02f, 1f) };
            var go = Obj(hinge, "shell", Lathe("FairingHalf", prof, 24, false, left ? 90f : -90f, 180f), mat);
            go.transform.localPosition = new Vector3(-side * radius, 0, 0);
            go.transform.localScale = new Vector3(radius, length, radius);
            return hinge;
        }

        /// <summary>
        /// 旋轉體網格：profile 是 (半徑比例, 高度比例) 由下往上；可只做部分角度（整流罩半邊）。
        /// </summary>
        static Mesh Lathe(string name, Vector2[] profile, int seg, bool open = false, float startDeg = 0f, float sweepDeg = 360f)
        {
            var v = new List<Vector3>();
            var uv = new List<Vector2>();
            var t = new List<int>();
            int rows = profile.Length;
            for (int i = 0; i <= seg; i++)
            {
                float a = (startDeg + sweepDeg * i / seg) * Mathf.Deg2Rad;
                for (int j = 0; j < rows; j++)
                {
                    v.Add(new Vector3(Mathf.Cos(a) * profile[j].x, profile[j].y, Mathf.Sin(a) * profile[j].x));
                    uv.Add(new Vector2(i / (float)seg, profile[j].y));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < rows - 1; j++)
                {
                    int a = i * rows + j, b = a + 1, c = a + rows, d = c + 1;
                    t.AddRange(new[] { a, b, c, b, d, c });
                }
            // 內面（半邊整流罩、噴嘴從裡面看也要有東西）
            int n = v.Count;
            var verts = new List<Vector3>(v);
            verts.AddRange(v);
            var uvs = new List<Vector2>(uv);
            uvs.AddRange(uv);
            var all = new List<int>(t);
            for (int k = 0; k < t.Count; k += 3) all.AddRange(new[] { t[k] + n, t[k + 2] + n, t[k + 1] + n });
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(open ? t : all, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
