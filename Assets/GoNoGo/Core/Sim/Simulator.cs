using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>評分與點名需要、但不影響物理模擬的資訊。</summary>
    public sealed class MissionContext
    {
        public string DifficultyId { get; set; }
        public IDictionary<Role, RequirementDef> Requirements { get; set; } = new Dictionary<Role, RequirementDef>();
        /// <summary>第一次點名就全員 GO（團隊協作加分）。</summary>
        public bool FirstCallAllGo { get; set; }
    }

    /// <summary>
    /// 任務模擬：發射判定 + 逐日營運。只用 System.Random(seed)，
    /// 同設計 + 同種子 + 同選擇 = 同結果。
    /// </summary>
    public static class Simulator
    {
        static readonly string[] Opportunities = { "wildfire", "volcano", "flood", "hurricane" };

        public static MissionResult SimulateMission(Design design, GameData data, int seed, IDecisionSource decisions) =>
            SimulateMission(design, data, seed, decisions, new MissionContext());

        /// <summary>
        /// 規劃發射擲骰與營運事件。所有亂數都在這裡一次擲完，
        /// 玩家在營運中途做的選擇不會改變後續事件。
        /// </summary>
        public static MissionPlan PlanMission(Design design, GameData data, int seed, string difficultyId)
        {
            var rng = new Random(seed);
            var diff = data.Difficulty(difficultyId) ?? data.Difficulties.FirstOrDefault() ?? new DifficultyDef();
            var plan = new MissionPlan { Seed = seed, LaunchRoll = rng.NextDouble(), LaunchRoll2 = rng.NextDouble() };

            int years = Math.Max(1, design.MissionYears);
            int days = (int)Math.Round(years * 365.25);
            int n = rng.Next(diff.EventsMin, diff.EventsMax + 1);

            // 會故障的零件，依不可靠度加權
            var failable = design.Instruments
                .Concat(new[] { design.Get(PartCategory.Computer), design.Get(PartCategory.Adcs), design.Get(PartCategory.Antenna) })
                .Select(data.Part).Where(p => p != null).ToList();
            double survive = failable.Aggregate(1.0, (acc, p) => acc * Math.Pow(p.ReliabilityPerYear, years));
            double pFail = Math.Min(0.85, Math.Max(0.15, 1 - survive));
            double failRoll = rng.NextDouble();

            var kinds = new List<EventKind> { EventKind.SolarStorm, EventKind.ScienceOpportunity };
            if (failRoll < pFail) kinds.Add(EventKind.PartFailure);
            while (kinds.Count < n) kinds.Add(rng.NextDouble() < 0.5 ? EventKind.SolarStorm : EventKind.ScienceOpportunity);
            if (kinds.Count > n) kinds = kinds.Take(n).ToList();
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }

            int start = Math.Min(30, days / 4), end = Math.Max(start + 1, days - 30);
            double seg = (end - start) / (double)Math.Max(1, kinds.Count);
            for (int i = 0; i < kinds.Count; i++)
            {
                var e = new MissionEvent
                {
                    Id = $"{kinds[i].ToString().ToLowerInvariant()}_{i}",
                    Kind = kinds[i],
                    Day = start + (int)(seg * i + rng.NextDouble() * seg * 0.8),
                };
                switch (e.Kind)
                {
                    case EventKind.SolarStorm:
                        var sw = data.SpaceWeather[rng.Next(data.SpaceWeather.Count)];
                        e.RefId = sw.Id;
                        e.Severity = Math.Max(1, Math.Min(3, sw.Severity));
                        e.Choices = new List<string> { "safe_mode", "continue" };
                        e.DefaultChoice = "safe_mode";
                        break;
                    case EventKind.PartFailure:
                        double total = failable.Sum(p => 1 - p.ReliabilityPerYear);
                        double pick = rng.NextDouble() * total;
                        var part = failable.Count > 0 ? failable[failable.Count - 1] : null;
                        foreach (var p in failable)
                        {
                            pick -= 1 - p.ReliabilityPerYear;
                            if (pick <= 0) { part = p; break; }
                        }
                        e.RefId = part?.Id ?? "";
                        e.Severity = 2;
                        e.Choices = new List<string> { "ack" };
                        e.DefaultChoice = "ack";
                        break;
                    default:
                        e.RefId = Opportunities[rng.Next(Opportunities.Length)];
                        e.Severity = 1 + rng.Next(2);
                        e.Choices = new List<string> { "observe", "skip" };
                        e.DefaultChoice = "observe";
                        break;
                }
                e.Roll = rng.NextDouble();
                e.Roll2 = rng.NextDouble();
                plan.Events.Add(e);
            }
            plan.Events = plan.Events.OrderBy(e => e.Day).ToList();
            return plan;
        }

        public static LaunchResult ResolveLaunch(Design design, GameData data, Evaluation ev, MissionPlan plan)
        {
            var launcher = data.Launcher(design.LauncherId);
            var scenario = data.Scenario(design.ScenarioId);
            var r = new LaunchResult
            {
                MassMargin = ev[ResourceId.Mass].Margin,
                Reliability = launcher?.Reliability ?? 0,
                Roll = plan.LaunchRoll,
            };
            var hist = scenario?.Historical;
            if (!ev.Mass.OrbitReachable) { r.Outcome = LaunchOutcome.Failure; r.ReasonCode = "launch.unreachable"; }
            else if (r.MassMargin < 0) { r.Outcome = LaunchOutcome.Failure; r.ReasonCode = "launch.overweight"; }
            else if (hist != null && hist.ScriptedLauncherId == design.LauncherId && hist.ScriptedEvents.Contains("fairing_stuck"))
            { r.Outcome = LaunchOutcome.FairingFailure; r.ReasonCode = "launch.fairing"; }
            else if (plan.LaunchRoll > r.Reliability) { r.Outcome = LaunchOutcome.Failure; r.ReasonCode = "launch.anomaly"; }
            else if (r.MassMargin < 0.05 && plan.LaunchRoll2 < 0.5) { r.Outcome = LaunchOutcome.LowerOrbit; r.ReasonCode = "launch.lower_orbit"; }
            else { r.Outcome = LaunchOutcome.Nominal; r.ReasonCode = "launch.nominal"; }
            return r;
        }

        public static MissionResult SimulateMission(Design design, GameData data, int seed, IDecisionSource decisions, MissionContext ctx)
        {
            ctx ??= new MissionContext();
            decisions ??= new FixedDecisions();
            var c = data.Constants;
            var ev = Evaluator.EvaluateDesign(design, data, ctx.DifficultyId, false);
            var plan = PlanMission(design, data, seed, ctx.DifficultyId);
            var result = new MissionResult { Seed = seed, TargetYears = Math.Max(1, design.MissionYears) };
            result.Launch = ResolveLaunch(design, data, ev, plan);

            if (!result.Launch.Success)
            {
                result.EndReason = EndReason.LaunchFailure;
                Finish(result, design, data, ev, ctx);
                return result;
            }

            var scenario = data.Scenario(design.ScenarioId);
            var orbit = data.Orbit(design.OrbitId);
            string objective = scenario?.Objective ?? "";
            double orbitMatch = orbit.ObjectiveMatch.TryGetValue(objective, out var om) ? om : 0;
            var inst = design.Instruments.Select(data.Part).Where(p => p != null).ToList();
            var health = inst.Select(_ => 1.0).ToList();
            var propulsion = data.Part(design.Get(PartCategory.Propulsion));
            var computer = data.Part(design.Get(PartCategory.Computer));
            var shielding = data.Part(design.Get(PartCategory.Shielding));
            bool lower = result.Launch.Outcome == LaunchOutcome.LowerOrbit;
            double dragFactor = lower ? 2.0 : 1.0;

            int targetDays = (int)Math.Round(result.TargetYears * 365.25);
            double storageCap = ev.Data.StorageGb;
            double antennaHealth = 1.0;
            double prop = ev.Mass.PropellantKg, mass = ev.Mass.WetKg;
            double isp = propulsion?.IspS ?? 0;
            double dvPerDay = orbit.DeltaVPerYearMs * dragFactor / 365.25;
            double naturalDays = orbit.NaturalServiceYears * 365.25 / dragFactor;
            double cyclesPerDay = ev.Orbit.EclipsesPerYear / 365.25;
            double cycleLimit = ev.Power.ActualDod >= 1 ? 1 : Evaluator.CycleLife(c, ev.Power.ActualDod);
            double cycles = 0;
            double doseFactor = shielding?.DoseFactor ?? 1;
            double dosePerDay = orbit.DoseKradPerYear * doseFactor / 365.25;
            double tolerance = computer?.RadToleranceKrad ?? 0;
            double dose = 0;
            double battDuty = ev.Power.ActualDod > 1 ? 1.0 / ev.Power.ActualDod : 1.0;

            double backlog = 0, backlogSci = 0;
            int safeDays = 0;
            double dutySum = 0;
            int step = Math.Max(1, targetDays / 360);
            var queue = new Queue<MissionEvent>(plan.Events);
            int day = 0;
            result.EndReason = EndReason.MissionComplete;

            while (day < targetDays)
            {
                while (queue.Count > 0 && queue.Peek().Day <= day)
                {
                    var e = queue.Dequeue();
                    var o = ApplyEvent(e, decisions.Decide(e), inst, health, ev, data, doseFactor,
                        ref safeDays, ref storageCap, ref antennaHealth, ref dose, ref cycles, cycleLimit);
                    if (o.ScienceDelta > 0) result.ScienceTotal += o.ScienceDelta;
                    result.Events.Add(o);
                }

                double years = day / 365.25;
                double arrayW = ev.Power.ArrayBolW * Math.Pow(1 - c.SolarDegradationPerYear, years);
                double duty = ev.Power.RequiredArrayW > 0 ? Math.Min(1.0, arrayW / ev.Power.RequiredArrayW) : 1.0;
                duty = Math.Min(duty, battDuty);
                bool safe = safeDays > 0;
                if (safe) safeDays--;
                double on = safe ? 0 : duty;
                dutySum += on;

                double gen = 0, sci = 0;
                for (int i = 0; i < inst.Count; i++)
                {
                    gen += inst[i].DataGbPerDay * health[i] * on;
                    sci += inst[i].ScienceValue * Evaluator.Match(inst[i], objective) * orbitMatch * health[i] * on / 365.25 * c.ScienceYearScale;
                }
                result.DataGeneratedGb += gen;
                backlog += gen; backlogSci += sci;
                double down = Math.Min(backlog, ev.Data.DownlinkGbPerDay * antennaHealth);
                double credit = backlog > 0 ? backlogSci * down / backlog : 0;
                backlog -= down; backlogSci -= credit;
                result.DataReturnedGb += down;
                result.ScienceTotal += credit;
                if (backlog > storageCap)
                {
                    double over = backlog - storageCap;
                    double lostSci = backlogSci * over / backlog;
                    backlog = storageCap; backlogSci -= lostSci;
                    result.DataLostGb += over;
                }

                // 消耗：推進劑（火箭方程式逐日積分）→ 用完後自然衰減倒數
                if (dvPerDay > 0)
                {
                    if (prop > 0 && isp > 0)
                    {
                        double dm = mass * (1 - Math.Exp(-dvPerDay / (isp * c.G0Ms2)));
                        prop -= dm; mass -= dm;
                        if (prop < 0) prop = 0;
                    }
                    else if (--naturalDays <= 0) { result.EndReason = EndReason.Propellant; day++; break; }
                }
                cycles += cyclesPerDay;
                if (cycles > cycleLimit) { result.EndReason = EndReason.Battery; day++; break; }
                dose += dosePerDay;
                if (tolerance > 0 && dose > tolerance) { result.EndReason = EndReason.Radiation; day++; break; }

                if (day % step == 0)
                    result.Samples.Add(MakeSample(day, ev, cycles, cycleLimit, backlog, storageCap, result, prop, arrayW, on, safe));
                day++;
            }
            result.Samples.Add(MakeSample(day, ev, cycles, cycleLimit, backlog, storageCap, result, prop,
                ev.Power.ArrayBolW * Math.Pow(1 - c.SolarDegradationPerYear, day / 365.25), 0, false));

            // 營運結束前還沒傳回的資料算遺失
            result.DataLostGb += backlog;
            result.ActualYears = day / 365.25;
            result.AverageDuty = day > 0 ? dutySum / day : 0;
            result.ReturnFraction = result.DataGeneratedGb > 0 ? result.DataReturnedGb / result.DataGeneratedGb : 0;
            Finish(result, design, data, ev, ctx);
            return result;
        }

        static Sample MakeSample(int day, Evaluation ev, double cycles, double cycleLimit, double backlog, double storageCap,
            MissionResult r, double prop, double arrayW, double duty, bool safe) => new Sample
        {
            Day = day,
            BatterySoc = Math.Max(0, 1 - Math.Min(1, ev.Power.ActualDod * (1 + 0.25 * Math.Min(1, cycles / Math.Max(1, cycleLimit))))),
            StorageFill = storageCap > 0 ? Math.Min(1, backlog / storageCap) : 0,
            DataReturnedGb = r.DataReturnedGb,
            PropellantKg = prop,
            Science = r.ScienceTotal,
            ArrayW = arrayW,
            Duty = duty,
            SafeMode = safe,
        };

        static EventOutcome ApplyEvent(MissionEvent e, string choice, List<PartDef> inst, List<double> health, Evaluation ev,
            GameData data, double doseFactor, ref int safeDays, ref double storageCap, ref double antennaHealth,
            ref double dose, ref double cycles, double cycleLimit)
        {
            var o = new EventOutcome { Event = e, Choice = choice };
            switch (e.Kind)
            {
                case EventKind.SolarStorm:
                    if (choice == "safe_mode")
                    {
                        o.LostDays = 3 + 3 * e.Severity;
                        safeDays += o.LostDays;
                        o.ResultCode = "event.storm.safe";
                    }
                    else
                    {
                        dose += 0.3 * e.Severity * doseFactor;
                        double p = Math.Min(0.85, 0.2 * e.Severity * Math.Sqrt(doseFactor));
                        if (e.Roll < p && inst.Count > 0)
                        {
                            int i = Math.Min(inst.Count - 1, (int)(e.Roll2 * inst.Count));
                            health[i] *= 0.5;
                            o.AffectedPartId = inst[i].Id;
                            o.ResultCode = "event.storm.damaged";
                        }
                        else o.ResultCode = "event.storm.ok";
                    }
                    break;

                case EventKind.PartFailure:
                    var part = data.Part(e.RefId);
                    o.AffectedPartId = e.RefId;
                    if (part == null) { o.ResultCode = "event.failure.redundant"; break; }
                    if (part.Redundant) { o.ResultCode = "event.failure.redundant"; break; }
                    switch (part.Category)
                    {
                        case PartCategory.Instrument:
                            int idx = inst.FindIndex(p => p.Id == part.Id);
                            if (idx >= 0) health[idx] *= 0.4;
                            o.ResultCode = "event.failure.instrument";
                            break;
                        case PartCategory.Computer:
                            storageCap *= 0.5;
                            o.ResultCode = "event.failure.computer";
                            break;
                        case PartCategory.Adcs:
                            for (int i = 0; i < health.Count; i++) health[i] *= 0.75;
                            o.ResultCode = "event.failure.adcs";
                            break;
                        default:
                            antennaHealth = 0.5;
                            o.ResultCode = "event.failure.antenna";
                            break;
                    }
                    break;

                default:
                    if (choice == "skip") { o.ResultCode = "event.opportunity.skipped"; break; }
                    double bonus = ev.SciencePerYear * 0.04 * e.Severity;
                    if (ev.Power.ArrayMargin >= 0.05)
                    {
                        o.ScienceDelta = bonus;
                        o.ResultCode = "event.opportunity.success";
                    }
                    else
                    {
                        o.ScienceDelta = bonus * 0.5;
                        cycles += cycleLimit * 0.03;
                        o.ResultCode = "event.opportunity.strained";
                    }
                    break;
            }
            return o;
        }

        static void Finish(MissionResult r, Design design, GameData data, Evaluation ev, MissionContext ctx)
        {
            r.Score = Scoring.Compute(design, data, ev, r, ctx);
            r.Causes = Causality.Build(design, data, ev, r);
            r.SuggestionCode = Causality.Suggest(r.Causes, ev);
        }
    }
}
