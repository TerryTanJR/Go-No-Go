using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 「玩家看到的」資料：挖空的數值換成玩家填的（沒填的不算）。設計主螢幕的儀表板只用這一份；
        /// 發射、營運、失敗原因和分數永遠用 <see cref="Data"/> 的真實數值。沒有挖空的局就是 <see cref="Data"/> 本身。
        /// </summary>
        public GameData PlayerData { get; private set; }

        /// <summary>設計主螢幕儀表板看的評估：設計主螢幕上選的設計，用玩家填的數值算（填錯就照錯的算）。</summary>
        public Evaluation Dashboard { get; private set; }
        /// <summary>同一個設計用真實數值算的評估（發射之後、報告用；設計階段不要拿它顯示被挖空的數值）。</summary>
        public Evaluation BenchEval { get; private set; }
        /// <summary>整合台上的衛星：設計主螢幕選的設計裡，已經組好裝上去的那些零件（3D 模型畫的是它）。</summary>
        public Design Satellite { get; private set; }
        /// <summary>整合台上那艘衛星的評估（真實數值；只給 3D 模型決定船身大小用）。</summary>
        public Evaluation SatelliteEval { get; private set; }

        int _cacheRev = -1;
        MissionResult _result;
        MissionPlan _plan;
        MissionEvent _event;
        Dictionary<string, List<Clue>> _clues;
        Dictionary<string, int> _trivia;
        string _cluesFor;

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
            PlayerData = Blanks.PlayerData(Data, S);
            Dashboard = Evaluator.EvaluateDesign(S.Bench, PlayerData, S.DifficultyId, false);
            BenchEval = Evaluator.EvaluateDesign(S.Bench, Data, S.DifficultyId, false);
            Satellite = InstalledDesign(S, Data);
            SatelliteEval = Evaluator.EvaluateDesign(Satellite, Data, S.DifficultyId, false);
            _cacheRev = -1;
        }

        /// <summary>設計主螢幕選的設計裡，已經裝上衛星的零件（軌道、火箭、地面站照設計）。</summary>
        static Design InstalledDesign(SessionState s, GameData data)
        {
            var d = s.Bench.Clone();
            d.Instruments.RemoveAll(id => !s.Installed.Contains(id));
            foreach (var cat in PartCategory.Single)
            {
                if (cat == PartCategory.GroundStation) continue;
                var id = d.Get(cat);
                if (id != null && !s.Installed.Contains(id)) d.Parts.Remove(cat);
            }
            return d;
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

        /// <summary>
        /// 這個位置這一局放的線索（內容每局固定，位置照房主開局時決定的種子）。
        /// 擺法只跟關卡、難度、人數模式和種子有關，算一次就記住。
        /// </summary>
        public List<Clue> CluesAt(string spotId)
        {
            EnsureClues();
            return _clues.TryGetValue(spotId ?? "", out var list) ? list : new List<Clue>();
        }

        /// <summary>沒放線索的位置放的是第幾則太空知識（有線索的位置 = -1）。</summary>
        public int TriviaAt(string spotId)
        {
            EnsureClues();
            return _trivia.TryGetValue(spotId ?? "", out var n) ? n : -1;
        }

        void EnsureClues()
        {
            string key = S.ScenarioId + "/" + S.DifficultyId + "/" + S.Solo + "/" + S.ClueSeed;
            if (_clues != null && _cluesFor == key) return;
            _clues = Clues.Place(Data, S);
            _trivia = Clues.Trivia(_clues, S.ClueSeed);
            _cluesFor = key;
        }
    }
}
