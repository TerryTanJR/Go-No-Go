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
            Assert.That(data.Orbits.Count, Is.EqualTo(6));
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
                "launch.dead_on_orbit", "launch.nominal", "launch.lower_orbit", "launch.anomaly", "launch.overweight", "launch.unreachable", "launch.fairing",
                "event.storm.safe", "event.storm.ok", "event.storm.damaged", "event.failure.redundant", "event.failure.instrument",
                "event.failure.computer", "event.failure.adcs", "event.failure.antenna",
                "event.opportunity.success", "event.opportunity.strained", "event.opportunity.skipped",
                "event.power.handled", "event.power.drained", "event.failure.instrument_contained", "event.failure.computer_contained",
                "event.failure.adcs_contained", "event.failure.antenna_contained",
                "opp.wildfire", "opp.volcano", "opp.flood", "opp.hurricane",
                "suggest.launcher_reach", "suggest.mass", "suggest.launcher", "suggest.history_launcher", "suggest.mass_margin",
                "suggest.downlink", "suggest.storage", "suggest.power", "suggest.propellant", "suggest.battery", "suggest.shielding",
                "suggest.storm_procedure", "suggest.battery_eclipse", "suggest.power_procedure", "suggest.redundancy", "suggest.more_science", "suggest.try_orbit",
            });
            foreach (var k in OpsEvents.All)
            {
                keys.Add("event.title." + OpsEvents.Key(k));
                keys.Add("event.diag." + OpsEvents.Key(k));
            }
            Assert.That(OpsEvents.All.Length, Is.EqualTo(Enum.GetValues(typeof(EventKind)).Length));
            foreach (var c in OpsConsoles.All)
            {
                keys.Add("ops.console." + c);
                keys.AddRange(OpsConsoles.Actions(c).Select(a => "ops.action." + a));
            }
            Assert.That(OpsActions.All.Select(OpsConsoles.ConsoleOf), Has.None.Empty, "每個操作都要有所屬的控制台");

            // 因果鏈：實際跑一堆設計，收集出現過的鍵
            var designs = new List<Design> { TestData.CheapSmallsat(), TestData.Balanced(), TestData.HighScience() };
            designs.AddRange(data.Scenarios.Select(s => s.DefaultDesign));
            foreach (var d in designs)
                foreach (var seed in Enumerable.Range(1, 40))
                {
                    IDecisionSource choices = (seed % 3) switch { 0 => null, 1 => new PerfectDecisions(), _ => Scripted.Partial(1) };
                    var diff = data.Difficulties[seed % data.Difficulties.Count].Id;
                    var r = Simulator.SimulateMission(d, data, seed, choices, new MissionContext { DifficultyId = diff });
                    keys.AddRange(r.Causes.SelectMany(c => c.Steps).Select(s => s.Key));
                    // 控制台讀數（每個事件、每台控制台）
                    var ev = Evaluator.EvaluateDesign(d, data);
                    foreach (var e in r.Events)
                        foreach (var c in OpsConsoles.All)
                            foreach (var rd in OpsTelemetry.For(c, e.Event, ev, d, data, null))
                            {
                                keys.Add(rd.Key.StartsWith("part:") ? null : rd.Key);
                                if (rd.Value.StartsWith("opp.")) keys.Add(rd.Value);
                            }
                    keys.Add(r.SuggestionCode);
                    keys.Add(r.Launch.ReasonCode);
                    keys.AddRange(r.Events.Select(e => e.ResultCode));
                }
            keys.AddRange(CausalKeysInSource);

            var missing = keys.Where(k => k != null).Distinct().Where(k => !data.Strings.ContainsKey(k)).ToList();
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
            "cause.battery_capacity", "cause.eclipse_need", "cause.dead_on_orbit", "cause.power_anomaly", "cause.not_handled",
            "cause.gs_wait", "cause.latency_total", "cause.timely", "cause.no_fire_detector", "cause.detection", "cause.pass_time",
            "cause.fire_weak", "cause.big_fire", "cause.fire_stuck", "cause.false_fire", "cause.lwir_flat", "cause.no_lwir", "cause.false_reported",
            "warn.latency", "err.rideshare_orbit", "suggest.latency", "suggest.fire_detector", "suggest.pass_time", "suggest.fire_procedure", "suggest.false_fire",
            "site.forest", "site.grassland", "site.desert", "site.glint",
            "event.fire.reported", "event.fire.partial", "event.fire.stuck",
            "event.false_fire.reported", "event.false_fire.flagged", "event.false_fire.ignored",
            "err.owned", "err.not_owned", "err.cart_empty", "err.no_orders", "cause.success_latency", "suggest.fire_tradeoff",
        };

        [Test]
        public void RequirementsReferToRealThings()
        {
            var data = TestData.Load();
            var types = new[] { "budget_reserve", "lifetime_target", "mass_power_margin", "science_threshold", "instrument_required", "data_return",
                                "instrument_any", "orbit_required", "battery_eclipse", "latency" };
            foreach (var s in data.Scenarios)
            {
                foreach (var kv in s.Requirements)
                {
                    Assert.That(Departments.All.Select(Departments.Key), Does.Contain(kv.Key), s.Id);
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
        public void DepartmentRequirementsFollowDifficulty()
        {
            var data = TestData.Load();
            foreach (var s in data.Scenarios)
            {
                var cadet = Departments.Active(s, data.Difficulty("cadet"));
                var eng = Departments.Active(s, data.Difficulty("engineer"));
                var dir = Departments.Active(s, data.Difficulty("director"));
                Assert.That(cadet.Count, Is.LessThan(eng.Count), s.Id + "：工程師難度要多一條需求");
                Assert.That(eng.Count, Is.LessThan(dir.Count), s.Id + "：任務主管難度要再多一條");
                Assert.That(cadet.All(a => a.Requirement.ConflictGroup == 0));
                // 每個處室在每個難度都至少有一條需求，打電話才有內容
                foreach (var dep in Departments.All)
                    Assert.That(cadet.Any(a => a.Department == dep), s.Id + " " + Departments.Key(dep));
                Assert.That(dir.Select(a => a.Requirement.Id).Distinct().Count(), Is.EqualTo(dir.Count), "需求 id 不能重複");
            }
        }

        [Test]
        public void DepartmentLinesRevealRealRequirementTypes()
        {
            var data = TestData.Load();
            var types = data.Scenarios.SelectMany(s => s.Requirements.Values.SelectMany(l => l)).Select(r => r.Type).Distinct().ToList();
            Assert.That(data.Departments.Select(d => d.Id), Is.EquivalentTo(Departments.All.Select(Departments.Key)));
            foreach (var dep in data.Departments)
            {
                Assert.That(dep.Lines.Count, Is.GreaterThanOrEqualTo(3), dep.Id);
                foreach (var line in dep.Lines)
                    foreach (var t in line.Reveals)
                        Assert.That(types, Does.Contain(t), dep.Id + " " + line.Id);
            }
            // 每個情境的每一種需求，都有某個處室的某句話會說出來（關卡可以有自己的對話）
            foreach (var s in data.Scenarios)
                foreach (var kv in s.Requirements)
                {
                    var dep = data.Department(kv.Key);
                    foreach (var r in kv.Value)
                        Assert.That(data.Lines(s, dep).Any(l => l.Reveals.Contains(r.Type)), s.Id + " " + kv.Key + " " + r.Type);
                }
        }

        [Test]
        public void PartsHaveUniqueCodesAndPlainFunctions()
        {
            var data = TestData.Load();
            var parts = data.Parts.Where(p => p.Category != PartCategory.GroundStation).ToList();
            Assert.That(parts.Select(p => p.Code).Distinct().Count(), Is.EqualTo(parts.Count));
            foreach (var p in parts)
            {
                Assert.That(data.PartByCode(p.Code), Is.SameAs(p));
                Assert.That(p.Function.En.Length * p.Function.ZhTW.Length, Is.GreaterThan(0), p.Id);
                Assert.That(p.Function.ZhTW, Does.Not.Match("[0-9]"), p.Id + "：功能說明不放數字");
                // 採購終端的科普說明：組裝區看不到數字
                Assert.That(p.Explainer.En.Length * p.Explainer.ZhTW.Length, Is.GreaterThan(0), p.Id + "：缺科普說明");
                Assert.That(p.Explainer.ZhTW, Does.Not.Match("[0-9]"), p.Id + "：科普說明不放數字");
            }
        }

        [Test]
        public void TutorialDifficultyIsHidden()
        {
            var data = TestData.Load();
            Assert.That(data.SelectableDifficulties.Select(d => d.Id), Is.EqualTo(new[] { "engineer", "cadet", "director" }));
            var t = data.Difficulty("tutorial");
            Assert.That(t.Hidden && t.GuaranteedLaunch && t.Turns == 1);
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
