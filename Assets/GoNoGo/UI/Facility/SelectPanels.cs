using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // ======================================================================== 設計主螢幕（中控室前方的影像牆）
    //
    // 選件：依類別列出零件，勾選每類要用哪個（酬載儀器可以多選）；軌道、火箭、地面站、年數也在這裡選。
    // 一般關卡開局一個零件都沒勾（軌道、火箭、地面站、年數有預設值）；新手訓練和歷史關卡照預設的設計。
    // 零件的六種數值開局有一部分是空的：玩家在兩個房間找到線索後，自己在空格輸入數字（系統不自動填，填錯不擋）。
    // 右邊的儀表板照「玩家看到的數值」算：沒填的不算，用到沒填數值的項目顯示「資料不足」。
    // 任務需求直接列出來，互相拉扯的需求標成「取捨」。
    // 確定設計之後，改成顯示每個零件要用哪些小零件（組裝區儀表板顯示同樣的內容）；要改選先按「改選」。
    // 所有人都可以看、都可以操作：選擇狀態在房主那裡，所有人看到的畫面一致。

    public sealed class SelectPanel : FacPanel
    {
        public override string Title => Loc.T("ui.sel.title");
        public override string WideClass => "modal--select";

        static int _tab;
        static string _cat = PartCategory.Instrument;
        static string _open;
        bool _typing, _pending;

        static readonly string[] Cats =
        {
            PartCategory.Instrument, PartCategory.Solar, PartCategory.Battery, PartCategory.Antenna,
            PartCategory.Computer, PartCategory.Adcs, PartCategory.Propulsion, PartCategory.Shielding,
        };

        /// <summary>同一個分頁、同一類零件：填數值、勾零件、展開說明之後，清單留在原本捲到的位置。</summary>
        protected override string ScrollKey => _tab + "/" + _cat + "/" + (M.S.Confirmed ? 1 : 0);

        /// <summary>正在空格裡打字時不重畫（別人改了別的東西，自己打到一半的數字不能被洗掉），離開空格再補畫。</summary>
        public override void Refresh()
        {
            if (_typing) { _pending = true; return; }
            _pending = false;
            base.Refresh();
        }

        public override void Detach()
        {
            _typing = false;
            base.Detach();
        }

        protected override void Build(VisualElement body)
        {
            var s = M.S;
            bool locked = s.DesignLocked(D);
            SelectView.Stages(body, false);
            if (locked) Ui.Text(Loc.T("err.locked"), "small", "tight").AddTo(body);

            var cols = Ui.Row("sel-cols").AddTo(body);
            var left = Ui.El("sel-left").AddTo(cols);
            var right = Ui.El("sel-right").AddTo(cols);

            if (s.Confirmed && !locked)
            {
                // 設計確定了：每個零件要用哪些小零件
                var head = Ui.Row("sel-confirmed-head").AddTo(left);
                Ui.Text(Loc.T("ui.sel.confirmed"), "h3", "go").AddTo(head);
                Ui.Spacer().AddTo(head);
                var reopen = Ui.Btn(Loc.T("ui.sel.reopen"), () => { GameSession.Send(CommandType.Confirm, "", 0); Hud.Signal("reopen"); }, "btn-ghost").AddTo(head);
                reopen.SetEnabled(s.Poll == null && (s.Phase == SessionPhase.Design || s.Phase == SessionPhase.Briefing));
                Ui.Text(Loc.T("ui.sel.confirmed_body"), "small", "dim", "sel-confirmed-body").AddTo(left);
                SelectView.AssemblyList(Scroll(left, "sel-scroll"), false);
            }
            else
            {
                Tabs(left, new[] { "ui.sel.tab.parts", "ui.sel.tab.mission", "ui.sel.tab.reqs" }, _tab, i => { _tab = i; Refresh(); });
                var sv = Scroll(left, "sel-scroll");
                switch (_tab)
                {
                    case 1: MissionTab(sv); break;
                    case 2: SelectView.Requirements(sv, false, true); break;
                    default: PartsTab(sv); break;
                }
            }

            SelectView.Dashboard(Scroll(right, "sel-scroll"), false);
            ConfirmBar(right, locked);
        }

        // ---------------------------------------------------------------- 零件

        void PartsTab(VisualElement parent)
        {
            var s = M.S;
            var catalog = SessionRules.Catalog(D, s);
            var cats = Cats.Where(c => catalog.Any(p => p.Category == c)).ToList();
            if (!cats.Contains(_cat)) _cat = cats.FirstOrDefault() ?? PartCategory.Instrument;

            var chips = Ui.Row("sel-cats").AddTo(parent);
            foreach (var c in cats)
            {
                var cat = c;
                bool has = c == PartCategory.Instrument ? s.Bench.Instruments.Count > 0 : !string.IsNullOrEmpty(s.Bench.Get(c));
                var b = Ui.Btn(Loc.T("ui.cat." + c) + (has ? "  ✓" : ""), () => { _cat = cat; Refresh(); }, "chip", "sel-cat").Cls("chip--on", c == _cat).AddTo(chips);
                b.focusable = false;
            }
            var cells = Blanks.Cells(D, s);
            int blanks = cells.Count, filled = cells.Count(c => Blanks.Filled(s, c.part, c.field) != null);
            if (_cat == PartCategory.Instrument)
                Ui.Text(Loc.T("ui.sel.instruments_n", ("n", s.Bench.Instruments.Count), ("max", M.Scenario?.MaxInstruments ?? 3)), "small", "dim").AddTo(parent);
            if (blanks > 0)
                Ui.Text(Loc.T("ui.sel.blank_hint", ("n", filled), ("total", blanks)), "small", filled >= blanks ? "go" : "tight", "sel-blank-hint").AddTo(parent);

            bool canEdit = SessionRules.CanEditDesign(s, D) == SessionRules.Ok;
            var picked = new HashSet<string>(s.Bench.AllPartIds());
            foreach (var p in catalog.Where(p => p.Category == _cat).OrderBy(p => SelectView.CodeNumber(p.Code)).ThenBy(p => p.Code, StringComparer.Ordinal))
                PartRow(parent, p, canEdit, picked.Contains(p.Id));
        }

        void PartRow(VisualElement list, PartDef p, bool canEdit, bool on)
        {
            var row = Ui.El("sel-part").Cls("sel-part--on", on).AddTo(list);
            var top = Ui.Row("sel-part-top").AddTo(row);
            var tick = Ui.Btn(on ? "✓" : "", () =>
            {
                Sfx.Click();
                GameSession.Send(CommandType.SelectPart, p.Id, on ? 0 : 1);
                Hud.Signal("select");
            }, "sel-tick").Cls("sel-tick--on", on).AddTo(top);
            tick.focusable = false;
            tick.SetEnabled(canEdit);
            Ui.Text(p.Code, "man-code", "mono").AddTo(top);
            Ui.Text(SelectView.PartName(p), "sel-part-name").AddTo(top);
            Ui.Spacer().AddTo(top);
            Ui.Text(Fmt.PartMoney(p.CostMusd), "sel-price", "mono").AddTo(top);
            bool open = _open == p.Id;
            var more = Ui.Btn(open ? "▼" : "▶", () => { _open = open ? null : p.Id; Refresh(); }, "sel-more").AddTo(top);
            more.focusable = false;

            var cells = Ui.Row("sel-cells").AddTo(row);
            foreach (var f in PartFields.All)
                if (PartFields.Get(p, f) > 0) Cell(cells, p, f);
            if (cells.childCount == 0) Ui.Text(Loc.T("ui.sel.no_values"), "small", "faint").AddTo(cells);

            if (open) Info(row, p);
        }

        /// <summary>一格數值：沒挖空的直接顯示；挖空的是一個輸入框，填過的旁邊寫是誰填的。</summary>
        void Cell(VisualElement parent, PartDef p, string field)
        {
            var s = M.S;
            var c = Ui.El("sel-cell").AddTo(parent);
            Ui.Text(Loc.T("ui.field." + field), "sel-cell-k").AddTo(c);
            var line = Ui.Row("sel-cell-line").AddTo(c);
            if (!Blanks.IsBlank(D, s, p.Id, field))
            {
                Ui.Text(SelectView.Num(PartFields.Get(p, field)), "sel-cell-v", "mono").AddTo(line);
                Ui.Text(Fmt.Unit(PartFields.Unit(field)), "sel-cell-u").AddTo(line);
                return;
            }
            var filled = Blanks.Filled(s, p.Id, field);
            var tf = new TextField { value = filled != null ? SelectView.Num(filled.V) : "", isDelayed = true, maxLength = 10 };
            tf.AddToClassList("sel-input");
            tf.EnableInClassList("sel-input--empty", filled == null);
            tf.textEdition.placeholder = "?";
            tf.SetEnabled(s.Phase == SessionPhase.Design || s.Phase == SessionPhase.Briefing);
            tf.RegisterCallback<FocusInEvent>(_ => _typing = true);
            tf.RegisterCallback<FocusOutEvent>(_ =>
            {
                _typing = false;
                if (_pending) Body?.schedule.Execute(() => { if (!_typing && Body != null) Refresh(); });
            });
            string pid = p.Id;
            tf.RegisterValueChangedCallback(e =>
            {
                e.StopPropagation();
                Commit(pid, field, e.newValue, e.previousValue, tf);
            });
            line.Add(tf);
            Ui.Text(Fmt.Unit(PartFields.Unit(field)), "sel-cell-u").AddTo(line);
            if (filled != null) Ui.Text(SelectView.PlayerName(filled.By), "sel-by").AddTo(c);
        }

        /// <summary>按 Enter 或離開空格：把數字送出去（空白 = 清掉這一格）。不是數字就退回原本的內容。</summary>
        void Commit(string partId, string field, string text, string before, TextField tf)
        {
            text = (text ?? "").Trim().Replace(",", "").Replace("，", "");
            double? value = null;
            if (text.Length > 0)
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || double.IsNaN(v) || double.IsInfinity(v)
                    || v < 0 || v > Blanks.MaxValue)
                {
                    Sfx.NoGo();
                    UIRoot.Toast(Loc.T("ui.sel.nan"));
                    tf.SetValueWithoutNotify(before ?? "");
                    return;
                }
                value = v;
            }
            Sfx.Confirm();
            _pending = true;
            GameSession.Send(CommandType.Fill, Blanks.FillArg(partId, field, value));
            Hud.Signal("fill");
            // 按 Enter 送出：離開空格，畫面才會重畫（顯示是誰填的）
            tf.Blur();
        }

        /// <summary>展開的零件資訊：一句功能、其他（沒挖空的）規格；數值都填完了才顯示完整的說明（說明裡常常直接寫了數字）。</summary>
        void Info(VisualElement row, PartDef p)
        {
            var box = Ui.El("sel-info").AddTo(row);
            Ui.Text(Loc.T(p.Function), "body", "sel-info-func").AddTo(box);
            var stats = Ui.Row("sel-info-stats").AddTo(box);
            foreach (var (key, value) in SelectView.OtherStats(p))
            {
                var st = Ui.El("sel-stat").AddTo(stats);
                Ui.Text(Loc.T("ui.mspec." + key), "sel-cell-k").AddTo(st);
                Ui.Text(value, "sel-cell-v", "mono").AddTo(st);
            }
            if (p.SunlitOnly) Ui.Text(Loc.T("ui.manual.sunlit"), "man-badge", "man-badge--warn").AddTo(stats);
            if (p.Redundant) Ui.Text(Loc.T("ui.manual.redundant"), "man-badge").AddTo(stats);
            int missing = Blanks.MissingOf(D, M.S, p.Id);
            if (missing > 0) Ui.Text(Loc.T("ui.sel.desc_locked", ("n", missing)), "small", "faint").AddTo(box);
            else Ui.Text(Ui.NoOrphan(Loc.T(p.Description)), "small", "dim", "sel-info-desc").AddTo(box);
        }

        // ---------------------------------------------------------------- 任務設定：軌道、火箭、地面站、年數

        void MissionTab(VisualElement parent)
        {
            var s = M.S;
            var d = s.Bench;
            bool canEdit = SessionRules.CanEditDesign(s, D) == SessionRules.Ok;
            var cols = Ui.Row("mc-cols").AddTo(parent);

            var orbitCol = Ui.El("mc-colx").AddTo(cols);
            Ui.Text(Loc.T("ui.cat.orbit"), "eyebrow").AddTo(orbitCol);
            var fire = M.Scenario != null && M.Scenario.FireAlert;
            foreach (var o in D.OrbitsFor(M.Scenario))
            {
                var id = o.Id;
                // 野火關卡：軌道的差別在過境時段，所以標出每圈地影
                string sub = Fmt.N(o.AltitudeKm) + " km";
                if (fire) sub += " · " + Loc.T("ui.mc.eclipse", ("min", Fmt.N(OrbitMath.PeriodMinutes(o.AltitudeKm, D.Constants.EarthRadiusKm, D.Constants.EarthMuKm3S2)
                    * OrbitMath.EclipseFraction(o.AltitudeKm, D.Constants.EarthRadiusKm, o.BetaDeg))));
                var b = Choice(orbitCol, Loc.T(o.Name), sub, id == d.OrbitId, canEdit, () => GameSession.Send(CommandType.SetOrbit, id));
                if (id != d.OrbitId && SessionRules.OrbitLauncherCheck(D, id, d.LauncherId) != SessionRules.Ok) b.SetEnabled(false);
            }
            var lockL = D.Launcher(d.LauncherId);
            if (lockL != null && !string.IsNullOrEmpty(lockL.OrbitLock))
                Ui.Text(Loc.T("ui.mc.rideshare_lock", ("orbit", Loc.Orbit(lockL.OrbitLock))), "small", "tight").AddTo(orbitCol);
            Ui.Text(Loc.T("ui.cat.groundstation"), "eyebrow", "mc-sub").AddTo(orbitCol);
            foreach (var g in D.PartsIn(PartCategory.GroundStation))
            {
                var id = g.Id;
                Choice(orbitCol, Loc.T(g.Name), Loc.T("ui.mc.stations", ("n", g.Stations)) + " · " + Fmt.Money(g.OpsCostMusdPerYear) + "/yr",
                    id == d.Get(PartCategory.GroundStation), canEdit, () => GameSession.Send(CommandType.SetGroundStation, id));
            }

            var lCol = Ui.El("mc-colx").AddTo(cols);
            Ui.Text(Loc.T("ui.cat.launcher"), "eyebrow").AddTo(lCol);
            foreach (var l in D.LaunchersFor(M.Scenario))
            {
                var id = l.Id;
                l.CapacityKg.TryGetValue(d.OrbitId, out var cap);
                bool rideshareBlocked = SessionRules.OrbitLauncherCheck(D, d.OrbitId, id) != SessionRules.Ok;
                string sub = (rideshareBlocked ? Loc.T("err.rideshare_orbit") : cap > 0 ? Fmt.N(cap) + " kg → " + Loc.Orbit(d.OrbitId) : Loc.T("ui.mc.cannot_reach"))
                             + " · " + Fmt.Money(l.CostMusd);
                var lb = Choice(lCol, Loc.T(l.Name), sub, id == d.LauncherId, canEdit, () => GameSession.Send(CommandType.SetLauncher, id));
                if (rideshareBlocked && id != d.LauncherId) lb.SetEnabled(false);
            }
            Ui.Text(Loc.T("ui.cat.years"), "eyebrow", "mc-sub").AddTo(lCol);
            var yr = Ui.Row("years-row").AddTo(lCol);
            Ui.Btn("−", () => GameSession.Send(CommandType.SetYears, "", d.MissionYears - 1), "btn-small").AddTo(yr).SetEnabled(canEdit && d.MissionYears > 1);
            Ui.Text(Fmt.Years(d.MissionYears), "h3", "mono", "years-value").AddTo(yr);
            Ui.Btn("+", () => GameSession.Send(CommandType.SetYears, "", d.MissionYears + 1), "btn-small").AddTo(yr)
                .SetEnabled(canEdit && d.MissionYears < SessionRules.MaxMissionYears(M.Scenario));
        }

        static Button Choice(VisualElement parent, string name, string sub, bool on, bool canEdit, Action pick)
        {
            var b = Ui.Btn("", () => { pick(); Sfx.Click(); Hud.Signal("mission"); }, "mc-choice").Cls("mc-choice--on", on).AddTo(parent);
            b.Clear();
            Ui.Text(name, "mc-name").AddTo(b);
            Ui.Text(sub, "small", "dim", "mono").AddTo(b);
            b.SetEnabled(canEdit || on);
            b.focusable = false;
            return b;
        }

        // ---------------------------------------------------------------- 確定設計

        void ConfirmBar(VisualElement parent, bool locked)
        {
            var s = M.S;
            var bar = Ui.El("sel-confirm").AddTo(parent);
            if (locked) return;
            if (s.Confirmed)
            {
                int left = SessionRules.NotInstalled(s, D).Count, total = SessionRules.SelectedParts(s, D).Count;
                Ui.Text(left == 0 ? Loc.T("ui.sel.all_installed") : Loc.T("ui.sel.installed_n", ("n", total - left), ("total", total)), "body", left == 0 ? "go" : "tight").AddTo(bar);
                return;
            }
            var gaps = SessionRules.DesignGaps(s, D);
            if (gaps.Count > 0)
                Ui.Text(Loc.T("ui.sel.gaps", ("list", string.Join(Loc.IsZh ? "、" : ", ", gaps.Select(c => Loc.T("ui.cat." + c))))), "small", "tight").AddTo(bar);
            else if (s.IsSandbox(D))
            {
                // 沙盒不用確定設計（選了就直接裝上衛星）：只留一句說明，不放按鈕
                Ui.Text(Loc.T("ui.sel.sandbox_note"), "small", "dim").AddTo(bar);
                return;
            }
            else Ui.Text(Loc.T("ui.sel.confirm_note"), "small", "dim").AddTo(bar);
            var go = Ui.Btn(Loc.T("ui.sel.confirm"), () =>
            {
                Sfx.Confirm();
                GameSession.Send(CommandType.Confirm, "", 1);
                Hud.Signal("confirm");
            }, "btn-go", "btn-big", "sel-confirm-btn").AddTo(bar);
            go.focusable = false;
            go.SetEnabled(gaps.Count == 0 && SessionRules.CanEditDesign(s, D) == SessionRules.Ok);
        }
    }

    // ======================================================================== 組裝區儀表板

    /// <summary>
    /// 組裝區牆上的儀表板：設計確定前顯示「等待中控室確定設計」，確定後顯示每個零件要用哪些小零件（和設計主螢幕相同）。
    /// </summary>
    public sealed class BayPanel : FacPanel
    {
        public override string Title => Loc.T("ui.bay.title");
        public override string WideClass => "modal--wide";

        protected override void Build(VisualElement body) => SelectView.Bay(Scroll(body), false);
    }

    /// <summary>設計主螢幕、組裝區儀表板、牆上的螢幕、個人模式的平板共用的內容。</summary>
    public static class SelectView
    {
        static MissionSession M => GameSession.Mission;
        static GameData D => GameSession.Data;

        public static string Num(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>編號裡的數字（P-10 → 10）：照數字排，P-2 才會排在 P-10 前面。</summary>
        public static int CodeNumber(string code)
        {
            int i = (code ?? "").IndexOf('-');
            return i >= 0 && int.TryParse(code.Substring(i + 1), out var n) ? n : 0;
        }

        /// <summary>零件名稱：被挖空的容量、速率還沒填時，名字後面括號裡的數字不顯示（不然等於直接寫答案）。</summary>
        public static string PartName(PartDef p) => p == null ? "" : Blanks.MaskedName(D, M.S, p, Loc.Lang);

        public static string PlayerName(int id)
        {
            // 沒取名字的玩家：「玩家 2」（和 Go/No-Go 點名的寫法一樣）
            var n = M.S.Player(id)?.Look?.Name;
            return string.IsNullOrEmpty(n) ? Loc.T("ui.poll.player", ("n", id)) : n;
        }

        /// <summary>沒挖空的其他規格（展開零件時列出）：照現狀顯示；可靠度本來就不顯示。</summary>
        public static IEnumerable<(string key, string value)> OtherStats(PartDef p)
        {
            var k = D.Constants;
            switch (p.Category)
            {
                case PartCategory.Instrument:
                    yield return ("science", Fmt.N(p.ScienceValue));
                    break;
                case PartCategory.Solar:
                    yield return ("array", Fmt.N(p.AreaM2 * k.SolarArrayWPerM2 * p.CellFactor) + " W");
                    yield return ("area", Fmt.N(p.AreaM2, 1) + " m²");
                    break;
                case PartCategory.Antenna:
                    if (p.TxPowerW > 0) yield return ("txpower", Fmt.N(p.TxPowerW, 1) + " W");
                    yield return ("link", Fmt.Pct(p.LinkAvailability));
                    break;
                case PartCategory.Computer:
                    yield return ("storage", Fmt.N(p.StorageGb) + " GB");
                    yield return ("radiation", Fmt.N(p.RadToleranceKrad) + " krad");
                    break;
                case PartCategory.Adcs:
                    yield return ("pointing", Fmt.N(p.PointingArcsec) + "″");
                    break;
                case PartCategory.Propulsion:
                    yield return ("propellant", p.PropellantKg > 0 ? Fmt.N(p.PropellantKg, 1) + " kg" : "—");
                    if (p.IspS > 0) yield return ("isp", Fmt.N(p.IspS) + " s");
                    break;
                case PartCategory.Shielding:
                    yield return ("dose", "×" + Fmt.N(p.DoseFactor, 2));
                    if (p.ShieldingMm > 0) yield return ("shield", Fmt.N(p.ShieldingMm, 1) + " mm");
                    break;
            }
        }

        // ---------------------------------------------------------------- 階段列

        /// <summary>選件 → 組裝 → 發射 → 營運 → 評價，標出現在在哪一步。</summary>
        public static void Stages(VisualElement parent, bool compact)
        {
            var s = M.S;
            bool orbit = s.Launches.Any(l => l.Reached);
            int total = SessionRules.SelectedParts(s, D).Count, left = SessionRules.NotInstalled(s, D).Count;
            int turns = M.Plan?.Turns ?? M.Difficulty.Turns;
            var cells = Blanks.Cells(D, s);
            int blanks = cells.Count, filled = cells.Count(c => Blanks.Filled(s, c.part, c.field) != null);
            // 沙盒不用確定設計（選了就直接裝上衛星）：選件、組裝直接算完成
            bool confirmed = s.Confirmed || s.IsSandbox(D);
            var steps = new List<(string key, bool done, string sub)>
            {
                ("select", confirmed, s.Confirmed ? Loc.T("ui.prog.confirmed") : s.IsSandbox(D) ? "" : blanks > 0 ? Loc.T("ui.prog.values_n", ("n", filled), ("total", blanks)) : Loc.T("ui.prog.not_yet")),
                ("assemble", confirmed && left == 0, Loc.T("ui.prog.parts_n", ("n", total - left), ("total", total))),
                ("launch", orbit, s.Launches.Count == 0 ? Loc.T("ui.prog.not_yet")
                    : orbit ? Loc.T("ui.prog.in_orbit") : Loc.T("ui.prog.failed_n", ("n", s.FailedLaunches))),
                ("ops", s.Phase == SessionPhase.Report, s.Phase == SessionPhase.Ops ? Loc.T("ui.ops.turn", ("n", s.OpsTurn + 1), ("total", turns))
                    : s.Phase == SessionPhase.Report ? Loc.T("ui.prog.done") : Loc.T("ui.prog.turns_n", ("n", turns))),
                ("eval", false, ""),
            };
            int current = s.Phase == SessionPhase.Launch ? 2 : s.Phase == SessionPhase.Ops ? 3 : s.Phase == SessionPhase.Report ? 4
                : !confirmed ? 0 : left > 0 ? 1 : 2;

            var box = Ui.El("prog").Cls("prog--compact", compact).AddTo(parent);
            var row = Ui.Row("prog-stages").AddTo(box);
            for (int i = 0; i < steps.Count; i++)
            {
                var (key, done, sub) = steps[i];
                if (i > 0) Ui.El("prog-link").Cls("prog-link--done", i <= current).AddTo(row);
                var st = Ui.El("prog-stage").Cls("prog-stage--done", done && i != current).Cls("prog-stage--now", i == current).AddTo(row);
                var top = Ui.Row("prog-stage-top").AddTo(st);
                Ui.Text(done && i != current ? "✓" : (i + 1).ToString(), "prog-dot", "mono").AddTo(top);
                Ui.Text(Loc.T("ui.prog.stage." + key), "prog-stage-name").AddTo(top);
                if (!compact && !string.IsNullOrEmpty(sub)) Ui.Text(sub, "prog-stage-sub").AddTo(st);
            }
        }

        // ---------------------------------------------------------------- 儀表板（用玩家填的數值算）

        /// <summary>
        /// 依目前的勾選和任務設定，各項畫成長條和要求、預算對照。可以直接加總的（質量、耗電、成本）沒填的欄位不算進去，
        /// 旁邊標「還有 N 個數值沒填」；其他用到沒填數值的項目顯示「資料不足」，不顯示通過或不通過。
        /// 超出預算時預算那一項標紅（所有難度）；其他項目的紅黃綠只有學員難度有。
        /// </summary>
        public static void Dashboard(VisualElement parent, bool compact)
        {
            var m = M;
            var s = m.S;
            var ev = m.Dashboard;
            var d = s.Bench;
            bool colors = m.Difficulty.DashboardColors;
            bool basic = m.Difficulty.DashboardBasic;
            int Miss(string item) => Blanks.Missing(D, s, d, Blanks.FieldsFor(item));
            int missMass = Miss("mass"), missPower = Miss("power");

            var head = Ui.Row("dash-head").AddTo(parent);
            Ui.Text(Loc.T("ui.dash.title"), "eyebrow", "dash-sec").AddTo(head);
            Ui.Spacer().AddTo(head);
            if (!compact) Ui.Text(Loc.Orbit(d.OrbitId) + " · " + Loc.Launcher(d.LauncherId) + " · " + Fmt.Years(d.MissionYears), "small", "dim").AddTo(head);
            int allMissing = Blanks.Missing(D, s, d, PartFields.All);
            if (allMissing > 0 && !compact)
                Ui.Text(Loc.T("ui.dash.blank_note", ("n", allMissing)), "small", "tight", "sd-note").AddTo(parent);

            var grid = Ui.El("sd-grid").AddTo(parent);

            // 預算：價格不挖空，永遠算得出來；超出就標紅
            var b = ev[ResourceId.Budget];
            Bar(grid, Loc.T("res.budget"), Fmt.Resource(b), b.Fill, b.Status == ResourceStatus.Over ? "over" : colors ? Status(b.Status) : null,
                b.Status == ResourceStatus.Over ? Loc.T("ui.dash.gap_over", ("v", Fmt.Money(b.Used - b.Capacity))) : Fmt.Pct(b.Margin), 0);
            if (!basic && !compact)
            {
                var co = ev.Cost;
                Ui.Text(Loc.T("ui.dash.cost_x", ("dev", Fmt.PartMoney(co.DevelopmentMusd)), ("launch", Fmt.Money(co.LaunchMusd)), ("ops", Fmt.Money(co.OpsTotalMusd))),
                    "small", "faint", "sd-sub").AddTo(grid);
            }

            // 質量、耗電：可以直接加總，沒填的不算進去
            var mass = ev[ResourceId.Mass];
            Bar(grid, Loc.T("res.mass"), Fmt.Resource(mass), mass.Fill, Lamp(mass.Status, colors, missMass), colors ? Gap(mass) : Fmt.Pct(mass.Margin), missMass);
            var p = ev.Power;
            double pFill = p.ArrayEolW > 0 ? p.RequiredArrayW / p.ArrayEolW : (p.RequiredArrayW > 0 ? 2 : 0);
            var pStatus = p.ArrayMargin < 0 ? ResourceStatus.Over : p.ArrayMargin < Evaluator.TightMargin ? ResourceStatus.Tight : ResourceStatus.Ok;
            Bar(grid, Loc.T("res.power"), Fmt.N(p.RequiredArrayW) + " / " + Fmt.N(p.ArrayEolW) + " W", pFill, Lamp(pStatus, colors, missPower),
                Fmt.Pct(p.ArrayMargin), missPower);

            // 以下不能直接加總：用到的數值有一格沒填就是「資料不足」
            Derived(grid, Loc.T("ui.dash.battery"), Miss("battery"),
                Fmt.N(p.BatteryWh) + " / " + Fmt.N(p.EclipseEnergyWh / Math.Max(1e-6, D.Constants.BatteryEfficiency)) + " Wh",
                p.BatteryWh > 0 ? p.EclipseEnergyWh / Math.Max(1e-6, D.Constants.BatteryEfficiency) / p.BatteryWh : 2,
                colors ? (p.ActualDod > 1 ? "over" : p.ActualDod > p.AllowedDod ? "tight" : "go") : null,
                p.ActualDod > 1 && colors ? Loc.T("ui.dash.battery_short") : Loc.T("ui.dash.dod", ("pct", Fmt.Pct(p.ActualDod))));
            var data = ev[ResourceId.Data];
            Derived(grid, Loc.T("res.data"), Miss("data"), Fmt.Resource(data), data.Fill, colors ? Status(data.Status) : null, colors ? Gap(data) : Fmt.Pct(data.Margin));
            var comms = ev[ResourceId.Comms];
            Derived(grid, Loc.T("res.comms"), Miss("comms"), Fmt.Resource(comms), comms.Fill, colors ? Status(comms.Status) : null, colors ? Gap(comms) : Fmt.Pct(comms.Margin));
            bool shortLife = ev.Lifetime.Years + 1e-9 < d.MissionYears;
            Derived(grid, Loc.T("ui.dash.lifetime"), Miss("lifetime"), Fmt.Years(ev.Lifetime.Years) + " / " + Fmt.Years(d.MissionYears),
                d.MissionYears > 0 ? Math.Min(ev.Lifetime.Years, d.MissionYears * 2.0) / d.MissionYears / 2 : 0,
                colors ? (shortLife ? "over" : "go") : null, Loc.T("ui.dash.limit." + ev.Lifetime.Limit.ToString().ToLowerInvariant()));

            // 解析度：選的儀器裡最細的那一台（從 700 公里高拍；數字越小越清楚）
            var res = d.Instruments.Select(m.PlayerData.Part).Where(x => x != null && x.ResolutionM > 0).Select(x => x.ResolutionM).ToList();
            bool anyRes = d.Instruments.Select(D.Part).Any(x => x != null && x.ResolutionM > 0);
            if (anyRes)
                Derived(grid, Loc.T("ui.dash.resolution"), Miss("resolution"), res.Count > 0 ? Num(res.Min()) + " m" : "—", 0, null, Loc.T("ui.dash.resolution_x"), false);
            if (!basic)
            {
                Derived(grid, Loc.T("ui.dash.science"), 0, Fmt.N(ev.SciencePerYear, 1) + " / yr", 0, null, Loc.T("ui.dash.instruments", ("n", d.Instruments.Count)), false);
                Derived(grid, Loc.T("ui.dash.return"), Miss("return"), Fmt.Pct(ev.Data.ReturnFraction), ev.Data.ReturnFraction,
                    colors ? (ev.Data.ReturnFraction < 0.9 ? "tight" : "go") : null, "");
            }
            // 野火關卡：通報延遲與偵測率
            var sc = m.Scenario;
            if (sc != null && sc.LatencyLimitMin > 0)
            {
                var f = ev.Fire;
                double lim = sc.LatencyLimitMin;
                Derived(grid, Loc.T("ui.dash.latency"), Miss("latency"), Fmt.N(f.LatencyMin, 1) + " / " + Fmt.N(lim) + " min", f.LatencyMin / Math.Max(1e-6, lim),
                    colors ? (f.LatencyMin <= lim ? "go" : f.LatencyMin <= lim * 1.5 ? "tight" : "over") : null,
                    Loc.T("ui.dash.latency_x", ("wait", Fmt.N(f.WaitMin)), ("down", Fmt.N(f.DownlinkMin, 1)), ("ground", Fmt.N(f.ProcessingMin))));
                if (!basic)
                    Derived(grid, Loc.T("ui.dash.detection"), 0, Fmt.Pct(f.Detection), f.Detection, null,
                        Loc.T("ui.dash.detection_x", ("inst", Fmt.Pct(f.InstrumentDetection)), ("orbit", "×" + Fmt.N(f.OrbitFactor, 1))));
            }
            // 沒有顏色提示的難度才說一聲（不然會以為壞了）；有顏色的不用解釋
            if (!compact && !colors) Ui.Text(Loc.T("ui.dash.colors_off"), "small", "faint", "sd-foot").AddTo(parent);
        }

        static string Status(ResourceStatus st) => st == ResourceStatus.Over ? "over" : st == ResourceStatus.Tight ? "tight" : "go";

        /// <summary>加總項的燈號：還有數值沒填時不給綠燈（總和還會變大），超出的照樣標出來。</summary>
        static string Lamp(ResourceStatus st, bool colors, int missing)
        {
            if (!colors) return null;
            if (missing > 0 && st != ResourceStatus.Over) return null;
            return Status(st);
        }

        /// <summary>有燈號時的說明：超出多少、還差多少、只剩多少；正常的不寫。</summary>
        public static string Gap(ResourceReport r)
        {
            if (r.Status == ResourceStatus.Ok) return "";
            double v = Math.Abs(r.Capacity - r.Used);
            bool storage = r.Id == ResourceId.Data || r.Id == ResourceId.Comms;
            string amount = (r.Id == ResourceId.Budget ? Fmt.Money(v) : Fmt.N(v, storage ? 1 : 0) + " " + Fmt.Unit(r.Unit)).Trim();
            string key = r.Used <= r.Capacity ? "ui.dash.gap_left" : storage ? "ui.dash.gap_short" : "ui.dash.gap_over";
            return Loc.T(key, ("v", amount));
        }

        /// <summary>不能直接加總的一項：有數值沒填就整項顯示「資料不足」。</summary>
        static void Derived(VisualElement grid, string name, int missing, string value, double fill, string status, string note, bool bar = true)
        {
            if (missing > 0)
            {
                var r = Ui.El("sd-row", "sd-row--unknown").AddTo(grid);
                var top = Ui.Row("sd-top").AddTo(r);
                Ui.El("dash-lamp", "dash-lamp--none").AddTo(top);
                Ui.Text(name, "sd-k").AddTo(top);
                Ui.Spacer().AddTo(top);
                Ui.Text(Loc.T("ui.dash.unknown"), "sd-v", "sd-v--unknown").AddTo(top);
                Ui.Text(Loc.T("ui.dash.missing_n", ("n", missing)), "small", "tight", "sd-x").AddTo(r);
                return;
            }
            Bar(grid, name, value, bar ? fill : -1, status, note, 0);
        }

        /// <summary>一列：燈號、名稱、數值；下面一條長條（fill 小於 0 = 不畫）和說明。missing &gt; 0：加總還不完整。</summary>
        static void Bar(VisualElement grid, string name, string value, double fill, string status, string note, int missing)
        {
            var r = Ui.El("sd-row").AddTo(grid);
            if (status == "tight" || status == "over") r.AddToClassList("sd-row--" + status);
            var top = Ui.Row("sd-top").AddTo(r);
            Ui.El("dash-lamp", "dash-lamp--" + (status ?? "none")).AddTo(top);
            Ui.Text(name, "sd-k").AddTo(top);
            Ui.Spacer().AddTo(top);
            Ui.Text(value, "sd-v", "mono", status == "over" ? "over" : null).AddTo(top);
            if (fill >= 0)
            {
                var track = Ui.El("sd-track").AddTo(r);
                var f = Ui.El("sd-fill", status != null ? "sd-fill--" + status : null).AddTo(track);
                f.style.width = Length.Percent((float)(Math.Max(0, Math.Min(1, fill)) * 100));
                // 上限（要求、預算）在長條的最右邊；超出時整條滿版變紅
            }
            string x = missing > 0 ? Loc.T("ui.dash.missing_n", ("n", missing)) : note;
            if (!string.IsNullOrEmpty(x)) Ui.Text(x, "small", missing > 0 ? "tight" : status == "over" || status == "tight" ? status : "dim", "sd-x").AddTo(r);
        }

        // ---------------------------------------------------------------- 任務需求

        /// <summary>
        /// 任務需求：直接全部列出來。互相拉扯的需求標成「取捨」。
        /// 用到沒填數值的需求顯示「資料不足」；判定（✓／×、紅綠）只有學員難度有，其他難度只列現在的數字和目標。
        /// notes：下面再列取捨說明（為什麼有這條需求、兩條需求怎麼互相拉扯）。
        /// </summary>
        public static void Requirements(VisualElement parent, bool compact, bool notes)
        {
            var m = M;
            var s = m.S;
            var diff = m.Difficulty;
            bool verdict = diff.DashboardColors;
            var active = Departments.Active(m.Scenario, diff);

            var head = Ui.Row("prog-goals-head").AddTo(parent);
            Ui.Text(Loc.T("ui.req.title"), "eyebrow").AddTo(head);
            Ui.Spacer().AddTo(head);
            if (m.Scenario != null && m.Scenario.Sandbox)
            {
                Ui.Text(Loc.T("ui.prog.goals_sandbox"), "small", "dim").AddTo(head);
            }
            else
            {
                // 每一條需求算不算得出來、有沒有達成：各算一次，標題的統計和下面每一列共用
                var canJudge = new bool[active.Count];
                var isMet = new bool[active.Count];
                int judged = 0, met = 0;
                for (int i = 0; i < active.Count; i++)
                {
                    var r = active[i].Requirement;
                    canJudge[i] = Blanks.CanJudge(D, s, s.Bench, r);
                    isMet[i] = canJudge[i] && Core.Requirements.IsMetPreLaunch(r, m.Dashboard, s.Bench, m.PlayerData, diff);
                    if (canJudge[i]) judged++;
                    if (isMet[i]) met++;
                }
                Ui.Text(verdict ? Loc.T("ui.req.met", ("met", met), ("n", active.Count)) : Loc.T("ui.req.judged", ("n", judged), ("total", active.Count)), "small", "dim").AddTo(head);

                var list = Ui.El("prog-goals").AddTo(parent);
                for (int i = 0; i < active.Count; i++)
                {
                    var a = active[i];
                    var req = a.Requirement;
                    bool can = canJudge[i];
                    var g = Measure(req, m, diff);
                    bool ok = isMet[i];
                    string state = !can ? "unknown" : verdict ? (ok ? "go" : "over") : "neutral";

                    var row = Ui.El("prog-goal", "prog-goal--" + state).AddTo(list);
                    var top = Ui.Row("prog-goal-top").AddTo(row);
                    Ui.El("prog-dept", "req-dot--" + Departments.Key(a.Department)).AddTo(top);
                    Ui.Text(Loc.T("ui.prog.type." + req.Type), "prog-goal-name").AddTo(top);
                    if (req.ConflictGroup > 0) Ui.Text(Loc.T("ui.req.tradeoff"), "req-tag").AddTo(top);
                    // 目前值 / 目標：自成一組靠右；名稱太長、放不下同一行時整組換到下一行（不會和名稱疊在一起）
                    var val = Ui.Row("prog-goal-val").AddTo(top);
                    if (!can)
                    {
                        Ui.Text(Loc.T("ui.dash.unknown"), "prog-goal-now", "sd-v--unknown").AddTo(val);
                        Ui.Text(" / " + g.Target, "prog-goal-target", "mono").AddTo(val);
                    }
                    else
                    {
                        Ui.Text(g.Now, "prog-goal-now", "mono").AddTo(val);
                        Ui.Text(" / " + g.Target, "prog-goal-target", "mono").AddTo(val);
                        if (verdict) Ui.Text(ok ? "✓" : "×", "prog-goal-mark", ok ? "go" : "over").AddTo(val);
                    }
                    if (can && verdict)
                    {
                        var track = Ui.El("prog-track").AddTo(row);
                        var fill = Ui.El("prog-fill", "prog-fill--" + state).AddTo(track);
                        fill.style.width = Length.Percent((float)(Math.Max(0, Math.Min(1, g.Fraction)) * 100));
                    }
                    if (!compact) Ui.Text(Ui.NoOrphan(Loc.T(req.Text)), "small", "dim", "prog-goal-text").AddTo(row);
                    if (!can && !compact)
                        Ui.Text(Loc.T("ui.dash.missing_n", ("n", Blanks.Missing(D, s, s.Bench, Blanks.FieldsForRequirement(req.Type)))), "small", "tight").AddTo(row);
                }
                if (active.Any(a => a.Requirement.ConflictGroup > 0) && !compact)
                    Ui.Text(Loc.T("ui.req.tradeoff_note"), "small", "faint", "req-note").AddTo(parent);
            }

            if (!notes) return;
            Ui.Text(Loc.T("ui.req.notes"), "eyebrow", "req-notes-title").AddTo(parent);
            foreach (var (note, title, text) in Tradeoffs.For(D, s, m.Dashboard, Loc.Lang))
            {
                var c = Ui.El("note", "req-notecard").AddTo(parent);
                var nh = Ui.Row("req-note-head").AddTo(c);
                Ui.El("prog-dept", "req-dot--" + note.Topic).AddTo(nh);
                Ui.Text(Loc.Mark(title), "req-note-title").AddTo(nh);
                Ui.Text(Ui.NoOrphan(Loc.Mark(text)), "body").AddTo(c);
            }
        }

        struct Gauge
        {
            public string Now, Target;
            public double Fraction;
        }

        /// <summary>需求目前的數值和目標（和 Requirements.IsMetPreLaunch 用同一套算法，數字是玩家看到的）。</summary>
        static Gauge Measure(RequirementDef req, MissionSession m, DifficultyDef diff)
        {
            var ev = m.Dashboard;
            var d = m.S.Bench;
            var sc = m.Scenario;
            switch (req.Type)
            {
                case "budget_reserve":
                {
                    double v = ev[ResourceId.Budget].Margin;
                    return new Gauge { Now = Fmt.Pct(v), Target = req.Value > 0 ? "≥ " + Fmt.Pct(req.Value) : Loc.T("ui.prog.within_budget"), Fraction = req.Value > 0 ? v / req.Value : (v >= 0 ? 1 : 0) };
                }
                case "lifetime_target":
                {
                    double target = sc?.TargetYears ?? d.MissionYears;
                    double v = Math.Min(ev.Lifetime.Years, d.MissionYears);
                    return new Gauge { Now = Fmt.Years(v), Target = Fmt.Years(target), Fraction = target > 0 ? v / target : 1 };
                }
                case "mass_power_margin":
                {
                    double need = req.Value > 0 ? req.Value : diff.RequiredMargin;
                    double v = Math.Min(ev[ResourceId.Mass].Margin, ev[ResourceId.Power].Margin);
                    return new Gauge { Now = Fmt.Pct(v), Target = "≥ " + Fmt.Pct(need), Fraction = need > 0 ? v / need : (v >= 0 ? 1 : 0) };
                }
                case "science_threshold":
                {
                    double expected = ev.SciencePerYear * Math.Min(d.MissionYears, ev.Lifetime.Years) * ev.Data.ReturnFraction;
                    double v = sc != null ? Scoring.ScienceScore(expected, sc, d.MissionYears) : 0;
                    return new Gauge { Now = Fmt.N(v), Target = "≥ " + Fmt.N(req.Value), Fraction = req.Value > 0 ? v / req.Value : 1 };
                }
                case "instrument_required":
                {
                    bool on = d.Instruments.Contains(req.PartId);
                    string code = D.Part(req.PartId)?.Code ?? req.PartId;
                    return new Gauge { Now = on ? Loc.T("ui.prog.installed") : Loc.T("ui.prog.missing"), Target = code, Fraction = on ? 1 : 0 };
                }
                case "data_return":
                {
                    double v = ev.Data.ReturnFraction;
                    return new Gauge { Now = Fmt.Pct(v), Target = "≥ " + Fmt.Pct(req.Value), Fraction = req.Value > 0 ? v / req.Value : 1 };
                }
                case "instrument_any":
                {
                    var on = d.Instruments.FirstOrDefault(req.PartIds.Contains);
                    string codes = string.Join(" / ", req.PartIds.Select(id => D.Part(id)?.Code ?? id));
                    return new Gauge { Now = on != null ? (D.Part(on)?.Code ?? on) : Loc.T("ui.prog.missing"), Target = codes, Fraction = on != null ? 1 : 0 };
                }
                case "orbit_required":
                {
                    bool on = d.OrbitId == req.OrbitId;
                    return new Gauge { Now = Loc.Orbit(d.OrbitId), Target = Loc.Orbit(req.OrbitId), Fraction = on ? 1 : 0 };
                }
                case "battery_eclipse":
                {
                    // 一次地影用掉電池的比例（≤ 100% 才撐得過）
                    double v = ev.Power.ActualDod;
                    return new Gauge { Now = Fmt.Pct(v), Target = "≤ 100%", Fraction = v <= 1 ? 1 : 1 / v };
                }
                case "latency":
                {
                    double v = ev.Fire.LatencyMin;
                    return new Gauge { Now = Fmt.N(v, 1) + " min", Target = "≤ " + Fmt.N(req.Value) + " min", Fraction = v <= req.Value ? 1 : req.Value / Math.Max(1e-6, v) };
                }
                default:
                    return new Gauge { Now = "—", Target = "—", Fraction = 0 };
            }
        }

        // ---------------------------------------------------------------- 組裝清單：每個零件要用哪些小零件

        /// <summary>這個零件現在的狀態：installed、carried、built（組好了還在組裝台上）、building、todo、ready（不用組裝）。</summary>
        public static string PartState(SessionState s, GameData data, string partId)
        {
            if (s.Installed.Contains(partId)) return SessionRules.HasAssembly(s, data, partId) ? "installed" : "ready";
            if (s.Held.Values.Contains(partId)) return "carried";
            if (SessionRules.BenchHolding(s, partId) >= 0) return "built";
            if (s.Benches.Any(b => b.Job != null && b.Job.PartId == partId)) return "building";
            return "todo";
        }

        /// <summary>
        /// 設計裡每個零件要用哪些小零件（名稱照筆畫／字母排，不是組裝順序：順序要在組裝台上看提示自己判斷），
        /// 還有現在組到哪裡。設計主螢幕和組裝區儀表板顯示同樣的內容。
        /// </summary>
        public static void AssemblyList(VisualElement parent, bool compact)
        {
            var s = M.S;
            var ids = SessionRules.SelectedParts(s, D);
            // 零件多（帶了兩三台儀器）：牆上的螢幕用小一級的字才排得下
            var grid = Ui.El("asm-list").Cls("asm-list--compact", compact).Cls("asm-list--many", ids.Count > 8).AddTo(parent);
            foreach (var id in ids)
            {
                var p = D.Part(id);
                string state = PartState(s, D, id);
                var card = Ui.El("asm-card", "asm-card--" + state).AddTo(grid);
                var top = Ui.Row("asm-card-top").AddTo(card);
                Ui.Text(p.Code, "man-code", "mono").AddTo(top);
                Ui.Text(PartName(p), "asm-card-name").AddTo(top);
                Ui.Spacer().AddTo(top);
                Ui.Text(Loc.T("ui.asm.state." + state), "asm-state", "asm-state--" + state).AddTo(top);
                var recipe = SessionRules.HasAssembly(s, D, id) ? D.Recipe(p) : null;
                if (recipe == null) { Ui.Text(Loc.T("ui.asm.no_recipe"), "small", "faint").AddTo(card); continue; }
                var comps = Ui.Row("asm-comps").AddTo(card);
                foreach (var c in recipe.Components.OrderBy(c => c.Id, StringComparer.Ordinal))
                    Ui.Text(Loc.T(c.Name), "asm-comp").AddTo(comps);
            }
        }

        /// <summary>組裝區儀表板：設計確定前顯示「等待中控室確定設計」，確定後是組裝清單。</summary>
        public static void Bay(VisualElement parent, bool compact)
        {
            var s = M.S;
            if (s.IsSandbox(D))
            {
                Ui.Text(Loc.T("ui.bench.sandbox"), "h3", "dim", "bay-wait").AddTo(parent);
                return;
            }
            if (!s.Confirmed)
            {
                var wait = Ui.El("bay-wait").AddTo(parent);
                Ui.Text(Loc.T("ui.bay.waiting"), "h2", "tight").AddTo(wait);
                Ui.Text(Loc.T("ui.bay.waiting_body"), "body").AddTo(wait);
                return;
            }
            int total = SessionRules.SelectedParts(s, D).Count, left = SessionRules.NotInstalled(s, D).Count;
            var head = Ui.Row("dash-head").AddTo(parent);
            Ui.Text(Loc.T("ui.bay.list"), "eyebrow", "dash-sec").AddTo(head);
            Ui.Spacer().AddTo(head);
            Ui.Text(Loc.T("ui.sel.installed_n", ("n", total - left), ("total", total)), "small", left == 0 ? "go" : "dim").AddTo(head);
            AssemblyList(parent, compact);
        }

        // ---------------------------------------------------------------- 牆上的螢幕

        /// <summary>牆上的設計主螢幕（選件階段）：現在選了什麼、儀表板。細節和操作按 E 打開。</summary>
        public static void WallMain(VisualElement body)
        {
            var s = M.S;
            Stages(body, true);
            // 設計確定了：整面列出每個零件要用哪些小零件（和組裝區儀表板一樣）；數字的細節按 E 看
            if (s.Confirmed && !s.DesignLocked(D) && !s.IsSandbox(D))
            {
                Bay(body, true);
                return;
            }
            var cols = Ui.Row("dash-wall").AddTo(body);
            var left = Ui.El("dash-wall-left").AddTo(cols);
            var right = Ui.El("dash-wall-right").AddTo(cols);
            Picks(left);
            Dashboard(right, true);
        }

        /// <summary>現在勾選的設計：每類一格（編號和名稱），加上軌道、火箭、地面站、年數。</summary>
        static void Picks(VisualElement parent)
        {
            var s = M.S;
            var d = s.Bench;
            Ui.Text(Loc.T("ui.sel.picks"), "eyebrow").AddTo(parent);
            var grid = Ui.El("pick-grid").AddTo(parent);
            void Row(string cat, IEnumerable<string> ids)
            {
                var r = Ui.Row("pick-row").AddTo(grid);
                Ui.Text(Loc.T("ui.cat." + cat), "pick-k").AddTo(r);
                bool any = false;
                foreach (var id in ids)
                {
                    var p = D.Part(id);
                    if (p == null) continue;
                    any = true;
                    Ui.Text(string.IsNullOrEmpty(p.Code) ? Loc.T(p.Name) : p.Code + " " + PartName(p), "pick-v").AddTo(r);
                }
                if (!any) Ui.Text("—", "pick-v", "tight").AddTo(r);
            }
            Row(PartCategory.Instrument, d.Instruments);
            foreach (var cat in PartCategory.Single.Where(c => c != PartCategory.GroundStation)) Row(cat, new[] { d.Get(cat) });
            var mis = Ui.Row("pick-row").AddTo(grid);
            Ui.Text(Loc.T("ui.sel.tab.mission"), "pick-k").AddTo(mis);
            Ui.Text(Loc.Orbit(d.OrbitId) + " · " + Loc.Launcher(d.LauncherId) + " · " + Loc.T(D.Part(d.Get(PartCategory.GroundStation))?.Name) + " · " + Fmt.Years(d.MissionYears), "pick-v").AddTo(mis);
            var cells = Blanks.Cells(D, s);
            int blanks = cells.Count, filled = cells.Count(c => Blanks.Filled(s, c.part, c.field) != null);
            if (blanks > 0) Ui.Text(Loc.T("ui.sel.blank_hint", ("n", filled), ("total", blanks)), "small", filled >= blanks ? "go" : "tight").AddTo(parent);
        }
    }
}
