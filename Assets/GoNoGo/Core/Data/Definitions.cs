using System.Collections.Generic;
using Newtonsoft.Json;

namespace GoNoGo.Core
{
    /// <summary>中英雙語字串。JSON 鍵為 "en" 與 "zh-TW"。</summary>
    public sealed class LocalizedText
    {
        [JsonProperty("en")] public string En { get; set; } = "";
        [JsonProperty("zh-TW")] public string ZhTW { get; set; } = "";

        public LocalizedText() { }
        public LocalizedText(string en, string zhTW) { En = en; ZhTW = zhTW; }

        public string Get(string lang) =>
            lang == Lang.ZhTW && !string.IsNullOrEmpty(ZhTW) ? ZhTW : En;

        public override string ToString() => En;
    }

    public static class Lang
    {
        public const string En = "en";
        public const string ZhTW = "zh-TW";
    }

    /// <summary>每筆數值的出處，科學有效性評分要用。</summary>
    public sealed class SourceRef
    {
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
        public string Note { get; set; } = "";
    }

    public static class PartCategory
    {
        public const string Instrument = "instrument";
        public const string Solar = "solar";
        public const string Battery = "battery";
        public const string Antenna = "antenna";
        public const string GroundStation = "groundstation";
        public const string Computer = "computer";
        public const string Adcs = "adcs";
        public const string Propulsion = "propulsion";
        public const string Shielding = "shielding";

        /// <summary>每類只能選一個的零件類別（儀器可以多選）。</summary>
        public static readonly string[] Single =
        {
            Solar, Battery, Antenna, GroundStation, Computer, Adcs, Propulsion, Shielding
        };

        public static readonly string[] All =
        {
            Instrument, Solar, Battery, Antenna, GroundStation, Computer, Adcs, Propulsion, Shielding
        };
    }

    /// <summary>
    /// 一個零件。不同類別用到的欄位不同，沒用到的保持 0。
    /// 單位：kg、W、百萬美元、月、GB/天、Mbps。
    /// </summary>
    public sealed class PartDef
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = "";
        public int Tier { get; set; }
        public LocalizedText Name { get; set; } = new LocalizedText();
        public LocalizedText Description { get; set; } = new LocalizedText();

        // 共通
        public double MassKg { get; set; }
        /// <summary>運作時的平均耗電（W）。天線的發射耗電另見 TxPowerW。</summary>
        public double PowerW { get; set; }
        public double CostMusd { get; set; }
        public double DevMonths { get; set; }
        /// <summary>每年不故障的機率（0–1）。</summary>
        public double ReliabilityPerYear { get; set; } = 0.99;
        /// <summary>有備援（雙套系統）：故障時自動切換，不影響任務。</summary>
        public bool Redundant { get; set; }

        // 儀器
        public double DataGbPerDay { get; set; }
        public double ScienceValue { get; set; }
        public double ResolutionM { get; set; }
        /// <summary>儀器只在日照段運作（光學儀器）。</summary>
        public bool SunlitOnly { get; set; }
        /// <summary>任務目標 → 吻合度（0–1）。</summary>
        public Dictionary<string, double> ObjectiveMatch { get; set; } = new Dictionary<string, double>();

        // 太陽能板
        public double AreaM2 { get; set; }

        // 電池
        public double CapacityWh { get; set; }

        // 天線
        public double DownlinkMbps { get; set; }
        public double TxPowerW { get; set; }

        // 地面站
        public int Stations { get; set; }
        public double OpsCostMusdPerYear { get; set; }

        // 電腦
        public double StorageGb { get; set; }
        public double RadToleranceKrad { get; set; }

        // 推進
        public double PropellantKg { get; set; }
        public double IspS { get; set; }

        // 輻射屏蔽：相對於 2.5 mm 鋁的劑量倍率
        public double ShieldingMm { get; set; }
        public double DoseFactor { get; set; } = 1.0;

        // 姿態控制
        public double PointingArcsec { get; set; }

        // 畫面
        public string Model { get; set; } = "";
        public string Mount { get; set; } = "";
        public string Color { get; set; } = "";

