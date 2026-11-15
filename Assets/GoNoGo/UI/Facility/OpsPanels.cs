using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 營運回合的倒數與狀態（畫面上方）。回合時間到時由房主結算。
    /// </summary>
    public sealed class OpsBar
    {
        VisualElement _bar;
        Label _turn, _timer, _event;
        int _turnShown = -1;
        bool _resolvedShown;
        float _left;
        int _lastSecond = -1;
        /// <summary>計時器上現在寫的是什麼：剩幾秒，或下面兩個特殊值（空白、「不計時」）。一樣就不重組字串。</summary>
        int _timerShown = TimerBlank;
        const int TimerBlank = int.MinValue, TimerNone = int.MinValue + 1;
        /// <summary>新事件的警鈴最多響幾秒；有人先動手處理就提早停。</summary>
        const float AlarmSeconds = 8f;

        /// <summary>
        /// 建立橫條的元素（畫面每次重建都要重新建，包括切語言）。回合的狀態——現在第幾回合、剩幾秒、警報響過沒——
        /// 留在這個物件上，不跟著重建歸零。
        /// </summary>
        public void Attach(VisualElement parent)
        {
            _bar = Ui.Row("ops-bar").AddTo(parent);
            _bar.pickingMode = PickingMode.Ignore;
            _turn = Ui.Text("", "eyebrow").AddTo(_bar);
            _event = Ui.Text("", "ops-bar-event").AddTo(_bar);
            _timer = Ui.Text("", "mono", "ops-bar-timer").AddTo(_bar);
            _timerShown = TimerBlank;
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
                _lastSecond = -1;
                Sfx.StartAlarm(AlarmSeconds);
                if (e != null)
                {
                    OpsClock.Day = e.Day;
                    OpsClock.Current = M.SampleAt(e.Day);
                    OpsClock.Trigger(e);
                }
                Hud.Toast(Loc.T("ui.ops.new_event"));
                // 外星人彩蛋：不用判斷，直接進遊戲
                if (OpsMinigames.Enabled && e != null && e.Kind == EventKind.Alien) Hud.Open("minigame");
            }
            _event.text = s.TurnResolved ? Loc.T("ui.ops.resolved")
                : (M.Difficulty.EventReveal || e?.Kind == EventKind.Alien) && e != null ? Loc.T("event.title." + OpsEvents.Key(e.Kind))
                : Loc.T("ui.ops.unknown_event");
            if (s.TurnResolved || (e?.Id != null && s.Responses.TryGetValue(e.Id, out var r) && r.Started)) Sfx.StopAlarm();
            if (s.TurnResolved && !_resolvedShown)
            {
                _resolvedShown = true;
                // 結算的那一刻：處置成功一段上行的音，沒處置好一段下行的音
                var o = M.CurrentOutcome;
                bool good = o != null && o.JudgedCorrect && o.StepsRequired > 0 && o.StepsDone >= o.StepsRequired && o.WrongActions.Count == 0;
                if (good) Sfx.Success(); else Sfx.Fail();
                Hud.Open("ops_desk");
            }
        }

        public void Tick(float dt)
        {
            if (M == null || M.S.Phase != SessionPhase.Ops) return;
            var diff = M.Difficulty;
            if (diff.TurnSeconds <= 0 || M.S.TurnResolved) { ShowTimer(TimerBlank); return; }
            // 判斷完、進入處置小遊戲之後不再倒數（小遊戲沒有計時）
            var cur = M.CurrentEvent;
            if (OpsMinigames.Enabled && cur != null && cur.Id != null && M.S.Responses.TryGetValue(cur.Id, out var resp)
                && !string.IsNullOrEmpty(resp.Judged) && !diff.EventReveal)
            { ShowTimer(TimerNone); _timer.EnableInClassList("over", false); return; }
            // 個人模式打開選單（Tab / Esc）時回合計時暫停；多人時大家共用時間，不停
            if (!(Hud.Paused && !M.S.Multiplayer)) _left -= dt;
            // 畫面上的時間只到秒（Fmt.Clock 也是無條件進位到秒）：秒數變了才重組字串
            int sec = Mathf.CeilToInt(_left);
            ShowTimer(sec);
            _timer.EnableInClassList("over", _left < 20);
            // 最後 10 秒：每秒響一聲
            if (sec != _lastSecond)
            {
                if (sec <= 10 && sec > 0 && _lastSecond > 0) Sfx.Countdown();
                _lastSecond = sec;
            }
            if (_left <= 0 && GameSession.IsHost)
            {
                _left = 9999;
                GameSession.Send(CommandType.EndTurn);
            }
        }

        void ShowTimer(int key)
        {
            if (key == _timerShown) return;
            _timerShown = key;
            _timer.text = key == TimerBlank ? "" : key == TimerNone ? Loc.T("mini.no_timer") : Fmt.Clock(key);
        }
    }

    /// <summary>
    /// 飛行指揮台：這回合的事件判斷、做過的操作、結算；結算後顯示結果並進下一回合。
    /// </summary>
    public sealed class OpsDeskPanel : FacPanel
    {
        public override string Title => Loc.T("ui.ops.desk");
        public override string WideClass => "modal--wide";

        /// <summary>步驟 2：依判斷的事件打處置小遊戲；全隊都交成績（或房主強制結算）才算完。</summary>
        void BuildTreat(VisualElement body, EventResponse resp)
        {
            Ui.Text(Loc.T("ui.ops.step_treat"), "h3").AddTo(body);
            var def = Minigames.For(resp.Judged);
            if (def == null)
            {
                Ui.Text(Loc.T("ui.ops.treat_wait_judge"), "small", "dim").AddTo(body);
            }
            else
            {
                int players = System.Math.Max(1, M.S.Players.Count);
                int me = GameSession.LocalPlayer;
                bool mine = resp.Mini.ContainsKey(me);
                var card = Ui.El("mini-link").AddTo(body);
                Ui.Text(Loc.T("mini." + def.Id + ".name"), "h3").AddTo(card);
                OpsGoal.Bar(card, def, players, true);
                if (players > 1) Ui.Text(Loc.T("ui.ops.treat_done", ("a", resp.Mini.Count), ("n", players)), "body", "dim").AddTo(card);
                if (mine)
                    Ui.Text(Loc.T("ui.ops.treat_mine", ("v", resp.Mini[me] / 10)), "h3", MinigameRules.PlayerPassed(def, resp.Mini[me]) ? "go" : "over").AddTo(card);
                var play = Ui.Btn(Loc.T(mine ? "ui.ops.treat_waiting" : "ui.ops.treat_start"), () => Hud.Open("minigame"), "btn-primary", "btn-big").AddTo(card);
                play.SetEnabled(!mine);
            }
            var actions = Ui.Row("modal-actions").AddTo(body);
            Ui.Spacer().AddTo(actions);
            if (!M.S.Multiplayer || GameSession.IsHost)
                Ui.Btn(Loc.T(resp.Mini.Count > 0 ? "ui.ops.force_end" : "ui.ops.skip_treat"), () => { Sfx.Confirm(); GameSession.Send(CommandType.EndTurn); Hud.Signal("end_turn"); }, "btn-ghost").AddTo(actions);
        }

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
                // 這回合怎樣算過關：寫在最上面
                if (OpsMinigames.Enabled)
                {
                    var goal = Ui.El("turn-goal").AddTo(body);
                    Ui.Text(Loc.T("ui.ops.turn_goal_k"), "goalbar-k").AddTo(goal);
                    bool known = diff.EventReveal || e.Kind == EventKind.Alien;
                    bool judged = known || !string.IsNullOrEmpty(resp.Judged);
                    var steps = Ui.Row("turn-goal-steps").AddTo(goal);
                    if (!known) Ui.Text((judged ? "✔ " : "① ") + Loc.T("ui.ops.turn_goal_judge"), "turn-goal-step").Cls("turn-goal-step--done", judged).AddTo(steps);
                    bool played = resp.Mini.ContainsKey(GameSession.LocalPlayer);
                    Ui.Text((played ? "✔ " : known ? "① " : "② ") + Loc.T("ui.ops.turn_goal_treat"), "turn-goal-step").Cls("turn-goal-step--done", played).AddTo(steps);
                }
                // 1. 判斷
                Ui.Text(Loc.T("ui.ops.step_judge"), "h3").AddTo(body);
                if (diff.EventReveal || e.Kind == EventKind.Alien)
                    Ui.Text(Loc.T("ui.ops.revealed", ("e", Loc.T("event.title." + OpsEvents.Key(e.Kind)))), "body", "accent").AddTo(body);
                else
                {
                    Ui.Text(Loc.T("ui.ops.judge_hint"), "small", "dim").AddTo(body);
                    var row = Ui.Row("judge-row").AddTo(body);
                    var types = new System.Collections.Generic.List<string>(GameData.EventTypes(M.Scenario, diff));
                    bool locked = OpsMinigames.Enabled && resp.Mini.Count > 0; // 有人開始處置了就不能改判斷
                    foreach (var t in types)
                    {
                        var key = t;
                        var chip = Ui.Btn(Loc.T("event.title." + key), () => { GameSession.Send(CommandType.Judge, key); Sfx.Confirm(); }, "chip")
                            .Cls("chip--on", resp.Judged == key).AddTo(row);
                        chip.SetEnabled(!locked);
                    }
                }
                if (OpsMinigames.Enabled) { BuildTreat(body, resp); return; }
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
                if (o.MiniGame != "")
                {
                    string gname = Loc.T("mini." + o.MiniGame + ".name");
                    if (!o.MiniPlayed) Ui.Text(Loc.T("ui.ops.mini_none", ("g", gname)), "body", "over").AddTo(body);
                    else if (!o.JudgedCorrect) Ui.Text(Loc.T("ui.ops.mini_wrong_game", ("g", gname)), "body", "over").AddTo(body);
                    else Ui.Text(Loc.T(o.MiniPassed ? "ui.ops.mini_pass" : "ui.ops.mini_fail", ("g", gname)), "body", o.MiniPassed ? "go" : "over").AddTo(body);
                }
                Ui.Text(Loc.T("ui.ops.steps", ("done", o.StepsDone), ("total", o.StepsRequired)), "body", o.StepsDone == o.StepsRequired ? "go" : "tight").AddTo(body);
                if (o.WrongActions.Count > 0)
                    Ui.Text(Loc.T("ui.ops.wrong", ("list", string.Join("、", o.WrongActions.Select(a => Loc.T("ops.action." + a)))),
                        ("pts", Fmt.N(M.Difficulty.ExtraActionPenalty * o.WrongActions.Count))), "small", "tight").AddTo(body);
                if (o.ConflictedActions.Count > 0)
                    Ui.Text(Loc.T("ui.ops.conflicted", ("list", string.Join("、", o.ConflictedActions.Select(a => Loc.T("ops.action." + a))))), "small", "over").AddTo(body);
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

    /// <summary>
    /// 四個營運控制台之一：這個子系統的讀數。處置在飛行指揮台（小遊戲），這裡只留一顆「去飛行指揮台」的按鈕；
    /// 這台能做的操作只有關掉 <see cref="OpsMinigames.Enabled"/>（舊流程，除錯用）時才列出來。
    /// </summary>
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
            var design = l?.Design ?? s.Bench;
            var ev = Evaluator.EvaluateDesign(design, D, s.DifficultyId, false); // 讀數照這一局的難度
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
                string val = r.Value.StartsWith("opp.") || r.Value.StartsWith("site.") ? Loc.T(r.Value)
                    : r.Value == "NO DATA" ? Loc.T("ui.ops.no_data") : r.Value;
                Ui.Text(val + (string.IsNullOrEmpty(r.Unit) ? "" : " " + r.Unit), "mono", "reading-v").AddTo(row);
            }

            if (s.Phase != SessionPhase.Ops || e == null)
            {
                Ui.Text(Loc.T("ui.ops.console_idle"), "small", "dim").AddTo(body);
                return;
            }
            var resp = M.ResponseTo(e.Id);
            if (OpsMinigames.Enabled)
            {
                Ui.Text(Loc.T("ui.ops.console_mini"), "small", "dim").AddTo(body);
                Ui.Btn(Loc.T("ui.ops.open_desk"), () => Hud.Open("ops_desk"), "btn-primary").AddTo(body);
                return;
            }
            Ui.Text(Loc.T("ui.ops.actions"), "eyebrow", "ops-actions-title").AddTo(body);
            var acts = Ui.Row("ops-actions").AddTo(body);
            bool fire = M.Scenario != null && M.Scenario.FireAlert;
            foreach (var a in OpsConsoles.Actions(_console))
            {
                if (OpsActions.FireOnly(a) && !fire) continue; // 火點操作只在野火關卡出現
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
        /// <summary>只有房主一個人時永遠不算就緒：要等其他人加入才能開始（和 SessionRules 的 err.need_players 一致）。</summary>
        public static bool Alone(SessionState s) => s.Players.Count < 2;

        /// <summary>可以開始了：每個人都按了準備。不分組：所有人開局都在中控室。</summary>
        public static bool AllReady(SessionState s) => !Alone(s) && s.Players.All(p => p.Ready);

        /// <summary>名單：房間裡有誰、誰準備好了。</summary>
        public static void Roster(VisualElement parent)
        {
            var s = GameSession.Mission.S;
            var col = Ui.El("lobby-team", "lobby-team--roster").AddTo(parent);
            Ui.Text(Loc.T("ui.lobby.roster"), "lobby-team-title").AddTo(col);
            foreach (var p in s.Players)
            {
                bool me = p.Id == GameSession.LocalPlayer;
                var r = Ui.Row("lobby-player").Cls("lobby-player--me", me).AddTo(col);
                Ui.Text((me ? "▶ " : "") + SelectView.PlayerName(p.Id), "lobby-pname").AddTo(r);
                Ui.Spacer().AddTo(r);
                Ui.Text(p.Ready ? "✔ " + Loc.T("ui.lobby.ready") : "…", "lobby-pstate", p.Ready ? "go" : "dim").AddTo(r);
            }
            if (s.Players.Count == 0) Ui.Text("—", "lobby-pname", "faint").AddTo(col);
        }

        /// <summary>牆上大螢幕（wall）和中央控制台面板（!wall）共用：房間碼、任務、名單；面板多了離開。</summary>
        public static void Build(VisualElement body, bool wall)
        {
            var m = GameSession.Mission;
            var link = GameSession.Link;
            var s = m.S;
            var head = Ui.Row("lobby-head").AddTo(body);
            bool ip = System.Net.IPAddress.TryParse(link?.RoomCode ?? "", out _);
            var codeBox = Ui.El().AddTo(head);
            Ui.Text(Loc.T(ip ? "ui.lobby.ip" : "ui.lobby.code"), "small", "dim").AddTo(codeBox);
            Ui.Text(link?.RoomCode ?? "", "lobby-code", "mono").AddTo(codeBox);
            Ui.Spacer().AddTo(head);
            var mis = Ui.El("lobby-mission").AddTo(head);
            Ui.Text(Loc.T(m.Scenario.Name), "h2").AddTo(mis);
            if (!m.IsHistorical) Ui.Text(Loc.T(m.Difficulty.Name), "small", "dim").AddTo(mis);   // 歷史關卡不分難度

            Roster(body);
            int ready = s.Players.Count(p => p.Ready);
            if (Alone(s)) Ui.Text(Loc.T("err.need_players"), "lobby-count", "tight").AddTo(body);
            else Ui.Text(Loc.T("ui.lobby.ready_count", ("r", ready), ("n", s.Players.Count)), "lobby-count", LobbyView.AllReady(s) ? "go" : "dim").AddTo(body);

            if (wall) return;
            var actions = Ui.Row("modal-actions").AddTo(body);
            Ui.Btn(Loc.T("ui.lobby.leave"), () => GameSession.EndMission(), "btn-ghost").AddTo(actions);
            Ui.Spacer().AddTo(actions);
            if (GameSession.IsHost && AllReady(s))
                Ui.Btn(Loc.T("ui.lobby.start_btn"), () => { Sfx.Go(); GameSession.Send(CommandType.StartMission); }, "btn-go", "btn-big").AddTo(actions);
        }

        /// <summary>大廳兩側牆上的看板：任務流程的說明（side = "select" 選件與找線索、"assemble" 組裝與發射）和名單。</summary>
        public static void SideBoard(VisualElement body, string side)
        {
            body.AddToClassList("team-board");
            body.AddToClassList("team-board--" + (side == "assemble" ? "assembly" : "control"));
            Ui.Text(Loc.T("ui.lobby.board." + side), "team-board-title").AddTo(body);
            Ui.Text(Loc.T("ui.lobby.board." + side + ".body"), "team-board-role").AddTo(body);
            foreach (var p in GameSession.Mission.S.Players)
            {
                bool me = p.Id == GameSession.LocalPlayer;
                var r = Ui.Row("team-board-name").Cls("team-board-name--me", me).AddTo(body);
                Ui.Text((me ? "▶ " : "") + SelectView.PlayerName(p.Id), "team-board-pn").AddTo(r);
                Ui.Spacer().AddTo(r);
                Ui.Text(p.Ready ? "✔ " + Loc.T("ui.lobby.ready") : "…", "team-board-pn", p.Ready ? "go" : "dim").AddTo(r);
            }
        }
    }

    /// <summary>全員準備好之後，房主會跳出這個：按 Enter 或點大按鈕開始任務（Esc = 再等等）。</summary>
    public sealed class LobbyStartPanel : FacPanel
    {
        public override string Title => Loc.T("ui.lobby.allready.t");
        public override string WideClass => "modal--wide";
        public override bool CloseWithE => false;

        VisualElement _btn;
        float _t;

        protected override void Build(VisualElement body)
        {
            var wrap = Ui.El("go-wrap").AddTo(body);
            Ui.Text(Loc.T("ui.lobby.allready.t"), "go-title").AddTo(wrap);
            LobbyView.Roster(wrap);
            _btn = Ui.Btn(Loc.T("ui.lobby.start_btn"), Go, "btn-go", "btn-big", "go-btn").AddTo(wrap);
            var keys = Ui.Row("go-keys").AddTo(wrap);
            Ui.Text("Enter", "keycap").AddTo(keys);
            Ui.Text(Loc.T("ui.lobby.start_btn"), "key-label").AddTo(keys);
            Ui.Spacer().AddTo(keys);
            Ui.Text("Esc", "keycap").AddTo(keys);
            Ui.Text(Loc.T("ui.lobby.allready.wait"), "key-label").AddTo(keys);
        }

        void Go()
        {
            Sfx.Go();
            GameSession.Send(CommandType.StartMission);
        }

        public override void Tick(float dt)
        {
            _t += dt;
            if (_btn != null) _btn.style.scale = new Scale(Vector2.one * (1f + 0.035f * Mathf.Sin(_t * 4f)));
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) Go();
        }
    }
}
