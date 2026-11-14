using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class SessionTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        SessionState Solo(string scenario = "earth_observation", string diff = "engineer")
        {
            var s = SessionRules.NewSession(_data, scenario, diff, false, false);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            return s;
        }

        void Ok(SessionState s, Command c) => Assert.That(SessionRules.Apply(s, _data, c), Is.EqualTo(SessionRules.Ok), c.Type + " " + c.A);
        string Try(SessionState s, CommandType t, int player, string a = "", int n = 0) => SessionRules.Apply(s, _data, Command.Of(t, player, a, n));

        /// <summary>做完環境測試再發射（發射前一定要測過目前的設計）。</summary>
        void Fly(SessionState s)
        {
            Ok(s, Command.Of(CommandType.RunTest, 1));
            Ok(s, Command.Of(CommandType.Launch, 1));
        }

        /// <summary>兩人房：1 號在組裝區、2 號在中控室，已進入設計階段。</summary>
        SessionState Duo(bool strict)
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.Join, 2));
            if (strict) Ok(s, Command.Of(CommandType.SetStrict, 1, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            return s;
        }

        [Test]
        public void LaunchNeedsAnEnvironmentalTestOfTheCurrentDesign()
        {
            var s = Solo();
            Assert.That(Try(s, CommandType.Launch, 1), Is.EqualTo("err.untested"));
            Ok(s, Command.Of(CommandType.RunTest, 1));
            Assert.That(s.LastTest.Findings.Select(f => f.Test).Distinct(), Is.EquivalentTo(new[] { EnvTests.Vibration, EnvTests.Thermal }));
            // 換了零件（或軌道、火箭）就要重測；年數、地面站不用
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 5));
            Ok(s, Command.Of(CommandType.SetGroundStation, 1, "gs_6"));
            Assert.That(EnvTests.IsCurrent(s), Is.True);
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Assert.That(Try(s, CommandType.Launch, 1), Is.EqualTo("err.retest"));
            Fly(s);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
        }

        [Test]
        public void ThermalVacuumCatchesABatteryThatDiesInEclipse()
        {
            var s = Solo();
            // 地球觀測的預設設計用 80 Wh 電池：找一個撐不過地影的組合（最小電池 + 最耗電的儀器）
            var hungry = _data.Parts.Where(p => p.Category == PartCategory.Instrument).OrderByDescending(p => p.PowerW).First();
            s.Bench.Parts[PartCategory.Battery] = "batt_small";
            s.Bench.Instruments = new System.Collections.Generic.List<string> { hungry.Id };
            var ev = Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false);
            Assume.That(ev.Power.ActualDod, Is.GreaterThan(1.0), "需要一組撐不過地影的設計");
            var f = EnvTests.Run(s.Bench, _data, s.DifficultyId);
            Assert.That(f.Single(x => x.Test == EnvTests.Thermal && x.Level == TestLevel.Fail).Code, Is.EqualTo("test.tv.battery_dead"));
            // 和發射結果一致：入軌後失效
            Fly(s);
            Assert.That(new[] { LaunchOutcome.DeadOnOrbit, LaunchOutcome.Failure }, Has.Member(s.LastLaunch.Outcome));
        }

        [Test]
        public void MultiplayerLaunchNeedsEveryoneToCallGo()
        {
            var s = Duo(false);
            Ok(s, Command.Of(CommandType.RunTest, 1));
            Ok(s, Command.Of(CommandType.Launch, 2));
            Assert.That(s.Poll, Is.Not.Null);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design), "點名還沒結束");
            // 點名中不能改設計
            Assert.That(Try(s, CommandType.SetYears, 2, "", 4), Is.EqualTo("err.poll_active"));
            Assert.That(Try(s, CommandType.Launch, 2), Is.EqualTo("err.poll_active"));
            Ok(s, Command.Of(CommandType.Vote, 2, "", 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
            Ok(s, Command.Of(CommandType.Vote, 1, "", 0));
            Assert.That(s.Poll, Is.Null);
            Assert.That(s.LastPoll.Go, Is.False);
            Assert.That(s.LastPoll.NoGoBy, Is.EqualTo(1));
            Assert.That(s.Launches, Is.Empty, "有人 NO-GO：HOLD");

            // 第二次：全員 GO 才發射
            Ok(s, Command.Of(CommandType.Launch, 2));
            Ok(s, Command.Of(CommandType.Vote, 1, "", 1));
            Ok(s, Command.Of(CommandType.Vote, 2, "", 1));
            Assert.That(s.LastPoll.Go, Is.True);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
            Assert.That(s.Launches.Count, Is.EqualTo(1));
        }

        [Test]
        public void SomeoneLeavingDuringThePollCanCompleteIt()
        {
            var s = Duo(false);
            Ok(s, Command.Of(CommandType.RunTest, 1));
            Ok(s, Command.Of(CommandType.Launch, 2));
            Ok(s, Command.Of(CommandType.Vote, 2, "", 1));
            Ok(s, Command.Of(CommandType.Leave, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch), "剩下的人都喊 GO 了");
        }

        [Test]
        public void StrictRolesSplitTheWork()
        {
            Assert.That(Try(Duo(false), CommandType.SetStrict, 1, "", 1), Is.EqualTo("err.phase"), "開始之後不能改");
            var lobby = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(lobby, Command.Of(CommandType.Join, 1));
            Ok(lobby, Command.Of(CommandType.Join, 2));
            Assert.That(Try(lobby, CommandType.SetStrict, 2, "", 1), Is.EqualTo("err.host_only"));
            Ok(lobby, Command.Of(CommandType.SetStrict, 1, "", 1));
            Ok(lobby, Command.Of(CommandType.SetStation, 2, "", 0));
            Assert.That(Try(lobby, CommandType.StartMission, 1), Is.EqualTo("err.strict_need_both"));

            var s = Duo(true);
            // 1 號在組裝區：不能用中控室
            Assert.That(Try(s, CommandType.SetLauncher, 1, "falcon9"), Is.EqualTo("err.strict_control"));
            Assert.That(Try(s, CommandType.Call, 1, "engineering", 0), Is.EqualTo("err.strict_control"));
            Assert.That(Try(s, CommandType.Launch, 1), Is.EqualTo("err.strict_control"));
            // 2 號在中控室：不能拿零件、測試
            Assert.That(Try(s, CommandType.Pick, 2, "batt_large"), Is.EqualTo("err.strict_assembly"));
            Assert.That(Try(s, CommandType.RunTest, 2), Is.EqualTo("err.strict_assembly"));
            Ok(s, Command.Of(CommandType.RunTest, 1));
            Ok(s, Command.Of(CommandType.SetYears, 2, "", 3));
            Ok(s, Command.Of(CommandType.Launch, 2));
            // 點名每個人都可以喊
            Ok(s, Command.Of(CommandType.Vote, 1, "", 1));
            Ok(s, Command.Of(CommandType.Vote, 2, "", 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));

            // 有一區沒人了：自動取消嚴格分工
            var s2 = Duo(true);
            Ok(s2, Command.Of(CommandType.Leave, 2));
            Assert.That(s2.StrictRoles, Is.False);
            Ok(s2, Command.Of(CommandType.SetLauncher, 1, "falcon9"));
        }

        [Test]
        public void LaunchSpeedAndSkipAreShared()
        {
            var s = Solo();
            Assert.That(Try(s, CommandType.SetLaunchSpeed, 1, "", 10), Is.EqualTo("err.phase"));
            Fly(s);
            Assert.That(s.LaunchSpeed, Is.EqualTo(1));
            Ok(s, Command.Of(CommandType.SetLaunchSpeed, 1, "", 10));
            Assert.That(Try(s, CommandType.SetLaunchSpeed, 1, "", 7), Is.EqualTo("err.unknown"));
            Ok(s, Command.Of(CommandType.SkipLaunch, 1));
            Assert.That(s.LaunchSkipped, Is.True);
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            if (s.Phase == SessionPhase.Design)
            {
                Fly(s);
                Assert.That(s.LaunchSpeed, Is.EqualTo(1), "每次發射重設");
                Assert.That(s.LaunchSkipped, Is.False);
            }
        }

        [Test]
        public void PickInstallReportMovesPartsThroughTheBay()
        {
            var s = Solo();
            Assert.That(s.Bench.Get(PartCategory.Battery), Is.EqualTo("batt_small"));
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.Pick, 1, "batt_small")), Is.EqualTo("err.hands_full"));
            Assert.That(SessionRules.WouldReplace(s, _data, "batt_large"), Is.EqualTo("batt_small"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Assert.That(s.Bench.Get(PartCategory.Battery), Is.EqualTo("batt_large"));
            Assert.That(s.HeldBy(1), Is.Null);
            // 還沒回報，中控室看到的是舊的
            Assert.That(s.Reported.Get(PartCategory.Battery), Is.EqualTo("batt_small"));
            Ok(s, Command.Of(CommandType.Report, 1));
            Assert.That(s.Reported.Get(PartCategory.Battery), Is.EqualTo("batt_large"));

            // 從船上拆下儀器，再裝回去
            var inst = s.Bench.Instruments[0];
            Ok(s, Command.Of(CommandType.Pick, 1, inst));
            Assert.That(s.Bench.Instruments, Has.No.Member(inst));
            Ok(s, Command.Of(CommandType.Install, 1));
            Assert.That(s.Bench.Instruments, Has.Member(inst));
        }

        [Test]
        public void InstrumentSlotsAreLimited()
        {
            var s = Solo();
            int max = _data.Scenario("earth_observation").MaxInstruments;
            foreach (var p in SessionRules.ShelfParts(_data, s).Where(p => p.Category == PartCategory.Instrument && !s.Bench.Instruments.Contains(p.Id)))
            {
                Ok(s, Command.Of(CommandType.Pick, 1, p.Id));
                var r = SessionRules.Apply(s, _data, Command.Of(CommandType.Install, 1));
                if (r != SessionRules.Ok)
                {
                    Assert.That(r, Is.EqualTo("err.instruments_full"));
                    Ok(s, Command.Of(CommandType.Drop, 1));
                }
            }
            Assert.That(s.Bench.Instruments.Count, Is.EqualTo(max));
        }

        [Test]
        public void TwoPlayersCannotHoldTheSamePart()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.Join, 2));
            Assert.That(s.Player(1).Station, Is.EqualTo(Station.Assembly));
            Assert.That(s.Player(2).Station, Is.EqualTo(Station.Control));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            Ok(s, Command.Of(CommandType.Pick, 1, "ant_xband_hr"));
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.Pick, 2, "ant_xband_hr")), Is.EqualTo("err.taken"));
            Ok(s, Command.Of(CommandType.Leave, 1));
            Assert.That(s.HolderOf("ant_xband_hr"), Is.Null, "離開的人手上的零件放回架上");
        }

        [Test]
        public void ControlRoomChoicesApplyToBothDesigns()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.SetLauncher, 1, "falcon9"));
            Ok(s, Command.Of(CommandType.SetGroundStation, 1, "gs_6"));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 4));
            Assert.That(s.Bench.LauncherId, Is.EqualTo("falcon9"));
            Assert.That(s.Reported.LauncherId, Is.EqualTo("falcon9"));
            Assert.That(s.Reported.Get(PartCategory.GroundStation), Is.EqualTo("gs_6"));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(4));
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.SetOrbit, 1, "llo_100")), Is.EqualTo("err.not_allowed"));
        }

        [Test]
        public void TheBenchDesignIsWhatFlies()
        {
            var s = Solo();
            // 沒回報：中控室看到的是小電池，實際裝的是大電池
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Fly(s);
            Assert.That(s.LastLaunch.Design.Get(PartCategory.Battery), Is.EqualTo("batt_large"));
            Assert.That(s.Reported.Get(PartCategory.Battery), Is.EqualTo("batt_small"));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
        }

        [Test]
        public void FailedLaunchReturnsToDesignAndSuccessRunsTurnsToTheReport()
        {
            var s = Solo();
            // 電子號到不了地球同步軌道：一定失敗
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Fly(s);
            Assert.That(s.LastLaunch.Reached, Is.False, s.LastLaunch.ReasonCode);
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
            Assert.That(s.FailedLaunches, Is.EqualTo(1));

            // 換一個一定能入軌的設計
            var good = TestData.Balanced();
            foreach (var cat in new[] { PartCategory.Propulsion, PartCategory.Shielding, PartCategory.Solar, PartCategory.Battery, PartCategory.Adcs, PartCategory.Computer, PartCategory.Antenna })
            {
                if (s.Bench.Get(cat) == good.Get(cat)) continue;
                Ok(s, Command.Of(CommandType.Pick, 1, good.Get(cat)));
                Ok(s, Command.Of(CommandType.Install, 1));
            }
            s.Bench.Instruments = good.Instruments.ToList();
            s.Bench.OrbitId = good.OrbitId;
            s.Bench.LauncherId = good.LauncherId;
            s.Bench.Parts[PartCategory.GroundStation] = good.Get(PartCategory.GroundStation);
            s.Bench.MissionYears = good.MissionYears;
            // 找一個入軌的種子：設計固定，種子固定，所以只能換年數試
            for (int y = 3; y <= 6 && (s.LastLaunch.Reached == false); y++)
            {
                s.Bench.MissionYears = y;
                if (s.Phase == SessionPhase.Launch) Ok(s, Command.Of(CommandType.FinishLaunch, 1));
                Fly(s);
            }
            Assume.That(s.LastLaunch.Reached);
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Ops));

            int turns = 0;
            while (s.Phase == SessionPhase.Ops)
            {
                var e = SessionRules.CurrentEvent(s, _data);
                Assert.That(e.Turn, Is.EqualTo(s.OpsTurn));
                Ok(s, Command.Of(CommandType.Judge, 1, OpsEvents.Key(e.Kind)));
                foreach (var a in Procedures.Required(e.Kind)) Ok(s, Command.Of(CommandType.Act, 1, a));
                Ok(s, Command.Of(CommandType.EndTurn, 1));
                Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.Act, 1, OpsActions.SafeMode)), Is.EqualTo("err.phase"), "結算後不能再操作");
                Ok(s, Command.Of(CommandType.NextTurn, 1, "", s.OpsTurn));
                turns++;
            }
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Report));
            Assert.That(turns, Is.EqualTo(_data.Difficulty("engineer").Turns));
            var r = SessionRules.Result(s, _data);
            Assert.That(r.Score.Ops, Is.EqualTo(100));
            Assert.That(r.Score.FirstLaunchBonus, Is.False, "第一次發射失敗過");
        }

        [Test]
        public void CadetEventsAreRevealed()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "tutorial", true, false);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            Fly(s);
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Ops));
            var e = SessionRules.CurrentEvent(s, _data);
            Assert.That(e.Kind, Is.EqualTo(EventKind.PowerAnomaly));
            Assert.That(s.Responses[e.Id].Judged, Is.EqualTo("power_anomaly"));
        }

        [Test]
        public void TutorialStartsWithFoldOutSolarWingsAndKeepsItsLesson()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "tutorial", true, false);
            Assert.That(s.Bench.Get(PartCategory.Solar), Is.EqualTo("solar_deployable"), "新手訓練的衛星要有展開式太陽能翼（發射動畫看得到展開）");
            Assert.That(s.Reported.Get(PartCategory.Solar), Is.EqualTo("solar_deployable"));
            var ev = Evaluator.EvaluateDesign(s.Bench, _data);
            var over = ev.Resources.Values.Where(r => r.Status == ResourceStatus.Over).Select(r => r.Id).ToList();
            Assert.That(over, Is.EqualTo(new[] { ResourceId.Data }), "教學重點不變：只有儲存一個紅燈");
            Assert.That(EnvTests.Run(s.Bench, _data, "tutorial").Any(f => f.Level == TestLevel.Fail), Is.False);
            // 其他難度、歷史關卡不受影響
            Assert.That(SessionRules.NewSession(_data, "earth_observation", "engineer", false, false).Bench.Get(PartCategory.Solar), Is.EqualTo("solar_body"));
            Assert.That(SessionRules.NewSession(_data, "hist_oco", "tutorial", true, false).Bench.Get(PartCategory.Solar),
                Is.EqualTo(_data.Scenario("hist_oco").Historical.PresetDesign.Get(PartCategory.Solar)));
        }

        [Test]
        public void HistoricalDesignIsLockedUntilTheReplay()
        {
            var s = Solo("hist_oco");
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.SetLauncher, 1, "delta_ii")), Is.EqualTo("err.locked"));
            Fly(s);
            Assert.That(s.LastLaunch.Outcome, Is.EqualTo(LaunchOutcome.FairingFailure));
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Ok(s, Command.Of(CommandType.SetLauncher, 1, "delta_ii"));
        }

        [Test]
        public void SnapshotRoundTripsExactly()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Ok(s, Command.Of(CommandType.Call, 1, "engineering", 1));
            var copy = SessionState.FromJson(s.ToJson());
            Assert.That(copy.ToJson(), Is.EqualTo(s.ToJson()));
            Assert.That(copy.HeldBy(1), Is.EqualTo("batt_large"));
        }

        [Test]
        public void DialogueFillsNumbersAndRevealsRequirements()
        {
            var s = Solo("earth_observation", "cadet");
            var ev = Evaluator.EvaluateDesign(s.Reported, _data);
            var budget = _data.Department("budget_office");
            var a = Dialogue.Answer(_data, s, "budget_office", budget.Lines[0], ev, Lang.ZhTW);
            Assert.That(a, Does.Contain("210 M$"), "學員預算 ×1.5");
            var reserve = Dialogue.Answer(_data, s, "budget_office", budget.Lines[1], ev, Lang.En);
            Assert.That(reserve, Does.Contain("15%"));

            var eng = _data.Department("engineering");
            var battery = Dialogue.Answer(_data, s, "engineering", eng.Lines[1], ev, Lang.En);
            Assert.That(battery, Does.Not.Contain("{"), battery);

            // 學員：熱紅外線這條（衝突第 1 組）不會出現；工程師會
            var sci = _data.Department("science");
            Assert.That(Dialogue.Revealed(_data, s, "science", sci.Lines[1]), Is.Empty);
            Assert.That(Dialogue.Answer(_data, s, "science", sci.Lines[1], ev, Lang.En), Does.Contain("up to 3"));
            var s2 = Solo("earth_observation", "engineer");
            Assert.That(Dialogue.Revealed(_data, s2, "science", sci.Lines[1]).Single().PartId, Is.EqualTo("inst_thermal"));

            // 筆記：學員是清單、任務主管不記錄
            Ok(s, Command.Of(CommandType.Call, 1, "budget_office", 1));
            Assert.That(Dialogue.Notes(_data, s, ev, Lang.En).Single().Requirements.Single().Type, Is.EqualTo("budget_reserve"));
            var s3 = Solo("earth_observation", "director");
            Ok(s3, Command.Of(CommandType.Call, 1, "budget_office", 1));
            Assert.That(Dialogue.Notes(_data, s3, ev, Lang.En), Is.Empty);
        }
    }
}
