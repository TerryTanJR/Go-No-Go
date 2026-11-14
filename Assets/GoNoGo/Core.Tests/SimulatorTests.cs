using System;
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
        public void ResponsesDoNotReshuffleLaterEvents()
        {
            var d = TestData.Balanced();
            var a = Simulator.SimulateMission(d, _data, 11, new PerfectDecisions());
            var b = Simulator.SimulateMission(d, _data, 11, new NoDecisions());
            Assert.That(b.Events.Select(e => e.Event.Id + "@" + e.Event.Day), Is.EqualTo(a.Events.Select(e => e.Event.Id + "@" + e.Event.Day)));
        }

        /// <summary>歷史關卡的太陽風暴只用任務期間真的發生過的事件（2009 年的 OCO 不會抽到 2024 年的風暴）；一般關卡用整個目錄。</summary>
        [Test]
        public void HistoricalStormsComeFromTheMissionYears()
        {
            var oco = _data.Scenario("hist_oco");
            var d = oco.Historical.PresetDesign.Clone();
            d.ScenarioId = oco.Id;
            var seen = new HashSet<string>();
            foreach (var seed in Enumerable.Range(1, 60))
                foreach (var e in Simulator.PlanMission(d, _data, seed, "engineer").Events.Where(e => e.Kind == EventKind.SolarStorm))
                {
                    var sw = _data.SpaceWeather.Single(x => x.Id == e.RefId);
                    int year = int.Parse(sw.Date.Substring(0, 4));
                    Assert.That(year, Is.InRange(oco.Historical.Year, oco.Historical.Year + d.MissionYears), sw.Id);
                    seen.Add(sw.Id);
                }
            Assert.That(seen, Is.EquivalentTo(new[] { "2010-04-05-GST", "2011-02-15-FLR-X2" }));
            Assert.That(_data.StormsKnownBy(oco, d.MissionYears).Max(x => x.Date), Is.LessThan("2012"), "手冊不列任務結束之後的事件");
            Assert.That(_data.StormsDuring(_data.Scenario("earth_observation"), 3), Is.SameAs(_data.SpaceWeather), "一般關卡用整個目錄");
        }

        [Test]
        public void OneEventPerTurnFromTheDifficultyPool()
        {
            var d = TestData.Balanced();
            foreach (var diff in _data.Difficulties)
            {
                var allowed = diff.EventTypes.Select(t => OpsEvents.Parse(t).Value).ToList();
                var seen = new HashSet<EventKind>();
                foreach (var seed in Enumerable.Range(1, 30))
                {
                    var plan = Simulator.PlanMission(d, _data, seed, diff.Id);
                    Assert.That(plan.Turns, Is.EqualTo(diff.Turns), diff.Id);
                    Assert.That(plan.Events.Count, Is.EqualTo(diff.Turns), diff.Id);
                    Assert.That(plan.TurnStartDays.Count, Is.EqualTo(diff.Turns + 1));
                    for (int i = 0; i < plan.Events.Count; i++)
                    {
                        var e = plan.Events[i];
                        Assert.That(e.Turn, Is.EqualTo(i));
                        Assert.That(e.Day, Is.InRange(plan.TurnStartDays[i], plan.TurnStartDays[i + 1] - 1), diff.Id + " " + e.Id);
                        if (e.Kind == EventKind.Alien) continue; // 外星人彩蛋不在難度的事件池裡
                        Assert.That(allowed, Does.Contain(e.Kind), diff.Id);
                        seen.Add(e.Kind);
                    }
                }
                Assert.That(seen, Is.EquivalentTo(allowed), diff.Id + "：每一種事件都要出現得到");
            }
        }

        [Test]
        public void TutorialAlwaysLaunchesAndHasOnePowerAnomaly()
        {
            var d = TestData.CheapSmallsat();
            foreach (var seed in Enumerable.Range(1, 40))
            {
                var r = Simulator.SimulateMission(d, _data, seed, null, new MissionContext { DifficultyId = "tutorial" });
                Assert.That(r.Launch.Outcome, Is.EqualTo(LaunchOutcome.Nominal), "seed " + seed);
                Assert.That(r.Events.Single().Event.Kind, Is.EqualTo(EventKind.PowerAnomaly));
            }
        }

        [Test]
        public void BatteryThatCannotSurviveAShadowDiesOnOrbit()
        {
            var d = TestData.HighScience();
            d.Parts[PartCategory.Battery] = "batt_small";
            var ev = Evaluator.EvaluateDesign(d, _data);
            Assume.That(ev.Power.ActualDod, Is.GreaterThan(1.0));
            int seed = Enumerable.Range(1, 200).First(s => Simulator.PlanMission(d, _data, s, null).LaunchRoll < 0.9);
            var r = Simulator.SimulateMission(d, _data, seed, null);
            Assert.That(r.Launch.Outcome, Is.EqualTo(LaunchOutcome.DeadOnOrbit));
            Assert.That(r.Launch.Deployed, Is.True, "動畫照樣部署");
            Assert.That(r.ReachedOrbit, Is.False, "但算任務失敗");
            Assert.That(r.Score.Grade, Is.EqualTo("FAIL"));
            Assert.That(r.Causes[0].Code, Is.EqualTo("dead_on_orbit"));
            Assert.That(r.SuggestionCode, Is.EqualTo("suggest.battery_eclipse"));
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
        public void HighLaunchRollDoesNotBlowUpTheRocket()
        {
            // 火箭不是玩家做的：發射骰再高也不會爆炸
            var d = TestData.CheapSmallsat();
            int seed = Enumerable.Range(1, 2000).First(s => Simulator.PlanMission(d, _data, s, null).LaunchRoll > 0.96);
            var r = Simulator.SimulateMission(d, _data, seed, null);
            Assert.That(r.Launch.ReasonCode, Is.Not.EqualTo("launch.anomaly"));
            Assert.That(r.Launch.Outcome, Is.Not.EqualTo(LaunchOutcome.Failure));
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
        public void StormProcedureCostsDaysButProtects()
        {
            var d = TestData.Balanced();
            int seed = Enumerable.Range(1, 200).First(s =>
            {
                var plan = Simulator.PlanMission(d, _data, s, null);
                return plan.LaunchRoll < 0.9 && plan.Events.Any(e => e.Kind == EventKind.SolarStorm && e.Roll < 0.2);
            });
            var safe = Simulator.SimulateMission(d, _data, seed, new PerfectDecisions());
            var risky = Simulator.SimulateMission(d, _data, seed, new NoDecisions());
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
        public void PowerProcedureSavesTheBattery()
        {
            var d = TestData.Balanced();
            var ctx = new MissionContext { DifficultyId = "tutorial" };
            var handled = Simulator.SimulateMission(d, _data, 5, new PerfectDecisions(), ctx);
            var ignored = Simulator.SimulateMission(d, _data, 5, new NoDecisions(), ctx);
            Assert.That(handled.Events.Single().ResultCode, Is.EqualTo("event.power.handled"));
            Assert.That(ignored.Events.Single().ResultCode, Is.EqualTo("event.power.drained"));
            Assert.That(handled.Score.Ops, Is.EqualTo(100));
            Assert.That(ignored.Score.Ops, Is.EqualTo(0));
            Assert.That(ignored.Causes.Select(c => c.Code), Does.Contain("power_anomaly"));
        }

        [Test]
        public void OpsScoreRewardsJudgementAndEachStep()
        {
            var d = TestData.Balanced();
            int seed = Enumerable.Range(1, 200).First(s =>
            {
                var pl = Simulator.PlanMission(d, _data, s, "engineer");
                return pl.LaunchRoll < 0.9 && pl.Events.All(e => !OpsEvents.IsFire(e.Kind) && e.Kind != EventKind.Alien); // 野火和彩蛋的步驟數不同
            });
            var ctx = new MissionContext { DifficultyId = "engineer" };
            double Ops(IDecisionSource src) => Simulator.SimulateMission(d, _data, seed, src, ctx).Score.Ops;
            Assert.That(Ops(new PerfectDecisions()), Is.EqualTo(100));
            Assert.That(Ops(new NoDecisions()), Is.EqualTo(0));
            // 判斷正確、只做一步：50 + 50/3
            Assert.That(Ops(Scripted.Partial(1)), Is.EqualTo(50 + 50.0 / 3).Within(1e-9));
            Assert.That(Ops(Scripted.Partial(2)), Is.GreaterThan(Ops(Scripted.Partial(1))));
        }

        [Test]
        public void PressingEveryButtonIsWorseThanFollowingTheManual()
        {
            var d = TestData.Balanced();
            int seed = Enumerable.Range(1, 200).First(s =>
            {
                var pl = Simulator.PlanMission(d, _data, s, "engineer");
                return pl.LaunchRoll < 0.9 && pl.Events.All(e => e.Kind != EventKind.Alien && !OpsEvents.IsFire(e.Kind)); // 彩蛋和野火事件的步驟不同，這個測試不要碰到
            });
            var ctx = new MissionContext { DifficultyId = "engineer" };
            // 判斷正確，但把所有非野火的操作都按下去：步驟一定做滿，多出來 7 個操作
            var all = new Scripted(e => new EventResponse
            {
                Judged = OpsEvents.Key(e.Kind),
                Actions = OpsActions.All.Where(a => !OpsActions.FireOnly(a) && a != OpsActions.RepelAlien).ToList(),
            });
            var r = Simulator.SimulateMission(d, _data, seed, all, ctx);
            Assert.That(r.Events.Where(e => e.Event.Kind != EventKind.Alien).All(e => e.WrongActions.Count == 7));
            // 衝突：太陽風暴的安全模式、儀器關機被抵銷；科學機會的三步全被抵銷；零件故障的步驟沒有衝突
            foreach (var e in r.Events)
            {
                int expectDone = e.Event.Kind == EventKind.SolarStorm ? 1
                    : e.Event.Kind == EventKind.ScienceOpportunity ? 0
                    : e.Event.Kind == EventKind.PowerAnomaly ? 1
                    : 3;
                Assert.That(e.StepsDone, Is.EqualTo(expectDone), e.Event.Kind.ToString());
                Assert.That(e.ConflictedActions, Is.EquivalentTo(new[]
                    { OpsActions.InstrumentsOff, OpsActions.HighResMode, OpsActions.BatteryProtect, OpsActions.SafeMode, OpsActions.PointTarget, OpsActions.PriorityDownlink }));
            }
            // 工程師每個多餘操作 −10，每回合下限 0
            Assert.That(r.Score.Ops, Is.EqualTo(r.Events.Average(e => Math.Max(0, 50 + 50.0 * e.StepsDone / e.StepsRequired - 70))).Within(1e-9));
            Assert.That(r.Score.Ops, Is.LessThan(Simulator.SimulateMission(d, _data, seed, Scripted.Partial(1), ctx).Score.Ops),
                "全部按下去要比判斷對、只做一步還差");
            // 學員扣得輕（−5），但衝突照樣讓步驟做不滿
            var cadet = Simulator.SimulateMission(d, _data, seed, all, new MissionContext { DifficultyId = "cadet" });
            Assert.That(cadet.Score.Ops, Is.LessThan(50));
            // 每回合下限 0
            var none = new EventOutcome { Event = r.Events[0].Event, JudgedCorrect = false, StepsRequired = 3, StepsDone = 0,
                WrongActions = new List<string> { "a", "b" } };
            Assert.That(Scoring.TurnOpsScore(none, 10), Is.EqualTo(0));
        }

        [Test]
        public void ManualProceduresNeverConflictWithThemselves()
        {
            foreach (var k in OpsEvents.All)
                Assert.That(Procedures.Conflicted(Procedures.Required(k)), Is.Empty, "照手冊做不能自己抵銷：" + k);
        }

        [Test]
        public void LongerMissionsDoNotInflateTheScienceScore()
        {
            var s = _data.Scenario("earth_observation");      // 目標 3 年、參考值 360
            Assert.That(Scoring.ScienceScore(90, s, 3), Is.EqualTo(50).Within(1e-9));
            // 5 年做出 5/3 倍的科學點數，分數一樣：參考值跟著放大
            Assert.That(Scoring.ScienceScore(150, s, 5), Is.EqualTo(50).Within(1e-9));
            // 短於目標不縮小參考值
            Assert.That(Scoring.ScienceScore(60, s, 2), Is.EqualTo(Scoring.ScienceScore(60, s, 3)).Within(1e-9));
        }

        [Test]
        public void WrongActionsCostObservingDays()
        {
            var d = TestData.Balanced();
            var ctx = new MissionContext { DifficultyId = "cadet" };
            int seed = Enumerable.Range(1, 200).First(s =>
            {
                var plan = Simulator.PlanMission(d, _data, s, "cadet");
                return plan.LaunchRoll < 0.9 && plan.Events.Any(e => e.Kind == EventKind.ScienceOpportunity);
            });
            // 把科學機會當成太陽風暴處理：關儀器、進安全模式都是多餘的
            var r = Simulator.SimulateMission(d, _data, seed, Scripted.AlwaysTreatAs(EventKind.SolarStorm), ctx);
            var opp = r.Events.First(e => e.Event.Kind == EventKind.ScienceOpportunity);
            Assert.That(opp.JudgedCorrect, Is.False);
            Assert.That(opp.WrongActions, Is.EquivalentTo(new[] { OpsActions.InstrumentsOff, OpsActions.SafeMode, OpsActions.SunPoint }));
            Assert.That(opp.LostDays, Is.EqualTo(4));
            Assert.That(opp.ResultCode, Is.EqualTo("event.opportunity.skipped"));
        }

        [Test]
        public void TelemetryTellsStormFromPowerAnomaly()
        {
            var d = TestData.Balanced();
            var ev = Evaluator.EvaluateDesign(d, _data);
            Reading Read(EventKind k, string console, string key) =>
                OpsTelemetry.For(console, new MissionEvent { Kind = k, Severity = 2, RefId = "x" }, ev, d, _data, null)
                    .Single(x => x.Key == "ops.read." + key);

            // 兩種事件電量都會掉
            Assert.That(double.Parse(Read(EventKind.PowerAnomaly, OpsConsoles.Power, "battery").Value), Is.LessThan(50));
            Assert.That(Read(EventKind.PowerAnomaly, OpsConsoles.Power, "battery").Alert, Is.True);
            // 只有風暴的輻射會暴增
            Assert.That(Read(EventKind.SolarStorm, OpsConsoles.Attitude, "dose_rate").Alert, Is.True);
            Assert.That(Read(EventKind.PowerAnomaly, OpsConsoles.Attitude, "dose_rate").Alert, Is.False);
            Assert.That(Read(EventKind.PowerAnomaly, OpsConsoles.Attitude, "proton_flux").Alert, Is.False);

            // 零件故障：只有故障零件所在的控制台出現警示
            var fail = new MissionEvent { Kind = EventKind.PartFailure, RefId = "inst_thermal", Severity = 2 };
            var payload = OpsTelemetry.For(OpsConsoles.Payload, fail, ev, d, _data, null);
            Assert.That(payload.Single(x => x.Key == "part:inst_thermal").Value, Is.EqualTo("NO SIGNAL"));
            Assert.That(OpsTelemetry.For(OpsConsoles.Comms, fail, ev, d, _data, null).Any(x => x.Alert), Is.False);

            // 科學機會：酬載台有地面通報
            var opp = new MissionEvent { Kind = EventKind.ScienceOpportunity, RefId = "wildfire", Severity = 1 };
            Assert.That(OpsTelemetry.For(OpsConsoles.Payload, opp, ev, d, _data, null).Single(x => x.Key == "ops.read.ground_alert").Value,
                Is.EqualTo("opp.wildfire"));
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

        public const string ExpectedFingerprint = "C7D74199B3F53284"; // 營運改 4 回合、各難度事件全開；先前：19F9E65FFF351256；任務天數改成無條件進位：2 年任務跑滿 731 天，月球測繪分支

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
