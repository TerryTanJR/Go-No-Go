using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 發射 HUD：倒數計時、遙測、播報紀錄、跳過鈕（第二次以後才有），播完顯示結果。
    /// 畫面本身不決定結果——結果在按下發射時就由核心算好了（GameState.Result），3D 只是演出來。
    /// </summary>
    public sealed class LaunchScreen : GameScreen
    {
        Label _clock, _alt, _vel, _thr, _stage;
        VisualElement _log, _outcome;
        readonly List<string> _lines = new List<string>();
        bool _shownOutcome;
        float _selfTimer;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.None);
            if (!LaunchFeed.DirectorActive && _selfTimer == 0) LaunchFeed.Reset();

            var head = Ui.El("launch-head").AddTo(Root);
            Ui.Text(Loc.T("ui.launch.eyebrow") + " · " + Loc.Launcher(S.Design.LauncherId), "eyebrow").AddTo(head);
            _clock = Ui.Text("T-00:00:10", "launch-clock", "mono").AddTo(head);
            _stage = Ui.Text("", "launch-stage").AddTo(head);

            var tele = Ui.El("panel", "launch-tele").AddTo(Root);
            _alt = Row(tele, "ui.launch.alt", "km");
            _vel = Row(tele, "ui.launch.vel", "km/s");
            _thr = Row(tele, "ui.launch.thr", "%");

            _log = Ui.El("launch-log").AddTo(Root);
            foreach (var l in _lines) AddLine(l, false);

            var actions = Ui.Row("launch-actions").AddTo(Root);
            Ui.Spacer().AddTo(actions);
            if (S.SkipLaunchAllowed)
                Ui.Btn(Loc.T("ui.launch.skip"), LaunchFeed.RequestSkip, "btn-ghost").AddTo(actions);

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

        void OnCallout(string key)
        {
            _lines.Add(key);
            AddLine(key, true);
        }

        void AddLine(string key, bool animate)
        {
            var l = Ui.Text(Loc.T(key), "launch-line", "fade-in").AddTo(_log);
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

            if (LaunchFeed.Finished && !_shownOutcome)
            {
                _shownOutcome = true;
                ShowOutcome(true);
            }
        }

        void ShowOutcome(bool sound)
        {
            var r = S.Result;
            _outcome.Clear();
            _outcome.style.display = DisplayStyle.Flex;
            var card = Ui.El("panel", "outcome-card", "fade-in").AddTo(_outcome);
            Ui.AnimateIn(card, "fade-in--on");
            bool ok = r != null && r.ReachedOrbit;
            string titleKey = r == null ? "ui.launch.result.nominal" : r.Launch.Outcome switch
            {
                LaunchOutcome.Nominal => "ui.launch.result.nominal",
                LaunchOutcome.LowerOrbit => "ui.launch.result.lower",
                LaunchOutcome.FairingFailure => "ui.launch.result.fairing",
                _ => "ui.launch.result.failure",
            };
            Ui.Text(Loc.T("ui.launch.result.eyebrow"), "eyebrow").AddTo(card);
            Ui.Text(Loc.T(titleKey), "h2", ok ? "go" : "over").AddTo(card);
            if (r != null) Ui.Text(Loc.T(r.Launch.ReasonCode), "body").AddTo(card);
            var row = Ui.Row("outcome-actions").AddTo(card);
            Ui.Spacer().AddTo(row);
            Ui.Btn(Loc.T(ok ? "ui.launch.to_ops" : "ui.launch.to_report"), Continue, ok ? "btn-go" : "btn-primary", "btn-big").AddTo(row);
            if (sound) { if (ok) Sfx.Go(); else Sfx.NoGo(); }
        }

        static void Continue()
        {
            Sfx.Confirm();
            if (S.Result != null && S.Result.ReachedOrbit)
            {
                S.SetPhase(Phase.Operations);
                SceneFlow.Go(SceneFlow.Operations, 0.8f);
            }
            else
            {
                S.SetPhase(Phase.Report);
                SceneFlow.Go(SceneFlow.Report, 0.8f);
            }
        }
    }
}
