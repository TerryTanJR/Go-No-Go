using System;
using GoNoGo.Core;

namespace GoNoGo.Game
{
    /// <summary>
    /// 一局遊戲的執行期包裝：持有 <see cref="SessionState"/>（多人時是房主廣播來的副本），
    /// 並算出畫面要用的東西（儀表板評估、模擬結果）。狀態只能透過 <see cref="ISessionLink"/> 送操作來改。
    /// </summary>
    public sealed class MissionSession
    {
        public GameData Data { get; }
        public SessionState S { get; private set; }

        public ScenarioDef Scenario => S.Scenario(Data);
        public DifficultyDef Difficulty => S.Difficulty(Data);
        public bool IsHistorical => Scenario?.Historical != null;

        /// <summary>中控室儀表板看的評估（最後一次回報的設計）。</summary>
        public Evaluation Dashboard { get; private set; }
        /// <summary>整合台上實際的設計的評估（組裝區畫面、太空船外型用）。</summary>
        public Evaluation BenchEval { get; private set; }

        int _cacheRev = -1;
        MissionResult _result;
        MissionPlan _plan;
        MissionEvent _event;

        /// <summary>狀態改變（任何操作、或收到房主的新狀態）。</summary>
        public event Action Changed;
        /// <summary>階段改變（舊、新）。</summary>
        public event Action<SessionPhase, SessionPhase> PhaseChanged;
        /// <summary>本機玩家的操作被拒絕（字串鍵）。</summary>
        public event Action<string> Rejected;

        public MissionSession(GameData data, SessionState s)
        {
            Data = data;
            S = s;
            Recompute();
        }

        public string Apply(Command c)
        {
            var before = S.Phase;
            var r = SessionRules.Apply(S, Data, c);
            if (r == SessionRules.Ok) Commit(before);
            return r;
        }

        /// <summary>多人：收到房主廣播的整包狀態。</summary>
        public void Replace(SessionState s)
        {
            var before = S.Phase;
            S = s;
            Commit(before);
        }

        public void RaiseRejected(string code) => Rejected?.Invoke(code);

        void Commit(SessionPhase before)
        {
            Recompute();
            Changed?.Invoke();
            if (S.Phase != before) PhaseChanged?.Invoke(before, S.Phase);
        }

        void Recompute()
        {
            Dashboard = Evaluator.EvaluateDesign(S.Reported, Data, S.DifficultyId, false);
            BenchEval = Evaluator.EvaluateDesign(S.Bench, Data, S.DifficultyId, false);
            _cacheRev = -1;
        }

        void Ensure()
        {
            if (_cacheRev == S.Revision) return;
            _cacheRev = S.Revision;
            _plan = SessionRules.Plan(S, Data);
            _result = SessionRules.Result(S, Data);
            _event = S.Phase == SessionPhase.Ops ? SessionRules.CurrentEvent(S, Data) : null;
        }

        /// <summary>最後一次發射的任務結果（含目前為止的營運處置）。</summary>
        public MissionResult Result { get { Ensure(); return _result; } }
        public MissionPlan Plan { get { Ensure(); return _plan; } }
        /// <summary>營運：這回合的事件。</summary>
        public MissionEvent CurrentEvent { get { Ensure(); return _event; } }

        public EventOutcome CurrentOutcome
        {
            get
            {
                var e = CurrentEvent;
                return e == null ? null : Result?.Events.Find(o => o.Event.Id == e.Id);
            }
        }

        /// <summary>事件發生那天的遙測樣本（控制台讀數用）。</summary>
        public Sample SampleAt(int day)
        {
            var r = Result;
            if (r == null || r.Samples.Count == 0) return null;
            Sample best = r.Samples[0];
            foreach (var s in r.Samples)
            {
                if (s.Day > day) break;
                best = s;
            }
            return best;
        }

        public EventResponse ResponseTo(string eventId) =>
            eventId != null && S.Responses.TryGetValue(eventId, out var r) ? r : new EventResponse();
    }
}
