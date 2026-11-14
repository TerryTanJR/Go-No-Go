using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class LaunchPacingTests
    {
        const double From = 8, Budget = 50;
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        IEnumerable<(string name, LaunchTimeline tl)> AllFlights()
        {
            var outcomes = new[]
            {
                (LaunchOutcome.Nominal, "launch.nominal"),
                (LaunchOutcome.DeadOnOrbit, "launch.nominal"),
                (LaunchOutcome.Failure, "launch.anomaly"),
                (LaunchOutcome.FairingFailure, "launch.fairing"),
            };
            foreach (var l in _data.Launchers)
                foreach (var orbit in l.CapacityKg.Keys)
                    foreach (var (o, reason) in outcomes)
                    {
                        var d = TestData.Balanced();
                        d.LauncherId = l.Id;
                        d.OrbitId = orbit;
                        yield return ($"{l.Id} → {orbit} ({o})", LaunchTimeline.Build(_data, d, o, reason));
                    }
        }

        [Test]
        public void EveryFlightFitsTheBudget()
        {
            foreach (var (name, tl) in AllFlights())
            {
                var p = new LaunchPacing(tl, From, Budget);
                Assert.That(p.PlannedTotal, Is.LessThanOrEqualTo(Budget + 0.5), name);
                string at(string id) => tl.First(id) is { } e ? $" {id} ×{p.PlanSpeed(e.T):0.0}" : "";
                TestContext.Out.WriteLine($"{name}: {tl.End:0} s → {p.PlannedTotal:0.0} s{at("stage_sep")}{at("fairing")}{at("deploy")}");
            }
        }

        [Test]
        public void ActionsAreSlowerThanCoasting()
        {
            foreach (var (name, tl) in AllFlights())
            {
                var p = new LaunchPacing(tl, From, Budget);
                double coast = 0;
                for (double t = From; t < tl.End; t += 1) coast = Math.Max(coast, p.PlanSpeed(t));
                foreach (var e in tl.Events.Where(e => e.Id == "stage_sep" || e.Id == "fairing" || e.Id == "deploy"))
                    Assert.That(p.PlanSpeed(e.T), Is.LessThan(coast / 3), name + " " + e.Id);
            }
        }

        [Test]
        public void SpeedChangesSmoothlyInRealTime()
        {
            foreach (var (name, tl) in AllFlights())
            {
                var p = new LaunchPacing(tl, From, Budget);
                // 用真實時間的小步長播一次：每一步倍率的變化不超過 Ramp 允許的比例（留一點餘裕給底速的切換）
                double t = From, s = p.PlanSpeed(t), dt = 1 / 60.0;
                while (t < tl.End)
                {
                    t += s * dt;
                    double next = p.PlanSpeed(t);
                    Assert.That(next / s, Is.InRange(Math.Exp(-LaunchPacing.Ramp * dt * 1.5), Math.Exp(LaunchPacing.Ramp * dt * 1.5)), $"{name} @ T+{t:0}");
                    s = next;
                }
            }
        }
    }
}
