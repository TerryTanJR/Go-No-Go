using System.Linq;
using System.Threading.Tasks;
using GoNoGo.Core;
using GoNoGo.Game;
public static class Go
{
    public static async Task<string> Launch()
    {
        var rejected = "";
        System.Action<string> onRej = c => rejected = c;
        GameSession.Mission.Rejected += onRej;
        try
        {
            if (GameSession.Mission.S.Poll == null)
            {
                GameSession.Send(CommandType.Launch);
                for (int i = 0; i < 100 && GameSession.Mission.S.Poll == null && rejected == ""; i++) await Task.Delay(30);
                if (rejected != "") return "launch rejected: " + rejected;
            }
            GameSession.Send(CommandType.Vote, "", 1);
            await Task.Delay(1500);
            var s = GameSession.Mission.S;
            var votes = s.Poll == null ? "(no poll)" : string.Join(",", s.Players.Select(p => p.Look?.Name + "=" + (s.Poll.Votes.TryGetValue(p.Id, out var v) ? (v ? "GO" : "NO-GO") : "?")));
            return $"rejected={rejected} phase={s.Phase} votes={votes}";
        }
        finally { GameSession.Mission.Rejected -= onRej; }
    }
}
