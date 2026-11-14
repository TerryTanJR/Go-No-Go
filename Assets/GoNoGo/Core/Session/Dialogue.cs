using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>一條筆記：學員難度整理成發射條件清單，工程師難度記下對話原文。</summary>
    public sealed class NoteItem
    {
        public string Department { get; set; } = "";
        public string Question { get; set; } = "";
        public string Answer { get; set; } = "";
        /// <summary>這通電話說出的需求（清單模式用）。</summary>
        public List<RequirementDef> Requirements { get; set; } = new List<RequirementDef>();
    }

    /// <summary>電話對話：把處室的回答代入這一局的數字，接上這個難度有效的需求。</summary>
    public static class Dialogue
    {
        static string N(double v, string fmt = "0") => v.ToString(fmt, CultureInfo.InvariantCulture);

        /// <summary>這一題會說出的需求（這個處室、這個難度有效、種類符合）。</summary>
        public static List<RequirementDef> Revealed(GameData data, SessionState s, string department, DialogueLine line)
        {
            var dep = Departments.Parse(department);
            if (dep == null || line.Reveals.Count == 0) return new List<RequirementDef>();
            return Departments.Active(s.Scenario(data), s.Difficulty(data))
                .Where(a => a.Department == dep.Value && line.Reveals.Contains(a.Requirement.Type))
                .Select(a => a.Requirement).ToList();
        }

        public static string Answer(GameData data, SessionState s, string department, DialogueLine line, Evaluation reported, string lang)
        {
            var reqs = Revealed(data, s, department, line);
            bool none = line.Reveals.Count > 0 && reqs.Count == 0 && !string.IsNullOrEmpty(line.AnswerNone.Get(lang));
            var text = Fill(data, s, none ? line.AnswerNone.Get(lang) : line.Answer.Get(lang), reported, lang);
            if (!none && reqs.Count > 0)
                text += (lang == Lang.ZhTW ? "" : " ") + string.Join(lang == Lang.ZhTW ? "" : " ", reqs.Select(r => r.Text.Get(lang)));
            return text;
        }

        public static string Fill(GameData data, SessionState s, string text, Evaluation reported, string lang)
        {
            var sc = s.Scenario(data);
            var diff = s.Difficulty(data);
            if (sc == null) return text;
            double budget = GameData.Budget(sc);
            var reserve = Departments.Active(sc, diff).Select(a => a.Requirement).FirstOrDefault(r => r.Type == "budget_reserve");
            var map = new Dictionary<string, string>
            {
                { "briefing", sc.Briefing.Get(lang) },
                { "objective", data.Text("objective." + sc.Objective, lang) },
                { "budget", N(budget, "0.#") + " M$" },
                { "reserve_pct", N((reserve?.Value ?? 0) * 100) },
                { "years", N(sc.TargetYears) },
                { "deadline", N(sc.DeadlineMonths) },
                { "max_instruments", sc.MaxInstruments.ToString(CultureInfo.InvariantCulture) },
                { "margin_pct", N(diff.RequiredMargin * 100) },
                { "eclipse_min", N(reported?.Orbit?.EclipseMin ?? 0) },
                { "latency_limit", N(sc.LatencyLimitMin) },
            };
            foreach (var kv in map) text = text.Replace("{" + kv.Key + "}", kv.Value);
            return text;
        }

        /// <summary>筆記板的內容（依難度：清單、逐字稿或不記錄）。</summary>
        public static List<NoteItem> Notes(GameData data, SessionState s, Evaluation reported, string lang)
        {
            var list = new List<NoteItem>();
            if (s.Difficulty(data).NotesMode == "none") return list;
            foreach (var c in s.Calls)
            {
                var dep = data.Department(c.Department);
                var line = data.Lines(s.Scenario(data), dep).FirstOrDefault(l => l.Id == c.LineId);
                if (line == null) continue;
                list.Add(new NoteItem
                {
                    Department = dep.Id,
                    Question = line.Question.Get(lang),
                    Answer = Answer(data, s, dep.Id, line, reported, lang),
                    Requirements = Revealed(data, s, dep.Id, line),
                });
            }
            return list;
        }
    }
}
