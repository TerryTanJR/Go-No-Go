using GoNoGo.Game;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>標題：只有遊戲名稱、一句簡介、「點一下開始」、語言切換和聲明。背景是 ISS 與太空梭繞地球。</summary>
    public sealed class TitleScreen : GameScreen
    {
        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            Sfx.SetMusic(Sfx.Music.Menu);
            var top = Ui.El("topbar", "topbar--clear").AddTo(Root);
            Ui.Spacer().AddTo(top);
            Ui.Btn(Loc.IsZh ? "EN" : "中文", Loc.Toggle, "btn-small", "btn-ghost").AddTo(top);

            var col = Ui.El("title-col", "fade-in").AddTo(Root);
            Ui.AnimateIn(col, "fade-in--on", 80);
            Ui.Text(Loc.T("ui.title.eyebrow"), "eyebrow").AddTo(col);
            Ui.Text("GO / NO-GO", "h1", "title-logo").AddTo(col);
            Ui.Text(Loc.T("ui.title.tagline"), "body", "title-tagline").AddTo(col);
            var start = Ui.Text(Loc.T("ui.title.click"), "title-click").AddTo(col);
            start.schedule.Execute(() => start.ToggleInClassList("title-click--dim")).Every(800);

            var foot = Ui.El("title-foot").AddTo(Root);
            Ui.Text(Loc.T("ui.title.credits"), "small", "faint").AddTo(foot);

            // 點畫面任何地方（按鈕以外）進主選單。畫面層是所有畫面共用的：同一個回呼只掛一次（換語言重建不會重複），離開時拿掉
            Root.pickingMode = PickingMode.Position;
            _onClick ??= e =>
            {
                if (e.target is Button) return;
                Go();
            };
            Root.RegisterCallback<ClickEvent>(_onClick);
        }

        EventCallback<ClickEvent> _onClick;
        bool _going;

        void Go()
        {
            if (_going) return;
            _going = true;
            Sfx.Confirm();
            SceneFlow.Go(SceneFlow.MainMenu);
        }

        public override void Tick(float dt)
        {
            var kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) Go();
        }

        protected override void OnClose()
        {
            Root.pickingMode = PickingMode.Ignore;
            if (_onClick != null) Root.UnregisterCallback<ClickEvent>(_onClick);
        }
    }
}
