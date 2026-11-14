using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // 任務簡報（BriefingPanel）在 BriefingPanel.cs

    // ======================================================================== 儀表板

    public sealed class DashboardPanel : FacPanel
    {
        public override string Title => Loc.T("ui.dash.title");
        public override string WideClass => "modal--dash";

        protected override void Build(VisualElement body) => DashboardView.Build(Scroll(body), false);
    }

    /// <summary>儀表板內容（面板、平板、牆上螢幕共用）。只顯示數值；學員難度加上紅黃綠。</summary>
    public static class DashboardView
    {
        public static void Build(VisualElement body, bool compact)
        {
            var m = GameSession.Mission;
            var ev = m.Dashboard;
            bool colors = m.Difficulty.DashboardColors;
            var d = m.S.Reported;
            var data = GameSession.Data;

            // 任務進度：現在在哪一步、問到的需求各自做到多少。
            // 影像牆上（compact）放不下上下兩段，改成左右兩欄：左邊進度、右邊設計數據。

            if (compact)
            {
                var cols = Ui.Row("dash-wall").AddTo(body);
                MissionProgressView.Build(Ui.El("dash-wall-left").AddTo(cols), true);
                body = Ui.El("dash-wall-right").AddTo(cols);
            }
            else MissionProgressView.Build(body, false);

            var head = Ui.Row("dash-head").AddTo(body);
            Ui.Text(Loc.T("ui.dash.design_data"), "eyebrow", "dash-sec").AddTo(head);
            Ui.Text(m.S.Reports == 0 ? Loc.T("ui.dash.no_report") : Loc.T("ui.dash.report_n", ("n", m.S.Reports)), "small", "dim").AddTo(head);
            Ui.Spacer().AddTo(head);
            if (!compact) Ui.Text(Loc.Orbit(d.OrbitId) + " · " + Loc.Launcher(d.LauncherId) + " · " + Fmt.Years(d.MissionYears), "small", "dim").AddTo(head);

            var grid = Ui.El("dash-grid").AddTo(body);
            foreach (ResourceId id in new[] { ResourceId.Mass, ResourceId.Power, ResourceId.Budget, ResourceId.Data, ResourceId.Comms, ResourceId.Time })
            {
                var r = ev[id];
                Row(grid, Loc.T("res." + id.ToString().ToLowerInvariant()), Fmt.Resource(r), Fmt.Pct(r.Margin), colors ? Status(r.Status) : null);
            }
            // 電池：放電深度（> 100% 代表撐不過一次地影）
            var p = ev.Power;
            Row(grid, Loc.T("ui.dash.battery"), Fmt.N(p.BatteryWh) + " Wh / " + Fmt.N(p.EclipseEnergyWh / System.Math.Max(1e-6, data.Constants.BatteryEfficiency)) + " Wh",
                Loc.T("ui.dash.dod", ("pct", Fmt.Pct(p.ActualDod))), colors ? (p.ActualDod > 1 ? "over" : p.ActualDod > p.AllowedDod ? "tight" : "go") : null);
            Row(grid, Loc.T("ui.dash.eclipse"), Fmt.N(ev.Orbit.EclipseMin, 1) + " min", Fmt.N(ev.Orbit.PeriodMin, 0) + " min/orbit", null);
            Row(grid, Loc.T("ui.dash.lifetime"), Fmt.Years(ev.Lifetime.Years) + " / " + Fmt.Years(d.MissionYears),
                Loc.T("ui.dash.limit." + ev.Lifetime.Limit.ToString().ToLowerInvariant()), colors ? (ev.Lifetime.Years + 1e-9 < d.MissionYears ? "over" : "go") : null);
            Row(grid, Loc.T("ui.dash.science"), Fmt.N(ev.SciencePerYear, 1) + " / yr", Loc.T("ui.dash.instruments", ("n", d.Instruments.Count)), null);
            Row(grid, Loc.T("ui.dash.return"), Fmt.Pct(ev.Data.ReturnFraction), "", colors ? (ev.Data.ReturnFraction < 0.9 ? "tight" : "go") : null);
            // 野火關卡：通報延遲（學員：≤ 時限綠、≤ 1.5 倍黃、超過紅）與偵測率
            var sc = m.Scenario;
            if (sc != null && sc.LatencyLimitMin > 0)
            {
                var f = ev.Fire;
                double lim = sc.LatencyLimitMin;
                Row(grid, Loc.T("ui.dash.latency"), Fmt.N(f.LatencyMin, 1) + " / " + Fmt.N(lim) + " min",
                    Loc.T("ui.dash.latency_x", ("wait", Fmt.N(f.WaitMin)), ("down", Fmt.N(f.DownlinkMin, 1)), ("ground", Fmt.N(f.ProcessingMin))),
                    colors ? (f.LatencyMin <= lim ? "go" : f.LatencyMin <= lim * 1.5 ? "tight" : "over") : null);
                Row(grid, Loc.T("ui.dash.detection"), Fmt.Pct(f.Detection),
                    Loc.T("ui.dash.detection_x", ("inst", Fmt.Pct(f.InstrumentDetection)), ("orbit", "×" + Fmt.N(f.OrbitFactor, 1))), null);
            }
            // 環境測試（組裝區做的，測的是整合台上的設計）
            TestView.Build(grid, true);

            if (!compact)
            {
                var installed = Ui.El("dash-parts").AddTo(body);
                Ui.Text(Loc.T("ui.dash.parts"), "eyebrow").AddTo(installed);
                var row = Ui.Row("dash-part-row").AddTo(installed);
                foreach (var id in d.AllPartIds().Where(x => data.Part(x)?.Category != PartCategory.GroundStation))
                    Ui.Text(data.Part(id)?.Code ?? id, "code-chip", "mono").AddTo(row);
                Ui.Text(Loc.T(colors ? "ui.dash.colors_on" : "ui.dash.colors_off"), "small", "faint").AddTo(body);
            }
        }

        static string Status(ResourceStatus s) => s == ResourceStatus.Over ? "over" : s == ResourceStatus.Tight ? "tight" : "go";

        static void Row(VisualElement parent, string name, string value, string extra, string cls)
        {
            var r = Ui.Row("dash-row").AddTo(parent);
            if (cls != null) Ui.El("dash-lamp", "dash-lamp--" + cls).AddTo(r);
            Ui.Text(name, "dash-k").AddTo(r);
            Ui.Spacer().AddTo(r);
            Ui.Text(value, "mono", "dash-v", cls).AddTo(r);
            Ui.Text(extra, "small", "dim", "dash-x", "mono").AddTo(r);
        }
    }

    // 工程手冊（ManualPanel、ManualIndexPanel、ManualView）在 ManualPanels.cs

    // ======================================================================== 電話

    public sealed class PhonePanel : FacPanel
    {
        public override string Title => Loc.T("ui.phone.title");
        public override string WideClass => "modal--wide";
        string _dept;
        int _line = -1;
        float _typed;
        Label _answer;
        string _answerText = "";

        protected override void Build(VisualElement body)
        {
            var wrap = Ui.Row("phone-wrap").AddTo(body);
            var left = Ui.El("phone-list").AddTo(wrap);
            foreach (var dep in D.Departments)
            {
                var d = dep;
                var b = Ui.Btn("", () => Dial(d.Id), "phone-dept").Cls("phone-dept--on", d.Id == _dept).AddTo(left);
                b.Clear();
                var dot = Ui.El("phone-dot").AddTo(b);
                if (UnityEngine.ColorUtility.TryParseHtmlString(d.Color, out var c)) dot.style.backgroundColor = c;
                var col = Ui.El().AddTo(b);
                Ui.Text(Loc.T(d.Name), "h3").AddTo(col);
                Ui.Text(Loc.T(d.Contact), "small", "dim").AddTo(col);
                int asked = M.S.Calls.Count(x => x.Department == d.Id);
                int open = D.Lines(M.Scenario, d).Count(l => GameData.LineOpen(l, M.Difficulty));
                if (asked > 0) Ui.Text(Loc.T("ui.phone.asked", ("n", asked), ("total", open)), "small", "faint").AddTo(col);
            }

            var right = Ui.El("phone-call").AddTo(wrap);
            var dep2 = _dept != null ? D.Department(_dept) : null;
            if (dep2 == null)
            {
                Ui.Text(Loc.T("ui.phone.pick"), "body").AddTo(right);
                return;
            }
            Ui.Text(Loc.T(dep2.Contact), "eyebrow").AddTo(right);
            Ui.Text("“" + Loc.T(dep2.Greeting) + "”", "phone-greet").AddTo(right);
            var qs = Ui.El("phone-qs").AddTo(right);
            // 關卡可以有自己的對話；有些題目要到某個難度才問得到（索引照完整清單，房主那邊才對得上）
            var lines = D.Lines(M.Scenario, dep2);
            for (int i = 0; i < lines.Count; i++)
            {
                int k = i;
                var line = lines[i];
                if (!GameData.LineOpen(line, M.Difficulty)) continue;
                bool asked = M.S.Calls.Any(x => x.Department == dep2.Id && x.LineId == line.Id);
                Ui.Btn(Loc.T(line.Question), () => Ask(k), "phone-q").Cls("phone-q--on", k == _line).Cls("phone-q--asked", asked).AddTo(qs);
            }
            _answer = Ui.Text("", "phone-answer").AddTo(right);
            if (_line >= 0 && _line < lines.Count)
            {
                var line = lines[_line];
                _answerText = Dialogue.Answer(D, M.S, dep2.Id, line, M.Dashboard, Loc.Lang);
                _answer.text = _answerText.Substring(0, (int)System.Math.Min(_answerText.Length, _typed));
            }
            var notes = M.Difficulty.NotesMode;
            Ui.Text(Loc.T("ui.phone.notes_" + notes), "small", "faint").AddTo(right);
        }

        void Dial(string dept)
        {
            if (dept == _dept) return;
            _dept = dept;
            _line = -1;
            Sfx.Ring();
            Refresh();
        }

        void Ask(int i)
        {
            _line = i;
            _typed = 0;
            GameSession.Send(CommandType.Call, _dept, i);
            Hud.Signal("call:" + _dept);
            Refresh();
        }

        public override void Tick(float dt)
        {
            if (_answer == null || _line < 0 || _typed >= _answerText.Length) return;
            _typed += dt * (Loc.IsZh ? 28f : 60f);
            _answer.text = _answerText.Substring(0, (int)System.Math.Min(_answerText.Length, _typed));
        }
    }

    // ======================================================================== 筆記板

    public sealed class NotesPanel : FacPanel
    {
        public override string Title => Loc.T("ui.notes.title");
        public override string WideClass => "modal--wide";
        protected override void Build(VisualElement body) => NotesView.Build(Scroll(body), false);
    }

    public static class NotesView
    {
        /// <summary>任務主管難度：玩家自己打的筆記（只存在本機）。</summary>
        public static string OwnNotes = "";

        public static void Build(VisualElement parent, bool wall)
        {
            var m = GameSession.Mission;
            var mode = m.Difficulty.NotesMode;
            var notes = Dialogue.Notes(GameSession.Data, m.S, m.Dashboard, Loc.Lang);
            if (mode == "none")
            {
                Ui.Text(Loc.T("ui.notes.none"), "small", "dim").AddTo(parent);
                if (wall) { Ui.Text(OwnNotes, "body").AddTo(parent); return; }
                var tf = new TextField { multiline = true, value = OwnNotes };
                tf.AddToClassList("notes-field");
                tf.RegisterValueChangedCallback(e => OwnNotes = e.newValue);
                parent.Add(tf);
                return;
            }
            if (notes.Count == 0) { Ui.Text(Loc.T("ui.notes.empty"), "small", "dim").AddTo(parent); return; }
            if (mode == "checklist")
            {
                Ui.Text(Loc.T("ui.notes.checklist"), "eyebrow").AddTo(parent);
                foreach (var n in notes)
                    foreach (var r in n.Requirements)
                    {
                        bool met = Requirements.IsMetPreLaunch(r, m.Dashboard, m.S.Reported, GameSession.Data, m.Difficulty);
                        var row = Ui.Row("check-row").AddTo(parent);
                        Ui.Text(met ? "✔" : "☐", met ? "go" : "tight", "check-box").AddTo(row);
                        Ui.Text(Fmt.Dept(n.Department) + "：" + Loc.T(r.Text), "body").AddTo(row);
                    }
                Ui.Text(Loc.T("ui.notes.checklist_hint"), "small", "faint").AddTo(parent);
                return;
            }
            foreach (var n in notes)
            {
                var c = Ui.El("note").AddTo(parent);
                Ui.Text(Fmt.Dept(n.Department) + " · " + n.Question, "small", "accent").AddTo(c);
                Ui.Text(n.Answer, "body").AddTo(c);
            }
        }
    }

    // ======================================================================== 軌道、火箭、地面站

    public sealed class MissionConsolePanel : FacPanel
    {
        public override string Title => Loc.T("ui.mc.title");
        public override string WideClass => "modal--wide";

        protected override void Build(VisualElement body)
        {
            var d = M.S.Reported;
            bool locked = M.S.DesignLocked(D);
            if (locked) Ui.Text(Loc.T("err.locked"), "small", "tight").AddTo(body);
            var cols = Ui.Row("mc-cols").AddTo(body);

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
                var b = Choice(orbitCol, Loc.T(o.Name), sub, id == d.OrbitId, locked, () => GameSession.Send(CommandType.SetOrbit, id));
                if (id != d.OrbitId && SessionRules.OrbitLauncherCheck(D, id, d.LauncherId) != SessionRules.Ok) b.SetEnabled(false);
            }
            var lockL = D.Launcher(d.LauncherId);
            if (lockL != null && !string.IsNullOrEmpty(lockL.OrbitLock))
                Ui.Text(Loc.T("ui.mc.rideshare_lock", ("orbit", Loc.Orbit(lockL.OrbitLock))), "small", "tight").AddTo(orbitCol);
            Ui.Text(Loc.T("ui.cat.groundstation"), "eyebrow", "mc-sub").AddTo(orbitCol);
            foreach (var g in D.PartsIn(PartCategory.GroundStation))
            {
                var id = g.Id;
                Choice(orbitCol, Loc.T(g.Name), Loc.T("ui.mc.stations", ("n", g.Stations)) + " · " + Fmt.Money(g.OpsCostMusdPerYear) + "/yr", id == d.Get(PartCategory.GroundStation), locked, () => GameSession.Send(CommandType.SetGroundStation, id));
            }

            var lCol = Ui.El("mc-colx").AddTo(cols);
            Ui.Text(Loc.T("ui.cat.launcher"), "eyebrow").AddTo(lCol);
            foreach (var l in D.LaunchersFor(M.Scenario))
            {
                var id = l.Id;
                l.CapacityKg.TryGetValue(d.OrbitId, out var cap);
                bool rideshareBlocked = SessionRules.OrbitLauncherCheck(D, d.OrbitId, id) != SessionRules.Ok;
                string sub = (rideshareBlocked ? Loc.T("err.rideshare_orbit") : cap > 0 ? Fmt.N(cap) + " kg → " + Loc.Orbit(d.OrbitId) : Loc.T("ui.mc.cannot_reach"))
                             + " · " + Fmt.Money(l.CostMusd) + " · " + Fmt.Pct(l.Reliability);
                var lb = Choice(lCol, Loc.T(l.Name), sub, id == d.LauncherId, locked, () => GameSession.Send(CommandType.SetLauncher, id));
                if (rideshareBlocked && id != d.LauncherId) lb.SetEnabled(false);
            }
            Ui.Text(Loc.T("ui.cat.years"), "eyebrow", "mc-sub").AddTo(lCol);
            var yr = Ui.Row("years-row").AddTo(lCol);
            Ui.Btn("−", () => GameSession.Send(CommandType.SetYears, "", d.MissionYears - 1), "btn-small").AddTo(yr).SetEnabled(!locked);
            Ui.Text(Fmt.Years(d.MissionYears), "h3", "mono", "years-value").AddTo(yr);
            Ui.Btn("+", () => GameSession.Send(CommandType.SetYears, "", d.MissionYears + 1), "btn-small").AddTo(yr).SetEnabled(!locked);
            Ui.Text(Loc.T("ui.mc.years_hint", ("target", Fmt.Years(M.Scenario.TargetYears))), "small", "faint").AddTo(lCol);
        }

        static Button Choice(VisualElement parent, string name, string sub, bool on, bool locked, System.Action pick)
        {
            var b = Ui.Btn("", () => { pick(); Sfx.Click(); }, "mc-choice").Cls("mc-choice--on", on).AddTo(parent);
            b.Clear();
            Ui.Text(name, "mc-name").AddTo(b);
            Ui.Text(sub, "small", "dim", "mono").AddTo(b);
            b.SetEnabled(!locked || on);
            return b;
        }
    }

    // ======================================================================== 發射

    public sealed class LaunchPanel : FacPanel
    {
        public override string Title => Loc.T("ui.launchp.title");

        protected override void Build(VisualElement body)
        {
            var s = M.S;
            bool poll = s.Multiplayer && s.Players.Count > 1;
            Ui.Text(Loc.T("ui.launchp.question"), "h2").AddTo(body);
            Ui.Text(Loc.T("ui.launchp.body"), "body").AddTo(body);

            // 發射前檢查：環境測試、回報、點名
            var gate = Ui.El("launch-gate").AddTo(body);
            var t = s.LastTest;
            bool current = EnvTests.IsCurrent(s);
            if (t == null) Gate(gate, "over", Loc.T("ui.launchp.test_none"));
            else if (!current) Gate(gate, "tight", Loc.T("ui.launchp.test_stale", ("n", t.N)));
            else
            {
                var worst = new[] { t.Worst(EnvTests.Vibration), t.Worst(EnvTests.Thermal) }.Max();
                Gate(gate, TestView.LevelClass(worst), Loc.T("ui.launchp.test_ok", ("n", t.N),
                    ("vib", TestView.LevelText(t.Worst(EnvTests.Vibration))), ("tv", TestView.LevelText(t.Worst(EnvTests.Thermal)))));
                if (worst != TestLevel.Pass) Ui.Text(Loc.T("ui.launchp.test_warn"), "small", "tight").AddTo(gate);
            }
            if (s.Reports == 0) Gate(gate, "tight", Loc.T("ui.launchp.no_report"));
            if (poll) Gate(gate, "dim", Loc.T("ui.launchp.poll_note", ("n", s.Players.Count)));

            var why = SessionRules.CanLaunch(s);
            var row = Ui.Row("modal-actions").AddTo(body);
            Ui.Btn(Loc.T("ui.launchp.cancel"), () => Close(), "btn-ghost").AddTo(row);
            Ui.Spacer().AddTo(row);
            if (why != SessionRules.Ok) Ui.Text(Loc.T(why), "small", "tight", "launch-why").AddTo(row);
            var go = Ui.Btn(Loc.T(poll ? "ui.launchp.poll" : "ui.launchp.go"), () =>
            {
                if (poll) Sfx.Alert(); else Sfx.Go();
                Hud.Signal("launch");
                GameSession.Send(CommandType.Launch);
                if (poll) Close();
            }, "btn-go", "btn-big").AddTo(row);
            go.SetEnabled(why == SessionRules.Ok);
        }

        static void Gate(VisualElement parent, string cls, string text)
        {
            var r = Ui.Row("gate-row").AddTo(parent);
            Ui.El("dash-lamp", "dash-lamp--" + (cls == "dim" ? "go" : cls)).AddTo(r);
            Ui.Text(text, "body", cls == "dim" ? "dim" : cls).AddTo(r);
        }
    }

    // ======================================================================== 失敗原因看板

    public sealed class FailuresPanel : FacPanel
    {
        public override string Title => Loc.T("ui.fail.title");
        public override string WideClass => "modal--wide";
        protected override void Build(VisualElement body) => FailureBoardView.Build(Scroll(body));
    }

    /// <summary>每一次發射的結果與原因，最新的在最上面。詳細程度依難度。</summary>
    public static class FailureBoardView
    {
        public static void Build(VisualElement parent)
        {
            var m = GameSession.Mission;
            var detail = m.Difficulty.FailureDetail;
            if (m.S.Launches.Count == 0) { Ui.Text(Loc.T("ui.fail.none"), "small", "dim").AddTo(parent); return; }
            for (int i = m.S.Launches.Count - 1; i >= 0; i--)
            {
                var l = m.S.Launches[i];
                var card = Ui.El("fail-card").Cls("fail-card--ok", l.Reached).AddTo(parent);
                var head = Ui.Row().AddTo(card);
                Ui.Text(Loc.T("ui.fail.attempt", ("n", l.Attempt)), "eyebrow").AddTo(head);
                Ui.Spacer().AddTo(head);
                Ui.Text(Loc.T(OutcomeKey(l.Outcome)), "small", l.Reached ? "go" : "over").AddTo(head);
                if (l.Reached) continue;
                if (detail == "category")
                {
                    Ui.Text(Loc.T("ui.fail.category." + Category(l)), "body").AddTo(card);
                    continue;
                }
                Ui.Text(Loc.T(l.ReasonCode), "body").AddTo(card);
                var r = SessionRules.ResultOf(l, m.S, GameSession.Data);
                var chain = r.Causes.FirstOrDefault(c => !c.Positive);
                if (chain != null)
                {
                    var row = Ui.El("chain").AddTo(card);
                    for (int k = 0; k < chain.Steps.Count; k++)
                    {
                        if (k > 0) Ui.Text("→", "chain-arrow").AddTo(row);
                        Ui.Text(Loc.Cause(chain.Steps[k]), "chain-step").AddTo(row);
                    }
                }
                if (detail == "advice" && !string.IsNullOrEmpty(r.SuggestionCode))
                    Ui.Text(Loc.T("ui.report.suggestion") + "：" + Loc.T(r.SuggestionCode), "small", "accent").AddTo(card);
            }
        }

        public static string OutcomeKey(LaunchOutcome o) => o switch
        {
            LaunchOutcome.Nominal => "ui.launch.result.nominal",
            LaunchOutcome.LowerOrbit => "ui.launch.result.lower",
            LaunchOutcome.FairingFailure => "ui.launch.result.fairing",
            LaunchOutcome.DeadOnOrbit => "ui.launch.result.dead",
            _ => "ui.launch.result.failure",
        };

        static string Category(LaunchRecord l) => l.Outcome switch
        {
            LaunchOutcome.DeadOnOrbit => "power",
            LaunchOutcome.FairingFailure => "launcher",
            _ => l.ReasonCode == "launch.anomaly" ? "launcher" : "mass",
        };
    }

    // ======================================================================== 平板（個人模式）

    public sealed class TabletPanel : FacPanel
    {
        public override string Title => Loc.T("ui.tablet.title");
        public override string WideClass => "modal--wide";
        public override bool CloseWithE => false;
        static int _tab;

        protected override void Build(VisualElement body)
        {
            Tabs(body, new[] { "ui.tablet.dashboard", "ui.manual.tab.parts", "ui.manual.tab.rules", "ui.notes.title", "ui.fail.title", "ui.test.short" }, _tab, i => { _tab = i; Refresh(); });
            var sv = Scroll(body);
            switch (_tab)
            {
                case 0: DashboardView.Build(sv, false); break;
                case 1: ManualView.BuildAllParts(sv); break;
                case 2: ManualView.Build(sv, "rules"); break;
                case 3: NotesView.Build(sv, true); break;
                case 4: FailureBoardView.Build(sv); break;
                default: TestView.Build(sv, false); break;
            }
            Ui.Text(Loc.T("ui.tablet.readonly"), "small", "faint").AddTo(body);
        }
    }
}
