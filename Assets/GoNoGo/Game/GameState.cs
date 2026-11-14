using System;
using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;

namespace GoNoGo.Game
{
    public enum Phase { Title, Briefing, Design, RollCall, Launch, Operations, Report }

    /// <summary>歷史關卡的步驟：背景卡 → 重演歷史 → 揭曉 → 改寫歷史 → 對照。</summary>
    public enum HistoryStage { None, Background, Replay, Reveal, Rewrite, Compare }

    /// <summary>
    /// 一局遊戲的全部狀態。玩家操作只會改這裡，這裡呼叫核心重算，再用事件通知畫面；畫面本身不做計算。
    /// </summary>
    public sealed class GameState
    {
        public GameData Data { get; }
        public ScenarioDef Scenario { get; private set; }
        public DifficultyDef Difficulty { get; private set; }
        public Design Design { get; private set; }
        public Evaluation Evaluation { get; private set; }
        public Phase Phase { get; private set; } = Phase.Title;
        public HistoryStage History { get; private set; } = HistoryStage.None;

        /// <summary>單人模式：開局抽好的部門要求（四人模式是各自的私有目標）。</summary>
        public Dictionary<Role, RequirementDef> Requirements { get; private set; } = new Dictionary<Role, RequirementDef>();
        public Role ActiveRole { get; private set; } = Role.Director;
        public List<RoleVote> Votes { get; private set; } = new List<RoleVote>();
        /// <summary>這次發射前的第一次點名就全員 GO（團隊協作加分）。</summary>
        public bool FirstCallAllGo { get; private set; }
        bool _firstCallTaken;

        public int Attempt { get; private set; }
        public MissionResult Result { get; private set; }
        public MissionPlan Plan { get; private set; }
        /// <summary>營運中玩家做過的選擇（事件 id → 選項）。</summary>
        public Dictionary<string, string> Choices { get; } = new Dictionary<string, string>();
        /// <summary>上一次任務的結果，報告頁「和上次比」用。</summary>
        public MissionResult PreviousResult { get; private set; }
        public bool SkipLaunchAllowed => Attempt > 1;
        public bool TutorialMode { get; set; }

        /// <summary>滑鼠移到零件上時的預覽（null = 沒有）。</summary>
        public Evaluation PreviewEvaluation { get; private set; }
        public string HoverCategory { get; private set; }
        public string HoverPartId { get; private set; }

        public event Action ScenarioStarted;
        public event Action<Evaluation> DesignChanged;
        public event Action PreviewChanged;
        public event Action<Phase> PhaseChanged;
        public event Action<Role> ActiveRoleChanged;
        public event Action<List<RoleVote>> RollCallCompleted;
        public event Action<MissionResult> MissionResolved;
        public event Action<HistoryStage> HistoryStageChanged;

        public GameState(GameData data)
        {
            Data = data;
        }

        public bool IsHistorical => Scenario?.Historical != null;
        public string DifficultyId => Difficulty?.Id;

        // ---------------- 開局 ----------------

        public void StartScenario(string scenarioId, string difficultyId, int seed)
        {
            Scenario = Data.Scenario(scenarioId) ?? Data.Scenarios[0];
            Difficulty = Data.Difficulty(difficultyId) ?? Data.Difficulties[0];
            var start = IsHistorical ? Scenario.Historical.PresetDesign : Scenario.DefaultDesign;
            Design = start.Clone();
            Design.ScenarioId = Scenario.Id;
            Requirements = Roles.DrawRequirements(Scenario, seed);
            Attempt = 0;
            Result = null;
            PreviousResult = null;
            Votes.Clear();
            _firstCallTaken = false;
            FirstCallAllGo = false;
            History = IsHistorical ? HistoryStage.Background : HistoryStage.None;
            Recompute();
            ScenarioStarted?.Invoke();
            SetPhase(Phase.Briefing);
        }

        public void SetPhase(Phase p)
        {
            Phase = p;
            PhaseChanged?.Invoke(p);
        }

        public void SetHistoryStage(HistoryStage s)
        {
            History = s;
            HistoryStageChanged?.Invoke(s);
        }

        public void SetActiveRole(Role r)
        {
            if (r == ActiveRole) return;
            ActiveRole = r;
            ActiveRoleChanged?.Invoke(r);
        }

        // ---------------- 設計 ----------------

        /// <summary>歷史關卡在「重演」之前不能改設計。</summary>
        public bool DesignLocked => History == HistoryStage.Background || History == HistoryStage.Replay;

        public void Modify(Action<Design> change)
        {
            if (DesignLocked) return;
            var d = Design.Clone();
            change(d);
            if (d.StableHash() == Design.StableHash() && d.MissionYears == Design.MissionYears) return;
            Design = d;
            Votes.Clear(); // 改了設計就要重新點名
            ClearPreview();
            Recompute();
        }

        public void SetPart(string category, string partId) => Modify(d =>
        {
            if (category == PartCategory.Instrument) ToggleInstrumentIn(d, partId);
            else d.Parts[category] = partId;
        });

        public void SetOrbit(string id) => Modify(d => d.OrbitId = id);
        public void SetLauncher(string id) => Modify(d => d.LauncherId = id);
        public void SetYears(int years) => Modify(d => d.MissionYears = Math.Max(1, Math.Min(10, years)));
        public void ApplyFix(Fix f) => Modify(d => { var n = f.ApplyTo(d); CopyInto(n, d); });

