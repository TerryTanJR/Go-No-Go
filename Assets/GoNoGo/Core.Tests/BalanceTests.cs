using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>
    /// 藍圖的平衡原則：每個情境至少 3 組明顯不同的設計都能「任務成功」，
    /// 而且各自在不同分項拿高分。
    /// </summary>
    public class BalanceTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        /// <summary>照真實流程跑：這個難度的處室需求、第一次就發射、營運事件照手冊處置。</summary>
        List<MissionResult> Runs(Design d, int n = 30)
        {
            var list = new List<MissionResult>();
            var scenario = _data.Scenario(d.ScenarioId);
            var diff = _data.Difficulty("engineer");
            for (int seed = 1; seed <= n; seed++)
            {
                var ctx = new MissionContext
                {
                    DifficultyId = "engineer",
                    Requirements = Departments.Active(scenario, diff),
                    FailedLaunchesBefore = 0,
                };
                list.Add(Simulator.SimulateMission(d, _data, seed, new PerfectDecisions(), ctx));
            }
            return list;
        }

        static bool Passing(string grade) => grade == "S" || grade == "A" || grade == "B";

        static double Median(IEnumerable<double> xs)
        {
            var a = xs.OrderBy(x => x).ToList();
            return a[a.Count / 2];
        }

        [TestCase("cheap")]
        [TestCase("balanced")]
        [TestCase("high")]
        public void RepresentativeDesignIsGoAndSucceeds(string which)
        {
            var d = which == "cheap" ? TestData.CheapSmallsat() : which == "balanced" ? TestData.Balanced() : TestData.HighScience();
            var ev = Evaluator.EvaluateDesign(d, _data);
            Assert.That(ev.AnyOver, Is.False, string.Join(", ", ev.Warnings.Select(w => w.Code)));
            Assert.That(ev.Lifetime.Years, Is.GreaterThanOrEqualTo(d.MissionYears));
            Assert.That(ev.Power.ActualDod, Is.LessThanOrEqualTo(1.0), "電池要撐得過地影");

            var runs = Runs(d);
            var reached = runs.Where(r => r.ReachedOrbit).ToList();
            Assert.That(reached.Count, Is.GreaterThanOrEqualTo(27), "火箭可靠度內大多數種子都要入軌");
            Assert.That(reached.Select(r => r.Score.Grade), Has.None.EqualTo("FAIL"), "入軌後不能失敗");
            Assert.That(reached.All(r => Passing(r.Score.Grade)), "任務成功至少要 B：" + string.Join(" ", reached.Select(r => r.Score.Grade)));
        }

        [Test]
        public void EachDesignWinsADifferentCategory()
        {
            var cheap = Runs(TestData.CheapSmallsat()).Where(r => r.ReachedOrbit).ToList();
            var bal = Runs(TestData.Balanced()).Where(r => r.ReachedOrbit).ToList();
            var high = Runs(TestData.HighScience()).Where(r => r.ReachedOrbit).ToList();
            double Sci(List<MissionResult> l) => Median(l.Select(r => r.Score.Science));
            double Eff(List<MissionResult> l) => Median(l.Select(r => r.Score.Efficiency));
            double Total(List<MissionResult> l) => Median(l.Select(r => r.Score.Total));
            double Cost(Design d) => Evaluator.EvaluateDesign(d, _data).Cost.TotalMusd;

            Assert.That(Sci(high), Is.GreaterThan(Sci(bal)), "高科學型科學分最高");
            Assert.That(Sci(bal), Is.GreaterThan(Sci(cheap)));
            Assert.That(Eff(cheap), Is.GreaterThan(Eff(high)), "便宜型效率比高科學型好");
            Assert.That(Cost(TestData.CheapSmallsat()), Is.LessThan(Cost(TestData.Balanced())));
            Assert.That(Cost(TestData.Balanced()), Is.LessThan(Cost(TestData.HighScience()) / 2), "三組設計的成本差距要明顯");
            TestContext.Out.WriteLine($"total median cheap {Total(cheap):0} balanced {Total(bal):0} high {Total(high):0}");
        }

        [Test]
        public void DefaultEarthDesignStartsWithOneTeachableProblem()
        {
            var s = _data.Scenario("earth_observation");
            var ev = Evaluator.EvaluateDesign(s.DefaultDesign, _data);
            var over = ev.Resources.Values.Where(r => r.Status == ResourceStatus.Over).Select(r => r.Id).ToList();
            Assert.That(over, Is.EqualTo(new[] { ResourceId.Data }), "預設設計刻意只有一個紅燈（儲存），讓玩家第一次就看到解法");
            Assert.That(ev.Warnings.Single(w => w.Code == "warn.storage").Fixes.Count, Is.EqualTo(2));
        }

        [Test]
        public void LunarScenarioIsWinnable()
        {
            var d = TestData.Eo(new[] { "inst_visible", "inst_thermal" },
                "solar_deployable", "batt_large", "ant_xband_hr", "gs_1", "obc_mass_memory", "adcs_wheels", "prop_hydrazine", "shield_std",
                "llo_100", "falcon9", 2);
            d.ScenarioId = "lunar_mapping";
            var ev = Evaluator.EvaluateDesign(d, _data);
            Assert.That(ev.AnyOver, Is.False, string.Join(", ", ev.Warnings.Select(w => w.Code)));
            Assert.That(ev.Lifetime.Years, Is.GreaterThanOrEqualTo(2));
            var reached = Runs(d).Where(r => r.ReachedOrbit).ToList();
            Assert.That(reached.All(r => Passing(r.Score.Grade)), string.Join(" ", reached.Select(r => r.Score.Grade)));
        }

        [Test]
        public void HistoryRepeatsThenCanBeRewritten()
        {
            var s = _data.Scenario("hist_oco");
            var original = s.Historical.PresetDesign;
            foreach (var seed in Enumerable.Range(1, 10))
            {
                var r = Simulator.SimulateMission(original, _data, seed, null, new MissionContext { DifficultyId = "engineer" });
                Assert.That(r.Launch.Outcome, Is.EqualTo(LaunchOutcome.FairingFailure), "重演歷史：整流罩每次都卡住");
                Assert.That(r.Causes[0].Code, Is.EqualTo("fairing"));
                Assert.That(r.SuggestionCode, Is.EqualTo("suggest.history_launcher"));
            }

            // 原始設計本身沒有超標：失敗來自火箭，不是設計
            Assert.That(Evaluator.EvaluateDesign(original, _data).AnyOver, Is.False);

            // 改寫歷史：換成德爾塔 II，仍在原始預算 110% 以內
            var fixedDesign = original.Clone();
            fixedDesign.LauncherId = "delta_ii";
            var ev = Evaluator.EvaluateDesign(fixedDesign, _data);
            Assert.That(ev[ResourceId.Budget].Status, Is.Not.EqualTo(ResourceStatus.Over));
            Assert.That(ev.Cost.BudgetMusd, Is.EqualTo(273.4 * 1.1).Within(1e-9));
            var reached = Runs(fixedDesign).Where(r => r.ReachedOrbit).ToList();
            Assert.That(reached.Count, Is.GreaterThanOrEqualTo(27));
            Assert.That(reached.All(r => Passing(r.Score.Grade)), string.Join(" ", reached.Select(r => r.Score.Grade)));
        }
    }
}
