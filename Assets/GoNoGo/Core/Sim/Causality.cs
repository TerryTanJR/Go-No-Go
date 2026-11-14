using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>因果鏈的一步。Key 是多語字串鍵，Args 會代入 {name}。值以 "part:" 開頭時，畫面要換成零件名稱。</summary>
    public sealed class CauseStep
    {
        public string Key { get; set; } = "";
        public Dictionary<string, string> Args { get; set; } = new Dictionary<string, string>();

        public CauseStep() { }
        public CauseStep(string key, params (string k, string v)[] args)
        {
            Key = key;
            foreach (var (k, v) in args) Args[k] = v;
        }
    }

    public sealed class CausalChain
    {
        public string Code { get; set; } = "";
        public List<CauseStep> Steps { get; set; } = new List<CauseStep>();
        /// <summary>影響大小（約等於扣掉的分數），用來排序。</summary>
        public double Impact { get; set; }
        public bool Positive { get; set; }
    }

    /// <summary>任務報告「為什麼會這樣」：最多 3 條因果鏈 + 一個下一輪建議。</summary>
    public static class Causality
    {
        static string N(double v, int digits = 0) => Math.Round(v, digits).ToString("0." + new string('#', Math.Max(0, digits)), CultureInfo.InvariantCulture);
        static string Pct(double f) => N(f * 100.0);
        static string Part(string id) => "part:" + id;

        public static List<CausalChain> Build(Design d, GameData data, Evaluation ev, MissionResult r)
        {
            var chains = new List<CausalChain>();
            var launcher = data.Launcher(d.LauncherId);
            var orbit = data.Orbit(d.OrbitId);
            string L = "launcher:" + d.LauncherId, O = "orbit:" + d.OrbitId;
            var lost = new CauseStep("cause.mission_lost");

            switch (r.Launch.Outcome)
            {
                case LaunchOutcome.Failure when r.Launch.ReasonCode == "launch.unreachable":
                    chains.Add(Chain("launch_unreachable", 100,
                        new CauseStep("cause.launch_unreachable", ("launcher", L), ("orbit", O)), lost));
                    break;
                case LaunchOutcome.Failure when r.Launch.ReasonCode == "launch.overweight":
                    chains.Add(Chain("launch_overweight", 100,
                        new CauseStep("cause.mass_total", ("wet", N(ev.Mass.WetKg)), ("cap", N(ev.Mass.LauncherCapacityKg)), ("launcher", L)),
                        new CauseStep("cause.launch_overweight"), lost));
                    break;
                case LaunchOutcome.Failure:
                    chains.Add(Chain("launch_anomaly", 100,
                        new CauseStep("cause.launch_reliability", ("launcher", L), ("rel", Pct(r.Launch.Reliability))),
                        new CauseStep("cause.launch_anomaly"), lost));
                    break;
                case LaunchOutcome.FairingFailure:
                    chains.Add(Chain("fairing", 100,
                        new CauseStep("cause.fairing", ("launcher", L)),
                        new CauseStep("cause.fairing_mass"), lost));
                    break;
                case LaunchOutcome.DeadOnOrbit:
                    chains.Add(Chain("dead_on_orbit", 100,
                        new CauseStep("cause.battery_capacity", ("wh", N(ev.Power.BatteryWh)), ("part", Part(d.Get(PartCategory.Battery)))),
                        new CauseStep("cause.eclipse_need", ("min", N(ev.Orbit.EclipseMin)),
                            ("need", N(ev.Power.EclipseEnergyWh / Math.Max(1e-6, data.Constants.BatteryEfficiency)))),
                        new CauseStep("cause.dead_on_orbit"), lost));
                    break;
                case LaunchOutcome.LowerOrbit:
                    chains.Add(Chain("lower_orbit", 25,
                        new CauseStep("cause.mass_margin", ("margin", Pct(r.Launch.MassMargin)), ("launcher", L)),
                        new CauseStep("cause.lower_orbit"),
                        new CauseStep("cause.drag_doubled", ("years", N(r.ActualYears, 1)))));
                    break;
            }
            if (!r.ReachedOrbit) return chains;

            // 資料遺失
            double lostFrac = r.DataGeneratedGb > 0 ? r.DataLostGb / r.DataGeneratedGb : 0;
            if (lostFrac >= 0.08)
            {
                var top = d.Instruments.Select(data.Part).Where(p => p != null).OrderByDescending(p => p.DataGbPerDay).FirstOrDefault();
                bool storageBound = ev.Data.StorageGb < ev.Data.StorageNeededGb / data.Constants.StorageSafetyFactor
                                    && ev.Data.DownlinkGbPerDay >= ev.Data.GeneratedGbPerDay;
                var gs = data.Part(d.Get(PartCategory.GroundStation));
                chains.Add(Chain(storageBound ? "storage_full" : "data_loss", lostFrac * 60,
                    new CauseStep("cause.data_generated", ("gb", N(ev.Data.GeneratedGbPerDay, 1)), ("part", Part(top?.Id ?? ""))),
                    storageBound
                        ? new CauseStep("cause.storage_capacity", ("gb", N(ev.Data.StorageGb)), ("part", Part(d.Get(PartCategory.Computer))))
                        : new CauseStep("cause.downlink_capacity", ("gb", N(ev.Data.DownlinkGbPerDay, 1)),
                            ("part", Part(d.Get(PartCategory.Antenna))), ("stations", (gs?.Stations ?? 0).ToString(CultureInfo.InvariantCulture))),
                    new CauseStep("cause.data_lost", ("pct", Pct(lostFrac)))));
            }

            // 電力不足 → 儀器降載
            bool powerShort = ev.Power.ArrayEolW < ev.Power.RequiredArrayW || ev.Power.ActualDod > 1;
            if (powerShort && r.AverageDuty < 0.97)
            {
                chains.Add(Chain("power_limited", (1 - r.AverageDuty) * 50,
                    new CauseStep("cause.power_load", ("w", N(ev.Power.RequiredArrayW))),
                    new CauseStep("cause.array_eol", ("w", N(ev.Power.ArrayEolW)), ("years", N(d.MissionYears))),
                    new CauseStep("cause.duty", ("pct", Pct(r.AverageDuty)))));
            }

            // 提早結束
            if (r.ActualYears < r.TargetYears - 0.01)
            {
                double impact = (1 - r.ActualYears / r.TargetYears) * 60;
                var end = new CauseStep("cause.ended_early", ("years", N(r.ActualYears, 1)), ("target", N(r.TargetYears)));
                switch (r.EndReason)
                {
                    case EndReason.Propellant:
                        chains.Add(Chain("lifetime_propellant", impact,
                            new CauseStep("cause.orbit_drag", ("orbit", O), ("dv", N(orbit?.DeltaVPerYearMs ?? 0))),
                            new CauseStep("cause.propellant_out", ("kg", N(ev.Mass.PropellantKg, 1)), ("part", Part(d.Get(PartCategory.Propulsion)))),
                            end));
                        break;
                    case EndReason.Battery:
                        chains.Add(Chain("lifetime_battery", impact,
                            new CauseStep("cause.eclipses", ("n", N(ev.Orbit.EclipsesPerYear)), ("dod", Pct(Math.Min(1, ev.Power.ActualDod)))),
                            new CauseStep("cause.battery_worn", ("part", Part(d.Get(PartCategory.Battery)))),
                            end));
                        break;
                    case EndReason.Funding:
                        chains.Add(Chain("lifetime_funding", impact,
                            new CauseStep("cause.funding_left", ("left", N(Math.Max(0, ev.Cost.BudgetMusd - ev.Cost.DevelopmentMusd - ev.Cost.LaunchMusd), 1)),
                                ("per", N(ev.Cost.OpsPerYearMusd, 2))),
                            new CauseStep("cause.funding_out", ("years", N(r.FundedYears, 1))),
                            end));
                        break;
                    case EndReason.Radiation:
                        chains.Add(Chain("lifetime_radiation", impact,
                            new CauseStep("cause.radiation_dose", ("orbit", O), ("dose", N(orbit?.DoseKradPerYear ?? 0, 1))),
                            new CauseStep("cause.radiation_limit", ("part", Part(d.Get(PartCategory.Computer))), ("shield", Part(d.Get(PartCategory.Shielding)))),
                            end));
                        break;
                }
            }

            // 事件
            foreach (var o in r.Events)
            {
                if (o.ResultCode == "event.storm.damaged")
                {
                    chains.Add(Chain("storm_damage", 12,
                        new CauseStep("cause.storm", ("event", "sw:" + o.Event.RefId)),
                        new CauseStep("cause.kept_running"),
                        new CauseStep("cause.instrument_damaged", ("part", Part(o.AffectedPartId)))));
                }
                else if (o.ResultCode == "event.power.drained")
                {
                    chains.Add(Chain("power_anomaly", 10,
                        new CauseStep("cause.power_anomaly"),
                        new CauseStep("cause.not_handled", ("done", o.StepsDone.ToString(CultureInfo.InvariantCulture)), ("total", o.StepsRequired.ToString(CultureInfo.InvariantCulture))),
                        new CauseStep("cause.battery_worn", ("part", Part(d.Get(PartCategory.Battery))))));
                }
                else if (o.ResultCode.StartsWith("event.failure.") && o.ResultCode != "event.failure.redundant" && !o.ResultCode.EndsWith("_contained"))
                {
                    chains.Add(Chain("part_failure", 12,
                        new CauseStep("cause.part_failed", ("part", Part(o.AffectedPartId))),
                        new CauseStep("cause.no_redundancy"),
                        new CauseStep("cause.degraded")));
                }
                else if (o.ResultCode == "event.storm.safe")
                {
                    chains.Add(Chain("storm_safe", 2,
                        new CauseStep("cause.storm", ("event", "sw:" + o.Event.RefId)),
                        new CauseStep("cause.safe_mode", ("days", o.LostDays.ToString(CultureInfo.InvariantCulture))),
                        new CauseStep("cause.protected")));
                }
            }

            // 野火關卡：通報延遲、過境時段、火點儀、火災事件
            var scenario = data.Scenario(d.ScenarioId);
            if (scenario != null && scenario.FireAlert) FireChains(chains, d, data, ev, r, scenario, O);

            // 科學門檻：評等被壓到 C 或判 FAIL 時一定要說明，不然總分 70 拿 C 看不懂
            var sc = r.Score;
            if (sc != null && (sc.GradeCapped || sc.ScienceFail))
            {
                chains.Add(Chain("science_low", 40,
                    new CauseStep("cause.science_low", ("score", N(sc.Science)), ("need", N(sc.ScienceThreshold))),
                    new CauseStep(sc.ScienceFail ? "cause.science_fail" : "cause.grade_capped")));
            }

            // 沒有問題時，說明成功的原因
            if (!chains.Any(c => c.Impact >= 5))
            {
                bool fire = scenario != null && scenario.FireAlert;
                // 野火關卡的主角是火點儀（看偵測率），其他關卡看科學值
                var best = d.Instruments.Select(data.Part).Where(p => p != null)
                    .OrderByDescending(p => fire ? p.FireDetection : p.ScienceValue).FirstOrDefault();
                var good = Chain("success", 0,
                    new CauseStep("cause.success_instrument", ("part", Part(best?.Id ?? ""))),
                    fire ? new CauseStep("cause.success_latency", ("min", N(ev.Fire.LatencyMin)), ("limit", N(scenario.LatencyLimitMin)))
                         : new CauseStep("cause.success_return", ("pct", Pct(r.ReturnFraction))),
                    new CauseStep("cause.success_science", ("score", N(r.Score?.Science ?? 0))));
                good.Positive = true;
                chains.Insert(0, good);
            }

            return chains.OrderByDescending(c => c.Impact).ThenBy(c => c.Code, StringComparer.Ordinal).Take(3).ToList();
        }

        static void FireChains(List<CausalChain> chains, Design d, GameData data, Evaluation ev, MissionResult r, ScenarioDef scenario, string O)
        {
            var fi = ev.Fire;
            var gs = data.Part(d.Get(PartCategory.GroundStation));
            string I(int v) => v.ToString(CultureInfo.InvariantCulture);
            if (fi.TimelyFraction < 0.999)
            {
                chains.Add(Chain("latency", (1 - fi.TimelyFraction) * 60,
                    new CauseStep("cause.gs_wait", ("stations", I(gs?.Stations ?? 0)), ("wait", N(fi.WaitMin))),
                    new CauseStep("cause.latency_total", ("min", N(fi.LatencyMin)), ("limit", N(scenario.LatencyLimitMin)),
                        ("downlink", N(fi.DownlinkMin, 1)), ("part", Part(d.Get(PartCategory.Antenna)))),
                    new CauseStep("cause.timely", ("pct", Pct(fi.TimelyFraction)))));
            }
            if (fi.InstrumentDetection < 0.5)
            {
                chains.Add(Chain("no_fire_detector", 45,
                    new CauseStep("cause.no_fire_detector"),
                    new CauseStep("cause.detection", ("pct", Pct(fi.Detection)))));
            }
            else if (fi.OrbitFactor < 0.999)
            {
                chains.Add(Chain("pass_time", (1 - fi.OrbitFactor) * 40,
                    new CauseStep("cause.pass_time", ("orbit", O)),
                    new CauseStep("cause.fire_weak", ("pct", Pct(fi.OrbitFactor))),
                    new CauseStep("cause.detection", ("pct", Pct(fi.Detection)))));
            }
            foreach (var o in r.Events)
            {
                if (o.Event.Kind == EventKind.BigFire && o.AlertFactor < 0.999)
                    chains.Add(Chain("big_fire", 9,
                        new CauseStep("cause.big_fire", ("site", "site:" + o.Event.RefId)),
                        new CauseStep("cause.not_handled", ("done", I(o.StepsDone)), ("total", I(o.StepsRequired))),
                        new CauseStep("cause.fire_stuck")));
                else if (o.Event.Kind == EventKind.FalseFire && o.Voided)
                    chains.Add(Chain("false_fire", 9,
                        new CauseStep("cause.false_fire", ("site", "site:" + o.Event.RefId)),
                        new CauseStep(d.Instruments.Contains("inst_thermal") ? "cause.lwir_flat" : "cause.no_lwir"),
                        new CauseStep("cause.false_reported")));
            }
        }

        public static string Suggest(List<CausalChain> chains, Evaluation ev) => Suggest(chains, ev, false);

        public static string Suggest(List<CausalChain> chains, Evaluation ev, bool fire)
        {
            // 「科學分不夠」是結果；建議優先針對造成它的原因（其他因果鏈），沒有才給通用建議
            var top = chains.FirstOrDefault(c => !c.Positive && c.Impact >= 2 && c.Code != "science_low");
            if (top == null && chains.Any(c => c.Code == "science_low")) return "suggest.science_low";
            if (top != null)
            {
                switch (top.Code)
                {
                    case "launch_unreachable": return "suggest.launcher_reach";
                    case "launch_overweight": return "suggest.mass";
                    case "launch_anomaly": return "suggest.launcher";
                    case "fairing": return "suggest.history_launcher";
                    case "dead_on_orbit": return "suggest.battery_eclipse";
                    case "power_anomaly": return "suggest.power_procedure";
                    case "lower_orbit": return "suggest.mass_margin";
                    case "data_loss": return "suggest.downlink";
                    case "storage_full": return "suggest.storage";
                    case "power_limited": return "suggest.power";
                    case "lifetime_propellant": return "suggest.propellant";
                    case "lifetime_battery": return "suggest.battery";
                    case "lifetime_radiation": return "suggest.shielding";
                    case "storm_damage": return "suggest.storm_procedure";
                    case "part_failure": return "suggest.redundancy";
                    case "storm_safe": return "suggest.shielding";
                    case "latency": return "suggest.latency";
                    case "no_fire_detector": return "suggest.fire_detector";
                    case "pass_time": return "suggest.pass_time";
                    case "big_fire": return "suggest.fire_procedure";
                    case "false_fire": return "suggest.false_fire";
                }
            }
            // 野火關卡沒出問題時：提示另一種正解的取捨（不叫人換軌道，下午軌道本來就是對的）
            if (fire) return "suggest.fire_tradeoff";
            return ev[ResourceId.Budget].Margin > 0.25 ? "suggest.more_science" : "suggest.try_orbit";
        }

        static CausalChain Chain(string code, double impact, params CauseStep[] steps) =>
            new CausalChain { Code = code, Impact = impact, Steps = steps.ToList() };
    }
}