        void ToggleInstrumentIn(Design d, string id)
        {
            if (d.Instruments.Contains(id)) d.Instruments.Remove(id);
            else if (d.Instruments.Count < (Scenario?.MaxInstruments ?? 3)) d.Instruments.Add(id);
        }

        static void CopyInto(Design from, Design to)
        {
            to.Instruments = from.Instruments;
            to.Parts = from.Parts;
            to.OrbitId = from.OrbitId;
            to.LauncherId = from.LauncherId;
            to.MissionYears = from.MissionYears;
        }

        public bool CanAddInstrument => Design.Instruments.Count < (Scenario?.MaxInstruments ?? 3);

        void Recompute()
        {
            Evaluation = Evaluator.EvaluateDesign(Design, Data, DifficultyId, true);
            DesignChanged?.Invoke(Evaluation);
        }

        /// <summary>滑鼠移到選項上：算出「選了會怎樣」，不改設計。</summary>
        public void Hover(string category, string id)
        {
            if (category == HoverCategory && id == HoverPartId) return;
            HoverCategory = category;
            HoverPartId = id;
            if (category == null || id == null || DesignLocked) PreviewEvaluation = null;
            else
            {
                var d = Design.Clone();
                switch (category)
                {
                    case "orbit": d.OrbitId = id; break;
                    case "launcher": d.LauncherId = id; break;
                    case PartCategory.Instrument: ToggleInstrumentIn(d, id); break;
                    default: d.Parts[category] = id; break;
                }
                PreviewEvaluation = Evaluator.EvaluateDesign(d, Data, DifficultyId, false);
            }
            PreviewChanged?.Invoke();
        }

        public void ClearPreview() => Hover(null, null);

        /// <summary>這個難度可以看到的零件。</summary>
        public IEnumerable<PartDef> PartsFor(string category) =>
            Data.PartsIn(category).Where(p => p.Tier <= (Difficulty?.TiersPerCategory ?? 3));

        public bool ResourceVisible(ResourceId id) =>
            Difficulty == null || Difficulty.VisibleResources.Count == 0 || Difficulty.VisibleResources.Contains(id.ToString().ToLowerInvariant());

        // ---------------- Go/No-Go ----------------

        public List<RoleVote> RunRollCall()
        {
            Votes = RollCall.Run(Evaluation, Design, Data, Difficulty, Requirements);
            if (!_firstCallTaken)
            {
                // 「第一次點名就全員 GO」只看每次發射前的第一次點名
                FirstCallAllGo = RollCall.AllGo(Votes);
                _firstCallTaken = true;
            }
            RollCallCompleted?.Invoke(Votes);
            return Votes;
        }

        public bool AllGo => Votes.Count == 4 && RollCall.AllGo(Votes);

        // ---------------- 發射與營運 ----------------

        /// <summary>同一個設計永遠用同一個種子，所以同一個設計跑兩次結果一模一樣。</summary>
        public int MissionSeed => Design.StableHash();

        public MissionContext Context => new MissionContext
        {
            DifficultyId = DifficultyId,
            Requirements = Requirements,
            FirstCallAllGo = FirstCallAllGo,
        };

        /// <summary>按下發射：用目前的選擇（營運事件先用預設）算出完整結果。</summary>
        public MissionResult Launch()
        {
            Attempt++;
            PreviousResult = Result;
            Choices.Clear();
            Plan = Simulator.PlanMission(Design, Data, MissionSeed, DifficultyId);
            Result = Simulator.SimulateMission(Design, Data, MissionSeed, new FixedDecisions(Choices), Context);
            _firstCallTaken = false;
            if (IsHistorical && History == HistoryStage.Background) SetHistoryStage(HistoryStage.Replay);
            MissionResolved?.Invoke(Result);
            return Result;
        }

        /// <summary>
        /// 營運中玩家做了選擇：重算整個任務。亂數在規劃時就擲好、而且事件之前的日子不受影響，
        /// 所以重算後「已經播過的部分」和畫面上一模一樣，只有之後的發展會改變。
        /// </summary>
        public event Action<string, string> ChoiceMade;

        public MissionResult Decide(string eventId, string choice)
        {
            Choices[eventId] = choice;
            ChoiceMade?.Invoke(eventId, choice);
            Result = Simulator.SimulateMission(Design, Data, MissionSeed, new FixedDecisions(Choices), Context);
            MissionResolved?.Invoke(Result);
            return Result;
        }

        /// <summary>從報告回到設計室再試一次。</summary>
        public void Redesign()
        {
            if (IsHistorical && (History == HistoryStage.Replay || History == HistoryStage.Reveal)) SetHistoryStage(HistoryStage.Rewrite);
            Votes.Clear();
            SetPhase(Phase.Design);
        }

        /// <summary>報告頁「為什麼」裡零件與部門要求的達成情況。</summary>
        public IEnumerable<(Role role, RequirementDef req, bool met)> RequirementStatus()
        {
            foreach (var r in Roles.All)
            {
                if (!Requirements.TryGetValue(r, out var req)) continue;
                yield return (r, req, Requirements_IsMet(req));
            }
        }

        bool Requirements_IsMet(RequirementDef req) =>
            GoNoGo.Core.Requirements.IsMetPreLaunch(req, Evaluation, Design, Data, Difficulty);
    }
}
