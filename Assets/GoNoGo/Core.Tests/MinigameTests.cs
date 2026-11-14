using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>營運小遊戲：每個遊戲都用自動駕駛驗證「不玩會輸、會玩會贏、同種子同結果、不會卡住」，再測多人過關規則和處置流程。</summary>
    public class MinigameTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        static readonly int[] Seeds = Enumerable.Range(1, 12).ToArray();

        static IEnumerable<string> GameIds => Minigames.All.Select(d => d.Id);

        static MiniGame Run(string id, int severity, int seed, float skill, bool idle, int players = 1)
        {
            var g = Minigames.Create(id, new MinigameContext { Severity = severity, Players = players }, seed);
            float limit = g.MaxSeconds + 10;
            while (g.State == MiniState.Running && g.Time < limit)
                g.Step(1f / 60f, idle ? default : g.Autopilot(skill));
            return g;
        }

        [Test]
        public void EveryEventKindHasAGame()
        {
            foreach (var k in OpsEvents.All.Concat(new[] { EventKind.Alien }))
                Assert.That(Minigames.For(k), Is.Not.Null, "事件 " + OpsEvents.Key(k) + " 沒有對應的小遊戲");
        }

        [Test]
        public void RegistryIsConsistent()
        {
            Assert.That(Minigames.All.Select(d => d.Id).Distinct().Count(), Is.EqualTo(Minigames.All.Count), "id 不能重複");
            Assert.That(Minigames.All.Select(d => d.Kind).Distinct().Count(), Is.EqualTo(Minigames.All.Count), "一種事件只能有一個遊戲");
            foreach (var d in Minigames.All)
            {
                Assert.That(d.PassScore, Is.InRange(1, 1000), d.Id);
                Assert.That(d.RatioNeeded, Is.InRange(0.1, 1.0), d.Id);
                Assert.That(Minigames.For(OpsEvents.Key(d.Kind)), Is.SameAs(d), d.Id);
            }
        }

        [Test]
        public void NotPlayingNeverWins()
        {
            foreach (var id in GameIds)
                foreach (var sev in new[] { 1, 2, 3 })
                    foreach (var seed in Seeds)
                    {
                        var g = Run(id, sev, seed, 0, true);
                        Assert.That(g.State, Is.Not.EqualTo(MiniState.Won), $"{id} sev{sev} seed{seed}：什麼都不做就贏了");
                    }
        }

        [Test]
        public void AGoodPlayerAlwaysWins()
        {
            foreach (var id in GameIds)
                foreach (var sev in new[] { 1, 2, 3 })
                    foreach (var seed in Seeds)
                    {
                        var g = Run(id, sev, seed, 1, false);
                        Assert.That(g.State, Is.EqualTo(MiniState.Won), $"{id} sev{sev} seed{seed}：自動駕駛 {g.Time:0.0} 秒後是 {g.State}，分數 {g.Score}");
                        Assert.That(g.Time, Is.LessThan(g.MaxSeconds), $"{id} sev{sev} seed{seed}：超過時間上限");
                    }
        }

        [Test]
        public void ABadPlayerUsuallyLoses()
        {
            foreach (var id in GameIds)
            {
                int wins = 0, total = 0;
                foreach (var sev in new[] { 1, 2, 3 })
                    foreach (var seed in Seeds)
                    {
                        total++;
                        if (Run(id, sev, seed, 0.0f, false).State == MiniState.Won) wins++;
                    }
                Assert.That(wins, Is.LessThan(total * 0.5), $"{id}：技術 0 的自動駕駛贏了 {wins}/{total}，太容易了");
            }
        }

        [Test]
        public void ClicksSurviveAnyFrameRate()
        {
            // 螢幕刷新率比 60Hz 高時，有些影格一步都不會跑；「剛按下」的訊號不能因此被吞掉（玩家會覺得點擊有時沒反應）
            foreach (var id in GameIds)
                foreach (var fps in new[] { 30f, 144f, 240f })
                    foreach (var seed in new[] { 1, 2, 3 })
                    {
                        var g = Minigames.Create(id, new MinigameContext { Severity = 2 }, seed);
                        float dt = 1f / fps, limit = g.MaxSeconds + 10;
                        while (g.State == MiniState.Running && g.Time < limit) g.Step(dt, g.Autopilot(1));
                        Assert.That(g.State, Is.EqualTo(MiniState.Won), $"{id} 在 {fps}fps 下沒通關（點擊被吞掉？）");
                    }
        }

        [Test]
        public void SameSeedSameResult()
        {
            foreach (var id in GameIds)
            {
                var a = Run(id, 2, 7, 1, false);
                var b = Run(id, 2, 7, 1, false);
                Assert.That(b.Score, Is.EqualTo(a.Score), id);
                Assert.That(b.State, Is.EqualTo(a.State), id);
                Assert.That(b.Time, Is.EqualTo(a.Time).Within(1e-4), id);
            }
        }

        [Test]
        public void FinalScoreAgreesWithState()
        {
            foreach (var id in GameIds)
                foreach (var idle in new[] { true, false })
                {
                    var g = Run(id, 2, 3, 1, idle);
                    Assert.That(g.FinalScore >= g.Def.PassScore, Is.EqualTo(g.State == MiniState.Won), $"{id} idle={idle}：分數和通關對不上");
                    Assert.That(g.FinalScore, Is.InRange(0, 1000), id);
                }
        }

        [Test]
        public void DrawingAndStatsNeverThrowAndStayBounded()
        {
            var shapes = new List<MiniShape>();
            foreach (var id in GameIds)
            {
                var g = Minigames.Create(id, new MinigameContext { Severity = 3 }, 5);
                for (int i = 0; i < 60 * 40 && g.State == MiniState.Running; i++)
                {
                    g.Step(1f / 60f, g.Autopilot(0.7f));
                    if (i % 20 != 0) continue;
                    shapes.Clear();
                    g.Draw(shapes);
                    Assert.That(shapes.Count, Is.InRange(1, 4000), id + " 形狀數量");
                    foreach (var sh in shapes) Assert.That(float.IsNaN(sh.X) || float.IsNaN(sh.Y) || float.IsNaN(sh.A), Is.False, id + " 座標有 NaN");
                    Assert.That(g.Stats.Count(), Is.GreaterThan(0), id);
                    foreach (var st in g.Stats) Assert.That(st.Key, Does.StartWith("mini."), id);
                }
            }
        }

        [Test]
        public void EverySpriteTheGamesDrawExistsOnDisk()
        {
            // 遊戲畫的每一張貼圖（Resources/Minigames/<遊戲>/<名字>.png）都要真的有檔案，不然畫面上會出現洋紅色框
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(TestData.Dir, "..", "..", "..", "Resources", "Minigames"));
            var shapes = new List<MiniShape>();
            foreach (var id in GameIds)
                foreach (var sev in new[] { 1, 3 })
                {
                    var g = Minigames.Create(id, new MinigameContext { Severity = sev, Hull = sev == 1 ? 0 : 2, Redundant = sev == 1, RefId = sev == 1 ? "wildfire" : "grassland" }, 4);
                    for (int i = 0; i < 60 * 45 && g.State == MiniState.Running; i++)
                    {
                        g.Step(1f / 60f, g.Autopilot(0.8f));
                        if (i % 30 != 0 && g.State == MiniState.Running) continue;
                        shapes.Clear();
                        g.Draw(shapes);
                        foreach (var sh in shapes.Where(x => x.Kind == MiniShapeKind.Sprite))
                        {
                            Assert.That(sh.Str, Does.StartWith(id + "/"), id + "：貼圖名字要放在自己的資料夾（遊戲id/名字）");
                            Assert.That(System.IO.File.Exists(System.IO.Path.Combine(dir, sh.Str + ".png")), Is.True, id + "：缺貼圖 " + sh.Str);
                            Assert.That(sh.A, Is.GreaterThan(0), id); Assert.That(sh.B, Is.GreaterThan(0), id);
                        }
                    }
                }
        }

        [Test]
        public void ContextReflectsDesign()
        {
            var good = TestData.Balanced();
            var c = MinigameContext.From(good, _data, new MissionEvent { Kind = EventKind.SolarStorm, Severity = 2 }, 3);
            Assert.That(c.Severity, Is.EqualTo(2));
            Assert.That(c.Players, Is.EqualTo(3));
            Assert.That(c.Hull, Is.InRange(0, 2));
            Assert.That(c.Steady, Is.InRange(0.0, 1.0));
            Assert.That(c.Margin, Is.InRange(0.0, 1.0));
        }

        // ---------------------------------------------------------------- 多人過關規則

        [Test]
        public void RatioRuleNeedsEnoughPlayersToPass()
        {
            var def = new MinigameDef { Rule = MiniRule.Ratio, PassScore = 1000, RatioNeeded = 0.5 };
            Assert.That(MinigameRules.TeamPassed(def, new[] { 1000 }, 1), Is.True);
            Assert.That(MinigameRules.TeamPassed(def, new[] { 400 }, 1), Is.False);
            Assert.That(MinigameRules.TeamPassed(def, new[] { 1000, 200 }, 2), Is.True, "兩人一半 = 1 人過");
            Assert.That(MinigameRules.TeamPassed(def, new[] { 1000, 1000, 200 }, 4), Is.True, "四人兩人過");
            Assert.That(MinigameRules.TeamPassed(def, new[] { 1000, 200, 100 }, 4), Is.False, "四人只有一人過");
            Assert.That(MinigameRules.TeamPassed(def, new[] { 1000 }, 4), Is.False, "沒交成績的算沒過");
        }

        [Test]
        public void SumRuleAveragesAllPlayers()
        {
            var def = new MinigameDef { Rule = MiniRule.Sum, PassScore = 600 };
            Assert.That(MinigameRules.TeamPassed(def, new[] { 900, 400 }, 2), Is.True);
            Assert.That(MinigameRules.TeamPassed(def, new[] { 900, 100 }, 2), Is.False);
            Assert.That(MinigameRules.TeamPassed(def, new[] { 1000, 1000 }, 4), Is.False, "沒交的兩人算 0 分");
            Assert.That(MinigameRules.TeamPassed(def, new[] { 700 }, 1), Is.True);
        }

        // ---------------------------------------------------------------- 處置流程（外星人彩蛋）

        /// <summary>找一個「會入軌、而且計畫裡有外星人事件」的種子。</summary>
        (SessionState s, MissionEvent alien) AlienSession(int players)
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, players > 1);
            for (int p = 1; p <= players; p++) SessionRules.Apply(s, _data, Command.Of(CommandType.Join, p));
            SessionRules.Apply(s, _data, Command.Of(CommandType.BeginDesign, 1));
            var design = GameData.DefaultDesign(_data.Scenario("earth_observation"), "engineer").Clone();
            design.ScenarioId = "earth_observation";
            for (int seed = 1; seed < 20000; seed++)
            {
                var plan = Simulator.PlanMission(design, _data, seed, "engineer");
                var alien = plan.Events.FirstOrDefault(e => e.Kind == EventKind.Alien);
                if (alien == null) continue;
                var r = Simulator.SimulateMission(design, _data, seed, new NoDecisions(), new MissionContext { DifficultyId = "engineer" });
                if (!r.ReachedOrbit || !r.Events.Any(o => o.Event.Id == alien.Id)) continue;
                s.Launches.Add(new LaunchRecord { Attempt = 1, Design = design, Seed = seed, Reached = true, Outcome = r.Launch.Outcome, ReasonCode = r.Launch.ReasonCode });
                s.Phase = SessionPhase.Ops;
                s.OpsTurn = alien.Turn;
                s.TurnResolved = false;
                s.Responses.Clear();
                return (s, alien);
            }
            Assert.Fail("找不到有外星人事件的種子");
            return (null, null);
        }

        string Do(SessionState s, CommandType t, int player, string a = "", int n = 0) =>
            SessionRules.Apply(s, _data, Command.Of(t, player, a, n));

        [Test]
        public void AlienIsHiddenFromTheManualAndJudgeList()
        {
            Assert.That(OpsEvents.All, Has.No.Member(EventKind.Alien));
            Assert.That(OpsEvents.Parse("alien"), Is.EqualTo(EventKind.Alien));
            Assert.That(OpsEvents.Key(EventKind.Alien), Is.EqualTo("alien"));
            foreach (var d in _data.Difficulties)
                Assert.That(GameData.EventTypes(_data.Scenario("earth_observation"), d), Has.No.Member("alien"));
        }

        [Test]
        public void AlienReplacesOnlyDuplicatesAndNeverTheFirstTurn()
        {
            var design = GameData.DefaultDesign(_data.Scenario("earth_observation"), "director").Clone();
            design.ScenarioId = "earth_observation";
            int aliens = 0;
            for (int seed = 1; seed < 3000; seed++)
            {
                var plan = Simulator.PlanMission(design, _data, seed, "director");
                Assert.That(plan.Events[0].Kind, Is.Not.EqualTo(EventKind.Alien), "第一回合不會是外星人");
                aliens += plan.Events.Count(e => e.Kind == EventKind.Alien);
                Assert.That(plan.Events.Count(e => e.Kind == EventKind.Alien), Is.LessThanOrEqualTo(1));
            }
            Assert.That(aliens, Is.InRange(3000 * 0.04, 3000 * 0.13), "出現機率大約 " + OpsEvents.AlienChance);
        }

        [Test]
        public void PassingTheGameCompletesTheProcedure()
        {
            var (s, alien) = AlienSession(1);
            Assert.That(Do(s, CommandType.Judge, 1, "alien"), Is.EqualTo(SessionRules.Ok));
            Assert.That(Do(s, CommandType.MiniDone, 1, "solar_storm_game", 800), Is.EqualTo("err.minigame"), "遊戲要對得上判斷");
            Assert.That(Do(s, CommandType.MiniDone, 1, "alien", 800), Is.EqualTo(SessionRules.Ok));
            Assert.That(s.TurnResolved, Is.True, "一個人的房間交了就結算");
            Assert.That(s.Responses[alien.Id].MiniPassed, Is.True);
            Assert.That(s.Responses[alien.Id].Actions, Is.EqualTo(Procedures.Required(EventKind.Alien)));
            var o = SessionRules.Result(s, _data).Events.First(x => x.Event.Id == alien.Id);
            Assert.That(o.ResultCode, Is.EqualTo("event.alien.repelled"));
            Assert.That(o.JudgedCorrect, Is.True);
            Assert.That(o.StepsDone, Is.EqualTo(o.StepsRequired));
            Assert.That(o.MiniGame, Is.EqualTo("alien"));
            Assert.That(o.MiniPlayed && o.MiniPassed, Is.True);
            Assert.That(o.ScienceDelta, Is.GreaterThan(0));
        }

        [Test]
        public void FailingTheGameCostsYou()
        {
            var (s, alien) = AlienSession(1);
            Do(s, CommandType.Judge, 1, "alien");
            Assert.That(Do(s, CommandType.MiniDone, 1, "alien", 100), Is.EqualTo(SessionRules.Ok));
            Assert.That(s.TurnResolved, Is.True);
            Assert.That(s.Responses[alien.Id].MiniPassed, Is.False);
            Assert.That(s.Responses[alien.Id].Actions, Is.Empty);
            var o = SessionRules.Result(s, _data).Events.First(x => x.Event.Id == alien.Id);
            Assert.That(o.ResultCode, Does.StartWith("event.alien.").And.Not.EqualTo("event.alien.repelled"));
            Assert.That(o.StepsDone, Is.EqualTo(0));
            Assert.That(o.LostDays, Is.GreaterThan(0));
            Assert.That(o.MiniPlayed && !o.MiniPassed, Is.True);
        }

        [Test]
        public void AlienIsJudgedAutomatically()
        {
            // 彩蛋不用查手冊：不管判斷成什麼，都是外星人，直接打外星人的遊戲
            var (s, alien) = AlienSession(1);
            Assert.That(Do(s, CommandType.Judge, 1, "solar_storm"), Is.EqualTo(SessionRules.Ok));
            Assert.That(s.Responses[alien.Id].Judged, Is.EqualTo("alien"));
            Assert.That(Do(s, CommandType.MiniDone, 1, "alien", 900), Is.EqualTo(SessionRules.Ok));
            var o = SessionRules.Result(s, _data).Events.First(x => x.Event.Id == alien.Id);
            Assert.That(o.JudgedCorrect, Is.True);
            Assert.That(o.ResultCode, Is.EqualTo("event.alien.repelled"));
        }

        [Test]
        public void MisjudgingARealEventMeansPlayingTheWrongGame()
        {
            // 一般事件：判斷錯就打到別的遊戲，救不了真正的事件
            var design = GameData.DefaultDesign(_data.Scenario("earth_observation"), "engineer").Clone();
            design.ScenarioId = "earth_observation";
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, false);
            SessionRules.Apply(s, _data, Command.Of(CommandType.Join, 1));
            SessionRules.Apply(s, _data, Command.Of(CommandType.BeginDesign, 1));
            for (int seed = 1; seed < 5000; seed++)
            {
                var plan = Simulator.PlanMission(design, _data, seed, "engineer");
                var ev = plan.Events.FirstOrDefault(e => e.Kind == EventKind.ScienceOpportunity);
                if (ev == null || plan.Events.Any(e => e.Kind == EventKind.Alien)) continue;
                var r = Simulator.SimulateMission(design, _data, seed, new NoDecisions(), new MissionContext { DifficultyId = "engineer" });
                if (!r.ReachedOrbit || !r.Events.Any(o => o.Event.Id == ev.Id)) continue;
                s.Launches.Add(new LaunchRecord { Attempt = 1, Design = design, Seed = seed, Reached = true, Outcome = r.Launch.Outcome, ReasonCode = r.Launch.ReasonCode });
                s.Phase = SessionPhase.Ops; s.OpsTurn = ev.Turn; s.TurnResolved = false; s.Responses.Clear();
                Do(s, CommandType.Judge, 1, "solar_storm");
                Assert.That(Do(s, CommandType.MiniDone, 1, Minigames.For(EventKind.ScienceOpportunity).Id, 1000), Is.EqualTo("err.minigame"), "判斷成太陽風暴就不能交科學機會的成績");
                Assert.That(Do(s, CommandType.MiniDone, 1, Minigames.For(EventKind.SolarStorm).Id, 1000), Is.EqualTo(SessionRules.Ok));
                var o = SessionRules.Result(s, _data).Events.First(x => x.Event.Id == ev.Id);
                Assert.That(o.JudgedCorrect, Is.False);
                Assert.That(o.StepsDone, Is.EqualTo(0).Or.LessThan(o.StepsRequired), "太陽風暴的步驟救不了科學機會");
                return;
            }
            Assert.Fail("找不到合適的種子");
        }

        [Test]
        public void SecondPlayerCannotChangeTheAlienJudgment()
        {
            var (s, alien) = AlienSession(2);
            Do(s, CommandType.Judge, 1, "alien");
            Do(s, CommandType.MiniDone, 1, "alien", 900);
            Assert.That(s.TurnResolved, Is.False, "還在等第二位玩家");
            Assert.That(Do(s, CommandType.Judge, 2, "solar_storm"), Is.EqualTo(SessionRules.Ok));
            Assert.That(s.Responses[alien.Id].Judged, Is.EqualTo("alien"), "外星人是自動判定，改不了");
            Assert.That(Do(s, CommandType.MiniDone, 1, "alien", 0), Is.EqualTo(SessionRules.Ok), "重複送出會被忽略");
            Assert.That(s.Responses[alien.Id].Mini[1], Is.EqualTo(900));
        }

        [Test]
        public void TeamResolvesWhenEveryoneHasReported()
        {
            var (s, alien) = AlienSession(2);
            Do(s, CommandType.Judge, 1, "alien");
            Do(s, CommandType.MiniDone, 1, "alien", 900);
            Do(s, CommandType.MiniDone, 2, "alien", 400);
            Assert.That(s.TurnResolved, Is.True);
            Assert.That(s.Responses[alien.Id].MiniPassed, Is.True, "平均 650 ≥ 600");
        }

        [Test]
        public void ForcedEndTurnCountsMissingPlayersAsZero()
        {
            var (s, alien) = AlienSession(2);
            Do(s, CommandType.Judge, 1, "alien");
            Do(s, CommandType.MiniDone, 1, "alien", 1000);
            Assert.That(s.TurnResolved, Is.False);
            Do(s, CommandType.EndTurn, 1);
            Assert.That(s.TurnResolved, Is.True);
            Assert.That(s.Responses[alien.Id].MiniPassed, Is.False, "平均 500 < 600");
        }

        [Test]
        public void EndingTheTurnWithoutPlayingDoesNothing()
        {
            var (s, alien) = AlienSession(1);
            Do(s, CommandType.Judge, 1, "alien");
            Do(s, CommandType.EndTurn, 1);
            Assert.That(s.TurnResolved, Is.True);
            Assert.That(s.Responses[alien.Id].Actions, Is.Empty);
        }

        [Test]
        public void ResponsesSurviveJsonRoundTrip()
        {
            var (s, alien) = AlienSession(2);
            Do(s, CommandType.Judge, 1, "alien");
            Do(s, CommandType.MiniDone, 1, "alien", 777);
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(s);
            var back = Newtonsoft.Json.JsonConvert.DeserializeObject<SessionState>(json);
            Assert.That(back.Responses[alien.Id].Mini[1], Is.EqualTo(777));
            Assert.That(back.Responses[alien.Id].Clone().Mini[1], Is.EqualTo(777));
        }
    }
}
