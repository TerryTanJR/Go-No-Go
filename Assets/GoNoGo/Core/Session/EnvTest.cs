using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    public enum TestLevel { Pass, Warn, Fail }

    /// <summary>環境測試的一項發現。Code 是字串鍵；A、B、C 是要放進句子的數值（意義依 Code）。</summary>
    public sealed class TestFinding
    {
        /// <summary>vibration（振動，模擬發射）或 thermal（熱真空，模擬軌道上的日照與地影）。</summary>
        public string Test { get; set; } = "";
        public string Code { get; set; } = "";
        public TestLevel Level { get; set; }
        public double A { get; set; }
        public double B { get; set; }
        public double C { get; set; }
    }

    /// <summary>一次環境測試：測的是哪個設計（雜湊）、結果如何。</summary>
    public sealed class TestRecord
    {
        public int N { get; set; }
        public string Hash { get; set; } = "";
        public int By { get; set; }
        public List<TestFinding> Findings { get; set; } = new List<TestFinding>();

        public TestLevel Worst(string test) =>
            Findings.Where(f => f.Test == test).Select(f => f.Level).DefaultIfEmpty(TestLevel.Pass).Max();
    }

    /// <summary>
    /// 簡化的環境測試。真實的太空船在組裝完、發射前要做一連串環境測試（振動台模擬發射、
    /// 熱真空艙模擬太空中的冷熱與真空），很多設計問題在這裡就被抓出來。這裡只模擬兩項，
    /// 而且只抓「部分」問題：發射載具的質量餘裕、電力（電池撐不撐得過地影、太陽能板夠不夠）。
    /// 資料、通訊、預算、時程、壽命還是要靠團隊自己看儀表板判斷。
    /// </summary>
    public static class EnvTests
    {
        public const string Vibration = "vibration";
        public const string Thermal = "thermal";

        /// <summary>
        /// 測試對象的指紋：零件、儀器、軌道、火箭（任務年數、地面站不影響測試結果，改了不用重測）。
        /// 軌道會改變地影長度、火箭會改變運力，所以中控室換了這兩樣也要重測。
        /// </summary>
        public static string Hash(Design d)
        {
            var parts = d.Parts.Where(kv => kv.Key != PartCategory.GroundStation)
                .OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value);
            var inst = (d.Instruments ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal);
            return string.Join(",", parts) + "|" + string.Join(",", inst) + "|" + d.OrbitId + "|" + d.LauncherId;
        }

        public static List<TestFinding> Run(Design d, GameData data, string difficultyId)
        {
            var ev = Evaluator.EvaluateDesign(d, data, difficultyId, false);
            var list = new List<TestFinding>();

            // ---- 振動測試（含發射載具介面檢查）：和 Simulator.ResolveLaunch 用同樣的門檻 ----
            var m = ev.Mass;
            double margin = ev[ResourceId.Mass].Margin;
            if (!m.OrbitReachable)
                list.Add(new TestFinding { Test = Vibration, Code = "test.vib.unreachable", Level = TestLevel.Fail });
            else if (margin < 0)
                list.Add(new TestFinding { Test = Vibration, Code = "test.vib.overweight", Level = TestLevel.Fail, A = m.WetKg, B = m.LauncherCapacityKg });
            else if (margin < 0.05)
                list.Add(new TestFinding { Test = Vibration, Code = "test.vib.tight", Level = TestLevel.Warn, A = m.WetKg, B = m.LauncherCapacityKg, C = margin });
            else
                list.Add(new TestFinding { Test = Vibration, Code = "test.vib.pass", Level = TestLevel.Pass, A = m.WetKg, B = m.LauncherCapacityKg, C = margin });

            // ---- 熱真空測試：模擬一圈軌道的日照與地影 ----
            var p = ev.Power;
            double eff = Math.Max(1e-6, data.Constants.BatteryEfficiency);
            double needWh = p.EclipseEnergyWh / eff;
            if (p.ActualDod > 1.0)
            {
                // 電池在地影中第幾分鐘耗盡
                double minutes = ev.Orbit.EclipseMin * Math.Min(1.0, 1.0 / p.ActualDod);
                list.Add(new TestFinding { Test = Thermal, Code = "test.tv.battery_dead", Level = TestLevel.Fail, A = ev.Orbit.EclipseMin, B = minutes, C = p.BatteryWh });
            }
            else if (p.ActualDod > p.AllowedDod)
                list.Add(new TestFinding { Test = Thermal, Code = "test.tv.battery_wear", Level = TestLevel.Warn, A = p.ActualDod, B = p.AllowedDod, C = ev.Orbit.EclipseMin });
            if (p.ArrayMargin < 0)
                list.Add(new TestFinding { Test = Thermal, Code = "test.tv.solar", Level = TestLevel.Warn, A = p.ArrayEolW, B = p.RequiredArrayW });
            if (!list.Any(f => f.Test == Thermal))
                list.Add(new TestFinding { Test = Thermal, Code = "test.tv.pass", Level = TestLevel.Pass, A = ev.Orbit.EclipseMin, B = p.BatteryWh, C = needWh });
            return list;
        }

        /// <summary>整合台上目前的設計測過了沒（測過之後只要動到零件、軌道或火箭就要重測）。</summary>
        public static bool IsCurrent(SessionState s) => s.LastTest != null && s.LastTest.Hash == Hash(s.Bench);
    }
}
