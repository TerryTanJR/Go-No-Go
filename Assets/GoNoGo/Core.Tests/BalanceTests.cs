using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>
    /// 藍圖的平衡原則：每個情境有多組明顯不同的設計都能「任務成功」，而且各自在不同分項拿高分。
    /// 科學分沒達科學門檻的設計（例如只帶一台可見光相機的便宜型）評等最高 C，不算成功。
    /// </summary>
    public class BalanceTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        /// <summary>照真實流程跑：這個難度的任務需求、第一次就發射、營運事件照手冊處置。</summary>
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
            if (which == "cheap")
            {
                // 只帶一台可見光相機：資源都綠燈，但科學分沒達科學門檻 60，評等壓在 C
                Assert.That(reached.All(r => r.Score.Grade == "C" && r.Score.GradeCapped && r.Score.Science < r.Score.ScienceThreshold),
                    "便宜型科學不夠，評等上限 C：" + string.Join(" ", reached.Select(r => r.Score.Grade)));
                Assert.That(reached.All(r => r.Causes.Any(c => c.Code == "science_low")), "評等被壓要說明原因");
                return;
            }
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
            // 儲存燈號分兩層之後：7.3 GB 存得下（不會真的掉資料）但低於 ×1.5 的建議值 → 只有儲存一個黃燈，沒有紅燈
            Assert.That(ev.AnyOver, Is.False);
            var notOk = ev.Resources.Values.Where(r => r.Status != ResourceStatus.Ok).Select(r => r.Id).ToList();
            Assert.That(notOk, Is.EqualTo(new[] { ResourceId.Data }), "預設設計刻意只有一個問題（儲存沒有安全餘裕）");
            Assert.That(ev[ResourceId.Data].Status, Is.EqualTo(ResourceStatus.Tight));
            Assert.That(ev.Warnings.Select(w => w.Code), Does.Contain("warn.storage_tight"));
            Assert.That(ev.Data.ReturnFraction, Is.EqualTo(1.0).Within(1e-9), "黃燈不掉資料");
        }

        static Design Lunar(string battery) => LunarWith(TestData.Eo(new[] { "inst_multispectral" },
            "solar_deployable", battery, "ant_xband_hr", "gs_1", "obc_mass_memory", "adcs_compact", "prop_green", "shield_std",
            "llo_100", "falcon9", 2));

        static Design LunarWith(Design d) { d.ScenarioId = "lunar_mapping"; return d; }

        /// <summary>
        /// BUG-001：月球關卡每個難度都要有一組零件架上選得到、全部任務需求達成、至少拿 B 的設計。
        /// 學員難度的零件架只到第 2 級，所以關卡另外開放 X 頻段高速天線（38 萬公里的下傳靠它）。
        /// </summary>
        [TestCase("cadet", "batt_medium")]
        [TestCase("engineer", "batt_large")]
        [TestCase("director", "batt_large")]
        public void LunarScenarioIsWinnableOnEveryDifficulty(string diffId, string battery)
        {
            var d = Lunar(battery);
            var scenario = _data.Scenario("lunar_mapping");
            var diff = _data.Difficulty(diffId);
            var shelf = _data.ShelfFor(scenario, diff).Where(p => !p.Decoy).Select(p => p.Id).ToList();
            foreach (var id in d.AllPartIds().Where(id => _data.Part(id).Category != PartCategory.GroundStation))
                Assert.That(shelf, Does.Contain(id), $"{diffId} 的零件架買得到 {id}");

            var ev = Evaluator.EvaluateDesign(d, _data, diffId, false);
            Assert.That(ev.AnyOver, Is.False, string.Join(", ", ev.Warnings.Select(w => w.Code)));
            var reached = new List<MissionResult>();
            for (int seed = 1; seed <= 30; seed++)
            {
                var r = Simulator.SimulateMission(d, _data, seed, new PerfectDecisions(), new MissionContext
                {
                    DifficultyId = diffId,
                    Requirements = Departments.Active(scenario, diff),
                });
                if (r.ReachedOrbit) reached.Add(r);
            }
            Assert.That(reached.Count, Is.GreaterThanOrEqualTo(27));
            foreach (var r in reached)
            {
                Assert.That(r.Score.Requirements.Where(x => !x.Met).Select(x => x.Requirement.Id), Is.Empty, $"{diffId} seed {r.Seed}");
                Assert.That(Passing(r.Score.Grade), r.Score.Grade);
            }
        }

        /// <summary>月球的預設設計（參考用，開局不再先勾）和地球觀測一樣只有一個教得會的問題：下傳不夠，而且每個難度都有一鍵換高速天線的修正。</summary>
        [TestCase("cadet")]
        [TestCase("engineer")]
        [TestCase("director")]
        public void DefaultLunarDesignStartsWithOneFixableProblem(string diffId)
        {
            var start = SessionRules.NewSession(_data, "lunar_mapping", diffId, false, false).PresetDefaults(_data).Bench;
            var ev = Evaluator.EvaluateDesign(start, _data, diffId, true);
            var over = ev.Resources.Values.Where(r => r.Status == ResourceStatus.Over).Select(r => r.Id).ToList();
            Assert.That(over, Is.EqualTo(new[] { ResourceId.Comms }), "開局只有下傳超標");
            var warn = ev.Warnings.Single(w => w.Code == "warn.downlink");
            var fix = warn.Fixes.FirstOrDefault(f => f.ToId == "ant_xband_hr");
            Assert.That(fix, Is.Not.Null, $"{diffId}：一鍵修正要提供高速天線");
            Assert.That(Evaluator.EvaluateDesign(fix.ApplyTo(start), _data, diffId, false).AnyOver, Is.False, "換完天線就沒有超標");
        }

        /// <summary>
        /// 營運天數要無條件進位：之前用 Math.Round，2 年 = 730.5 天被捨成 730 天（銀行家捨入），
        /// 任務只跑 1.9986 年，「撐滿年數」永遠判定沒達成。
        /// </summary>
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void CompletedMissionLastsTheWholeTarget(int years)
        {
            var d = Lunar("batt_large");
            d.MissionYears = years;
            var r = Simulator.SimulateMission(d, _data, 1, new PerfectDecisions(), new MissionContext { DifficultyId = "engineer" });
            Assert.That(r.ReachedOrbit);
            Assert.That(r.EndReason, Is.EqualTo(EndReason.MissionComplete));
            Assert.That(r.ActualYears, Is.GreaterThanOrEqualTo(years));
            Assert.That(r.ActualYears, Is.LessThan(years + 1.0 / 365.25 + 1e-9), "最多只多跑不到一天");
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
