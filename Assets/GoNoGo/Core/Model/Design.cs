using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GoNoGo.Core
{
    /// <summary>玩家的太空船設計。只存選擇的 id，數值一律從 GameData 查。</summary>
    public sealed class Design
    {
        public string ScenarioId { get; set; } = "";
        public List<string> Instruments { get; set; } = new List<string>();
        /// <summary>單選類別 → 零件 id（見 PartCategory.Single）。</summary>
        public Dictionary<string, string> Parts { get; set; } = new Dictionary<string, string>();
        public string OrbitId { get; set; } = "";
        public string LauncherId { get; set; } = "";
        public int MissionYears { get; set; } = 3;
        /// <summary>
        /// 採購的額外開銷（M$）：買了沒裝上去的零件、補單的急件費。由 SessionRules 依採購紀錄算好放進來，
        /// 評估時加進預算。單純比較設計（測試、修正建議）時是 0。
        /// </summary>
        public double ProcurementMusd { get; set; }
        /// <summary>補單讓時程延後的月數。</summary>
        public double ProcurementMonths { get; set; }

        public string Get(string category) =>
            Parts != null && Parts.TryGetValue(category, out var id) ? id : null;

        public Design Clone() => new Design
        {
            ScenarioId = ScenarioId,
            Instruments = new List<string>(Instruments ?? new List<string>()),
            Parts = new Dictionary<string, string>(Parts ?? new Dictionary<string, string>()),
            OrbitId = OrbitId,
            LauncherId = LauncherId,
            MissionYears = MissionYears,
            ProcurementMusd = ProcurementMusd,
            ProcurementMonths = ProcurementMonths,
        };

        /// <summary>所有選到的零件 id（儀器 + 各單選類別），依固定順序。</summary>
        public IEnumerable<string> AllPartIds()
        {
            foreach (var i in Instruments ?? Enumerable.Empty<string>()) yield return i;
            foreach (var c in PartCategory.Single)
            {
                var id = Get(c);
                if (!string.IsNullOrEmpty(id)) yield return id;
            }
        }

        /// <summary>設計的穩定雜湊，用來決定模擬種子（同設計 + 同種子 = 同結果）。</summary>
        public int StableHash()
        {
            unchecked
            {
                int h = 17;
                void Mix(string s)
                {
                    if (s == null) { h = h * 31; return; }
                    foreach (char ch in s) h = h * 31 + ch;
                    h = h * 31 + 7;
                }
                Mix(ScenarioId);
                foreach (var i in (Instruments ?? new List<string>()).OrderBy(x => x, System.StringComparer.Ordinal)) Mix(i);
                foreach (var c in PartCategory.Single) Mix(Get(c));
                Mix(OrbitId);
                Mix(LauncherId);
                h = h * 31 + MissionYears;
                return h & 0x7fffffff;
            }
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None, GameDataLoader.Settings);
    }
}
