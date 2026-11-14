using System;
using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 儀表板最上面的「任務進度」：
    /// 1. 階段列：問需求 → 回報 → 環境測試 → 發射 → 營運 → 評價，標出現在在哪一步。
    /// 2. 任務目標：電話問到的每條需求，現在的數值對目標的進度條。
    ///    只列玩家問到的需求（任務主管難度不自動記錄，所以不列）；
    ///    判定（✔／✘、紅綠）只在學員難度顯示，其他難度只給數字與進度，能不能發射仍由玩家判斷。
    /// </summary>
    public static class MissionProgressView
    {
        public static void Build(VisualElement parent, bool compact)
        {
            var box = Ui.El("prog").Cls("prog--compact", compact).AddTo(parent);
            Stages(box, compact);
            Goals(box, compact);
        }

        // ---------------------------------------------------------------- 階段列

        static void Stages(VisualElement parent, bool compact)
        {
            var m = GameSession.Mission;
            var s = m.S;
            var data = GameSession.Data;
            int depts = data.Departments.Count;
            int asked = s.Calls.Select(c => c.Department).Distinct().Count();
            bool orbit = s.Launches.Any(l => l.Reached);
            int turns = m.Plan?.Turns ?? m.Difficulty.Turns;

            var steps = new List<(string key, bool done, string sub)>
            {
                ("calls", asked >= depts, Loc.T("ui.prog.calls_n", ("n", asked), ("total", depts))),
                ("procure", s.Orders.Count > 0, s.Orders.Count == 0 ? Loc.T("ui.prog.not_yet") : Loc.T("ui.prog.orders_n", ("n", s.Orders.Count))),
                ("launch", orbit, s.Launches.Count == 0 ? Loc.T("ui.prog.not_yet")
                    : orbit ? Loc.T("ui.prog.in_orbit") : Loc.T("ui.prog.failed_n", ("n", s.FailedLaunches))),
                ("ops", s.Phase == SessionPhase.Report, s.Phase == SessionPhase.Ops ? Loc.T("ui.ops.turn", ("n", s.OpsTurn + 1), ("total", turns))
                    : s.Phase == SessionPhase.Report ? Loc.T("ui.prog.done") : Loc.T("ui.prog.turns_n", ("n", turns))),
                ("eval", false, ""),
            };

            // 現在這一步：設計階段是第一個還沒完成的（問需求沒問完不擋後面，所以從回報開始找），其他階段照階段
            int current;
            if (s.Phase == SessionPhase.Launch) current = 2;
            else if (s.Phase == SessionPhase.Ops) current = 3;
            else if (s.Phase == SessionPhase.Report) current = 4;
            else if (s.Orders.Count == 0 && asked == 0) current = 0;
            else current = s.Orders.Count == 0 ? 1 : 2;

            var row = Ui.Row("prog-stages").AddTo(parent);
            for (int i = 0; i < steps.Count; i++)
            {
                var (key, done, sub) = steps[i];
                if (i > 0) Ui.El("prog-link").Cls("prog-link--done", i <= current).AddTo(row);
                var st = Ui.El("prog-stage").Cls("prog-stage--done", done && i != current).Cls("prog-stage--now", i == current).AddTo(row);
                var top = Ui.Row("prog-stage-top").AddTo(st);
                Ui.Text(done && i != current ? "✔" : (i + 1).ToString(), "prog-dot", "mono").AddTo(top);
                Ui.Text(Loc.T("ui.prog.stage." + key), "prog-stage-name").AddTo(top);
                if (!compact && !string.IsNullOrEmpty(sub)) Ui.Text(sub, "prog-stage-sub").AddTo(st);
            }
        }

        // ---------------------------------------------------------------- 任務目標

        static void Goals(VisualElement parent, bool compact)
        {
            var m = GameSession.Mission;
            var data = GameSession.Data;
            var diff = m.Difficulty;
            bool verdict = diff.DashboardColors;

            var head = Ui.Row("prog-goals-head").AddTo(parent);
            Ui.Text(Loc.T("ui.prog.goals"), "eyebrow").AddTo(head);
            Ui.Spacer().AddTo(head);

            if (diff.NotesMode == "none")
            {
                Ui.Text(Loc.T("ui.prog.goals_none"), "small", "dim").AddTo(head);
                return;
            }

            // 問到的需求（不重複），依處室順序
            var heard = new List<(string dept, RequirementDef req)>();
            foreach (var n in Dialogue.Notes(data, m.S, m.Dashboard, Loc.Lang))
                foreach (var r in n.Requirements)
                    if (heard.All(h => h.req.Id != r.Id)) heard.Add((n.Department, r));
            var active = Departments.Active(m.Scenario, diff).Count;

            int met = heard.Count(h => Requirements.IsMetPreLaunch(h.req, m.Dashboard, m.S.Reported, data, diff));
            Ui.Text(verdict ? Loc.T("ui.prog.goals_met", ("met", met), ("n", heard.Count), ("total", active))
                            : Loc.T("ui.prog.goals_heard", ("n", heard.Count), ("total", active)), "small", "dim").AddTo(head);

            if (heard.Count == 0)
            {
                Ui.Text(Loc.T("ui.prog.goals_empty"), "small", "faint", "prog-empty").AddTo(parent);
                return;
            }

            // 船上的實際數字在組裝區的大螢幕：中控室只看得到目標（學員難度例外，直接顯示現在做到多少）
            var list = Ui.El("prog-goals").AddTo(parent);
            foreach (var (dept, req) in heard)
            {
                var g = Measure(req, m, data, diff);
                if (!verdict) { g.Now = ""; g.Target = Target(req, data, diff); }
                bool ok = Requirements.IsMetPreLaunch(req, m.Dashboard, m.S.Reported, data, diff);
                string state = verdict ? (ok ? "go" : "over") : "neutral";

                var row = Ui.El("prog-goal", "prog-goal--" + state).AddTo(list);
                var top = Ui.Row("prog-goal-top").AddTo(row);
                var dot = Ui.El("prog-dept").AddTo(top);
                var dd = data.Department(dept);
                if (dd != null && UnityEngine.ColorUtility.TryParseHtmlString(dd.Color, out var c)) dot.style.backgroundColor = c;
                Ui.Text(Loc.T("ui.prog.type." + req.Type), "prog-goal-name").AddTo(top);
                if (!compact) Ui.Text(Fmt.Dept(dept), "small", "faint", "prog-goal-dept").AddTo(top);
                Ui.Spacer().AddTo(top);
                if (verdict) Ui.Text(g.Now, "prog-goal-now", "mono").AddTo(top);
                Ui.Text((verdict ? " / " : "") + g.Target, "prog-goal-target", "mono").AddTo(top);
                if (verdict)
                {
                    Ui.Text(ok ? "✔" : "✘", "prog-goal-mark", ok ? "go" : "over").AddTo(top);
                    var track = Ui.El("prog-track").AddTo(row);
                    var fill = Ui.El("prog-fill", "prog-fill--" + state).AddTo(track);
                    fill.style.width = Length.Percent((float)(Math.Clamp(g.Fraction, 0, 1) * 100));
                }
                if (!compact) Ui.Text(Loc.T(req.Text), "small", "dim", "prog-goal-text").AddTo(row);
            }
        }

        struct Gauge
        {
            public string Now, Target;
            public double Fraction;
        }

        /// <summary>只有目標（不看船）：給工程師、任務主管難度的中控室。</summary>
        static string Target(RequirementDef req, GameData data, DifficultyDef diff) => req.Type switch
        {
            "budget_reserve" => req.Value > 0 ? "≥ " + Fmt.Pct(req.Value) : Loc.T("ui.prog.within_budget"),
            "lifetime_target" => Fmt.Years(GameSession.Mission.Scenario?.TargetYears ?? 0),
            "mass_power_margin" => "≥ " + Fmt.Pct(req.Value > 0 ? req.Value : diff.RequiredMargin),
            "science_threshold" => "≥ " + Fmt.N(req.Value),
            "instrument_required" => data.Part(req.PartId)?.Code ?? req.PartId,
            "instrument_any" => string.Join(" / ", req.PartIds.Select(id => data.Part(id)?.Code ?? id)),
            "orbit_required" => Loc.Orbit(req.OrbitId),
            "battery_eclipse" => "≤ 100%",
            "latency" => "≤ " + Fmt.N(req.Value) + " min",
            "data_return" => "≥ " + Fmt.Pct(req.Value),
            _ => "—",
        };

        /// <summary>需求目前的數值和目標（和 Requirements.IsMetPreLaunch 用同一套算法）。</summary>
        static Gauge Measure(RequirementDef req, MissionSession m, GameData data, DifficultyDef diff)
        {
            var ev = m.Dashboard;
            var d = m.S.Reported;
            var sc = m.Scenario;
            switch (req.Type)
            {
                case "budget_reserve":
                {
                    double v = ev[ResourceId.Budget].Margin;
                    return new Gauge { Now = Fmt.Pct(v), Target = "≥ " + Fmt.Pct(req.Value), Fraction = req.Value > 0 ? v / req.Value : (v >= 0 ? 1 : 0) };
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
                    string code = data.Part(req.PartId)?.Code ?? req.PartId;
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
                    string codes = string.Join(" / ", req.PartIds.Select(id => data.Part(id)?.Code ?? id));
                    return new Gauge { Now = on != null ? (data.Part(on)?.Code ?? on) : Loc.T("ui.prog.missing"), Target = codes, Fraction = on != null ? 1 : 0 };
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
    }
}
