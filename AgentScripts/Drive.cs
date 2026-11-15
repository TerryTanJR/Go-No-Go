using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GoNoGo.Core;
using GoNoGo.Game;
public static class Drive
{
    static StringBuilder _log;
    static string _rejected;

    /// <summary>送一個操作，等狀態版本變了（房主套用、廣播回來）或被拒絕。</summary>
    static async Task<bool> Do(CommandType t, string a = "", int n = 0)
    {
        var m = GameSession.Mission; int rev = m.S.Revision; _rejected = null;
        Action<string> onRej = code => _rejected = code;
        m.Rejected += onRej;
        try
        {
            GameSession.Send(t, a, n);
            var until = DateTime.Now.AddSeconds(5);
            while (GameSession.Mission.S.Revision == rev && _rejected == null && DateTime.Now < until) await Task.Delay(30);
        }
        finally { m.Rejected -= onRej; }
        if (_rejected != null) { _log.AppendLine($"REJECTED {t} {a} {n}: {_rejected}"); return false; }
        if (GameSession.Mission.S.Revision == rev) { _log.AppendLine($"TIMEOUT {t} {a} {n}"); return false; }
        return true;
    }

    public static async Task<string> Build()
    {
        _log = new StringBuilder(); var d = GameSession.Data; int me = GameSession.LocalPlayer; int sent = 0;
        SessionState s() => GameSession.Mission.S;
        foreach (var id in GameData.DefaultDesign(s().Scenario(d), s().DifficultyId).AllPartIds())
        {
            var part = d.Part(id);
            if (part == null || part.Category == PartCategory.GroundStation || s().Bench.AllPartIds().Contains(id)) continue;
            if (!await Do(CommandType.SelectPart, id, 1)) return _log.ToString(); sent++;
        }
        if (!s().Confirmed) { if (!await Do(CommandType.Confirm, "", 1)) return _log.ToString(); sent++; }
        _log.AppendLine("confirmed; to build=" + string.Join(",", SessionRules.NotInstalled(s(), d)));
        foreach (var id in SessionRules.NotInstalled(s(), d).ToList())
        {
            var recipe = d.Recipe(d.Part(id));
            foreach (var c in recipe.Components)
            {
                if (!await Do(CommandType.CompPick, c.Id)) return _log.ToString();
                if (s().CompsOf(me).Count >= SessionRules.CarryCapacity(s()) || c == recipe.Components[recipe.Components.Count - 1])
                    if (!await Do(CommandType.BenchPut, "", 0)) return _log.ToString();
            }
            foreach (var c in recipe.Components)
            {
                if (!await Do(CommandType.BenchTake, c.Id, 0)) return _log.ToString();
                if (!await Do(CommandType.CompFit, "", 0)) return _log.ToString();
            }
            if (!await Do(CommandType.Pick, id)) return _log.ToString();
            if (!await Do(CommandType.Install)) return _log.ToString();
            _log.AppendLine("installed " + id);
        }
        _log.AppendLine("left=" + string.Join(",", SessionRules.NotInstalled(s(), d)) + " phase=" + s().Phase);
        return _log.ToString();
    }
}
