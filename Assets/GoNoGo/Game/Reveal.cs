using System.Linq;
using GoNoGo.Core;

namespace GoNoGo.Game
{
    /// <summary>
    /// 只揭露現在用得到的資訊（docs/info_reveal.md）：營運才用的手冊章節到營運階段才開放，失敗原因看板第一次發射失敗才出現。
    /// 控制台（ConsoleTarget）、手冊的章節書籤、個人模式的平板共用這些規則。
    /// </summary>
    public static class Reveal
    {
        /// <summary>營運時才用得到的手冊章節：異常判讀、處置程序、太空天氣。</summary>
        static readonly string[] OpsChapters = { "diagnose", "procedures", "donki" };

        static bool Operating(SessionState s) => s.Phase == SessionPhase.Ops || s.Phase == SessionPhase.Report;

        /// <summary>失敗原因看板：有過失敗的發射才有東西可看。</summary>
        public static bool Failures => !GameSession.HasMission || GameSession.Mission.S.FailedLaunches > 0;

        public static bool Chapter(string ch) => ChapterLock(ch) == null;

        /// <summary>這一章還沒開放的原因（字串鍵）；已經開放 = null。</summary>
        public static string ChapterLock(string ch)
        {
            if (!GameSession.HasMission || !OpsChapters.Contains(ch)) return null;
            return Operating(GameSession.Mission.S) ? null : "ui.hint.locked_ops";
        }

        /// <summary>這個面板還沒揭露的原因（字串鍵）；已經揭露 = null。</summary>
        public static string Lock(string panel)
        {
            if (string.IsNullOrEmpty(panel) || !GameSession.HasMission) return null;
            if (panel == "failures") return Failures ? null : "ui.hint.locked_failures";
            if (panel.StartsWith("manual:")) return ChapterLock(panel.Substring(7));
            return null;
        }
    }
}
