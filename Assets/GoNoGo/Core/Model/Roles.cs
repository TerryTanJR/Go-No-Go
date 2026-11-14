using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    public enum Role { Director, Engineer, Scientist, Comms }

    public enum Vote { Go, NoGo }

    public static class Roles
    {
        public static readonly Role[] All = { Role.Director, Role.Engineer, Role.Scientist, Role.Comms };

        public static string Key(Role r) => r switch
        {
            Role.Director => "director",
            Role.Engineer => "engineer",
            Role.Scientist => "scientist",
            Role.Comms => "comms",
            _ => "director",
        };

        /// <summary>每個角色負責的資源（面板與點名的依據）。</summary>
        public static ResourceId[] Domain(Role r) => r switch
        {
            Role.Director => new[] { ResourceId.Budget, ResourceId.Time },
            Role.Engineer => new[] { ResourceId.Mass, ResourceId.Power },
            Role.Scientist => new ResourceId[0],
            Role.Comms => new[] { ResourceId.Data, ResourceId.Comms },
            _ => new ResourceId[0],
        };

        /// <summary>每個角色可以操作的零件類別。</summary>
        public static string[] Categories(Role r) => r switch
        {
            Role.Director => new[] { "launcher", "years" },
            Role.Engineer => new[] { PartCategory.Solar, PartCategory.Battery, PartCategory.Adcs, PartCategory.Propulsion, PartCategory.Shielding },
            Role.Scientist => new[] { PartCategory.Instrument },
            Role.Comms => new[] { "orbit", PartCategory.Antenna, PartCategory.GroundStation, PartCategory.Computer },
            _ => new string[0],
        };

        /// <summary>開局每個角色抽一張私有目標（單人模式顯示成「部門要求」）。</summary>
        public static Dictionary<Role, RequirementDef> DrawRequirements(ScenarioDef s, int seed)
        {
            var rng = new Random(seed ^ 0x5bd1e995);
            var res = new Dictionary<Role, RequirementDef>();
            foreach (var r in All)
            {
                if (s.Requirements.TryGetValue(Key(r), out var list) && list.Count > 0)
                    res[r] = list[rng.Next(list.Count)];
            }
            return res;
        }
    }

    public sealed class RoleVote
    {
        public Role Role { get; set; }
        public Vote Vote { get; set; }
        /// <summary>NO-GO 的理由（多語鍵），GO 時為空。</summary>
        public string ReasonCode { get; set; } = "";
        public bool RequirementMet { get; set; }
        public RequirementDef Requirement { get; set; }
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
                    return s != null && Scoring.ScienceScore(expected, s) >= req.Value;
                case "instrument_required": return d.Instruments.Contains(req.PartId);
                case "data_return": return ev.Data.ReturnFraction >= req.Value;
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
                default: return false;
            }
        }
    }

    /// <summary>Go/No-Go 點名：單人模式由系統依各角色的門檻自動點名。</summary>
    public static class RollCall
    {
        public static List<RoleVote> Run(Evaluation ev, Design d, GameData data, DifficultyDef diff,
            IDictionary<Role, RequirementDef> reqs)
        {
            var votes = new List<RoleVote>();
            foreach (var role in Roles.All)
            {
                var v = new RoleVote { Role = role, Vote = Vote.Go };
                reqs.TryGetValue(role, out var req);
                v.Requirement = req;
                v.RequirementMet = req == null || Requirements.IsMetPreLaunch(req, ev, d, data, diff);

                var over = Roles.Domain(role).Where(id => ev[id].Status == ResourceStatus.Over).ToList();
                if (over.Count > 0)
                {
                    v.Vote = Vote.NoGo;
                    v.ReasonCode = "nogo." + over[0].ToString().ToLowerInvariant();
                }
                else if (role == Role.Scientist && (d.Instruments.Count == 0 || ev.SciencePerYear <= 0))
                {
                    v.Vote = Vote.NoGo;
                    v.ReasonCode = "nogo.science";
                }
                else if (role == Role.Engineer && ev.Lifetime.Years < 1.0)
                {
                    v.Vote = Vote.NoGo;
                    v.ReasonCode = "nogo.lifetime";
                }
                else if (role == Role.Engineer && !ev.Mass.OrbitReachable)
                {
                    v.Vote = Vote.NoGo;
                    v.ReasonCode = "nogo.mass";
                }
                votes.Add(v);
            }
            return votes;
        }

        public static bool AllGo(IEnumerable<RoleVote> votes) => votes.All(v => v.Vote == Vote.Go);
    }
}
