using System;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>平衡試算表：印出代表設計的數字，調數值時用。不參與一般測試。</summary>
    public class BalanceReport
    {
        [Test, Explicit("只在調數值時手動執行")]
        public void Print()
        {
            var data = TestData.Load();
            var sb = new StringBuilder();
            void Row(string name, Design d)
            {
                var ev = Evaluator.EvaluateDesign(d, data);
                sb.AppendLine($"== {name}");
                foreach (var r in ev.Resources.Values.OrderBy(r => r.Id))
                    sb.AppendLine($"  {r.Id,-7} used {r.Used,9:0.0} / {r.Capacity,9:0.0} {r.Unit,-6} margin {r.Margin * 100,6:0.0}% {r.Status}");
                sb.AppendLine($"  life {ev.Lifetime.Years:0.0} ({ev.Lifetime.Limit})  sci/yr {ev.SciencePerYear:0.0}  return {ev.Data.ReturnFraction:P0}  dod {ev.Power.ActualDod:0.00}  eclipse {ev.Orbit.EclipseMin:0.0} min");
                foreach (var w in ev.Warnings)
                    sb.AppendLine($"  ! {w.Code} fixes: {string.Join(" | ", w.Fixes.Select(f => $"{f.Kind}:{f.FromId}->{f.ToId}{(f.Years > 0 ? f.Years.ToString() : "")}"))}");
                var grades = Enumerable.Range(1, 20).Select(seed => Simulator.SimulateMission(d, data, seed, null)).ToList();
                var r0 = grades[0];
                sb.AppendLine($"  seed1: {r0.Launch.Outcome} years {r0.ActualYears:0.0} sci {r0.Score.Science:0} eff {r0.Score.Efficiency:0} life {r0.Score.Lifetime:0} team {r0.Score.Teamwork:0} total {r0.Score.Total:0} {r0.Score.Grade} return {r0.ReturnFraction:P0}");
                sb.AppendLine("  grades(20 seeds): " + string.Join(" ", grades.Select(g => g.Score.Grade)));
            }
            Row("cheap", TestData.CheapSmallsat());
            Row("balanced", TestData.Balanced());
            Row("high", TestData.HighScience());
            foreach (var s in data.Scenarios) Row("default " + s.Id, s.DefaultDesign);
            Console.WriteLine(sb.ToString());
            TestContext.Out.WriteLine(sb.ToString());
        }
    }
}
