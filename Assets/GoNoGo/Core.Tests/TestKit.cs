using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>測試共用的小工具。</summary>
    public static class TestKit
    {
        /// <summary>
        /// 把這一關（這個難度）的預設設計一個一個勾起來。一般關卡開局什麼零件都沒勾（玩家自己選），
        /// 大部分測試要從「已經選好一套」開始，就先呼叫這個。用的是玩家勾選的同一個指令。
        /// </summary>
        public static SessionState TickDefaults(this SessionState s, GameData data, int player = 1)
        {
            var d = GameData.DefaultDesign(s.Scenario(data), s.DifficultyId);
            foreach (var id in d.AllPartIds())
            {
                var part = data.Part(id);
                if (part == null || part.Category == PartCategory.GroundStation) continue;
                if (s.Bench.AllPartIds().Contains(id)) continue;
                var why = SessionRules.Apply(s, data, Command.Of(CommandType.SelectPart, player, id, 1));
                Assert.That(why, Is.EqualTo(SessionRules.Ok), "勾選 " + id);
            }
            return s;
        }

        /// <summary>
        /// 直接把預設設計放到大屏幕上（不經過指令、不檢查這個難度能不能選）：
        /// 只需要「有一套設計」來算儀表板、挖空的測試用。
        /// </summary>
        public static SessionState PresetDefaults(this SessionState s, GameData data)
        {
            var d = GameData.DefaultDesign(s.Scenario(data), s.DifficultyId);
            s.Bench.Instruments = d.Instruments.ToList();
            foreach (var kv in d.Parts) s.Bench.Parts[kv.Key] = kv.Value;
            return s;
        }
    }
}
