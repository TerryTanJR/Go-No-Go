using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // 任務簡報（BriefingPanel）在 BriefingPanel.cs；設計主螢幕選件（SelectPanel）和組裝區儀表板（BayPanel）在 SelectPanels.cs；
    // 線索（CluePanel）在 CluePanels.cs；參考書（ManualPanel）在 ManualPanels.cs

    // ======================================================================== 發射

    public sealed class LaunchPanel : FacPanel
    {
        public override string Title => Loc.T("ui.launchp.title");

        protected override void Build(VisualElement body)
        {
            var s = M.S;
            bool poll = s.Multiplayer && s.Players.Count > 1;
            Ui.Text(Loc.T("ui.launchp.question"), "h2").AddTo(body);
            Ui.Text(Loc.T("ui.launchp.body"), "body").AddTo(body);

            // 發射條件：設計選齊並確定、每個零件都組好裝上衛星、沒有超出預算。還缺什麼列在這裡。
            // 設計好不好，系統不判斷：結果交給發射。
            var gate = Ui.El("launch-gate").AddTo(body);
            var blockers = SessionRules.LaunchBlockers(s, D);
            foreach (var why in blockers)
            {
                switch (why)
                {
                    case "err.design_incomplete":
                        Gate(gate, "over", Loc.T("ui.launchp.need_design", ("list", Join(SessionRules.DesignGaps(s, D).Select(c => Loc.T("ui.cat." + c))))));
                        break;
                    case "err.not_confirmed":
                        Gate(gate, "over", Loc.T("ui.launchp.need_confirm"));
                        break;
                    case "err.not_installed":
                    {
                        var left = SessionRules.NotInstalled(s, D);
                        Gate(gate, "over", Loc.T("ui.launchp.need_install", ("n", left.Count), ("list", Join(left.Select(id => D.Part(id)?.Code ?? id)))));
                        break;
                    }
                    case "err.over_budget":
                    {
                        var b = M.BenchEval[ResourceId.Budget];
                        Gate(gate, "over", Loc.T("ui.launchp.over_budget", ("over", Fmt.Money(b.Used - b.Capacity))));
                        break;
                    }
                    default:
                        Gate(gate, "tight", Loc.T(why));
                        break;
                }
            }
            if (blockers.Count == 0) Gate(gate, "dim", Loc.T("ui.launchp.ready"));

            // 營運經費撐得了幾年（預算 − 開發 − 火箭，除以每年營運費）
            double funded = Simulator.FundedYears(M.BenchEval);
            int years = s.Bench.MissionYears;
            if (funded < years && !blockers.Contains("err.over_budget"))
                Gate(gate, funded <= 0 ? "over" : "tight", Loc.T("ui.launchp.funds_short", ("funded", Fmt.N(funded, 1)), ("years", years)));
            // 歷史任務失敗不重來（保留原設計），其他關卡失敗就回到選件重做
            if (s.IsSandbox(D)) Gate(gate, "dim", Loc.T("ui.launchp.sandbox_note"));
            else if (s.Scenario(D)?.Historical == null) Gate(gate, "dim", Loc.T("ui.launchp.restart_note"));
            if (poll) Gate(gate, "dim", Loc.T("ui.launchp.poll_note", ("n", s.Players.Count)));

            var row = Ui.Row("modal-actions").AddTo(body);
            Ui.Btn(Loc.T("ui.launchp.cancel"), () => Close(), "btn-big").AddTo(row);
            Ui.Spacer().AddTo(row);
            var go = Ui.Btn(Loc.T(poll ? "ui.launchp.poll" : "ui.launchp.go"), () =>
            {
                if (poll) Sfx.Alert(); else Sfx.Go();
                Hud.Signal("launch");
                GameSession.Send(CommandType.Launch);
                if (poll) Close();
            }, "btn-go", "btn-big").AddTo(row);
            go.SetEnabled(blockers.Count == 0);
        }

        static string Join(IEnumerable<string> items) => string.Join(Loc.IsZh ? "、" : ", ", items);

        static void Gate(VisualElement parent, string cls, string text)
        {
            var r = Ui.Row("gate-row").AddTo(parent);
            Ui.El("dash-lamp", "dash-lamp--" + (cls == "dim" ? "go" : cls)).AddTo(r);
            Ui.Text(text, "body", cls == "dim" ? "dim" : cls).AddTo(r);
        }
    }

    // ======================================================================== 失敗原因看板

    public sealed class FailuresPanel : FacPanel
    {
        public override string Title => Loc.T("ui.fail.title");
        public override string WideClass => "modal--wide";
        protected override void Build(VisualElement body) => FailureBoardView.Build(Scroll(body));
    }

    /// <summary>
    /// 每一次發射的結果與原因，最新的在最上面。詳細程度依難度。
    /// 原因用零件的真實數值說明（例如「電池只有 80 Wh」）：玩家在設計主螢幕上填錯了數字，從失敗裡得到正確的數值也算一種找數值的方式。
    /// </summary>
    public static class FailureBoardView
    {
        /// <summary>
        /// 一次失敗的原因要把那一趟任務重新模擬一遍才知道，而看板每次狀態改變都會重畫（牆上兩面螢幕、面板、平板），
        /// 所以算過的結果記在那筆發射紀錄上。模擬的種子固定：同一筆紀錄、同一份資料、同一個關卡和難度，結果一定一樣；
        /// 紀錄上的設計、種子事後被改掉（開發選單）就重算。紀錄不再被任何狀態引用時，這一筆也跟著被回收。
        /// </summary>
        sealed class Cached
        {
            public GameData Data;
            public string Scenario, Difficulty;
            public int Seed, FailedBefore, DesignHash;
            public MissionResult Result;
        }

        static readonly ConditionalWeakTable<LaunchRecord, Cached> Results = new ConditionalWeakTable<LaunchRecord, Cached>();

        static MissionResult ResultOf(LaunchRecord l, SessionState s, GameData data)
        {
            int design = l.Design?.StableHash() ?? 0;
            if (Results.TryGetValue(l, out var c) && ReferenceEquals(c.Data, data) && c.Scenario == s.ScenarioId && c.Difficulty == s.DifficultyId
                && c.Seed == l.Seed && c.FailedBefore == l.FailedBefore && c.DesignHash == design)
                return c.Result;
            Results.Remove(l);
            c = new Cached
            {
                Data = data, Scenario = s.ScenarioId, Difficulty = s.DifficultyId, Seed = l.Seed, FailedBefore = l.FailedBefore, DesignHash = design,
                Result = SessionRules.ResultOf(l, s, data),
            };
            Results.Add(l, c);
            return c.Result;
        }

        public static void Build(VisualElement parent)
        {
            var m = GameSession.Mission;
            var detail = m.Difficulty.FailureDetail;
            if (m.S.Launches.Count == 0) { Ui.Text(Loc.T("ui.fail.none"), "small", "dim").AddTo(parent); return; }
            for (int i = m.S.Launches.Count - 1; i >= 0; i--)
            {
                var l = m.S.Launches[i];
                var card = Ui.El("fail-card").Cls("fail-card--ok", l.Reached).AddTo(parent);
                var head = Ui.Row().AddTo(card);
                Ui.Text(Loc.T("ui.fail.attempt", ("n", l.Attempt)), "eyebrow").AddTo(head);
                Ui.Spacer().AddTo(head);
                Ui.Text(Loc.T(OutcomeKey(l.Outcome)), "small", l.Reached ? "go" : "over").AddTo(head);
                if (l.Reached) continue;
                if (detail == "category")
                {
                    Ui.Text(Loc.T("ui.fail.category." + Category(l)), "body").AddTo(card);
                    continue;
                }
                Ui.Text(Loc.T(l.ReasonCode), "body").AddTo(card);
                var r = ResultOf(l, m.S, GameSession.Data);
                var chain = r.Causes.FirstOrDefault(c => !c.Positive);
                if (chain != null)
                {
                    var row = Ui.El("chain").AddTo(card);
                    for (int k = 0; k < chain.Steps.Count; k++)
                    {
                        if (k > 0) Ui.Text("→", "chain-arrow").AddTo(row);
                        Ui.Text(Loc.Cause(chain.Steps[k]), "chain-step").AddTo(row);
                    }
                }
                if (detail == "advice" && !string.IsNullOrEmpty(r.SuggestionCode))
                    Ui.Text(Loc.T("ui.report.suggestion") + "：" + Loc.T(r.SuggestionCode), "small", "accent").AddTo(card);
            }
        }

        public static string OutcomeKey(LaunchOutcome o) => o switch
        {
            LaunchOutcome.Nominal => "ui.launch.result.nominal",
            LaunchOutcome.LowerOrbit => "ui.launch.result.lower",
            LaunchOutcome.FairingFailure => "ui.launch.result.fairing",
            LaunchOutcome.DeadOnOrbit => "ui.launch.result.dead",
            _ => "ui.launch.result.failure",
        };

        static string Category(LaunchRecord l) => l.Outcome switch
        {
            LaunchOutcome.DeadOnOrbit => "power",
            LaunchOutcome.FairingFailure => "launcher",
            _ => l.ReasonCode == "launch.anomaly" ? "launcher" : "mass",
        };
    }

    // ======================================================================== 平板（個人模式）

    /// <summary>
    /// 個人模式按 T 叫出來的平板：把兩個房間牆上公開的資訊帶在身上（儀表板、任務需求、組裝清單、設計規則、失敗原因），
    /// 一個人不用為了看一眼跑來跑去。只能看，不能操作；線索不在這裡，還是要走過去放大看。
    /// </summary>
    public sealed class TabletPanel : FacPanel
    {
        public override string Title => Loc.T("ui.tablet.title");
        public override string WideClass => "modal--wide";
        public override bool CloseWithE => false;
        static int _tab;
        protected override string ScrollKey => _tab.ToString();

        protected override void Build(VisualElement body)
        {
            // 失敗原因：第一次發射失敗才出現這一頁（Reveal）
            var keys = new List<string> { "ui.tablet.dashboard", "ui.req.title", "ui.bay.list", "ui.manual.tab.rules" };
            if (Reveal.Failures) keys.Add("ui.fail.title");
            if (_tab >= keys.Count) _tab = 0;
            Tabs(body, keys.ToArray(), _tab, i => { _tab = i; Refresh(); });
            var sv = Scroll(body);
            switch (_tab)
            {
                case 0: SelectView.Dashboard(sv, false); break;
                case 1: SelectView.Requirements(sv, false, true); break;
                case 2: SelectView.Bay(sv, false); break;
                case 3: ManualView.Build(sv, "rules"); break;
                default: FailureBoardView.Build(sv); break;
            }
        }
    }
}
