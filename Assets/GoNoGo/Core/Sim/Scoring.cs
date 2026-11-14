using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    public sealed class RequirementResult
    {
        public Department Department { get; set; }
        public RequirementDef Requirement { get; set; }
        public bool Met { get; set; }
    }

    public sealed class Score
    {
        public double Science { get; set; }
        public double Lifetime { get; set; }
        public double Efficiency { get; set; }
        /// <summary>處室需求：80 × 達成比例 + 第一次發射就成功入軌加 20。</summary>
        public double Departments { get; set; }
        /// <summary>營運處置：每回合「判斷對」50% +「處置步驟完成比例」50%，各回合平均。</summary>
        public double Ops { get; set; }
        public double Total { get; set; }
        /// <summary>S、A、B、C 或 FAIL。</summary>
        public string Grade { get; set; } = "C";
        public bool FirstLaunchBonus { get; set; }
        public List<RequirementResult> Requirements { get; set; } = new List<RequirementResult>();
    }

    /// <summary>任務評價的五個分項與總評。</summary>
    public static class Scoring
    {
        public const double WScience = 0.35, WLifetime = 0.20, WEfficiency = 0.15, WDepartments = 0.15, WOps = 0.15;

        public static Score Compute(Design design, GameData data, Evaluation ev, MissionResult r, MissionContext ctx)
        {
            var scenario = data.Scenario(design.ScenarioId);
            var diff = data.Difficulty(ctx.DifficultyId) ?? data.Difficulties.FirstOrDefault() ?? new DifficultyDef();
            var s = new Score();

            s.Science = ScienceScore(r.ScienceTotal, scenario);
            s.Efficiency = EfficiencyScore(ev);
            s.Lifetime = r.ReachedOrbit ? Clamp(100.0 * Math.Min(1.0, r.ActualYears / Math.Max(1e-6, r.TargetYears))) : 0;

            foreach (var a in ctx.Requirements ?? new List<ActiveRequirement>())
            {
                s.Requirements.Add(new RequirementResult
                {
                    Department = a.Department,
                    Requirement = a.Requirement,
                    Met = Requirements.IsMetAfterMission(a.Requirement, ev, design, r, data, diff, s.Science),
                });
            }
            int n = Math.Max(1, s.Requirements.Count);
            // 學員難度：第一次發射失敗不算
            int allowed = diff.FreeFirstFailure ? 1 : 0;
            s.FirstLaunchBonus = r.ReachedOrbit && ctx.FailedLaunchesBefore <= allowed;
            s.Departments = Clamp(80.0 * s.Requirements.Count(x => x.Met) / n + (s.FirstLaunchBonus ? 20 : 0));
            s.Ops = OpsScore(r);

            if (!r.ReachedOrbit)
            {
                s.Total = 0;
                s.Grade = "FAIL";
                return s;
            }
            s.Total = Math.Round(WScience * s.Science + WLifetime * s.Lifetime + WEfficiency * s.Efficiency
                                 + WDepartments * s.Departments + WOps * s.Ops, 1);
            s.Grade = s.Science < 15 ? "FAIL"
                : s.Total >= 90 ? "S"
                : s.Total >= 78 ? "A"
                : s.Total >= 62 ? "B"
                : "C";
            return s;
        }

        /// <summary>營運處置分數；沒有遇到任何事件（任務提早結束）時，入軌就算滿分。</summary>
        public static double OpsScore(MissionResult r)
        {
            if (!r.ReachedOrbit) return 0;
            if (r.Events.Count == 0) return 100;
            return Clamp(r.Events.Average(e => (e.JudgedCorrect ? 50.0 : 0.0) + 50.0 * e.StepsFraction));
        }

        /// <summary>
        /// 科學分數 = 100 · √(科學點數 / 參考值)，上限 100。
        /// 開根號讓小任務也拿得到分數，大任務則邊際效益遞減（多帶一台儀器不會讓分數翻倍）。
        /// </summary>
        public static double ScienceScore(double scienceTotal, ScenarioDef scenario)
        {
            double reference = scenario != null && scenario.ScienceReference > 0 ? scenario.ScienceReference : 100;
            return Clamp(100.0 * Math.Sqrt(Math.Max(0, scienceTotal) / reference));
        }

        /// <summary>
        /// 資源效率：預算留 5% 以上的準備金就是好習慣（留越多越好，25% 封頂）；
        /// 質量與電力餘裕 10–50% 最好，太緊有風險，太大代表設計過頭。
        /// </summary>
        public static double EfficiencyScore(Evaluation ev)
        {
            double mb = ev[ResourceId.Budget].Margin;
            double budgetScore = mb < 0 ? 0
                : mb < 0.05 ? 40 + mb * 800
                : Math.Min(100, 80 + (mb - 0.05) * 100);
            double designMargin = (Math.Max(0, ev[ResourceId.Mass].Margin) + Math.Max(0, ev[ResourceId.Power].Margin)) / 2;
            double marginScore = designMargin < 0.10 ? 50 + designMargin * 500
                : designMargin <= 0.50 ? 100
                : 100 - (designMargin - 0.50) * 80;
            return Clamp(0.6 * Clamp(budgetScore) + 0.4 * Clamp(marginScore));
        }

        static double Clamp(double v) => Math.Max(0, Math.Min(100, v));
    }
}
