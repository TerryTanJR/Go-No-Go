using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 營運回合的倒數與狀態（畫面上方）。回合時間到時由房主結算。
    /// </summary>
    public sealed class OpsBar
    {
        readonly VisualElement _bar;
        readonly Label _turn, _timer, _event;
        int _turnShown = -1;
        bool _resolvedShown;
        float _left;

        public OpsBar(VisualElement parent)
        {
            _bar = Ui.Row("ops-bar").AddTo(parent);
            _bar.pickingMode = PickingMode.Ignore;
            _turn = Ui.Text("", "eyebrow").AddTo(_bar);
            _event = Ui.Text("", "ops-bar-event").AddTo(_bar);
            _timer = Ui.Text("", "mono", "ops-bar-timer").AddTo(_bar);
        }

        static MissionSession M => GameSession.Mission;

        public void Refresh()
        {
            var s = M.S;
            bool ops = s.Phase == SessionPhase.Ops;
            _bar.style.display = ops ? DisplayStyle.Flex : DisplayStyle.None;
            if (!ops) { _turnShown = -1; return; }
            var e = M.CurrentEvent;
            _turn.text = Loc.T("ui.ops.turn", ("n", s.OpsTurn + 1), ("total", M.Plan?.Turns ?? 0));
            if (s.OpsTurn != _turnShown)
            {
                _turnShown = s.OpsTurn;
                _resolvedShown = false;
                _left = M.Difficulty.TurnSeconds;
                Sfx.Alert();
                if (e != null)
                {
                    OpsClock.Day = e.Day;
                    OpsClock.Current = M.SampleAt(e.Day);
                    OpsClock.Trigger(e);
                }
                Hud.Toast(Loc.T("ui.ops.new_event"));
            }
            var resp = M.ResponseTo(e?.Id);
            _event.text = s.TurnResolved ? Loc.T("ui.ops.resolved")
                : M.Difficulty.EventReveal && e != null ? Loc.T("event.title." + OpsEvents.Key(e.Kind))
                : Loc.T("ui.ops.unknown_event");
            if (s.TurnResolved && !_resolvedShown) { _resolvedShown = true; Hud.Open("ops_desk"); }
        }

        public void Tick(float dt)
        {
            if (M == null || M.S.Phase != SessionPhase.Ops) return;
            var limit = M.Difficulty.TurnSeconds;
            if (limit <= 0 || M.S.TurnResolved) { _timer.text = ""; return; }
            // 個人模式打開選單（Tab / Esc）時回合計時暫停；多人時大家共用時間，不停
            if (!(Hud.Paused && !M.S.Multiplayer)) _left -= dt;
            _timer.text = Fmt.Clock(_left);
            _timer.EnableInClassList("over", _left < 20);
            if (_left <= 0 && GameSession.IsHost)
            {
                _left = 9999;
                GameSession.Send(CommandType.EndTurn);
            }
        }
    }

    /// <summary>
    /// 飛行指揮台：這回合的事件判斷、做過的操作、結算；結算後顯示結果並進下一回合。
    /// </summary>
    public sealed class OpsDeskPanel : FacPanel
    {
        public override string Title => Loc.T("ui.ops.desk");
        public override string WideClass => "modal--wide";

        protected override void Build(VisualElement body)
        {
            var s = M.S;
            if (s.Phase != SessionPhase.Ops)
            {
                Ui.Text(Loc.T(s.Phase == SessionPhase.Design ? "ui.ops.not_launched" : "ui.ops.done"), "body").AddTo(body);
                return;
            }
            var e = M.CurrentEvent;
            if (e == null) { Ui.Text(Loc.T("ui.ops.done"), "body").AddTo(body); return; }
            var resp = M.ResponseTo(e.Id);
            var diff = M.Difficulty;

            Ui.Text(Loc.T("ui.ops.turn", ("n", s.OpsTurn + 1), ("total", M.Plan.Turns)) + " · " + Loc.T("ui.ops.day", ("d", e.Day)), "eyebrow").AddTo(body);

            if (!s.TurnResolved)
            {
                // 1. 判斷
                Ui.Text(Loc.T("ui.ops.step_judge"), "h3").AddTo(body);
                if (diff.EventReveal)
                    Ui.Text(Loc.T("ui.ops.revealed", ("e", Loc.T("event.title." + OpsEvents.Key(e.Kind)))), "body", "accent").AddTo(body);
                else
                {
                    Ui.Text(Loc.T("ui.ops.judge_hint"), "small", "dim").AddTo(body);
                    var row = Ui.Row("judge-row").AddTo(body);
                    foreach (var t in diff.EventTypes)
                    {
                        var key = t;
                        Ui.Btn(Loc.T("event.title." + key), () => { GameSession.Send(CommandType.Judge, key); Sfx.Click(); }, "chip")
                            .Cls("chip--on", resp.Judged == key).AddTo(row);
                    }
                }
                // 2. 操作
                Ui.Text(Loc.T("ui.ops.step_act"), "h3").AddTo(body);
                if (resp.Actions.Count == 0) Ui.Text(Loc.T("ui.ops.no_actions"), "small", "dim").AddTo(body);
                else
                {
                    var row = Ui.Row("act-row").AddTo(body);
                    foreach (var a in resp.Actions) Ui.Text(Loc.T("ops.action." + a), "code-chip").AddTo(row);
                }
                Ui.Text(Loc.T("ui.ops.act_hint"), "small", "faint").AddTo(body);

                var actions = Ui.Row("modal-actions").AddTo(body);
                Ui.Spacer().AddTo(actions);
                Ui.Btn(Loc.T("ui.ops.end_turn"), () => { Sfx.Confirm(); GameSession.Send(CommandType.EndTurn); Hud.Signal("end_turn"); }, "btn-primary", "btn-big").AddTo(actions);
                return;
            }

            // 結算結果
            var o = M.CurrentOutcome;
            Ui.Text(Loc.T("ui.ops.result"), "h3").AddTo(body);
            Ui.Text(Loc.T("ui.ops.was", ("e", Loc.T("event.title." + OpsEvents.Key(e.Kind)))), "body", "accent").AddTo(body);
            if (o != null)
            {
                bool judgedOk = o.JudgedCorrect;
                Ui.Text(Loc.T(judgedOk ? "ui.ops.judged_ok" : "ui.ops.judged_wrong"), "body", judgedOk ? "go" : "over").AddTo(body);
                Ui.Text(Loc.T("ui.ops.steps", ("done", o.StepsDone), ("total", o.StepsRequired)), "body", o.StepsDone == o.StepsRequired ? "go" : "tight").AddTo(body);
                if (o.WrongActions.Count > 0)
                    Ui.Text(Loc.T("ui.ops.wrong", ("list", string.Join("、", o.WrongActions.Select(a => Loc.T("ops.action." + a))))), "small", "tight").AddTo(body);
                Ui.Text(Loc.T(o.ResultCode, ("part", "part:" + (o.AffectedPartId ?? ""))), "body").AddTo(body);
                if (o.LostDays > 0) Ui.Text(Loc.T("ui.ops.lost_days", ("d", o.LostDays)), "small", "dim").AddTo(body);
                if (o.ScienceDelta > 0) Ui.Text(Loc.T("ui.ops.science_bonus", ("v", Fmt.N(o.ScienceDelta, 1))), "small", "go").AddTo(body);
                var steps = Procedures.Required(e.Kind);
                Ui.Text(Loc.T("ui.ops.correct_steps", ("list", string.Join(" → ", steps.Select(a => Loc.T("ops.action." + a))))), "small", "faint").AddTo(body);
            }
            var row2 = Ui.Row("modal-actions").AddTo(body);
            Ui.Spacer().AddTo(row2);
            int turn = s.OpsTurn;
            bool last = turn + 1 >= (M.Plan?.Events.Count ?? 0);
            Ui.Btn(Loc.T(last ? "ui.ops.to_report" : "ui.ops.next_turn"), () => { Sfx.Confirm(); GameSession.Send(CommandType.NextTurn, "", turn); },
                "btn-primary", "btn-big").AddTo(row2);
        }
    }

    /// <summary>四個營運控制台之一：讀數 + 這台能做的操作。</summary>
    public sealed class OpsConsolePanel : FacPanel
    {
        readonly string _console;
        public OpsConsolePanel(string console) { _console = console; }

        public override string Title => Loc.T("ops.console." + _console);

        protected override void Build(VisualElement body)
        {
            var s = M.S;
            var e = s.Phase == SessionPhase.Ops ? M.CurrentEvent : null;
            var l = s.LastLaunch;
            var design = l?.Design ?? s.Reported;
            var ev = Evaluator.EvaluateDesign(design, D);
            var sample = e != null ? M.SampleAt(e.Day) : null;
            // 沒有事件時顯示正常讀數
            var readings = OpsTelemetry.For(_console, e, ev, design, D, sample);
            var table = Ui.El("readings").AddTo(body);
            foreach (var r in readings)
            {
                var row = Ui.Row("reading").Cls("reading--alert", r.Alert && e != null).AddTo(table);
                string name = r.Key.StartsWith("part:") ? (D.Part(r.Key.Substring(5))?.Code + " " + Loc.Part(r.Key.Substring(5))) : Loc.T(r.Key);
                Ui.Text(name, "reading-k").AddTo(row);
                Ui.Spacer().AddTo(row);
                string val = r.Value.StartsWith("opp.") ? Loc.T(r.Value) : r.Value;
                Ui.Text(val + (string.IsNullOrEmpty(r.Unit) ? "" : " " + r.Unit), "mono", "reading-v").AddTo(row);
            }

            if (s.Phase != SessionPhase.Ops || e == null)
            {
                Ui.Text(Loc.T("ui.ops.console_idle"), "small", "dim").AddTo(body);
                return;
            }
            var resp = M.ResponseTo(e.Id);
            Ui.Text(Loc.T("ui.ops.actions"), "eyebrow", "ops-actions-title").AddTo(body);
            var acts = Ui.Row("ops-actions").AddTo(body);
            foreach (var a in OpsConsoles.Actions(_console))
            {
                var act = a;
                bool done = resp.Actions.Contains(a);
                var b = Ui.Btn(Loc.T("ops.action." + a) + (done ? "  ✔" : ""), () =>
                {
                    GameSession.Send(CommandType.Act, act);
                    Sfx.Confirm();
                    Hud.Signal("act:" + act);
                }, "btn-primary").AddTo(acts);
                b.SetEnabled(!done && !s.TurnResolved);
            }
            Ui.Text(Loc.T("ui.ops.irreversible"), "small", "faint").AddTo(body);
        }
    }

    // ======================================================================== 大廳

    public sealed class LobbyPanel : FacPanel
    {
        public override string Title => Loc.T("ui.lobby.title");
        public override string WideClass => "modal--wide";

        protected override void Build(VisualElement body) => LobbyView.Build(body, false);
    }

    public static class LobbyView
    {
        public static void Build(VisualElement body, bool wall)
        {
            var m = GameSession.Mission;
            var link = GameSession.Link;
            var head = Ui.Row("lobby-head").AddTo(body);
            bool ip = System.Net.IPAddress.TryParse(link?.RoomCode ?? "", out _);
            Ui.Text(Loc.T(ip ? "ui.lobby.ip" : "ui.lobby.code"), "small", "dim").AddTo(head);
            Ui.Text(link?.RoomCode ?? "", "h2", "mono", "lobby-code").AddTo(head);
            Ui.Spacer().AddTo(head);
            Ui.Text(Loc.T(m.Scenario.Name) + " · " + Loc.T(m.Difficulty.Name), "h3").AddTo(head);

            var list = Ui.El("lobby-list").AddTo(body);
            foreach (var p in m.S.Players)
            {
                var row = Ui.Row("lobby-player").AddTo(list);
                Ui.Text((p.Id == GameSession.LocalPlayer ? "▶ " : "") + p.Look.Name, "h3").AddTo(row);
                Ui.Spacer().AddTo(row);
                if (!wall && p.Id == GameSession.LocalPlayer)
                {
                    Ui.Btn(Loc.T("ui.area.assembly"), () => GameSession.Send(CommandType.SetStation, "", 0), "chip").Cls("chip--on", p.Station == Station.Assembly).AddTo(row);
                    Ui.Btn(Loc.T("ui.area.control"), () => GameSession.Send(CommandType.SetStation, "", 1), "chip").Cls("chip--on", p.Station == Station.Control).AddTo(row);
                }
                else Ui.Text(Loc.T(p.Station == Station.Assembly ? "ui.area.assembly" : "ui.area.control"), "small", "accent").AddTo(row);
            }
            Ui.Text(Loc.T("ui.lobby.split", ("n", m.S.Players.Count)), "small", "dim").AddTo(body);

            // 嚴格分工：房主決定；開了之後組裝區不能用中控室的控制台，反過來也一樣，門也鎖上
            var strict = Ui.Row("lobby-strict").AddTo(body);
            var col = Ui.El().AddTo(strict);
            col.style.flexShrink = 1;
            Ui.Text(Loc.T("ui.lobby.strict"), "h3").AddTo(col);
            Ui.Text(Loc.T("ui.lobby.strict_body"), "small", "dim").AddTo(col);
            Ui.Spacer().AddTo(strict);
            if (!wall && GameSession.IsHost)
            {
                Ui.Btn(Loc.T("ui.lobby.strict_off"), () => GameSession.Send(CommandType.SetStrict, "", 0), "chip").Cls("chip--on", !m.S.StrictRoles).AddTo(strict);
                Ui.Btn(Loc.T("ui.lobby.strict_on"), () => GameSession.Send(CommandType.SetStrict, "", 1), "chip").Cls("chip--on", m.S.StrictRoles).AddTo(strict);
            }
            else Ui.Text(Loc.T(m.S.StrictRoles ? "ui.lobby.strict_on" : "ui.lobby.strict_off"), "h3", m.S.StrictRoles ? "tight" : "dim").AddTo(strict);
            if (m.S.StrictRoles && !(m.S.Players.Any(p => p.Station == Station.Assembly) && m.S.Players.Any(p => p.Station == Station.Control)))
                Ui.Text(Loc.T("err.strict_need_both"), "small", "tight").AddTo(body);
            if (wall) return;
            var actions = Ui.Row("modal-actions").AddTo(body);
            Ui.Btn(Loc.T("ui.lobby.leave"), () => GameSession.EndMission(), "btn-ghost").AddTo(actions);
            Ui.Spacer().AddTo(actions);
            if (GameSession.IsHost)
                Ui.Btn(Loc.T("ui.lobby.start"), () => { Sfx.Go(); GameSession.Send(CommandType.StartMission); }, "btn-go", "btn-big").AddTo(actions);
            else Ui.Text(Loc.T("ui.lobby.waiting"), "small", "dim").AddTo(actions);
        }
    }
}
