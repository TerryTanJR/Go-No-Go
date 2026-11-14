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
        /// <summary>零件上印的編號（例如 X-2）。組裝區只看得到編號，規格要查中控室的手冊。</summary>
        public string Code { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        /// <summary>一句白話功能，組裝區看零件時顯示（不含任何數值）。</summary>
        public LocalizedText Function { get; set; } = new LocalizedText();
        public LocalizedText Description { get; set; } = new LocalizedText();
        /// <summary>採購終端的科普說明：這東西怎麼運作、真實任務怎麼用它。不含數字（數字在中控室的型錄）。</summary>
        public LocalizedText Explainer { get; set; } = new LocalizedText();

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
        /// <summary>野火偵測率（0–1）：同一艘船上的儀器相加，上限 1。只有野火關卡用到。</summary>
        public double FireDetection { get; set; }

        /// <summary>只在這些情境的零件架上出現；空白 = 所有情境。</summary>
        public List<string> Scenarios { get; set; } = new List<string>();

        /// <summary>
        /// 誘餌零件：聽起來有用、其實不適合（或只在特定關卡有用）。不受難度等級限制，
        /// 只在難度的 <see cref="DifficultyDef.DecoyCategories"/> 有這個類別時上架；修正建議不會推薦它。
        /// </summary>
        public bool Decoy { get; set; }

        // 太陽能板
        public double AreaM2 { get; set; }
        /// <summary>每平方公尺發電量相對於太空用三接面電池的倍率（地面用矽晶電池較低）。</summary>
        public double CellFactor { get; set; } = 1.0;

        // 電池
        public double CapacityWh { get; set; }

        // 天線
        public double DownlinkMbps { get; set; }
        public double TxPowerW { get; set; }
        /// <summary>過站時間裡真的連得上的比例（雷射通訊會被雲擋、要專用光學地面站）。</summary>
        public double LinkAvailability { get; set; } = 1.0;

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
        /// <summary>共乘：只能去主酬載的這條軌道（空白 = 沒有限制）。中控室選不到別的軌道。</summary>
        public string OrbitLock { get; set; } = "";
        public SourceRef Source { get; set; } = new SourceRef();
        /// <summary>真實的飛行時間表（發射演出用）。</summary>
        public FlightProfileDef Flight { get; set; } = new FlightProfileDef();
    }

    /// <summary>一個飛行事件：幾秒（T+）發生什麼。id 見 <see cref="LaunchTimeline"/>。</summary>
    public sealed class FlightEventDef
    {
        public string Id { get; set; } = "";
        public double T { get; set; }
        /// <summary>播報用的字串鍵（空白 = 依 id 的預設播報）。</summary>
        public string Call { get; set; } = "";
    }

    /// <summary>
    /// 火箭的飛行時間表：爬升段（到第一次關機 SECO-1 為止，各軌道共用），
    /// 以及依目標軌道的後半段（滑行、再點火、踢昇級、衛星分離）。數字取自真實任務的公開時間表。
    /// </summary>
    public sealed class FlightProfileDef
    {
        public List<FlightEventDef> Ascent { get; set; } = new List<FlightEventDef>();
        /// <summary>軌道 id → 入軌後的事件；沒有列出的軌道用 default。</summary>
        public Dictionary<string, List<FlightEventDef>> Orbits { get; set; } = new Dictionary<string, List<FlightEventDef>>();
        /// <summary>火箭的節數（不含助推器），演出用：Taurus XL 4 節、Electron 2 節 + 踢昇級算 3。</summary>
        public int Stages { get; set; } = 2;
        public List<SourceRef> Sources { get; set; } = new List<SourceRef>();
    }

    public sealed class RequirementDef
    {
        /// <summary>情境內唯一的 id（電話問到後記在筆記板上）。</summary>
        public string Id { get; set; } = "";
        /// <summary>
        /// budget_reserve、lifetime_target、mass_power_margin、science_threshold、instrument_required、data_return；
        /// 野火關卡另有 instrument_any（part_ids 任一台）、orbit_required（orbit_id）、battery_eclipse（撐過地影）、latency（通報延遲 ≤ value 分鐘）。
        /// </summary>
        public string Type { get; set; } = "";
        /// <summary>0 = 一定有；n = 和其他處室衝突的需求，難度的 requirement_conflicts ≥ n 才出現。</summary>
        public int ConflictGroup { get; set; }
        public double Value { get; set; }
        public string PartId { get; set; } = "";
        /// <summary>instrument_any：這些儀器裝了任一台就算達成。</summary>
        public List<string> PartIds { get; set; } = new List<string>();
        /// <summary>orbit_required：要選這條軌道。</summary>
        public string OrbitId { get; set; } = "";
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
        /// <summary>處室 key（mission_lead、budget_office、science、engineering、comms）→ 這個處室的需求。</summary>
        public Dictionary<string, List<RequirementDef>> Requirements { get; set; } = new Dictionary<string, List<RequirementDef>>();
        public Design DefaultDesign { get; set; } = new Design();
        public List<string> ComparisonMissions { get; set; } = new List<string>();
        public HistoricalDef Historical { get; set; }
        /// <summary>這一關固定的難度 id（一關一個難度，玩家不另外選）。空白 = 基準難度（工程師）。</summary>
        public string Difficulty { get; set; } = "";

        // ---- 關卡專屬的設定（沒寫就照難度的通用規則）----

        /// <summary>難度 id（或 "*"）→ 零件架上的零件 id。沒寫 = 依難度等級的通用零件架。</summary>
        public Dictionary<string, List<string>> Shelf { get; set; } = new Dictionary<string, List<string>>();
        /// <summary>難度 id → 開局設計（取代 default_design）。</summary>
        public Dictionary<string, Design> DefaultDesigns { get; set; } = new Dictionary<string, Design>();
        /// <summary>難度 id → 營運事件種類（取代難度的 event_types）。</summary>
        public Dictionary<string, List<string>> EventTypes { get; set; } = new Dictionary<string, List<string>>();
        /// <summary>處室 id → 這個關卡的對話（取代 departments.json 的通用對話）。</summary>
        public Dictionary<string, List<DialogueLine>> Dialogue { get; set; } = new Dictionary<string, List<DialogueLine>>();
        /// <summary>科學成果的算法：空白 = 通用（科學點數）；fire_alert = 偵測率 × 及時通報比例 × 工作比例。</summary>
        public string ScienceModel { get; set; } = "";
        /// <summary>火點通報時限（分鐘）。大於 0 時儀表板顯示「通報延遲」。</summary>
        public double LatencyLimitMin { get; set; }

        public bool FireAlert => ScienceModel == "fire_alert";
    }

    public sealed class DifficultyDef
    {
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        public LocalizedText Description { get; set; } = new LocalizedText();
        /// <summary>不出現在選單（新手訓練用的難度）。</summary>
        public bool Hidden { get; set; }
        /// <summary>工程部要求的設計餘裕（質量與電力）。</summary>
        public double RequiredMargin { get; set; } = 0.15;
        public int TiersPerCategory { get; set; } = 3;
        /// <summary>哪些類別的誘餌零件會上架（學員只有儀器，越難類別越多）。</summary>
        public List<string> DecoyCategories { get; set; } = new List<string>();
        /// <summary>儀表板加上紅黃綠（學員）。</summary>
        public bool DashboardColors { get; set; }
        /// <summary>
        /// 儀表板、任務目標只列會判定成敗的項目（學員、新手訓練）：不列地影、科學值、資料回傳、偵測率這些參考數字，
        /// 經費也不拆零件／發射／營運細項。
        /// </summary>
        public bool DashboardBasic { get; set; }
        /// <summary>筆記板：checklist（整理成發射條件清單）、transcript（記下對話原文）、none（不自動記錄）。</summary>
        public string NotesMode { get; set; } = "transcript";
        /// <summary>開放到第幾組互相衝突的處室需求。</summary>
        public int RequirementConflicts { get; set; }
        /// <summary>失敗原因看板：advice（原因 + 因果鏈 + 建議）、chain（原因 + 因果鏈）、category（只寫大類）。</summary>
        public string FailureDetail { get; set; } = "chain";
        /// <summary>第一次發射失敗不影響評價。</summary>
        public bool FreeFirstFailure { get; set; }
        /// <summary>營運事件直接顯示名稱（不用自己判讀）。</summary>
        public bool EventReveal { get; set; }
        /// <summary>營運回合數（每回合一個事件）。</summary>
        public int Turns { get; set; } = 6;
        /// <summary>會出現的事件種類：solar_storm、part_failure、science_opportunity、power_anomaly。</summary>
        public List<string> EventTypes { get; set; } = new List<string>();
        /// <summary>每回合時間限制（秒），0 = 無限制。</summary>
        public int TurnSeconds { get; set; }
        /// <summary>發射一定成功（新手訓練）。</summary>
        public bool GuaranteedLaunch { get; set; }
        /// <summary>開局時換掉情境預設設計的零件（類別 → 零件 id）。歷史關卡不套用（當年的設計不能改）。</summary>
        public Dictionary<string, string> StartParts { get; set; } = new Dictionary<string, string>();
        /// <summary>營運時每個多餘操作從那一回合的營運分扣掉的分數（每回合下限 0）。讓「全部按下去」不再是最佳解。</summary>
        public double ExtraActionPenalty { get; set; } = 10;
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

    /// <summary>中控室電話可以撥的處室（departments.json）。</summary>
    public sealed class DepartmentDef
    {
        /// <summary>mission_lead、budget_office、science、engineering、comms。</summary>
        public string Id { get; set; } = "";
        public LocalizedText Name { get; set; } = new LocalizedText();
        /// <summary>接電話的人（虛構角色）。</summary>
        public LocalizedText Contact { get; set; } = new LocalizedText();
        public LocalizedText Greeting { get; set; } = new LocalizedText();
        public string Color { get; set; } = "";
        public List<DialogueLine> Lines { get; set; } = new List<DialogueLine>();
    }

    /// <summary>
    /// 對話的一個問題。回答可以用 {budget}、{reserve_pct}、{years}… 這類參數（GameState 代入）；
    /// reveals 列出這題會說出的需求種類，回答後面接上該處室這些需求的文字，並記到筆記板。
    /// </summary>
    public sealed class DialogueLine
    {
        public string Id { get; set; } = "";
        public LocalizedText Question { get; set; } = new LocalizedText();
        public LocalizedText Answer { get; set; } = new LocalizedText();
        /// <summary>這個處室在這個情境沒有 reveals 列的需求時，改用這段回答。</summary>
        public LocalizedText AnswerNone { get; set; } = new LocalizedText();
        public List<string> Reveals { get; set; } = new List<string>();
        /// <summary>難度的 requirement_conflicts 至少這麼多才問得到（例如工程部推晨昏軌道只在工程師以上）。</summary>
        public int MinConflicts { get; set; }
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
        /// <summary>火點資料落地後，地面處理、確認、通知消防單位的時間（分鐘）。</summary>
        public double GroundProcessingMin { get; set; } = 5;
        /// <summary>軌道上的科學分數換算：每年每點科學值。</summary>
        public double ScienceYearScale { get; set; } = 1.0;

        public Dictionary<string, SourceRef> Sources { get; set; } = new Dictionary<string, SourceRef>();
    }
}
