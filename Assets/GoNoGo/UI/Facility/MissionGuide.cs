using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;

namespace GoNoGo.UI
{
    /// <summary>
    /// 學員難度的一般局（不是新手訓練）：左上角的「下一步」。不照固定順序打勾，而是看遊戲現在的狀態判斷該做什麼：
    /// 問需求 → 採購 → 拿零件裝上整合台 → 儀表板有問題就修 → 發射（點名：喊自己負責的項目）；營運時是看控制台 → 結束回合。
    /// 多人時依自己這一組（組裝區 / 中控室）給不同的下一步。字串在 strings.json 的 ui.guide.*。
    /// </summary>
    public static class MissionGuide
    {
        public readonly struct Step
        {
            public readonly string Key, Body, Target;
            public Step(string key, string body, string target = "") { Key = key; Body = body; Target = target; }
        }

        /// <summary>現在的下一步；null = 不顯示（發射、評價等不在設施裡的階段）。</summary>
        public static Step? Next()
        {
            if (!GameSession.HasMission) return null;
            var m = GameSession.Mission;
            var s = m.S;
            var data = GameSession.Data;
            var me = s.Player(GameSession.LocalPlayer);
            bool multi = s.Multiplayer && s.Players.Count > 1;
            bool asm = !multi || me?.Station == Station.Assembly;
            bool ctl = !multi || me?.Station == Station.Control;

            if (s.Phase == SessionPhase.Ops) return Ops(s, data);
            if (s.Phase != SessionPhase.Design) return null;

            // 點名中：輪到我喊的項目；都喊完了，飛行總監放行，其他人等
            if (s.Poll != null)
            {
                var mine = s.Poll.Pending(GameSession.LocalPlayer);
                if (mine.Count > 0) return Make("vote", "", ("list", PollText.Items(mine)));
                return s.Poll.CalledBy == GameSession.LocalPlayer && s.Poll.Complete() ? Make("poll_go") : Make("poll_wait");
            }

            // 中控室：每個處室至少問一題（每題都花時程，不用全部問）
            int depts = data.Departments.Count;
            int asked = s.Calls.Select(c => c.Department).Distinct().Count();
            if (ctl && asked < depts) return Make("call", "Screen_phone", ("n", depts - asked), ("m", Fmt.N(data.Constants.CallMonths, 1)));

            // 多人：組裝區送來的採購單在等中控室（任務目標與預算的控制台，場景物件叫 Screen_dashboard）
            if (s.Request != null)
                return ctl ? Make("review", "Screen_dashboard") : Make("wait_review");

            if (asm)
            {
                // 手上拿著零件 → 裝上去；買了還沒裝 → 去拿；什麼都還沒買 → 採購
                var held = s.HeldBy(GameSession.LocalPlayer);
                // 組裝工作台：組好了 → 拿起成品；正在組 → 到零件盤拿小零件回來裝；手上是一箱小零件 → 放上工作台
                var job = s.Workbench;
                if (job != null && job.Done && held == null) return Make("take", "AssemblyBench");
                if (job != null && !job.Done && held == null)
                    return Make("fit", s.HeldCompBy(GameSession.LocalPlayer) != null ? "AssemblyBench" : "CompRack");
                if (held != null && SessionRules.NeedsAssembly(s, data, held)) return Make("assemble", "AssemblyBench");
                if (held != null) return Make("install", "IntegrationStand");
                var bench = new HashSet<string>(s.Bench.AllPartIds());
                var last = s.Orders.LastOrDefault();
                if (last != null && last.Parts.Any(id => !bench.Contains(id))) return Make("pick", "code");
                if (s.Orders.Count == 0) return Make("procure", "ProcureConsole");
            }

            // 儀表板有超出的項目 → 看儀表板；只剩問到的需求沒達成 → 看任務目標（學員難度那裡列出每一條現在做到多少，
            // 科學成果、資料回傳這些在儀表板上沒有）
            // 再接一句「可以打給哪個處室問哪一題」：建議類的題目在這時候才派得上用場
            var over = Over(m);
            if (over.Count > 0)
            {
                string list = List(over.Select(id => Loc.T("res." + id)).ToList());
                var step = asm ? Make("fix", "BayDashboard", ("list", list)) : Make("fix_ctl", "", ("list", list));
                return WithAdvice(step, s, data, over);
            }
            var unmet = Unmet(m, data);
            if (unmet.Count > 0)
                return WithAdvice(Make("fix_goals", "Screen_dashboard", ("list", List(unmet.Select(t => Loc.T("ui.prog.type." + t)).ToList()))), s, data, unmet);

            return ctl ? Make("launch", "LaunchButton") : Make("ready_asm");
        }

