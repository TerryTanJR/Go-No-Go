using System.Linq;
using System.Text;
using GoNoGo.Core;
using GoNoGo.Game;
public static class Sim
{
    /// <summary>在目前狀態的複本上走完：選預設設計 → 確定 → 組裝裝上 → 發射（全員 GO）。不送任何指令。</summary>
    public static string Run()
    {
        var d = GameSession.Data; var s = GameSession.Mission.S.Clone(); int me = GameSession.LocalPlayer;
        var sb = new StringBuilder(); int n = 0;
        bool Do(CommandType t, string a = "", int k = 0, int p = 0)
        {
            var why = SessionRules.Apply(s, d, Command.Of(t, p == 0 ? me : p, a, k)); n++;
            if (why != SessionRules.Ok) sb.AppendLine($"FAIL {t} {a} {k}: {why}");
            return why == SessionRules.Ok;
        }
        foreach (var id in GameData.DefaultDesign(s.Scenario(d), s.DifficultyId).AllPartIds())
        {
            var part = d.Part(id);
            if (part == null || part.Category == PartCategory.GroundStation || s.Bench.AllPartIds().Contains(id)) continue;
            Do(CommandType.SelectPart, id, 1);
        }
        var ev = Evaluator.EvaluateDesign(s.Bench, d, s.DifficultyId, true);
        sb.AppendLine("gaps=" + string.Join(",", SessionRules.DesignGaps(s, d)));
        if (!Do(CommandType.Confirm, "", 1)) return sb.ToString();
        var todo = SessionRules.NotInstalled(s, d).ToList();
        sb.AppendLine("to build=" + string.Join(",", todo) + " carry=" + SessionRules.CarryCapacity(s));
        foreach (var id in todo)
        {
            var recipe = d.Recipe(d.Part(id));
            if (recipe == null) { sb.AppendLine("no recipe " + id); continue; }
            foreach (var c in recipe.Components)
            {
                Do(CommandType.CompPick, c.Id);
                if (s.CompsOf(me).Count >= SessionRules.CarryCapacity(s) || c == recipe.Components[recipe.Components.Count - 1])
                    Do(CommandType.BenchPut, "", 0);
            }
            foreach (var c in recipe.Components) { Do(CommandType.BenchTake, c.Id, 0); Do(CommandType.CompFit, "", 0); }
            Do(CommandType.Pick, id); Do(CommandType.Install);
        }
        sb.AppendLine("left=" + string.Join(",", SessionRules.NotInstalled(s, d)));
        Do(CommandType.Launch);
        foreach (var p in s.Players) if (s.Poll != null) Do(CommandType.Vote, "", 1, p.Id);
        sb.AppendLine($"cmds={n} phase={s.Phase} reached={s.LastLaunch?.Reached} outcome={s.LastLaunch?.Outcome} reason={s.LastLaunch?.ReasonCode}");
        return sb.ToString();
    }
}
