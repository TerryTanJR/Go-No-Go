using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>中控室電話可以撥的五個處室。每個處室提出自己的需求，有些彼此衝突。</summary>
    public enum Department { MissionLead, BudgetOffice, Science, Engineering, Comms }

    /// <summary>一條這一局有效的處室需求。</summary>
    public sealed class ActiveRequirement
    {
        public Department Department { get; set; }
        public RequirementDef Requirement { get; set; }
    }

    public static class Departments
    {
        public static readonly Department[] All =
            { Department.MissionLead, Department.BudgetOffice, Department.Science, Department.Engineering, Department.Comms };

        public static string Key(Department d) => d switch
        {
            Department.MissionLead => "mission_lead",
            Department.BudgetOffice => "budget_office",
            Department.Science => "science",
            Department.Engineering => "engineering",
            Department.Comms => "comms",
            _ => "mission_lead",
        };

        public static Department? Parse(string key)
        {
            foreach (var d in All) if (Key(d) == key) return d;
            return null;
        }

        /// <summary>這個情境、這個難度下有效的全部需求（衝突需求依難度開放）。順序固定：依處室、再依資料順序。</summary>
        public static List<ActiveRequirement> Active(ScenarioDef s, DifficultyDef diff)
        {
            var list = new List<ActiveRequirement>();
            if (s == null) return list;
            int conflicts = diff?.RequirementConflicts ?? 0;
            foreach (var d in All)
            {
                if (!s.Requirements.TryGetValue(Key(d), out var reqs)) continue;
                foreach (var r in reqs.Where(r => r.ConflictGroup <= conflicts))
                    list.Add(new ActiveRequirement { Department = d, Requirement = r });
            }
            return list;
        }
    }

    /// <summary>需求是否達成的判斷（發射前用估計值，任務後用實際結果）。</summary>
    public static class Requirements
    {
        public static bool IsMetPreLaunch(RequirementDef req, Evaluation ev, Design d, GameData data, DifficultyDef diff)
        {
            var s = data.Scenario(d.ScenarioId);
            switch (req.Type)
            {
                case "budget_reserve": return ev[ResourceId.Budget].Margin >= req.Value;
                case "lifetime_target": return ev.Lifetime.Years >= d.MissionYears && d.MissionYears >= (s?.TargetYears ?? 0);
                case "mass_power_margin":
                    double need = req.Value > 0 ? req.Value : diff.RequiredMargin;
                    return ev[ResourceId.Mass].Margin >= need && ev[ResourceId.Power].Margin >= need;
                case "science_threshold":
                    double expected = ev.SciencePerYear * Math.Min(d.MissionYears, ev.Lifetime.Years) * ev.Data.ReturnFraction;
                    return s != null && Scoring.ScienceScore(expected, s, d.MissionYears) >= req.Value;
                case "instrument_required": return d.Instruments.Contains(req.PartId);
                case "data_return": return ev.Data.ReturnFraction >= req.Value;
                case "instrument_any": return d.Instruments.Any(req.PartIds.Contains);
                case "orbit_required": return d.OrbitId == req.OrbitId;
                case "battery_eclipse": return ev.Power.ActualDod <= 1.0;
                case "latency": return ev.Fire.LatencyMin <= req.Value;
                default: return false;
            }
        }

        public static bool IsMetAfterMission(RequirementDef req, Evaluation ev, Design d, MissionResult r, GameData data,
            DifficultyDef diff, double scienceScore)
        {
            switch (req.Type)
            {
                case "budget_reserve": return ev[ResourceId.Budget].Margin >= req.Value;
                case "lifetime_target": return r.ReachedOrbit && r.ActualYears >= d.MissionYears - 1e-6 && d.MissionYears >= (data.Scenario(d.ScenarioId)?.TargetYears ?? 0);
                case "mass_power_margin": return IsMetPreLaunch(req, ev, d, data, diff);
                case "science_threshold": return r.ReachedOrbit && scienceScore >= req.Value;
                case "instrument_required": return d.Instruments.Contains(req.PartId);
                case "data_return": return r.ReachedOrbit && r.ReturnFraction >= req.Value;
                case "instrument_any":
                case "orbit_required": return IsMetPreLaunch(req, ev, d, data, diff);
                case "battery_eclipse": return r.ReachedOrbit && ev.Power.ActualDod <= 1.0;
                case "latency": return r.ReachedOrbit && ev.Fire.LatencyMin <= req.Value;
                default: return false;
            }
        }
    }
}
