using System.Collections.Generic;

namespace GoNoGo.Core
{
    public enum LaunchOutcome { Nominal, LowerOrbit, Failure, FairingFailure, NotLaunched }

    public sealed class LaunchResult
    {
        public LaunchOutcome Outcome { get; set; }
        /// <summary>多語鍵，說明為什麼是這個結果。</summary>
        public string ReasonCode { get; set; } = "";
        public double MassMargin { get; set; }
        public double Reliability { get; set; }
        public double Roll { get; set; }
        public bool Success => Outcome == LaunchOutcome.Nominal || Outcome == LaunchOutcome.LowerOrbit;
    }

    public enum EventKind { SolarStorm, PartFailure, ScienceOpportunity }

    /// <summary>營運期間的事件。亂數在規劃時就擲好，所以玩家的選擇不會改變後面的亂數。</summary>
    public sealed class MissionEvent
    {
        public string Id { get; set; } = "";
        public EventKind Kind { get; set; }
        public int Day { get; set; }
        /// <summary>太陽風暴：DONKI 事件 id；零件故障：零件 id；科學機會：機會種類。</summary>
        public string RefId { get; set; } = "";
        public int Severity { get; set; } = 1;
        public double Roll { get; set; }
        public double Roll2 { get; set; }
        public List<string> Choices { get; set; } = new List<string>();
        public string DefaultChoice { get; set; } = "";
    }

    public sealed class MissionPlan
    {
        public int Seed { get; set; }
        public double LaunchRoll { get; set; }
        public double LaunchRoll2 { get; set; }
        public List<MissionEvent> Events { get; set; } = new List<MissionEvent>();
    }

    public interface IDecisionSource
    {
        string Decide(MissionEvent e);
    }

    /// <summary>用事先記錄的選擇；沒記錄的事件用預設選項。</summary>
    public sealed class FixedDecisions : IDecisionSource
    {
        readonly IDictionary<string, string> _map;
        public FixedDecisions(IDictionary<string, string> map = null) { _map = map ?? new Dictionary<string, string>(); }
        public string Decide(MissionEvent e) =>
            _map.TryGetValue(e.Id, out var c) && e.Choices.Contains(c) ? c : e.DefaultChoice;
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
        public double Duty { get; set; }
        public bool SafeMode { get; set; }
    }

    public sealed class EventOutcome
    {
        public MissionEvent Event { get; set; }
        public string Choice { get; set; } = "";
        /// <summary>多語鍵，例如 event.storm.safe。</summary>
        public string ResultCode { get; set; } = "";
        public string AffectedPartId { get; set; } = "";
        public int LostDays { get; set; }
        public double ScienceDelta { get; set; }
    }

    public enum EndReason { MissionComplete, Propellant, Battery, Radiation, LaunchFailure }

    public sealed class MissionResult
    {
        public int Seed { get; set; }
        public LaunchResult Launch { get; set; } = new LaunchResult();
        public bool ReachedOrbit => Launch.Success;
        public double TargetYears { get; set; }
        public double ActualYears { get; set; }
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
