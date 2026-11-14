using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>評分需要、但不影響物理模擬的資訊。</summary>
    public sealed class MissionContext
    {
        public string DifficultyId { get; set; }
        /// <summary>這一局有效的處室需求。</summary>
        public List<ActiveRequirement> Requirements { get; set; } = new List<ActiveRequirement>();
        /// <summary>這次發射之前已經失敗過幾次（重新設計的次數）。</summary>
        public int FailedLaunchesBefore { get; set; }
    }

    /// <summary>
    /// 任務模擬：發射判定 + 逐日營運。只用 System.Random(seed)，
    /// 同設計 + 同種子 + 同選擇 = 同結果。
    /// </summary>
    public static class Simulator
    {
        static readonly string[] Opportunities = { "wildfire", "volcano", "flood", "hurricane" };
        static readonly string[] FireSites = { "forest", "grassland" };
        static readonly string[] FalseSites = { "desert", "glint" };

        /// <summary>
        /// 營運經費撐得了幾年：(預算 − 開發費 − 這次用的火箭) ÷ 每年營運費，可以有小數，小於 0 當成 0。
        /// 沒有預算或沒有營運費時是無限大（不限制）。
        /// </summary>
        public static double FundedYears(Evaluation ev)
        {
            var co = ev.Cost;
            if (co.BudgetMusd <= 0 || co.OpsPerYearMusd <= 0) return double.PositiveInfinity;
            return Math.Max(0, (co.BudgetMusd - co.DevelopmentMusd - co.LaunchMusd) / co.OpsPerYearMusd);
        }

        public static MissionResult SimulateMission(Design design, GameData data, int seed, IDecisionSource decisions) =>
            SimulateMission(design, data, seed, decisions, new MissionContext());

        /// <summary>
        /// 規劃發射擲骰與營運事件。所有亂數都在這裡一次擲完，
        /// 玩家在營運中途做的選擇不會改變後續事件。
        /// </summary>
        public static MissionPlan PlanMission(Design design, GameData data, int seed, string difficultyId)
        {
            var rng = new Random(seed);
            var diff = data.Difficulty(difficultyId) ?? data.DefaultDifficulty ?? new DifficultyDef();
            var plan = new MissionPlan { Seed = seed, LaunchRoll = rng.NextDouble(), LaunchRoll2 = rng.NextDouble() };
            if (diff.GuaranteedLaunch) { plan.LaunchRoll = 0; plan.LaunchRoll2 = 1; }

            int years = Math.Max(1, design.MissionYears);
            int days = (int)Math.Round(years * 365.25);
            int n = Math.Max(1, diff.Turns);
            plan.Turns = n;
            for (int i = 0; i <= n; i++) plan.TurnStartDays.Add((int)Math.Round(days * (double)i / n));

            // 會故障的零件，依不可靠度加權
            var failable = design.Instruments
                .Concat(new[] { design.Get(PartCategory.Computer), design.Get(PartCategory.Adcs), design.Get(PartCategory.Antenna) })
                .Select(data.Part).Where(p => p != null).ToList();

            // 每回合一個事件：把可用的事件種類輪流排滿，再洗牌（同一種事件不會連出太多次）
            var pool = GameData.EventTypes(data.Scenario(design.ScenarioId), diff).Select(OpsEvents.Parse).Where(k => k.HasValue).Select(k => k.Value).Distinct().ToList();
            if (pool.Count == 0) pool = new List<EventKind> { EventKind.SolarStorm, EventKind.ScienceOpportunity };
            if (failable.Count == 0) pool.Remove(EventKind.PartFailure);
            if (pool.Count == 0) pool.Add(EventKind.SolarStorm);
            var kinds = Enumerable.Range(0, n).Select(i => pool[i % pool.Count]).ToList();
            for (int i = kinds.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (kinds[i], kinds[j]) = (kinds[j], kinds[i]);
            }

            for (int i = 0; i < n; i++)
            {
                int a = plan.TurnStartDays[i], b = plan.TurnStartDays[i + 1];
                var e = new MissionEvent
                {
                    Id = OpsEvents.Key(kinds[i]) + "_" + i,
                    Kind = kinds[i],
                    Turn = i,
                    Day = Math.Min(b - 1, a + (int)((b - a) * (0.3 + 0.4 * rng.NextDouble()))),
                };
                switch (e.Kind)
                {
                    case EventKind.SolarStorm:
                        var storms = data.StormsDuring(data.Scenario(design.ScenarioId), years);
                        var sw = storms[rng.Next(storms.Count)];
                        e.RefId = sw.Id;
                        e.Severity = Math.Max(1, Math.Min(3, sw.Severity));
                        break;
                    case EventKind.PartFailure:
                        double total = failable.Sum(p => 1 - p.ReliabilityPerYear);
                        double pick = rng.NextDouble() * total;
                        var part = failable[failable.Count - 1];
                        foreach (var p in failable)
                        {
                            pick -= 1 - p.ReliabilityPerYear;
                            if (pick <= 0) { part = p; break; }
                        }
                        e.RefId = part.Id;
                        e.Severity = 2;
                        break;
                    case EventKind.PowerAnomaly:
                        e.Severity = 1 + rng.Next(3);
                        break;
                    case EventKind.BigFire:
                        e.RefId = FireSites[rng.Next(FireSites.Length)];
                        e.Severity = 1 + rng.Next(3);
                        break;
                    case EventKind.FalseFire:
                        e.RefId = FalseSites[rng.Next(FalseSites.Length)];
                        e.Severity = 1 + rng.Next(2);
                        break;
                    default:
                        e.RefId = Opportunities[rng.Next(Opportunities.Length)];
                        e.Severity = 1 + rng.Next(2);
                        break;
                }
                e.Roll = rng.NextDouble();
                e.Roll2 = rng.NextDouble();
                plan.Events.Add(e);
            }
            return plan;
        }

        public static LaunchResult ResolveLaunch(Design design, GameData data, Evaluation ev, MissionPlan plan)
        {
            var scenario = data.Scenario(design.ScenarioId);
            var r = new LaunchResult
            {
                MassMargin = ev[ResourceId.Mass].Margin,
                Roll = plan.LaunchRoll,
            };
            var hist = scenario?.Historical;
            if (!ev.Mass.OrbitReachable) { r.Outcome = LaunchOutcome.Failure; r.ReasonCode = "launch.unreachable"; }
            else if (r.MassMargin < 0) { r.Outcome = LaunchOutcome.Failure; r.ReasonCode = "launch.overweight"; }
            else if (hist != null && hist.ScriptedLauncherId == design.LauncherId && hist.ScriptedEvents.Contains("fairing_stuck"))
            { r.Outcome = LaunchOutcome.FairingFailure; r.ReasonCode = "launch.fairing"; }
            // 火箭不會隨機爆炸，也沒有可靠度：火箭不是玩家做的，失敗只來自玩家的設計和選擇。
            // 發射骰照擲（記在 Roll），亂數順序不變，其他結果才不會跟著變。
            // 電池撐不過一次地影：部署後第一次進地影就斷電（原本由 Go/No-Go 點名擋下，點名拿掉後改成發射後果）
            else if (ev.Power.ActualDod > 1.0) { r.Outcome = LaunchOutcome.DeadOnOrbit; r.ReasonCode = "launch.dead_on_orbit"; }
            else if (r.MassMargin < 0.05 && plan.LaunchRoll2 < 0.5) { r.Outcome = LaunchOutcome.LowerOrbit; r.ReasonCode = "launch.lower_orbit"; }
            else { r.Outcome = LaunchOutcome.Nominal; r.ReasonCode = "launch.nominal"; }
            return r;
        }

        public static MissionResult SimulateMission(Design design, GameData data, int seed, IDecisionSource decisions, MissionContext ctx)
        {
            ctx ??= new MissionContext();
            decisions ??= new NoDecisions();
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

            // 無條件進位：Math.Round(730.5) 是 730（銀行家捨入），2 年任務只跑 1.9986 年，「撐滿年數」永遠判定沒達成
            int targetDays = (int)Math.Ceiling(result.TargetYears * 365.25);
            // 營運經費（採購機制 v2）：發射後剩下的錢付每年的營運費，錢用完的那一天任務結束
            result.FundedYears = FundedYears(ev);
            int fundedDays = result.FundedYears >= targetDays / 365.25 ? int.MaxValue : (int)Math.Floor(result.FundedYears * 365.25);
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
                if (day >= fundedDays) { result.EndReason = EndReason.Funding; break; }
                while (queue.Count > 0 && queue.Peek().Day <= day)
                {
                    var e = queue.Dequeue();
                    var o = ApplyEvent(e, decisions.Respond(e) ?? new EventResponse(), inst, health, ev, data, doseFactor,
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

        static EventOutcome ApplyEvent(MissionEvent e, EventResponse resp, List<PartDef> inst, List<double> health, Evaluation ev,
            GameData data, double doseFactor, ref int safeDays, ref double storageCap, ref double antennaHealth,
            ref double dose, ref double cycles, double cycleLimit)
        {
            var actions = resp.Actions ?? new List<string>();
            var o = new EventOutcome
            {
                Event = e,
                Judged = resp.Judged ?? "",
                JudgedCorrect = resp.Judged == OpsEvents.Key(e.Kind),
                StepsRequired = Procedures.Required(e.Kind).Count,
                StepsDone = Procedures.Done(e.Kind, actions),
                WrongActions = Procedures.Wrong(e.Kind, actions),
                ConflictedActions = Procedures.Conflicted(actions),
            };
            double f = o.StepsFraction;
            // 互相衝突的操作沒有效果（也不算步驟）；多餘操作的停機懲罰照算（按了就是按了）
            var effective = Procedures.Effective(actions);
            bool Did(string a) => effective.Contains(a);

            // 不必要的操作也有代價：沒事進安全模式、關掉儀器都會少觀測幾天
            if (o.WrongActions.Contains(OpsActions.SafeMode)) { safeDays += 3; o.LostDays += 3; }
            if (o.WrongActions.Contains(OpsActions.InstrumentsOff)) { safeDays += 1; o.LostDays += 1; }

            switch (e.Kind)
            {
                case EventKind.SolarStorm:
                    if (Did(OpsActions.SafeMode))
                    {
                        int lost = 3 + 3 * e.Severity;
                        safeDays += lost;
                        o.LostDays += lost;
                    }
                    // 處置做得越完整，劑量和損壞機率越低；全部做完就完全受保護
                    dose += 0.3 * e.Severity * doseFactor * (1 - f);
                    double p = Math.Min(0.85, 0.2 * e.Severity * Math.Sqrt(doseFactor)) * (1 - f);
                    if (f >= 1) o.ResultCode = "event.storm.safe";
                    else if (e.Roll < p && inst.Count > 0)
                    {
                        int i = Math.Min(inst.Count - 1, (int)(e.Roll2 * inst.Count));
                        health[i] *= 0.5;
                        o.AffectedPartId = inst[i].Id;
                        o.ResultCode = "event.storm.damaged";
                    }
                    else o.ResultCode = "event.storm.ok";
                    break;

                case EventKind.PowerAnomaly:
                    if (Did(OpsActions.InstrumentsOff)) { safeDays += 2; o.LostDays += 2; }
                    if (f >= 1) { o.ResultCode = "event.power.handled"; break; }
                    // 電池越大（餘裕越多）越撐得住
                    double k = Math.Max(0.3, Math.Min(1.5, 1 - ev.Power.BatteryMargin));
                    cycles += cycleLimit * 0.06 * e.Severity * (1 - f) * k;
                    o.ResultCode = "event.power.drained";
                    break;

                case EventKind.PartFailure:
                    var part = data.Part(e.RefId);
                    o.AffectedPartId = e.RefId;
                    if (part == null || part.Redundant) { o.ResultCode = "event.failure.redundant"; break; }
                    switch (part.Category)
                    {
                        case PartCategory.Instrument:
                            int idx = inst.FindIndex(x => x.Id == part.Id);
                            if (idx >= 0) health[idx] *= 0.4 + 0.4 * f;
                            o.ResultCode = "event.failure.instrument";
                            break;
                        case PartCategory.Computer:
                            storageCap *= 0.5 + 0.4 * f;
                            o.ResultCode = "event.failure.computer";
                            break;
                        case PartCategory.Adcs:
                            for (int i = 0; i < health.Count; i++) health[i] *= 0.75 + 0.2 * f;
                            o.ResultCode = "event.failure.adcs";
                            break;
                        default:
                            antennaHealth = 0.5 + 0.4 * f;
                            o.ResultCode = "event.failure.antenna";
                            break;
                    }
                    if (f >= 1) o.ResultCode += "_contained";
                    break;

                case EventKind.BigFire:
                    // 火點優先下傳 + 增加下傳：火點及時送到地面；沒做完，火點積在船上
                    o.AlertFactor = 0.5 + 0.5 * f;
                    o.ResultCode = f >= 1 ? "event.fire.reported" : f > 0 ? "event.fire.partial" : "event.fire.stuck";
                    break;

                case EventKind.FalseFire:
                    // 把假火點下傳出去 = 誤報給消防單位
                    if (Did(OpsActions.FirePriority) || Did(OpsActions.PriorityDownlink))
                    {
                        o.Voided = true;
                        o.ResultCode = "event.false_fire.reported";
                    }
                    else o.ResultCode = f >= 1 ? "event.false_fire.flagged" : "event.false_fire.ignored";
                    break;

                default:
                    if (f <= 0) { o.ResultCode = "event.opportunity.skipped"; break; }
                    double bonus = ev.SciencePerYear * 0.04 * e.Severity * f;
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
            r.SuggestionCode = Causality.Suggest(r.Causes, ev, data.Scenario(design.ScenarioId)?.FireAlert ?? false);
        }
    }
}
