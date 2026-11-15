using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>
    /// 大屏幕「任務需求」頁的取捨說明：把說明文字裡的參數換成這一局的數字。
    /// 參數只用關卡的設定和軌道（預算、年數、每圈地影…），不會用到零件被挖空的數值。
    /// </summary>
    public static class Tradeoffs
    {
        static string N(double v, string fmt = "0") => v.ToString(fmt, CultureInfo.InvariantCulture);

        /// <summary>這一局列出的取捨說明（已經代入數字）：標題、內文。</summary>
        public static List<(TradeoffNote note, string title, string text)> For(GameData data, SessionState s, Evaluation eval, string lang) =>
            data.TradeoffsFor(s.Scenario(data), s.Difficulty(data))
                .Select(t => (t, Fill(data, s, t.Title.Get(lang), eval, lang), Fill(data, s, t.Text.Get(lang), eval, lang))).ToList();

        public static string Fill(GameData data, SessionState s, string text, Evaluation eval, string lang)
        {
            var sc = s.Scenario(data);
            var diff = s.Difficulty(data);
            if (sc == null || string.IsNullOrEmpty(text)) return text;
            double budget = GameData.Budget(sc);
            var reserve = Departments.Active(sc, diff).Select(a => a.Requirement).FirstOrDefault(r => r.Type == "budget_reserve");
            var map = new Dictionary<string, string>
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
            foreach (var kv in map) text = text.Replace("{" + kv.Key + "}", kv.Value);
            return text;
        }
    }
}
