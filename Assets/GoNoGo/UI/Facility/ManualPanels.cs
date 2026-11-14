using System;
using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // ======================================================================== 工程手冊
    //
    // 手冊拆成八章，放在中控室後排的八個席位（Screen_manual-<章>）：
    //   北座（面向影像牆時在左）：1 酬載、2 電力、3 通訊、4 航電 —— 對應組裝區的四座零件架
    //   南座（在右）：5 設計規則、6 異常判讀、7 處置程序、8 太空天氣
    // 前排設計組的「manual」席是目錄：每章在哪一席、零件代號在第幾章。

    /// <summary>後排一席 = 手冊的一章。chapter 為 null 時（組裝區、不嚴格分工）左邊有章節導覽，可以翻全部。</summary>
    public sealed class ManualPanel : FacPanel
    {
        readonly string _fixed;
        static string _browse = ManualView.Chapters[0];

        public ManualPanel(string chapter = null) { _fixed = chapter; }

        public override string Title => _fixed != null ? Loc.T("ui.manual.ch." + _fixed) : Loc.T("ui.manual.title");
        public override string WideClass => "modal--manual";

        protected override void Build(VisualElement body)
        {
            if (_fixed != null)
            {
                ManualView.Header(body, _fixed);
                ManualView.Build(Scroll(body, "man-scroll"), _fixed);
                return;
            }
            var wrap = Ui.Row("man-wrap").AddTo(body);
            var nav = Ui.El("man-nav").AddTo(wrap);
            string group = null;
            foreach (var ch in ManualView.Chapters)
            {
                string g = ManualView.IsParts(ch) ? "ui.manual.group.parts" : "ui.manual.group.ops";
                if (g != group) { Ui.Text(Loc.T(g), "eyebrow", "man-nav-group").AddTo(nav); group = g; }
                var c = ch;
                var b = Ui.Btn("", () => { _browse = c; Sfx.Click(); Refresh(); }, "man-nav-item").Cls("man-nav-item--on", ch == _browse).AddTo(nav);
                b.Clear();
                Ui.Text(ManualView.Number(ch).ToString(), "man-nav-num", "mono").AddTo(b);
                Ui.Text(Loc.T("ui.manual.name." + ch), "man-nav-name").AddTo(b);
            }
            var right = Ui.El("man-main").AddTo(wrap);
            ManualView.Header(right, _browse, false);
            ManualView.Build(Scroll(right, "man-scroll"), _browse);
        }
    }

    /// <summary>前排的手冊席：目錄（每章在後排哪一席）與零件代號速查。</summary>
    public sealed class ManualIndexPanel : FacPanel
    {
        public override string Title => Loc.T("ui.fac.manual_index");
        public override string WideClass => "modal--manual";

        protected override void Build(VisualElement body)
        {
            Ui.Text(Loc.T("ui.manual.index_intro"), "body", "man-intro").AddTo(body);
            var sv = Scroll(body, "man-scroll");
            var cols = Ui.Row("man-index").AddTo(sv);
            foreach (var side in new[] { "N", "S" })
            {
                var col = Ui.El("man-index-col").AddTo(cols);
                Ui.Text(Loc.T("ui.manual.side." + side), "eyebrow").AddTo(col);
                foreach (var ch in ManualView.Chapters.Where(c => ManualView.Side(c) == side))
                {
                    var card = Ui.Row("man-index-card").AddTo(col);
                    Ui.Text(ManualView.Number(ch).ToString(), "man-num", "mono").AddTo(card);
                    var t = Ui.El("man-index-text").AddTo(card);
                    Ui.Text(Loc.T("ui.manual.name." + ch), "h3").AddTo(t);
                    Ui.Text(Loc.T("ui.manual.ch." + ch + "_body"), "small", "dim").AddTo(t);
                    Ui.Spacer().AddTo(card);
                    Ui.Text(Loc.T("ui.manual.seat", ("n", ManualView.Seat(ch))), "small", "faint", "man-index-seat").AddTo(card);
                }
            }

            // 零件代號速查：組裝區講代號，中控室查是哪一章
            Ui.Text(Loc.T("ui.manual.lookup"), "eyebrow", "man-sec").AddTo(sv);
            Ui.Text(Loc.T("ui.manual.lookup_hint"), "small", "dim").AddTo(sv);
            var grid = Ui.El("man-lookup").AddTo(sv);
            var parts = SessionRules.ShelfParts(D, M.S);
            foreach (var ch in ManualView.Chapters.Where(ManualView.IsParts))
                foreach (var cat in ManualView.CategoriesOf(ch))
                    foreach (var p in parts.Where(x => x.Category == cat))
                    {
                        var r = Ui.Row("man-lookup-row").AddTo(grid);
                        Ui.Text(p.Code, "man-code", "mono").AddTo(r);
                        Ui.Text(Loc.T(p.Name), "man-lookup-name").AddTo(r);
                        Ui.Spacer().AddTo(r);
                        Ui.Text(Loc.T("ui.manual.see", ("n", ManualView.Number(ch))), "small", "accent").AddTo(r);
                    }
        }
    }

    /// <summary>手冊各章的內容（後排面板、組裝區手冊、個人模式的平板共用）。</summary>
    public static class ManualView
    {
        public static readonly string[] Chapters = { "payload", "power", "comms", "avionics", "rules", "diagnose", "procedures", "donki" };

        public static int Number(string ch) => Array.IndexOf(Chapters, ch) + 1;
        public static bool IsParts(string ch) => Number(ch) is >= 1 and <= 4;
        public static string Side(string ch) => Number(ch) <= 4 ? "N" : "S";
        /// <summary>面向影像牆，左起第幾席。</summary>
        public static int Seat(string ch) => (Number(ch) - 1) % 4 + 1;

        public static string[] CategoriesOf(string ch) => ch switch
        {
            "payload" => new[] { PartCategory.Instrument },
            "power" => new[] { PartCategory.Solar, PartCategory.Battery },
            "comms" => new[] { PartCategory.Antenna },
            "avionics" => new[] { PartCategory.Computer, PartCategory.Adcs, PartCategory.Propulsion, PartCategory.Shielding },
            _ => new string[0],
        };

        /// <summary>章節的標題列：大章號、章名、在哪一席，下面一段閱讀說明。</summary>
        public static void Header(VisualElement parent, string ch, bool showSeat = true)
        {
            var head = Ui.Row("man-head").AddTo(parent);
            Ui.Text(Number(ch).ToString(), "man-num", "man-num--big", "mono").AddTo(head);
            var col = Ui.El("man-head-text").AddTo(head);
            Ui.Text(Loc.T("ui.manual.chapter", ("n", Number(ch))), "eyebrow").AddTo(col);
            Ui.Text(Loc.T("ui.manual.name." + ch), "h2").AddTo(col);
            Ui.Spacer().AddTo(head);
            if (showSeat)
                Ui.Text(Loc.T("ui.manual.side." + Side(ch)) + " · " + Loc.T("ui.manual.seat", ("n", Seat(ch))), "man-head-seat").AddTo(head);
            Ui.Text(Loc.T("ui.manual.intro." + ch), "body", "man-intro").AddTo(parent);
        }

        public static void Build(VisualElement parent, string ch)
        {
            if (IsParts(ch))
            {
                foreach (var cat in CategoriesOf(ch)) PartsSection(parent, cat, true);
                if (ch == "comms") GroundStations(parent);
                Ui.Text(Loc.T("ui.manual.bar_legend"), "small", "faint", "man-legend").AddTo(parent);
                return;
            }
            switch (ch)
            {
                case "rules": Rules(parent); break;
                case "diagnose": Diagnose(parent); break;
                case "procedures": ProceduresView(parent); break;
                default: Donki(parent); break;
            }
        }

        /// <summary>四章零件規格接在一起（平板用）。</summary>
        public static void BuildAllParts(VisualElement parent)
        {
            foreach (var ch in Chapters.Where(IsParts))
                foreach (var cat in CategoriesOf(ch)) PartsSection(parent, cat, true);
            Ui.Text(Loc.T("ui.manual.bar_legend"), "small", "faint", "man-legend").AddTo(parent);
        }

        // ---------------------------------------------------------------- 零件

        /// <summary>一類零件：類別標題與用途，下面一排零件卡。numbers = false 時只有代號、名稱與白話功能（嚴格分工的組裝區）。</summary>
        public static void PartsSection(VisualElement parent, string cat, bool numbers)
        {
            var m = GameSession.Mission;
            var parts = SessionRules.ShelfParts(GameSession.Data, m.S).Where(x => x.Category == cat).ToList();
            if (parts.Count == 0) return;
            var sec = Ui.El("man-section").AddTo(parent);
            var head = Ui.Row("man-sec-head").AddTo(sec);
            Ui.Text(Loc.T("ui.cat." + cat), "eyebrow").AddTo(head);
            Ui.Text(Loc.T("ui.baymanual.cat." + cat), "small", "dim", "man-sec-why").AddTo(head);

            var installed = new HashSet<string>(m.S.Reported.AllPartIds());
            // 每一項數值在同類裡的最大值（長條的比例）
            var stats = parts.Select(p => Stats(p).ToList()).ToList();
            var max = new Dictionary<string, double>();
            foreach (var list in stats)
                foreach (var s in list)
                    max[s.Key] = Math.Max(max.TryGetValue(s.Key, out var v) ? v : 0, s.Raw);

            var grid = Ui.El("man-cards").AddTo(sec);
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                var card = Ui.El("man-card").Cls("man-card--on", numbers && installed.Contains(p.Id)).AddTo(grid);
                var top = Ui.Row("man-card-top").AddTo(card);
                Ui.Text(p.Code, "man-code", "mono").AddTo(top);
                Ui.Text(Loc.T(p.Name), "man-card-name").AddTo(top);
                var badges = Ui.Row("man-badges").AddTo(card);
                if (numbers && installed.Contains(p.Id)) Badge(badges, "ui.manual.installed", "man-badge--on");
                if (p.Redundant) Badge(badges, "ui.manual.redundant", null);
                if (p.SunlitOnly) Badge(badges, "ui.manual.sunlit", "man-badge--warn");
                Ui.Text(Loc.T(p.Function), "small", "dim", "man-func").AddTo(card);
                if (!numbers) continue;

                var key = stats[i][0];
                var kf = Ui.Row("man-key").AddTo(card);
                Ui.Text(key.Value, "man-key-v", "mono").AddTo(kf);
                Ui.Text(Loc.T("ui.mspec." + key.Key), "small", "dim", "man-key-k").AddTo(kf);
                Bar(card, key, max).AddToClassList("man-bar--key");
                var list = Ui.El("man-stats").AddTo(card);
                foreach (var s in stats[i].Skip(1))
                {
                    var r = Ui.Row("man-stat").AddTo(list);
                    Ui.Text(Loc.T("ui.mspec." + s.Key), "man-stat-k").AddTo(r);
                    Ui.Spacer().AddTo(r);
                    Ui.Text(s.Value, "man-stat-v", "mono").AddTo(r);
                    if (s.Raw > 0 && s.Compare) Bar(list, s, max);
                }
            }
        }

        static void Badge(VisualElement parent, string key, string cls)
        {
            var b = Ui.Text(Loc.T(key), "man-badge").AddTo(parent);
            if (cls != null) b.AddToClassList(cls);
        }

        static VisualElement Bar(VisualElement parent, Stat s, Dictionary<string, double> max)
        {
            double m = max.TryGetValue(s.Key, out var v) ? v : 0;
            var track = Ui.El("man-bar").AddTo(parent);
            var fill = Ui.El("man-bar-fill", s.Better ? "man-bar-fill--good" : "man-bar-fill--cost").AddTo(track);
            fill.style.width = Length.Percent(m > 0 ? (float)Math.Max(4, s.Raw / m * 100) : 0);
            return track;
        }

        /// <summary>零件卡上的一項數值。Better = 越大越好（青色長條），否則是代價（灰色）；Compare = 要不要畫長條。</summary>
        struct Stat
        {
            public string Key, Value;
            public double Raw;
            public bool Better, Compare;
            public Stat(string key, double raw, string value, bool better, bool compare = true)
            { Key = key; Raw = raw; Value = value; Better = better; Compare = compare; }
        }

        /// <summary>第一項是這類零件的「關鍵數值」（卡片上的大字），其餘依序列出。</summary>
        static IEnumerable<Stat> Stats(PartDef p)
        {
            var k = GameSession.Data.Constants;
            switch (p.Category)
            {
                case PartCategory.Instrument:
                    yield return new Stat("science", p.ScienceValue, Fmt.N(p.ScienceValue), true);
                    yield return new Stat("data", p.DataGbPerDay, Fmt.N(p.DataGbPerDay, 0) + " GB/d", false);
                    if (p.ResolutionM > 0) yield return new Stat("resolution", p.ResolutionM, Fmt.N(p.ResolutionM) + " m", false, false);
                    break;
                case PartCategory.Solar:
                    yield return new Stat("array", p.AreaM2 * k.SolarArrayWPerM2, Fmt.N(p.AreaM2 * k.SolarArrayWPerM2) + " W", true);
                    yield return new Stat("area", p.AreaM2, Fmt.N(p.AreaM2, 1) + " m²", true, false);
                    break;
                case PartCategory.Battery:
                    yield return new Stat("battery", p.CapacityWh, Fmt.N(p.CapacityWh) + " Wh", true);
                    break;
                case PartCategory.Antenna:
                    yield return new Stat("downlink", p.DownlinkMbps, Fmt.N(p.DownlinkMbps) + " Mbps", true);
                    if (p.TxPowerW > 0) yield return new Stat("txpower", p.TxPowerW, Fmt.N(p.TxPowerW, 1) + " W", true, false);
                    break;
                case PartCategory.Computer:
                    yield return new Stat("storage", p.StorageGb, Fmt.N(p.StorageGb) + " GB", true);
                    yield return new Stat("radiation", p.RadToleranceKrad, Fmt.N(p.RadToleranceKrad) + " krad", true);
                    break;
                case PartCategory.Adcs:
                    // 指向精度：角秒越小越準
                    yield return new Stat("pointing", p.PointingArcsec, Fmt.N(p.PointingArcsec) + "″", false);
                    break;
                case PartCategory.Propulsion:
                    yield return new Stat("propellant", p.PropellantKg, p.PropellantKg > 0 ? Fmt.N(p.PropellantKg, 0) + " kg" : "—", true);
                    if (p.IspS > 0) yield return new Stat("isp", p.IspS, Fmt.N(p.IspS) + " s", true);
                    break;
                case PartCategory.Shielding:
                    // 劑量倍率：越小擋越多
                    yield return new Stat("dose", p.DoseFactor, "×" + Fmt.N(p.DoseFactor, 2), false);
                    if (p.ShieldingMm > 0) yield return new Stat("shield", p.ShieldingMm, Fmt.N(p.ShieldingMm, 1) + " mm", true, false);
                    break;
            }
            yield return new Stat("mass", p.MassKg, Fmt.N(p.MassKg, 1) + " kg", false);
            yield return new Stat("power", p.PowerW, Fmt.N(p.PowerW, 1) + " W", false);
            yield return new Stat("cost", p.CostMusd, Fmt.Money(p.CostMusd), false);
            yield return new Stat("reliability", p.ReliabilityPerYear, Fmt.Pct(p.ReliabilityPerYear) + " / yr", true, false);
        }

        /// <summary>地面站不是實體零件（在任務控制台選），放在通訊章當參考。</summary>
        static void GroundStations(VisualElement parent)
        {
            var sec = Ui.El("man-section").AddTo(parent);
            var head = Ui.Row("man-sec-head").AddTo(sec);
            Ui.Text(Loc.T("ui.cat.groundstation"), "eyebrow").AddTo(head);
            Ui.Text(Loc.T("ui.manual.gs_why"), "small", "dim", "man-sec-why").AddTo(head);
            var grid = Ui.El("man-cards").AddTo(sec);
            foreach (var g in GameSession.Data.PartsIn(PartCategory.GroundStation))
            {
                var card = Ui.El("man-card").AddTo(grid);
                var top = Ui.Row("man-card-top").AddTo(card);
                Ui.Text(Loc.T(g.Name), "man-card-name").AddTo(top);
                var kf = Ui.Row("man-key").AddTo(card);
                Ui.Text(Fmt.N(g.Stations), "man-key-v", "mono").AddTo(kf);
                Ui.Text(Loc.T("ui.mspec.stations"), "small", "dim", "man-key-k").AddTo(kf);
                var list = Ui.El("man-stats").AddTo(card);
                var r = Ui.Row("man-stat").AddTo(list);
                Ui.Text(Loc.T("ui.mspec.opscost"), "man-stat-k").AddTo(r);
                Ui.Spacer().AddTo(r);
                Ui.Text(Fmt.Money(g.OpsCostMusdPerYear) + " / yr", "man-stat-v", "mono").AddTo(r);
            }
        }

        // ---------------------------------------------------------------- 設計規則

        static readonly (string rule, string dash)[] RuleList =
        {
            ("power", "res.power"), ("battery", "ui.dash.battery"), ("mass", "res.mass"), ("data", "res.data"),
            ("comms", "res.comms"), ("budget", "res.budget"), ("lifetime", "ui.dash.lifetime"), ("instruments", "ui.dash.science"),
        };

        static void Rules(VisualElement parent)
        {
            var grid = Ui.El("man-rules").AddTo(parent);
            int i = 0;
            foreach (var (k, dash) in RuleList)
            {
                var c = Ui.El("man-rule").AddTo(grid);
                var head = Ui.Row("man-rule-head").AddTo(c);
                Ui.Text((++i).ToString("00"), "man-rule-n", "mono").AddTo(head);
                Ui.Text(Loc.T("ui.manual.rule." + k + ".t"), "h3").AddTo(head);
                Ui.Text(Loc.T("ui.manual.rule." + k), "body").AddTo(c);
                var check = Ui.Row("man-rule-check").AddTo(c);
                Ui.Text(Loc.T("ui.manual.check_on"), "small", "faint").AddTo(check);
                Ui.Text(Loc.T(dash), "man-chip").AddTo(check);
            }
        }

        // ---------------------------------------------------------------- 異常判讀

        // 症狀對照表：每個事件在各項遙測上的樣子（和 event.diag.* 的文字一致）
        static readonly string[] Signals = { "radiation", "battery", "solar", "parts", "alert" };
        static readonly Dictionary<EventKind, string[]> Matrix = new Dictionary<EventKind, string[]>
        {
            { EventKind.SolarStorm, new[] { "up2", "", "down", "", "" } },
            { EventKind.PartFailure, new[] { "", "", "", "fault", "" } },
            { EventKind.ScienceOpportunity, new[] { "", "", "", "", "alert" } },
            { EventKind.PowerAnomaly, new[] { "", "down2", "half", "", "" } },
        };

        static void Diagnose(VisualElement parent)
        {
            Ui.Text(Loc.T("ui.manual.matrix"), "eyebrow", "man-sec").AddTo(parent);
            var table = Ui.El("man-matrix").AddTo(parent);
            var head = Ui.Row("man-mx-row", "man-mx-row--head").AddTo(table);
            Ui.Text("", "man-mx-event").AddTo(head);
            foreach (var s in Signals) Ui.Text(Loc.T("ui.manual.sig." + s), "man-mx-cell").AddTo(head);
            foreach (var k in OpsEvents.All)
            {
                var row = Ui.Row("man-mx-row").AddTo(table);
                Ui.Text(Loc.T("event.title." + OpsEvents.Key(k)), "man-mx-event").AddTo(row);
                var vals = Matrix.TryGetValue(k, out var v) ? v : new string[Signals.Length];
                foreach (var x in vals)
                {
                    var cell = Ui.El("man-mx-cell").AddTo(row);
                    if (string.IsNullOrEmpty(x)) { Ui.Text("—", "faint").AddTo(cell); continue; }
                    Ui.Text(Loc.T("ui.manual.mx." + x), "man-mx-v", "man-mx-v--" + x).AddTo(cell);
                }
            }
            Ui.Text(Loc.T("ui.manual.matrix_hint"), "small", "faint", "man-legend").AddTo(parent);

            Ui.Text(Loc.T("ui.manual.signs"), "eyebrow", "man-sec").AddTo(parent);
            var grid = Ui.El("man-rules").AddTo(parent);
            foreach (var k in OpsEvents.All)
            {
                var c = Ui.El("man-rule").AddTo(grid);
                Ui.Text(Loc.T("event.title." + OpsEvents.Key(k)), "h3").AddTo(c);
                Ui.Text(Loc.T("event.diag." + OpsEvents.Key(k)), "body").AddTo(c);
                var check = Ui.Row("man-rule-check").AddTo(c);
                Ui.Text(Loc.T("ui.manual.then"), "small", "faint").AddTo(check);
                Ui.Text(Loc.T("ui.manual.name.procedures") + " · " + Loc.T("ui.manual.chapter", ("n", Number("procedures"))), "man-chip").AddTo(check);
            }
        }

        // ---------------------------------------------------------------- 處置程序

        static void ProceduresView(VisualElement parent)
        {
            // 四座控制台在營運組（綠）中控台上，由左到右
            var legend = Ui.Row("man-consoles").AddTo(parent);
            Ui.Text(Loc.T("ui.manual.consoles_at"), "small", "dim").AddTo(legend);
            foreach (var c in OpsConsoles.All) Ui.Text(Loc.T("ops.console." + c), "man-chip", "man-chip--" + c).AddTo(legend);

            foreach (var k in OpsEvents.All)
            {
                var card = Ui.El("man-proc").AddTo(parent);
                Ui.Text(Loc.T("event.title." + OpsEvents.Key(k)), "h3").AddTo(card);
                var row = Ui.Row("man-steps").AddTo(card);
                var steps = Procedures.Required(k);
                for (int i = 0; i < steps.Count; i++)
                {
                    if (i > 0) Ui.Text("→", "man-step-arrow").AddTo(row);
                    var st = Ui.Row("man-step").AddTo(row);
                    Ui.Text((i + 1).ToString(), "man-step-n", "mono").AddTo(st);
                    var col = Ui.El("man-step-text").AddTo(st);
                    Ui.Text(Loc.T("ops.action." + steps[i]), "man-step-name").AddTo(col);
                    string console = OpsConsoles.ConsoleOf(steps[i]);
                    Ui.Text(Loc.T("ops.console." + console), "man-chip", "man-chip--" + console).AddTo(col);
                }
            }
        }

        // ---------------------------------------------------------------- 太空天氣（DONKI）

        static void Donki(VisualElement parent)
        {
            var grid = Ui.El("man-rules").AddTo(parent);
            foreach (var e in GameSession.Data.SpaceWeather.OrderByDescending(x => x.Severity).ThenByDescending(x => x.Date))
            {
                var c = Ui.El("man-rule", "man-storm", "man-storm--" + Math.Clamp(e.Severity, 1, 3)).AddTo(grid);
                var head = Ui.Row("man-rule-head").AddTo(c);
                var pips = Ui.Row("man-pips").AddTo(head);
                for (int i = 1; i <= 3; i++) Ui.El("man-pip").Cls("man-pip--on", i <= e.Severity).AddTo(pips);
                Ui.Text(Loc.SpaceWeather(e.Id), "h3").AddTo(head);
                var meta = Ui.Row("man-storm-meta").AddTo(c);
                // 名稱裡已經有日期、等級和 Kp，這裡只補類型和速度
                if (!string.IsNullOrEmpty(e.Type)) Ui.Text(Loc.T("ui.manual.sw." + e.Type), "man-chip").AddTo(meta);
                if (e.SpeedKms > 0) Ui.Text(Fmt.N(e.SpeedKms) + " km/s", "small", "mono", "accent").AddTo(meta);
                Ui.Text(Loc.T(e.Summary), "body").AddTo(c);
            }
        }
    }
}
