using System;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 發射 HUD：倒數計時、遙測、下一個事件，播報在右下角淡入淡出，播完顯示結果。
    /// 播放速度由 3D 導演自動決定（整段約 70 秒），不能跳過也不能調速。
    /// 畫面本身不決定結果——結果在按下發射時就由核心算好了（SessionState.LastLaunch），3D 只是演出來。
    /// </summary>
    public sealed class LaunchScreen : GameScreen
    {
        Label _clock, _alt, _vel, _thr, _stage, _next, _speed, _altKey;
        bool _altMoon;
        VisualElement _calls, _outcome;
        LaunchTimeline _tl, _nominal;
        bool _shownOutcome;
        float _selfTimer;

        // 上一次寫到畫面上的值：每一幀都在更新，值沒變就不重組字串、不重設文字
        int _clockS;
        bool _clockNeg;
        float _altShown, _velShown, _thrShown;
        string _stageShown;
        double _speedShown;
        // 「下一步」：出事的時間點、目前的下一個事件（在哪個時間點找的）、上一次組好的文字
        double _anomalyT;
        TimelineEvent _nextEv, _nextTextEv;
        float _nextAt;
        int _nextLeft;
        string _nextText;

        static readonly char[] SentenceEnd = { '。', '.' };

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.None);
            Sfx.SetMusic(Sfx.Music.None); // 發射有自己的倒數和引擎聲
            GameSession.EnsureMission();
            if (M.S.LastLaunch == null) GameSession.Send(CommandType.Launch);
            if (!LaunchFeed.DirectorActive && _selfTimer == 0) LaunchFeed.Reset();

            var head = Ui.El("launch-head").AddTo(Root);
            Ui.Text(Loc.T("ui.launch.eyebrow") + " · " + Loc.Launcher(M.S.LastLaunch?.Design.LauncherId ?? ""), "eyebrow").AddTo(head);
            _clock = Ui.Text("T-00:00:10", "launch-clock", "mono").AddTo(head);
            // 加速播放時在時鐘旁邊標出倍率：時鐘一下跳十幾分鐘、地球轉很快，不說明的話看起來像壞掉
            _speed = Ui.Text("", "launch-speed", "mono").AddTo(head);
            _stage = Ui.Text("", "launch-stage").AddTo(head);
            _next = Ui.Text("", "launch-next", "mono").AddTo(head);
            var last = M.S.LastLaunch;
            _tl = last != null ? LaunchTimeline.Build(D, last.Design, last.Outcome, last.ReasonCode ?? "") : null;
            // 「下一步」照成功的飛行顯示，出事前不先爆雷
            _nominal = last != null ? LaunchTimeline.Build(D, last.Design, LaunchOutcome.Nominal, "launch.nominal") : null;
            ResetShown();

            // 播報放在右下角（放在倒數下方會擋到火箭）
            _calls = Ui.El("launch-calls").AddTo(Root);

            var tele = Ui.El("panel", "launch-tele").AddTo(Root);
            _alt = Row(tele, "ui.launch.alt", "km", out _altKey);
            _vel = Row(tele, "ui.launch.vel", "km/s", out _);
            _thr = Row(tele, "ui.launch.thr", "%", out _);
            _altMoon = false;

            _outcome = Ui.El("launch-outcome").AddTo(Root);
            _outcome.style.display = DisplayStyle.None;
            if (_shownOutcome) ShowOutcome(false);

            On(() => LaunchFeed.Callout += OnCallout, () => LaunchFeed.Callout -= OnCallout);
        }

        /// <summary>畫面重建（換語言）後標籤都是新的：忘掉上一次寫了什麼，下一幀全部重寫。</summary>
        void ResetShown()
        {
            _clockS = int.MinValue;
            _clockNeg = false;
            _altShown = _velShown = _thrShown = float.NaN;
            _stageShown = null;
            _speedShown = double.NaN;
            _nextEv = _nextTextEv = null;
            _nextAt = float.PositiveInfinity;
            _nextLeft = 0;
            _nextText = "";
            // 出事的時間點（沒出事 = 無限大）：過了之後不再顯示「下一步」
            _anomalyT = double.PositiveInfinity;
            if (_tl == null) return;
            foreach (var f in _tl.Events)
                if ((f.Id == "anomaly" || f.Id == "underspeed" || f.Id == "fairing_fail") && f.T < _anomalyT) _anomalyT = f.T;
        }

        /// <summary>兩個數字寫出來會不會一樣（正零和負零分開算，NaN 一律當成不一樣）。</summary>
        static bool Same(float a, float b) => a == b && (a != 0f || 1f / a == 1f / b);

        static Label Row(VisualElement parent, string key, string unit, out Label keyLabel)
        {
            var r = Ui.Row("tele-row").AddTo(parent);
            keyLabel = Ui.Text(Loc.T(key), "small", "dim", "tele-k").AddTo(r);
            Ui.Spacer().AddTo(r);
            var v = Ui.Text("0", "mono", "tele-v").AddTo(r);
            Ui.Text(unit, "small", "faint", "tele-u").AddTo(r);
            return v;
        }

        /// <summary>播報字串鍵後面可以帶「#節數」（第 {n} 節點火）。</summary>
        static string CallText(string key)
        {
            int hash = key.IndexOf('#');
            if (hash < 0) return Loc.T(key);
            int.TryParse(key.Substring(hash + 1), out var n);
            return Loc.T(key.Substring(0, hash), ("n", n));
        }

        /// <summary>下一個事件：照成功的飛行顯示（失敗不先爆雷），出事之後就不再顯示。</summary>
        string NextText(float t)
        {
            if (_tl == null || _nominal == null || t < 0 || LaunchFeed.Finished) return "";
            if (t >= _anomalyT) return "";
            // 下一個事件在時間走到它之前不會換：不用每一幀重找（時間倒退、或已經過了那個事件才重找）
            if (!(t >= _nextAt && (_nextEv == null || _nextEv.T > t)))
            {
                _nextEv = _nominal.Next(t);
                _nextAt = t;
            }
            var e = _nextEv;
            if (e == null) return "";
            int left = Mathf.Max(0, Mathf.CeilToInt((float)e.T - t));
            // 文字只跟「哪一個事件、還剩幾秒」有關：都沒變就用上一次組好的
            if (ReferenceEquals(e, _nextTextEv) && left == _nextLeft) return _nextText;
            _nextTextEv = e;
            _nextLeft = left;
            string id = e.Id == "underspeed" ? "seco" : e.Id == "fairing_fail" ? "fairing" : e.Id;
            if (id == "anomaly" || id == "eclipse" || id == "signal_lost") return _nextText = "";
            // 有專屬播報的（踢昇級、Taurus 的第 n 節）直接用播報的說法
            bool own = e.Call.Contains(".kick_") || e.Call.Contains(".stage_n_") || e.Call.Contains(".stage0_");
            string name = own ? CallText(e.N > 0 ? e.Call + "#" + e.N : e.Call).TrimEnd(SentenceEnd) : Loc.T("ui.launch.ev." + id);
            return _nextText = Loc.T("ui.launch.next") + "  ·  " + name + "  ·  " + LaunchClock.Format(e.T) + "  (−" + left / 60 + ":" + (left % 60).ToString("00") + ")";
        }

        const long CallHoldMs = 2600, CallFadeMs = 350; // 和 Theme.uss 的 .launch-call 淡入淡出時間一致
        const int MaxCalls = 3;

        /// <summary>播報在右下角淡入、停一下再淡出；加速時一次來好幾句就往下排，最多同時三句。</summary>
        void OnCallout(string key)
        {
            var l = Ui.Text(CallText(key).TrimEnd(SentenceEnd), "launch-call").AddTo(_calls); // 播報字幕不加句號
            Ui.AnimateIn(l, "launch-call--on");
            l.schedule.Execute(() => l.RemoveFromClassList("launch-call--on")).StartingIn(CallHoldMs);
            l.schedule.Execute(() => l.RemoveFromHierarchy()).StartingIn(CallHoldMs + CallFadeMs);
            while (_calls.childCount > MaxCalls) _calls.RemoveAt(0);
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
            // 倒數無條件進位：念「十」的那一刻畫面才跳到 10（截斷的話會早一秒）。
            // 時鐘的字只跟「顯示的秒數」和正負號有關，這兩個沒變就不用重組
            int clockS = LaunchClock.DisplaySeconds(T);
            bool clockNeg = T < 0;
            if (clockS != _clockS || clockNeg != _clockNeg)
            {
                _clockS = clockS;
                _clockNeg = clockNeg;
                _clock.text = LaunchClock.Format(T);
            }
            float alt = LaunchFeed.AltitudeKm, vel = LaunchFeed.VelocityKms, thr = LaunchFeed.Throttle;
            if (!Same(alt, _altShown)) { _altShown = alt; _alt.text = alt.ToString("0.0"); }
            if (!Same(vel, _velShown)) { _velShown = vel; _vel.text = vel.ToString("0.00"); }
            if (!Same(thr, _thrShown)) { _thrShown = thr; _thr.text = (thr * 100).ToString("0"); }
            string stageKey = LaunchFeed.StageKey;
            if (stageKey != _stageShown)
            {
                _stageShown = stageKey;
                _stage.text = string.IsNullOrEmpty(stageKey) ? "" : Loc.T(stageKey);
            }
            // 月球任務：抵達月球後高度改成離月面（縮時倍率和其他加速一樣顯示在時鐘旁邊）
            bool moon = _tl != null && _tl.MoonRelative(T);
            if (moon != _altMoon && _altKey != null)
            {
                _altMoon = moon;
                _altKey.text = Loc.T(moon ? "ui.launch.alt_moon" : "ui.launch.alt");
            }
            _next.text = NextText(T);
            UpdateSpeed();
            // 這一局已經結束（例如多人時房主斷線、正要回主選單）：結果卡片不用再畫
            if (!GameSession.HasMission) return;

            if (LaunchFeed.Finished && !_shownOutcome)
            {
                _shownOutcome = true;
                ShowOutcome(true);
            }
        }

        /// <summary>
        /// 倍率取兩位有效數字（×47、×120、×19,000），數字才不會每一幀都在跳；接近原速時淡出。
        /// 月球任務的地月轉移縮時（幾萬倍）也算在 LaunchFeed.Speed 裡。
        /// </summary>
        void UpdateSpeed()
        {
            float x = LaunchFeed.Finished ? 1f : LaunchFeed.Speed;
            bool on = x >= 1.5f;
            _speed.EnableInClassList("launch-speed--on", on);
            if (!on) return;
            double mag = Math.Pow(10, Math.Max(0, Math.Floor(Math.Log10(x)) - 1));
            double shown = Math.Round(x / mag) * mag;
            if (shown == _speedShown) return;
            _speedShown = shown;
            _speed.text = Loc.T("ui.launch.timelapse", ("x", shown.ToString("#,0")));
        }

        void ShowOutcome(bool sound)
        {
            var l = M.S.LastLaunch;
            _outcome.Clear();
            _outcome.style.display = DisplayStyle.Flex;
            // 背景慢慢壓暗再出卡片（結尾不要直接蓋上去）
            if (sound) Ui.AnimateIn(_outcome, "launch-outcome--dim");
            else _outcome.AddToClassList("launch-outcome--dim");
            var card = Ui.El("panel", "outcome-card", "fade-in").AddTo(_outcome);
            Ui.AnimateIn(card, "fade-in--on");
            bool ok = l != null && l.Reached;
            Ui.Text(Loc.T("ui.launch.result.eyebrow"), "eyebrow").AddTo(card);
            Ui.Text(Loc.T(FailureBoardView.OutcomeKey(l?.Outcome ?? LaunchOutcome.Nominal)), "h2", ok ? "go" : "over").AddTo(card);
            if (l != null) Ui.Text(Loc.T(l.ReasonCode), "body").AddTo(card);
            // 一般關卡失敗就整個任務重來；歷史關卡保留設計，回去重新設計
            bool restart = M.S.Scenario(D)?.Historical == null;
            if (!ok) Ui.Text(Loc.T(restart ? "ui.launch.restart_note" : "ui.launch.redesign_note"), "small", "dim").AddTo(card);
            var row = Ui.Row("outcome-actions").AddTo(card);
            Ui.Spacer().AddTo(row);
            if (GameSession.IsHost)
                Ui.Btn(Loc.T(ok ? "ui.launch.to_ops" : restart ? "ui.launch.to_restart" : "ui.launch.to_redesign"), Continue, ok ? "btn-go" : "btn-primary", "btn-big").AddTo(row);
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