        static Step Ops(SessionState s, GameData data)
        {
            if (s.TurnResolved) return Make("ops_next", "Screen_ops_desk");
            var e = SessionRules.CurrentEvent(s, data);
            bool acted = e != null && s.Responses.TryGetValue(e.Id, out var r) && r.Actions.Count > 0;
            return acted ? Make("ops_end", "Screen_ops_desk") : Make("ops_act", "Screen_ops-power");
        }

        /// <summary>儀表板上超出的資源（紅燈），用小寫的 ResourceId（"data"、"budget"…）。</summary>
        static List<string> Over(MissionSession m) =>
            m.Dashboard.Resources.Where(kv => kv.Value.Status == ResourceStatus.Over)
                .Select(kv => kv.Key.ToString().ToLowerInvariant()).ToList();

        /// <summary>問到但還沒達成的需求種類（同一種只列一次）。</summary>
        static List<string> Unmet(MissionSession m, GameData data)
        {
            var list = new List<string>();
            var heard = Dialogue.Heard(data, m.S);
            foreach (var a in Departments.Active(m.Scenario, m.Difficulty))
            {
                var req = a.Requirement;
                if (!heard.Contains(req.Id) || list.Contains(req.Type)) continue;
                if (!Requirements.IsMetPreLaunch(req, m.Dashboard, m.S.Reported, data, m.Difficulty)) list.Add(req.Type);
            }
            return list;
        }

        /// <summary>
        /// 出問題時可以問的建議題：超出的資源（小寫 ResourceId）或沒達成的需求種類 → 處室與題目 id。
        /// 質量、電力沒有對應的建議題（要看手冊的規格），不列。
        /// </summary>
        static readonly Dictionary<string, (string dept, string line)> Advice = new Dictionary<string, (string, string)>
        {
            ["data"] = ("comms", "storage"),
            ["comms"] = ("comms", "stations"),
            ["budget"] = ("budget_office", "costs"),
            ["time"] = ("mission_lead", "deadline"),
            ["budget_reserve"] = ("budget_office", "costs"),
            ["battery_eclipse"] = ("engineering", "battery"),
            ["lifetime_target"] = ("engineering", "lifetime"),
            ["data_return"] = ("comms", "stations"),
            ["latency"] = ("comms", "stations"),
            ["science_threshold"] = ("science", "orbit"),
            ["orbit_required"] = ("science", "orbit"),
            ["instrument_required"] = ("science", "instrument"),
            ["instrument_any"] = ("science", "instrument"),
        };

        /// <summary>第一個有建議題、而且還沒問過的問題，接在說明後面（這一關沒有那一題、或難度沒開放就略過）。</summary>
        static Step WithAdvice(Step step, SessionState s, GameData data, List<string> keys)
        {
            foreach (var k in keys)
            {
                if (!Advice.TryGetValue(k, out var a)) continue;
                var dep = data.Department(a.dept);
                var line = data.Lines(s.Scenario(data), dep).FirstOrDefault(l => l.Id == a.line);
                if (line == null || !GameData.LineOpen(line, s.Difficulty(data))) continue;
                if (s.Calls.Any(c => c.Department == a.dept && c.LineId == a.line)) continue;
                return new Step(step.Key, step.Body + Loc.T("ui.guide.ask_hint", ("dept", Loc.T(dep.Name)), ("q", Loc.T(line.Question))), step.Target);
            }
            return step;
        }

        static string List(List<string> items) => string.Join(Loc.IsZh ? "、" : ", ", items.Take(3));

        static Step Make(string key, string target = "", params (string, object)[] args) =>
            new Step(key, Loc.T("ui.guide." + key, args), target);
    }
}
