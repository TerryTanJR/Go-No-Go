using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>組裝工作台（小零件 → 零件）、沙盒模式、設計分享碼。</summary>
    public class WorkshopTests
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

        void Buy(SessionState s, params string[] parts)
        {
            foreach (var p in parts) Ok(s, Command.Of(CommandType.CartAdd, 1, p));
            Ok(s, Command.Of(CommandType.PlaceOrder, 1));
        }

        // ---------------------------------------------------------------- 組裝工作台

        [Test]
        public void EveryPartHasARecipeExceptGroundStationsAndThePlate()
        {
            Assert.That(GameDataLoader.Validate(_data), Is.Empty);
            foreach (var p in _data.Parts)
            {
                var r = _data.Recipe(p);
                if (p.Category == PartCategory.GroundStation || p.Id == "prop_none") Assert.That(r, Is.Null, p.Id);
                else
                {
                    Assert.That(r, Is.Not.Null, p.Id);
                    Assert.That(r.Category, Is.EqualTo(p.Category), p.Id);
                    Assert.That(r.Components.Count, Is.InRange(3, 5), p.Id);
                    Assert.That(r.Source.Url, Does.StartWith("http"), p.Id);
                    foreach (var c in r.Components)
                    {
                        Assert.That(c.Role.ZhTW, Does.Not.Match("[0-9]"), c.Id + "：小零件的功能說明不放數字");
                        Assert.That(c.Name.En.Length * c.Name.ZhTW.Length * c.Role.En.Length * c.Role.ZhTW.Length, Is.GreaterThan(0), c.Id);
                    }
                }
            }
            // 指定零件的配方優先於類別通用的
            Assert.That(_data.Recipe(_data.Part("inst_thermal")).Components.Select(c => c.Id), Has.Member("cryocooler"));
            Assert.That(_data.Recipe(_data.Part("inst_visible")).Components.Select(c => c.Id), Has.No.Member("cryocooler"));
            Assert.That(_data.Recipe(_data.Part("solar_body")).Components.Select(c => c.Id), Has.No.Member("hinge"));
        }

        [Test]
        public void DistractorsAreStableAndNeverPartOfTheRecipe()
        {
            foreach (var p in _data.Parts.Where(p => _data.Recipe(p) != null))
            {
                var own = _data.Recipe(p).Components.Select(c => c.Id).ToList();
                var a = _data.Distractors(p, 2);
                var b = _data.Distractors(p, 2);
                Assert.That(a.Count, Is.EqualTo(2), p.Id);
                Assert.That(a.Select(c => c.Id), Is.EqualTo(b.Select(c => c.Id)), p.Id + "：每次混進來的要一樣");
                Assert.That(a.Select(c => c.Id).Distinct().Count(), Is.EqualTo(2), p.Id);
                foreach (var c in a) Assert.That(own, Has.No.Member(c.Id), p.Id);
            }
        }

        [Test]
        public void BoughtPartsMustBeAssembledBeforeInstalling()
        {
            var s = Solo();
            // 開局就在船上的零件本來就是組好的
            foreach (var id in s.Bench.AllPartIds().Where(id => _data.Part(id).Category != PartCategory.GroundStation))
                Assert.That(SessionRules.NeedsAssembly(s, _data, id), Is.False, id);

            Buy(s, "batt_large");
            Assert.That(SessionRules.NeedsAssembly(s, _data, "batt_large"), Is.True);
            Assert.That(SessionRules.CanAssemble(s, _data, 1), Is.EqualTo("err.nothing_held"));
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Assert.That(SessionRules.CanInstall(s, _data, 1), Is.EqualTo("err.not_assembled"));
            Assert.That(Try(s, CommandType.Install, 1), Is.EqualTo("err.not_assembled"));
            Assert.That(Try(s, CommandType.Assemble, 1, "batt_small"), Is.EqualTo("err.unknown"), "組的要是手上那一個");

            int rev = s.Revision;
            Ok(s, Command.Of(CommandType.Assemble, 1, "batt_large"));
            Assert.That(s.Revision, Is.GreaterThan(rev));
            Assert.That(s.Built, Has.Member("batt_large"));
            Assert.That(s.HeldBy(1), Is.EqualTo("batt_large"), "組好之後還在手上");
            Assert.That(Try(s, CommandType.Assemble, 1), Is.EqualTo("err.already_built"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Assert.That(s.Bench.Get(PartCategory.Battery), Is.EqualTo("batt_large"));

            // 換下來的舊電池、拆下來再裝回去的零件都不用再組
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_small"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Assert.That(SessionRules.CanInstall(s, _data, 1), Is.EqualTo(SessionRules.Ok));
        }

        [Test]
        public void ThePlateNeedsNoAssemblyAndTheStateSurvivesTheNetwork()
        {
            var s = Solo("lunar_mapping", "engineer");
            Buy(s, "prop_none", "batt_large");
            Assert.That(SessionRules.NeedsAssembly(s, _data, "prop_none"), Is.False, "封板沒有小零件");
            Ok(s, Command.Of(CommandType.Pick, 1, "prop_none"));
            Ok(s, Command.Of(CommandType.Install, 1));

            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Ok(s, Command.Of(CommandType.Assemble, 1));
            // 多人：整包狀態序列化後傳給其他人，組好的零件要還在
            var copy = SessionState.FromJson(s.ToJson());
            Assert.That(copy.Built, Is.EquivalentTo(s.Built));
            Assert.That(SessionRules.NeedsAssembly(copy, _data, "batt_large"), Is.False);
        }

        [Test]
        public void AssemblingIsAnAssemblyBayJobUnderStrictRoles()
        {
            Assert.That(SessionRules.IsAssemblyCommand(CommandType.Assemble), Is.True);
            Assert.That(SessionRules.IsControlCommand(CommandType.Assemble), Is.False);
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.Join, 2));
            Ok(s, Command.Of(CommandType.SetStrict, 1, "", 1));
            Ok(s, Command.Of(CommandType.SetReady, 1, "", 1));
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            Assert.That(Try(s, CommandType.Assemble, 2), Is.EqualTo("err.strict_assembly"));
        }

        [Test]
        public void AFailedLaunchForgetsWhatWasAssembled()
        {
            var s = Solo();
            Buy(s, "batt_large");
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Ok(s, Command.Of(CommandType.Assemble, 1));
            Ok(s, Command.Of(CommandType.Drop, 1));
            // 找一個會失敗的設計太費工：直接假裝上一次發射沒入軌，走失敗重來的路
            Ok(s, Command.Of(CommandType.Launch, 1));
            s.LastLaunch.Reached = false;
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
            Assert.That(s.Built, Has.No.Member("batt_large"), "重來之後買的零件都沒了，組裝紀錄也跟著清掉");
            Assert.That(s.Built, Is.EquivalentTo(s.Owned));
        }

        // ---------------------------------------------------------------- 沙盒

        [Test]
        public void SandboxIsNotAMenuScenarioButCanBeLookedUp()
        {
            Assert.That(_data.Scenarios.Select(s => s.Id), Has.No.Member("sandbox"));
            Assert.That(_data.Difficulties.Select(d => d.Id), Has.No.Member("sandbox"));
            Assert.That(_data.SelectableDifficulties.Select(d => d.Id), Has.No.Member("sandbox"));
            var sc = _data.Scenario("sandbox");
            Assert.That(sc, Is.Not.Null);
            Assert.That(sc.Sandbox && sc.Requirements.Count == 0);
            Assert.That(_data.DifficultyFor(sc).Id, Is.EqualTo("sandbox"));
            Assert.That(sc.AllowedOrbits, Is.EquivalentTo(_data.Orbits.Select(o => o.Id)), "所有軌道都開放");
            Assert.That(_data.LaunchersFor(sc).Select(l => l.Id), Is.EquivalentTo(_data.Launchers.Select(l => l.Id)), "所有火箭都開放");
            foreach (var key in new[] { "ui.brief.speech.sandbox", "objective.open", "ui.objective.open" })
                Assert.That(_data.Strings.ContainsKey(key), key);
        }

        [Test]
        public void SandboxHasNoPresetLimits()
        {
            var s = Solo("sandbox", null);
            Assert.That(s.DifficultyId, Is.EqualTo("sandbox"));
            var sc = s.Scenario(_data);

            // 所有零件（含誘餌、關卡專用的）都買得到，錢不是問題
            var shelf = SessionRules.ShelfParts(_data, s).Select(p => p.Id).ToList();
            Assert.That(shelf, Is.EquivalentTo(_data.Parts.Where(p => p.Category != PartCategory.GroundStation).Select(p => p.Id)));
            Buy(s, shelf.Where(id => !s.Owned.Contains(id)).ToArray());
            Assert.That(SessionRules.Headroom(s, _data), Is.GreaterThan(ScenarioDef.NoLimit / 2));

            // 不用組裝；儀器可以裝到 6 台；年數可以拉到 15
            Assert.That(sc.MaxInstruments, Is.EqualTo(6));
            foreach (var id in new[] { "inst_sar", "inst_hyperspectral", "inst_thermal", "inst_multispectral", "inst_co2_spectrometer" })
            {
                Assert.That(SessionRules.NeedsAssembly(s, _data, id), Is.False);
                Ok(s, Command.Of(CommandType.Pick, 1, id));
                Ok(s, Command.Of(CommandType.Install, 1));
            }
            Assert.That(s.Bench.Instruments.Count, Is.EqualTo(6));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 99));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(SessionRules.SandboxMaxYears));

            // 每條軌道、每支火箭都選得到
            foreach (var o in _data.Orbits)
            {
                if (SessionRules.OrbitLauncherCheck(_data, o.Id, s.Bench.LauncherId) != SessionRules.Ok) continue;
                Ok(s, Command.Of(CommandType.SetOrbit, 1, o.Id));
            }
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "sso_700"));
            foreach (var l in _data.Launchers.Where(l => string.IsNullOrEmpty(l.OrbitLock)))
                Ok(s, Command.Of(CommandType.SetLauncher, 1, l.Id));

            // 預算和時間永遠不會是紅燈、也沒有處室需求
            var ev = Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false);
            Assert.That(ev[ResourceId.Budget].Status, Is.EqualTo(ResourceStatus.Ok));
            Assert.That(ev[ResourceId.Time].Status, Is.EqualTo(ResourceStatus.Ok));
            Assert.That(Departments.Active(sc, s.Difficulty(_data)), Is.Empty);
        }

        [Test]
        public void SandboxScoresEveryInstrumentAndOrbitOnItsBestJob()
        {
            var sc = _data.Scenario("sandbox");
            foreach (var p in _data.Parts.Where(p => p.Category == PartCategory.Instrument))
                Assert.That(Evaluator.Match(p, sc.Objective), Is.EqualTo(p.ObjectiveMatch.Values.Max()).Within(1e-12), p.Id);
            foreach (var o in _data.Orbits)
                Assert.That(Evaluator.OrbitMatch(o, sc.Objective), Is.EqualTo(o.ObjectiveMatch.Values.DefaultIfEmpty(0).Max()).Within(1e-12), o.Id);
            // 一般關卡照舊：只看自己的目標
            var cam = _data.Part("inst_visible");
            Assert.That(Evaluator.Match(cam, "earth_observation"), Is.EqualTo(cam.ObjectiveMatch["earth_observation"]));
            Assert.That(Evaluator.Match(cam, "no_such_objective"), Is.EqualTo(0));
        }

        [Test]
        public void SandboxKeepsTheDesignAfterAFailedLaunchAndScoresNoDepartments()
        {
            var s = Solo("sandbox", null);
            Buy(s, "batt_large");
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_large"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Ok(s, Command.Of(CommandType.Launch, 1));
            s.LastLaunch.Reached = false;
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
            Assert.That(s.Bench.Get(PartCategory.Battery), Is.EqualTo("batt_large"), "沙盒失敗不重來");
            Assert.That(s.Owned, Has.Member("batt_large"));
            Assert.That(s.Restarts, Is.EqualTo(0));

            // 沒有處室需求：處室分不會因此變成 0
            var d = s.Bench.Clone();
            var ctx = SessionRules.Context(_data, s, 0);
            for (int seed = 1; seed <= 40; seed++)
            {
                var r = Simulator.SimulateMission(d, _data, seed, new PerfectDecisions(), ctx);
                if (!r.ReachedOrbit) continue;
                Assert.That(r.Score.Requirements, Is.Empty);
                Assert.That(r.Score.Departments, Is.EqualTo(100));
                return;
            }
            Assert.Fail("40 個種子都沒入軌");
        }

        [Test]
        public void LoadDesignOnlyWorksInTheSandbox()
        {
            var eo = TestData.Balanced();
            string code = DesignCode.Encode(eo, _data, "balanced");

            var normal = Solo();
            Assert.That(Try(normal, CommandType.LoadDesign, 1, code), Is.EqualTo("err.sandbox_only"));

            var s = Solo("sandbox", null);
            Assert.That(Try(s, CommandType.LoadDesign, 1, "hello"), Is.EqualTo("err.code_format"));
            Ok(s, Command.Of(CommandType.LoadDesign, 1, code));
            Assert.That(s.Bench.ScenarioId, Is.EqualTo("sandbox"));
            Assert.That(s.Bench.Instruments, Is.EqualTo(eo.Instruments));
            foreach (var c in PartCategory.Single) Assert.That(s.Bench.Get(c), Is.EqualTo(eo.Get(c)), c);
            Assert.That(s.Bench.OrbitId, Is.EqualTo(eo.OrbitId));
            Assert.That(s.Bench.LauncherId, Is.EqualTo(eo.LauncherId));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(eo.MissionYears));
            Assert.That(s.Reported.StableHash(), Is.EqualTo(s.Bench.StableHash()), "大螢幕跟著整合台");
            foreach (var id in eo.AllPartIds().Where(id => _data.Part(id).Category != PartCategory.GroundStation))
            {
                Assert.That(s.Owned, Has.Member(id));
                Assert.That(s.Built, Has.Member(id));
            }
            Assert.That(GameDataLoader.ValidateDesign(_data, s.Bench, "loaded"), Is.Empty);
            // 載入之後照樣可以發射
            Ok(s, Command.Of(CommandType.Launch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
        }

        // ---------------------------------------------------------------- 設計分享碼

        [Test]
        public void ShareCodeRoundTripsEveryKindOfDesign()
        {
            var designs = new List<Design> { TestData.CheapSmallsat(), TestData.Balanced(), TestData.HighScience() };
            designs.AddRange(_data.AllScenarios.Select(s => { var d = s.DefaultDesign.Clone(); d.ScenarioId = s.Id; return d; }));
            // 沙盒可以把某一類拆掉不裝、儀器裝到 6 台
            var bare = TestData.Balanced();
            bare.ScenarioId = "sandbox";
            bare.Parts.Remove(PartCategory.Propulsion);
            bare.Instruments = new List<string> { "inst_sar", "inst_thermal", "inst_visible", "inst_magnetometer", "inst_fire_mwir", "inst_ais" };
            bare.MissionYears = 15;
            designs.Add(bare);

            foreach (var d in designs)
            {
                string code = DesignCode.Encode(d, _data, "我的設計 #1");
                Assert.That(code, Does.StartWith(DesignCode.Prefix));
                Assert.That(code, Does.Match("^[A-Za-z0-9_-]+$"), "只用貼到哪裡都不會壞的字元");
                Assert.That(code.Length, Is.LessThan(220), "一行貼得完");
                Assert.That(DesignCode.TryDecode("  " + code + "\n", _data, out var back, out var name, out var error), error);
                Assert.That(name, Is.EqualTo("我的設計 #1"));
                Assert.That(back.ScenarioId, Is.EqualTo(d.ScenarioId));
                Assert.That(back.Instruments, Is.EqualTo(d.Instruments));
                foreach (var c in PartCategory.Single) Assert.That(back.Get(c), Is.EqualTo(d.Get(c)), c);
                Assert.That(back.OrbitId, Is.EqualTo(d.OrbitId));
                Assert.That(back.LauncherId, Is.EqualTo(d.LauncherId));
                Assert.That(back.MissionYears, Is.EqualTo(d.MissionYears));
                Assert.That(back.StableHash(), Is.EqualTo(d.StableHash()), "同一個碼 = 同一個設計 = 同樣的模擬結果");
            }
        }

        [Test]
        public void ShareCodeRejectsGarbageAndTypos()
        {
            string code = DesignCode.Encode(TestData.Balanced(), _data, "x");
            foreach (var bad in new[] { null, "", "GNG1-", "GNG2-" + code.Substring(5), "not a code", "GNG1-!!!!", code.Substring(0, code.Length - 6) })
                Assert.That(DesignCode.TryDecode(bad, _data, out var d, out _, out var error), Is.False, bad ?? "null");

            // 改掉中間任何一個字元：不是解不開，就是檢查碼對不上；絕對不會解出另一個設計
            int same = 0;
            for (int i = DesignCode.Prefix.Length; i < code.Length; i++)
            {
                char swap = code[i] == 'A' ? 'B' : 'A';
                string typo = code.Substring(0, i) + swap + code.Substring(i + 1);
                if (DesignCode.TryDecode(typo, _data, out var d, out _, out var error))
                {
                    same++;
                    Assert.That(d.StableHash(), Is.EqualTo(TestData.Balanced().StableHash()), "位置 " + i);
                }
                else Assert.That(new[] { "err.code_format", "err.code_checksum", "err.code_unknown" }, Has.Member(error));
            }
            // base64 最後一個字元的低位元不帶資料，改了也解得出同一份：只允許這一種
            Assert.That(same, Is.LessThanOrEqualTo(1));
            foreach (var key in new[] { "err.code_format", "err.code_checksum", "err.code_unknown", "err.sandbox_only", "err.not_assembled", "err.already_built" })
                Assert.That(_data.Strings.ContainsKey(key), key);
        }

        [Test]
        public void ShareCodeNamesAreCleaned()
        {
            Assert.That(DesignCode.CleanName("  a|b\nc  "), Is.EqualTo("a b c"));
            Assert.That(DesignCode.CleanName(new string('x', 80)).Length, Is.EqualTo(DesignCode.MaxName));
            string code = DesignCode.Encode(TestData.Balanced(), _data, "a|b");
            Assert.That(DesignCode.TryDecode(code, _data, out _, out var name, out _), Is.True);
            Assert.That(name, Is.EqualTo("a b"));
        }
    }
}
