using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>
    /// 取捨引擎：輸入設計，輸出五種資源、壽命、科學潛力與警告。
    /// 純函式、沒有亂數，同樣的輸入永遠得到同樣的輸出。
    /// </summary>
    public static class Evaluator
    {
        /// <summary>剩不到一成餘裕就是「緊」（黃燈）。</summary>
        public const double TightMargin = 0.10;
        public const double Unlimited = 99.0;

        public static Evaluation EvaluateDesign(Design design, GameData data) =>
            EvaluateDesign(design, data, null, true);

        public static Evaluation EvaluateDesign(Design design, GameData data, string difficultyId, bool withFixes)
        {
            var c = data.Constants;
            var ev = new Evaluation();
            var scenario = data.Scenario(design.ScenarioId);
            ev.DesignErrors = GameDataLoader.ValidateDesign(data, design, "design");

            var orbit = data.Orbit(design.OrbitId);
            var launcher = data.Launcher(design.LauncherId);
            var instruments = design.Instruments.Select(data.Part).Where(p => p != null).ToList();
            PartDef P(string cat) => data.Part(design.Get(cat));
            var solar = P(PartCategory.Solar);
            var battery = P(PartCategory.Battery);
            var antenna = P(PartCategory.Antenna);
            var gs = P(PartCategory.GroundStation);
            var computer = P(PartCategory.Computer);
            var adcs = P(PartCategory.Adcs);
            var propulsion = P(PartCategory.Propulsion);
            var shielding = P(PartCategory.Shielding);
            int years = Math.Max(1, design.MissionYears);

            // ---------- 軌道 ----------
            bool moon = orbit != null && orbit.Body == "moon";
            double R = moon ? c.MoonRadiusKm : c.EarthRadiusKm;
            double mu = moon ? c.MoonMuKm3S2 : c.EarthMuKm3S2;
            var oi = ev.Orbit;
            if (orbit != null)
            {
                oi.PeriodMin = OrbitMath.PeriodMinutes(orbit.AltitudeKm, R, mu);
                oi.EclipseFraction = OrbitMath.EclipseFraction(orbit.AltitudeKm, R, orbit.BetaDeg);
                oi.EclipseMin = oi.PeriodMin * oi.EclipseFraction;
                oi.SunlitMin = oi.PeriodMin - oi.EclipseMin;
                oi.OrbitsPerDay = 1440.0 / oi.PeriodMin;
                oi.EclipsesPerYear = oi.EclipseFraction <= 0 ? 0
                    : orbit.EclipsesPerYearOverride > 0 ? orbit.EclipsesPerYearOverride
                    : oi.OrbitsPerDay * 365.25;
                if (orbit.ContactFraction > 0)
                {
                    oi.ContactMinPerDay = 1440.0 * orbit.ContactFraction;
                    oi.ContactsPerDay = 1;
                }
                else
                {
                    oi.ContactsPerDay = (gs?.Stations ?? 0) * orbit.PassesPerDayPerStation;
                    oi.ContactMinPerDay = Math.Min(1440.0, oi.ContactsPerDay * orbit.PassMinutes);
                }
            }

            // ---------- 電力 ----------
            var pw = ev.Power;
            double pe = c.BusBasePowerW, pd = c.BusBasePowerW;
            foreach (var i in instruments)
            {
                pd += i.PowerW;
                if (!i.SunlitOnly) pe += i.PowerW;
            }
            foreach (var p in new[] { computer, adcs, propulsion, gs, solar, battery, shielding })
            {
                if (p == null || p.PowerW <= 0) continue;
                pe += p.PowerW; pd += p.PowerW;
            }
            if (antenna != null)
            {
                double tx = antenna.TxPowerW * oi.ContactMinPerDay / 1440.0 + antenna.PowerW;
                pe += tx; pd += tx;
            }
            double te = oi.EclipseMin, td = oi.SunlitMin;
            pw.LoadW = oi.PeriodMin > 0 ? (pe * te + pd * td) / oi.PeriodMin : pd;
            pw.RequiredArrayW = td > 0
                ? (pe * te / c.PathEfficiencyEclipse + pd * td / c.PathEfficiencyDaylight) / td
                : pd / c.PathEfficiencyDaylight;
            pw.ArrayBolW = (solar?.AreaM2 ?? 0) * c.SolarArrayWPerM2 * (solar?.CellFactor ?? 1);
            pw.ArrayEolW = pw.ArrayBolW * Math.Pow(1 - c.SolarDegradationPerYear, years);
            pw.EclipseEnergyWh = pe * te / 60.0;
            pw.AllowedDod = orbit?.BatteryDod ?? 0.3;
            pw.BatteryWh = battery?.CapacityWh ?? 0;
            pw.BatteryRequiredWh = pw.EclipseEnergyWh / (pw.AllowedDod * c.BatteryEfficiency);
            pw.ActualDod = pw.BatteryWh > 0 ? pw.EclipseEnergyWh / c.BatteryEfficiency / pw.BatteryWh : (pw.EclipseEnergyWh > 0 ? 9 : 0);
            pw.ArrayMargin = pw.ArrayEolW > 0 ? (pw.ArrayEolW - pw.RequiredArrayW) / pw.ArrayEolW : -1;
            pw.BatteryMargin = pw.EclipseEnergyWh <= 0 ? 1 : pw.BatteryWh > 0 ? (pw.BatteryWh - pw.BatteryRequiredWh) / pw.BatteryWh : -1;
            AddResource(ev, ResourceId.Power, pw.RequiredArrayW, pw.ArrayEolW, "W",
                Math.Min(pw.ArrayMargin, pw.BatteryMargin));

            // ---------- 質量 ----------
            var m = ev.Mass;
            // 選到的零件（質量和成本都要逐個加總，只查一次）
            var parts = design.AllPartIds().Select(data.Part).Where(p => p != null).ToList();
            foreach (var p in parts) m.PartsKg += p.MassKg;
            m.DryKg = m.PartsKg / (1 - c.StructureFraction);
            m.StructureKg = m.DryKg - m.PartsKg;
            m.PropellantKg = propulsion?.PropellantKg ?? 0;
            m.WetKg = m.DryKg + m.PropellantKg;
            m.OrbitReachable = orbit != null && GameData.Reaches(launcher, orbit.Id);
            m.LauncherCapacityKg = m.OrbitReachable ? launcher.CapacityKg[orbit.Id] : 0;
            AddResource(ev, ResourceId.Mass, m.WetKg, m.LauncherCapacityKg, "kg",
                m.LauncherCapacityKg > 0 ? (m.LauncherCapacityKg - m.WetKg) / m.LauncherCapacityKg : -1);

            // ---------- 資料與通訊 ----------
            var dt = ev.Data;
            foreach (var i in instruments) dt.GeneratedGbPerDay += i.DataGbPerDay;
            // 平均速率：雷射通訊之類的連線只有部分過站時間連得上
            double rate = (antenna?.DownlinkMbps ?? 0) * (antenna?.LinkAvailability ?? 1) * (orbit?.DownlinkRateFactor ?? 1);
            dt.DownlinkGbPerDay = rate * oi.ContactMinPerDay * 60.0 / 8.0 / 1000.0 * c.LinkEfficiency;
            double gapHours;
            if (orbit != null && orbit.ContactFraction > 0) gapHours = 24.0 * (1 - orbit.ContactFraction);
            else gapHours = oi.ContactsPerDay > 0 ? Math.Min(24.0, c.ContactGapFactor * 24.0 / oi.ContactsPerDay) : 24.0;
            gapHours = Math.Max(1.0, gapHours);
            double storageRaw = dt.GeneratedGbPerDay * gapHours / 24.0;
            dt.StorageRawGb = storageRaw;
            dt.StorageNeededGb = storageRaw * c.StorageSafetyFactor;
            dt.StorageGb = computer?.StorageGb ?? 0;
            double linkFrac = dt.GeneratedGbPerDay > 0 ? Math.Min(1, dt.DownlinkGbPerDay / dt.GeneratedGbPerDay) : 1;
            double storeFrac = storageRaw > 0 ? Math.Min(1, dt.StorageGb / storageRaw) : 1;
            dt.ReturnFraction = linkFrac * storeFrac;
            // 儲存燈號分兩層：比原始需求小 = 紅（真的會掉資料）；比含安全係數的需求小 = 黃（漏接或電腦故障時會掉）
            double storageMargin = dt.StorageGb > 0 ? (dt.StorageGb - storageRaw) / dt.StorageGb : (storageRaw > 0 ? -1 : 1);
            var storageStatus = storageMargin < 0 ? ResourceStatus.Over
                : dt.StorageGb < dt.StorageNeededGb ? ResourceStatus.Tight
                : ResourceStatus.Ok;
            AddResource(ev, ResourceId.Data, dt.StorageNeededGb, dt.StorageGb, "GB", storageMargin, storageStatus);
            AddResource(ev, ResourceId.Comms, dt.GeneratedGbPerDay, dt.DownlinkGbPerDay, "GB/day",
                dt.DownlinkGbPerDay > 0 ? (dt.DownlinkGbPerDay - dt.GeneratedGbPerDay) / dt.DownlinkGbPerDay : (dt.GeneratedGbPerDay > 0 ? -1 : 1));

            // ---------- 預算 ----------
            var co = ev.Cost;
            co.DevelopmentMusd = c.BusBaseCostMusd;
            foreach (var p in parts) co.DevelopmentMusd += p.CostMusd;
            co.LaunchMusd = launcher?.CostMusd ?? 0;
            co.OpsPerYearMusd = c.OpsBaseCostMusdPerYear + (gs?.OpsCostMusdPerYear ?? 0);
            co.OpsTotalMusd = co.OpsPerYearMusd * years;
            co.TotalMusd = co.DevelopmentMusd + co.LaunchMusd + co.OpsTotalMusd;
            co.BudgetMusd = GameData.Budget(scenario);
            AddResource(ev, ResourceId.Budget, co.TotalMusd, co.BudgetMusd, "M$",
                co.BudgetMusd > 0 ? (co.BudgetMusd - co.TotalMusd) / co.BudgetMusd : -1);

            // ---------- 壽命 ----------
            var lf = ev.Lifetime;
            lf.TargetYears = years;
            lf.DeltaVAvailableMs = OrbitMath.DeltaV(propulsion?.IspS ?? 0, c.G0Ms2, m.WetKg, m.PropellantKg);
            lf.PropellantYears = orbit == null ? 0
                : orbit.DeltaVPerYearMs <= 0 ? Unlimited
                : Math.Min(Unlimited, orbit.NaturalServiceYears + lf.DeltaVAvailableMs / orbit.DeltaVPerYearMs);
            if (oi.EclipsesPerYear <= 0) lf.BatteryYears = Unlimited;
            else if (pw.ActualDod >= 1.0 || pw.BatteryWh <= 0) lf.BatteryYears = 0;
            else lf.BatteryYears = Math.Min(Unlimited, CycleLife(c, pw.ActualDod) / oi.EclipsesPerYear);
            double dose = (orbit?.DoseKradPerYear ?? 0) * (shielding?.DoseFactor ?? 1);
            lf.RadiationYears = dose <= 0 ? Unlimited : Math.Min(Unlimited, (computer?.RadToleranceKrad ?? 0) / dose);
            lf.Years = Math.Min(lf.PropellantYears, Math.Min(lf.BatteryYears, lf.RadiationYears));
            lf.Limit = lf.Years >= Unlimited ? LifetimeLimit.None
                : lf.Years == lf.PropellantYears ? LifetimeLimit.Propellant
                : lf.Years == lf.BatteryYears ? LifetimeLimit.Battery
                : LifetimeLimit.Radiation;

            // ---------- 科學潛力 ----------
            string objective = scenario?.Objective ?? "";
            double orbitMatch = OrbitMatch(orbit, objective);
            ev.SciencePerYear = instruments.Sum(i => i.ScienceValue * Match(i, objective)) * orbitMatch * c.ScienceYearScale;

            // ---------- 火點偵測與通報延遲（野火關卡；其他關卡也算，只是不顯示） ----------
            var fi = ev.Fire;
            fi.InstrumentDetection = Math.Min(1.0, instruments.Sum(i => i.FireDetection));
            fi.OrbitFactor = orbitMatch;
            fi.Detection = fi.InstrumentDetection * fi.OrbitFactor;
            // 每天一段連續通聯：落在通聯時段內不用等，落在空檔（機率 1 − f）平均等空檔的一半
            if (orbit != null && orbit.ContactFraction > 0) fi.WaitMin = (1 - orbit.ContactFraction) * 1440.0 * (1 - orbit.ContactFraction) / 2;
            else fi.WaitMin = oi.ContactsPerDay > 0 ? 1440.0 / oi.ContactsPerDay / 2 : 1440.0;
            fi.OrbitDataGb = oi.OrbitsPerDay > 0 ? dt.GeneratedGbPerDay / oi.OrbitsPerDay : dt.GeneratedGbPerDay;
            double mbps = rate * c.LinkEfficiency;
            fi.DownlinkMin = fi.OrbitDataGb <= 0 ? 0 : mbps > 0 ? fi.OrbitDataGb * 8000.0 / mbps / 60.0 : 1440.0;
            fi.ProcessingMin = c.GroundProcessingMin;
            fi.LatencyMin = fi.WaitMin + fi.DownlinkMin + fi.ProcessingMin;
            double limit = scenario?.LatencyLimitMin ?? 0;
            fi.TimelyFraction = limit > 0 ? Math.Min(1.0, limit / Math.Max(1e-6, fi.LatencyMin)) : 1.0;

            BuildWarnings(ev, design, scenario, instruments, objective);
            if (withFixes) FixFinder.AttachFixes(ev, design, data, difficultyId);
            return ev;
        }

        /// <summary>儀器和任務目標的吻合度。沙盒的目標不限定主題：用這台儀器最擅長的那個目標。</summary>
        public static double Match(PartDef instrument, string objective) =>
            instrument.ObjectiveMatch == null ? 0
            : objective == ScenarioDef.OpenObjective ? Best(instrument.ObjectiveMatch)
            : instrument.ObjectiveMatch.TryGetValue(objective, out var v) ? v : 0;

        /// <summary>軌道和任務目標的吻合度（沙盒同上）。</summary>
        public static double OrbitMatch(OrbitDef orbit, string objective) =>
            orbit?.ObjectiveMatch == null ? 0
            : objective == ScenarioDef.OpenObjective ? Best(orbit.ObjectiveMatch)
            : orbit.ObjectiveMatch.TryGetValue(objective, out var v) ? v : 0;

        static double Best(Dictionary<string, double> match) => match.Count == 0 ? 0 : match.Values.Max();

        /// <summary>依放電深度內插電池循環壽命。</summary>
        public static double CycleLife(Constants c, double dod)
        {
            // 對照點通常已經照放電深度由小到大排好：排好的就直接用（穩定排序的結果和原本的順序一樣）
            var pts = c.BatteryCycleLife;
            for (int i = 1; i < pts.Count; i++)
                if (pts[i][0] < pts[i - 1][0]) { pts = pts.OrderBy(p => p[0]).ToList(); break; }
            if (pts.Count == 0) return 0;
            if (dod <= pts[0][0]) return pts[0][1];
            for (int i = 1; i < pts.Count; i++)
            {
                if (dod <= pts[i][0])
                {
                    double t = (dod - pts[i - 1][0]) / (pts[i][0] - pts[i - 1][0]);
                    // 循環壽命隨 DoD 大約指數下降，用對數內插比線性準
                    return Math.Exp(Math.Log(pts[i - 1][1]) + t * (Math.Log(pts[i][1]) - Math.Log(pts[i - 1][1])));
                }
            }
            return pts[pts.Count - 1][1];
        }

        static void AddResource(Evaluation ev, ResourceId id, double used, double capacity, string unit,
            double margin, ResourceStatus? status = null)
        {
            if (double.IsNaN(margin) || double.IsInfinity(margin)) margin = -1;
            ev.Resources[id] = new ResourceReport
            {
                Id = id,
                Used = used,
                Capacity = capacity,
                Unit = unit,
                Margin = margin,
                Status = status ?? (margin < 0 ? ResourceStatus.Over : margin < TightMargin ? ResourceStatus.Tight : ResourceStatus.Ok),
            };
        }

        static void BuildWarnings(Evaluation ev, Design design, ScenarioDef scenario,
            List<PartDef> instruments, string objective)
        {
            foreach (var r in ev.Resources.Values.Where(r => r.Status != ResourceStatus.Ok).OrderBy(r => r.Id))
            {
                var w = new Warning { Resource = r.Id, Severity = r.Status };
                w.Values["used"] = r.Used;
                w.Values["capacity"] = r.Capacity;
                w.Values["margin_pct"] = r.Margin * 100;
                switch (r.Id)
                {
                    case ResourceId.Mass:
                        w.Code = ev.Mass.OrbitReachable ? "warn.mass" : "warn.orbit_unreachable";
                        w.Values["over"] = r.Used - r.Capacity;
                        break;
                    case ResourceId.Power:
                        bool batteryWorse = ev.Power.BatteryMargin < ev.Power.ArrayMargin;
                        w.Code = batteryWorse ? "warn.battery" : "warn.power";
                        w.Values["short_w"] = ev.Power.RequiredArrayW - ev.Power.ArrayEolW;
                        w.Values["short_wh"] = ev.Power.BatteryRequiredWh - ev.Power.BatteryWh;
                        w.Values["eclipse_min"] = ev.Orbit.EclipseMin;
                        break;
                    case ResourceId.Budget:
                        w.Code = "warn.budget";
                        w.Values["over"] = r.Used - r.Capacity;
                        break;
                    case ResourceId.Data:
                        w.Code = r.Status == ResourceStatus.Over ? "warn.storage" : "warn.storage_tight";
                        w.Values["raw"] = ev.Data.StorageRawGb;
                        w.Values["return_pct"] = ev.Data.ReturnFraction * 100;
                        break;
                    case ResourceId.Comms:
                        w.Code = "warn.downlink";
                        w.Values["return_pct"] = ev.Data.ReturnFraction * 100;
                        w.Values["lost_pct"] = (1 - ev.Data.ReturnFraction) * 100;
                        break;
                }
                ev.Warnings.Add(w);
            }

            if (ev.Lifetime.Years < design.MissionYears)
            {
                var w = new Warning { Code = "warn.lifetime." + ev.Lifetime.Limit.ToString().ToLowerInvariant(), Severity = ResourceStatus.Over };
                w.Values["years"] = ev.Lifetime.Years;
                w.Values["target"] = design.MissionYears;
                ev.Warnings.Add(w);
            }
            if (instruments.Count == 0)
                ev.Warnings.Add(new Warning { Code = "warn.no_instrument", Severity = ResourceStatus.Over });
            foreach (var i in instruments.Where(i => Match(i, objective) <= 0))
            {
                var w = new Warning { Code = "warn.instrument_mismatch", Severity = ResourceStatus.Tight };
                w.Values["index"] = design.Instruments.IndexOf(i.Id);
                ev.Warnings.Add(w);
            }
            if (scenario != null && instruments.Count > scenario.MaxInstruments)
                ev.Warnings.Add(new Warning { Code = "warn.too_many_instruments", Severity = ResourceStatus.Over });
            if (scenario != null && scenario.LatencyLimitMin > 0 && ev.Fire.LatencyMin > scenario.LatencyLimitMin)
            {
                var w = new Warning { Code = "warn.latency", Severity = ResourceStatus.Over };
                w.Values["latency"] = ev.Fire.LatencyMin;
                w.Values["limit"] = scenario.LatencyLimitMin;
                w.Values["wait"] = ev.Fire.WaitMin;
                w.Values["downlink"] = ev.Fire.DownlinkMin;
                ev.Warnings.Add(w);
            }
        }
    }
}
