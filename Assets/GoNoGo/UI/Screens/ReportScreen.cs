using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 任務報告：評等印章、四項分數、因果鏈（為什麼是這個結果）、建議、和上次比、對照真實任務。
    /// 歷史關卡：第一次（重演）之後揭曉真實歷史與 NASA 的解法；改寫之後並排比較。
    /// </summary>
    public sealed class ReportScreen : GameScreen
    {
        static MissionResult _stampedFor;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            var r = S.Result;
            if (r == null) { S.Launch(); r = S.Result; }

            if (S.IsHistorical)
            {
                if (S.History == HistoryStage.Replay) S.SetHistoryStage(HistoryStage.Reveal);
                else if (S.History == HistoryStage.Rewrite) S.SetHistoryStage(HistoryStage.Compare);
            }

            TopBar(Loc.T(S.Scenario.Name));
            var wrap = Ui.El("report-wrap").AddTo(Root);

            // ---- 左欄：印章與分數 ----
            var left = Ui.El("report-col", "report-col--score").AddTo(wrap);
            var scorePanel = Ui.Panel(left, Loc.T("ui.report.title"), out _);
            var stampRow = Ui.Row("stamp-row").AddTo(scorePanel);
            string grade = r.Score.Grade;
            var stamp = Ui.Text(grade, "stamp", "stamp--hidden").AddTo(stampRow);
            stamp.EnableInClassList("stamp--fail", grade == "FAIL");
            stamp.EnableInClassList("stamp--b", grade == "B");
            stamp.EnableInClassList("stamp--c", grade == "C");
            bool animate = !ReferenceEquals(_stampedFor, r);
            _stampedFor = r;
            if (animate)
            {
                stamp.schedule.Execute(() => { stamp.RemoveFromClassList("stamp--hidden"); Sfx.Stamp(); }).StartingIn(500);
            }
            else stamp.RemoveFromClassList("stamp--hidden");

            var totals = Ui.El("total-box").AddTo(stampRow);
            Ui.Text(Loc.T("ui.report.total"), "small", "dim").AddTo(totals);
            Ui.Text(Fmt.N(r.Score.Total, 1), "h1", "mono", "total-num").AddTo(totals);
            if (S.PreviousResult != null)
            {
                double d = r.Score.Total - S.PreviousResult.Score.Total;
                Ui.Text(Loc.T("ui.report.vs_last", ("d", (object)((d >= 0 ? "+" : "") + Fmt.N(d, 1)))), "small", d >= 0 ? "go" : "over").AddTo(totals);
            }
            Ui.Text(Loc.T("ui.report.grade." + grade.ToLowerInvariant()), "small", "dim", "grade-desc").AddTo(scorePanel);

            ScoreRow(scorePanel, "ui.report.science", r.Score.Science, Scoring.WScience, animate);
            ScoreRow(scorePanel, "ui.report.efficiency", r.Score.Efficiency, Scoring.WEfficiency, animate);
            ScoreRow(scorePanel, "ui.report.lifetime", r.Score.Lifetime, Scoring.WLifetime, animate);
            ScoreRow(scorePanel, "ui.report.teamwork", r.Score.Teamwork, Scoring.WTeamwork, animate);

            var reqPanel = Ui.Panel(left, Loc.T("ui.report.requirements"), out _);
            foreach (var rr in r.Score.Requirements)
            {
                var card = Ui.El("req-card").Cls("req-card--met", rr.Met).AddTo(reqPanel);
                var head = Ui.Row().AddTo(card);
                Ui.Text(Fmt.Role(rr.Role), "eyebrow").AddTo(head);
                Ui.Spacer().AddTo(head);
                Ui.Text(Loc.T(rr.Met ? "ui.report.met" : "ui.report.unmet"), "small", rr.Met ? "go" : "tight").AddTo(head);
                Ui.Text(Loc.T(rr.Requirement.Text), "small").AddTo(card);
            }
            if (S.FirstCallAllGo) Ui.Text(Loc.T("ui.report.first_call"), "small", "go", "first-call").AddTo(reqPanel);

            // ---- 中欄：為什麼 ----
            var mid = Ui.El("report-col", "report-col--why").AddTo(wrap);
            if (S.IsHistorical && (S.History == HistoryStage.Reveal || S.History == HistoryStage.Compare))
                HistoryPanel(mid);

            var why = Ui.Panel(mid, Loc.T("ui.report.why"), out _);
            var whyScroll = new ScrollView(ScrollViewMode.Vertical).AddTo(why);
            whyScroll.AddToClassList("why-scroll");
            var chains = r.Causes.OrderByDescending(c => c.Impact).Take(5).ToList();
            if (chains.Count == 0) Ui.Text(Loc.T("ui.report.no_causes"), "small", "dim").AddTo(whyScroll.contentContainer);
            foreach (var c in chains)
            {
                var row = Ui.El("chain").Cls("chain--good", c.Positive).AddTo(whyScroll.contentContainer);
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

            // ---- 右欄：對照 ----
            var right = Ui.El("report-col", "report-col--compare").AddTo(wrap);
            var you = Ui.Panel(right, Loc.T("ui.report.your_mission"), out _);
            Fact(you, "ui.report.f.launch", Loc.T(r.Launch.ReasonCode));
            Fact(you, "ui.report.f.years", Fmt.Years(r.ActualYears) + " / " + Fmt.Years(r.TargetYears));
            Fact(you, "ui.report.f.data", Fmt.N(r.DataReturnedGb / 1000, 1) + " TB (" + Fmt.Pct(r.ReturnFraction) + ")");
            Fact(you, "ui.report.f.cost", Fmt.Money(S.Evaluation.Cost.TotalMusd) + " / " + Fmt.Money(S.Evaluation.Cost.BudgetMusd));
            Fact(you, "ui.report.f.mass", Fmt.N(S.Evaluation.Mass.WetKg) + " kg");

            var cmp = Ui.Panel(right, Loc.T("ui.report.real_missions"), out _);
            foreach (var id in S.Scenario.ComparisonMissions)
            {
                var m = S.Data.Missions.FirstOrDefault(x => x.Id == id);
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
            Ui.Btn(Loc.T("ui.report.new_mission"), () => SceneFlow.Go(SceneFlow.Boot), "btn-ghost").AddTo(actions);
            Ui.Spacer().AddTo(actions);
            string again = S.IsHistorical && S.History == HistoryStage.Reveal ? "ui.report.rewrite" : "ui.report.redesign";
            Ui.Btn(Loc.T(again), Redesign, "btn-primary", "btn-big").AddTo(actions);
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

        void HistoryPanel(VisualElement parent)
        {
            var h = S.Scenario.Historical;
            var body = Ui.Panel(parent, Loc.T("ui.history.reveal") + " · " + h.Year, out _, "history-card");
            Ui.Text(Loc.T(h.Reveal), "body").AddTo(body);
            Ui.Text(Loc.T("ui.history.real_fix"), "eyebrow", "history-sub").AddTo(body);
            Ui.Text(Loc.T(h.RealFix), "body").AddTo(body);
            if (S.History == HistoryStage.Compare)
            {
                bool better = S.Result.ReachedOrbit;
                Ui.Text(Loc.T(better ? "ui.history.you_rewrote" : "ui.history.still_failed"), "h3", better ? "go" : "over", "history-verdict").AddTo(body);
            }
            var src = Ui.El("history-sources").AddTo(body);
            Ui.Text(Loc.T("ui.design.source"), "small", "faint").AddTo(src);
            foreach (var s in h.Sources) Ui.Text("• " + s.Title, "small", "faint").AddTo(src);
        }

        static void Redesign()
        {
            Sfx.Confirm();
            S.Redesign();
            SceneFlow.Go(SceneFlow.DesignRoom);
        }
    }
}
