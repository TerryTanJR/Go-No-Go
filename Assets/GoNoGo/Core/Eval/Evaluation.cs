using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    public enum ResourceId { Mass, Power, Budget, Data, Comms, Time }

    public enum ResourceStatus { Ok, Tight, Over }

    /// <summary>一個零件對某項資源的貢獻量（滑鼠移上去時顯示）。</summary>
    public sealed class Contribution
    {
        public string Key { get; set; }
        public double Amount { get; set; }
        public Contribution(string key, double amount) { Key = key; Amount = amount; }
    }

    /// <summary>六條資源條之一。Margin = (Capacity − Used) / Capacity。</summary>
    public sealed class ResourceReport
    {
        public ResourceId Id { get; set; }
        public double Used { get; set; }
        public double Capacity { get; set; }
        public string Unit { get; set; } = "";
        public double Margin { get; set; }
        public ResourceStatus Status { get; set; }
        public List<Contribution> Contributions { get; set; } = new List<Contribution>();

        /// <summary>
        /// 資源條長度（0–1 以上）= 1 − Margin。電力的 Margin 取太陽能板和電池中較差的一個，
        /// 所以不直接用 Used / Capacity。
        /// </summary>
        public double Fill => System.Math.Max(0.0, 1.0 - Margin);
    }

    public sealed class OrbitInfo
    {
        public double PeriodMin { get; set; }
        public double EclipseMin { get; set; }
        public double SunlitMin { get; set; }
        public double EclipseFraction { get; set; }
        public double OrbitsPerDay { get; set; }
        public double EclipsesPerYear { get; set; }
        public double ContactMinPerDay { get; set; }
        public double ContactsPerDay { get; set; }
    }

    public sealed class PowerInfo
    {
        public double LoadW { get; set; }
        public double RequiredArrayW { get; set; }
        public double ArrayBolW { get; set; }
        public double ArrayEolW { get; set; }
        public double EclipseEnergyWh { get; set; }
        public double BatteryRequiredWh { get; set; }
        public double BatteryWh { get; set; }
        public double AllowedDod { get; set; }
        /// <summary>實際每圈放電深度。</summary>
        public double ActualDod { get; set; }
        public double ArrayMargin { get; set; }
        public double BatteryMargin { get; set; }
    }

    public sealed class MassInfo
    {
        public double PartsKg { get; set; }
        public double StructureKg { get; set; }
        public double DryKg { get; set; }
        public double PropellantKg { get; set; }
        public double WetKg { get; set; }
        public double LauncherCapacityKg { get; set; }
        public bool OrbitReachable { get; set; }
    }

    public sealed class DataInfo
    {
        public double GeneratedGbPerDay { get; set; }
        public double DownlinkGbPerDay { get; set; }
        public double StorageNeededGb { get; set; }
        public double StorageGb { get; set; }
        /// <summary>預估資料傳回率（0–1）。</summary>
        public double ReturnFraction { get; set; }
    }

    public sealed class CostInfo
    {
        public double DevelopmentMusd { get; set; }
        public double LaunchMusd { get; set; }
        public double OpsPerYearMusd { get; set; }
        public double OpsTotalMusd { get; set; }
        public double TotalMusd { get; set; }
        public double BudgetMusd { get; set; }
    }

    public enum LifetimeLimit { None, Propellant, Battery, Radiation }

    public sealed class LifetimeInfo
    {
        public double PropellantYears { get; set; }
        public double BatteryYears { get; set; }
        public double RadiationYears { get; set; }
        public double DeltaVAvailableMs { get; set; }
        public double Years { get; set; }
        public LifetimeLimit Limit { get; set; }
        public double TargetYears { get; set; }
    }

    public enum FixKind { SwapPart, RemoveInstrument, ChangeOrbit, ChangeLauncher, ChangeYears }

    /// <summary>電腦算出來、保證有效的解法。</summary>
    public sealed class Fix
    {
        public FixKind Kind { get; set; }
        public string Category { get; set; } = "";
        public string FromId { get; set; } = "";
        public string ToId { get; set; } = "";
        public int Years { get; set; }
        /// <summary>套用後這項資源的餘裕。</summary>
        public double NewMargin { get; set; }
        public double CostDeltaMusd { get; set; }
        public double ScienceDelta { get; set; }

        public Design ApplyTo(Design d)
        {
            var c = d.Clone();
            switch (Kind)
            {
                case FixKind.SwapPart:
                    if (Category == PartCategory.Instrument)
                    {
                        int i = c.Instruments.IndexOf(FromId);
                        if (i >= 0) c.Instruments[i] = ToId;
                    }
                    else c.Parts[Category] = ToId;
                    break;
                case FixKind.RemoveInstrument: c.Instruments.Remove(FromId); break;
                case FixKind.ChangeOrbit: c.OrbitId = ToId; break;
                case FixKind.ChangeLauncher: c.LauncherId = ToId; break;
                case FixKind.ChangeYears: c.MissionYears = Years; break;
            }
            return c;
        }
    }

    public sealed class Warning
    {
        /// <summary>對應的資源條；壽命、科學等非資源條的警告為 null。</summary>
        public ResourceId? Resource { get; set; }
        /// <summary>多語字串鍵，例如 warn.power_array。</summary>
        public string Code { get; set; } = "";
        public ResourceStatus Severity { get; set; }
        public Dictionary<string, double> Values { get; set; } = new Dictionary<string, double>();
        public List<Fix> Fixes { get; set; } = new List<Fix>();
    }

    /// <summary>EvaluateDesign 的完整輸出。畫面只讀這個，不自己算。</summary>
    public sealed class Evaluation
    {
        public Dictionary<ResourceId, ResourceReport> Resources { get; set; } = new Dictionary<ResourceId, ResourceReport>();
        public OrbitInfo Orbit { get; set; } = new OrbitInfo();
        public PowerInfo Power { get; set; } = new PowerInfo();
        public MassInfo Mass { get; set; } = new MassInfo();
        public DataInfo Data { get; set; } = new DataInfo();
        public CostInfo Cost { get; set; } = new CostInfo();
        public LifetimeInfo Lifetime { get; set; } = new LifetimeInfo();
        /// <summary>每年可得的科學點數（假設資料全部傳回）。</summary>
        public double SciencePerYear { get; set; }
        public double ScheduleMonths { get; set; }
        public List<Warning> Warnings { get; set; } = new List<Warning>();
        public List<string> DesignErrors { get; set; } = new List<string>();

        public bool AnyOver => Resources.Values.Any(r => r.Status == ResourceStatus.Over);
        public bool LaunchFeasible => Mass.OrbitReachable && Resources[ResourceId.Mass].Status != ResourceStatus.Over;
        public ResourceReport this[ResourceId id] => Resources[id];
    }
}
