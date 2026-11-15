using System;
using System.Globalization;
using GoNoGo.Core;
using GoNoGo.Game;

namespace GoNoGo.UI
{
    /// <summary>數字與單位的顯示格式（中英文共用規則）。</summary>
    public static class Fmt
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // 常用的小數位數先備好格式字串，不用每次接一個新的
        static readonly string[] NFormats = { "N0", "N1", "N2", "N3" };

        public static string N(double v, int dec = 0) => v.ToString(dec >= 0 && dec < NFormats.Length ? NFormats[dec] : "N" + dec, Inv);

        /// <summary>沙盒的預算「沒有上限」：資料裡是一個很大的數字，畫面一律寫 ∞。</summary>
        public static bool Unlimited(double v) => v >= ScenarioDef.NoLimit / 2;

        public static string Money(double musd) => Unlimited(musd) ? "∞" : musd >= 100 ? $"${musd.ToString("0", Inv)}M" : $"${musd.ToString("0.0", Inv)}M";

        /// <summary>零件的價錢：很多零件不到 0.1 M$，要多兩位小數才看得出差別。</summary>
        public static string PartMoney(double musd) => Math.Abs(musd) < 1 ? $"${musd.ToString("0.00", Inv)}M" : Money(musd);

        public static string Pct(double f) => (f * 100).ToString("0", Inv) + "%";

        public static string Unit(string unit)
        {
            switch (unit)
            {
                case "GB/day": return Loc.IsZh ? "GB/天" : "GB/day";
                default: return unit;
            }
        }

        /// <summary>一項資源的「已用 / 上限」。</summary>
        public static string Resource(ResourceReport r)
        {
            switch (r.Id)
            {
                case ResourceId.Budget: return $"{Money(r.Used)} / {Money(r.Capacity)}";
                case ResourceId.Comms: return $"{N(r.Used, 1)} / {N(r.Capacity, 1)} {Unit(r.Unit)}";
                case ResourceId.Data: return $"{N(r.Used, 1)} / {N(r.Capacity, 0)} {Unit(r.Unit)}";
                default: return Unlimited(r.Capacity) ? $"{N(r.Used)} / ∞ {Unit(r.Unit)}" : $"{N(r.Used)} / {N(r.Capacity)} {Unit(r.Unit)}";
            }
        }

        public static string Years(double y) => Loc.IsZh ? $"{y.ToString("0.0", Inv)} 年" : $"{y.ToString("0.0", Inv)} yr";

        /// <summary>倒數計時 m:ss。</summary>
        public static string Clock(float seconds)
        {
            int t = Math.Max(0, (int)Math.Ceiling(seconds));
            // 每一幀都會被呼叫，但一秒才變一次：記住上一次的結果
            if (t != _clockT)
            {
                _clockT = t;
                _clockText = $"{t / 60}:{(t % 60).ToString("00", Inv)}";
            }
            return _clockText;
        }

        static int _clockT = -1;
        static string _clockText = "";

        /// <summary>任務需求的分類名稱（任務、預算、科學、工程、通訊）：只是分類，不用部門的名義。</summary>
        public static string ReqGroup(Department d) => Loc.T("ui.req.group." + Departments.Key(d));
    }
}
