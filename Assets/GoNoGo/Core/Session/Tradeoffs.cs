using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>
    /// 設計主螢幕「任務需求」頁的取捨說明：把說明文字裡的參數換成這一局的數字。
    /// 參數只用關卡的設定和軌道（預算、年數、每圈地影…），不會用到零件被挖空的數值。
    /// </summary>
    public static class Tradeoffs
    {
        static string N(double v, string fmt = "0") => v.ToString(fmt, CultureInfo.InvariantCulture);

        /// <summary>這一局列出的取捨說明（已經代入數字）：標題、內文。</summary>
        public static List<(TradeoffNote note, string title, string text)> For(GameData data, SessionState s, Evaluation eval, string lang)
        {
            var sc = s.Scenario(data);
            var diff = s.Difficulty(data);
            // 參數對照表整頁共用：第一段要代入的文字出現時才建，之後每一則都用同一份
            Dictionary<string, string> map = null;
            string Apply(string text)
            {
                if (sc == null || string.IsNullOrEmpty(text)) return text;
                return Replace(text, map ??= Params(data, sc, diff, eval, lang));
            }
            return data.TradeoffsFor(sc, diff).Select(t => (t, Apply(t.Title.Get(lang)), Apply(t.Text.Get(lang)))).ToList();
        }

        public static string Fill(GameData data, SessionState s, string text, Evaluation eval, string lang)
        {
            var sc = s.Scenario(data);
            var diff = s.Difficulty(data);
            if (sc == null || string.IsNullOrEmpty(text)) return text;
            return Replace(text, Params(data, sc, diff, eval, lang));
        }

        /// <summary>參數名稱 → 這一局的數字（已經格式化）。</summary>
        static Dictionary<string, string> Params(GameData data, ScenarioDef sc, DifficultyDef diff, Evaluation eval, string lang)
        {
            double budget = GameData.Budget(sc);
            var reserve = Departments.Active(sc, diff).Select(a => a.Requirement).FirstOrDefault(r => r.Type == "budget_reserve");
            return new Dictionary<string, string>
            {
                { "briefing", sc.Briefing.Get(lang) },
                { "objective", data.Text("objective." + sc.Objective, lang) },
                { "budget", budget >= ScenarioDef.NoLimit ? "∞" : "$" + N(budget, "0.#") + "M" },
                { "reserve_pct", N((reserve?.Value ?? 0) * 100) },
                { "years", N(sc.TargetYears) },
                { "max_instruments", sc.MaxInstruments.ToString(CultureInfo.InvariantCulture) },
                { "margin_pct", N(diff.RequiredMargin * 100) },
                { "eclipse_min", N(eval?.Orbit?.EclipseMin ?? 0) },
                { "latency_limit", N(sc.LatencyLimitMin) },
            };
        }

        static string Replace(string text, Dictionary<string, string> map)
        {
            foreach (var kv in map) text = text.Replace("{" + kv.Key + "}", kv.Value);
            return text;
        }
    }
}
