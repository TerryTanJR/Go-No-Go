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
        /// <summary>任務需求：80 × 達成比例 + 第一次發射就成功入軌加 20。</summary>
        public double Needs { get; set; }
        /// <summary>營運處置：每回合「判斷對」50% +「處置步驟完成比例」50% − 多餘操作扣分，各回合平均。</summary>
        public double Ops { get; set; }
        public double Total { get; set; }
        /// <summary>S、A、B、C 或 FAIL。</summary>
        public string Grade { get; set; } = "C";
        /// <summary>這一局的科學門檻（任務需求的 science_threshold；沒有就用 <see cref="Scoring.DefaultScienceThreshold"/>）。</summary>
        public double ScienceThreshold { get; set; }
        /// <summary>總分夠高，但科學分沒達門檻，評等被壓到 C。</summary>
        public bool GradeCapped { get; set; }
        /// <summary>科學分不到門檻的一半而判 FAIL（入軌了，但成果不算數）。</summary>
        public bool ScienceFail { get; set; }
        public bool FirstLaunchBonus { get; set; }
        public List<RequirementResult> Requirements { get; set; } = new List<RequirementResult>();
    }

    /// <summary>任務評價的五個分項與總評。</summary>
    public static class Scoring
    {
        public const double WScience = 0.35, WLifetime = 0.20, WEfficiency = 0.15, WNeeds = 0.15, WOps = 0.15;
        /// <summary>關卡（或這個難度）沒有科學門檻需求時用的門檻。一半（15）以下 FAIL。</summary>
        public const double DefaultScienceThreshold = 30;

        /// <summary>這一局的科學門檻：生效的 science_threshold 需求值，沒有就用預設值。</summary>
        public static double ScienceThreshold(IEnumerable<ActiveRequirement> active)
        {
            var req = (active ?? Enumerable.Empty<ActiveRequirement>())
                .FirstOrDefault(a => a.Requirement?.Type == "science_threshold")?.Requirement;
            return req != null && req.Value > 0 ? req.Value : DefaultScienceThreshold;
        }

        public static Score Compute(Design design, GameData data, Evaluation ev, MissionResult r, MissionContext ctx)
        {
            var scenario = data.Scenario(design.ScenarioId);
            var diff = data.Difficulty(ctx.DifficultyId) ?? data.DefaultDifficulty ?? new DifficultyDef();
            var s = new Score();

            s.Science = scenario != null && scenario.FireAlert ? FireScience(ev, r) : ScienceScore(r.ScienceTotal, scenario, design.MissionYears);
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
            // 沙盒：發射失敗沒有任何代價，第一次失敗不算
            int allowed = scenario != null && scenario.Sandbox ? 1 : 0;
            s.FirstLaunchBonus = r.ReachedOrbit && ctx.FailedLaunchesBefore <= allowed;
            // 沙盒沒有任何任務需求：這一項不扣分（一般關卡沒有傳需求進來時照舊算 0，不影響既有的結果）
            double met = scenario != null && scenario.Sandbox && s.Requirements.Count == 0 ? 1.0 : s.Requirements.Count(x => x.Met) / (double)n;
            s.Needs = Clamp(80.0 * met + (s.FirstLaunchBonus ? 20 : 0));
            s.Ops = OpsScore(r, diff.ExtraActionPenalty);
            s.ScienceThreshold = ScienceThreshold(ctx.Requirements);

            if (!r.ReachedOrbit)
            {
                s.Total = 0;
                s.Grade = "FAIL";
                return s;
            }
            s.Total = Math.Round(WScience * s.Science + WLifetime * s.Lifetime + WEfficiency * s.Efficiency
                                 + WNeeds * s.Needs + WOps * s.Ops, 1);
            string byTotal = s.Total >= 90 ? "S"
                : s.Total >= 78 ? "A"
                : s.Total >= 62 ? "B"
                : "C";
            // 科學門檻：不到一半 FAIL，不到門檻評等最高 C（其他分項再好，任務目的沒達成就不算好任務）
            s.ScienceFail = s.Science < s.ScienceThreshold * 0.5;
            s.GradeCapped = !s.ScienceFail && s.Science < s.ScienceThreshold && byTotal != "C";
            s.Grade = s.ScienceFail ? "FAIL" : s.GradeCapped ? "C" : byTotal;
            return s;
        }

        /// <summary>
        /// 營運處置分數。每回合：判斷對 50 + 50 × 步驟完成比例 − 每個多餘操作 penalty，下限 0；誤報的回合 0。各回合平均。
        /// 沒有遇到任何事件（任務提早結束）時，入軌就算滿分。
        /// </summary>
        public static double OpsScore(MissionResult r, double penalty)
        {
            if (!r.ReachedOrbit) return 0;
            if (r.Events.Count == 0) return 100;
            return Clamp(r.Events.Average(e => TurnOpsScore(e, penalty)));
        }

        public static double TurnOpsScore(EventOutcome e, double penalty) =>
            e.Voided ? 0.0
            : Math.Max(0, (e.JudgedCorrect ? 50.0 : 0.0) + 50.0 * e.StepsFraction - penalty * e.WrongActions.Count);

        /// <summary>
        /// 野火關卡的科學成果 = 100 × 偵測率 × 及時通報比例 × 工作比例。
        /// 及時通報比例 = min(1, 時限 ÷ 通報延遲)，再乘上營運事件的影響（每個沒處置好的大火爆發，那一回合打折）。
        /// 工作比例 = 儀器平均開機比例 × 實際運作年數 ÷ 目標年數。
        /// </summary>
        public static double FireScience(Evaluation ev, MissionResult r)
        {
            if (!r.ReachedOrbit) return 0;
            int turns = Math.Max(1, r.Events.Count);
            double eventFactor = r.Events.Count == 0 ? 1.0 : r.Events.Sum(e => e.AlertFactor) / turns;
            double timely = ev.Fire.TimelyFraction * eventFactor;
            double work = r.AverageDuty * Math.Min(1.0, r.ActualYears / Math.Max(1e-6, r.TargetYears));
            return Clamp(100.0 * ev.Fire.Detection * timely * work);
        }

        /// <summary>發射前的預估（儀表板、任務需求進度用）：假設營運事件都處置好、儀器全時開機。</summary>
        public static double ExpectedFireScience(Evaluation ev, Design d) =>
            Clamp(100.0 * ev.Fire.Detection * ev.Fire.TimelyFraction * Math.Min(1.0, ev.Lifetime.Years / Math.Max(1, d.MissionYears)));

        /// <summary>
        /// 科學分數 = 100 · √(科學點數 / 參考值)，上限 100。
        /// 開根號讓小任務也拿得到分數，大任務則邊際效益遞減（多帶一台儀器不會讓分數翻倍）。
        /// </summary>
        public static double ScienceScore(double scienceTotal, ScenarioDef scenario) =>
            ScienceScore(scienceTotal, scenario, 0);

        /// <summary>
        /// 任務年數超過目標時，參考值依比例放大：科學分看的是每年的表現，多做幾年不會白拿分，
        /// 只會多付營運費、多承擔壽命風險（中途結束就拿不到那幾年的科學）。短於目標不縮小（短任務本來就少拿）。
        /// missionYears ≤ 0 表示用目標年數。
        /// </summary>
        public static double ScienceScore(double scienceTotal, ScenarioDef scenario, double missionYears)
        {
            double reference = scenario != null && scenario.ScienceReference > 0 ? scenario.ScienceReference : 100;
            double target = scenario != null && scenario.TargetYears > 0 ? scenario.TargetYears : 0;
            if (target > 0 && missionYears > target) reference *= missionYears / target;
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
