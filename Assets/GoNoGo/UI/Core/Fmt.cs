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

        public static string N(double v, int dec = 0) => v.ToString("N" + dec, Inv);

        /// <summary>月數：最多兩位小數、不補零（18、18.5、18.25）。</summary>
        public static string Months(double v) => v.ToString("#,0.##", Inv);

        /// <summary>沙盒的預算、期限「沒有上限」：資料裡是一個很大的數字，畫面一律寫 ∞。</summary>
        public static bool Unlimited(double v) => v >= ScenarioDef.NoLimit / 2;

        public static string Money(double musd) => Unlimited(musd) ? "∞" : musd >= 100 ? $"${musd.ToString("0", Inv)}M" : $"${musd.ToString("0.0", Inv)}M";

        /// <summary>零件的價錢：很多零件不到 0.1 M$，要多兩位小數才看得出差別。</summary>
        public static string PartMoney(double musd) => Math.Abs(musd) < 1 ? $"${musd.ToString("0.00", Inv)}M" : Money(musd);

        public static string Pct(double f) => (f * 100).ToString("0", Inv) + "%";

        public static string Unit(string unit)
        {
            switch (unit)
            {
                case "mo": return Loc.IsZh ? "個月" : "mo";
                case "GB/day": return Loc.IsZh ? "GB/天" : "GB/day";
                default: return unit;
            }
        }

        /// <summary>資源條右邊的「已用 / 上限」。</summary>
        public static string Resource(ResourceReport r)
        {
            switch (r.Id)
            {
                case ResourceId.Budget: return $"{Money(r.Used)} / {Money(r.Capacity)}";
                case ResourceId.Comms: return $"{N(r.Used, 1)} / {N(r.Capacity, 1)} {Unit(r.Unit)}";
                case ResourceId.Data: return $"{N(r.Used, 1)} / {N(r.Capacity, 0)} {Unit(r.Unit)}";
                // 時程：返工一次 +0.25 個月，取整數的話玩家看不到變化
                case ResourceId.Time when !Unlimited(r.Capacity): return $"{Months(r.Used)} / {Months(r.Capacity)} {Unit(r.Unit)}";
                default: return Unlimited(r.Capacity) ? $"{N(r.Used)} / ∞ {Unit(r.Unit)}" : $"{N(r.Used)} / {N(r.Capacity)} {Unit(r.Unit)}";
            }
        }

        public static string Amount(ResourceId id, double v, string unit)
        {
            switch (id)
            {
                case ResourceId.Budget: return Money(v);
                case ResourceId.Comms:
                case ResourceId.Data: return $"{N(v, 1)} {Unit(unit)}";
                case ResourceId.Time: return $"{Months(v)} {Unit(unit)}";
                default: return $"{N(v)} {Unit(unit)}";
            }
        }

        /// <summary>貢獻項目的名稱：零件、火箭、或固定項目（bus、structure…）。</summary>
        public static string ContributionName(string key)
        {
            var d = GameSession.Data;
            if (d.Part(key) != null) return Loc.Part(key);
            if (d.Launcher(key) != null) return Loc.Launcher(key);
            return Loc.T("ui.contrib." + key);
        }

        public static string Years(double y) => Loc.IsZh ? $"{y.ToString("0.0", Inv)} 年" : $"{y.ToString("0.0", Inv)} yr";

        /// <summary>倒數計時 m:ss。</summary>
        public static string Clock(float seconds)
        {
            int t = Math.Max(0, (int)Math.Ceiling(seconds));
            return $"{t / 60}:{(t % 60).ToString("00", Inv)}";
        }

        public static string Dept(Department d) => Loc.T(GameSession.Data.Department(Departments.Key(d))?.Name);
        public static string Dept(string key) => Loc.T(GameSession.Data.Department(key)?.Name);
    }
}
