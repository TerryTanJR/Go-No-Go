using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class FixAndRollCallTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        /// <summary>隨機產生很多設計：每個超標警告附的解法，套用後都必須真的解決那一項。</summary>
        [Test]
        public void EveryOfferedFixActuallyFixesItsWarning()
        {
            var rng = new Random(31415);
            var scenario = _data.Scenario("earth_observation");
            int checkedFixes = 0;
            for (int i = 0; i < 120; i++)
            {
                var d = RandomDesign(rng, scenario);
                var ev = Evaluator.EvaluateDesign(d, _data, "engineer", true);
                foreach (var w in ev.Warnings.Where(w => w.Severity == ResourceStatus.Over))
                {
                    foreach (var f in w.Fixes)
                    {
                        var after = Evaluator.EvaluateDesign(f.ApplyTo(d), _data, "engineer", false);
                        Assert.That(after.DesignErrors, Is.Empty);
                        if (w.Resource.HasValue)
                            Assert.That(after[w.Resource.Value].Status, Is.Not.EqualTo(ResourceStatus.Over), $"{w.Code} ← {f.Kind} {f.FromId}->{f.ToId}");
                        else
                            Assert.That(after.Lifetime.Years, Is.GreaterThanOrEqualTo(f.ApplyTo(d).MissionYears), $"{w.Code} ← {f.Kind}");
                        checkedFixes++;
                    }
                    Assert.That(w.Fixes.Select(f => (f.Kind, f.Category)).Distinct().Count(), Is.EqualTo(w.Fixes.Count), "兩個解法要是不同方向");
                }
            }
            Assert.That(checkedFixes, Is.GreaterThan(50));
        }

        [Test]
        public void FixesRespectDifficultyTierLimit()
        {
            var d = _data.Scenario("earth_observation").DefaultDesign;
            foreach (var f in FixFinder.Candidates(d, _data, "cadet").Where(f => f.Kind == FixKind.SwapPart))
                Assert.That(_data.Part(f.ToId).Tier, Is.LessThanOrEqualTo(2), f.ToId);
        }

        [Test]
        public void HistoricalLaunchersOnlyInTheirScenario()
        {
            var eo = _data.Scenario("earth_observation");
            var ids = _data.LaunchersFor(eo).Select(l => l.Id).ToList();
            Assert.That(ids, Has.No.Member("taurus_xl"));
            Assert.That(ids, Has.No.Member("delta_ii"));
            Assert.That(_data.LaunchersFor(_data.Scenario("hist_oco")).Select(l => l.Id), Is.EquivalentTo(new[] { "taurus_xl", "delta_ii" }));
        }

        [Test]
        public void RollCallBlamesTheRightRole()
        {
            var diff = _data.Difficulty("engineer");
            var none = new Dictionary<Role, RequirementDef>();

            var heavy = TestData.HighScience();
            heavy.LauncherId = "electron";
            var v = RollCall.Run(Evaluator.EvaluateDesign(heavy, _data), heavy, _data, diff, none);
            Assert.That(v.Single(x => x.Role == Role.Engineer).ReasonCode, Is.EqualTo("nogo.mass"));
            Assert.That(v.Single(x => x.Role == Role.Director).Vote, Is.EqualTo(Vote.Go));

            var noData = TestData.HighScience();
            noData.Parts[PartCategory.Antenna] = "ant_sband";
            v = RollCall.Run(Evaluator.EvaluateDesign(noData, _data), noData, _data, diff, none);
            Assert.That(v.Single(x => x.Role == Role.Comms).ReasonCode, Is.EqualTo("nogo.comms"));

            var empty = TestData.Balanced();
            empty.Instruments.Clear();
            v = RollCall.Run(Evaluator.EvaluateDesign(empty, _data), empty, _data, diff, none);
            Assert.That(v.Single(x => x.Role == Role.Scientist).ReasonCode, Is.EqualTo("nogo.science"));

            var pricey = TestData.HighScience();
            pricey.Instruments[2] = "inst_sar";
            pricey.Parts[PartCategory.GroundStation] = "gs_6";
            pricey.Parts[PartCategory.Computer] = "obc_radhard";
            v = RollCall.Run(Evaluator.EvaluateDesign(pricey, _data), pricey, _data, diff, none);
            Assert.That(v.Single(x => x.Role == Role.Director).Vote, Is.EqualTo(Vote.NoGo));
        }

        [Test]
        public void RequirementsAreDrawnDeterministically()
        {
            var s = _data.Scenario("earth_observation");
            var a = Roles.DrawRequirements(s, 5);
            var b = Roles.DrawRequirements(s, 5);
            Assert.That(a.Keys, Is.EquivalentTo(Roles.All));
            foreach (var k in a.Keys) Assert.That(b[k], Is.SameAs(a[k]));
            var distinct = Enumerable.Range(1, 40).Select(seed => Roles.DrawRequirements(s, seed)[Role.Comms].Value).Distinct().Count();
            Assert.That(distinct, Is.EqualTo(2), "兩張卡都要抽得到");
        }

        Design RandomDesign(Random rng, ScenarioDef s)
        {
            T Pick<T>(IList<T> xs) => xs[rng.Next(xs.Count)];
            var inst = _data.PartsIn(PartCategory.Instrument).Select(p => p.Id).OrderBy(_ => rng.Next()).Take(1 + rng.Next(3)).ToList();
            var d = new Design
            {
                ScenarioId = s.Id,
                Instruments = inst,
                OrbitId = Pick(s.AllowedOrbits),
                LauncherId = Pick(_data.LaunchersFor(s).Select(l => l.Id).ToList()),
                MissionYears = 1 + rng.Next(5),
            };
            foreach (var c in PartCategory.Single) d.Parts[c] = Pick(_data.PartsIn(c).Select(p => p.Id).ToList());
            return d;
        }
    }
}
