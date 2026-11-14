using System;
using System.Collections.Generic;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 唯一的 UI 根物件（跨場景存在）：一個 UIDocument，分三層——畫面、浮動層（提示、通知）、黑幕。
    /// 場景載入時依場景名稱換上對應畫面；黑幕跨場景保留，所以轉場不會閃白。
    /// </summary>
    public sealed class UIRoot : MonoBehaviour
    {
        static UIRoot _i;
        public static UIRoot Instance => _i;

        UIDocument _doc;
        VisualElement _screenLayer, _overlay, _fader, _toast, _tip;
        Label _toastText, _tipText;
        GameScreen _current;
        IVisualElementScheduledItem _toastHide;

        static readonly Dictionary<string, Func<GameScreen>> Screens = new Dictionary<string, Func<GameScreen>>
        {
            { SceneFlow.Boot, () => new TitleScreen() },
            { SceneFlow.MainMenu, () => new MainMenuScreen() },
            { SceneFlow.Facility, () => new FacilityScreen() },
            { SceneFlow.Launch, () => new LaunchScreen() },
            { SceneFlow.Report, () => new ReportScreen() },
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _i = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (_i != null || !Screens.ContainsKey(SceneManager.GetActiveScene().name)) return;
            var go = new GameObject("[UIRoot]");
            go.SetActive(false);
            DontDestroyOnLoad(go);
            _i = go.AddComponent<UIRoot>();
            _i._doc = go.AddComponent<UIDocument>();
            _i._doc.panelSettings = Resources.Load<PanelSettings>("UI/PanelSettings");
            if (_i._doc.panelSettings == null) Debug.LogError("[GoNoGo] 找不到 Resources/UI/PanelSettings（請執行 GoNoGo/Build/UI Assets）");
            go.SetActive(true);
            _i.Init();
            _i.ShowFor(SceneManager.GetActiveScene().name);
        }

        void Init()
        {
            var root = _doc.rootVisualElement;
            root.AddToClassList("app");
            _screenLayer = Ui.El("screen").AddTo(root).Named("screenLayer");
            _overlay = Ui.El("screen").AddTo(root).Named("overlay");
            _overlay.pickingMode = PickingMode.Ignore;

            _toast = Ui.El("toast").AddTo(_overlay);
            _toast.pickingMode = PickingMode.Ignore;
            _toastText = Ui.Text("", "small").AddTo(_toast);

            _tip = Ui.El("tooltip").AddTo(_overlay);
            _tip.pickingMode = PickingMode.Ignore;
            _tipText = Ui.Text("", "body").AddTo(_tip);
            _tip.style.display = DisplayStyle.None;

            _fader = Ui.El("fader").AddTo(root).Named("fader");
            _fader.pickingMode = PickingMode.Ignore;

            // 只有真正的 UI 元件會被點到：根與畫面層本身不接收指標，3D 鏡頭才能在空白處拖曳
            root.pickingMode = PickingMode.Ignore;
            _screenLayer.pickingMode = PickingMode.Ignore;
            PointerHooks.OverUI = IsPointerOverUI;

            SceneFlow.FadeOut = FadeOut;
            SceneFlow.FadeIn = FadeIn;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Loc.Changed += OnLangChanged;
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Loc.Changed -= OnLangChanged;
            if (_i == this)
            {
                _i = null;
                PointerHooks.OverUI = null;
            }
        }

        bool IsPointerOverUI(Vector2 screenPos)
        {
            var panel = _doc != null ? _doc.rootVisualElement?.panel : null;
            if (panel == null) return false;
            // Input System 的座標以左下為原點，UI Toolkit 以左上為原點
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            return panel.Pick(p) != null;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Single) ShowFor(s.name);
        }

        void OnLangChanged() => _current?.Rebuild();

        void ShowFor(string sceneName)
        {
            HideTip();
            _current?.Close();
            _current = null;
            _screenLayer.Clear();
            if (!Screens.TryGetValue(sceneName, out var make)) return;
            if (sceneName != SceneFlow.Boot && sceneName != SceneFlow.MainMenu) GameSession.EnsureMission();
            _current = make();
            _current.Attach(this, _screenLayer);
        }

        void Update() => _current?.Tick(Time.unscaledDeltaTime);

        // ---------------- 黑幕 ----------------

        void FadeOut(float seconds, Action done)
        {
            _fader.pickingMode = PickingMode.Position; // 轉場中不能再點
            _fader.AddToClassList("fader--on");
            _fader.schedule.Execute(() => done?.Invoke()).StartingIn((long)(seconds * 1000));
        }

        void FadeIn(float seconds)
        {
            _fader.schedule.Execute(() =>
            {
                _fader.RemoveFromClassList("fader--on");
                _fader.pickingMode = PickingMode.Ignore;
            }).StartingIn(60);
        }

        // ---------------- 通知與提示 ----------------

        public static void Toast(string text, float seconds = 2.6f)
        {
            if (_i == null) return;
            _i._toastText.text = text;
            _i._toast.AddToClassList("toast--on");
            _i._toastHide?.Pause();
            _i._toastHide = _i._toast.schedule.Execute(() => _i._toast.RemoveFromClassList("toast--on")).StartingIn((long)(seconds * 1000));
        }

        /// <summary>在元素旁邊顯示說明（執行時的 UI Toolkit 不會顯示 tooltip 屬性，所以自己畫）。</summary>
        public static void ShowTip(VisualElement anchor, string text)
        {
            if (_i == null || anchor?.panel == null) return;
            _i._tipText.text = text;
            _i._tip.style.display = DisplayStyle.Flex;
            var b = anchor.worldBound;
            var root = _i._overlay.worldBound;
            float x = b.xMax + 12, y = b.yMin;
            if (x + 360 > root.width) x = Mathf.Max(8, b.xMin - 372);
            if (y + 160 > root.height) y = Mathf.Max(64, root.height - 170);
            _i._tip.style.left = x;
            _i._tip.style.top = y;
        }

        public static void HideTip()
        {
            if (_i == null) return;
            _i._tip.style.display = DisplayStyle.None;
        }

        /// <summary>讓元素在滑鼠移上去時顯示說明。</summary>
        public static void AttachTip(VisualElement e, Func<string> text)
        {
            e.RegisterCallback<PointerEnterEvent>(_ => ShowTip(e, text()));
            e.RegisterCallback<PointerLeaveEvent>(_ => HideTip());
            e.RegisterCallback<DetachFromPanelEvent>(_ => HideTip());
        }
    }
}
