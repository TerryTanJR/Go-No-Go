using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 發射 HUD：倒數計時、遙測、播報紀錄、下一個事件、播放速度（×1／×5／×10／×50，鍵盤 1–4，大家共用）、
    /// 跳過鈕，播完顯示結果。
    /// 畫面本身不決定結果——結果在按下發射時就由核心算好了（SessionState.LastLaunch），3D 只是演出來。
    /// </summary>
    public sealed class LaunchScreen : GameScreen
    {
        Label _clock, _alt, _vel, _thr, _stage, _next;
        VisualElement _log, _outcome, _speedRow;
        Button _skip;
        LaunchTimeline _tl;
        int _speedShown = -1;
        readonly List<string> _lines = new List<string>();
        bool _shownOutcome;
        float _selfTimer;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.None);
            GameSession.EnsureMission();
            if (M.S.LastLaunch == null) { GameSession.Send(CommandType.RunTest); GameSession.Send(CommandType.Launch); }
            if (!LaunchFeed.DirectorActive && _selfTimer == 0) LaunchFeed.Reset();

            var head = Ui.El("launch-head").AddTo(Root);
            Ui.Text(Loc.T("ui.launch.eyebrow") + " · " + Loc.Launcher(M.S.LastLaunch?.Design.LauncherId ?? ""), "eyebrow").AddTo(head);
            _clock = Ui.Text("T-00:00:10", "launch-clock", "mono").AddTo(head);
            _stage = Ui.Text("", "launch-stage").AddTo(head);
            _next = Ui.Text("", "launch-next", "mono").AddTo(head);
            var last = M.S.LastLaunch;
            _tl = last != null ? LaunchTimeline.Build(D, last.Design, last.Outcome, last.ReasonCode ?? "") : null;

            var tele = Ui.El("panel", "launch-tele").AddTo(Root);
            _alt = Row(tele, "ui.launch.alt", "km");
            _vel = Row(tele, "ui.launch.vel", "km/s");
            _thr = Row(tele, "ui.launch.thr", "%");

            _log = Ui.El("launch-log").AddTo(Root);
            foreach (var l in _lines) AddLine(l, false);

            // 播放速度與跳過：升空後才有作用；多人時大家看同一個速度（存在共用的狀態裡）
            var actions = Ui.Row("launch-actions").AddTo(Root);
            Ui.Spacer().AddTo(actions);
            var speed = Ui.El("launch-speed").AddTo(actions);
            var srow = Ui.Row().AddTo(speed);
            Ui.Text(Loc.T("ui.launch.speed"), "small", "dim", "launch-speed-k").AddTo(srow);
            _speedRow = Ui.Row().AddTo(srow);
            _speedShown = -1;
            if (M.S.Multiplayer) Ui.Text(Loc.T("ui.launch.speed_shared"), "small", "faint").AddTo(speed);
            _skip = Ui.Btn(Loc.T("ui.launch.skip"), Skip, "btn-ghost", "launch-skip").AddTo(actions);

            _outcome = Ui.El("launch-outcome").AddTo(Root);
            _outcome.style.display = DisplayStyle.None;
            if (_shownOutcome) ShowOutcome(false);

            On(() => LaunchFeed.Callout += OnCallout, () => LaunchFeed.Callout -= OnCallout);
        }

        static Label Row(VisualElement parent, string key, string unit)
        {
            var r = Ui.Row("tele-row").AddTo(parent);
            Ui.Text(Loc.T(key), "small", "dim", "tele-k").AddTo(r);
            Ui.Spacer().AddTo(r);
            var v = Ui.Text("0", "mono", "tele-v").AddTo(r);
            Ui.Text(unit, "small", "faint", "tele-u").AddTo(r);
            return v;
        }

        static void SetSpeed(int x)
        {
            if (M.S.LaunchSpeed == x || LaunchFeed.Finished) return;
            Sfx.Click();
            GameSession.Send(CommandType.SetLaunchSpeed, "", x);
        }

        static void Skip()
        {
            if (M.S.LaunchSkipped || LaunchFeed.Finished) return;
            Sfx.Click();
            GameSession.Send(CommandType.SkipLaunch);
        }

        void DrawSpeed()
        {
            int now = M.S.LaunchSpeed;
            if (now == _speedShown) return;
            _speedShown = now;
            _speedRow.Clear();
            for (int i = 0; i < SessionRules.LaunchSpeeds.Length; i++)
            {
                int x = SessionRules.LaunchSpeeds[i];
                Ui.Btn($"×{x}", () => SetSpeed(x), "chip", "mono").Cls("chip--on", x == now).AddTo(_speedRow).tooltip = (i + 1).ToString();
            }
        }

        /// <summary>播報字串鍵後面可以帶「#節數」（第 {n} 節點火）。</summary>
        static string CallText(string key)
        {
            int hash = key.IndexOf('#');
            if (hash < 0) return Loc.T(key);
            int.TryParse(key.Substring(hash + 1), out var n);
            return Loc.T(key.Substring(0, hash), ("n", n));
        }

        /// <summary>下一個事件（失敗的事件不先爆雷：速度不足顯示成關機、整流罩沒分離顯示成整流罩分離）。</summary>
        string NextText(float t)
        {
            if (_tl == null || t < 0 || LaunchFeed.Finished) return "";
            var e = _tl.Next(t);
            if (e == null) return "";
            string id = e.Id == "underspeed" ? "seco" : e.Id == "fairing_fail" ? "fairing" : e.Id;
            if (id == "anomaly" || id == "eclipse" || id == "signal_lost") return "";
            // 有專屬播報的（踢昇級、Taurus 的第 n 節）直接用播報的說法
            bool own = e.Call.Contains(".kick_") || e.Call.Contains(".stage_n_") || e.Call.Contains(".stage0_");
            string name = own ? CallText(e.N > 0 ? e.Call + "#" + e.N : e.Call).TrimEnd('。', '.') : Loc.T("ui.launch.ev." + id);
            int left = Mathf.Max(0, Mathf.CeilToInt((float)e.T - t));
            return Loc.T("ui.launch.next") + "  ·  " + name + "  ·  " + LaunchClock.Format(e.T) + "  (−" + left / 60 + ":" + (left % 60).ToString("00") + ")";
        }

        void OnCallout(string key)
        {
            _lines.Add(key);
            AddLine(key, true);
        }

        void AddLine(string key, bool animate)
        {
            var l = Ui.Text(CallText(key), "launch-line", "fade-in").AddTo(_log);
            if (animate) { Ui.AnimateIn(l, "fade-in--on"); Sfx.Tick(); }
            else l.AddToClassList("fade-in--on");
            while (_log.childCount > 6) _log.RemoveAt(0);
        }

        public override void Tick(float dt)
        {
            if (!LaunchFeed.DirectorActive)
            {
                // 沒有 3D 導演（例如單獨測 UI）：自己跑一段簡化的倒數
                _selfTimer += dt;
                LaunchFeed.T = -10f + _selfTimer;
                if (LaunchFeed.T > 0) { LaunchFeed.AltitudeKm = LaunchFeed.T * LaunchFeed.T * 0.02f; LaunchFeed.VelocityKms = LaunchFeed.T * 0.05f; LaunchFeed.Throttle = 1; }
                if (LaunchFeed.T > 15f) LaunchFeed.Finished = true;
            }

            float T = LaunchFeed.T;
            // 倒數無條件進位：念「十」的那一刻畫面才跳到 10（截斷的話會早一秒）
            _clock.text = LaunchClock.Format(T);
            _alt.text = LaunchFeed.AltitudeKm.ToString("0.0");
            _vel.text = LaunchFeed.VelocityKms.ToString("0.00");
            _thr.text = (LaunchFeed.Throttle * 100).ToString("0");
            _stage.text = string.IsNullOrEmpty(LaunchFeed.StageKey) ? "" : Loc.T(LaunchFeed.StageKey);
            _next.text = NextText(T);
            DrawSpeed();
            _skip.SetEnabled(!M.S.LaunchSkipped && !LaunchFeed.Finished);
            var kb = Keyboard.current;
            if (kb != null && !Hud.Paused && !UIRoot.IsTyping)
            {
                if (kb.digit1Key.wasPressedThisFrame) SetSpeed(SessionRules.LaunchSpeeds[0]);
                else if (kb.digit2Key.wasPressedThisFrame) SetSpeed(SessionRules.LaunchSpeeds[1]);
                else if (kb.digit3Key.wasPressedThisFrame) SetSpeed(SessionRules.LaunchSpeeds[2]);
                else if (kb.digit4Key.wasPressedThisFrame) SetSpeed(SessionRules.LaunchSpeeds[3]);
            }

            if (LaunchFeed.Finished && !_shownOutcome)
            {
                _shownOutcome = true;
                ShowOutcome(true);
            }
        }

        void ShowOutcome(bool sound)
        {
            var l = M.S.LastLaunch;
            _outcome.Clear();
            _outcome.style.display = DisplayStyle.Flex;
            var card = Ui.El("panel", "outcome-card", "fade-in").AddTo(_outcome);
            Ui.AnimateIn(card, "fade-in--on");
            bool ok = l != null && l.Reached;
            Ui.Text(Loc.T("ui.launch.result.eyebrow"), "eyebrow").AddTo(card);
            Ui.Text(Loc.T(FailureBoardView.OutcomeKey(l?.Outcome ?? LaunchOutcome.Nominal)), "h2", ok ? "go" : "over").AddTo(card);
            if (l != null) Ui.Text(Loc.T(l.ReasonCode), "body").AddTo(card);
            if (!ok) Ui.Text(Loc.T("ui.launch.redesign_note"), "small", "dim").AddTo(card);
            var row = Ui.Row("outcome-actions").AddTo(card);
            Ui.Spacer().AddTo(row);
            if (GameSession.IsHost)
                Ui.Btn(Loc.T(ok ? "ui.launch.to_ops" : "ui.launch.to_redesign"), Continue, ok ? "btn-go" : "btn-primary", "btn-big").AddTo(row);
            else Ui.Text(Loc.T("ui.launch.wait_host"), "small", "dim").AddTo(row);
            if (sound) { if (ok) Sfx.Go(); else Sfx.NoGo(); }
        }

        static void Continue()
        {
            Sfx.Confirm();
            GameSession.Send(CommandType.FinishLaunch);
        }
    }
}
