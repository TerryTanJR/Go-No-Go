using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>
    /// 測試用：照玩家實際的步驟走一遍流程（設計主螢幕選件 → 確定設計 → 架子拿小零件 → 組裝台左側認配方、右側照順序組 → 裝上衛星）。
    /// </summary>
    public static class Flow
    {
        public static void Do(SessionState s, GameData data, Command c) =>
            Assert.That(SessionRules.Apply(s, data, c), Is.EqualTo(SessionRules.Ok), c.Type + " " + c.A + " " + c.N);

        /// <summary>設計主螢幕上勾選（設計確定著的話先按「改選」）。</summary>
        public static void Select(SessionState s, GameData data, string partId, int player = 1)
        {
            if (s.Confirmed) Do(s, data, Command.Of(CommandType.Confirm, player, "", 0));
            Do(s, data, Command.Of(CommandType.SelectPart, player, partId, 1));
        }

        public static void Confirm(SessionState s, GameData data, int player = 1)
        {
            if (!s.Confirmed) Do(s, data, Command.Of(CommandType.Confirm, player, "", 1));
        }

        /// <summary>
        /// 組一個零件：到架子把小零件拿齊放到左側（手上放不下就多跑幾趟），認出配方後照順序一個一個拿到右側裝上去。
        /// 組好的成品留在組裝台上。
        /// </summary>
        public static void Assemble(SessionState s, GameData data, string partId, int player = 1, int bench = 0)
        {
            var recipe = data.Recipe(data.Part(partId));
            Assert.That(recipe, Is.Not.Null, partId + " 沒有配方");
            foreach (var c in recipe.Components)
            {
                Do(s, data, Command.Of(CommandType.CompPick, player, c.Id));
                if (s.CompsOf(player).Count >= SessionRules.CarryCapacity(s) || c == recipe.Components[recipe.Components.Count - 1])
                    Do(s, data, Command.Of(CommandType.BenchPut, player, "", bench));
            }
            Assert.That(s.Benches[bench].Job?.PartId, Is.EqualTo(partId), "左側放齊之後要認出這個零件");
            foreach (var c in recipe.Components)
            {
                Do(s, data, Command.Of(CommandType.BenchTake, player, c.Id, bench));
                Do(s, data, Command.Of(CommandType.CompFit, player, "", bench));
            }
            Assert.That(s.Benches[bench].Job.Done, partId + " 要組好了");
        }

        /// <summary>組好、拿起來、裝上衛星。</summary>
        public static void Build(SessionState s, GameData data, string partId, int player = 1, int bench = 0)
        {
            Assemble(s, data, partId, player, bench);
            Do(s, data, Command.Of(CommandType.Pick, player, partId));
            Do(s, data, Command.Of(CommandType.Install, player));
        }

        /// <summary>確定設計，把還沒裝上衛星的零件全部組好裝上去。</summary>
        public static void BuildAll(SessionState s, GameData data, int player = 1)
        {
            Confirm(s, data, player);
            foreach (var id in SessionRules.NotInstalled(s, data)) Build(s, data, id, player);
            Assert.That(SessionRules.NotInstalled(s, data), Is.Empty);
        }

        /// <summary>整個走完再按發射。</summary>
        public static void Fly(SessionState s, GameData data, int player = 1)
        {
            BuildAll(s, data, player);
            Do(s, data, Command.Of(CommandType.Launch, player));
        }
    }

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
            return s.TickDefaults(_data);
        }

        void Ok(SessionState s, Command c) => Flow.Do(s, _data, c);

        string Try(SessionState s, CommandType t, int player, string a = "", int n = 0) => SessionRules.Apply(s, _data, Command.Of(t, player, a, n));

        void Fly(SessionState s) => Flow.Fly(s, _data);

        /// <summary>兩人房，已進入設計階段。沒有分組：所有人都從中控室開始。</summary>
        SessionState Duo()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.Join, 2));
            Ok(s, Command.Of(CommandType.SetReady, 1, "", 1));
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            return s.TickDefaults(_data);
        }

        // ---------------------------------------------------------------- 開局

        [Test]
        public void AMissionStartsWithNothingTickedAndAnEmptySatellite()
        {
            foreach (var diff in new[] { "cadet", "engineer", "director" })
            {
                var s = SessionRules.NewSession(_data, "earth_observation", diff, false, false);
                Ok(s, Command.Of(CommandType.Join, 1));
                Ok(s, Command.Of(CommandType.BeginDesign, 1));
                var start = GameData.DefaultDesign(_data.Scenario("earth_observation"), diff);
                Assert.That(s.Bench.Instruments, Is.Empty, diff + "：零件一個都不先勾");
                foreach (var c in PartCategory.Single)
                    if (c != PartCategory.GroundStation) Assert.That(s.Bench.Get(c), Is.Null.Or.Empty, diff + " " + c);
                // 軌道、火箭、地面站、年數留預設：儀表板才算得出東西
                Assert.That(s.Bench.OrbitId, Is.EqualTo(start.OrbitId));
                Assert.That(s.Bench.LauncherId, Is.EqualTo(start.LauncherId));
                Assert.That(s.Bench.Get(PartCategory.GroundStation), Is.EqualTo(start.Get(PartCategory.GroundStation)));
                Assert.That(s.Bench.MissionYears, Is.EqualTo(start.MissionYears));
                Assert.That(s.Confirmed, Is.False);
                Assert.That(s.Benches.Count, Is.EqualTo(GameData.BenchCount), "兩座組裝台");
                Assert.That(s.Installed, Is.Empty, "衛星是空的");
                Assert.That(Try(s, CommandType.Confirm, 1, "", 1), Is.EqualTo("err.design_incomplete"), "沒選齊不能確定設計");
                Assert.That(s.Solo, Is.True);
                // 儀表板、任務需求在什麼都沒選的時候也算得出來（不會丟例外）
                Assert.DoesNotThrow(() => Evaluator.EvaluateDesign(s.Bench, _data, diff, true));
            }
        }

        [Test]
        public void TickingTheDefaultDesignGivesTheOldStart()
        {
            var s = Solo();
            var start = _data.Scenario("earth_observation").DefaultDesign;
            Assert.That(s.Bench.Instruments, Is.EqualTo(start.Instruments));
            foreach (var c in PartCategory.Single) Assert.That(s.Bench.Get(c), Is.EqualTo(start.Get(c)), c);
            // 衛星是空的：只有不用組裝的封板（不裝推進）選了就在上面
            Assert.That(s.Installed, Is.EqualTo(new[] { "prop_none" }));
            Assert.That(SessionRules.NotInstalled(s, _data).Count, Is.EqualTo(7));
        }

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

        [Test]
        public void HostAloneCannotStartAMultiplayerMission()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Lobby));
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.SetReady, 1, "", 1));
            Assert.That(Try(s, CommandType.StartMission, 1), Is.EqualTo("err.need_players"), "一個人玩走單人模式");
        }

        [Test]
        public void EveryoneMustBeReadyAndNobodyPicksATeam()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.Join, 2));
            Ok(s, Command.Of(CommandType.SetReady, 1, "", 1));
            Assert.That(Try(s, CommandType.StartMission, 1), Is.EqualTo("err.not_ready"));
            Assert.That(Try(s, CommandType.StartMission, 2), Is.EqualTo("err.host_only"));
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design), "多人：簡報各自看，直接進設計階段");
            Assert.That(s.Solo, Is.False, "兩人以上：挖空照所選難度");
            Assert.That(Try(s, CommandType.SetReady, 1, "", 0), Is.EqualTo("err.phase"));
            // 不分組：誰都可以操作設計主螢幕，也都可以去組裝
            Ok(s, Command.Of(CommandType.SelectPart, 2, "batt_medium", 1));
            Ok(s, Command.Of(CommandType.SelectPart, 1, "ant_sband", 1));
            Ok(s, Command.Of(CommandType.CompPick, 2, "cells"));
            Ok(s, Command.Of(CommandType.CompPick, 1, "optics"));
        }

        // ---------------------------------------------------------------- 選件（設計主螢幕）

        [Test]
        public void TheBigScreenTicksOnePartPerCategoryAndSeveralInstruments()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.SelectPart, 1, "batt_large", 1));
            Assert.That(s.Bench.Get(PartCategory.Battery), Is.EqualTo("batt_large"), "每類只選一個：選了就換掉");
            Assert.That(Try(s, CommandType.SelectPart, 1, "batt_large", 0), Is.EqualTo("err.single_required"), "只能換，不能取消");

            Ok(s, Command.Of(CommandType.SelectPart, 1, "inst_thermal", 1));
            Ok(s, Command.Of(CommandType.SelectPart, 1, "inst_sar", 1));
            Assert.That(s.Bench.Instruments, Is.EqualTo(new[] { "inst_visible", "inst_thermal", "inst_sar" }));
            Assert.That(Try(s, CommandType.SelectPart, 1, "inst_multispectral", 1), Is.EqualTo("err.instruments_full"), "上限照關卡的 max_instruments");
            Ok(s, Command.Of(CommandType.SelectPart, 1, "inst_visible", 0));
            Ok(s, Command.Of(CommandType.SelectPart, 1, "inst_multispectral", 1));
            Assert.That(s.Bench.Instruments, Is.EqualTo(new[] { "inst_thermal", "inst_sar", "inst_multispectral" }));

            Assert.That(Try(s, CommandType.SelectPart, 1, "inst_fire_mwir", 1), Is.EqualTo("err.not_allowed"), "別的關卡專用的零件");
            Assert.That(Try(s, CommandType.SelectPart, 1, "gs_3", 1), Is.EqualTo("err.unknown"), "地面站在任務設定選");
            Assert.That(Try(s, CommandType.SelectPart, 1, "nope", 1), Is.EqualTo("err.unknown"));
        }

        [Test]
        public void MissionSettingsAreChosenOnTheBigScreen()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.SetLauncher, 1, "falcon9"));
            Ok(s, Command.Of(CommandType.SetGroundStation, 1, "gs_6"));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 4));
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "leo_400"));
            Assert.That(s.Bench.LauncherId, Is.EqualTo("falcon9"));
            Assert.That(s.Bench.Get(PartCategory.GroundStation), Is.EqualTo("gs_6"));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(4));
            Assert.That(s.Bench.OrbitId, Is.EqualTo("leo_400"));
            Assert.That(Try(s, CommandType.SetOrbit, 1, "llo_100"), Is.EqualTo("err.not_allowed"));
        }

        [Test]
        public void MissionYearsAreCappedAtTargetPlusTwo()
        {
            var s = Solo();                                   // 地球觀測：目標 3 年
            Assert.That(SessionRules.MaxMissionYears(s.Scenario(_data)), Is.EqualTo(5));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 10));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(5), "年數不能超過目標 + 2");
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 0));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(1));
            Assert.That(SessionRules.MaxMissionYears(_data.Scenario("lunar_mapping")), Is.EqualTo(4));
        }

        [Test]
        public void ConfirmingFreezesTheScreenUntilItIsReopened()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.SelectPart, 1, "inst_visible", 0));
            Assert.That(SessionRules.DesignGaps(s, _data), Is.EqualTo(new[] { PartCategory.Instrument }));
            Assert.That(Try(s, CommandType.Confirm, 1, "", 1), Is.EqualTo("err.design_incomplete"), "至少要一台酬載儀器");
            Ok(s, Command.Of(CommandType.SelectPart, 1, "inst_visible", 1));
            Ok(s, Command.Of(CommandType.Confirm, 1, "", 1));
            Assert.That(s.Confirmed);
            Assert.That(s.Confirms, Is.EqualTo(1));
            Assert.That(Try(s, CommandType.SelectPart, 1, "batt_large", 1), Is.EqualTo("err.confirmed"));
            Assert.That(Try(s, CommandType.SetYears, 1, "", 4), Is.EqualTo("err.confirmed"));
            Ok(s, Command.Of(CommandType.Confirm, 1, "", 0));
            Ok(s, Command.Of(CommandType.SelectPart, 1, "batt_large", 1));
            Assert.That(s.Bench.Get(PartCategory.Battery), Is.EqualTo("batt_large"));
        }

        [Test]
        public void ChangingAPickOnlyTakesThatPartOffTheSatellite()
        {
            var s = Solo();
            Flow.BuildAll(s, _data);
            var before = s.Installed.ToList();
            Assert.That(before, Has.Member("batt_small").And.Member("inst_visible"));

            // 換電池：只有電池從衛星拆下，其他留著
            Flow.Select(s, _data, "batt_medium");
            Assert.That(s.Installed, Has.No.Member("batt_small"));
            Assert.That(s.Installed, Is.EquivalentTo(before.Where(id => id != "batt_small")), "沒換的留在衛星上");
            Assert.That(SessionRules.NotInstalled(s, _data), Is.EqualTo(new[] { "batt_medium" }), "只有換上來的要重新組裝");
            Assert.That(SessionRules.NeedsAssembly(s, _data, "batt_medium"), Is.True);

            // 改選回原本的電池：成品還留著，直接裝回去，不用再組
            Flow.Select(s, _data, "batt_small");
            Assert.That(s.Installed, Is.EquivalentTo(before));
            Assert.That(SessionRules.NotInstalled(s, _data), Is.Empty);
        }

        [Test]
        public void CostCountsOnlyThePartsTickedNow()
        {
            var s = Solo();
            double Total() => Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false).Cost.TotalMusd;
            double start = Total();
            Flow.BuildAll(s, _data);
            // 拿過的小零件不扣錢
            Ok(s, Command.Of(CommandType.CompPick, 1, "optics"));
            Ok(s, Command.Of(CommandType.CompDrop, 1));
            Assert.That(Total(), Is.EqualTo(start).Within(1e-9));
            // 換掉的零件不算錢：成本只算目前勾選的零件總和
            Flow.Select(s, _data, "batt_large");
            Assert.That(Total(), Is.EqualTo(start - _data.Part("batt_small").CostMusd + _data.Part("batt_large").CostMusd).Within(1e-9));
            Flow.Select(s, _data, "batt_small");
            Assert.That(Total(), Is.EqualTo(start).Within(1e-9));
        }

        // ---------------------------------------------------------------- 發射

        [Test]
        public void LaunchNeedsAConfirmedDesignWithEveryPartOnTheSatellite()
        {
            var s = Solo();
            Assert.That(SessionRules.LaunchBlockers(s, _data), Is.EqualTo(new[] { "err.not_confirmed", "err.not_installed" }));
            Assert.That(Try(s, CommandType.Launch, 1), Is.EqualTo("err.not_confirmed"));
            Ok(s, Command.Of(CommandType.Confirm, 1, "", 1));
            Assert.That(SessionRules.CanLaunch(s, _data), Is.EqualTo("err.not_installed"));
            var todo = SessionRules.NotInstalled(s, _data);
            foreach (var id in todo.Take(todo.Count - 1)) Flow.Build(s, _data, id);
            Assert.That(SessionRules.CanLaunch(s, _data), Is.EqualTo("err.not_installed"), "還差一個零件");
            // 組好了但還拿在手上：也不算
            Flow.Assemble(s, _data, todo.Last());
            Ok(s, Command.Of(CommandType.Pick, 1, todo.Last()));
            Assert.That(SessionRules.CanLaunch(s, _data), Is.EqualTo("err.not_installed"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Assert.That(SessionRules.LaunchBlockers(s, _data), Is.Empty);
            Assert.That(SessionRules.CanLaunch(s, _data), Is.EqualTo(SessionRules.Ok));
            Ok(s, Command.Of(CommandType.Launch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
        }

        [Test]
        public void OverBudgetBlocksTheLaunch()
        {
            var s = Solo("wildfire", null);            // 預算 40
            Ok(s, Command.Of(CommandType.SetGroundStation, 1, "gs_6"));
            Ok(s, Command.Of(CommandType.SetLauncher, 1, "electron"));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 5));
            Assume.That(Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false)[ResourceId.Budget].Margin, Is.LessThan(0));
            Assert.That(SessionRules.OverBudget(s, _data));
            Flow.BuildAll(s, _data);
            Assert.That(SessionRules.LaunchBlockers(s, _data), Is.EqualTo(new[] { "err.over_budget" }));
            Assert.That(Try(s, CommandType.Launch, 1), Is.EqualTo("err.over_budget"));
            // 改回便宜的設定就能發射
            Ok(s, Command.Of(CommandType.Confirm, 1, "", 0));
            Ok(s, Command.Of(CommandType.SetGroundStation, 1, "gs_1"));
            Ok(s, Command.Of(CommandType.SetLauncher, 1, "falcon9_rideshare"));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 3));
            Ok(s, Command.Of(CommandType.Confirm, 1, "", 1));
            Assert.That(SessionRules.CanLaunch(s, _data), Is.EqualTo(SessionRules.Ok));
        }

        [Test]
        public void TheSystemNeverJudgesTheDesignAndABatteryThatDiesInEclipseFailsAtLaunch()
        {
            var s = Solo();
            // 預設的 80 Wh 電池配上最耗電的儀器：撐不過地影
            var hungry = SessionRules.Catalog(_data, s).Where(p => p.Category == PartCategory.Instrument).OrderByDescending(p => p.PowerW).First();
            Ok(s, Command.Of(CommandType.SelectPart, 1, "inst_visible", 0));
            Ok(s, Command.Of(CommandType.SelectPart, 1, hungry.Id, 1));
            var ev = Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false);
            Assume.That(ev.Power.ActualDod, Is.GreaterThan(1.0), "需要一組撐不過地影的設計");
            Flow.BuildAll(s, _data);
            Assert.That(SessionRules.CanLaunch(s, _data), Is.EqualTo(SessionRules.Ok), "設計好不好，系統不擋");
            Ok(s, Command.Of(CommandType.Launch, 1));
            Assert.That(new[] { LaunchOutcome.DeadOnOrbit, LaunchOutcome.Failure }, Has.Member(s.LastLaunch.Outcome));
            Assert.That(s.LastLaunch.Reached, Is.False, "入軌後就斷電也算失敗（會重來）");
        }

        [Test]
        public void TheTickedDesignIsWhatFlies()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.SelectPart, 1, "batt_large", 1));
            Fly(s);
            Assert.That(s.LastLaunch.Design.Get(PartCategory.Battery), Is.EqualTo("batt_large"));
            Assert.That(s.LastLaunch.Design.StableHash(), Is.EqualTo(s.Bench.StableHash()));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
        }

        [Test]
        public void MultiplayerLaunchNeedsEveryoneToCallGo()
        {
            var s = Duo();
            Flow.BuildAll(s, _data);
            Ok(s, Command.Of(CommandType.Launch, 2));
            Assert.That(s.Poll, Is.Not.Null);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design), "點名還沒結束");
            // 點名中不能改設計
            Assert.That(Try(s, CommandType.Confirm, 2, "", 0), Is.EqualTo("err.poll_active"));
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
            var s = Duo();
            Flow.BuildAll(s, _data);
            Ok(s, Command.Of(CommandType.Launch, 2));
            Ok(s, Command.Of(CommandType.Vote, 2, "", 1));
            Ok(s, Command.Of(CommandType.Leave, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch), "剩下的人都喊 GO 了");
        }

        [Test]
        public void APartCarriedByAPlayerWhoLeavesGoesOnTheSatellite()
        {
            var s = Duo();
            Flow.Confirm(s, _data);
            Flow.Assemble(s, _data, "batt_small", 2);
            Ok(s, Command.Of(CommandType.Pick, 2, "batt_small"));
            Assert.That(s.HeldBy(2), Is.EqualTo("batt_small"));
            Assert.That(Try(s, CommandType.Drop, 2), Is.EqualTo("err.carry_part"), "組好的零件沒有地方放，拿起來就是要裝上衛星");
            Ok(s, Command.Of(CommandType.Leave, 2));
            Assert.That(s.Held, Is.Empty);
            Assert.That(s.Installed, Has.Member("batt_small"), "零件不會跟著人一起消失");
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
            foreach (var id in s.Bench.Instruments.ToList()) Ok(s, Command.Of(CommandType.SelectPart, 1, id, 0));
            foreach (var id in good.Instruments) Ok(s, Command.Of(CommandType.SelectPart, 1, id, 1));
            foreach (var cat in PartCategory.Single.Where(c => c != PartCategory.GroundStation))
                Ok(s, Command.Of(CommandType.SelectPart, 1, good.Get(cat), 1));
            Ok(s, Command.Of(CommandType.SetOrbit, 1, good.OrbitId));
            Ok(s, Command.Of(CommandType.SetLauncher, 1, good.LauncherId));
            Ok(s, Command.Of(CommandType.SetGroundStation, 1, good.Get(PartCategory.GroundStation)));
            // 找一個入軌的種子（種子會混入第幾次發射，換年數也會換種子）
            for (int y = 3; y <= 5 && s.LastLaunch.Reached == false; y++)
            {
                if (s.Phase == SessionPhase.Launch)
                {
                    Ok(s, Command.Of(CommandType.FinishLaunch, 1));
                    // 失敗重來之後要整個重選一次
                    foreach (var id in s.Bench.Instruments.ToList()) Ok(s, Command.Of(CommandType.SelectPart, 1, id, 0));
                    foreach (var id in good.Instruments) Ok(s, Command.Of(CommandType.SelectPart, 1, id, 1));
                    foreach (var cat in PartCategory.Single.Where(c => c != PartCategory.GroundStation))
                        Ok(s, Command.Of(CommandType.SelectPart, 1, good.Get(cat), 1));
                    Ok(s, Command.Of(CommandType.SetOrbit, 1, good.OrbitId));
                    Ok(s, Command.Of(CommandType.SetLauncher, 1, good.LauncherId));
                    Ok(s, Command.Of(CommandType.SetGroundStation, 1, good.Get(PartCategory.GroundStation)));
                }
                Ok(s, Command.Of(CommandType.SetYears, 1, "", y));
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
        public void AFailedLaunchStartsTheMissionOverButKeepsWhatWasLearned()
        {
            var s = Solo("earth_observation", null);
            var start = SessionRules.StartDesign(s.Scenario(_data), s.Difficulty(_data));
            var cell = Blanks.Cells(_data, s).First();
            Ok(s, Command.Of(CommandType.Fill, 1, Blanks.FillArg(cell.part, cell.field, 12.5)));
            Ok(s, Command.Of(CommandType.SelectPart, 1, "batt_medium", 1));
            Ok(s, Command.Of(CommandType.SetGroundStation, 1, "gs_6"));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 5));
            // 電子號到不了地球同步軌道：一定失敗
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Fly(s);
            Assert.That(s.LastLaunch.Reached, Is.False);
            var learned = s.Learned.ToList();
            Assert.That(learned, Is.Not.Empty);
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));

            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
            Assert.That(s.Restarts, Is.EqualTo(1));
            Assert.That(s.Bench.Parts, Is.EquivalentTo(start.Parts), "零件和地面站回到開局");
            Assert.That(s.Bench.Parts.Keys, Is.EqualTo(new[] { PartCategory.GroundStation }), "開局沒有勾任何零件");
            Assert.That(s.Bench.Instruments, Is.Empty);
            Assert.That(s.Bench.OrbitId, Is.EqualTo(start.OrbitId));
            Assert.That(s.Bench.LauncherId, Is.EqualTo(start.LauncherId));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(start.MissionYears));
            Assert.That(s.Confirmed, Is.False, "回設計主螢幕重選");
            Assert.That(s.Installed, Is.Empty, "衛星回到開局：重新組裝");
            Assert.That(s.Built, Is.Empty);
            Assert.That(s.Held, Is.Empty);
            Assert.That(s.Comps, Is.Empty);
            Assert.That(s.Benches.All(b => b.Job == null && b.Left.Count == 0));
            // 保留：組過的構造、發射紀錄、玩家填的數值
            Assert.That(s.Learned, Is.EquivalentTo(learned));
            Assert.That(s.Launches.Count, Is.EqualTo(1), "發射紀錄保留（評價要用）");
            Assert.That(s.FailedLaunches, Is.EqualTo(1));
            Assert.That(Blanks.Filled(s, cell.part, cell.field).V, Is.EqualTo(12.5), "已經填的數值保留");
        }

        [Test]
        public void RebuildingTheSameDesignRollsANewLaunch()
        {
            var s = Solo("earth_observation", null);
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Fly(s);
            var first = s.LastLaunch;
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            s.TickDefaults(_data);
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Fly(s);
            Assert.That(s.LastLaunch.Design.StableHash(), Is.EqualTo(first.Design.StableHash()), "照原樣組回同一個設計");
            Assert.That(s.LastLaunch.Seed, Is.Not.EqualTo(first.Seed), "種子混入第幾次發射，不會一直擲出同一個結果");
            Assert.That(first.Seed, Is.EqualTo(first.Design.StableHash()), "第一次發射的種子不變");
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
        public void TutorialOnlyLeavesTheSolarWingToBuildAndOneValueToFind()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "tutorial", true, false);
            Assert.That(s.Bench.Get(PartCategory.Solar), Is.EqualTo("solar_deployable"), "新手訓練的衛星要有展開式太陽能翼（發射動畫看得到展開）");
            Assert.That(SessionRules.NotInstalled(s, _data), Is.EqualTo(new[] { "solar_deployable" }), "其他零件開局就在衛星上，只留一個親手組");
            Assert.That(Blanks.Cells(_data, s), Is.EqualTo(new[] { ("solar_deployable", PartFields.MassKg) }), "只挖空一格");
            Assert.That(Clues.For(_data, s).Count, Is.EqualTo(1));
            var ev = Evaluator.EvaluateDesign(s.Bench, _data);
            var notOk = ev.Resources.Values.Where(r => r.Status != ResourceStatus.Ok).Select(r => r.Id).ToList();
            Assert.That(notOk, Is.EqualTo(new[] { ResourceId.Data }), "教學重點不變：只有儲存一個問題（黃燈：沒有安全餘裕）");
            Assert.That(ev.AnyOver, Is.False);
            Assert.That(ev.Power.ActualDod, Is.LessThanOrEqualTo(1.0), "電池撐得過地影");
            Assert.That(ev.LaunchFeasible);
            // 其他難度、歷史關卡不受影響
            Assert.That(SessionRules.NewSession(_data, "earth_observation", "engineer", false, false).Bench.Get(PartCategory.Solar), Is.Null.Or.Empty, "一般難度開局不先勾");
            Assert.That(SessionRules.NewSession(_data, "hist_oco", "tutorial", true, false).Bench.Get(PartCategory.Solar),
                Is.EqualTo(_data.Scenario("hist_oco").Historical.PresetDesign.Get(PartCategory.Solar)));
        }

        /// <summary>歷史關卡：簡報一結束就照當年的設計發射（不用組裝、不用按發射、不用點名），重演完設計才解鎖。</summary>
        [Test]
        public void HistoricalMissionReplaysRightAfterTheBriefing()
        {
            var s = SessionRules.NewSession(_data, "hist_oco", "engineer", false, false);
            Ok(s, Command.Of(CommandType.Join, 1));
            Assert.That(s.Confirmed, "當年的設計已經定案");
            Assert.That(SessionRules.NotInstalled(s, _data), Is.Empty, "當年的衛星整艘組好了");
            Assert.That(Blanks.Cells(_data, s), Is.Empty, "歷史關卡不挖空");
            Assert.That(Try(s, CommandType.SetLauncher, 1, "delta_ii"), Is.EqualTo("err.locked"));
            Assert.That(Try(s, CommandType.SelectPart, 1, "batt_medium", 1), Is.EqualTo("err.locked"));
            Assert.That(Try(s, CommandType.Confirm, 1, "", 0), Is.EqualTo("err.locked"));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch), "簡報完直接進發射演出");
            Assert.That(s.LastLaunch.Design.LauncherId, Is.EqualTo("taurus_xl"));
            Assert.That(s.LastLaunch.Outcome, Is.EqualTo(LaunchOutcome.FairingFailure));

            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
            Assert.That(s.Restarts, Is.Zero, "歷史關卡失敗不重來，保留設計接著改");
            Assert.That(s.Confirmed, Is.False, "回設計主螢幕接著改");
            Assert.That(SessionRules.NotInstalled(s, _data), Is.Empty, "衛星還在");
            Ok(s, Command.Of(CommandType.SetLauncher, 1, "delta_ii"));
            Fly(s);
            Assert.That(s.LastLaunch.Reached);
            Assert.That(s.FailedLaunches, Is.EqualTo(1), "失敗次數照記（計分時再扣掉重演）");
            Assert.That(s.LastLaunch.FailedBefore, Is.EqualTo(0), "重演不算玩家的失敗");
            Assert.That(SessionRules.Result(s, _data).Score.FirstLaunchBonus, "自己的第一次發射就成功：照樣加分");
        }

        /// <summary>歷史關卡不分難度：不管選哪個難度，規則都一樣，預算是當年的 110%。</summary>
        [TestCase("cadet")]
        [TestCase("engineer")]
        [TestCase("director")]
        public void HistoricalMissionIgnoresDifficulty(string diff)
        {
            var s = SessionRules.NewSession(_data, "hist_oco", diff, false, false);
            Assert.That(s.DifficultyId, Is.EqualTo(_data.Scenario("hist_oco").Difficulty));
            Assert.That(Evaluator.EvaluateDesign(s.Bench, _data, diff, false).Cost.BudgetMusd, Is.EqualTo(273.4 * 1.1).Within(1e-9));
        }

        [Test]
        public void SnapshotRoundTripsExactly()
        {
            var s = Solo("earth_observation", null);
            var cell = Blanks.Cells(_data, s).First();
            Ok(s, Command.Of(CommandType.Fill, 1, Blanks.FillArg(cell.part, cell.field, 3.25)));
            Flow.Confirm(s, _data);
            Flow.Assemble(s, _data, "batt_small");
            Ok(s, Command.Of(CommandType.CompPick, 1, "optics"));
            Ok(s, Command.Of(CommandType.CompPick, 1, "detector"));
            Ok(s, Command.Of(CommandType.BenchPut, 1, "", 1));
            Ok(s, Command.Of(CommandType.CompPick, 1, "cells"));
            var copy = SessionState.FromJson(s.ToJson());
            Assert.That(copy.ToJson(), Is.EqualTo(s.ToJson()));
            Assert.That(copy.CompsOf(1), Is.EqualTo(new[] { "cells" }));
            Assert.That(copy.Benches[0].Job.Done);
            Assert.That(copy.Benches[1].Left, Is.EqualTo(new[] { "optics", "detector" }));
            Assert.That(Blanks.Filled(copy, cell.part, cell.field).By, Is.EqualTo(1));
            Assert.That(copy.Confirmed);
        }

        // ---------------------------------------------------------------- 預算與營運經費

        [Test]
        public void OpsStopWhenTheMoneyRunsOut()
        {
            // 預算調到發射後只剩半年的營運費（用另一份資料，不動共用的那份）
            var data = GameDataLoader.Load(TestData.Files());
            var d = TestData.Balanced();
            var ev0 = Evaluator.EvaluateDesign(d, data, "cadet", false);
            data.Scenario(d.ScenarioId).BudgetMusd = ev0.Cost.DevelopmentMusd + ev0.Cost.LaunchMusd + ev0.Cost.OpsPerYearMusd * 0.5;
            MissionResult r = null;
            for (int seed = 1; seed < 20 && (r == null || !r.ReachedOrbit); seed++)
                r = Simulator.SimulateMission(d, data, seed, new NoDecisions(), new MissionContext { DifficultyId = "cadet" });
            Assume.That(r.ReachedOrbit);
            Assert.That(r.FundedYears, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(r.EndReason, Is.EqualTo(EndReason.Funding));
            Assert.That(r.ActualYears * 365.25, Is.EqualTo(182).Within(1), "大約 182 天停下");
            Assert.That(r.Events.All(o => o.Event.Day < 183), "錢用完之後的事件不會發生");
            Assert.That(r.Causes.Select(c => c.Code), Does.Contain("lifetime_funding"));
            Assert.That(r.Score.Lifetime, Is.LessThan(100));
        }

        [Test]
        public void FundedYearsFollowTheTickedDesign()
        {
            var s = Solo("earth_observation", null);
            double before = SessionRules.FundedYears(s, _data);
            Ok(s, Command.Of(CommandType.SetLauncher, 1, "falcon9"));
            Assert.That(SessionRules.FundedYears(s, _data), Is.LessThan(before), "火箭越貴，剩下的營運經費撐越短");
        }

        // ---------------------------------------------------------------- 取捨說明

        [Test]
        public void TradeoffNotesAreListedWithThisMissionsNumbers()
        {
            var s = Solo("earth_observation", "cadet");
            var ev = Evaluator.EvaluateDesign(s.Bench, _data);
            var notes = Tradeoffs.For(_data, s, ev, Lang.ZhTW);
            Assert.That(notes.Count, Is.GreaterThanOrEqualTo(8));
            foreach (var (note, title, text) in notes)
            {
                Assert.That(title + text, Does.Not.Contain("{"), note.Id);
                Assert.That(Departments.Parse(note.Topic), Is.Not.Null, note.Id);
            }
            Assert.That(notes.Single(n => n.note.Id == "budget").text, Does.Contain("$210M"), "地球觀測的預算");
            Assert.That(Tradeoffs.For(_data, s, ev, Lang.En).Single(n => n.note.Id == "target").text, Does.Contain("up to 3"));
            Assert.That(notes.Single(n => n.note.Id == "battery").text, Does.Contain(ev.Orbit.EclipseMin.ToString("0")));

            // 野火關卡有自己的說明；推薦晨昏軌道那一則只在工程師以上列出
            var cadet = SessionRules.NewSession(_data, "wildfire", "cadet", false, false);
            var eng = SessionRules.NewSession(_data, "wildfire", "engineer", false, false);
            var dir = SessionRules.NewSession(_data, "wildfire", "director", false, false);
            List<string> Ids(SessionState x) => _data.TradeoffsFor(x.Scenario(_data), x.Difficulty(_data)).Select(t => t.Id).ToList();
            Assert.That(Ids(cadet), Has.Member("pass_time").And.No.Member("orbit").And.No.Member("more"));
            Assert.That(Ids(eng), Has.Member("orbit").And.No.Member("more"));
            Assert.That(Ids(dir), Has.Member("orbit").And.Member("more"));
            Assert.That(Tradeoffs.For(_data, dir, Evaluator.EvaluateDesign(dir.Bench, _data), Lang.En).Single(n => n.note.Id == "latency").title, Does.Contain("30"));
        }

        [Test]
        public void EveryRequirementIsListedWithoutAsking()
        {
            // 需求直接列在設計主螢幕上：每個關卡、每個難度都拿得到完整清單，互相拉扯的那幾條照難度開放
            var eo = _data.Scenario("earth_observation");
            Assert.That(Departments.Active(eo, _data.Difficulty("cadet")).Select(a => a.Requirement.Id), Has.No.Member("eo_thermal"));
            Assert.That(Departments.Active(eo, _data.Difficulty("engineer")).Select(a => a.Requirement.Id), Has.Member("eo_thermal").And.No.Member("eo_return_high"));
            Assert.That(Departments.Active(eo, _data.Difficulty("director")).Select(a => a.Requirement.Id), Has.Member("eo_thermal").And.Member("eo_return_high"));
        }
    }
}
