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

        /// <summary>組裝區下一張訂單（零件要先買才放得上架）。多人時 2 號（中控室）批准。</summary>
        void Buy(SessionState s, params string[] parts)
        {
            foreach (var p in parts) Ok(s, Command.Of(CommandType.CartAdd, 1, p));
            Ok(s, Command.Of(CommandType.PlaceOrder, 1));
            if (SessionRules.NeedsApproval(s)) Ok(s, Command.Of(CommandType.ApproveOrder, 2));
        }
        string Try(SessionState s, CommandType t, int player, string a = "", int n = 0) => SessionRules.Apply(s, _data, Command.Of(t, player, a, n));

        /// <summary>發射（採購機制 v2：不用先做環境測試）。</summary>
        void Fly(SessionState s) => Ok(s, Command.Of(CommandType.Launch, 1));

        double Cost(params string[] ids) => ids.Sum(id => _data.Part(id).CostMusd);

        /// <summary>兩人房：1 號在組裝區、2 號在中控室，已進入設計階段。</summary>
        SessionState Duo(bool strict)
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.Join, 2));
            if (strict) Ok(s, Command.Of(CommandType.SetStrict, 1, "", 1));
            Ok(s, Command.Of(CommandType.SetReady, 1, "", 1));
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            return s;
        }

        [Test]
        public void LaunchNeedsNoTestAndTheSystemNeverJudgesTheDesign()
        {
            var s = Solo();
            Assert.That(SessionRules.CanLaunch(s), Is.EqualTo(SessionRules.Ok));
            Assert.That(Try(s, CommandType.RunTest, 1), Is.EqualTo("err.unknown"), "環境測試拿掉了");
            Fly(s);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
        }

        [Test]
        public void ABatteryThatDiesInEclipseFailsAtLaunch()
        {
            var s = Solo();
            // 地球觀測的預設設計用 80 Wh 電池：找一個撐不過地影的組合（最小電池 + 最耗電的儀器）
            var hungry = _data.Parts.Where(p => p.Category == PartCategory.Instrument).OrderByDescending(p => p.PowerW).First();
            s.Bench.Parts[PartCategory.Battery] = "batt_small";
            s.Bench.Instruments = new System.Collections.Generic.List<string> { hungry.Id };
            var ev = Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false);
            Assume.That(ev.Power.ActualDod, Is.GreaterThan(1.0), "需要一組撐不過地影的設計");
            Fly(s);
            Assert.That(new[] { LaunchOutcome.DeadOnOrbit, LaunchOutcome.Failure }, Has.Member(s.LastLaunch.Outcome));
            Assert.That(s.LastLaunch.Reached, Is.False, "入軌後就斷電也算失敗（會重來）");
        }

        [Test]
        public void MultiplayerLaunchNeedsEveryoneToCallGo()
        {
            var s = Duo(false);
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
            Ok(s, Command.Of(CommandType.Launch, 2));
            Ok(s, Command.Of(CommandType.Vote, 2, "", 1));
            Ok(s, Command.Of(CommandType.Leave, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch), "剩下的人都喊 GO 了");
        }

        [Test]
        public void HostAloneCannotStartAMultiplayerMission()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.SetReady, 1, "", 1));
            Assert.That(Try(s, CommandType.StartMission, 1), Is.EqualTo("err.need_players"), "房主一個人要等人加入");
            Ok(s, Command.Of(CommandType.Join, 2));
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
        }

        [Test]
        public void ReadyLocksTheSideAndEveryoneMustBeReadyToStart()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.Join, 2));
            Assert.That(Try(s, CommandType.StartMission, 1), Is.EqualTo("err.not_ready"));
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 1));
            Assert.That(Try(s, CommandType.SetStation, 2, "", 0), Is.EqualTo("err.ready_locked"), "準備好就不能換組");
            Ok(s, Command.Of(CommandType.SetReady, 1, "", 1));
            Assert.That(Try(s, CommandType.StartMission, 2), Is.EqualTo("err.host_only"));
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 0));
            Ok(s, Command.Of(CommandType.SetStation, 2, "", 0));
            Assert.That(Try(s, CommandType.StartMission, 1), Is.EqualTo("err.not_ready"), "有人撤回準備就不能開始");
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
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
            // 2 號在中控室：不能拿零件、送採購單
            Assert.That(Try(s, CommandType.Pick, 2, "batt_large"), Is.EqualTo("err.strict_assembly"));
            Assert.That(Try(s, CommandType.PlaceOrder, 2), Is.EqualTo("err.strict_assembly"));
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
        public void PickInstallReportMovesPartsThroughTheBay()
        {
            var s = Solo();
            Assert.That(s.Bench.Get(PartCategory.Battery), Is.EqualTo("batt_small"));
            Assert.That(Try(s, CommandType.Pick, 1, "batt_large"), Is.EqualTo("err.not_owned"), "沒買的零件不在架上");
            Buy(s, "batt_large");
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Assert.That(SessionRules.Apply(s, _data, Command.Of(CommandType.Pick, 1, "batt_small")), Is.EqualTo("err.hands_full"));
            Assert.That(SessionRules.WouldReplace(s, _data, "batt_large"), Is.EqualTo("batt_small"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Assert.That(s.Bench.Get(PartCategory.Battery), Is.EqualTo("batt_large"));
            Assert.That(s.HeldBy(1), Is.Null);
            // 大螢幕在組裝區，直接看整合台
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
            // 便宜的先買，買到上限為止（儀器很貴，不一定全部買得起）
            var more = SessionRules.ShelfParts(_data, s).Where(p => p.Category == PartCategory.Instrument && !s.Bench.Instruments.Contains(p.Id))
                .OrderBy(p => p.CostMusd).ToList();
            foreach (var p in more) if (p.CostMusd <= SessionRules.Headroom(s, _data)) Buy(s, p.Id);
            more = more.Where(p => s.Owned.Contains(p.Id)).ToList();
            Assume.That(s.Bench.Instruments.Count + more.Count, Is.GreaterThan(max), "要買得起超過上限的儀器");
            foreach (var p in more)
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
            Ok(s, Command.Of(CommandType.SetReady, 1, "", 1));
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            Buy(s, "ant_xband_hr");
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
            Buy(s, "batt_large");
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Fly(s);
            Assert.That(s.LastLaunch.Design.Get(PartCategory.Battery), Is.EqualTo("batt_large"));
            Assert.That(s.LastLaunch.Design.ProcurementMusd, Is.EqualTo(Cost("batt_small")).Within(1e-9), "換下來的開局電池還是算錢");
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

            // 整局重來：軌道回到開局
            Assert.That(s.Bench.OrbitId, Is.EqualTo(_data.Scenario("earth_observation").DefaultDesign.OrbitId));

            // 換一個一定能入軌的設計
            var good = TestData.Balanced();
            Buy(s, new[] { PartCategory.Propulsion, PartCategory.Shielding, PartCategory.Solar, PartCategory.Battery, PartCategory.Adcs, PartCategory.Computer, PartCategory.Antenna }
                .Select(good.Get).Where(id => !s.Owned.Contains(id)).Distinct().ToArray());
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
            // 找一個入軌的種子（種子會混入第幾次發射，換年數也會換種子）
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
        public void MissionYearsAreCappedAtTargetPlusTwo()
        {
            var s = Solo();                                   // 地球觀測：目標 3 年
            Assert.That(SessionRules.MaxMissionYears(s.Scenario(_data)), Is.EqualTo(5));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 10));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(5), "年數不能超過目標 + 2");
            Assert.That(s.Reported.MissionYears, Is.EqualTo(5));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 0));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(1));
            Assert.That(SessionRules.MaxMissionYears(_data.Scenario("lunar_mapping")), Is.EqualTo(4));
        }

        [Test]
        public void TutorialStartsWithFoldOutSolarWingsAndKeepsItsLesson()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "tutorial", true, false);
            Assert.That(s.Bench.Get(PartCategory.Solar), Is.EqualTo("solar_deployable"), "新手訓練的衛星要有展開式太陽能翼（發射動畫看得到展開）");
            Assert.That(s.Reported.Get(PartCategory.Solar), Is.EqualTo("solar_deployable"));
            var ev = Evaluator.EvaluateDesign(s.Bench, _data);
            var notOk = ev.Resources.Values.Where(r => r.Status != ResourceStatus.Ok).Select(r => r.Id).ToList();
            Assert.That(notOk, Is.EqualTo(new[] { ResourceId.Data }), "教學重點不變：只有儲存一個問題（黃燈：沒有安全餘裕）");
            Assert.That(ev.AnyOver, Is.False);
            Assert.That(ev.Power.ActualDod, Is.LessThanOrEqualTo(1.0), "電池撐得過地影");
            Assert.That(ev.LaunchFeasible);
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
            Assert.That(s.Restarts, Is.Zero, "歷史關卡失敗不重來，保留設計接著改");
            Ok(s, Command.Of(CommandType.SetLauncher, 1, "delta_ii"));
        }

        [Test]
        public void SnapshotRoundTripsExactly()
        {
            var s = Solo();
            Buy(s, "batt_large");
            Ok(s, Command.Of(CommandType.CartAdd, 1, "ant_xband_hr"));
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Ok(s, Command.Of(CommandType.Call, 1, "engineering", 1));
            var copy = SessionState.FromJson(s.ToJson());
            Assert.That(copy.ToJson(), Is.EqualTo(s.ToJson()));
            Assert.That(copy.HeldBy(1), Is.EqualTo("batt_large"));
            Assert.That(copy.Cart, Is.EqualTo(new[] { "ant_xband_hr" }));
            Assert.That(copy.Orders.Single().Parts, Is.EqualTo(new[] { "batt_large" }));
        }

        // ---------------------------------------------------------------- 採購（採購機制 v2，見 docs/procurement_v2.md）

        [Test]
        public void EachScenarioHasItsOwnDifficultyAndBudget()
        {
            Assert.That(SessionRules.NewSession(_data, "earth_observation", null, false, false).DifficultyId, Is.EqualTo("cadet"));
            Assert.That(SessionRules.NewSession(_data, "lunar_mapping", null, false, true).DifficultyId, Is.EqualTo("engineer"));
            Assert.That(SessionRules.NewSession(_data, "wildfire", null, false, false).DifficultyId, Is.EqualTo("director"));
            Assert.That(SessionRules.NewSession(_data, "earth_observation", "tutorial", true, false).DifficultyId, Is.EqualTo("tutorial"), "新手訓練另外指定");
            var s = SessionRules.NewSession(_data, "earth_observation", null, false, false);
            Assert.That(SessionRules.Budget(s, _data), Is.EqualTo(210));
            Assert.That(Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false).Cost.BudgetMusd, Is.EqualTo(210));
            Assert.That(SessionRules.Budget(SessionRules.NewSession(_data, "wildfire", null, false, false), _data), Is.EqualTo(40));
        }

        [TestCase("earth_observation", "electron", 191.88)]
        [TestCase("lunar_mapping", "electron", 228.32)]
        [TestCase("wildfire", "falcon9_rideshare", 26.58)]
        public void EveryStartingDesignFitsUnderTheCap(string scenario, string reserve, double room)
        {
            var s = SessionRules.NewSession(_data, scenario, null, false, false);
            Assert.That(SessionRules.ReserveLauncher(s, _data).Id, Is.EqualTo(reserve));
            Assert.That(SessionRules.Headroom(s, _data), Is.EqualTo(room).Within(0.005));
        }

        [Test]
        public void SoloOrdersBuyAtOnceUntilOnlyTheRocketIsLeft()
        {
            var s = Solo("earth_observation", null);
            Assert.That(Try(s, CommandType.PlaceOrder, 1), Is.EqualTo("err.cart_empty"));
            Assert.That(Try(s, CommandType.CartAdd, 1, "batt_small"), Is.EqualTo("err.owned"), "開局設計上的零件已經有了");
            double dev = SessionRules.DevelopmentMusd(s, _data);
            Buy(s, "batt_medium");
            Assert.That(s.Owned, Has.Member("batt_medium"));
            Assert.That(s.Cart, Is.Empty);
            Assert.That(SessionRules.DevelopmentMusd(s, _data), Is.EqualTo(dev + Cost("batt_medium")).Within(1e-9), "原價，沒有急件費");

            // 買幾次都行，直到只剩一支飛得到的火箭的錢
            string blocked = null;
            foreach (var p in SessionRules.ShelfParts(_data, s).Where(p => !s.Owned.Contains(p.Id)).OrderByDescending(p => p.CostMusd).ToList())
            {
                if (p.CostMusd <= SessionRules.Headroom(s, _data)) Buy(s, p.Id);
                else { blocked = p.Id; break; }
            }
            Assume.That(blocked, Is.Not.Null, "型錄全買也碰不到上限");
            Assert.That(s.Orders.Count, Is.GreaterThan(2), "沒有採購次數限制");
            Ok(s, Command.Of(CommandType.CartAdd, 1, blocked));
            Assert.That(Try(s, CommandType.PlaceOrder, 1), Is.EqualTo("err.order_over_cap"));
            Assert.That(s.Owned, Has.No.Member(blocked));
            Assert.That(SessionRules.Headroom(s, _data), Is.GreaterThanOrEqualTo(0), "還留得下一支飛得到的火箭");
            Assert.That(Evaluator.EvaluateDesign(s.Reported, _data, s.DifficultyId, false).ScheduleMonths,
                Is.EqualTo(Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false).ScheduleMonths), "買幾次都不會讓時程延後");
        }

        [Test]
        public void MoneyFollowsBuyingNotInstalling()
        {
            var s = Solo("earth_observation", null);
            double Dev() => Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false).Cost.DevelopmentMusd;
            double dev0 = SessionRules.DevelopmentMusd(s, _data);
            Assert.That(dev0, Is.EqualTo(_data.Constants.BusBaseCostMusd + s.Owned.Sum(id => _data.Part(id).CostMusd)).Within(1e-9), "開局零件也算錢");
            Assert.That(Dev(), Is.EqualTo(dev0).Within(1e-9), "儀表板和採購上限用同一筆開發費");

            Buy(s, "batt_medium");
            double dev1 = dev0 + Cost("batt_medium");
            Assert.That(Dev(), Is.EqualTo(dev1).Within(1e-9), "買了沒裝也算錢");
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_medium"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Assert.That(Dev(), Is.EqualTo(dev1).Within(1e-9), "裝上去不會再多算");
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_medium"));
            Ok(s, Command.Of(CommandType.Drop, 1));
            Assert.That(Dev(), Is.EqualTo(dev1).Within(1e-9), "買了又拆下來，錢不會回來");
            var inst = s.Bench.Instruments[0];
            Ok(s, Command.Of(CommandType.Pick, 1, inst));
            Ok(s, Command.Of(CommandType.Drop, 1));
            Assert.That(Dev(), Is.EqualTo(dev1).Within(1e-9), "拆下開局零件，錢也不會回來");
            Assert.That(SessionRules.DevelopmentMusd(s, _data), Is.EqualTo(dev1).Within(1e-9));
        }

        [Test]
        public void TheReservedRocketFollowsTheOrbit()
        {
            var s = Solo("earth_observation", null);
            Assert.That(SessionRules.ReserveLauncher(s, _data).Id, Is.EqualTo("electron"), "共乘飛不到 sso_700");
            double room = SessionRules.Headroom(s, _data);
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Assert.That(SessionRules.ReserveLauncher(s, _data).Id, Is.EqualTo("falcon9"), "地球同步軌道只有 Falcon 9 以上飛得到");
            Assert.That(SessionRules.Headroom(s, _data),
                Is.EqualTo(room - (_data.Launcher("falcon9").CostMusd - _data.Launcher("electron").CostMusd)).Within(1e-9));
            Assert.That(SessionRules.ReserveLauncher(Solo("wildfire", null), _data).Id, Is.EqualTo("falcon9_rideshare"));
        }

        [Test]
        public void MultiplayerOrdersWaitForTheControlRoom()
        {
            var s = Duo(true);
            Ok(s, Command.Of(CommandType.CartAdd, 1, "batt_large"));
            Ok(s, Command.Of(CommandType.PlaceOrder, 1));
            Assert.That(s.Request.Parts, Is.EqualTo(new[] { "batt_large" }));
            Assert.That(s.Owned, Has.No.Member("batt_large"), "還沒批准");
            Assert.That(s.Cart, Is.Empty);
            Assert.That(Try(s, CommandType.CartAdd, 1, "batt_large"), Is.EqualTo("err.in_request"));
            Ok(s, Command.Of(CommandType.CartAdd, 1, "batt_medium"));
            Assert.That(Try(s, CommandType.PlaceOrder, 1), Is.EqualTo("err.request_pending"), "一次一張");
            Assert.That(Try(s, CommandType.ApproveOrder, 1), Is.EqualTo("err.strict_control"), "批准是中控室的事");

            // 退回：零件回到購物車，可以改了再送
            Ok(s, Command.Of(CommandType.RejectOrder, 2));
            Assert.That(s.Request, Is.Null);
            Assert.That(s.Rejected.Parts, Is.EqualTo(new[] { "batt_large" }));
            Assert.That(s.Cart, Is.EquivalentTo(new[] { "batt_medium", "batt_large" }));
            Assert.That(s.Owned, Has.No.Member("batt_large"));

            // 撤回
            Ok(s, Command.Of(CommandType.PlaceOrder, 1));
            Assert.That(s.Rejected, Is.Null, "重新送審，清掉退回的提示");
            Assert.That(Try(s, CommandType.WithdrawOrder, 2), Is.EqualTo("err.strict_assembly"));
            Ok(s, Command.Of(CommandType.WithdrawOrder, 1));
            Assert.That(s.Request, Is.Null);
            Assert.That(s.Cart.Count, Is.EqualTo(2));

            // 批准才下單。退回、撤回過幾次，單號都還是「批准後是第幾單」，跟採購紀錄對得上
            Ok(s, Command.Of(CommandType.PlaceOrder, 1));
            Assert.That(s.Request.N, Is.EqualTo(1));
            Assert.That(s.Request.Seq, Is.EqualTo(3), "第三次送審");
            Ok(s, Command.Of(CommandType.ApproveOrder, 2));
            Assert.That(s.Orders.Single().N, Is.EqualTo(1));
            Assert.That(s.Request, Is.Null);
            Assert.That(s.Owned, Is.SupersetOf(new[] { "batt_large", "batt_medium" }));
            Assert.That(s.Orders.Single().By, Is.EqualTo(1));
            Assert.That(s.Orders.Single().ApprovedBy, Is.EqualTo(2));
            Assert.That(Try(s, CommandType.ApproveOrder, 2), Is.EqualTo("err.no_request"));

            Ok(s, Command.Of(CommandType.CartAdd, 1, "ant_sband"));
            Ok(s, Command.Of(CommandType.PlaceOrder, 1));
            Assert.That(s.Request.N, Is.EqualTo(2), "下一張是第 2 單");
        }

        [Test]
        public void OverTheCapTheAssemblyBayCanSubmitButTheControlRoomCannotApprove()
        {
            var s = Duo(false);
            var all = SessionRules.ShelfParts(_data, s).Where(p => !s.Owned.Contains(p.Id)).Select(p => p.Id).ToList();
            Assume.That(SessionRules.PartsMusd(s, _data, all), Is.GreaterThan(SessionRules.Headroom(s, _data)));
            foreach (var id in all) Ok(s, Command.Of(CommandType.CartAdd, 1, id));
            Ok(s, Command.Of(CommandType.PlaceOrder, 1));
            Assert.That(s.Request, Is.Not.Null, "送審不擋：組裝區不該從錯誤訊息知道錢的狀況");
            Assert.That(Try(s, CommandType.ApproveOrder, 2), Is.EqualTo("err.order_over_cap"));
            Ok(s, Command.Of(CommandType.RejectOrder, 2));
            Assert.That(s.Orders, Is.Empty);
        }

        [Test]
        public void AFailedLaunchStartsTheMissionOver()
        {
            var s = Solo("earth_observation", null);
            var start = s.Bench.Clone();
            var owned = s.Owned.ToList();
            double dev0 = SessionRules.DevelopmentMusd(s, _data);
            Ok(s, Command.Of(CommandType.Call, 1, "engineering", 1));
            Buy(s, "batt_medium");
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_medium"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Ok(s, Command.Of(CommandType.CartAdd, 1, "solar_deployable"));
            Ok(s, Command.Of(CommandType.SetGroundStation, 1, "gs_6"));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 5));
            // 電子號到不了地球同步軌道：一定失敗
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Fly(s);
            Assert.That(s.LastLaunch.Reached, Is.False);
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));

            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
            Assert.That(s.Restarts, Is.EqualTo(1));
            foreach (var d in new[] { s.Bench, s.Reported })
            {
                Assert.That(d.Parts, Is.EquivalentTo(start.Parts), "整合台和地面站回到開局");
                Assert.That(d.Instruments, Is.EqualTo(start.Instruments));
                Assert.That(d.OrbitId, Is.EqualTo(start.OrbitId));
                Assert.That(d.LauncherId, Is.EqualTo(start.LauncherId));
                Assert.That(d.MissionYears, Is.EqualTo(start.MissionYears));
            }
            Assert.That(s.Owned, Is.EquivalentTo(owned), "買過的全部收回");
            Assert.That(s.Orders, Is.Empty);
            Assert.That(s.Cart, Is.Empty);
            Assert.That(s.Held, Is.Empty);
            Assert.That(SessionRules.DevelopmentMusd(s, _data), Is.EqualTo(dev0).Within(1e-9), "錢全部回來");
            Assert.That(s.Calls, Is.Not.Empty, "電話問到的需求保留");
            Assert.That(s.Launches.Count, Is.EqualTo(1), "發射紀錄保留（評價要用）");
            Assert.That(s.FailedLaunches, Is.EqualTo(1));
        }

        [Test]
        public void RebuildingTheSameDesignRollsANewLaunch()
        {
            var s = Solo("earth_observation", null);
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Fly(s);
            var first = s.LastLaunch;
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Fly(s);
            Assert.That(s.LastLaunch.Design.StableHash(), Is.EqualTo(first.Design.StableHash()), "照原樣組回同一個設計");
            Assert.That(s.LastLaunch.Seed, Is.Not.EqualTo(first.Seed), "種子混入第幾次發射，不會一直擲出同一個結果");
            Assert.That(first.Seed, Is.EqualTo(first.Design.StableHash()), "第一次發射的種子不變");
        }

        [Test]
        public void OpsStopWhenTheMoneyRunsOut()
        {
            var d = TestData.Balanced();
            var ev = Evaluator.EvaluateDesign(d, _data, "cadet", false);
            // 開發費調到發射後只剩半年的營運費
            d.ProcurementMusd = ev.Cost.BudgetMusd - ev.Cost.DevelopmentMusd - ev.Cost.LaunchMusd - ev.Cost.OpsPerYearMusd * 0.5;
            Assume.That(d.ProcurementMusd, Is.GreaterThan(0));
            MissionResult r = null;
            for (int seed = 1; seed < 20 && (r == null || !r.ReachedOrbit); seed++)
                r = Simulator.SimulateMission(d, _data, seed, new NoDecisions(), new MissionContext { DifficultyId = "cadet" });
            Assume.That(r.ReachedOrbit);
            Assert.That(r.FundedYears, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(r.EndReason, Is.EqualTo(EndReason.Funding));
            Assert.That(r.ActualYears * 365.25, Is.EqualTo(182).Within(1), "大約 182 天停下");
            Assert.That(r.Events.All(o => o.Event.Day < 183), "錢用完之後的事件不會發生");
            Assert.That(r.Causes.Select(c => c.Code), Does.Contain("lifetime_funding"));
            Assert.That(r.Score.Lifetime, Is.LessThan(100));
        }

        [Test]
        public void NoMoneyLeftAfterLaunchGoesStraightToTheReport()
        {
            var s = Solo("earth_observation", null);
            foreach (var p in SessionRules.ShelfParts(_data, s).Where(p => !s.Owned.Contains(p.Id)).OrderByDescending(p => p.CostMusd).ToList())
                if (p.CostMusd <= SessionRules.Headroom(s, _data)) Buy(s, p.Id);
            // 上限只預留電子號；中控室選了 Falcon 9，不擋發射，但營運經費是負的
            Ok(s, Command.Of(CommandType.SetLauncher, 1, "falcon9"));
            Assume.That(SessionRules.FundedYears(s, _data), Is.Zero);
            Fly(s);
            Assume.That(s.LastLaunch.Reached, s.LastLaunch.ReasonCode);
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Report), "入軌後直接進評價頁");
            Assert.That(s.Restarts, Is.Zero, "有入軌，不算失敗、不重來");
            var r = SessionRules.Result(s, _data);
            Assert.That(r.EndReason, Is.EqualTo(EndReason.Funding));
            Assert.That(r.ActualYears, Is.Zero);
        }

        [Test]
        public void ProcurementBelongsToTheAssemblyBay()
        {
            var s = Duo(true);
            Assert.That(Try(s, CommandType.CartAdd, 2, "batt_large"), Is.EqualTo("err.strict_assembly"));
            Ok(s, Command.Of(CommandType.CartAdd, 1, "batt_large"));
            Assert.That(Try(s, CommandType.PlaceOrder, 2), Is.EqualTo("err.strict_assembly"));
            Ok(s, Command.Of(CommandType.PlaceOrder, 1));
            Ok(s, Command.Of(CommandType.ApproveOrder, 2));
            Assert.That(s.Owned, Has.Member("batt_large"));
        }

        [Test]
        public void DialogueFillsNumbersAndRevealsRequirements()
        {
            var s = Solo("earth_observation", "cadet");
            var ev = Evaluator.EvaluateDesign(s.Reported, _data);
            var budget = _data.Department("budget_office");
            var a = Dialogue.Answer(_data, s, "budget_office", budget.Lines[0], ev, Lang.ZhTW);
            Assert.That(a, Does.Contain("210 M$"), "地球觀測的預算（難度倍率已經併進去）");
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
