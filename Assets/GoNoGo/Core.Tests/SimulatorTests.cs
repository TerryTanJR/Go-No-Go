using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class SimulatorTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        static string Json(MissionResult r) => JsonConvert.SerializeObject(r);

        [Test]
        public void SameDesignSameSeedSameResult()
        {
            foreach (var d in new[] { TestData.CheapSmallsat(), TestData.Balanced(), TestData.HighScience() })
                foreach (var seed in new[] { 1, 7, 42, 1337 })
                    Assert.That(Json(Simulator.SimulateMission(d.Clone(), _data, seed, null)),
                        Is.EqualTo(Json(Simulator.SimulateMission(d, _data, seed, null))), $"seed {seed}");
        }

        [Test]
        public void DifferentSeedsGiveDifferentEvents()
        {
            var d = TestData.Balanced();
            var days = Enumerable.Range(1, 10)
                .Select(s => string.Join(",", Simulator.PlanMission(d, _data, s, null).Events.Select(e => e.Day)))
                .Distinct().Count();
            Assert.That(days, Is.GreaterThan(5));
        }

        [Test]
        public void ChoicesDoNotReshuffleLaterEvents()
        {
            var d = TestData.Balanced();
            var a = Simulator.SimulateMission(d, _data, 11, new AlwaysChoose("safe_mode", "observe"));
            var b = Simulator.SimulateMission(d, _data, 11, new AlwaysChoose("continue", "skip"));
            Assert.That(b.Events.Select(e => e.Event.Id + "@" + e.Event.Day), Is.EqualTo(a.Events.Select(e => e.Event.Id + "@" + e.Event.Day)));
        }

        [Test]
        public void EventCountFollowsDifficulty()
        {
            var d = TestData.Balanced();
            foreach (var seed in Enumerable.Range(1, 30))
            {
                Assert.That(Simulator.PlanMission(d, _data, seed, "engineer").Events.Count, Is.InRange(3, 4));
                Assert.That(Simulator.PlanMission(d, _data, seed, "cadet").Events.Count, Is.InRange(1, 2));
                Assert.That(Simulator.PlanMission(d, _data, seed, "director").Events.Count, Is.InRange(4, 5));
            }
        }

        [Test]
        public void OverweightNeverReachesOrbit()
        {
            var d = TestData.HighScience();
            d.LauncherId = "electron";
            var r = Simulator.SimulateMission(d, _data, 1, null);
            Assert.That(r.Launch.Outcome, Is.EqualTo(LaunchOutcome.Failure));
            Assert.That(r.Launch.ReasonCode, Is.EqualTo("launch.overweight"));
            Assert.That(r.Score.Grade, Is.EqualTo("FAIL"));
            Assert.That(r.Causes[0].Code, Is.EqualTo("launch_overweight"));
            Assert.That(r.SuggestionCode, Is.EqualTo("suggest.mass"));
        }

        [Test]
        public void LaunchRollAboveReliabilityIsAnAnomaly()
        {
            var d = TestData.CheapSmallsat();
            int seed = Enumerable.Range(1, 2000).First(s => Simulator.PlanMission(d, _data, s, null).LaunchRoll > 0.96);
            var r = Simulator.SimulateMission(d, _data, seed, null);
            Assert.That(r.Launch.Outcome, Is.EqualTo(LaunchOutcome.Failure));
            Assert.That(r.Launch.ReasonCode, Is.EqualTo("launch.anomaly"));
        }

        [Test]
        public void ThinMassMarginCanEndInLowerOrbit()
        {
            var d = TestData.Balanced();
            d.Parts[PartCategory.Propulsion] = "prop_hydrazine";  // 加 60 kg，Electron 只剩不到 5% 運力
            var ev = Evaluator.EvaluateDesign(d, _data);
            Assume.That(ev[ResourceId.Mass].Margin, Is.InRange(0.0, 0.05));
            var outcomes = Enumerable.Range(1, 60).Select(s => Simulator.SimulateMission(d, _data, s, null).Launch.Outcome).ToList();
            Assert.That(outcomes, Does.Contain(LaunchOutcome.LowerOrbit));
            Assert.That(outcomes, Does.Contain(LaunchOutcome.Nominal));
        }

        [Test]
        public void SafeModeCostsDaysButProtects()
        {
            var d = TestData.Balanced();
            int seed = Enumerable.Range(1, 200).First(s =>
            {
                var plan = Simulator.PlanMission(d, _data, s, null);
                return plan.LaunchRoll < 0.9 && plan.Events.Any(e => e.Kind == EventKind.SolarStorm && e.Roll < 0.2);
            });
            var safe = Simulator.SimulateMission(d, _data, seed, new AlwaysChoose("safe_mode"));
            var risky = Simulator.SimulateMission(d, _data, seed, new AlwaysChoose("continue"));
            Assert.That(safe.Events.Where(e => e.Event.Kind == EventKind.SolarStorm).Select(e => e.ResultCode), Has.All.EqualTo("event.storm.safe"));
            Assert.That(risky.Events.Select(e => e.ResultCode), Does.Contain("event.storm.damaged"));
            Assert.That(safe.Events.Where(e => e.Event.Kind == EventKind.SolarStorm).All(e => e.LostDays > 0));
        }

        [Test]
        public void RedundantPartShrugsOffFailure()
        {
            var d = TestData.Balanced(); // adcs_wheels 有備援
            foreach (var seed in Enumerable.Range(1, 300))
            {
                var plan = Simulator.PlanMission(d, _data, seed, null);
                var f = plan.Events.FirstOrDefault(e => e.Kind == EventKind.PartFailure && e.RefId == "adcs_wheels");
                if (f == null || plan.LaunchRoll > 0.95) continue;
                var r = Simulator.SimulateMission(d, _data, seed, null);
                Assert.That(r.Events.First(e => e.Event.Id == f.Id).ResultCode, Is.EqualTo("event.failure.redundant"));
                return;
            }
            Assert.Inconclusive("300 個種子裡沒有反應輪故障");
        }

        [Test]
        public void SamplesCoverTheWholeMission()
        {
            var r = Simulator.SimulateMission(TestData.Balanced(), _data, 3, null);
            Assume.That(r.ReachedOrbit);
            Assert.That(r.Samples.Count, Is.InRange(300, 400));
            Assert.That(r.Samples.Last().Day, Is.EqualTo((int)System.Math.Round(3 * 365.25)));
            Assert.That(r.Samples.Select(s => s.Day), Is.Ordered);
            Assert.That(r.Samples.Select(s => s.DataReturnedGb), Is.Ordered);
        }

        /// <summary>
        /// 所有情境預設設計 8 個種子的結果指紋。Unity（Mono）與 .NET 8 必須一致，否則「同一個設計跑兩次一模一樣」
        /// 在不同平台會破功。數字用固定小數位格式化：兩個執行環境的 double 轉字串規則不同（Mono 17 位、.NET 8 最短往返）。
        /// 在 Unity 裡用 script eval 呼叫 Fingerprint() 比對同一個值。
        /// </summary>
        [Test]
        public void CrossRuntimeFingerprint()
        {
            Assert.That(Fingerprint(_data), Is.EqualTo(ExpectedFingerprint), "核心模擬結果變了：如果是刻意調整數值，請更新 ExpectedFingerprint 並在 Unity 裡比對");
        }

        public const string ExpectedFingerprint = "9370C5F071BA88B4"; // Unity 6000.6.8f1（Mono）與 .NET 8 實測一致

        public static string Fingerprint(GameData data)
        {
            var sb = new System.Text.StringBuilder();
            string F(double v) => v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture);
            foreach (var sc in data.Scenarios)
                for (int seed = 1; seed <= 8; seed++)
                {
                    var r = Simulator.SimulateMission(sc.DefaultDesign, data, seed, null);
                    sb.Append(sc.Id).Append(seed).Append(r.Launch.Outcome).Append(F(r.ActualYears)).Append(F(r.ScienceTotal))
                      .Append(F(r.DataReturnedGb)).Append(F(r.DataLostGb)).Append(F(r.Score.Total)).Append(r.Score.Grade);
                    foreach (var e in r.Events) sb.Append(e.Event.Id).Append(e.Event.Day).Append(e.ResultCode).Append(F(e.ScienceDelta));
                    foreach (var smp in r.Samples) sb.Append(F(smp.BatterySoc)).Append(F(smp.Science));
                }
            var h = System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(sb.ToString()));
            return System.BitConverter.ToString(h).Replace("-", "").Substring(0, 16);
        }

        [Test]
        public void ScienceScoreIsSublinearAndCapped()
        {
            var s = _data.Scenario("earth_observation");
            Assert.That(Scoring.ScienceScore(360, s), Is.EqualTo(100).Within(1e-9));
            Assert.That(Scoring.ScienceScore(90, s), Is.EqualTo(50).Within(1e-9));
            Assert.That(Scoring.ScienceScore(1000, s), Is.EqualTo(100));
            Assert.That(Scoring.ScienceScore(0, s), Is.EqualTo(0));
        }
    }
}
