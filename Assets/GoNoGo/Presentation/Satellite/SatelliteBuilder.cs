using System.Linq;
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
        static float _skin = 0.012f;

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
            // 貼身太陽能板蓋住四個側面時，側面的小板（散熱板、電池板、感測器）要再往外一點，不然兩者在同一個平面上會閃爍破圖
            var solarDef = data.Part(design.Get(PartCategory.Solar));
            _skin = solarDef != null && (solarDef.Id == "solar_body" || solarDef.AreaM2 < 0.6) ? 0.03f : 0.012f;
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

        /// <summary>
        /// 單一零件的模型（組裝區零件架上、玩家手上拿的那個）：用只裝這個零件的設計組一艘船，
        /// 取出這個零件那一組，縮放到 <paramref name="size"/> 公尺、中心對齊 parent。沒有外型的零件（例如不裝推進）用一個小箱子代替。
        /// </summary>
        public static GameObject BuildPart(string partId, GameData data, Transform parent, float size)
        {
            var part = data.Part(partId);
            var root = new GameObject("Part_" + partId);
            root.transform.SetParent(parent, false);
            var mats = SatMaterials.Load();
            if (part == null || mats == null) return root;

            var d = new Design();
            if (part.Category == PartCategory.Instrument) d.Instruments.Add(partId);
            else d.Parts[part.Category] = partId;
            var tmp = Build(d, data, null, null);
            Transform group = null;
            foreach (var sp in tmp.GetComponentsInChildren<SatPart>())
                if (sp.partId == partId && sp.category == part.Category && sp.GetComponentsInChildren<Renderer>().Length > 0) { group = sp.transform; break; }
            // 屏蔽就是本體外殼
            if (group == null && part.Category == PartCategory.Shielding)
                foreach (var sp in tmp.GetComponentsInChildren<SatPart>())
                    if (sp.category == PartCategory.Shielding) { group = sp.transform; break; }

            if (group != null)
            {
                group.SetParent(root.transform, false);
                group.localPosition = Vector3.zero;
                group.localRotation = Quaternion.identity;
            }
            else Box(root.transform, Vector3.zero, new Vector3(0.6f, 0.08f, 0.6f), mats.paintWhite);
            UnityEngine.Object.DestroyImmediate(tmp);

            // 縮放、置中
            var rs = root.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                float longest = Mathf.Max(b.size.x, b.size.y, b.size.z, 0.01f);
                float k = size / longest;
                var inner = new GameObject("Model").transform;
                inner.SetParent(root.transform, false);
                foreach (Transform c in root.transform.Cast<Transform>().ToArray())
                    if (c != inner) c.SetParent(inner, true);
                inner.localScale = Vector3.one * k;
                b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                inner.position += root.transform.position - b.center;
            }
            return root;
        }

        /// <summary>
        /// 單人的籃子：一個開口朝上的淺箱子，裡面最多放四個小零件（2 × 2）。最長邊是 <paramref name="size"/> 公尺，
        /// 底部在 parent 的原點。回傳的物件底下有名叫 Slot0…Slot3 的四個位置。
        /// </summary>
        public static GameObject BuildBasket(Transform parent, float size)
        {
            var root = new GameObject("Basket");
            root.transform.SetParent(parent, false);
            var mats = SatMaterials.Load();
            if (mats == null) return root;
            root.AddComponent<SatPart>();
            var t = root.transform;
            float w = size, d = size * 0.72f, h = size * 0.22f, wall = size * 0.03f;
            Box(t, new Vector3(0, wall / 2, 0), new Vector3(w, wall, d), mats.frameDark);
            foreach (var z in new[] { -1, 1 })
                Box(t, new Vector3(0, h / 2, z * (d / 2 - wall / 2)), new Vector3(w, h, wall), mats.paintWhite);
            foreach (var x in new[] { -1, 1 })
                Box(t, new Vector3(x * (w / 2 - wall / 2), h / 2, 0), new Vector3(wall, h, d), mats.paintWhite);
            // 兩邊的提把
            foreach (var x in new[] { -1, 1 })
                Box(t, new Vector3(x * (w / 2 + wall * 0.6f), h * 0.8f, 0), new Vector3(wall * 0.8f, wall * 0.8f, d * 0.4f), mats.metal);
            for (int i = 0; i < 4; i++)
            {
                var slot = new GameObject("Slot" + i).transform;
                slot.SetParent(t, false);
                slot.localPosition = new Vector3((i % 2 == 0 ? -1 : 1) * w * 0.23f, wall, (i < 2 ? -1 : 1) * d * 0.22f);
            }
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

            if (shield == "shield_vault" || shield == "shield_lead" || heavy)
            {
                var v = Group(root, PartCategory.Shielding, shield);
                // 電子設備「保險箱」：比較厚的鋁屏蔽盒，裝在上甲板
                Box(v, new Vector3(-0.22f * s, h / 2 + 0.1f * s, 0.22f * s), new Vector3(0.32f * s, 0.16f * s, 0.32f * s), m.mliSilver);
                if (heavy)
                    Box(v, new Vector3(0.22f * s, h / 2 + 0.1f * s, -0.22f * s), new Vector3(0.28f * s, 0.16f * s, 0.28f * s), m.mliSilver);
            }
            else
            {
                // 標準外殼：一個薄的鋁製電子設備盒，裝在上甲板同一個角落。
                // 以前沒有模型（「就是隔熱毯本身」），準星對不到，裝上去就拆不下來，零件架上也看不到
                var v = Group(root, PartCategory.Shielding, shield);
                if (!string.IsNullOrEmpty(shield))
                {
                    Box(v, new Vector3(-0.22f * s, h / 2 + 0.06f * s, 0.22f * s), new Vector3(0.28f * s, 0.08f * s, 0.28f * s), m.metal);
                    Box(v, new Vector3(-0.22f * s, h / 2 + 0.105f * s, 0.22f * s), new Vector3(0.22f * s, 0.012f * s, 0.22f * s), m.frameDark);
                }
            }
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
            if (string.IsNullOrEmpty(id)) return; // 天線拆掉了：不要還畫著一支拆不下來的預設天線
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

        /// <summary>朝天儀器在頂面的位置（角落，避開中間的火箭轉接環、電子艙屏蔽盒與星象儀）。</summary>
        static readonly Vector2[] TopSlots = { new Vector2(0.34f, 0.34f), new Vector2(-0.34f, -0.34f), new Vector2(0.36f, 0f) };

        /// <summary>
        /// 酬載甲板一層放三台儀器。沙盒可以裝到六台：第四台起放到下面（朝天的放到上面）再多掛的一層酬載板，
        /// 每台都有自己的位置，不會疊在一起（疊在一起就對不到、拆不下來）。
        /// </summary>
        const float DeckGap = 0.62f;

        /// <summary>多掛一層酬載板：一片板子加四根支柱（算船身結構，拆不下來）。</summary>
        static void ExtraDeck(Transform root, SatMaterials m, float s, float y, float fromY)
        {
            var g = Group(root, "bus", "");
            Box(g, new Vector3(0, y, 0), new Vector3(0.92f * s, 0.03f * s, 0.92f * s), m.paintWhite);
            float mid = (y + fromY) / 2, len = Mathf.Abs(y - fromY);
            foreach (var x in new[] { -1, 1 })
                foreach (var z in new[] { -1, 1 })
                    Box(g, new Vector3(x * 0.43f * s, mid, z * 0.43f * s), new Vector3(0.03f * s, len, 0.03f * s), m.metal);
        }

        static void BuildInstruments(Transform root, SatMaterials m, float s, float h, Design d, GameData data)
        {
            int slot = 0, top = 0;
            int decksDown = 0, decksUp = 0;
            foreach (var id in d.Instruments)
            {
                var p = data.Part(id);
                var g = Group(root, PartCategory.Instrument, id);
                Color accent = Color.white;
                if (p != null) ColorUtility.TryParseHtmlString(p.Color, out accent);

                // Blender 做的模型（tools/parts/build_instruments.py）：有就用，沒有就用下面的程式外形
                var model = p != null ? PartModel(p.Id) : null;
                if (model != null)
                {
                    bool up = p.Mount == "zenith" || p.Mount == "boom";
                    int tier = (up ? top : slot) / Slots.Length;
                    var tp = TopSlots[top % TopSlots.Length];
                    var sp = Slots[slot % Slots.Length];
                    float yUp = h / 2 + 0.015f * s + tier * DeckGap * s, yDown = -h / 2 - tier * DeckGap * s;
                    if (up && tier > decksUp) { decksUp = tier; ExtraDeck(root, m, s, yUp - 0.015f * s, h / 2); }
                    if (!up && tier > decksDown) { decksDown = tier; ExtraDeck(root, m, s, yDown + 0.015f * s, -h / 2); }
                    var pos = up ? new Vector3(tp.x * s, yUp, tp.y * s) : new Vector3(sp.x * s, yDown, sp.y * s);
                    if (up) top++; else slot++;
                    PlaceModel(g, model, m, pos, s, accent);
                    continue;
                }

                var at = Slots[slot % Slots.Length];
                int deck = slot / Slots.Length;
                // 這台儀器裝在哪一層的底面（第一層就是船身底面）
                float bottom = -h / 2 - deck * DeckGap * s;
                if (deck > decksDown) { decksDown = deck; ExtraDeck(root, m, s, bottom + 0.015f * s, -h / 2); }
                var basePos = new Vector3(at.x * s, bottom, at.y * s);
                slot++;
                switch (id)
                {
                    case "inst_sar":
                    {
                        // 合成孔徑雷達：長條平板天線，比本體還長
                        var pos = new Vector3(0, bottom - 0.05f * s, 0.05f * s);
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
                    case "inst_fire_mwir":
                    {
                        // 火點儀：沿用熱像儀的造型（散熱片），換成橘色鏡頭
                        var pos = basePos + new Vector3(0, -0.09f * s, 0);
                        const float w = 0.2f;
                        Box(g, pos, new Vector3(w * s, 0.16f * s, w * s), m.mliGold);
                        for (int i = -1; i <= 1; i++)
                            Box(g, pos + new Vector3((w / 2 + 0.02f) * s, 0, i * 0.06f * s), new Vector3(0.035f * s, 0.14f * s, 0.012f * s), m.paintWhite);
                        Lens(g, m, pos + new Vector3(0, -0.09f * s, 0), 0.05f * s, accent);
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

        // ---------------- Blender 模型 ----------------

        static readonly Dictionary<string, GameObject> _partModels = new Dictionary<string, GameObject>();

        /// <summary>Resources/Presentation/Parts/&lt;零件 id&gt;（GLB）；沒有就回傳 null。</summary>
        static GameObject PartModel(string partId)
        {
            if (!_partModels.TryGetValue(partId, out var go))
            {
                go = Resources.Load<GameObject>("Presentation/Parts/" + partId);
                _partModels[partId] = go;
            }
            return go;
        }

        /// <summary>
        /// 放一個 Blender 模型：單位是本體邊長（乘上 s），原點是安裝面。
        /// glTFast 匯入時 Blender +Y 變成 Unity -Z、X 翻面，轉 180° 讓 Blender 的 +Y 對到 Unity 的 +Z（和控制室模型一樣）。
        /// 材質依名稱換成 SatMaterials 的同名材質；accent 染成零件顏色。
        /// </summary>
        static void PlaceModel(Transform g, GameObject prefab, SatMaterials m, Vector3 pos, float s, Color accent)
        {
            var go = UnityEngine.Object.Instantiate(prefab, g, false);
            go.name = "model";
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0, 180, 0);
            go.transform.localScale = Vector3.one * s;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool isAccent = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i] != null ? mats[i].name : "";
                    var sat = SatMaterial(m, n);
                    if (sat != null) mats[i] = sat;
                    if (n.StartsWith("accent")) isAccent = true;
                }
                r.sharedMaterials = mats;
                if (isAccent)
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetColor("_BaseColor", accent);
                    r.SetPropertyBlock(mpb);
                }
            }
        }

        static Material SatMaterial(SatMaterials m, string name)
        {
            // Blender 重複名稱會加 .001，比對開頭就好
            if (name.StartsWith("mliGold")) return m.mliGold;
            if (name.StartsWith("mliSilver")) return m.mliSilver;
            if (name.StartsWith("paintWhite")) return m.paintWhite;
            if (name.StartsWith("frameDark")) return m.frameDark;
            if (name.StartsWith("solarCell")) return m.solarCell;
            if (name.StartsWith("dish")) return m.dish;
            if (name.StartsWith("lens")) return m.lens;
            if (name.StartsWith("metal")) return m.metal;
            if (name.StartsWith("accent")) return m.accent;
            return null;
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
            if (string.IsNullOrEmpty(id)) return; // 沒裝姿態控制：什麼都不畫
            // 太陽感測器（每種都有）
            Box(g, new Vector3(s / 2 + (_skin + 0.008f) * s, h * 0.42f, s * 0.38f), new Vector3(0.03f * s, 0.07f * s, 0.07f * s), m.lens);
            if (id != "adcs_wheels" && id != "adcs_precision" && id != "adcs_cmg")
            {
                // 整合式模組：一個小盒子（三顆小反應輪在裡面）加一支星象儀鏡筒，裝在背面。
                // 以前只畫了太陽感測器那一小塊，幾乎看不到，也對不到
                var c = new Vector3(0, h * 0.25f, -s / 2 - (_skin + 0.035f) * s);
                Box(g, c, new Vector3(0.24f * s, 0.16f * s, 0.07f * s), m.frameDark);
                Cyl(g, c + new Vector3(0.06f * s, 0, -0.06f * s), 0.03f * s, 0.06f * s, Vector3.forward, m.metal);
                Box(g, c + new Vector3(-0.06f * s, 0, -0.037f * s), new Vector3(0.06f * s, 0.06f * s, 0.006f * s), m.lens);
            }
            if (id == "adcs_wheels" || id == "adcs_precision" || id == "adcs_cmg")
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
            if (string.IsNullOrEmpty(id)) return;
            var back = new Vector3(0, -0.1f * h, s / 2);
            if (id == "prop_none")
            {
                // 不裝推進：推進器的位置蓋一片封板（四顆螺絲）。有模型才對得到、拆得下來
                var plate = back + new Vector3(0, 0, (_skin + 0.006f) * s);
                Box(g, plate, new Vector3(0.22f * s, 0.22f * s, 0.012f * s), m.paintWhite);
                foreach (var x in new[] { -1, 1 })
                    foreach (var y in new[] { -1, 1 })
                        Cyl(g, plate + new Vector3(x * 0.08f * s, y * 0.08f * s, 0.008f * s), 0.012f * s, 0.008f * s, Vector3.forward, m.metal);
                return;
            }
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
            if (string.IsNullOrEmpty(id)) return;
            // 電腦：側面的一個電子盒，外面是白色散熱板；等級越高越大。
            // 盒子有厚度（以前只是一片 8 毫米的薄板，從旁邊幾乎看不到，也很難對準）
            float size = id == "obc_radhard" ? 0.5f : id == "obc_mass_memory" ? 0.38f : 0.24f;
            float tall = Mathf.Min(size, 0.34f) * h, depth = 0.06f * s;
            float x = -s / 2 - _skin * s - depth / 2;
            Box(g, new Vector3(x, -0.26f * h, 0), new Vector3(depth, tall, size * s), m.frameDark);
            Box(g, new Vector3(x - depth / 2 - 0.004f * s, -0.26f * h, 0), new Vector3(0.008f * s, tall * 0.9f, size * s * 0.9f), m.paintWhite);
        }

        static void BuildBattery(Transform root, SatMaterials m, float s, float h, string id)
        {
            var g = Group(root, PartCategory.Battery, id ?? "");
            if (string.IsNullOrEmpty(id)) return;
            // 電池：側面的一個電池盒（有厚度），正面兩條電極匯流排
            float size = id == "batt_large" ? 0.34f : id == "batt_medium" ? 0.24f : id == "batt_cubesat" ? 0.11f : 0.15f;
            float depth = 0.07f * s;
            float x = s / 2 + _skin * s + depth / 2;
            var c = new Vector3(x, -0.26f * h, -0.18f * s);
            Box(g, c, new Vector3(depth, size * h, size * s), m.paintWhite);
            foreach (var z in new[] { -1, 1 })
                Box(g, c + new Vector3(depth / 2 + 0.003f * s, 0, z * size * s * 0.25f), new Vector3(0.006f * s, size * h * 0.8f, 0.02f * s), m.metal);
        }

        // ---------------- 小零件（架子上拿、在組裝台組起來） ----------------

        /// <summary>小零件的外型：形狀種類 + 顏色。沒列到的用一個灰色盒子。</summary>
        static readonly Dictionary<string, (string shape, string color)> CompLooks = new Dictionary<string, (string, string)>
        {
            ["optics"] = ("lens", "#7FE3FF"), ["beam_telescope"] = ("lens", "#E8C15A"),
            ["detector"] = ("chip", "#3B6BD6"), ["readout"] = ("board", "#2E9E5B"), ["bms"] = ("board", "#D6453B"),
            ["cpu"] = ("board", "#2E9E5B"), ["memory"] = ("board", "#3B6BD6"), ["power_board"] = ("board", "#E08A2E"),
            ["control"] = ("board", "#8A5BD6"),
            ["baffle"] = ("tube", "#2A2E36"), ["star_tracker"] = ("tube", "#E9EDF2"), ["laser"] = ("tube", "#E0453B"),
            ["neutralizer"] = ("tube", "#9AA3AE"),
            ["cryocooler"] = ("cooler", "#C9D2DC"), ["catalyst"] = ("cooler", "#B88A4A"), ["ionizer"] = ("cooler", "#5B8FE0"),
            ["radar_antenna"] = ("plate", "#E9EDF2"), ["substrate"] = ("plate", "#E9EDF2"), ["plates"] = ("plate", "#AEB6C0"),
            ["spot_shield"] = ("plate", "#E8C15A"), ["heater"] = ("plate", "#E0703B"), ["radiator"] = ("plate", "#E9EDF2"),
            ["cover_glass"] = ("glass", "#BFE9FF"), ["solar_cells"] = ("solar", "#274B9E"),
            ["transmitter"] = ("box", "#8E98A4"), ["receiver"] = ("box", "#4E7FC2"), ["diplexer"] = ("box", "#C9D2DC"),
            ["sensor_head"] = ("box", "#E8C15A"), ["housing"] = ("shell", "#3A404A"), ["pointing"] = ("gimbal", "#C9D2DC"),
            ["boom"] = ("rod", "#C9D2DC"), ["feed"] = ("rod", "#C98A3B"), ["harness"] = ("rod", "#E0703B"),
            ["torquer"] = ("rod", "#B8743B"), ["hinge"] = ("hinge", "#AEB6C0"), ["valve"] = ("valve", "#D6453B"),
            ["wheels"] = ("wheel", "#AEB6C0"), ["grids"] = ("wheel", "#4A515C"),
            ["cells"] = ("cells", "#3BB36B"), ["tank"] = ("tank", "#E9EDF2"), ["nozzle"] = ("nozzle", "#8E98A4"),
            ["fasteners"] = ("bolts", "#AEB6C0"),
            // 酬載儀器各自的配方多出來的小零件
            ["filter_wheel"] = ("wheel", "#C86BE0"), ["disperser"] = ("nozzle", "#B58CFF"), ["grating"] = ("plate", "#E0C23B"),
            ["band_filter"] = ("glass", "#FFB36B"), ["event_processor"] = ("board", "#D6B03B"),
            ["pulse_laser"] = ("tube", "#FF5A3C"), ["timer"] = ("chip", "#E08A2E"),
        };

        /// <summary>
        /// 一個小零件（架子上、拿在手上、組裝台上的）：依 id 用幾種基本形狀拼出來，顏色也不同，
        /// 遠遠看得出是板子、鏡頭、桶子還是桿子。最長邊大約 <paramref name="size"/> 公尺，底部在 parent 的原點。
        /// </summary>
        public static GameObject BuildComponent(string compId, Transform parent, float size)
        {
            var root = new GameObject("Comp_" + compId);
            root.transform.SetParent(parent, false);
            var m = SatMaterials.Load();
            if (m == null) return root;
            root.AddComponent<SatPart>();
            var t = root.transform;
            if (!CompLooks.TryGetValue(compId ?? "", out var look)) look = ("box", "#8E98A4");
            ColorUtility.TryParseHtmlString(look.color, out var col);
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", col);
            GameObject Tint(GameObject go) { go.GetComponent<Renderer>().SetPropertyBlock(mpb); return go; }
            float s = size;
            switch (look.shape)
            {
                case "board":
                    Tint(Box(t, new Vector3(0, 0.03f * s, 0), new Vector3(s, 0.06f * s, 0.7f * s), m.accent));
                    Box(t, new Vector3(-0.15f * s, 0.09f * s, 0.05f * s), new Vector3(0.3f * s, 0.06f * s, 0.3f * s), m.frameDark);
                    Box(t, new Vector3(0.28f * s, 0.085f * s, -0.15f * s), new Vector3(0.18f * s, 0.05f * s, 0.14f * s), m.frameDark);
                    Box(t, new Vector3(0.3f * s, 0.09f * s, 0.2f * s), new Vector3(0.24f * s, 0.07f * s, 0.1f * s), m.metal);
                    break;
                case "chip":
                    Box(t, new Vector3(0, 0.04f * s, 0), new Vector3(0.8f * s, 0.08f * s, 0.8f * s), m.frameDark);
                    Tint(Box(t, new Vector3(0, 0.09f * s, 0), new Vector3(0.5f * s, 0.03f * s, 0.5f * s), m.accent));
                    foreach (var x in new[] { -1, 1 })
                        Box(t, new Vector3(x * 0.42f * s, 0.03f * s, 0), new Vector3(0.06f * s, 0.03f * s, 0.6f * s), m.metal);
                    break;
                case "lens":
                    Cyl(t, new Vector3(0, 0.3f * s, 0), 0.3f * s, 0.6f * s, Vector3.up, m.frameDark);
                    Tint(Cyl(t, new Vector3(0, 0.62f * s, 0), 0.32f * s, 0.06f * s, Vector3.up, m.accent));
                    Cyl(t, new Vector3(0, 0.655f * s, 0), 0.25f * s, 0.02f * s, Vector3.up, m.lens);
                    break;
                case "tube":
                    Tint(Cyl(t, new Vector3(0, 0.4f * s, 0), 0.2f * s, 0.8f * s, Vector3.up, m.accent));
                    Cyl(t, new Vector3(0, 0.81f * s, 0), 0.16f * s, 0.02f * s, Vector3.up, m.frameDark);
                    Box(t, new Vector3(0, 0.05f * s, 0), new Vector3(0.5f * s, 0.1f * s, 0.5f * s), m.metal);
                    break;
                case "cooler":
                    Tint(Cyl(t, new Vector3(0, 0.35f * s, 0), 0.22f * s, 0.7f * s, Vector3.up, m.accent));
                    for (int i = 0; i < 4; i++)
                        Cyl(t, new Vector3(0, (0.2f + i * 0.14f) * s, 0), 0.3f * s, 0.03f * s, Vector3.up, m.metal);
                    break;
                case "plate":
                    Tint(Box(t, new Vector3(0, 0.035f * s, 0), new Vector3(s, 0.07f * s, 0.7f * s), m.accent));
                    Box(t, new Vector3(0, 0.03f * s, 0), new Vector3(1.04f * s, 0.03f * s, 0.74f * s), m.metal);
                    break;
                case "glass":
                    Box(t, new Vector3(0, 0.02f * s, 0), new Vector3(1.04f * s, 0.03f * s, 0.74f * s), m.metal);
                    Box(t, new Vector3(0, 0.045f * s, 0), new Vector3(s, 0.03f * s, 0.7f * s), m.lens);
                    break;
                case "solar":
                    Box(t, new Vector3(0, 0.02f * s, 0), new Vector3(1.04f * s, 0.03f * s, 0.74f * s), m.metal);
                    for (int i = 0; i < 3; i++)
                        Box(t, new Vector3((i - 1) * 0.33f * s, 0.045f * s, 0), new Vector3(0.3f * s, 0.03f * s, 0.68f * s), m.solarCell);
                    break;
                case "shell":
                    Tint(Box(t, new Vector3(0, 0.25f * s, 0), new Vector3(0.9f * s, 0.5f * s, 0.7f * s), m.accent));
                    Box(t, new Vector3(0, 0.51f * s, 0), new Vector3(0.7f * s, 0.02f * s, 0.5f * s), m.frameDark);
                    foreach (var x in new[] { -1, 1 })
                        foreach (var z in new[] { -1, 1 })
                            Cyl(t, new Vector3(x * 0.4f * s, 0.52f * s, z * 0.3f * s), 0.035f * s, 0.03f * s, Vector3.up, m.metal);
                    break;
                case "gimbal":
                    Box(t, new Vector3(0, 0.08f * s, 0), new Vector3(0.6f * s, 0.16f * s, 0.6f * s), m.frameDark);
                    Tint(Cyl(t, new Vector3(0, 0.3f * s, 0), 0.24f * s, 0.2f * s, Vector3.up, m.accent));
                    Cyl(t, new Vector3(0, 0.55f * s, 0), 0.1f * s, 0.34f * s, new Vector3(0.5f, 1, 0).normalized, m.lens);
                    break;
                case "rod":
                    Tint(Cyl(t, new Vector3(0, 0.07f * s, 0), 0.06f * s, s, Vector3.right, m.accent));
                    foreach (var x in new[] { -1, 1 })
                        Cyl(t, new Vector3(x * 0.48f * s, 0.07f * s, 0), 0.09f * s, 0.08f * s, Vector3.right, m.metal);
                    break;
                case "hinge":
                    foreach (var x in new[] { -1, 1 })
                        Tint(Box(t, new Vector3(x * 0.28f * s, 0.04f * s, 0), new Vector3(0.44f * s, 0.06f * s, 0.6f * s), m.accent));
                    Cyl(t, new Vector3(0, 0.08f * s, 0), 0.07f * s, 0.7f * s, Vector3.forward, m.metal);
                    break;
                case "valve":
                    Cyl(t, new Vector3(0, 0.12f * s, 0), 0.12f * s, 0.9f * s, Vector3.right, m.metal);
                    Cyl(t, new Vector3(0, 0.3f * s, 0), 0.08f * s, 0.3f * s, Vector3.up, m.metal);
                    Tint(Cyl(t, new Vector3(0, 0.47f * s, 0), 0.26f * s, 0.05f * s, Vector3.up, m.accent));
                    break;
                case "wheel":
                    Tint(Cyl(t, new Vector3(0, 0.1f * s, 0), 0.45f * s, 0.2f * s, Vector3.up, m.accent));
                    Cyl(t, new Vector3(0, 0.15f * s, 0), 0.14f * s, 0.3f * s, Vector3.up, m.frameDark);
                    break;
                case "cells":
                    for (int i = 0; i < 3; i++)
                    {
                        Tint(Cyl(t, new Vector3((i - 1) * 0.3f * s, 0.35f * s, 0), 0.14f * s, 0.7f * s, Vector3.up, m.accent));
                        Cyl(t, new Vector3((i - 1) * 0.3f * s, 0.72f * s, 0), 0.06f * s, 0.05f * s, Vector3.up, m.metal);
                    }
                    break;
                case "tank":
                    Tint(Cyl(t, new Vector3(0, 0.4f * s, 0), 0.28f * s, 0.6f * s, Vector3.up, m.accent));
                    Cyl(t, new Vector3(0, 0.08f * s, 0), 0.2f * s, 0.1f * s, Vector3.up, m.metal);
                    Cyl(t, new Vector3(0, 0.74f * s, 0), 0.2f * s, 0.1f * s, Vector3.up, m.metal);
                    Cyl(t, new Vector3(0, 0.84f * s, 0), 0.06f * s, 0.12f * s, Vector3.up, m.metal);
                    break;
                case "nozzle":
                    // 圓錐底面在下：開口朝下放在盤子上，上面接一小段喉部
                    Tint(Cone(t, Vector3.zero, 0.4f * s, 0.7f * s, Vector3.up, m.accent));
                    Cyl(t, new Vector3(0, 0.74f * s, 0), 0.12f * s, 0.2f * s, Vector3.up, m.metal);
                    break;
                case "bolts":
                    for (int i = 0; i < 4; i++)
                    {
                        var c = new Vector3((i % 2 == 0 ? -1 : 1) * 0.22f * s, 0, (i < 2 ? -1 : 1) * 0.22f * s);
                        Tint(Cyl(t, c + new Vector3(0, 0.2f * s, 0), 0.06f * s, 0.4f * s, Vector3.up, m.accent));
                        Cyl(t, c + new Vector3(0, 0.42f * s, 0), 0.12f * s, 0.06f * s, Vector3.up, m.metal);
                    }
                    break;
                default:
                    Tint(Box(t, new Vector3(0, 0.2f * s, 0), new Vector3(0.8f * s, 0.4f * s, 0.6f * s), m.accent));
                    Box(t, new Vector3(0.2f * s, 0.2f * s, -0.31f * s), new Vector3(0.2f * s, 0.14f * s, 0.04f * s), m.metal);
                    Box(t, new Vector3(-0.2f * s, 0.2f * s, -0.31f * s), new Vector3(0.2f * s, 0.14f * s, 0.04f * s), m.metal);
                    Box(t, new Vector3(0, 0.41f * s, 0), new Vector3(0.6f * s, 0.02f * s, 0.4f * s), m.frameDark);
                    break;
            }
            return root;
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
            // 照網格實際大小縮放：Resources.GetBuiltinResource 拿到的 Cylinder.fbx 直徑是 2、高 2（不是 CreatePrimitive 的直徑 1）
            go.transform.localScale = CylScale(radius * 2, height);
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
            feed.transform.localScale = CylScale(0.12f, 1.8f);
        }

        static Mesh CubeMesh => _cube ??= Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        static Mesh CylinderMesh => _cylinder ??= Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");

        /// <summary>讓內建圓柱變成指定的直徑與高度（公尺）。</summary>
        static Vector3 CylScale(float diameter, float height)
        {
            var m = CylinderMesh.bounds.size;
            return new Vector3(diameter / m.x, height / m.y, diameter / m.z);
        }

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
