using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class DataTests
    {
        [Test]
        public void AllFilesLoadAndValidate()
        {
            var data = TestData.Load();
            var errors = GameDataLoader.Validate(data);
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(data.Parts.Count, Is.GreaterThanOrEqualTo(25));
            Assert.That(data.Orbits.Count, Is.EqualTo(4));
            Assert.That(data.Scenarios.Any(s => s.Historical != null), "至少要有一個歷史關卡");
        }

        [Test]
        public void MisspelledFieldIsRejected()
        {
            var files = TestData.Files();
            files["orbits"] = files["orbits"].Replace("\"altitude_km\"", "\"altitude\"");
            Assert.Throws<FormatException>(() => GameDataLoader.Load(files));
        }

        [Test]
        public void EverySingleChoiceCategoryHasThreeTiers()
        {
            var data = TestData.Load();
            foreach (var cat in PartCategory.Single)
                Assert.That(data.PartsIn(cat).Select(p => p.Tier).Distinct().OrderBy(t => t), Is.EqualTo(new[] { 1, 2, 3 }), cat);
            Assert.That(data.PartsIn(PartCategory.Instrument).Count(p => p.Tier <= 2), Is.GreaterThanOrEqualTo(2), "學員難度至少要有兩台儀器");
        }

        [Test]
        public void EveryValueHasASourceUrl()
        {
            var data = TestData.Load();
            foreach (var p in data.Parts) Assert.That(p.Source.Url, Does.StartWith("http"), p.Id);
            foreach (var o in data.Orbits) Assert.That(o.Source.Url, Does.StartWith("http"), o.Id);
            foreach (var l in data.Launchers) Assert.That(l.Source.Url, Does.StartWith("http"), l.Id);
            foreach (var s in data.Scenarios.Where(s => s.Historical != null))
                foreach (var src in s.Historical.Sources) Assert.That(src.Url, Does.StartWith("http"), s.Id);
            foreach (var kv in data.Constants.Sources) Assert.That(kv.Value.Url, Does.StartWith("http"), kv.Key);
            foreach (var m in data.Missions) Assert.That(m.Url, Does.StartWith("http"), m.Id);
        }

        [Test]
        public void EveryStringHasBothLanguages()
        {
            var data = TestData.Load();
            foreach (var kv in data.Strings)
            {
                Assert.That(kv.Value.En, Is.Not.Empty, kv.Key);
                Assert.That(kv.Value.ZhTW, Is.Not.Empty, kv.Key);
            }
            foreach (var p in data.Parts)
            {
                Assert.That(p.Description.En, Is.Not.Empty, p.Id);
                Assert.That(p.Description.ZhTW, Is.Not.Empty, p.Id);
            }
            foreach (var s in data.Scenarios)
                foreach (var list in s.Requirements.Values)
                    foreach (var r in list)
                        Assert.That(r.Text.En.Length * r.Text.ZhTW.Length, Is.GreaterThan(0), s.Id + " " + r.Type);
        }

        /// <summary>核心可能送出的每個字串鍵，都要在 strings.json 裡。</summary>
        [Test]
        public void EveryCodeTheCoreCanEmitHasAString()
        {
            var data = TestData.Load();
            var keys = new List<string>();
            keys.AddRange(Enum.GetValues(typeof(ResourceId)).Cast<ResourceId>().Select(r => "res." + r.ToString().ToLowerInvariant()));
            keys.AddRange(Enum.GetValues(typeof(ResourceId)).Cast<ResourceId>().Select(r => "res." + r.ToString().ToLowerInvariant() + ".why"));
            keys.AddRange(new[]
            {
                "warn.mass", "warn.orbit_unreachable", "warn.power", "warn.battery", "warn.budget", "warn.storage", "warn.downlink",
                "warn.schedule", "warn.lifetime.propellant", "warn.lifetime.battery", "warn.lifetime.radiation",
                "warn.no_instrument", "warn.instrument_mismatch", "warn.too_many_instruments",
                "fix.swap", "fix.remove", "fix.orbit", "fix.launcher", "fix.years",
                "nogo.science", "nogo.lifetime", "nogo.mass",
                "launch.nominal", "launch.lower_orbit", "launch.anomaly", "launch.overweight", "launch.unreachable", "launch.fairing",
                "event.storm.safe", "event.storm.ok", "event.storm.damaged", "event.failure.redundant", "event.failure.instrument",
                "event.failure.computer", "event.failure.adcs", "event.failure.antenna",
                "event.opportunity.success", "event.opportunity.strained", "event.opportunity.skipped",
                "choice.safe_mode", "choice.continue", "choice.observe", "choice.skip", "choice.ack",
                "opp.wildfire", "opp.volcano", "opp.flood", "opp.hurricane",
                "suggest.launcher_reach", "suggest.mass", "suggest.launcher", "suggest.history_launcher", "suggest.mass_margin",
                "suggest.downlink", "suggest.storage", "suggest.power", "suggest.propellant", "suggest.battery", "suggest.shielding",
                "suggest.safe_mode", "suggest.redundancy", "suggest.more_science", "suggest.try_orbit",
            });
            foreach (var role in Roles.All)
                keys.AddRange(Roles.Domain(role).Select(r => "nogo." + r.ToString().ToLowerInvariant()));
            foreach (EventKind k in Enum.GetValues(typeof(EventKind)))
            {
                keys.Add("event.title." + k.ToString().ToLowerInvariant());
                keys.Add("event.prompt." + k.ToString().ToLowerInvariant());
            }

            // 因果鏈：實際跑一堆設計，收集出現過的鍵
            var designs = new List<Design> { TestData.CheapSmallsat(), TestData.Balanced(), TestData.HighScience() };
            designs.AddRange(data.Scenarios.Select(s => s.DefaultDesign));
            foreach (var d in designs)
                foreach (var seed in Enumerable.Range(1, 40))
                {
                    var choices = seed % 2 == 0 ? null : new AlwaysChoose("continue", "skip");
                    var r = Simulator.SimulateMission(d, data, seed, choices);
                    keys.AddRange(r.Causes.SelectMany(c => c.Steps).Select(s => s.Key));
                    keys.Add(r.SuggestionCode);
                    keys.Add(r.Launch.ReasonCode);
                    keys.AddRange(r.Events.Select(e => e.ResultCode));
                }
            keys.AddRange(CausalKeysInSource);

            var missing = keys.Distinct().Where(k => !data.Strings.ContainsKey(k)).ToList();
            Assert.That(missing, Is.Empty, "strings.json 缺少：" + string.Join(", ", missing));
        }

        /// <summary>Causality.cs 裡寫死的鍵（有些情況測試不一定跑得到）。</summary>
        static readonly string[] CausalKeysInSource =
        {
            "cause.mission_lost", "cause.launch_unreachable", "cause.mass_total", "cause.launch_overweight", "cause.launch_reliability",
            "cause.launch_anomaly", "cause.fairing", "cause.fairing_mass", "cause.mass_margin", "cause.lower_orbit", "cause.drag_doubled",
            "cause.data_generated", "cause.storage_capacity", "cause.downlink_capacity", "cause.data_lost", "cause.power_load",
            "cause.array_eol", "cause.duty", "cause.ended_early", "cause.orbit_drag", "cause.propellant_out", "cause.eclipses",
            "cause.battery_worn", "cause.radiation_dose", "cause.radiation_limit", "cause.storm", "cause.kept_running",
            "cause.instrument_damaged", "cause.part_failed", "cause.no_redundancy", "cause.degraded", "cause.safe_mode",
            "cause.protected", "cause.success_instrument", "cause.success_return", "cause.success_science",
        };

        [Test]
        public void RequirementsReferToRealThings()
        {
            var data = TestData.Load();
            var types = new[] { "budget_reserve", "lifetime_target", "mass_power_margin", "science_threshold", "instrument_required", "data_return" };
            foreach (var s in data.Scenarios)
            {
                foreach (var kv in s.Requirements)
                {
                    Assert.That(Roles.All.Select(Roles.Key), Does.Contain(kv.Key), s.Id);
                    foreach (var r in kv.Value)
                    {
                        Assert.That(types, Does.Contain(r.Type), s.Id);
                        if (r.Type == "instrument_required")
                            Assert.That(data.Part(r.PartId)?.Category, Is.EqualTo(PartCategory.Instrument), s.Id + " " + r.PartId);
                    }
                }
                Assert.That(data.LaunchersFor(s).Any(l => s.AllowedOrbits.Any(o => l.CapacityKg.ContainsKey(o))), s.Id + "：沒有火箭到得了允許的軌道");
            }
        }

        [Test]
        public void SpaceWeatherSnapshotIsUsable()
        {
            var data = TestData.Load();
            Assert.That(data.SpaceWeather.Count, Is.GreaterThanOrEqualTo(8));
            Assert.That(data.SpaceWeather.Select(e => e.Severity).Distinct().Count(), Is.EqualTo(3), "三種嚴重度都要有");
            foreach (var e in data.SpaceWeather)
            {
                Assert.That(new[] { "FLR", "CME", "GST" }, Does.Contain(e.Type), e.Id);
                Assert.That(DateTime.TryParse(e.Date, out _), e.Id);
                Assert.That(e.Summary.ZhTW, Is.Not.Empty, e.Id);
            }
        }
    }
}
