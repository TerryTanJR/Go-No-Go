using System;
using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // ======================================================================== 工程手冊
    //
    // 手冊拆成八章，放在中控室後排的八個席位（Screen_manual-<章>）：
    //   北座（面向影像牆時在左）：1 酬載、2 電力、3 通訊、4 航電 —— 對應組裝區的四座零件架
    //   南座（在右）：5 設計規則、6 異常判讀、7 處置程序、8 太空天氣
    // 前排設計組的「manual」席是目錄：每章在哪一席、零件代號在第幾章。

    /// <summary>
    /// 後排一席 = 手冊的一章，做成一本書：一頁一個主題，← → 或下方按鈕翻頁，翻頁時舊的那頁折向書脊。
    /// 第一次打開時封面翻開。每章有自己的顏色（書脊、封面、標題、圖表），看一眼就知道翻到哪一章。
    /// chapter 為 null 時（組裝區、不嚴格分工）上方有每章的書籤，可以翻全部，翻到一章最後一頁再往後就到下一章。
    /// </summary>
    public sealed class ManualPanel : FacPanel
    {
        const long FlipMs = 420;

        readonly string _fixed;
        static string _browse = ManualView.Chapters[0];
        /// <summary>每章翻到第幾頁（關掉再打開還在同一頁）。</summary>
        static readonly Dictionary<string, int> PageOf = new Dictionary<string, int>();

        bool _opened;
        // 等著播的翻頁：從哪一章第幾頁翻過來、方向（+1 往後、-1 往前）
        string _fromCh;
        int _fromPage = -1, _dir;

        public ManualPanel(string chapter = null) { _fixed = chapter; }

        public override string Title => _fixed != null ? Loc.T("ui.manual.ch." + _fixed) : Loc.T("ui.manual.title");
        public override string WideClass => "modal--manual";

        string Ch => _fixed ?? _browse;
        static int PageIn(string ch) => PageOf.TryGetValue(ch, out var p) ? p : 0;

        protected override void Build(VisualElement body)
        {
            body.parent?.AddToClassList("modal--book");
            string ch = Ch;
            int count = ManualView.Pages(ch).Count;
            int page = PageOf[ch] = Math.Clamp(PageIn(ch), 0, count - 1);

            var book = Ui.El("book", "book--" + ch).AddTo(body);
            if (_fixed == null)
            {
                var tabs = Ui.Row("book-tabs").AddTo(book);
                foreach (var c in ManualView.Chapters)
                {
                    var cc = c;
                    var t = Ui.Btn("", () => GoChapter(cc), "book-tab", "book--" + c).Cls("book-tab--on", c == ch).AddTo(tabs);
                    t.Clear();
                    Ui.Text(ManualView.Number(c).ToString(), "book-tab-num", "mono").AddTo(t);
                    Ui.Text(Loc.T("ui.manual.name." + c), "book-tab-name").AddTo(t);
                }
            }

            var spread = Ui.Row("book-spread").AddTo(book);
            var spine = Ui.El("book-spine").AddTo(spread);
            Ui.Text(ManualView.Number(ch).ToString(), "book-spine-num", "mono").AddTo(spine);
            var sheet = Ui.El("book-sheet").AddTo(spread);
            ManualView.PageHead(sheet, ch, _fixed != null);
            var host = Ui.El("book-page").AddTo(sheet);

            // 往前翻：底下先放舊的那頁，新的一頁從書脊翻出來蓋上；往後翻：底下是新頁，舊頁折向書脊
            bool flip = _fromPage >= 0;
            bool back = flip && _dir < 0;
            var under = Scroll(host, "book-scroll");
            under.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            if (back) ManualView.RenderPage(under, _fromCh, _fromPage);
            else ManualView.RenderPage(under, ch, page);
            if (flip) Leaf(host, back ? ch : _fromCh, back ? page : _fromPage, back);
            _fromPage = -1;

            var foot = Ui.Row("book-foot").AddTo(sheet);
            var prev = Ui.Btn("‹  " + Loc.T("ui.manual.prev"), () => Flip(-1), "book-turn").AddTo(foot);
            prev.SetEnabled(page > 0 || (_fixed == null && Array.IndexOf(ManualView.Chapters, ch) > 0));
            Ui.Spacer().AddTo(foot);
            var dots = Ui.Row("book-dots").AddTo(foot);
            for (int i = 0; i < count; i++) Ui.El("book-dot").Cls("book-dot--on", i == page).AddTo(dots);
            Ui.Text(Loc.T("ui.manual.page", ("n", page + 1), ("total", count)), "book-pageno", "mono").AddTo(foot);
            Ui.Spacer().AddTo(foot);
            var next = Ui.Btn(Loc.T("ui.manual.next") + "  ›", () => Flip(1), "book-turn").AddTo(foot);
            next.SetEnabled(page < count - 1 || (_fixed == null && Array.IndexOf(ManualView.Chapters, ch) < ManualView.Chapters.Length - 1));

            if (!_opened)
            {
                _opened = true;
                Cover(spread, ch);
            }
        }

        /// <summary>翻頁的那一張紙：蓋在頁面上，以書脊（左邊）為軸縮成一條線，越翻越暗。</summary>
        void Leaf(VisualElement host, string ch, int page, bool comingIn)
        {
            var leaf = Ui.El("book-leaf").AddTo(host);
            leaf.pickingMode = PickingMode.Ignore;
            var inner = Ui.El("book-leaf-inner").AddTo(leaf);
            inner.pickingMode = PickingMode.Ignore;
            ManualView.RenderPage(inner, ch, page);
            Ui.El("book-leaf-shade").AddTo(leaf).pickingMode = PickingMode.Ignore;
            if (comingIn)
            {
                leaf.AddToClassList("book-leaf--turned");
                leaf.schedule.Execute(() => leaf.RemoveFromClassList("book-leaf--turned")).StartingIn(16);
                // 翻完：重畫一次，底下換成新的一頁
                leaf.schedule.Execute(() => { if (Body != null) Refresh(); }).StartingIn(FlipMs);
            }
            else
            {
                Ui.AnimateIn(leaf, "book-leaf--turned");
                leaf.schedule.Execute(() => leaf.RemoveFromHierarchy()).StartingIn(FlipMs);
            }
        }

        /// <summary>第一次打開：章節顏色的封面停一下，再往左翻開。</summary>
        static void Cover(VisualElement spread, string ch)
        {
            var cover = Ui.El("book-cover").AddTo(spread);
            cover.pickingMode = PickingMode.Ignore;
            Ui.Text(Loc.T("ui.manual.title"), "book-cover-kicker").AddTo(cover);
            Ui.Text(ManualView.Number(ch).ToString(), "book-cover-num", "mono").AddTo(cover);
            Ui.Text(Loc.T("ui.manual.name." + ch), "book-cover-name").AddTo(cover);
            cover.schedule.Execute(() => { Sfx.Page(); cover.AddToClassList("book-cover--open"); }).StartingIn(260);
            cover.schedule.Execute(() => cover.RemoveFromHierarchy()).StartingIn(260 + 700);
        }

        void Flip(int dir)
        {
            string ch = Ch;
            int count = ManualView.Pages(ch).Count;
            int page = PageIn(ch), to = page + dir;
            if (to >= 0 && to < count)
            {
                Turn(ch, page, dir);
                PageOf[ch] = to;
                Refresh();
                return;
            }
            // 翻過頭：瀏覽模式接到上一章的最後一頁／下一章的第一頁
            if (_fixed != null) return;
            int ci = Array.IndexOf(ManualView.Chapters, ch) + dir;
            if (ci < 0 || ci >= ManualView.Chapters.Length) return;
            var next = ManualView.Chapters[ci];
            Turn(ch, page, dir);
            PageOf[next] = dir > 0 ? 0 : ManualView.Pages(next).Count - 1;
            _browse = next;
            Refresh();
        }

        void GoChapter(string c)
        {
            if (c == _browse) return;
            int dir = Array.IndexOf(ManualView.Chapters, c) > Array.IndexOf(ManualView.Chapters, _browse) ? 1 : -1;
            Turn(_browse, PageIn(_browse), dir);
            _browse = c;
            Refresh();
        }

        void Turn(string ch, int page, int dir)
        {
            _fromCh = ch;
            _fromPage = page;
            _dir = dir;
            Sfx.Page();
        }

        public override void Tick(float dt)
        {
            var kb = Keyboard.current;
            if (kb == null || Body == null || UIRoot.IsTyping) return;
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame || kb.pageDownKey.wasPressedThisFrame) Flip(1);
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame || kb.pageUpKey.wasPressedThisFrame) Flip(-1);
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

        /// <summary>每章的顏色（書脊、封面、圖表），和 Theme.uss 的 .book--章 一致。</summary>
        public static Color ChapterColor(string ch) => ch switch
        {
            "payload" => new Color(0.71f, 0.55f, 1f),
            "power" => new Color(1f, 0.72f, 0.28f),
            "comms" => new Color(0.22f, 0.78f, 0.96f),
            "avionics" => new Color(0.24f, 0.86f, 0.52f),
            "rules" => new Color(0.42f, 0.62f, 1f),
            "diagnose" => new Color(1f, 0.42f, 0.45f),
            "procedures" => new Color(1f, 0.62f, 0.26f),
            _ => new Color(0.95f, 0.41f, 0.88f),
        };

        /// <summary>書頁上方的一行：第幾章、章名、在後排哪一席。</summary>
        public static void PageHead(VisualElement parent, string ch, bool showSeat)
        {
            var head = Ui.Row("book-head").AddTo(parent);
            Ui.Text(Loc.T("ui.manual.chapter", ("n", Number(ch))), "eyebrow", "book-head-ch").AddTo(head);
            Ui.Text(Loc.T("ui.manual.name." + ch), "book-head-name").AddTo(head);
            Ui.Spacer().AddTo(head);
            if (showSeat)
                Ui.Text(Loc.T("ui.manual.side." + Side(ch)) + " · " + Loc.T("ui.manual.seat", ("n", Seat(ch))), "man-head-seat").AddTo(head);
        }

        /// <summary>書的一頁：第一頁前面多一段這一章怎麼讀。</summary>
        public static void RenderPage(VisualElement parent, string ch, int page)
        {
            var pages = Pages(ch);
            page = Math.Clamp(page, 0, pages.Count - 1);
            if (page == 0) Ui.Text(Loc.T("ui.manual.intro." + ch), "body", "man-intro").AddTo(parent);
            pages[page](parent);
        }

        /// <summary>
        /// 一章拆成幾頁：零件章第一頁是比較圖、之後一類零件一頁；規則四張一頁、快查表一頁；
        /// 異常判讀先對照表再徵兆；太空天氣先時間軸、再六張一頁。
        /// </summary>
        public static List<Action<VisualElement>> Pages(string ch)
        {
            var pages = new List<Action<VisualElement>>();
            if (IsParts(ch))
            {
                pages.Add(p => Overview(p, ch));
                foreach (var cat in CategoriesOf(ch))
                {
                    var c = cat;
                    if (ShelfOf(c).Count == 0) continue;
                    pages.Add(p => { PartsSection(p, c, true); Ui.Text(Loc.T("ui.manual.bar_legend"), "small", "faint", "man-legend").AddTo(p); });
                }
                if (ch == "comms") pages.Add(GroundStations);
                return pages;
            }
            switch (ch)
            {
                case "rules":
                    var rules = RuleItems();
                    for (int i = 0; i < rules.Count; i += 4)
                    {
                        int from = i;
                        pages.Add(p => RuleCards(p, rules, from, 4));
                    }
                    if (HasOrbits) pages.Add(QuickOrbits);
                    if (HasLinkTables) pages.Add(QuickLinks);
                    break;
                case "diagnose":
                    pages.Add(Matrix);
                    pages.Add(Signs);
                    break;
                case "procedures":
                    pages.Add(ProceduresView);
                    break;
                default:
                    pages.Add(DonkiTimeline);
                    int n = Storms().Count;
                    for (int i = 0; i < n; i += 6)
                    {
                        int from = i;
                        pages.Add(p => DonkiCards(p, from, 6));
                    }
                    break;
            }
            return pages;
        }

        /// <summary>整章接在一起（平板的捲動版）。</summary>
        public static void Build(VisualElement parent, string ch)
        {
            foreach (var page in Pages(ch)) page(parent);
        }

        static List<PartDef> ShelfOf(string cat) =>
            SessionRules.ShelfParts(GameSession.Data, GameSession.Mission.S).Where(x => x.Category == cat).ToList();

        // ---------------------------------------------------------------- 零件章第一頁：比較圖

        /// <summary>每類零件一張散布圖：橫軸成本、縱軸這類的關鍵數值。船上裝的那個是綠點。</summary>
        static void Overview(VisualElement parent, string ch)
        {
            Legend(parent, Loc.T("ui.manual.chart.intro"), "dim", "man-legend");
            var row = Ui.Row("mchart-row").AddTo(parent);
            var installed = new HashSet<string>(GameSession.Mission.S.Reported.AllPartIds());
            var color = ChapterColor(ch);
            foreach (var cat in CategoriesOf(ch))
            {
                var parts = ShelfOf(cat);
                if (parts.Count == 0) continue;
                var key = Stats(parts[0]).First();
                var box = Ui.El("mchart-box").AddTo(row);
                Ui.Text(Loc.T("ui.cat." + cat), "h3").AddTo(box);
                string unit = KeyUnit(key);
                var chart = new ScatterChart(Loc.T("ui.mspec.cost"), Loc.T("ui.mspec." + key.Key),
                    v => v > 0 && v < 0.1f ? "$" + Fmt.N(v * 1000, 0) + "K" : Fmt.Money(v), v => Fmt.N(v, v < 10 ? 1 : 0) + unit, color);
                foreach (var part in parts)
                {
                    var k = Stats(part).First();
                    chart.AddPoint((float)part.CostMusd, (float)k.Raw, part.Code, installed.Contains(part.Id));
                }
                box.Add(chart);
                Legend(box, Loc.T(key.Better ? "ui.manual.chart.up" : "ui.manual.chart.down", ("stat", Loc.T("ui.mspec." + key.Key))), "faint");
                Ui.Text(Loc.T(key.Better ? "ui.manual.chart.corner_up" : "ui.manual.chart.corner_down"), "small", "mchart-corner").AddTo(box);
            }
        }

        /// <summary>一排短標籤（字串用 | 分開），各自一塊、空間不夠整塊換行，不會在詞中間斷開。</summary>
        static void Legend(VisualElement parent, string text, params string[] cls)
        {
            var row = Ui.Row("mlegend").AddTo(parent);
            foreach (var c in cls) row.AddToClassList(c);
            foreach (var part in text.Split('|')) Ui.Text(part.Trim(), "small", "mlegend-item").AddTo(row);
        }

        /// <summary>關鍵數值的單位（從格式化好的字串取空白後面那段）。</summary>
        static string KeyUnit(Stat s)
        {
            int i = s.Value.LastIndexOf(' ');
            return i > 0 ? " " + s.Value.Substring(i + 1) : "";
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
                Named(top, Loc.T(p.Name), "man-card-name");
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
                    double arrayW = p.AreaM2 * k.SolarArrayWPerM2 * p.CellFactor;
                    yield return new Stat("array", arrayW, Fmt.N(arrayW) + " W", true);
                    yield return new Stat("area", p.AreaM2, Fmt.N(p.AreaM2, 1) + " m²", true, false);
                    break;
                case PartCategory.Battery:
                    yield return new Stat("battery", p.CapacityWh, Fmt.N(p.CapacityWh) + " Wh", true);
                    break;
                case PartCategory.Antenna:
                    yield return new Stat("downlink", p.DownlinkMbps, Fmt.N(p.DownlinkMbps) + " Mbps", true);
                    if (p.TxPowerW > 0) yield return new Stat("txpower", p.TxPowerW, Fmt.N(p.TxPowerW, 1) + " W", true, false);
                    // 每張天線卡都列出可用比例（只有誘餌才列的話，一看就知道哪個有問題）
                    yield return new Stat("link", p.LinkAvailability, Fmt.Pct(p.LinkAvailability), true, false);
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
                Named(top, Loc.T(g.Name), "man-card-name");
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

        /// <summary>野火關卡多三張：火點偵測原理、通報延遲怎麼算、排除假火點。</summary>
        static readonly (string rule, string dash)[] FireRuleList =
        {
            ("fire", "ui.dash.detection"), ("latency", "ui.dash.latency"), ("falsefire", "ops.console.payload"),
        };

        static bool FireMission => GameSession.Mission?.Scenario?.FireAlert ?? false;

        static List<(string rule, string dash)> RuleItems() => (FireMission ? RuleList.Concat(FireRuleList) : RuleList).ToList();

        static void RuleCards(VisualElement parent, List<(string rule, string dash)> rules, int from, int count)
        {
            var grid = Ui.El("man-rules").AddTo(parent);
            int i = from;
            foreach (var (k, dash) in rules.Skip(from).Take(count))
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

        // ---------------------------------------------------------------- 快查表

        /// <summary>
        /// 中控室試算用的快查表。儀表板（組裝區）只看得到船上現在的數字，換零件之前要靠這些表推算。
        /// 每張表只有一個變數；組合起來（例如這條軌道 × 這個負載要多大電池）留給玩家。
        /// </summary>
        static List<OrbitDef> Orbits => GameSession.Data.OrbitsFor(GameSession.Mission.Scenario).ToList();
        static bool HasOrbits => Orbits.Count > 0;
        static List<PartDef> Antennas => SessionRules.ShelfParts(GameSession.Data, GameSession.Mission.S).Where(p => p.Category == PartCategory.Antenna).OrderBy(p => p.Tier).ToList();
        static List<OrbitDef> PassOrbits => Orbits.Where(o => o.ContactFraction <= 0).ToList();
        static bool HasLinkTables => Antennas.Count > 0 && PassOrbits.Count > 0;

        static double Period(OrbitDef o)
        {
            var k = GameSession.Data.Constants;
            return OrbitMath.PeriodMinutes(o.AltitudeKm, o.Body == "moon" ? k.MoonRadiusKm : k.EarthRadiusKm, o.Body == "moon" ? k.MoonMuKm3S2 : k.EarthMuKm3S2);
        }

        static double EclipseFraction(OrbitDef o)
        {
            var k = GameSession.Data.Constants;
            return OrbitMath.EclipseFraction(o.AltitudeKm, o.Body == "moon" ? k.MoonRadiusKm : k.EarthRadiusKm, o.BetaDeg);
        }

        static double Eclipse(OrbitDef o) => Period(o) * EclipseFraction(o);

        /// <summary>快查表第一頁：每條軌道一個日照／地影圓環，下面是軌道表和電池表。</summary>
        static void QuickOrbits(VisualElement parent)
        {
            var k = GameSession.Data.Constants;
            var orbits = Orbits;
            Ui.Text(Loc.T("ui.manual.quick"), "eyebrow", "man-sec").AddTo(parent);
            Ui.Text(Loc.T("ui.manual.quick_hint"), "small", "dim").AddTo(parent);

            var rings = Ui.Row("mring-row").AddTo(parent);
            foreach (var o in orbits)
            {
                var cell = Ui.El("mring-cell").AddTo(rings);
                cell.Add(new OrbitRing((float)EclipseFraction(o), Fmt.N(Eclipse(o), 1), Loc.T("ui.manual.ring.eclipse")));
                Named(cell, Loc.T(o.Name), "mring-name");
                Ui.Text(Loc.T("ui.manual.ring.period", ("min", Fmt.N(Period(o)))), "small", "faint").AddTo(cell);
            }
            var legend = Ui.Row("mring-legend").AddTo(parent);
            Ui.El("mring-swatch", "mring-swatch--sun").AddTo(legend);
            Ui.Text(Loc.T("ui.manual.ring.sun"), "small", "dim").AddTo(legend);
            Ui.El("mring-swatch", "mring-swatch--shadow").AddTo(legend);
            Ui.Text(Loc.T("ui.manual.ring.shadow"), "small", "dim").AddTo(legend);

            // 1. 軌道：每圈多久、地影多長、每座地面站每天過站幾次
            var t1 = Table(parent, "ui.manual.q.orbits", new[] { "ui.manual.q.period", "ui.manual.q.eclipse", "ui.manual.q.passes" });
            foreach (var o in orbits)
                TableRow(t1, Loc.T(o.Name), Fmt.N(Period(o)) + " min", Fmt.N(Eclipse(o), 1) + " min",
                    o.ContactFraction > 0 ? Fmt.Pct(o.ContactFraction) : Fmt.N(o.PassesPerDayPerStation));

            // 2. 電池：地影中每 10 W 的負載，撐過一次要多少、在安全放電深度下要多少
            var t2 = Table(parent, "ui.manual.q.battery", new[] { "ui.manual.q.survive", "ui.manual.q.safe" });
            foreach (var o in orbits)
            {
                double wh = 10 * Eclipse(o) / 60 / k.BatteryEfficiency;
                TableRow(t2, Loc.T(o.Name), Fmt.N(wh, 1) + " Wh", Fmt.N(wh / o.BatteryDod, 1) + " Wh (" + Fmt.Pct(o.BatteryDod) + ")");
            }
        }

        /// <summary>快查表第二頁：天線每次過站能傳多少、野火關卡的地面站等待時間。</summary>
        static void QuickLinks(VisualElement parent)
        {
            var m = GameSession.Mission;
            var data = GameSession.Data;
            var k = data.Constants;
            var antennas = Antennas;
            var passOrbits = PassOrbits;
            // 3. 天線：每次過站能傳多少資料（依軌道的過站時間）
            if (antennas.Count > 0 && passOrbits.Count > 0)
            {
                var t3 = Table(parent, "ui.manual.q.downlink", passOrbits.Select(o => Loc.T(o.Name)).ToArray(), raw: true);
                foreach (var a in antennas)
                    TableRow(t3, a.Code + " " + Loc.T(a.Name), passOrbits.Select(o =>
                        Fmt.N(a.DownlinkMbps * o.DownlinkRateFactor * o.PassMinutes * 60 / 8 / 1000 * k.LinkEfficiency, 1)).ToArray());
            }

            // 4. 野火關卡：地面站數量 → 平均等過站的時間
            if (m.Scenario != null && m.Scenario.LatencyLimitMin > 0 && passOrbits.Count > 0)
            {
                var gs = data.PartsIn(PartCategory.GroundStation).ToList();
                var t4 = Table(parent, "ui.manual.q.wait", passOrbits.Select(o => Loc.T(o.Name)).ToArray(), raw: true);
                foreach (var g in gs)
                    TableRow(t4, Loc.T("ui.manual.q.stations", ("n", g.Stations)),
                        passOrbits.Select(o => Fmt.N(1440.0 / (g.Stations * o.PassesPerDayPerStation) / 2)).ToArray());
            }
        }

        /// <summary>
        /// 名稱和括號裡的規格分兩行（「展開式太陽能翼」／「1.2 m²」），不會在括號中間斷行。
        /// cls 掛在外框上，字級和顏色由兩行繼承；沒有括號就是一個 Label。
        /// </summary>
        static VisualElement Named(VisualElement parent, string full, params string[] cls)
        {
            int i = full.IndexOf('（');
            if (i < 0) i = full.IndexOf(" (", StringComparison.Ordinal);
            if (i <= 0) return Ui.Text(full, cls).AddTo(parent);
            var box = Ui.El("named").AddTo(parent);
            foreach (var c in cls) box.AddToClassList(c);
            Ui.Text(full.Substring(0, i).Trim(), "named-main").AddTo(box);
            Ui.Text(full.Substring(i).Trim().Trim('（', '）', '(', ')', ' '), "named-sub", "mono").AddTo(box);
            return box;
        }

        static VisualElement Table(VisualElement parent, string titleKey, string[] cols, bool raw = false)
        {
            var box = Ui.El("man-qt").AddTo(parent);
            Ui.Text(Loc.T(titleKey), "h3").AddTo(box);
            var head = Ui.Row("man-mx-row", "man-mx-row--head").AddTo(box);
            Ui.Text("", "man-mx-event").AddTo(head);
            foreach (var c in cols) Named(head, raw ? c : Loc.T(c), "man-mx-cell");
            return box;
        }

        static void TableRow(VisualElement table, string name, params string[] cells)
        {
            var row = Ui.Row("man-mx-row").AddTo(table);
            Named(row, name, "man-mx-event");
            foreach (var c in cells) Ui.Text(c, "man-mx-cell", "mono").AddTo(row);
        }

        // ---------------------------------------------------------------- 異常判讀

        // 症狀對照表：每個事件在各項遙測上的樣子（和 event.diag.* 的文字一致）
        // 最後兩欄（火點儀、長波熱紅外線）只在野火關卡顯示
        static readonly string[] Signals = { "radiation", "battery", "solar", "parts", "alert", "mwir", "lwir" };
        const int BaseSignals = 5;
        static readonly Dictionary<EventKind, string[]> SignMatrix = new Dictionary<EventKind, string[]>
        {
            { EventKind.SolarStorm, new[] { "up2", "", "down", "", "", "", "" } },
            { EventKind.PartFailure, new[] { "", "", "", "fault", "", "", "" } },
            { EventKind.ScienceOpportunity, new[] { "", "", "", "", "alert", "", "" } },
            { EventKind.PowerAnomaly, new[] { "", "down2", "half", "", "", "", "" } },
            { EventKind.BigFire, new[] { "", "", "", "", "", "up2", "up" } },
            { EventKind.FalseFire, new[] { "", "", "", "", "", "up2", "flat" } },
        };

        /// <summary>手冊列的事件：野火關卡只列這一局會出現的；其他關卡照舊列四種通用事件。</summary>
        static List<EventKind> Events()
        {
            var m = GameSession.Mission;
            if (FireMission && m != null) return OpsEvents.For(m.Scenario, m.Difficulty);
            return OpsEvents.All.Where(k => !OpsEvents.IsFire(k)).ToList();
        }

        static void Matrix(VisualElement parent)
        {
            Ui.Text(Loc.T("ui.manual.matrix"), "eyebrow", "man-sec").AddTo(parent);
            var table = Ui.El("man-matrix").AddTo(parent);
            var head = Ui.Row("man-mx-row", "man-mx-row--head").AddTo(table);
            Ui.Text("", "man-mx-event").AddTo(head);
            int cols = FireMission ? Signals.Length : BaseSignals;
            foreach (var s in Signals.Take(cols)) Ui.Text(Loc.T("ui.manual.sig." + s), "man-mx-cell").AddTo(head);
            var events = Events();
            foreach (var k in events)
            {
                var row = Ui.Row("man-mx-row").AddTo(table);
                Ui.Text(Loc.T("event.title." + OpsEvents.Key(k)), "man-mx-event").AddTo(row);
                var vals = SignMatrix.TryGetValue(k, out var v) ? v : new string[Signals.Length];
                foreach (var x in vals.Take(cols))
                {
                    var cell = Ui.El("man-mx-cell").AddTo(row);
                    if (string.IsNullOrEmpty(x)) { Ui.Text("—", "faint").AddTo(cell); continue; }
                    Ui.Text(Loc.T("ui.manual.mx." + x), "man-mx-v", "man-mx-v--" + x).AddTo(cell);
                }
            }
            Ui.Text(Loc.T("ui.manual.matrix_hint"), "small", "faint", "man-legend").AddTo(parent);
        }

        static void Signs(VisualElement parent)
        {
            var events = Events();
            Ui.Text(Loc.T("ui.manual.signs"), "eyebrow", "man-sec").AddTo(parent);
            var grid = Ui.El("man-rules").AddTo(parent);
            foreach (var k in events)
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

            foreach (var k in Events())
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

            // 互相衝突的指令：同一回合一起送出，兩個都無效
            var conf = Ui.El("man-proc").AddTo(parent);
            Ui.Text(Loc.T("ui.manual.conflicts"), "h3").AddTo(conf);
            Ui.Text(Loc.T("ui.manual.conflicts_body"), "small", "dim").AddTo(conf);
            foreach (var (a, b) in Procedures.Conflicts)
                Ui.Text(Loc.T("ops.action." + a) + "  ✕  " + Loc.T("ops.action." + b), "body").AddTo(conf);
        }

        // ---------------------------------------------------------------- 太空天氣（DONKI）

        /// <summary>第一頁：照日期排的時間軸，長條高度是嚴重程度，顏色是類型。</summary>
        static void DonkiTimeline(VisualElement parent)
        {
            Ui.Text(Loc.T("ui.manual.timeline"), "eyebrow", "man-sec").AddTo(parent);
            var items = Storms().OrderBy(x => x.Date)
                .Select(x => new StormTimeline.Item { Date = x.Date, Type = x.Type, Severity = x.Severity }).ToList();
            parent.Add(new StormTimeline(items));
            var legend = Ui.Row("mring-legend").AddTo(parent);
            foreach (var t in new[] { "FLR", "CME", "GST" })
            {
                var sw = Ui.El("mring-swatch").AddTo(legend);
                sw.style.backgroundColor = StormTimeline.TypeColor(t);
                Ui.Text(Loc.T("ui.manual.sw." + t), "small", "dim").AddTo(legend);
            }
            Ui.Text(Loc.T("ui.manual.timeline_hint"), "small", "faint", "man-legend").AddTo(parent);
        }

        /// <summary>手冊列的事件：歷史關卡（例如 2009 年的 OCO）只列到任務結束那一年。</summary>
        static List<SpaceWeatherEvent> Storms()
        {
            var m = GameSession.HasMission ? GameSession.Mission : null;
            return GameSession.Data.StormsKnownBy(m?.Scenario, m?.S.Bench.MissionYears ?? 1);
        }

        static void DonkiCards(VisualElement parent, int from, int count)
        {
            var grid = Ui.El("man-rules").AddTo(parent);
            foreach (var e in Storms().OrderByDescending(x => x.Severity).ThenByDescending(x => x.Date).Skip(from).Take(count))
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
