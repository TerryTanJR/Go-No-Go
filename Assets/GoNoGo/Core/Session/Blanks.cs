using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>零件會被挖空的六種數值（欄位名和 parts.json 一樣）。價格、可靠度這些不挖空。</summary>
    public static class PartFields
    {
        public const string MassKg = "mass_kg";
        public const string PowerW = "power_w";
        public const string CapacityWh = "capacity_wh";
        public const string ResolutionM = "resolution_m";
        public const string DataGbPerDay = "data_gb_per_day";
        public const string DownlinkMbps = "downlink_mbps";

        public static readonly string[] All = { MassKg, PowerW, CapacityWh, ResolutionM, DataGbPerDay, DownlinkMbps };

        public static double Get(PartDef p, string field)
        {
            if (p == null) return 0;
            switch (field)
            {
                case MassKg: return p.MassKg;
                case PowerW: return p.PowerW;
                case CapacityWh: return p.CapacityWh;
                case ResolutionM: return p.ResolutionM;
                case DataGbPerDay: return p.DataGbPerDay;
                case DownlinkMbps: return p.DownlinkMbps;
                default: return 0;
            }
        }

        public static void Set(PartDef p, string field, double v)
        {
            switch (field)
            {
                case MassKg: p.MassKg = v; break;
                case PowerW: p.PowerW = v; break;
                case CapacityWh: p.CapacityWh = v; break;
                case ResolutionM: p.ResolutionM = v; break;
                case DataGbPerDay: p.DataGbPerDay = v; break;
                case DownlinkMbps: p.DownlinkMbps = v; break;
            }
        }

        /// <summary>單位（畫面和線索上寫的）。</summary>
        public static string Unit(string field)
        {
            switch (field)
            {
                case MassKg: return "kg";
                case PowerW: return "W";
                case CapacityWh: return "Wh";
                case ResolutionM: return "m";
                case DataGbPerDay: return "GB/day";
                case DownlinkMbps: return "Mbps";
                default: return "";
            }
        }
    }

    /// <summary>玩家在設計主螢幕上填的一個數值，和是誰填的（格子旁邊顯示）。</summary>
    public sealed class FilledValue
    {
        public double V { get; set; }
        public int By { get; set; }
    }

    /// <summary>
    /// 數值挖空：開局時設計主螢幕上一部分零件數值是空的，玩家找到線索後自己填回去。
    /// 設計主螢幕的儀表板只用「看得到的數值」算（沒挖空的真實數值 + 玩家填的數值）：填錯不擋、不提示，照玩家填的算；
    /// 發射、營運、失敗原因和分數永遠用 parts.json 的真實數值，不受玩家輸入影響。
    /// 歷史關卡（第一次發射前設計鎖住）和沙盒（沒有需求）不挖空。
    /// </summary>
    public static class Blanks
    {
        /// <summary>玩家填的數值上限（擋掉打錯多按幾個零的情況；畫面上的格子也放不下更長的數字）。</summary>
        public const double MaxValue = 9999999;

        public static string Key(string partId, string field) => partId + "/" + field;

        /// <summary>這一局有沒有挖空。</summary>
        public static bool Enabled(GameData data, SessionState s)
        {
            var sc = s.Scenario(data);
            return sc != null && !sc.Sandbox && sc.Historical == null;
        }

        /// <summary>這一局的挖空比例：單人比多人少，但仍照難度越高越多。</summary>
        public static double Ratio(GameData data, SessionState s)
        {
            var diff = s.Difficulty(data);
            return s.Solo ? diff.BlankRatioSolo : diff.BlankRatio;
        }

        /// <summary>
        /// 0–1 的固定雜湊：同一格每一局、每台電腦都一樣。比例越高，挖空的格子只會多不會換一批
        /// （學員挖空的格子，工程師和任務主管也一定挖空）。
        /// </summary>
        public static double Hash01(string key)
        {
            uint h = 2166136261;
            foreach (char ch in key) h = unchecked((h ^ ch) * 16777619);
            h ^= h >> 15;
            h = unchecked(h * 2246822519);
            h ^= h >> 13;
            return (h & 0xFFFFFF) / (double)0x1000000;
        }

        /// <summary>這一格開局時是不是空的。只有這一關列得出來的零件、而且真實數值大於 0 的欄位才會被挖空。</summary>
        public static bool IsBlank(GameData data, SessionState s, string partId, string field)
        {
            if (!Enabled(data, s)) return false;
            var p = data.Part(partId);
            // 真實數值大於 0 的一定是六個欄位之一（PartFields.Get 對其他欄位名回傳 0）
            if (p == null || p.Category == PartCategory.GroundStation || PartFields.Get(p, field) <= 0) return false;
            return Picked(s.Difficulty(data), s.Solo, partId, field);
        }

        /// <summary>難度的設定有沒有挑中這一格：有指定格子就照指定，否則雜湊值小於挖空比例的就挖空。</summary>
        static bool Picked(DifficultyDef diff, bool solo, string partId, string field)
        {
            string key = Key(partId, field);
            if (diff.BlankCells.Count > 0) return diff.BlankCells.Contains(key);
            return Hash01(key) < (solo ? diff.BlankRatioSolo : diff.BlankRatio);
        }

        /// <summary>這一局所有挖空的格子（零件 id、欄位），照零件資料和欄位的順序。</summary>
        public static List<(string part, string field)> Cells(GameData data, SessionState s)
        {
            var list = new List<(string, string)>();
            if (!Enabled(data, s)) return list;
            // 和 IsBlank 同一套判斷；清單裡的零件都查得到、也沒有地面站，難度只查一次
            DifficultyDef diff = null;
            foreach (var p in SessionRules.Catalog(data, s))
                foreach (var f in PartFields.All)
                {
                    if (PartFields.Get(p, f) <= 0) continue;
                    if (Picked(diff ??= s.Difficulty(data), s.Solo, p.Id, f)) list.Add((p.Id, f));
                }
            return list;
        }

        /// <summary>玩家填的（沒填 = null）。</summary>
        public static FilledValue Filled(SessionState s, string partId, string field) =>
            s.Filled.TryGetValue(Key(partId, field), out var v) ? v : null;

        /// <summary>這一格現在看不看得到數值（沒挖空，或挖空了但已經有人填）。</summary>
        public static bool Known(GameData data, SessionState s, string partId, string field) =>
            !IsBlank(data, s, partId, field) || Filled(s, partId, field) != null;

        /// <summary>設計主螢幕上這一格顯示的數值：沒挖空 = 真實數值；挖空已填 = 玩家填的；挖空沒填 = null。</summary>
        public static double? Shown(GameData data, SessionState s, string partId, string field)
        {
            if (!IsBlank(data, s, partId, field)) return PartFields.Get(data.Part(partId), field);
            return Filled(s, partId, field)?.V;
        }

        /// <summary>
        /// 「玩家看到的」資料：挖空的格子換成玩家填的數值（沒填的當 0，不算進加總）。沒有挖空的局直接回傳原本的資料。
        /// 只給設計主螢幕的儀表板用；發射和評分一律用原本的資料。
        /// </summary>
        public static GameData PlayerData(GameData data, SessionState s)
        {
            var cells = Cells(data, s);
            if (cells.Count == 0) return data;
            var clones = new Dictionary<string, PartDef>();
            foreach (var (part, field) in cells)
            {
                if (!clones.TryGetValue(part, out var c)) clones[part] = c = data.Part(part).Clone();
                PartFields.Set(c, field, Filled(s, part, field)?.V ?? 0);
            }
            return data.WithParts(data.Parts.Select(p => clones.TryGetValue(p.Id, out var c) ? c : p).ToList());
        }

        /// <summary>這個設計選的零件裡，這幾種欄位還有幾格沒填。0 = 這些欄位的數值都看得到。</summary>
        public static int Missing(GameData data, SessionState s, Design design, params string[] fields)
        {
            if (design == null || !Enabled(data, s)) return 0;
            int n = 0;
            foreach (var id in design.AllPartIds()) n += Unfilled(data, s, id, fields);
            return n;
        }

        /// <summary>這個零件還有幾格沒填。</summary>
        public static int MissingOf(GameData data, SessionState s, string partId) =>
            Enabled(data, s) ? Unfilled(data, s, partId, PartFields.All) : 0;

        /// <summary>這個零件的這幾種欄位裡，挖空了還沒填的格數（有挖空的局才呼叫）。零件只查一次。</summary>
        static int Unfilled(GameData data, SessionState s, string partId, string[] fields)
        {
            var p = data.Part(partId);
            if (p == null || p.Category == PartCategory.GroundStation) return 0;
            int n = 0;
            DifficultyDef diff = null;
            foreach (var f in fields)
            {
                if (PartFields.Get(p, f) <= 0) continue;
                if (Picked(diff ??= s.Difficulty(data), s.Solo, partId, f) && Filled(s, partId, f) == null) n++;
            }
            return n;
        }

        // ---- 儀表板的每一項用到哪些欄位（用到的欄位只要有一格沒填，這一項就是「資料不足」；可以直接加總的另外標示）----

        static readonly string[] MassF = { PartFields.MassKg };
        static readonly string[] PowerF = { PartFields.PowerW };
        static readonly string[] BatteryF = { PartFields.PowerW, PartFields.CapacityWh };
        static readonly string[] LifeF = { PartFields.MassKg, PartFields.PowerW, PartFields.CapacityWh };
        static readonly string[] DataF = { PartFields.DataGbPerDay };
        static readonly string[] LinkF = { PartFields.DataGbPerDay, PartFields.DownlinkMbps };
        static readonly string[] ResF = { PartFields.ResolutionM };
        static readonly string[] AllCalc = { PartFields.MassKg, PartFields.PowerW, PartFields.CapacityWh, PartFields.DataGbPerDay, PartFields.DownlinkMbps };
        static readonly string[] None = new string[0];

        /// <summary>
        /// 儀表板項目 → 用到的欄位。項目：mass（質量）、power（耗電對發電）、battery（電池撐不撐得過地影）、lifetime（壽命）、
        /// data（儲存）、comms（下傳）、return（資料傳回率）、science（科學成果）、latency（通報延遲）、resolution（解析度）、budget（預算）。
        /// </summary>
        public static string[] FieldsFor(string item)
        {
            switch (item)
            {
                case "mass": return MassF;
                case "power": return PowerF;
                case "battery": return BatteryF;
                case "lifetime": return LifeF;
                case "data": return DataF;
                case "comms":
                case "return":
                case "latency": return LinkF;
                case "science": return AllCalc;
                case "resolution": return ResF;
                default: return None;
            }
        }

        /// <summary>可以直接加總的項目（質量、功率、成本）：沒填的欄位不算進去，總和旁邊標示還有幾個數值沒填。</summary>
        public static bool Summable(string item) => item == "mass" || item == "power" || item == "budget";

        /// <summary>一條任務需求用到的欄位（<see cref="Requirements.IsMetPreLaunch"/> 的算法會讀到的）。</summary>
        public static string[] FieldsForRequirement(string type)
        {
            switch (type)
            {
                case "lifetime_target": return LifeF;
                case "mass_power_margin": return LifeF;
                case "science_threshold": return AllCalc;
                case "data_return": return LinkF;
                case "battery_eclipse": return BatteryF;
                case "latency": return LinkF;
                default: return None;   // budget_reserve、instrument_required、instrument_any、orbit_required：不用挖空的數值
            }
        }

        /// <summary>這條需求現在算不算得出來（用到的數值都看得到）。算不出來就顯示「資料不足」，不顯示通過或不通過。</summary>
        public static bool CanJudge(GameData data, SessionState s, Design design, RequirementDef req) =>
            Missing(data, s, design, FieldsForRequirement(req.Type)) == 0;

        // ---- 零件名稱 ----

        /// <summary>
        /// 設計主螢幕上的零件名稱。名稱後面括號裡寫的容量、速率（「小型鋰電池（80 Wh）」、「X 波段發射機（150 Mbps）」）
        /// 就是被挖空的數值：那一格還沒填時把括號拿掉，不然等於直接把答案寫在名字上。
        /// </summary>
        public static string MaskedName(GameData data, SessionState s, PartDef p, string lang)
        {
            string name = p.Name.Get(lang);
            // 先看名稱（大部分零件的名稱裡沒有這兩個單位），有才去查那一格填了沒
            bool hide = (name.Contains("Wh") && !Known(data, s, p.Id, PartFields.CapacityWh))
                        || (name.Contains("bps") && !Known(data, s, p.Id, PartFields.DownlinkMbps));
            return hide ? StripParen(name) : name;
        }

        /// <summary>拿掉名稱最後面的括號（全形或半形）。</summary>
        public static string StripParen(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            name = name.TrimEnd();
            char close = name[name.Length - 1];
            if (close != ')' && close != '）') return name;
            int open = Math.Max(name.LastIndexOf('('), name.LastIndexOf('（'));
            return open <= 0 ? name : name.Substring(0, open).TrimEnd();
        }

        // ---- 填值指令的格式 ----

        /// <summary>填值指令的內容：零件 id|欄位|數值（數值空白 = 清掉）。</summary>
        public static string FillArg(string partId, string field, double? value) =>
            partId + "|" + field + "|" + (value.HasValue ? value.Value.ToString("R", CultureInfo.InvariantCulture) : "");

        public static bool TryParseFill(string arg, out string partId, out string field, out double? value)
        {
            partId = field = null;
            value = null;
            var bits = (arg ?? "").Split('|');
            if (bits.Length != 3) return false;
            partId = bits[0];
            field = bits[1];
            if (bits[2].Length == 0) return true;
            if (!double.TryParse(bits[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
            if (double.IsNaN(v) || double.IsInfinity(v)) return false;
            value = v;
            return true;
        }
    }
}
