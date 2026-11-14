using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>營運事件的字串 key（資料檔、處置手冊、判讀選項都用這個）。</summary>
    public static class OpsEvents
    {
        public static readonly EventKind[] All = { EventKind.SolarStorm, EventKind.PartFailure, EventKind.ScienceOpportunity, EventKind.PowerAnomaly };

        public static string Key(EventKind k) => k switch
        {
            EventKind.SolarStorm => "solar_storm",
            EventKind.PartFailure => "part_failure",
            EventKind.ScienceOpportunity => "science_opportunity",
            EventKind.PowerAnomaly => "power_anomaly",
            _ => "solar_storm",
        };

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
            Payload => new[] { OpsActions.InstrumentsOff, OpsActions.HighResMode, OpsActions.Reschedule },
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

        public static readonly string[] All =
        {
            InstrumentsOff, HighResMode, Reschedule, IsolatePart, BatteryProtect, SafeMode, SunPoint, PointTarget, PriorityDownlink, SwitchBackup,
        };
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
            _ => new string[0],
        };

        /// <summary>做到幾個必要步驟（不看順序，重複的只算一次）。</summary>
        public static int Done(EventKind k, IEnumerable<string> actions)
        {
            var set = new HashSet<string>(actions ?? Enumerable.Empty<string>());
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
    /// 太陽風暴和電量異常都會讓電量下降（症狀相似），差別在輻射劑量。純函式，多人時每台電腦算出一樣的數字。
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
