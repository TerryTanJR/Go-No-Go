using System.Collections.Generic;
using GoNoGo.Core;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 依設計組出程序化的太空船模型：本體大小跟著發射質量、太陽能板依類型展開、天線依等級換大小、
    /// 每台儀器有自己的外形（相機鏡頭、熱像儀散熱片、SAR 平板天線…），推進與屏蔽也看得出差別。
    /// 座標：-Y 朝地（儀器在下面），+Y 接火箭轉接環，±X 是太陽能板。
    /// </summary>
    public static class SatelliteBuilder
    {
        static Mesh _cone, _dish, _cylinder, _cube;

        public static GameObject Build(Design design, GameData data, Evaluation ev, Transform parent)
        {
            var mats = SatMaterials.Load();
            var root = new GameObject("Satellite");
            root.transform.SetParent(parent, false);
            if (mats == null)
            {
                Debug.LogError("[GoNoGo] 找不到 Resources/Presentation/SatMaterials（請執行 GoNoGo/Build/All Scenes）");
                return root;
            }

            double wet = ev?.Mass?.WetKg ?? 100;
            float s = Mathf.Clamp(0.45f + Mathf.Pow((float)wet, 1f / 3f) * 0.09f, 0.55f, 1.8f);
            float h = s * 1.15f;

            string shield = design.Get(PartCategory.Shielding) ?? "";
            BuildBus(root.transform, mats, s, h, shield);
            BuildSolar(root.transform, mats, s, h, data.Part(design.Get(PartCategory.Solar)));
            BuildAntenna(root.transform, mats, s, h, design.Get(PartCategory.Antenna));
            BuildInstruments(root.transform, mats, s, h, design, data);
            BuildAdcs(root.transform, mats, s, h, design.Get(PartCategory.Adcs));
            BuildPropulsion(root.transform, mats, s, h, design.Get(PartCategory.Propulsion));
            BuildComputer(root.transform, mats, s, h, design.Get(PartCategory.Computer));
            BuildBattery(root.transform, mats, s, h, design.Get(PartCategory.Battery));
            return root;
        }

        // ---------------- 本體 ----------------

        static void BuildBus(Transform root, SatMaterials m, float s, float h, string shield)
        {
            var g = Group(root, "bus", "");
            bool heavy = shield == "shield_heavy";
            var skin = heavy ? m.mliSilver : m.mliGold;
            Box(g, Vector3.zero, new Vector3(s, h, s), skin);
            // 上下甲板（白色）與四邊框條
            Box(g, new Vector3(0, h / 2 + 0.01f * s, 0), new Vector3(s * 1.02f, 0.03f * s, s * 1.02f), m.paintWhite);
            Box(g, new Vector3(0, -h / 2 - 0.01f * s, 0), new Vector3(s * 1.02f, 0.03f * s, s * 1.02f), m.paintWhite);
            float e = 0.035f * s;
            foreach (var x in new[] { -1, 1 })
                foreach (var z in new[] { -1, 1 })
                    Box(g, new Vector3(x * s / 2, 0, z * s / 2), new Vector3(e, h * 1.02f, e), m.frameDark);
            // 轉接環（接火箭）
            Cyl(g, new Vector3(0, h / 2 + 0.06f * s, 0), 0.32f * s, 0.08f * s, Vector3.up, m.metal);
            Cyl(g, new Vector3(0, h / 2 + 0.11f * s, 0), 0.36f * s, 0.025f * s, Vector3.up, m.frameDark);

            if (shield == "shield_vault" || heavy)
            {
                var v = Group(root, PartCategory.Shielding, shield);
                // 電子設備「保險箱」：比較厚的鋁屏蔽盒，裝在上甲板
                Box(v, new Vector3(-0.22f * s, h / 2 + 0.1f * s, 0.22f * s), new Vector3(0.32f * s, 0.16f * s, 0.32f * s), m.mliSilver);
                if (heavy)
                    Box(v, new Vector3(0.22f * s, h / 2 + 0.1f * s, -0.22f * s), new Vector3(0.28f * s, 0.16f * s, 0.28f * s), m.mliSilver);
            }
            else Group(root, PartCategory.Shielding, shield); // 標準屏蔽：就是金色隔熱毯本身
        }

        // ---------------- 太陽能板 ----------------

        static void BuildSolar(Transform root, SatMaterials m, float s, float h, PartDef solar)
        {
            var g = Group(root, PartCategory.Solar, solar?.Id ?? "");
            if (solar == null) return;
            if (solar.Id == "solar_body" || solar.AreaM2 < 0.6)
            {
                // 貼在四個側面
                float ph = h * 0.78f, pw = s * 0.86f, off = s / 2 + 0.012f * s;
                Panel(g, new Vector3(off, 0.05f * h, 0), new Vector3(0.01f * s, ph, pw), m.solarCell);
                Panel(g, new Vector3(-off, 0.05f * h, 0), new Vector3(0.01f * s, ph, pw), m.solarCell);
                Panel(g, new Vector3(0, 0.05f * h, off), new Vector3(pw, ph, 0.01f * s), m.solarCell);
                Panel(g, new Vector3(0, 0.05f * h, -off), new Vector3(pw, ph, 0.01f * s), m.solarCell);
                return;
            }
            bool large = solar.Id == "solar_large" || solar.AreaM2 >= 2.5;
            int panels = large ? 4 : 3;
            float pW = s * (large ? 1.0f : 0.85f), pH = s * (large ? 1.25f : 0.95f), gap = 0.03f * s;
            foreach (var side in new[] { -1, 1 })
            {
                // 支架（yoke）
                float x0 = side * (s / 2 + 0.02f * s);
                float yokeLen = 0.35f * s;
                Box(g, new Vector3(x0 + side * yokeLen / 2, 0, 0), new Vector3(yokeLen, 0.04f * s, 0.04f * s), m.metal);
                Cyl(g, new Vector3(x0, 0, 0), 0.06f * s, 0.06f * s, Vector3.right, m.frameDark);
                float start = x0 + side * yokeLen;
                for (int i = 0; i < panels; i++)
                {
                    float cx = start + side * (pW / 2 + i * (pW + gap));
                    Panel(g, new Vector3(cx, 0, 0), new Vector3(pW, 0.012f * s, pH), m.solarCell);
                    // 板框
                    Box(g, new Vector3(cx, -0.008f * s, 0), new Vector3(pW * 1.01f, 0.006f * s, pH * 1.01f), m.frameDark);
                }
            }
        }

        // ---------------- 天線 ----------------

        static void BuildAntenna(Transform root, SatMaterials m, float s, float h, string id)
        {
            var g = Group(root, PartCategory.Antenna, id ?? "");
            switch (id)
            {
                case "ant_xband_hr":
                {
                    // 大型高增益碟：伸出支臂 + 雙軸萬向架
                    var boomEnd = new Vector3(0, -h / 2 - 0.05f * s, -s / 2 - 0.35f * s);
                    Box(g, new Vector3(0, -h / 2 + 0.05f * s, -s / 2 - 0.17f * s), new Vector3(0.05f * s, 0.05f * s, 0.36f * s), m.metal);
                    Cyl(g, boomEnd, 0.06f * s, 0.12f * s, Vector3.up, m.frameDark);
                    Dish(g, boomEnd + new Vector3(0, -0.08f * s, 0), 0.34f * s, 0.12f * s, Quaternion.Euler(150, 0, 0), m.dish, m.metal);
                    break;
                }
                case "ant_xband":
                {
                    var pos = new Vector3(0.22f * s, -h / 2 - 0.08f * s, -0.22f * s);
                    Cyl(g, pos + new Vector3(0, 0.04f * s, 0), 0.03f * s, 0.08f * s, Vector3.up, m.metal);
                    Dish(g, pos, 0.17f * s, 0.06f * s, Quaternion.Euler(180, 0, 0), m.dish, m.metal);
                    break;
                }
                default:
                {
                    // S 頻段貼片天線 + 小鞭狀天線
                    Cyl(g, new Vector3(0.28f * s, -h / 2 - 0.012f * s, -0.28f * s), 0.07f * s, 0.02f * s, Vector3.up, m.paintWhite);
                    Cyl(g, new Vector3(-0.3f * s, h / 2 + 0.12f * s, -0.3f * s), 0.006f * s, 0.25f * s, Vector3.up, m.metal);
                    break;
                }
            }
        }

        // ---------------- 儀器 ----------------

        static readonly Vector2[] Slots = { new Vector2(-0.2f, 0.1f), new Vector2(0.2f, 0.12f), new Vector2(0f, -0.22f) };

        static void BuildInstruments(Transform root, SatMaterials m, float s, float h, Design d, GameData data)
        {
            int slot = 0;
            foreach (var id in d.Instruments)
            {
                var p = data.Part(id);
                var g = Group(root, PartCategory.Instrument, id);
                Color accent = Color.white;
                if (p != null) ColorUtility.TryParseHtmlString(p.Color, out accent);
                var at = Slots[Mathf.Min(slot, Slots.Length - 1)];
                var basePos = new Vector3(at.x * s, -h / 2, at.y * s);
                slot++;
                switch (id)
                {
                    case "inst_sar":
                    {
                        // 合成孔徑雷達：長條平板天線，比本體還長
                        var pos = new Vector3(0, -h / 2 - 0.05f * s, 0.05f * s);
                        Box(g, pos, new Vector3(0.36f * s, 0.035f * s, 1.9f * s), m.paintWhite);
                        for (int i = -3; i <= 3; i++)
                            Box(g, pos + new Vector3(0, -0.02f * s, i * 0.26f * s), new Vector3(0.34f * s, 0.008f * s, 0.2f * s), m.frameDark);
                        AccentRing(g, m, pos + new Vector3(0, 0.03f * s, 0.95f * s), 0.04f * s, accent);
                        break;
                    }
                    case "inst_hyperspectral":
                    {
                        var pos = basePos + new Vector3(0, -0.18f * s, 0);
                        Box(g, pos, new Vector3(0.42f * s, 0.34f * s, 0.36f * s), m.mliSilver);
                        Lens(g, m, pos + new Vector3(-0.09f * s, -0.17f * s, 0), 0.07f * s, accent);
                        Lens(g, m, pos + new Vector3(0.09f * s, -0.17f * s, 0), 0.07f * s, accent);
                        break;
                    }
                    case "inst_co2_spectrometer":
                    {
                        var pos = basePos + new Vector3(0, -0.2f * s, 0);
                        Box(g, pos, new Vector3(0.44f * s, 0.38f * s, 0.4f * s), m.paintWhite);
                        // 遮光罩
                        Cyl(g, pos + new Vector3(0, -0.28f * s, 0), 0.11f * s, 0.2f * s, Vector3.up, m.frameDark);
                        Lens(g, m, pos + new Vector3(0, -0.38f * s, 0), 0.09f * s, accent);
                        break;
                    }
                    case "inst_thermal":
                    {
                        var pos = basePos + new Vector3(0, -0.12f * s, 0);
                        Box(g, pos, new Vector3(0.26f * s, 0.22f * s, 0.26f * s), m.mliSilver);
                        // 散熱片（熱像儀要冷）
                        for (int i = -1; i <= 1; i++)
                            Box(g, pos + new Vector3(0.15f * s, 0, i * 0.07f * s), new Vector3(0.04f * s, 0.2f * s, 0.012f * s), m.paintWhite);
                        Lens(g, m, pos + new Vector3(0, -0.12f * s, 0), 0.06f * s, accent);
                        break;
                    }
                    case "inst_multispectral":
                    {
                        var pos = basePos + new Vector3(0, -0.1f * s, 0);
                        Box(g, pos, new Vector3(0.22f * s, 0.18f * s, 0.3f * s), m.mliGold);
                        Lens(g, m, pos + new Vector3(0, -0.1f * s, 0.06f * s), 0.05f * s, accent);
                        Lens(g, m, pos + new Vector3(0, -0.1f * s, -0.06f * s), 0.05f * s, accent);
                        break;
                    }
                    default:
                    {
                        // 小型可見光相機：鏡筒
                        var pos = basePos + new Vector3(0, -0.07f * s, 0);
                        Cyl(g, pos, 0.07f * s, 0.14f * s, Vector3.up, m.frameDark);
                        Lens(g, m, pos + new Vector3(0, -0.075f * s, 0), 0.055f * s, accent);
                        break;
                    }
                }
            }
        }

        static void Lens(Transform g, SatMaterials m, Vector3 pos, float r, Color accent)
        {
            Cyl(g, pos, r, 0.02f, Vector3.up, m.lens);
            AccentRing(g, m, pos + new Vector3(0, 0.012f, 0), r * 1.25f, accent);
        }

        static void AccentRing(Transform g, SatMaterials m, Vector3 pos, float r, Color accent)
        {
            var ring = Cyl(g, pos, r, 0.012f, Vector3.up, m.accent);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", accent);
            ring.GetComponent<Renderer>().SetPropertyBlock(mpb);
        }

        // ---------------- 姿態控制 / 推進 / 電腦 / 電池 ----------------

        static void BuildAdcs(Transform root, SatMaterials m, float s, float h, string id)
        {
            var g = Group(root, PartCategory.Adcs, id ?? "");
            // 太陽感測器（每種都有）
            Box(g, new Vector3(s / 2 + 0.01f * s, h * 0.42f, s * 0.38f), new Vector3(0.02f * s, 0.05f * s, 0.05f * s), m.lens);
            if (id == "adcs_wheels" || id == "adcs_precision")
            {
                foreach (var z in new[] { -0.2f, 0.2f })
                    Cyl(g, new Vector3(0, h * 0.25f, -s / 2 - 0.03f * s) + new Vector3(z * s, 0, 0), 0.08f * s, 0.06f * s, Vector3.forward, m.metal);
            }
            if (id == "adcs_precision")
            {
                // 兩具星象儀，帶遮光擋板，斜向上
                foreach (var x in new[] { -0.28f, 0.28f })
                {
                    var t = Cyl(g, new Vector3(x * s, h / 2 + 0.09f * s, -0.3f * s), 0.05f * s, 0.14f * s, Vector3.up, m.frameDark).transform;
                    t.localRotation = Quaternion.Euler(-30, x > 0 ? 25 : -25, 0) * t.localRotation;
                }
            }
        }

        static void BuildPropulsion(Transform root, SatMaterials m, float s, float h, string id)
        {
            var g = Group(root, PartCategory.Propulsion, id ?? "");
            if (string.IsNullOrEmpty(id) || id == "prop_none") return;
            var back = new Vector3(0, -0.1f * h, s / 2);
            Cone(g, back + new Vector3(0, 0, 0.08f * s), 0.07f * s, 0.16f * s, Vector3.forward, m.metal);
            if (id == "prop_hydrazine")
            {
                foreach (var x in new[] { -1, 1 })
                    foreach (var y in new[] { -1, 1 })
                        Cone(g, new Vector3(x * 0.4f * s, y * 0.42f * h, s / 2 + 0.04f * s), 0.025f * s, 0.06f * s, Vector3.forward, m.metal);
                // 外露的推進劑管路
                Box(g, back + new Vector3(0, 0.15f * h, 0.012f * s), new Vector3(0.012f * s, 0.3f * h, 0.012f * s), m.metal);
            }
        }

        static void BuildComputer(Transform root, SatMaterials m, float s, float h, string id)
        {
            var g = Group(root, PartCategory.Computer, id ?? "");
            // 電腦的散熱板：等級越高越大
            float size = id == "obc_radhard" ? 0.5f : id == "obc_mass_memory" ? 0.38f : 0.24f;
            Box(g, new Vector3(-s / 2 - 0.012f * s, -0.15f * h, 0), new Vector3(0.008f * s, size * h, size * s), m.paintWhite);
        }

        static void BuildBattery(Transform root, SatMaterials m, float s, float h, string id)
        {
            var g = Group(root, PartCategory.Battery, id ?? "");
            float size = id == "batt_large" ? 0.34f : id == "batt_medium" ? 0.24f : 0.15f;
            Box(g, new Vector3(s / 2 + 0.012f * s, -0.22f * h, -0.18f * s), new Vector3(0.008f * s, size * h, size * s), m.paintWhite);
        }

        // ---------------- 幾何工具 ----------------

        static Transform Group(Transform root, string category, string partId)
        {
            var go = new GameObject(string.IsNullOrEmpty(partId) ? category : partId);
            go.transform.SetParent(root, false);
            var sp = go.AddComponent<SatPart>();
            sp.category = category;
            sp.partId = partId;
            return go.transform;
        }

        static GameObject Prim(Transform parent, string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            return go;
        }

        static GameObject Box(Transform parent, Vector3 center, Vector3 size, Material mat)
        {
            var go = Prim(parent, "box", CubeMesh, mat);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            return go;
        }

        static GameObject Panel(Transform parent, Vector3 center, Vector3 size, Material mat) => Box(parent, center, size, mat);

        static GameObject Cyl(Transform parent, Vector3 center, float radius, float height, Vector3 axis, Material mat)
        {
            var go = Prim(parent, "cyl", CylinderMesh, mat);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            // Unity 圓柱高 2、直徑 1
            go.transform.localScale = new Vector3(radius * 2, height / 2, radius * 2);
            return go;
        }

        static GameObject Cone(Transform parent, Vector3 center, float radius, float height, Vector3 axis, Material mat)
        {
            var go = Prim(parent, "cone", ConeMesh, mat);
            go.transform.localPosition = center;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, axis);
            go.transform.localScale = new Vector3(radius, height, radius);
            return go;
        }

        static void Dish(Transform parent, Vector3 center, float radius, float depth, Quaternion rot, Material mat, Material feedMat)
        {
            var go = Prim(parent, "dish", DishMesh, mat);
            go.transform.localPosition = center;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(radius, depth, radius);
            // 饋源與支架
            var feed = Prim(go.transform, "feed", CylinderMesh, feedMat);
            feed.transform.localPosition = new Vector3(0, 0.9f, 0);
            feed.transform.localScale = new Vector3(0.12f, 0.9f, 0.12f);
        }

        static Mesh CubeMesh => _cube ??= Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        static Mesh CylinderMesh => _cylinder ??= Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

        /// <summary>單位圓錐：底面半徑 1 在 y=0，尖端 y=1（用來當噴嘴，開口朝 -Y 所以翻過來用）。</summary>
        static Mesh ConeMesh => _cone ??= MakeCone(24);

        /// <summary>單位拋物面碟：半徑 1、深度 1（y = r²），凹面朝 +Y，雙面。</summary>
        static Mesh DishMesh => _dish ??= MakeDish(28, 8);

        static Mesh MakeCone(int seg)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2;
                var dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                // 噴嘴：底部大口在 y=0，喉部在 y=1 半徑 0.35（鐘形噴嘴的簡化）
                v.Add(dir); n.Add((dir + Vector3.down * 0.4f).normalized);
                v.Add(dir * 0.35f + Vector3.up); n.Add((dir + Vector3.down * 0.4f).normalized);
            }
            for (int i = 0; i < seg; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                t.AddRange(new[] { a, b, c, b, d, c });
                t.AddRange(new[] { a, c, b, b, c, d }); // 內面
            }
            var mesh = new Mesh { name = "Nozzle" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh MakeDish(int seg, int rings)
        {
            // 正面與背面各一組頂點（共用頂點的話兩面法線會互相抵消，碟子會變黑）
            var front = new List<Vector3> { Vector3.zero };
            for (int r = 1; r <= rings; r++)
            {
                float rr = r / (float)rings;
                for (int i = 0; i < seg; i++)
                {
                    float a = i / (float)seg * Mathf.PI * 2;
                    front.Add(new Vector3(Mathf.Cos(a) * rr, rr * rr, Mathf.Sin(a) * rr));
                }
            }
            var tris = new List<int>();
            for (int i = 0; i < seg; i++) tris.AddRange(new[] { 0, 1 + (i + 1) % seg, 1 + i });
            for (int r = 1; r < rings; r++)
            {
                int o0 = 1 + (r - 1) * seg, o1 = 1 + r * seg;
                for (int i = 0; i < seg; i++)
                {
                    int a = o0 + i, b = o0 + (i + 1) % seg, c = o1 + i, d = o1 + (i + 1) % seg;
                    tris.AddRange(new[] { a, b, c, b, d, c });
                }
            }
            int n = front.Count;
            var verts = new List<Vector3>(front);
            verts.AddRange(front);
            var all = new List<int>(tris);
            for (int i = 0; i < tris.Count; i += 3)
                all.AddRange(new[] { tris[i] + n, tris[i + 2] + n, tris[i + 1] + n });
            var mesh = new Mesh { name = "Dish" };
            mesh.SetVertices(verts);
            mesh.SetTriangles(all, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
