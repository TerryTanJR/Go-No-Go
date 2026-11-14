using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>
    /// 野火偵測關卡（docs/level_wildfire.md）的教學點。每一組零件組合對應一件要教的事，
    /// 調數值時跑這組就知道有沒有把教學點弄壞。
    /// </summary>
    public class WildfireTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        Evaluation Ev(Design d, string diff = "engineer") => Evaluator.EvaluateDesign(d, _data, diff, false);

        TestLevel Level(Design d, string test, string diff = "engineer") =>
            EnvTests.Run(d, _data, diff).Where(f => f.Test == test).Select(f => f.Level).DefaultIfEmpty(TestLevel.Pass).Max();

        MissionResult Run(Design d, string diff = "engineer", int seed = 3, IDecisionSource choices = null) =>
            Simulator.SimulateMission(d, _data, seed, choices ?? new PerfectDecisions(), new MissionContext
            {
                DifficultyId = diff,
                Requirements = Departments.Active(_data.Scenario("wildfire"), _data.Difficulty(diff)),
            });

        static string[] Unmet(MissionResult r) => r.Score.Requirements.Where(x => !x.Met).Select(x => x.Requirement.Id).ToArray();

        [Test]
        public void A_CheapestFliesButAlertsTooLate()
        {
            var d = WildfireCombos.A();
            var ev = Ev(d);
            Assert.That(ev.AnyOver, Is.False);
            Assert.That(Level(d, EnvTests.Vibration), Is.EqualTo(TestLevel.Pass));
            Assert.That(Level(d, EnvTests.Thermal), Is.EqualTo(TestLevel.Pass));
            Assert.That(ev.Fire.LatencyMin, Is.GreaterThan(60), "一座地面站 + S 頻段：通報要等一個多小時");
            Assert.That(ev.Fire.Detection, Is.EqualTo(0.36).Within(1e-9));
            var r = Run(d);
            Assert.That(r.Launch.Outcome, Is.EqualTo(LaunchOutcome.Nominal));
            Assert.That(r.Score.Grade, Is.EqualTo("FAIL"), "能飛不代表任務成功：科學成果低於 15");
            Assert.That(Unmet(r), Does.Contain("wf_latency"));
            Assert.That(r.Causes.Select(c => c.Code), Does.Contain("latency"));
        }

        [Test]
        public void B_AfternoonWithoutAskingEngineeringDiesInFirstShadow()
        {
            var d = WildfireCombos.B();
            var ev = Ev(d);
            Assert.That(ev.Orbit.EclipseMin, Is.EqualTo(35).Within(1), "下午軌道每圈約 35 分鐘地影");
            Assert.That(ev.Power.ActualDod, Is.GreaterThan(1.0));
            Assert.That(Level(d, EnvTests.Thermal), Is.EqualTo(TestLevel.Fail), "熱真空測試先抓到");
            var r = Run(d);
            Assert.That(r.Launch.Outcome, Is.EqualTo(LaunchOutcome.DeadOnOrbit));
            Assert.That(r.Causes[0].Code, Is.EqualTo("dead_on_orbit"));
            Assert.That(Unmet(r), Does.Contain("wf_eclipse"));
        }

        [TestCase("C")]
        [TestCase("D")]
        public void CAndDAreBothRightAnswersForEngineer(string which)
        {
            var d = which == "C" ? WildfireCombos.C() : WildfireCombos.Dd();
            var ev = Ev(d);
            Assert.That(ev.AnyOver, Is.False, string.Join(", ", ev.Warnings.Select(w => w.Code)));
            Assert.That(ev.Fire.LatencyMin, Is.LessThanOrEqualTo(30));
            Assert.That(Level(d, EnvTests.Vibration), Is.EqualTo(TestLevel.Pass));
            Assert.That(Level(d, EnvTests.Thermal), Is.EqualTo(TestLevel.Pass));
            foreach (int seed in Enumerable.Range(1, 30))
            {
                var r = Run(d, seed: seed);
                if (!r.ReachedOrbit) continue; // 火箭本身的可靠度
                Assert.That(Unmet(r), Is.Empty, "工程師難度：C、D 都全部達成");
                Assert.That(new[] { "S", "A", "B" }, Has.Member(r.Score.Grade));
            }
        }

        [Test]
        public void DScoresMoreScienceButOnlyCSatisfiesTheDirector()
        {
            Assert.That(Run(WildfireCombos.Dd()).Score.Science, Is.GreaterThan(Run(WildfireCombos.C()).Score.Science));
            Assert.That(Ev(WildfireCombos.C()).Cost.TotalMusd, Is.EqualTo(Ev(WildfireCombos.Dd()).Cost.TotalMusd).Within(1.0), "C、D 成本差不多");

            Assert.That(Unmet(Run(WildfireCombos.C(), "director")), Is.Empty, "任務主管難度：C 是唯一全部達成的");
            Assert.That(Unmet(Run(WildfireCombos.Dd(), "director")), Is.EqualTo(new[] { "wf_lwir" }));
            Assert.That(Unmet(Run(WildfireCombos.F(), "director")), Does.Contain("wf_afternoon"));
            Assert.That(Unmet(Run(WildfireCombos.E(), "director")), Does.Contain("wf_reserve"));
            Assert.That(Ev(WildfireCombos.C(), "director").Cost.BudgetMusd, Is.EqualTo(37), "本關任務主管難度預算倍率 ×1.0");
        }

        [Test]
        public void F_DawnDuskIsSafeButMissesTheMissionLead()
        {
            var d = WildfireCombos.F();
            var ev = Ev(d);
            Assert.That(ev.Orbit.EclipseMin, Is.EqualTo(0));
            Assert.That(ev.AnyOver, Is.False);
            Assert.That(ev.Fire.Detection, Is.EqualTo(0.51).Within(1e-9));
            var r = Run(d);
            Assert.That(Unmet(r), Is.EqualTo(new[] { "wf_afternoon" }));
            Assert.That(r.Causes.Select(c => c.Code), Does.Contain("pass_time"));
        }

        [Test]
        public void E_LuxuryIsOverBudgetExceptForCadets()
        {
            var d = WildfireCombos.E();
            Assert.That(Ev(d)[ResourceId.Budget].Status, Is.EqualTo(ResourceStatus.Over));
            Assert.That(Ev(d, "cadet")[ResourceId.Budget].Status, Is.Not.EqualTo(ResourceStatus.Over), "學員難度預算 ×1.5 放得下");
            Assert.That(Ev(d).Fire.LatencyMin, Is.LessThan(Ev(WildfireCombos.C()).Fire.LatencyMin));
        }

        [Test]
        public void NearMisses()
        {
            // C 的大電池換成 80 Wh：熱真空 WARN、可以發射，但電池很快耗損
            var c = WildfireCombos.CSmallBattery();
            Assert.That(Level(c, EnvTests.Thermal), Is.EqualTo(TestLevel.Warn));
            Assert.That(Run(c).Launch.Success);
            Assert.That(Ev(c).Lifetime.Years, Is.LessThan(3));
            // D 只用一座地面站：最常見的「沒問通訊」
            var d1 = WildfireCombos.DOneStation();
            Assert.That(Ev(d1).Fire.LatencyMin, Is.GreaterThan(60));
            Assert.That(Unmet(Run(d1)), Is.EqualTo(new[] { "wf_latency" }));
        }

        [Test]
        public void RideshareOnlyGoesToTheDawnDuskOrbit()
        {
            var s = SessionRules.NewSession(_data, "wildfire", "engineer", false, false);
            s.Phase = SessionPhase.Design;
            Assert.That(s.Bench.LauncherId, Is.EqualTo("falcon9_rideshare"));
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.SetOrbit, 1, "sso_1330")), Is.EqualTo("err.rideshare_orbit"));
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.SetLauncher, 1, "electron")), Is.EqualTo(SessionRules.Ok));
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.SetOrbit, 1, "sso_1330")), Is.EqualTo(SessionRules.Ok));
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.SetLauncher, 1, "falcon9_rideshare")), Is.EqualTo("err.rideshare_orbit"));
            // 其他關卡看不到共乘、野火專用零件
            var eo = _data.Scenario("earth_observation");
            Assert.That(_data.LaunchersFor(eo).Select(l => l.Id), Does.Not.Contain("falcon9_rideshare"));
            var eoShelf = _data.ShelfFor(eo, _data.Difficulty("engineer")).Select(p => p.Id).ToList();
            Assert.That(eoShelf, Does.Not.Contain("inst_fire_mwir").And.Not.Contain("inst_fire_mwir_hr").And.Not.Contain("batt_cubesat"));
        }

        [Test]
        public void ShelfAndStartFollowTheSpec()
        {
            var cadet = SessionRules.NewSession(_data, "wildfire", "cadet", false, false);
            Assert.That(cadet.Bench.Instruments, Is.EqualTo(new[] { "inst_fire_mwir" }));
            Assert.That(cadet.Bench.Get(PartCategory.Solar), Is.EqualTo("solar_deployable"));
            var shelf = SessionRules.ShelfParts(_data, cadet).Select(p => p.Id).ToList();
            Assert.That(shelf, Does.Not.Contain("inst_thermal"), "學員難度沒有長波熱紅外線，也就不會遇到假火點");
            foreach (var cat in new[] { PartCategory.Instrument, PartCategory.Solar, PartCategory.Battery, PartCategory.Antenna })
                Assert.That(_data.ShelfFor(_data.Scenario("wildfire"), _data.Difficulty("cadet")).Count(p => p.Category == cat), Is.EqualTo(2), cat);

            var eng = SessionRules.NewSession(_data, "wildfire", "engineer", false, false);
            Assert.That(eng.Bench.Instruments, Is.EqualTo(new[] { "inst_thermal" }));
            var ev = Ev(eng.Bench);
            Assert.That(ev.Fire.InstrumentDetection, Is.LessThan(0.5), "開局缺火點儀");
            Assert.That(ev.Fire.LatencyMin, Is.GreaterThan(90), "開局通報延遲很長");
        }

        [Test]
        public void EngineeringPushesDawnDuskOnlyFromEngineerUp()
        {
            var sc = _data.Scenario("wildfire");
            var line = _data.Lines(sc, _data.Department("engineering")).Single(l => l.Id == "orbit");
            Assert.That(GameData.LineOpen(line, _data.Difficulty("cadet")), Is.False);
            Assert.That(GameData.LineOpen(line, _data.Difficulty("engineer")), Is.True);
            var s = SessionRules.NewSession(_data, "wildfire", "cadet", false, false);
            int i = _data.Lines(sc, _data.Department("engineering")).IndexOf(line);
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.Call, 1, "engineering", i)), Is.EqualTo("err.unknown"));
        }

        [Test]
        public void EventsFollowDifficulty()
        {
            var sc = _data.Scenario("wildfire");
            Assert.That(GameData.EventTypes(sc, _data.Difficulty("cadet")), Is.EqualTo(new[] { "big_fire", "power_anomaly" }));
            Assert.That(GameData.EventTypes(sc, _data.Difficulty("director")), Does.Contain("false_fire"));
            Assert.That(GameData.EventTypes(sc, _data.Difficulty("engineer")), Does.Not.Contain("false_fire"));
            var plan = Simulator.PlanMission(WildfireCombos.C(), _data, 1, "director");
            Assert.That(plan.Events.Select(e => e.Kind), Has.Member(EventKind.BigFire).And.Member(EventKind.FalseFire));
        }

        [Test]
        public void FireEventsRewardTheRightProcedure()
        {
            var d = WildfireCombos.C();
            var plan = Simulator.PlanMission(d, _data, 1, "director");
            var big = plan.Events.First(e => e.Kind == EventKind.BigFire);
            var fake = plan.Events.First(e => e.Kind == EventKind.FalseFire);

            // 大火爆發沒處置：這回合及時通報打五折，科學成果下降
            var perfect = Run(d, "director", 1);
            var lazy = Run(d, "director", 1, new Scripted(e => e.Kind == EventKind.BigFire ? new EventResponse() : new PerfectDecisions().Respond(e)));
            Assert.That(lazy.Events.Single(o => o.Event.Id == big.Id).AlertFactor, Is.EqualTo(0.5));
            Assert.That(lazy.Score.Science, Is.LessThan(perfect.Score.Science));

            // 把假火點當成大火通報出去：這回合營運處置 0 分
            var fooled = Run(d, "director", 1, new Scripted(e => e.Kind == EventKind.FalseFire
                ? new EventResponse { Judged = "big_fire", Actions = { OpsActions.FirePriority, OpsActions.PriorityDownlink } }
                : new PerfectDecisions().Respond(e)));
            var o2 = fooled.Events.Single(o => o.Event.Id == fake.Id);
            Assert.That(o2.Voided && o2.ResultCode == "event.false_fire.reported");
            Assert.That(fooled.Score.Ops, Is.LessThan(perfect.Score.Ops));
            Assert.That(fooled.Causes.Select(c => c.Code), Does.Contain("false_fire"));
        }

        [Test]
        public void ThermalImagerTellsFalseFiresApart()
        {
            var plan = Simulator.PlanMission(WildfireCombos.C(), _data, 1, "director");
            var big = plan.Events.First(e => e.Kind == EventKind.BigFire);
            var fake = plan.Events.First(e => e.Kind == EventKind.FalseFire);
            string Read(Design d, MissionEvent e, string key) =>
                OpsTelemetry.For(OpsConsoles.Payload, e, Ev(d), d, _data, null).Single(r => r.Key == key).Value;

            var c = WildfireCombos.C();
            Assert.That(Read(c, big, "ops.read.mwir"), Is.Not.EqualTo("2"));
            Assert.That(Read(c, fake, "ops.read.mwir"), Is.Not.EqualTo("2"), "兩個事件的火點儀讀數都暴增");
            Assert.That(double.Parse(Read(c, big, "ops.read.lwir")), Is.GreaterThan(double.Parse(Read(c, fake, "ops.read.lwir")) + 20), "真火的長波亮溫明顯比較高");
            Assert.That(Read(WildfireCombos.Dd(), fake, "ops.read.lwir"), Is.EqualTo("NO DATA"), "沒裝長波熱紅外線就只能猜");
        }
    }

    /// <summary>野火關卡 spec 第 6 節的零件組合（照現有物理引擎重算過）。</summary>
    public static class WildfireCombos
    {
        public static Design D(string[] inst, string solar, string batt, string ant, string gs, string orbit, string launcher) => new Design
        {
            ScenarioId = "wildfire",
            Instruments = new List<string>(inst),
            Parts = new Dictionary<string, string>
            {
                [PartCategory.Solar] = solar,
                [PartCategory.Battery] = batt,
                [PartCategory.Antenna] = ant,
                [PartCategory.GroundStation] = gs,
                [PartCategory.Computer] = "obc_mass_memory",
                [PartCategory.Adcs] = "adcs_compact",
                [PartCategory.Propulsion] = "prop_none",
                [PartCategory.Shielding] = "shield_std",
            },
            OrbitId = orbit,
            LauncherId = launcher,
            MissionYears = 3,
        };

        public const string Mwir = "inst_fire_mwir", MwirHr = "inst_fire_mwir_hr", Lwir = "inst_thermal";
        public const string Dawn = "sso_dawn_dusk", Noon = "sso_1330", Ride = "falcon9_rideshare", Electron = "electron";

        /// <summary>A 最省錢：發射沒問題，但單一地面站 + S 頻段讓通報太慢。</summary>
        public static Design A() => D(new[] { Mwir }, "solar_body", "batt_cubesat", "ant_sband", "gs_1", Dawn, Ride);
        /// <summary>B 照任務主管：下午軌道，沒問工程部，小電池撐不過地影。</summary>
        public static Design B() => D(new[] { MwirHr }, "solar_deployable", "batt_cubesat", "ant_xband", "gs_3", Noon, Electron);
        /// <summary>C 平衡（能分辨假火點）。</summary>
        public static Design C() => D(new[] { Mwir, Lwir }, "solar_deployable", "batt_medium", "ant_xband", "gs_3", Noon, Electron);
        /// <summary>D 平衡（高解析）。</summary>
        public static Design Dd() => D(new[] { MwirHr }, "solar_deployable", "batt_medium", "ant_xband", "gs_3", Noon, Electron);
        /// <summary>F 晨昏保守：工程部最滿意，偵測率低。</summary>
        public static Design F() => D(new[] { MwirHr }, "solar_deployable", "batt_cubesat", "ant_xband", "gs_3", Dawn, Ride);
        /// <summary>E 豪華版：性能最好但超預算。</summary>
        public static Design E() => D(new[] { MwirHr, Lwir }, "solar_large", "batt_medium", "ant_xband_hr", "gs_6", Noon, Electron);

        /// <summary>差一點的變化：C 的大電池換成 80 Wh。</summary>
        public static Design CSmallBattery() { var d = C(); d.Parts[PartCategory.Battery] = "batt_small"; return d; }
        /// <summary>D 只用一座地面站。</summary>
        public static Design DOneStation() { var d = Dd(); d.Parts[PartCategory.GroundStation] = "gs_1"; return d; }
    }
}