        public SourceRef Source { get; set; } = new SourceRef();
    }

    public sealed class OrbitDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public LocalizedText Description { get; set; } = new LocalizedText();
        /// <summary>"earth" 或 "moon"。</summary>
        public string Body { get; set; } = "earth";
        public double AltitudeKm { get; set; }
        /// <summary>軌道面和太陽方向的夾角；0° 是影子最長的最壞情況。</summary>
        public double BetaDeg { get; set; }
        /// <summary>每年維持軌道所需的 Δv（m/s）。</summary>
        public double DeltaVPerYearMs { get; set; }
        /// <summary>沒有推進劑時，還能維持任務可用的年數（自然衰減或漂移）。</summary>
        public double NaturalServiceYears { get; set; }
        /// <summary>2.5 mm 鋁屏蔽後的年劑量 krad(Si)。</summary>
        public double DoseKradPerYear { get; set; }
        /// <summary>電池允許的放電深度（循環次數越多越要淺放）。</summary>
        public double BatteryDod { get; set; }
        /// <summary>每個地面站每天的過境次數。</summary>
        public double PassesPerDayPerStation { get; set; }
        public double PassMinutes { get; set; }
        /// <summary>大於 0 時代表通聯時間佔一天的比例（地球同步、深空網），不看地面站數。</summary>
        public double ContactFraction { get; set; }
        /// <summary>距離造成的下傳速率倍率（自由空間損耗，已計入地面大天線的補償）。</summary>
        public double DownlinkRateFactor { get; set; } = 1.0;
        /// <summary>每年進入地球影子的次數上限（GEO 只有春秋分附近會進影子）。0 = 每圈都進。</summary>
        public double EclipsesPerYearOverride { get; set; }
        public Dictionary<string, double> ObjectiveMatch { get; set; } = new Dictionary<string, double>();
        public string Color { get; set; } = "";
        public SourceRef Source { get; set; } = new SourceRef();
    }

    public sealed class LauncherDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public LocalizedText Description { get; set; } = new LocalizedText();
        public int Tier { get; set; }
        /// <summary>軌道 id → 運力（kg）。沒有的軌道代表到不了。</summary>
        public Dictionary<string, double> CapacityKg { get; set; } = new Dictionary<string, double>();
        public double CostMusd { get; set; }
        public double Reliability { get; set; }
        public double FairingDiameterM { get; set; }
        public double LeadMonths { get; set; }
        /// <summary>歷史火箭只在歷史任務關卡出現。</summary>
        public bool Historical { get; set; }
        public string FlightRecord { get; set; } = "";
        public string Model { get; set; } = "";
        public SourceRef Source { get; set; } = new SourceRef();
    }

    public sealed class RequirementDef
    {
        /// <summary>budget_reserve、lifetime_target、mass_power_margin、science_threshold、instrument_required、data_return。</summary>
        public string Type { get; set; } = "";
        public double Value { get; set; }
        public string PartId { get; set; } = "";
        public LocalizedText Text { get; set; } = new LocalizedText();
    }

    public sealed class HistoricalDef
    {
        public int Year { get; set; }
        public Design PresetDesign { get; set; } = new Design();
        /// <summary>重演歷史時強制發生的事件，例如 fairing_stuck。</summary>
        public List<string> ScriptedEvents { get; set; } = new List<string>();
        /// <summary>只有用這支火箭時才會觸發歷史事件。</summary>
        public string ScriptedLauncherId { get; set; } = "";
        public double BudgetCap { get; set; } = 1.1;
        public string Cinematic { get; set; } = "";
        public LocalizedText Background { get; set; } = new LocalizedText();
        public LocalizedText Reveal { get; set; } = new LocalizedText();
        public LocalizedText RealFix { get; set; } = new LocalizedText();
        public List<SourceRef> Sources { get; set; } = new List<SourceRef>();
    }

    public sealed class ScenarioDef
    {
        public string Id { get; set; } = "";
        public string Objective { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public LocalizedText Briefing { get; set; } = new LocalizedText();
        public double BudgetMusd { get; set; }
        public double TargetYears { get; set; }
        public double DeadlineMonths { get; set; }
        public int MaxInstruments { get; set; } = 3;
        public List<string> AllowedOrbits { get; set; } = new List<string>();
        /// <summary>空白代表所有非歷史火箭都可用。</summary>
        public List<string> AllowedLaunchers { get; set; } = new List<string>();
        /// <summary>科學分數 100 分對應的科學點數。</summary>
        public double ScienceReference { get; set; } = 100;
        /// <summary>角色 → 可抽的私有目標（部門要求）。</summary>
        public Dictionary<string, List<RequirementDef>> Requirements { get; set; } = new Dictionary<string, List<RequirementDef>>();
        public Design DefaultDesign { get; set; } = new Design();
        public List<string> ComparisonMissions { get; set; } = new List<string>();
        public HistoricalDef Historical { get; set; }
    }

    public sealed class DifficultyDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public double BudgetMultiplier { get; set; } = 1.0;
        /// <summary>部門要求的設計餘裕（工程師的質量與電力）。</summary>
        public double RequiredMargin { get; set; } = 0.15;
        public int EventsMin { get; set; } = 3;
        public int EventsMax { get; set; } = 4;
        /// <summary>fixes：直接給兩個解法；reasons：只說原因；lights：只顯示紅燈。</summary>
        public string HintLevel { get; set; } = "reasons";
        public List<string> VisibleResources { get; set; } = new List<string>();
        public int TiersPerCategory { get; set; } = 3;
    }

    /// <summary>NASA DONKI 的歷史太空天氣事件（賽前抓的快照）。</summary>
    public sealed class SpaceWeatherEvent
    {
        public string Id { get; set; } = "";
        /// <summary>FLR（閃焰）、CME（日冕巨量噴發）、GST（地磁暴）。</summary>
        public string Type { get; set; } = "";
        public string Date { get; set; } = "";
        public string ClassType { get; set; } = "";
        public double KpIndex { get; set; }
        public double SpeedKms { get; set; }
        /// <summary>1–3，影響事件傷害程度。</summary>
        public int Severity { get; set; } = 1;
        public string Link { get; set; } = "";
        public LocalizedText Summary { get; set; } = new LocalizedText();
    }

    /// <summary>報告頁「對照真實任務」的卡片。</summary>
    public sealed class MissionCard
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public int LaunchYear { get; set; }
        public double MassKg { get; set; }
        public double PowerW { get; set; }
        public double CostMusd { get; set; }
        public string Orbit { get; set; } = "";
        public LocalizedText Instruments { get; set; } = new LocalizedText();
        public LocalizedText Lesson { get; set; } = new LocalizedText();
        public double LifetimeYears { get; set; }
        public string Url { get; set; } = "";
    }

    /// <summary>公式用到的常數，全部放在資料檔並附來源。</summary>
    public sealed class Constants
    {
        public double EarthMuKm3S2 { get; set; } = 398600.4418;
        public double EarthRadiusKm { get; set; } = 6378.137;
        public double MoonMuKm3S2 { get; set; } = 4902.800066;
        public double MoonRadiusKm { get; set; } = 1737.4;
        public double G0Ms2 { get; set; } = 9.80665;

        /// <summary>太陽能板陣列層級的比功率（W/m²，BOL，已含封裝與指向損失）。</summary>
        public double SolarArrayWPerM2 { get; set; } = 250;
        public double SolarDegradationPerYear { get; set; } = 0.0275;
        /// <summary>SMAD：影子段經電池供電的路徑效率。</summary>
        public double PathEfficiencyEclipse { get; set; } = 0.6;
        /// <summary>SMAD：日照段直接供電的路徑效率。</summary>
        public double PathEfficiencyDaylight { get; set; } = 0.8;
        public double BatteryEfficiency { get; set; } = 0.9;
        /// <summary>放電深度 → 循環壽命的對照點（線性內插）。</summary>
        public List<double[]> BatteryCycleLife { get; set; } = new List<double[]>();

        /// <summary>結構與線材佔乾質量的比例。</summary>
        public double StructureFraction { get; set; } = 0.22;
        public double BusBasePowerW { get; set; } = 25;
        public double BusBaseCostMusd { get; set; } = 8;
        public double OpsBaseCostMusdPerYear { get; set; } = 1.5;
        public double IntegrationMonths { get; set; } = 8;
        public double LinkEfficiency { get; set; } = 0.8;
        /// <summary>過境分布不均：最長空檔 = 平均空檔 × 這個倍數。</summary>
        public double ContactGapFactor { get; set; } = 2.0;
        public double StorageSafetyFactor { get; set; } = 1.5;
        /// <summary>軌道上的科學分數換算：每年每點科學值。</summary>
        public double ScienceYearScale { get; set; } = 1.0;

        public Dictionary<string, SourceRef> Sources { get; set; } = new Dictionary<string, SourceRef>();
    }
}
