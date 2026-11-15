using System;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>發射前的逐項 Go/No-Go 點名（docs/go_no_go_poll.md）。</summary>
    public class PollTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        void Ok(SessionState s, Command c) => Assert.That(SessionRules.Apply(s, _data, c), Is.EqualTo(SessionRules.Ok), c.Type + " " + c.A);
        string Try(SessionState s, CommandType t, int player, string a = "", int n = 0) => SessionRules.Apply(s, _data, Command.Of(t, player, a, n));
        void Call(SessionState s, int player, string item, bool go) => Ok(s, Command.Of(CommandType.PollCall, player, item, go ? 1 : 0));

        SessionState Solo(string scenario = "earth_observation", string diff = "engineer")
        {
            var s = SessionRules.NewSession(_data, scenario, diff, false, false);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            return s;
        }

        /// <summary>
        /// 多人房，已進入設計階段。預設的席位：1、3 號組裝區，2、4 號中控室；setup 可以在開始前換席位。
        /// </summary>
        SessionState Room(int players, bool strict = false, Action<SessionState> setup = null)
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            for (int p = 1; p <= players; p++) Ok(s, Command.Of(CommandType.Join, p));
            if (strict) Ok(s, Command.Of(CommandType.SetStrict, 1, "", 1));
            setup?.Invoke(s);
            for (int p = 1; p <= players; p++) Ok(s, Command.Of(CommandType.SetReady, p, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            return s;
        }

        const string Mass = "mass", Power = "power", Budget = "budget", Data = "data", Comms = "comms", Time = "time";

        // ---------------------------------------------------------------- 單人

        [Test]
        public void SoloLaunchGoesThroughTheRollCall()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.Launch, 1));
            Assert.That(s.Poll, Is.Not.Null, "單人也要點名");
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design), "還沒發射");
            Assert.That(s.Poll.Owner.Keys, Is.EquivalentTo(PollItems.All));
            Assert.That(s.Poll.Owner.Values.Distinct(), Is.EqualTo(new[] { 1 }), "六項都自己喊");
            Assert.That(s.Poll.Truth.Keys, Is.EquivalentTo(PollItems.All));
            Assert.That(s.Poll.Readings.Keys, Is.EquivalentTo(PollItems.All));
            Assert.That(Try(s, CommandType.SetYears, 1, "", 4), Is.EqualTo("err.poll_active"), "點名中設計鎖住");
            Assert.That(Try(s, CommandType.Launch, 1), Is.EqualTo("err.poll_active"));

            Assert.That(Try(s, CommandType.PollLaunch, 1), Is.EqualTo("err.poll_incomplete"), "六項都喊完才能送出");
            foreach (var item in PollItems.All) Call(s, 1, item, true);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design), "喊完還要送出");
            Ok(s, Command.Of(CommandType.PollLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
            Assert.That(s.Poll, Is.Null);
            Assert.That(s.LastPoll.End, Is.EqualTo(PollEnd.Launched));
            Assert.That(s.LastLaunch.Calls.Select(c => c.Item), Is.EqualTo(PollItems.All), "報告要用的紀錄");
            Assert.That(s.LastLaunch.Calls.All(c => c.Go && c.By == 1 && !c.Proxy));
        }

        [Test]
        public void SoloAnswersCanChangeUntilSubmittedAndAnyNoGoHolds()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.Launch, 1));
            Call(s, 1, Mass, false);
            Assert.That(s.Poll, Is.Not.Null, "單人：送出前不結算");
            Call(s, 1, Mass, true);
            foreach (var item in PollItems.All.Where(k => k != Mass)) Call(s, 1, item, item != Power);
            Ok(s, Command.Of(CommandType.PollLaunch, 1));
            Assert.That(s.Poll, Is.Null);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design), "HOLD：回去改設計");
            Assert.That(s.Launches, Is.Empty);
            Assert.That(s.LastPoll.End, Is.EqualTo(PollEnd.Hold));
            Assert.That(s.LastPoll.NoGo, Is.EqualTo(new[] { Power }), "改口之後質量是 GO");
            Assert.That(s.Holds.Single().Calls.Count, Is.EqualTo(6));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 4));
            PollFlow.Fly(s, _data);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch), "重新點名，六項都 GO 就發射");
        }

        [Test]
        public void HistoricalReplayAndTheOldVoteSkipTheRollCall()
        {
            var s = SessionRules.NewSession(_data, "hist_oco", "engineer", false, false);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch), "重演歷史：當年的設計不能改，不用點名");
            Assert.That(s.LastLaunch.Calls, Is.Empty);
            Assert.That(SessionRules.Result(s, _data).Score.PollPenalty, Is.Zero);

            var solo = Solo();
            Ok(solo, Command.Of(CommandType.Launch, 1));
            Assert.That(Try(solo, CommandType.Vote, 1, "", 1), Is.EqualTo("err.unknown"), "舊的全員投票拿掉了");
            Assert.That(Try(solo, CommandType.PollCall, 1, "weather", 1), Is.EqualTo("err.unknown"));
            Assert.That(Try(Solo(), CommandType.PollCall, 1, Mass, 1), Is.EqualTo("err.no_poll"));
        }

        // ---------------------------------------------------------------- 答案與對答案

        [Test]
        public void TheAnswerIsFixedWhenThePollStarts()
        {
            var s = Solo();
            // 電子號到不了地球同步軌道：質量超標
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Ok(s, Command.Of(CommandType.Launch, 1));
            Assert.That(s.Poll.Truth[Mass], Is.EqualTo(ResourceStatus.Over));
            string Truth() => string.Join(",", PollItems.All.Select(k => k + "=" + s.Poll.Truth[k]));
            var before = Truth();
            double used = s.Poll.Readings[Mass].Used;
            // 就算有人繞過鎖改了設計，答案和讀數也不變
            s.Bench.OrbitId = _data.Scenario("earth_observation").DefaultDesign.OrbitId;
            Call(s, 1, Mass, true);
            Assert.That(Truth(), Is.EqualTo(before));
            foreach (var item in PollItems.All.Where(k => k != Mass)) Call(s, 1, item, true);
            Ok(s, Command.Of(CommandType.PollLaunch, 1));
            var mass = s.LastLaunch.Calls.Single(c => c.Item == Mass);
            Assert.That(mass.Truth, Is.EqualTo(ResourceStatus.Over));
            Assert.That(mass.Reading.Used, Is.EqualTo(used));
            Assert.That(mass.Verdict, Is.EqualTo(PollVerdict.BadRelease));
        }

        [Test]
        public void VerdictsFollowTheStatusAtPollTime()
        {
            Assert.That(PollItems.Verdict(ResourceStatus.Over, true), Is.EqualTo(PollVerdict.BadRelease), "超標喊 GO：放行錯誤");
            Assert.That(PollItems.Verdict(ResourceStatus.Over, false), Is.EqualTo(PollVerdict.Correct));
            Assert.That(PollItems.Verdict(ResourceStatus.Ok, true), Is.EqualTo(PollVerdict.Correct));
            Assert.That(PollItems.Verdict(ResourceStatus.Ok, false), Is.EqualTo(PollVerdict.FalseAlarm), "有餘量喊 NO-GO：誤報");
            Assert.That(PollItems.Verdict(ResourceStatus.Tight, true), Is.EqualTo(PollVerdict.RiskyGo), "餘量低：喊 GO 不算錯，標冒險放行");
            Assert.That(PollItems.Verdict(ResourceStatus.Tight, false), Is.EqualTo(PollVerdict.Correct));

            Assert.That(PollItems.ItemFor("launch.overweight"), Is.EqualTo(Mass));
            Assert.That(PollItems.ItemFor("launch.unreachable"), Is.EqualTo(Mass));
            Assert.That(PollItems.ItemFor("launch.lower_orbit"), Is.EqualTo(Mass));
            Assert.That(PollItems.ItemFor("launch.dead_on_orbit"), Is.EqualTo(Power));
            Assert.That(PollItems.ItemFor("launch.anomaly"), Is.Null, "火箭自己出事不是哪一項喊錯");
            Assert.That(PollItems.StationOf(Budget), Is.EqualTo(Station.Control));
            Assert.That(PollItems.StationOf(Time), Is.EqualTo(Station.Control));
            Assert.That(PollItems.All.Count(k => PollItems.StationOf(k) == Station.Assembly), Is.EqualTo(4));
        }

        [Test]
        public void BadReleasesCostEfficiency()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            PollFlow.Fly(s, _data);
            var l = s.LastLaunch;
            var bad = SessionRules.BadReleases(l);
            Assert.That(bad.Select(c => c.Item), Has.Member(Mass), "質量超標還喊 GO");
            Assert.That(bad.Count, Is.EqualTo(l.Calls.Count(c => c.Truth == ResourceStatus.Over)), "全部喊 GO：每個超標的項目都是放行錯誤");
            Assert.That(SessionRules.RiskyGos(l).Count, Is.EqualTo(l.Calls.Count(c => c.Truth == ResourceStatus.Tight)));
            var r = SessionRules.Result(s, _data);
            var ev = Evaluator.EvaluateDesign(l.Design, _data, s.DifficultyId, false);
            Assert.That(r.Score.PollPenalty, Is.EqualTo(Scoring.BadReleasePenalty * bad.Count));
            Assert.That(r.Score.Efficiency, Is.EqualTo(Math.Max(0, Scoring.EfficiencyScore(ev) - r.Score.PollPenalty)).Within(1e-9));
            Assert.That(SessionRules.ResultOf(l, s, _data).Score.PollPenalty, Is.EqualTo(r.Score.PollPenalty), "失敗看板重算也一樣");
        }

        [Test]
        public void FalseAlarmsAreRecordedAndScoredButACaughtMistakeIsNot()
        {
            var s = Solo();
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "geo"));
            Ok(s, Command.Of(CommandType.Launch, 1));
            var fine = PollItems.All.FirstOrDefault(k => s.Poll.Truth[k] == ResourceStatus.Ok);
            Assume.That(fine, Is.Not.Null, "需要一個有餘量的項目");
            // 質量超標卻喊 GO（放行錯誤），但另一項喊了 NO-GO：這次沒有放行出去
            foreach (var item in PollItems.All) Call(s, 1, item, item != fine);
            Ok(s, Command.Of(CommandType.PollLaunch, 1));
            var hold = s.Holds.Single();
            Assert.That(hold.Attempt, Is.EqualTo(1));
            Assert.That(hold.Calls.Single(c => c.Item == fine).Verdict, Is.EqualTo(PollVerdict.FalseAlarm));
            Assert.That(hold.Calls.Single(c => c.Item == Mass).Verdict, Is.EqualTo(PollVerdict.BadRelease));

            // 修好（換回到得了的軌道）再點名
            Ok(s, Command.Of(CommandType.SetOrbit, 1, _data.Scenario("earth_observation").DefaultDesign.OrbitId));
            PollFlow.Fly(s, _data);
            var l = s.LastLaunch;
            Assert.That(SessionRules.BadReleases(l).Select(c => c.Item), Has.No.Member(Mass), "HOLD 之後修好再點名：不算放行錯誤");
            Assert.That(SessionRules.FalseAlarms(s, l).Select(c => c.Item), Is.EqualTo(new[] { fine }), "誤報照記");
            Assert.That(SessionRules.Result(s, _data).Score.PollPenalty,
                Is.EqualTo(Scoring.FalseAlarmPenalty + Scoring.BadReleasePenalty * SessionRules.BadReleases(l).Count));
        }

        // ---------------------------------------------------------------- 多人

        [Test]
        public void ItemsGoToWhoeverCanSeeTheNumber()
        {
            var duo = Room(2);
            Ok(duo, Command.Of(CommandType.Launch, 2));
            Assert.That(duo.Poll.CalledBy, Is.EqualTo(2), "發起的人是飛行總監");
            foreach (var item in new[] { Mass, Power, Data, Comms }) Assert.That(duo.Poll.Owner[item], Is.EqualTo(1), item + "：組裝區");
            foreach (var item in new[] { Budget, Time }) Assert.That(duo.Poll.Owner[item], Is.EqualTo(2), item + "：中控室");

            // 同一席位兩個人：輪流分
            var four = Room(4);
            Ok(four, Command.Of(CommandType.Launch, 2));
            Assert.That(new[] { Mass, Power, Data, Comms }.Select(k => four.Poll.Owner[k]), Is.EqualTo(new[] { 1, 3, 1, 3 }));
            Assert.That(new[] { Budget, Time }.Select(k => four.Poll.Owner[k]), Is.EqualTo(new[] { 2, 4 }));

            // 中控室三個人只有兩項：從項目最多的人那裡拿一項，每個人至少一項
            var crowded = Room(4, false, s => Ok(s, Command.Of(CommandType.SetStation, 3, "", 1)));
            Ok(crowded, Command.Of(CommandType.Launch, 2));
            for (int p = 1; p <= 4; p++) Assert.That(crowded.Poll.Owner.Values, Has.Member(p), "玩家 " + p);
            Assert.That(crowded.Poll.Owner.Count, Is.EqualTo(6));

            // 組裝區沒人：那四項改由中控室負責
            var control = Room(2, false, s => Ok(s, Command.Of(CommandType.SetStation, 1, "", 1)));
            Ok(control, Command.Of(CommandType.Launch, 1));
            Assert.That(control.Poll.Owner.Values.Distinct().OrderBy(x => x), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(control.Poll.Owner.Count, Is.EqualTo(6));
        }

        [Test]
        public void MultiplayerAnyNoGoHoldsAtOnceAndOnlyTheDirectorLaunches()
        {
            var s = Room(2);
            Ok(s, Command.Of(CommandType.Launch, 2));
            Call(s, 2, Budget, true);
            Assert.That(s.Poll, Is.Not.Null);
            Call(s, 1, Power, false);
            Assert.That(s.Poll, Is.Null, "有人喊 NO-GO：馬上 HOLD");
            Assert.That(s.LastPoll.End, Is.EqualTo(PollEnd.Hold));
            Assert.That(s.LastPoll.NoGoBy, Is.EqualTo(1));
            Assert.That(s.LastPoll.NoGo, Is.EqualTo(new[] { Power }));
            Assert.That(s.Launches, Is.Empty);
            Assert.That(s.Holds.Single().Calls.Select(c => c.Item), Is.EqualTo(new[] { Power, Budget }), "只記喊過的");
            Ok(s, Command.Of(CommandType.SetYears, 2, "", 4));

            // 第二次：六項都 GO，等飛行總監放行
            Ok(s, Command.Of(CommandType.Launch, 2));
            PollFlow.CallAll(s, _data, true);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design), "都 GO 了也要等飛行總監");
            Assert.That(s.Poll.AllGo());
            Assert.That(Try(s, CommandType.PollLaunch, 1), Is.EqualTo("err.poll_director"));
            // 放行前還可以改口叫停
            Call(s, 1, Mass, false);
            Assert.That(s.LastPoll.End, Is.EqualTo(PollEnd.Hold));

            PollFlow.Fly(s, _data, 2);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
            Assert.That(s.LastLaunch.Calls.Single(c => c.Item == Time).By, Is.EqualTo(2), "報告寫得出是誰喊的");
            Assert.That(SessionRules.HoldsFor(s, s.LastLaunch).Count, Is.EqualTo(2));
        }

        [Test]
        public void YouCanOnlyCallYourOwnItems()
        {
            // 嚴格分工：只能喊分配給自己的，也不開放代喊
            var strict = Room(2, true);
            Ok(strict, Command.Of(CommandType.Launch, 2));
            Assert.That(Try(strict, CommandType.PollCall, 2, Mass, 1), Is.EqualTo("err.poll_not_yours"));
            Assert.That(Try(strict, CommandType.PollProxy, 1), Is.EqualTo("err.poll_not_yours"));
            Assert.That(Try(strict, CommandType.PollCall, 2, Mass, 1), Is.EqualTo("err.poll_not_yours"));
            Call(strict, 1, Mass, true);

            // 不是嚴格分工：負責人太久沒喊（房主的計時器開放代喊），其他人才可以代喊，報告標出來
            var s = Room(2);
            Ok(s, Command.Of(CommandType.Launch, 2));
            Assert.That(Try(s, CommandType.PollCall, 2, Mass, 1), Is.EqualTo("err.poll_not_yours"), "還沒開放代喊");
            Assert.That(Try(s, CommandType.PollProxy, 2), Is.EqualTo("err.host_only"));
            Call(s, 1, Power, true);
            Ok(s, Command.Of(CommandType.PollProxy, 1));
            Assert.That(Try(s, CommandType.PollCall, 2, Power, 0), Is.EqualTo("err.poll_not_yours"), "負責人喊過的不能代改");
            Call(s, 2, Mass, true);
            Call(s, 1, Mass, true);
            Assert.That(s.Poll.By[Mass], Is.EqualTo(1), "負責人可以自己再喊一次");
            Call(s, 2, Data, true);
            PollFlow.CallAll(s, _data, true, onlyPending: true);
            Ok(s, Command.Of(CommandType.PollLaunch, 2));
            var data = s.LastLaunch.Calls.Single(c => c.Item == Data);
            Assert.That(data.Proxy && data.By == 2 && data.Owner == 1, "代喊");
            Assert.That(s.LastLaunch.Calls.Count(c => c.Proxy), Is.EqualTo(1));
        }

        [Test]
        public void TimeoutAndCancelCostNothing()
        {
            var s = Room(2);
            Ok(s, Command.Of(CommandType.Launch, 1));
            Call(s, 2, Budget, true);
            Assert.That(Try(s, CommandType.PollCancel, 2), Is.EqualTo("err.poll_director"), "只有飛行總監能取消");
            Ok(s, Command.Of(CommandType.PollCancel, 1));
            Assert.That(s.Poll, Is.Null);
            Assert.That(s.LastPoll.End, Is.EqualTo(PollEnd.Cancelled));

            // 逾時：房主的計時器送出（飛行總監不是房主也一樣）
            Ok(s, Command.Of(CommandType.Launch, 2));
            Call(s, 1, Mass, true);
            Ok(s, Command.Of(CommandType.PollCancel, 1, "", 1));
            Assert.That(s.LastPoll.End, Is.EqualTo(PollEnd.TimedOut));
            Assert.That(s.Holds, Is.Empty, "沒有代價，也不留紀錄");
            Assert.That(s.Launches, Is.Empty);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
            Ok(s, Command.Of(CommandType.SetYears, 2, "", 4));
            Assert.That(Try(s, CommandType.PollCancel, 1), Is.EqualTo("err.no_poll"));
        }

        [Test]
        public void SomeoneLeavingHandsTheirItemsOver()
        {
            // 三個人：1、3 號組裝區，2 號中控室（飛行總監）
            var s = Room(3);
            Ok(s, Command.Of(CommandType.Launch, 2));
            Assert.That(s.Poll.Owner[Power], Is.EqualTo(3));
            Call(s, 3, Power, true);
            Ok(s, Command.Of(CommandType.Leave, 3));
            Assert.That(s.Poll, Is.Not.Null, "點名繼續");
            Assert.That(new[] { Mass, Power, Data, Comms }.Select(k => s.Poll.Owner[k]).Distinct(), Is.EqualTo(new[] { 1 }), "改給同席位的人");
            Assert.That(s.Poll.Calls[Power], Is.True, "已經喊的保留");

            // 飛行總監離線：換人當，他的項目給剩下的人；只剩一個人就照單人的規則（送出才結算）
            Ok(s, Command.Of(CommandType.Leave, 2));
            Assert.That(s.Poll.CalledBy, Is.EqualTo(1));
            Assert.That(s.Poll.Owner.Values.Distinct(), Is.EqualTo(new[] { 1 }));
            Call(s, 1, Budget, false);
            Assert.That(s.Poll, Is.Not.Null);
            Call(s, 1, Budget, true);
            PollFlow.CallAll(s, _data, true, onlyPending: true);
            Ok(s, Command.Of(CommandType.PollLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
            Assert.That(s.LastLaunch.Calls.Single(c => c.Item == Power).By, Is.EqualTo(3));
        }

        [Test]
        public void ThePhoneAndTheBenchAreFrozenDuringThePoll()
        {
            var s = Solo();
            var dep = _data.Departments[0];
            Ok(s, Command.Of(CommandType.Call, 1, dep.Id, 0));
            Ok(s, Command.Of(CommandType.Launch, 1));
            Ok(s, Command.Of(CommandType.Call, 1, dep.Id, 0));
            Assert.That(Try(s, CommandType.Call, 1, dep.Id, 1), Is.EqualTo("err.poll_active"), "新問題會花時程，答案就不準了");
            Assert.That(Try(s, CommandType.CompFit, 1), Is.EqualTo("err.poll_active"));
            Ok(s, Command.Of(CommandType.PollCancel, 1));
            Ok(s, Command.Of(CommandType.Call, 1, dep.Id, 1));
        }

        [Test]
        public void StateSurvivesSerialization()
        {
            var s = Room(2);
            Ok(s, Command.Of(CommandType.Launch, 2));
            Call(s, 2, Budget, true);
            Call(s, 1, Mass, false);
            Ok(s, Command.Of(CommandType.Launch, 2));
            Ok(s, Command.Of(CommandType.PollProxy, 1));
            Call(s, 2, Mass, true);
            var open = s.Clone();
            Assert.That(open.ToJson(), Is.EqualTo(s.ToJson()));
            Assert.That(open.Poll.Owner, Is.EquivalentTo(s.Poll.Owner));
            Assert.That(open.Poll.Truth, Is.EquivalentTo(s.Poll.Truth));
            Assert.That(open.Poll.ProxyOpen);
            Assert.That(open.Poll.Readings[Power].ExtraLimit, Is.EqualTo(s.Poll.Readings[Power].ExtraLimit));
            Assert.That(open.LastPoll.NoGo, Is.EqualTo(new[] { Mass }));
            Assert.That(open.Holds.Single().Calls.Single(c => !c.Go).Item, Is.EqualTo(Mass));

            PollFlow.CallAll(s, _data, true, onlyPending: true);
            Ok(s, Command.Of(CommandType.PollLaunch, 2));
            var flown = s.Clone();
            Assert.That(flown.ToJson(), Is.EqualTo(s.ToJson()));
            Assert.That(flown.LastLaunch.Calls.Count, Is.EqualTo(6));
            Assert.That(flown.LastLaunch.Calls.Single(c => c.Item == Mass).Proxy);
            Assert.That(SessionRules.Result(flown, _data).Score.PollPenalty, Is.EqualTo(SessionRules.Result(s, _data).Score.PollPenalty));
        }

        [Test]
        public void TimersComeFromTheDataFile()
        {
            var c = _data.Constants;
            Assert.That(c.PollProxySeconds, Is.GreaterThan(0));
            Assert.That(c.PollTimeoutSeconds, Is.GreaterThan(c.PollProxySeconds));
            Assert.That(c.Sources.Keys, Has.Member("poll_timeout_seconds"));
            Assert.That(c.Sources.Keys, Has.Member("poll_proxy_seconds"));
        }
    }

    /// <summary>測試用：把發射前的點名走完。</summary>
    public static class PollFlow
    {
        /// <summary>發起點名、每一項由負責人喊 GO、飛行總監送出。</summary>
        public static void Fly(SessionState s, GameData data, int director = 1)
        {
            Do(s, data, Command.Of(CommandType.Launch, director));
            CallAll(s, data, true);
            Do(s, data, Command.Of(CommandType.PollLaunch, director));
        }

        /// <summary>每一項由負責人喊（onlyPending：只喊還沒人喊的）。</summary>
        public static void CallAll(SessionState s, GameData data, bool go, bool onlyPending = false)
        {
            foreach (var item in PollItems.All)
            {
                if (s.Poll == null) return;
                if (onlyPending && s.Poll.Called(item)) continue;
                Do(s, data, Command.Of(CommandType.PollCall, s.Poll.Owner[item], item, go ? 1 : 0));
            }
        }

        static void Do(SessionState s, GameData data, Command c) =>
            Assert.That(SessionRules.Apply(s, data, c), Is.EqualTo(SessionRules.Ok), c.Type + " " + c.A);
    }
}
