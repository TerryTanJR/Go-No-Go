using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;

namespace GoNoGo.UI
{
    /// <summary>
    /// 學員難度的一般局（不是新手訓練）：左上角的「下一步」。不照固定順序打勾，而是看遊戲現在的狀態判斷該做什麼：
    /// 每一類選齊零件 → 找線索把選的零件的數值填齊 → 儀表板、任務需求有問題就改 → 確定設計 → 到架子拿小零件 → 組裝台組好 → 裝上衛星 → 發射；
    /// 營運時是看營運席 → 結束回合。不分組，每個人看到的下一步依自己手上的東西而定。
    /// 字串在 strings.json 的 ui.guide.*。
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
            int me = GameSession.LocalPlayer;

            if (s.Phase == SessionPhase.Ops) return Ops(m);
            if (s.Phase != SessionPhase.Design) return null;
            if (s.Poll != null) return Make("vote");
            if (s.DesignLocked(data)) return null;

            if (!s.Confirmed && !s.IsSandbox(data))
            {
                // 選件：開局一個零件都沒勾，先每一類選齊；再把選的零件的數值找齊（儀表板才判斷得出來），看有沒有紅字、需求有沒有達成，最後確定
                int missing = Blanks.Missing(data, s, s.Bench, PartFields.All);
                var gaps = SessionRules.DesignGaps(s, data);
                if (gaps.Count > 0) return Make("pick", "BigScreen", ("list", List(gaps.Select(c => Loc.T("ui.cat." + c)).ToList())));
                if (missing > 0) return Make("values", "clue", ("n", missing));
                var over = Over(m);
                if (over.Count > 0) return Make("fix", "BigScreen", ("list", List(over.Select(id => Loc.T("res." + id)).ToList())));
                var unmet = Unmet(m, data);
                if (unmet.Count > 0) return Make("fix_goals", "BigScreen", ("list", List(unmet.Select(t => Loc.T("ui.prog.type." + t)).ToList())));
                return Make("confirm", "BigScreen");
            }

            // 組裝：手上拿著什麼，就接著做什麼
            if (s.HeldBy(me) != null) return Make("install", "IntegrationStand");
            for (int i = 0; i < s.Benches.Count; i++)
                if (s.Benches[i].Job != null && s.Benches[i].Job.Done && s.CompsOf(me).Count == 0) return Make("take", "AssemblyBench" + i);
            int active = s.Benches.FindIndex(b => b.Job != null && !b.Job.Done);
            if (s.CompsOf(me).Count > 0)
                return active >= 0 ? Make("fit", "AssemblyBench" + active) : Make("put", "AssemblyBench" + FreeBench(s));
            if (active >= 0) return Make("fit", "AssemblyBench" + active);
            var todo = SessionRules.NotInstalled(s, data).Where(id => SessionRules.NeedsAssembly(s, data, id)).ToList();
            if (todo.Count > 0)
            {
                var p = data.Part(todo[0]);
                var comps = data.Recipe(p).Components.OrderBy(c => c.Id, System.StringComparer.Ordinal).Select(c => Loc.T(c.Name)).ToList();
                return Make("fetch", "CompShelf", ("code", p.Code), ("list", string.Join(Loc.IsZh ? "、" : ", ", comps)), ("n", todo.Count));
            }
            // 和 SessionRules.OverBudget 同一個判斷，用 MissionSession 這一輪已經算好的評估
            if (m.BenchEval[ResourceId.Budget].Margin < -1e-9) return Make("budget", "BigScreen");
            return Make("launch", "LaunchButton");
        }

        /// <summary>左側空著（或東西最少）的那一座組裝台。</summary>
        static int FreeBench(SessionState s)
        {
            int best = 0;
            for (int i = 1; i < s.Benches.Count; i++)
                if (s.Benches[i].Job == null && (s.Benches[best].Job != null || s.Benches[i].Left.Count > s.Benches[best].Left.Count)) best = i;
            return best;
        }

        static Step Ops(MissionSession m)
        {
            var s = m.S;
            if (s.TurnResolved) return Make("ops_next", "Screen_ops_desk");
            var e = m.CurrentEvent;   // 有快取（SessionRules.CurrentEvent 每次都重跑整趟任務模擬）
            bool acted = e != null && s.Responses.TryGetValue(e.Id, out var r) && r.Actions.Count > 0;
            return acted ? Make("ops_end", "Screen_ops_desk") : Make("ops_act", "Screen_ops-power");
        }

        /// <summary>儀表板上超出的資源（紅燈），用小寫的 ResourceId（"data"、"budget"…）。用到沒填數值的不算（還判斷不出來）。</summary>
        static List<string> Over(MissionSession m)
        {
            var list = new List<string>();
            foreach (var kv in m.Dashboard.Resources)
            {
                if (kv.Value.Status != ResourceStatus.Over) continue;
                string id = kv.Key.ToString().ToLowerInvariant();
                if (Blanks.Missing(m.Data, m.S, m.S.Bench, Blanks.FieldsFor(id)) > 0 && id != "budget") continue;
                list.Add(id);
            }
            return list;
        }

        /// <summary>判斷得出來、但還沒達成的需求種類（同一種只列一次）。</summary>
        static List<string> Unmet(MissionSession m, GameData data)
        {
            var list = new List<string>();
            foreach (var a in Departments.Active(m.Scenario, m.Difficulty))
            {
                var req = a.Requirement;
                if (list.Contains(req.Type) || !Blanks.CanJudge(data, m.S, m.S.Bench, req)) continue;
                if (!Requirements.IsMetPreLaunch(req, m.Dashboard, m.S.Bench, m.PlayerData, m.Difficulty)) list.Add(req.Type);
            }
            return list;
        }

        static string List(List<string> items) => string.Join(Loc.IsZh ? "、" : ", ", items.Take(3));

        static Step Make(string key, string target = "", params (string, object)[] args) =>
            new Step(key, Loc.T("ui.guide." + key, args), target);
    }
}
