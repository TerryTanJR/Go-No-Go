using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// DeadOnOrbit：入軌、部署都正常，但電池撐不過第一次地影就斷電失聯（算失敗）。
    /// 編號跟著整包狀態傳給其他人，不能改（4 = 已刪除的 NotLaunched）。
    /// </summary>
    public enum LaunchOutcome { Nominal = 0, LowerOrbit = 1, Failure = 2, FairingFailure = 3, DeadOnOrbit = 5 }

    public sealed class LaunchResult
    {
        public LaunchOutcome Outcome { get; set; }
        /// <summary>多語鍵，說明為什麼是這個結果。</summary>
        public string ReasonCode { get; set; } = "";
        public double MassMargin { get; set; }
        public bool Success => Outcome == LaunchOutcome.Nominal || Outcome == LaunchOutcome.LowerOrbit;
        /// <summary>太空船有被送進軌道並部署（發射演出要播到展開太陽能板）。</summary>
        public bool Deployed => Success || Outcome == LaunchOutcome.DeadOnOrbit;
    }

    /// <summary>BigFire、FalseFire 只出現在野火關卡（由關卡的 event_types 指定）。</summary>
    public enum EventKind { SolarStorm, PartFailure, ScienceOpportunity, PowerAnomaly, BigFire, FalseFire, Alien }

    /// <summary>營運期間的事件（每回合一個）。亂數在規劃時就擲好，所以玩家的處置不會改變後面的事件。</summary>
    public sealed class MissionEvent
    {
        public string Id { get; set; } = "";
        public EventKind Kind { get; set; }
        /// <summary>第幾回合（0 起算）。</summary>
        public int Turn { get; set; }
        public int Day { get; set; }
        /// <summary>太陽風暴：DONKI 事件 id；零件故障：零件 id；科學機會：機會種類；電量異常：空白；大火／假火點：地點（forest、grassland、desert、glint）。</summary>
        public string RefId { get; set; } = "";
        public int Severity { get; set; } = 1;
        public double Roll { get; set; }
        public double Roll2 { get; set; }
    }

    public sealed class MissionPlan
    {
        public int Seed { get; set; }
        public double LaunchRoll { get; set; }
        public double LaunchRoll2 { get; set; }
        public int Turns { get; set; }
        /// <summary>每回合開始的任務天數（長度 = Turns + 1，最後一個是任務結束）。</summary>
        public List<int> TurnStartDays { get; set; } = new List<int>();
        public List<MissionEvent> Events { get; set; } = new List<MissionEvent>();
    }

    /// <summary>玩家對一個事件的回應：判斷成哪一種事件、在控制台做了哪些操作。</summary>
    public sealed class EventResponse
    {
        /// <summary>判讀結果（事件 key，例如 solar_storm），空白 = 沒判斷。</summary>
        public string Judged { get; set; } = "";
        /// <summary>做過的操作（OpsActions 的 id），依時間順序。</summary>
        public List<string> Actions { get; set; } = new List<string>();
        /// <summary>小遊戲成績：玩家編號 → 分數（0 ~ 1000）。只有交出成績的人才有。</summary>
        public Dictionary<int, int> Mini { get; set; } = new Dictionary<int, int>();
        /// <summary>全隊小遊戲有沒有過（結算時才有意義）。過了就等於做完手冊上的全部步驟。</summary>
        public bool MiniPassed { get; set; }

        /// <summary>有人動手處理了：判斷過、做過操作，或有人交了小遊戲成績。</summary>
        [Newtonsoft.Json.JsonIgnore]
        public bool Started => !string.IsNullOrEmpty(Judged) || Actions.Count > 0 || Mini.Count > 0;

        public EventResponse Clone() => new EventResponse
        {
            Judged = Judged,
            Actions = new List<string>(Actions),
            Mini = new Dictionary<int, int>(Mini),
            MiniPassed = MiniPassed,
        };
    }

    public interface IDecisionSource
    {
        EventResponse Respond(MissionEvent e);
    }

    /// <summary>用事先記錄的回應；沒記錄的事件視為沒處理。</summary>
    public sealed class FixedDecisions : IDecisionSource
    {
        readonly IDictionary<string, EventResponse> _map;
        public FixedDecisions(IDictionary<string, EventResponse> map = null) { _map = map ?? new Dictionary<string, EventResponse>(); }
        public EventResponse Respond(MissionEvent e) => _map.TryGetValue(e.Id, out var r) && r != null ? r : new EventResponse();
    }

    /// <summary>每個事件都判斷正確、照處置手冊做完（平衡測試與「略過到結尾」的參考線）。</summary>
    public sealed class PerfectDecisions : IDecisionSource
    {
        public EventResponse Respond(MissionEvent e) => new EventResponse
        {
            Judged = OpsEvents.Key(e.Kind),
            Actions = new List<string>(Procedures.Required(e.Kind)),
        };
    }

    /// <summary>完全不處理任何事件。</summary>
    public sealed class NoDecisions : IDecisionSource
    {
        public EventResponse Respond(MissionEvent e) => new EventResponse();
    }

    public sealed class Sample
    {
        public int Day { get; set; }
        public double BatterySoc { get; set; }
        public double StorageFill { get; set; }
        public double DataReturnedGb { get; set; }
        public double PropellantKg { get; set; }
        public double Science { get; set; }
        public double ArrayW { get; set; }
        public bool SafeMode { get; set; }
    }

    public sealed class EventOutcome
    {
        public MissionEvent Event { get; set; }
        public string Judged { get; set; } = "";
        public bool JudgedCorrect { get; set; }
        /// <summary>處置手冊要求的操作數、做到的數量。</summary>
        public int StepsRequired { get; set; }
        public int StepsDone { get; set; }
        /// <summary>做了但這個事件不需要的操作。</summary>
        public List<string> WrongActions { get; set; } = new List<string>();
        /// <summary>互相衝突而沒有生效的操作（見 Procedures.Conflicts）。</summary>
        public List<string> ConflictedActions { get; set; } = new List<string>();
        public double StepsFraction => StepsRequired > 0 ? (double)StepsDone / StepsRequired : 1;
        /// <summary>多語鍵，例如 event.storm.safe。</summary>
        public string ResultCode { get; set; } = "";
        public string AffectedPartId { get; set; } = "";
        public int LostDays { get; set; }
        public double ScienceDelta { get; set; }
        /// <summary>這回合的及時通報比例倍率（大火爆發沒處置 = 0.5）。只有野火關卡的科學成果用到。</summary>
        public double AlertFactor { get; set; } = 1.0;
        /// <summary>這回合營運處置分數直接歸零（把假火點通報給消防單位）。</summary>
        public bool Voided { get; set; }
        /// <summary>這回合打的小遊戲（依判斷的事件而定，沒判斷就是空字串）、有沒有交成績、全隊有沒有過。</summary>
        public string MiniGame { get; set; } = "";
        public bool MiniPlayed { get; set; }
        public bool MiniPassed { get; set; }
    }

    /// <summary>任務結束的原因。Funding = 營運經費用完，加在最後，舊的編號不變。</summary>
    public enum EndReason { MissionComplete, Propellant, Battery, Radiation, LaunchFailure, Funding }

    public sealed class MissionResult
    {
        public int Seed { get; set; }
        public LaunchResult Launch { get; set; } = new LaunchResult();
        public bool ReachedOrbit => Launch.Success;
        public double TargetYears { get; set; }
        public double ActualYears { get; set; }
        /// <summary>營運經費撐得了幾年（預算 − 開發費 − 火箭，再除以每年營運費）。沒有預算的設計是無限大。</summary>
        public double FundedYears { get; set; } = double.PositiveInfinity;
        public EndReason EndReason { get; set; }
        public double ScienceTotal { get; set; }
        public double DataGeneratedGb { get; set; }
        public double DataReturnedGb { get; set; }
        public double DataLostGb { get; set; }
        public double ReturnFraction { get; set; }
        public double AverageDuty { get; set; }
        public List<Sample> Samples { get; set; } = new List<Sample>();
        public List<EventOutcome> Events { get; set; } = new List<EventOutcome>();
        public Score Score { get; set; }
        public List<CausalChain> Causes { get; set; } = new List<CausalChain>();
        public string SuggestionCode { get; set; } = "";
    }
}
