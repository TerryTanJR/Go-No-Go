using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 最後評價：評等印章、五項分數（科學、壽命、效率、處室需求、營運處置）、處室需求達成、
    /// 每次發射的結果、營運每回合的判斷與處置、對照真實任務。歷史關卡加上揭曉與 NASA 的真實解法。
    /// </summary>
    public sealed class ReportScreen : GameScreen
    {
        static MissionResult _stampedFor;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            Sfx.SetMusic(Sfx.Music.Menu);
            GameSession.EnsureMission();
            var r = M.Result;
            if (r == null)
            {
                GameSession.Send(CommandType.Launch);
                r = M.Result;
            }
            var l = M.S.LastLaunch;
            // 用這一局的難度評估：預算倍率跟著難度（沒傳的話一律用預設難度的預算）
            var ev = Evaluator.EvaluateDesign(l.Design, D, M.S.DifficultyId, false);

            TopBar(Loc.T(M.Scenario.Name), showLang: false); // 語言在 Tab 選單的基本設定
            var wrap = Ui.El("report-wrap").AddTo(Root);

            // ---- 左欄：印章、分數、處室需求 ----
            var left = Ui.El("report-col", "report-col--score").AddTo(wrap);
            var leftScroll = new ScrollView(ScrollViewMode.Vertical).AddTo(left);
            leftScroll.AddToClassList("why-scroll");
            var scorePanel = Ui.Panel(leftScroll.contentContainer, Loc.T("ui.report.title"), out _);
            var stampRow = Ui.Row("stamp-row").AddTo(scorePanel);
            string grade = r.Score.Grade;
            var stamp = Ui.Text(grade, "stamp", "stamp--hidden").AddTo(stampRow);
            stamp.EnableInClassList("stamp--fail", grade == "FAIL");
            stamp.EnableInClassList("stamp--b", grade == "B");
            stamp.EnableInClassList("stamp--c", grade == "C");
            bool animate = !ReferenceEquals(_stampedFor, r) && (_stampedFor == null || _stampedFor.Score.Total != r.Score.Total);
            _stampedFor = r;
            if (animate) stamp.schedule.Execute(() => { stamp.RemoveFromClassList("stamp--hidden"); Sfx.Stamp(); }).StartingIn(500);
            else stamp.RemoveFromClassList("stamp--hidden");

            var totals = Ui.El("total-box").AddTo(stampRow);
            Ui.Text(Loc.T("ui.report.total"), "small", "dim").AddTo(totals);
            Ui.Text(Fmt.N(r.Score.Total, 1), "h1", "mono", "total-num").AddTo(totals);
            Ui.Text(Loc.T("ui.report.grade." + grade.ToLowerInvariant()), "small", "dim", "grade-desc").AddTo(scorePanel);

            ScoreRow(scorePanel, "ui.report.science", r.Score.Science, Scoring.WScience, animate);
            ScoreRow(scorePanel, "ui.report.lifetime", r.Score.Lifetime, Scoring.WLifetime, animate);
            ScoreRow(scorePanel, "ui.report.efficiency", r.Score.Efficiency, Scoring.WEfficiency, animate);
            ScoreRow(scorePanel, "ui.report.departments", r.Score.Departments, Scoring.WDepartments, animate);
            ScoreRow(scorePanel, "ui.report.ops", r.Score.Ops, Scoring.WOps, animate);

            var reqPanel = Ui.Panel(leftScroll.contentContainer, Loc.T("ui.report.requirements") + "  " + r.Score.Requirements.Count(x => x.Met) + " / " + r.Score.Requirements.Count, out _);
            foreach (var rr in r.Score.Requirements)
            {
                var card = Ui.El("req-card").Cls("req-card--met", rr.Met).AddTo(reqPanel);
                var head = Ui.Row().AddTo(card);
                Ui.Text(Fmt.Dept(rr.Department), "eyebrow").AddTo(head);
                Ui.Spacer().AddTo(head);
                Ui.Text(Loc.T(rr.Met ? "ui.report.met" : "ui.report.unmet"), "small", rr.Met ? "go" : "tight").AddTo(head);
                Ui.Text(Loc.T(rr.Requirement.Text), "small").AddTo(card);
            }
            int fails = SessionRules.FailedBeforeForScore(M.S, D);   // 歷史關卡的重演不算玩家的失敗
            // 有加分但其實失敗過（學員、沙盒第一次失敗不算）：照實寫，不要說「第一次就成功」
            string firstKey = !r.Score.FirstLaunchBonus ? "ui.report.redesigns" : fails > 0 ? "ui.report.first_free" : "ui.report.first_launch";
            Ui.Text(Loc.T(firstKey, ("n", fails)), "small", r.Score.FirstLaunchBonus ? "go" : "dim", "first-call").AddTo(reqPanel);

            // ---- 中欄：營運、發射紀錄 ----
            var mid = Ui.El("report-col", "report-col--why").AddTo(wrap);
            var midScroll = new ScrollView(ScrollViewMode.Vertical).AddTo(mid);
            midScroll.AddToClassList("why-scroll");
            var mc = midScroll.contentContainer;
            if (M.IsHistorical && M.S.Launches.Count > 0) HistoryPanel(mc, r);

            if (r.Events.Count > 0)
            {
                var ops = Ui.Panel(mc, Loc.T("ui.report.ops_log"), out _);
                for (int i = 0; i < r.Events.Count; i++)
                {
                    var o = r.Events[i];
                    var row = Ui.Row("ops-log-row").AddTo(ops);
                    Ui.Text((i + 1) + ".", "small", "faint", "mono").AddTo(row);
                    Ui.Text(Loc.T("event.title." + OpsEvents.Key(o.Event.Kind)), "small").AddTo(row);
                    Ui.Spacer().AddTo(row);
                    Ui.Text(Loc.T(o.JudgedCorrect ? "ui.report.judged_ok" : "ui.report.judged_wrong"), "small", o.JudgedCorrect ? "go" : "over").AddTo(row);
                    Ui.Text(o.StepsDone + "/" + o.StepsRequired, "small", "mono", o.StepsDone == o.StepsRequired ? "go" : "tight").AddTo(row);
                }
            }

            var launches = Ui.Panel(mc, Loc.T("ui.report.launches"), out _);
            FailureBoardView.Build(launches);

            // ---- 右欄：對照 ----
            var rightCol = Ui.El("report-col", "report-col--compare").AddTo(wrap);
            // 和左、中欄一樣可以捲動：字體放大時對照卡片會變長
            var rightScroll = new ScrollView(ScrollViewMode.Vertical).AddTo(rightCol);
            rightScroll.AddToClassList("why-scroll");
            var right = rightScroll.contentContainer;
            var you = Ui.Panel(right, Loc.T("ui.report.your_mission"), out _);
            Fact(you, "ui.report.f.launch", Loc.T(r.Launch.ReasonCode));
            Fact(you, "ui.report.f.years", Fmt.Years(r.ActualYears) + " / " + Fmt.Years(r.TargetYears));
            Fact(you, "ui.report.f.data", Fmt.N(r.DataReturnedGb / 1000, 1) + " TB (" + Fmt.Pct(r.ReturnFraction) + ")");
            Fact(you, "ui.report.f.cost", Fmt.Money(ev.Cost.TotalMusd) + " / " + Fmt.Money(ev.Cost.BudgetMusd));
            Fact(you, "ui.report.f.mass", Fmt.N(ev.Mass.WetKg) + " kg");
            // 這一局收進圖鑑的知識卡（組裝、安裝零件時收的）
            int cards = Codex.NewThisMission.Count;
            if (cards > 0) Fact(you, "ui.report.f.cards", Loc.T("ui.report.cards", ("n", cards), ("have", Codex.Count(D)), ("total", Codex.Total(D))));

            // 沒有對照任務的關卡（月球、野火）不放空的面板
            var missions = M.Scenario.ComparisonMissions.Select(id => D.Missions.FirstOrDefault(x => x.Id == id)).Where(x => x != null).ToList();
            var cmp = missions.Count > 0 ? Ui.Panel(right, Loc.T("ui.report.real_missions"), out _) : null;
            foreach (var m in missions)
            {
                var card = Ui.El("mission-card").AddTo(cmp);
                var head = Ui.Row().AddTo(card);
                Ui.Text(Loc.T(m.Name), "h3").AddTo(head);
                Ui.Spacer().AddTo(head);
                Ui.Text(m.LaunchYear.ToString(), "small", "faint", "mono").AddTo(head);
                var stats = Ui.Row("part-stats").AddTo(card);
                Ui.Text(Fmt.N(m.MassKg) + " kg", "part-stat", "mono").AddTo(stats);
                Ui.Text(Fmt.N(m.PowerW) + " W", "part-stat", "mono").AddTo(stats);
                Ui.Text(Fmt.Money(m.CostMusd), "part-stat", "mono").AddTo(stats);
                if (m.LifetimeYears > 0) Ui.Text(Fmt.Years(m.LifetimeYears), "part-stat", "mono").AddTo(stats);
                Ui.Text(Loc.T(m.Lesson), "small", "dim").AddTo(card);
            }

            var actions = Ui.Row("report-actions").AddTo(Root);
            Ui.Btn(Loc.T("ui.report.to_menu"), () => GameSession.EndMission(), "btn-big").AddTo(actions);
            // 儲存、分享這一份設計（設計圖庫在主選單，可以拿來比較、在沙盒打開）
            if (!M.S.Tutorial)
            {
                string code = DesignCode.Encode(l.Design, D, "");
                bool saved = ReferenceEquals(_savedFor, r);
                var save = Ui.Btn(Loc.T(saved ? "ui.report.saved" : "ui.report.save"), null, "btn-big", "report-save").AddTo(actions);
                save.SetEnabled(!saved);
                save.clicked += () =>
                {
                    var s = DesignsView.SaveFlown();
                    _savedFor = r;
                    Sfx.Confirm();
                    UIRoot.Toast(Loc.T("ui.designs.saved", ("name", s.name)));
                    save.text = Loc.T("ui.report.saved");
                    save.SetEnabled(false);
                };
                Button share = null;
                share = Ui.Btn(Loc.T("ui.report.share"), () =>
                {
                    ClipboardUtil.Copy(code);
                    Sfx.Confirm();
                    UIRoot.Toast(Loc.T("ui.report.shared"), 4f);
                    share.text = Loc.T("ui.report.share_done");
                }, "btn-big", "report-save").AddTo(actions);
            }
            Ui.Spacer().AddTo(actions);
            if (!M.S.Multiplayer && !M.S.Tutorial)
            {
                var sid = M.S.ScenarioId;
                var did = M.S.DifficultyId;
                bool sandbox = M.Scenario != null && M.Scenario.Sandbox;
                string again = sandbox ? DesignCode.Encode(l.Design, D, "") : null;
                // 沙盒：帶著這一份設計回去接著改；一般任務：從頭再玩一次
                Ui.Btn(Loc.T(sandbox ? "ui.report.again_sandbox" : "ui.report.again"), () =>
                {
                    Sfx.Confirm();
                    if (sandbox) GameSession.StartSandbox(again);
                    else GameSession.StartSolo(sid, did);
                }, "btn-primary", "btn-big").AddTo(actions);
            }
        }

        /// <summary>這一局的設計已經存進圖庫（按鈕變成「已儲存」）。</summary>
        static MissionResult _savedFor;

        static void ScoreRow(VisualElement parent, string key, double value, double weight, bool animate)
        {
            var row = Ui.Row("score-row").AddTo(parent);
            Ui.Text(Loc.T(key) + $"  ×{weight:0.00}", "score-name").AddTo(row);
            var track = Ui.El("score-track").AddTo(row);
            var fill = Ui.El("score-fill").AddTo(track);
            Ui.Text(Fmt.N(value), "score-num", "mono").AddTo(row);
            float pct = (float)System.Math.Max(0, System.Math.Min(100, value));
            if (animate) fill.schedule.Execute(() => fill.style.width = Length.Percent(pct)).StartingIn(700);
            else fill.style.width = Length.Percent(pct);
        }

        static void Fact(VisualElement parent, string key, string value)
        {
            var row = Ui.Row("fact-row").AddTo(parent);
            Ui.Text(Loc.T(key), "small", "dim", "fact-k").AddTo(row);
            Ui.Text(value, "small", "fact-v").AddTo(row);
        }

        void HistoryPanel(VisualElement parent, MissionResult r)
        {
            var h = M.Scenario.Historical;
            var body = Ui.Panel(parent, Loc.T("ui.history.reveal") + " · " + h.Year, out _, "history-card");
            Ui.Text(Loc.T(h.Reveal), "body").AddTo(body);
            Ui.Text(Loc.T("ui.history.real_fix"), "eyebrow", "history-sub").AddTo(body);
            Ui.Text(Loc.T(h.RealFix), "body").AddTo(body);
            Ui.Text(Loc.T(r.ReachedOrbit ? "ui.history.you_rewrote" : "ui.history.still_failed"), "h3", r.ReachedOrbit ? "go" : "over", "history-verdict").AddTo(body);
            var src = Ui.El("history-sources").AddTo(body);
            Ui.Text(Loc.T("ui.design.source"), "small", "faint").AddTo(src);
            foreach (var s in h.Sources) Ui.Text("• " + s.Title, "small", "faint").AddTo(src);
        }
    }
}
