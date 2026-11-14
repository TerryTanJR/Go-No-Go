using System;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 新手訓練：右上角的提示框，一次一個步驟，做到了（收到 <see cref="Hud.Signal"/>）就打勾、換下一步。
    /// 步驟跨場景保留（發射畫面回來後接著做營運的步驟）。
    /// </summary>
    public sealed class TutorialCoach : IDisposable
    {
        sealed class Step
        {
            public string Key;
            public Func<string, bool> Done;
            public SessionPhase Phase;
        }

        // 順序照遊戲的核心：先問需求（電話、筆記板），再到組裝區採購、安裝、測試、看大螢幕，
        // 回中控室到後排查型錄（價格與規格），最後發射。玩家一開始就站在中控室。
        static readonly Step[] Steps =
        {
            new Step { Key = "move", Phase = SessionPhase.Design, Done = s => s == "move" },
            new Step { Key = "view", Phase = SessionPhase.Design, Done = s => s == "view" },
            new Step { Key = "call", Phase = SessionPhase.Design, Done = s => s.StartsWith("call:") },
            new Step { Key = "notes", Phase = SessionPhase.Design, Done = s => s == "open:notes" },
            new Step { Key = "procure", Phase = SessionPhase.Design, Done = s => s == "order" },
            new Step { Key = "pick", Phase = SessionPhase.Design, Done = s => s.StartsWith("pick:") },
            new Step { Key = "install", Phase = SessionPhase.Design, Done = s => s == "install" },
            new Step { Key = "test", Phase = SessionPhase.Design, Done = s => s == "test" },
            new Step { Key = "dashboard", Phase = SessionPhase.Design, Done = s => s == "open:dashboard" || s == "open:tablet" },
            // 後排北座的手冊 2・電力（組裝區的完整手冊也算，裡面一樣翻得到第 2 章）
            new Step { Key = "manual", Phase = SessionPhase.Design, Done = s => s == "open:manual:power" || s == "open:bay_manual" },
            new Step { Key = "launch", Phase = SessionPhase.Design, Done = s => s == "launch" },
            new Step { Key = "console", Phase = SessionPhase.Ops, Done = s => s.StartsWith("act:") },
            new Step { Key = "judge", Phase = SessionPhase.Ops, Done = s => s == "end_turn" },
        };

        static int _step;
        static object _mission;

        readonly VisualElement _box;
        readonly Label _count, _title, _body;
        readonly VisualElement _dots;
        float _advanceIn = -1;

        public TutorialCoach(VisualElement parent)
        {
            if (!ReferenceEquals(_mission, GameSession.Mission))
            {
                _mission = GameSession.Mission;
                _step = 0;
            }
            _box = Ui.El("coach").AddTo(parent);
            _box.pickingMode = PickingMode.Ignore;
            _count = Ui.Text("", "eyebrow").AddTo(_box);
            _title = Ui.Text("", "h3", "coach-title").AddTo(_box);
            _body = Ui.Text("", "body", "coach-body").AddTo(_box);
            _dots = Ui.Row("coach-dots").AddTo(_box);
            Hud.TutorialSignal += OnSignal;
            if (GameSession.HasMission) GameSession.Mission.PhaseChanged += OnPhase;
            SkipToPhase();
            Show();
        }

        public void Dispose()
        {
            Hud.TutorialSignal -= OnSignal;
            if (GameSession.HasMission) GameSession.Mission.PhaseChanged -= OnPhase;
            _box.RemoveFromHierarchy();
        }

        void OnPhase(SessionPhase before, SessionPhase now)
        {
            SkipToPhase();
            Show();
        }

        /// <summary>直接發射或營運開始了：跳到那個階段的第一步（還沒做完的設計步驟就略過）。</summary>
        void SkipToPhase()
        {
            if (!GameSession.HasMission) return;
            var phase = GameSession.Mission.S.Phase;
            if (phase == SessionPhase.Ops && _step < Steps.Length && Steps[_step].Phase != SessionPhase.Ops)
                _step = Array.FindIndex(Steps, s => s.Phase == SessionPhase.Ops);
        }

        void OnSignal(string what)
        {
            if (_step >= Steps.Length || _advanceIn >= 0) return;
            if (!Steps[_step].Done(what)) return;
            Sfx.Confirm();
            _box.AddToClassList("coach--done");
            _title.text = "✔ " + _title.text;
            _advanceIn = 0.9f;
        }

        public void Tick(float dt)
        {
            if (_advanceIn < 0) return;
            _advanceIn -= dt;
            if (_advanceIn > 0) return;
            _advanceIn = -1;
            _step++;
            _box.RemoveFromClassList("coach--done");
            Show();
        }

        void Show()
        {
            _dots.Clear();
            for (int i = 0; i < Steps.Length; i++)
                Ui.El("coach-dot").Cls("coach-dot--done", i < _step).Cls("coach-dot--now", i == _step).AddTo(_dots);
            if (_step >= Steps.Length)
            {
                _count.text = Loc.T("ui.tut.eyebrow");
                _title.text = Loc.T("ui.tut.done.t");
                _body.text = Loc.T("ui.tut.done");
                return;
            }
            var s = Steps[_step];
            _count.text = Loc.T("ui.tut.eyebrow") + "  " + (_step + 1) + " / " + Steps.Length;
            _title.text = Loc.T("ui.tut." + s.Key + ".t");
            _body.text = Loc.T("ui.tut." + s.Key);
        }
    }
}
