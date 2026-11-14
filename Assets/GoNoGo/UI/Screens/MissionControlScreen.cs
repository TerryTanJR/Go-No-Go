using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 任務控制中心：開局簡報（含歷史關卡背景卡），以及 Go/No-Go 點名。
    /// 點名時四個席位依序回答，燈號亮起；全員 GO 才能按發射。
    /// </summary>
    public sealed class MissionControlScreen : GameScreen
    {
        // 點名動畫的進度：切語言重建畫面時不要重播（每次進場景是新的畫面物件，會重新點名）
        bool _ran;
        int _revealed;
        float _timer;
        readonly List<(VisualElement console, VisualElement lamp, Label verdict, Label reason)> _consoles =
            new List<(VisualElement, VisualElement, Label, Label)>();
        VisualElement _after;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            TopBar(Loc.T(S.Scenario.Name));
            if (S.Phase == Phase.RollCall) BuildRollCall();
            else BuildBriefing();
        }

        // ---------------- 簡報 ----------------

        void BuildBriefing()
        {
            var sc = S.Scenario;
            var wrap = Ui.El("mc-wrap", "fade-in").AddTo(Root);
            Ui.AnimateIn(wrap, "fade-in--on", 60);

            var left = Ui.El("mc-col").AddTo(wrap);
            var body = Ui.Panel(left, Loc.T("ui.brief.title"), out _);
            Ui.Text(Loc.T(sc.Name), "h2").AddTo(body);
            Ui.Text(Loc.T(sc.Briefing), "body", "brief-text").AddTo(body);

            var stats = Ui.Row("brief-stats").AddTo(body);
            Stat(stats, Loc.T("ui.brief.budget"), Fmt.Money(S.Evaluation.Cost.BudgetMusd));
            Stat(stats, Loc.T("ui.brief.lifetime"), Fmt.Years(sc.TargetYears));
            Stat(stats, Loc.T("ui.brief.window"), Fmt.N(sc.DeadlineMonths) + " " + Fmt.Unit("mo"));
            Stat(stats, Loc.T("ui.brief.instruments"), sc.MaxInstruments.ToString());
            Stat(stats, Loc.T("ui.brief.difficulty"), Loc.T(S.Difficulty.Name));

            if (S.IsHistorical && (S.History == HistoryStage.Background || S.History == HistoryStage.None))
            {
                var h = Ui.Panel(left, Loc.T("ui.history.background") + " · " + sc.Historical.Year, out var hp, "history-card");
                Ui.Text(Loc.T(sc.Historical.Background), "body").AddTo(h);
                Ui.Text(Loc.T("ui.history.locked"), "small", "tight", "history-note").AddTo(h);
            }

            var right = Ui.El("mc-col", "mc-col--narrow").AddTo(wrap);
            var req = Ui.Panel(right, Loc.T("ui.brief.requirements"), out _);
            Ui.Text(Loc.T("ui.brief.requirements_desc"), "small", "dim", "req-intro").AddTo(req);
            foreach (var (role, r, met) in S.RequirementStatus())
            {
                var card = Ui.El("req-card").AddTo(req);
                Ui.Text(Fmt.Role(role), "eyebrow").AddTo(card);
                Ui.Text(Loc.T(r.Text), "small").AddTo(card);
            }

            if (S.TutorialMode)
            {
                var tip = Ui.Panel(right, Loc.T("ui.training.title"), out _, "training-card");
                Ui.Text(Loc.T("ui.training.brief"), "small", "body").AddTo(tip);
            }

            var actions = Ui.Row("mc-actions").AddTo(Root);
            Ui.Btn(Loc.T("ui.brief.back"), () => SceneFlow.Go(SceneFlow.Boot), "btn-ghost").AddTo(actions);
            Ui.Spacer().AddTo(actions);
            string go = S.IsHistorical && S.DesignLocked ? Loc.T("ui.brief.review_design") : Loc.T("ui.brief.enter_design");
            Ui.Btn(go, EnterDesign, "btn-primary", "btn-big").AddTo(actions);
        }

        static void Stat(VisualElement parent, string k, string v)
        {
            var s = Ui.El("brief-stat").AddTo(parent);
            Ui.Text(k, "small", "dim").AddTo(s);
            Ui.Text(v, "h3", "mono").AddTo(s);
        }

        static void EnterDesign()
        {
            Sfx.Confirm();
            S.SetPhase(Phase.Design);
            SceneFlow.Go(SceneFlow.DesignRoom);
        }

        // ---------------- 點名 ----------------

        void BuildRollCall()
        {
            if (!_ran)
            {
                S.RunRollCall();
                _ran = true;
                _revealed = 0;
                _timer = 0.9f;
            }

            var head = Ui.El("rc-head", "fade-in").AddTo(Root);
            Ui.AnimateIn(head, "fade-in--on", 40);
            Ui.Text(Loc.T("ui.rollcall.eyebrow"), "eyebrow").AddTo(head);
            Ui.Text(Loc.T("ui.rollcall.title"), "h2", "rc-title").AddTo(head);
            Ui.Text(Loc.T("ui.rollcall.desc"), "body").AddTo(head);

            var row = Ui.Row("rc-row").AddTo(Root);
            _consoles.Clear();
            foreach (var v in S.Votes)
            {
                var c = Ui.El("console").AddTo(row);
                Ui.Text(Fmt.Role(v.Role), "eyebrow").AddTo(c);
                Ui.Text(Loc.T("ui.rollcall.station." + Roles.Key(v.Role)), "small", "dim").AddTo(c);
                var lamp = Ui.El("lamp").AddTo(c);
                var verdict = Ui.Text("—", "verdict", "mono").AddTo(c);
                var reason = Ui.Text("", "small", "rc-reason").AddTo(c);
                _consoles.Add((c, lamp, verdict, reason));
            }
            for (int i = 0; i < _revealed; i++) Reveal(i, false);

            _after = Ui.El("rc-after").AddTo(Root);
            if (_revealed >= 4) ShowOutcome();
        }

        public override void Tick(float dt)
        {
            if (S.Phase != Phase.RollCall || _consoles.Count == 0 || _revealed >= 4) return;
            _timer -= dt;
            if (_timer > 0) return;
            if (_revealed < 4 && !_consoles[_revealed].console.ClassListContains("console--calling"))
            {
                // 先「呼叫」這個席位，再揭曉回答
                _consoles[_revealed].console.AddToClassList("console--calling");
                _consoles[_revealed].lamp.AddToClassList("lamp--calling");
                _consoles[_revealed].verdict.text = "…";
                Sfx.Tick();
                _timer = 0.55f;
                return;
            }
            Reveal(_revealed, true);
            _revealed++;
            _timer = 0.35f;
            if (_revealed >= 4) _after.schedule.Execute(ShowOutcome).StartingIn(500);
        }

        void Reveal(int i, bool sound)
        {
            var v = S.Votes[i];
            var (console, lamp, verdict, reason) = _consoles[i];
            console.RemoveFromClassList("console--calling");
            lamp.RemoveFromClassList("lamp--calling");
            bool go = v.Vote == Vote.Go;
            console.AddToClassList(go ? "console--go" : "console--nogo");
            lamp.AddToClassList(go ? "lamp--go" : "lamp--nogo");
            verdict.text = go ? "GO" : "NO-GO";
            verdict.EnableInClassList("go", go);
            verdict.EnableInClassList("over", !go);
            if (!go) reason.text = Loc.T(v.ReasonCode);
            else if (!v.RequirementMet && v.Requirement != null) reason.text = Loc.T("ui.rollcall.req_unmet");
            else reason.text = Loc.T("ui.rollcall.go_line");
            reason.EnableInClassList("tight", go && !v.RequirementMet);
            if (sound) { if (go) Sfx.Go(); else Sfx.NoGo(); }
        }

        void ShowOutcome()
        {
            _after.Clear();
            _after.AddToClassList("rc-after--on"); // 結果出來才顯示底板
            bool allGo = S.AllGo;
            var box = Ui.El("rc-outcome", "fade-in").AddTo(_after);
            Ui.AnimateIn(box, "fade-in--on");
            if (allGo)
            {
                Ui.Text(Loc.T("ui.rollcall.all_go"), "h2", "go").AddTo(box);
                if (S.FirstCallAllGo) Ui.Text(Loc.T("ui.rollcall.first_call_bonus"), "small", "dim").AddTo(box);
                var unmet = S.Votes.Where(v => !v.RequirementMet && v.Requirement != null).ToList();
                if (unmet.Count > 0) Ui.Text(Loc.T("ui.rollcall.unmet_warning"), "small", "tight").AddTo(box);
                var row = Ui.Row("rc-actions").AddTo(box);
                Ui.Btn(Loc.T("ui.rollcall.back"), BackToDesign, "btn-ghost").AddTo(row);
                Ui.Btn(Loc.T("ui.rollcall.launch"), Launch, "btn-go", "btn-big").AddTo(row);
            }
            else
            {
                Ui.Text(Loc.T("ui.rollcall.hold"), "h2", "over").AddTo(box);
                Ui.Text(Loc.T("ui.rollcall.hold_desc"), "small", "dim").AddTo(box);
                var row = Ui.Row("rc-actions").AddTo(box);
                Ui.Btn(Loc.T("ui.rollcall.back"), BackToDesign, "btn-primary", "btn-big").AddTo(row);
                Sfx.Alert();
            }
        }

        static void BackToDesign()
        {
            S.SetPhase(Phase.Design);
            SceneFlow.Go(SceneFlow.DesignRoom);
        }

        static void Launch()
        {
            Sfx.Confirm();
            S.Launch();
            S.SetPhase(Phase.Launch);
            SceneFlow.Go(SceneFlow.Launch, 0.8f);
        }
    }
}
