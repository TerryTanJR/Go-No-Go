using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 最後評價：評等印章、五項分數（科學、壽命、效率、處室需求、營運處置）、處室需求達成、
    /// 每次發射的結果、營運每回合的判斷與處置、因果鏈與建議、對照真實任務。歷史關卡加上揭曉與 NASA 的真實解法。
    /// </summary>
    public sealed class ReportScreen : GameScreen
    {
        static MissionResult _stampedFor;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            GameSession.EnsureMission();
            var r = M.Result;
            if (r == null)
            {
                GameSession.Send(CommandType.RunTest);
                GameSession.Send(CommandType.Launch);
                r = M.Result;
            }
            var l = M.S.LastLaunch;
            var ev = Evaluator.EvaluateDesign(l.Design, D);

            TopBar(Loc.T(M.Scenario.Name));
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
            int fails = M.S.FailedLaunches;
            Ui.Text(r.Score.FirstLaunchBonus ? Loc.T("ui.report.first_launch") : Loc.T("ui.report.redesigns", ("n", fails)), "small", r.Score.FirstLaunchBonus ? "go" : "dim", "first-call").AddTo(reqPanel);

            // ---- 中欄：為什麼、營運、發射紀錄 ----
            var mid = Ui.El("report-col", "report-col--why").AddTo(wrap);
            var midScroll = new ScrollView(ScrollViewMode.Vertical).AddTo(mid);
            midScroll.AddToClassList("why-scroll");
            var mc = midScroll.contentContainer;
            if (M.IsHistorical && M.S.Launches.Count > 0) HistoryPanel(mc, r);

            var why = Ui.Panel(mc, Loc.T("ui.report.why"), out _);
            var chains = r.Causes.OrderByDescending(c => c.Impact).Take(4).ToList();
            if (chains.Count == 0) Ui.Text(Loc.T("ui.report.no_causes"), "small", "dim").AddTo(why);
            foreach (var c in chains)
            {
                var row = Ui.El("chain").Cls("chain--good", c.Positive).AddTo(why);
                for (int i = 0; i < c.Steps.Count; i++)
                {
                    if (i > 0) Ui.Text("→", "chain-arrow").AddTo(row);
                    Ui.Text(Loc.Cause(c.Steps[i]), "chain-step").AddTo(row);
                }
            }
            if (!string.IsNullOrEmpty(r.SuggestionCode))
            {
                var sug = Ui.El("suggest").AddTo(why);
                Ui.Text(Loc.T("ui.report.suggestion"), "eyebrow").AddTo(sug);
                Ui.Text(Loc.T(r.SuggestionCode), "body").AddTo(sug);
            }

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

            var cmp = Ui.Panel(right, Loc.T("ui.report.real_missions"), out _);
            foreach (var id in M.Scenario.ComparisonMissions)
            {
                var m = D.Missions.FirstOrDefault(x => x.Id == id);
                if (m == null) continue;
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
            Ui.Btn(Loc.T("ui.report.to_menu"), () => GameSession.EndMission(), "btn-ghost").AddTo(actions);
            Ui.Spacer().AddTo(actions);
            if (!M.S.Multiplayer && !M.S.Tutorial)
            {
                var sid = M.S.ScenarioId;
                var did = M.S.DifficultyId;
                Ui.Btn(Loc.T("ui.report.again"), () => { Sfx.Confirm(); GameSession.StartSolo(sid, did); }, "btn-primary", "btn-big").AddTo(actions);
            }
        }

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
