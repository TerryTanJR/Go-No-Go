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

        public static string Money(double musd) => musd >= 100 ? $"${musd.ToString("0", Inv)}M" : $"${musd.ToString("0.0", Inv)}M";

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
                default: return $"{N(r.Used)} / {N(r.Capacity)} {Unit(r.Unit)}";
            }
        }

        public static string Amount(ResourceId id, double v, string unit)
        {
            switch (id)
            {
                case ResourceId.Budget: return Money(v);
                case ResourceId.Comms:
                case ResourceId.Data: return $"{N(v, 1)} {Unit(unit)}";
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
