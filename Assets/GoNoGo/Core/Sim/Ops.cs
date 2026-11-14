using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>營運事件的字串 key（資料檔、處置手冊、判讀選項都用這個）。</summary>
    public static class OpsEvents
    {
        public static readonly EventKind[] All =
            { EventKind.SolarStorm, EventKind.PartFailure, EventKind.ScienceOpportunity, EventKind.PowerAnomaly, EventKind.BigFire, EventKind.FalseFire };

        public static string Key(EventKind k) => k switch
        {
            EventKind.SolarStorm => "solar_storm",
            EventKind.PartFailure => "part_failure",
            EventKind.ScienceOpportunity => "science_opportunity",
            EventKind.PowerAnomaly => "power_anomaly",
            EventKind.BigFire => "big_fire",
            EventKind.FalseFire => "false_fire",
            _ => "solar_storm",
        };

        public static bool IsFire(EventKind k) => k == EventKind.BigFire || k == EventKind.FalseFire;

        /// <summary>這個關卡、這個難度會出現的事件（手冊、判讀選項用），依 All 的順序。</summary>
        public static List<EventKind> For(ScenarioDef s, DifficultyDef d)
        {
            var keys = GameData.EventTypes(s, d);
            return All.Where(k => keys.Contains(Key(k))).ToList();
        }

        public static EventKind? Parse(string key)
        {
            foreach (var k in All) if (Key(k) == key) return k;
            return null;
        }
    }

    /// <summary>中控室四座營運控制台。</summary>
    public static class OpsConsoles
    {
        public const string Payload = "payload", Power = "power", Attitude = "attitude", Comms = "comms";
        public static readonly string[] All = { Payload, Power, Attitude, Comms };

        public static IReadOnlyList<string> Actions(string console) => console switch
        {
            Payload => new[] { OpsActions.InstrumentsOff, OpsActions.HighResMode, OpsActions.Reschedule, OpsActions.FirePriority, OpsActions.MarkFalseAlarm },
            Power => new[] { OpsActions.IsolatePart, OpsActions.BatteryProtect },
            Attitude => new[] { OpsActions.SafeMode, OpsActions.SunPoint, OpsActions.PointTarget },
            Comms => new[] { OpsActions.PriorityDownlink, OpsActions.SwitchBackup },
            _ => new string[0],
        };

        public static string ConsoleOf(string action) => All.FirstOrDefault(c => Actions(c).Contains(action)) ?? "";
    }

    /// <summary>控制台上可以做的操作。</summary>
    public static class OpsActions
    {
        public const string InstrumentsOff = "instruments_off";
        public const string HighResMode = "high_res_mode";
        public const string Reschedule = "reschedule";
        public const string IsolatePart = "isolate_part";
        public const string BatteryProtect = "battery_protect";
        public const string SafeMode = "safe_mode";
        public const string SunPoint = "sun_point";
        public const string PointTarget = "point_target";
        public const string PriorityDownlink = "priority_downlink";
        public const string SwitchBackup = "switch_backup";
        /// <summary>野火關卡：火點資料排到下傳佇列最前面。</summary>
        public const string FirePriority = "fire_priority";
        /// <summary>野火關卡：把這個火點標記為誤報，不通報。</summary>
        public const string MarkFalseAlarm = "mark_false_alarm";

        public static readonly string[] All =
        {
            InstrumentsOff, HighResMode, Reschedule, IsolatePart, BatteryProtect, SafeMode, SunPoint, PointTarget, PriorityDownlink, SwitchBackup,
            FirePriority, MarkFalseAlarm,
        };

        /// <summary>只在野火關卡出現的操作（其他關卡的控制台不顯示）。</summary>
        public static bool FireOnly(string a) => a == FirePriority || a == MarkFalseAlarm;
    }

    /// <summary>處置手冊：每種事件要做的操作（順序就是手冊上的步驟順序）。</summary>
    public static class Procedures
    {
        public static IReadOnlyList<string> Required(EventKind k) => k switch
        {
            EventKind.SolarStorm => new[] { OpsActions.InstrumentsOff, OpsActions.SafeMode, OpsActions.SunPoint },
            EventKind.PowerAnomaly => new[] { OpsActions.InstrumentsOff, OpsActions.SunPoint, OpsActions.BatteryProtect },
            EventKind.PartFailure => new[] { OpsActions.IsolatePart, OpsActions.SwitchBackup, OpsActions.Reschedule },
            EventKind.ScienceOpportunity => new[] { OpsActions.PointTarget, OpsActions.HighResMode, OpsActions.PriorityDownlink },
            EventKind.BigFire => new[] { OpsActions.FirePriority, OpsActions.PriorityDownlink },
            EventKind.FalseFire => new[] { OpsActions.MarkFalseAlarm },
            _ => new string[0],
        };

        /// <summary>
        /// 互相衝突的操作：同一回合兩個都送出，兩個都無效（不算步驟、也沒有效果）。
        /// 讓「全部按下去」連必要步驟都做不滿。
        /// </summary>
        public static readonly (string a, string b)[] Conflicts =
        {
            (OpsActions.SafeMode, OpsActions.PointTarget),        // 安全模式鎖定對日姿態，不能指向目標
            (OpsActions.SafeMode, OpsActions.HighResMode),        // 安全模式下儀器關機
            (OpsActions.InstrumentsOff, OpsActions.HighResMode),  // 儀器關了就不能高解析拍攝
            (OpsActions.BatteryProtect, OpsActions.PriorityDownlink), // 下傳要用發射功率，和省電互斥
        };

        /// <summary>這回合因為互相衝突而無效的操作。</summary>
        public static List<string> Conflicted(IEnumerable<string> actions)
        {
            var set = new HashSet<string>(actions ?? Enumerable.Empty<string>());
            return OpsActions.All.Where(a => Conflicts.Any(c => (c.a == a && set.Contains(c.b)) || (c.b == a && set.Contains(c.a))) && set.Contains(a)).ToList();
        }

        /// <summary>真正生效的操作（去掉互相衝突的）。</summary>
        public static List<string> Effective(IEnumerable<string> actions)
        {
            var list = (actions ?? Enumerable.Empty<string>()).Distinct().ToList();
            var bad = new HashSet<string>(Conflicted(list));
            return list.Where(a => !bad.Contains(a)).ToList();
        }

        /// <summary>做到幾個必要步驟（不看順序，重複的只算一次；互相衝突的不算）。</summary>
        public static int Done(EventKind k, IEnumerable<string> actions)
        {
            var set = new HashSet<string>(Effective(actions));
            return Required(k).Count(set.Contains);
        }

        public static List<string> Wrong(EventKind k, IEnumerable<string> actions)
        {
            var req = Required(k);
            return (actions ?? Enumerable.Empty<string>()).Where(a => !req.Contains(a)).Distinct().ToList();
        }
    }

    /// <summary>控制台上的一個讀數。Key 是多語字串鍵（ops.read.*），Value 已格式化；Alert 代表超出正常範圍。</summary>
    public sealed class Reading
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "";
        public string Unit { get; set; } = "";
        public bool Alert { get; set; }
    }

    /// <summary>
    /// 控制台讀數：事件發生時「看起來」的樣子。系統不說是什麼事件，玩家要從讀數判斷。
    /// 太陽風暴和電量異常都會讓電量下降（症狀相似），差別在輻射劑量。
    /// 大火爆發和假火點都讓中波紅外線讀數暴增（症狀相似），差別在長波熱紅外線有沒有跟著升高、以及地點。
    /// 純函式，多人時每台電腦算出一樣的數字。
    /// </summary>
    public static class OpsTelemetry
    {
        static string F(double v, string fmt = "0") => v.ToString(fmt, CultureInfo.InvariantCulture);

        public static List<Reading> For(string console, MissionEvent e, Evaluation ev, Design d, GameData data, Sample s)
        {
            var list = new List<Reading>();
            void Add(string key, string value, string unit, bool alert = false) =>
                list.Add(new Reading { Key = "ops.read." + key, Value = value, Unit = unit, Alert = alert });

            var kind = e?.Kind;
            int sev = Math.Max(1, e?.Severity ?? 1);
            double soc = (s?.BatterySoc ?? 0.9) * 100;
            double array = s?.ArrayW ?? ev.Power.ArrayBolW;
            double load = ev.Power.LoadW;
            var orbit = data.Orbit(d.OrbitId);
            double doseRate = (orbit?.DoseKradPerYear ?? 1) * 1e6 / 8766.0; // mrad/h
            double flux = 1;
            double temp = 18;
            string failedCat = kind == EventKind.PartFailure ? data.Part(e.RefId)?.Category ?? "" : "";
            bool fireMission = data.Scenario(d.ScenarioId)?.FireAlert ?? false;
            bool fireEvent = kind.HasValue && OpsEvents.IsFire(kind.Value);
            bool redundant = kind == EventKind.PartFailure && (data.Part(e.RefId)?.Redundant ?? false);

            switch (kind)
            {
                case EventKind.SolarStorm:
                    doseRate *= 8 + 6 * sev;
                    flux = 100 * Math.Pow(10, sev);
                    temp += 4;
                    array *= 0.9;
                    soc = Math.Max(5, soc - 6);
                    break;
                case EventKind.PowerAnomaly:
                    soc = Math.Max(5, 42 - 9 * sev);
                    array *= 0.55;
                    temp -= 3;
                    break;
            }

            switch (console)
            {
                case OpsConsoles.Power:
                    Add("battery", F(soc), "%", soc < 50);
                    Add("array", F(array), "W", array < ev.Power.RequiredArrayW * 0.8);
                    Add("load", F(load), "W");
                    Add("bus", failedCat == PartCategory.Computer && !redundant ? "FAULT" : "NOMINAL", "", failedCat == PartCategory.Computer && !redundant);
                    break;
                case OpsConsoles.Payload:
                    foreach (var id in d.Instruments)
                    {
                        var p = data.Part(id);
                        bool down = failedCat == PartCategory.Instrument && e.RefId == id && !redundant;
                        list.Add(new Reading { Key = "part:" + id, Value = down ? "NO SIGNAL" : "OK", Unit = "", Alert = down });
                    }
                    Add("temp", F(temp, "0.0"), "°C", Math.Abs(temp - 18) > 2.5);
                    if (kind == EventKind.ScienceOpportunity)
                        Add("ground_alert", "opp." + e.RefId, "", true);
                    else Add("ground_alert", "—", "");
                    if (fireMission)
                    {
                        // 中波紅外線（火點儀）：熱點像素數
                        bool mwir = d.Instruments.Any(id => (data.Part(id)?.FireDetection ?? 0) >= 0.5);
                        int hot = fireEvent ? 30 + 25 * sev : 2;
                        Add("mwir", mwir ? F(hot) : "NO DATA", mwir ? "px" : "", mwir && fireEvent);
                        // 長波熱紅外線：真的火會一起升高；沙漠曬熱、太陽反光不會
                        bool lwir = d.Instruments.Contains("inst_thermal");
                        double bt = kind == EventKind.BigFire ? 335 + 12 * sev : kind == EventKind.FalseFire ? 309 : 296;
                        Add("lwir", lwir ? F(bt) : "NO DATA", lwir ? "K" : "", lwir && kind == EventKind.BigFire);
                        Add("fire_site", fireEvent ? "site." + e.RefId : "—", "", fireEvent);
                    }
                    break;
                case OpsConsoles.Attitude:
                    double point = data.Part(d.Get(PartCategory.Adcs))?.PointingArcsec ?? 30;
                    bool adcsDown = failedCat == PartCategory.Adcs && !redundant;
                    if (adcsDown) point *= 40;
                    Add("pointing", F(point), "arcsec", adcsDown);
                    Add("dose_rate", F(doseRate, "0.0"), "mrad/h", kind == EventKind.SolarStorm);
                    Add("proton_flux", F(flux), "pfu", flux >= 10);
                    Add("wheels", adcsDown ? "FAULT" : redundant && failedCat == PartCategory.Adcs ? "BACKUP" : "OK", "", adcsDown);
                    break;
                case OpsConsoles.Comms:
                    double fill = (s?.StorageFill ?? 0.3) * 100;
                    if (fireEvent) fill = Math.Min(100, fill + 30 + 10 * sev);
                    bool memDown = failedCat == PartCategory.Computer && !redundant;
                    if (memDown) fill = Math.Min(100, fill * 2 + 30);
                    bool linkDown = failedCat == PartCategory.Antenna && !redundant;
                    Add("storage", F(fill), "%", fill > 85);
                    Add("link", linkDown ? "DEGRADED" : "LOCKED", "", linkDown);
                    Add("downlink", F(ev.Data.DownlinkGbPerDay * (linkDown ? 0.5 : 1), "0.0"), "GB/day", linkDown);
                    Add("memory", memDown ? "ERR" : "OK", "", memDown);
                    break;
            }
            return list;
        }
    }
}
