using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class FixTests
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
