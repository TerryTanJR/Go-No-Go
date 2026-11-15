using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
public static class Peek
{
    public static string State()
    {
        if (!GameSession.HasMission) return "no mission";
        var s = GameSession.Mission.S; var d = GameSession.Data;
        return $"phase={s.Phase} scen={s.ScenarioId} diff={s.DifficultyId} tut={s.Tutorial} mp={s.Multiplayer} solo={s.Solo} host={GameSession.IsHost} me={GameSession.LocalPlayer}\n" +
            $"players={string.Join(",", s.Players.Select(p => p.Id + ":" + p.Look?.Name))}\n" +
            $"orbit={s.Bench.OrbitId} launcher={s.Bench.LauncherId} years={s.Bench.MissionYears}\n" +
            $"bench parts={string.Join(",", s.Bench.AllPartIds())}\n" +
            $"confirmed={s.Confirmed} built={string.Join(",", s.Built)} installed={string.Join(",", s.Installed)}\n" +
            $"filled={string.Join(",", s.Filled.Keys)}\n" +
            $"default={string.Join(",", GameData.DefaultDesign(s.Scenario(d), s.DifficultyId).AllPartIds())}";
    }
}
