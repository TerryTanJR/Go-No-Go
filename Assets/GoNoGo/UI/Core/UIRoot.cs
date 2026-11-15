using System;
using System.Collections.Generic;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 唯一的 UI 根物件（跨場景存在）：一個 UIDocument，分五層——畫面、遊戲選單、浮動層（通知）、黑幕、進場過場（<see cref="LoadingScreen"/>）。
    /// 場景載入時依場景名稱換上對應畫面；黑幕跨場景保留，所以轉場不會閃白。
    /// 遊戲中（設施、發射、報告）按 Tab 或 Esc 打開 <see cref="GameMenu"/>。
    /// </summary>
    public sealed class UIRoot : MonoBehaviour
    {
        static UIRoot _i;

        UIDocument _doc;
        VisualElement _screenLayer, _menuLayer, _overlay, _fader, _toast;
        Label _toastText;
        GameScreen _current;
        string _scene;
        // 現在的場景可不可以開遊戲選單（換場景時查一次，不用每一幀查表）
        bool _menuScene;
        GameMenu _menu;
        LoadingScreen _loading;
        bool _wasLocked;
        int _seenClosedFrame = -1;
        float _lockGrace, _autoOpenedAt = -10f;

        /// <summary>可以打開遊戲選單的場景（主選單、標題本身就是選單）。</summary>
        static readonly HashSet<string> MenuScenes = new HashSet<string> { SceneFlow.Facility, SceneFlow.Launch, SceneFlow.Report };
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
            _menuLayer = Ui.El("screen").AddTo(root).Named("menuLayer");
            _menuLayer.pickingMode = PickingMode.Ignore;
            // 關選單時只有在設施裡、沒開面板才把游標鎖回去（發射、報告畫面要用滑鼠點）
            _menu = new GameMenu(_menuLayer, () => _scene == SceneFlow.Facility && !Hud.PanelOpen);
            _overlay = Ui.El("screen").AddTo(root).Named("overlay");
            _overlay.pickingMode = PickingMode.Ignore;

            _toast = Ui.El("toast").AddTo(_overlay);
            _toast.pickingMode = PickingMode.Ignore;
            _toastText = Ui.Text("", "small").AddTo(_toast);

            _fader = Ui.El("fader").AddTo(root).Named("fader");
            _fader.pickingMode = PickingMode.Ignore;
            // 過場在黑幕之上：換場景淡出成黑的時候進度條還看得到
            _loading = new LoadingScreen(root);

            // 只有真正的 UI 元件會被點到：根與畫面層本身不接收指標，3D 鏡頭才能在空白處拖曳
            root.pickingMode = PickingMode.Ignore;
            _screenLayer.pickingMode = PickingMode.Ignore;
            PointerHooks.OverUI = IsPointerOverUI;

            SceneFlow.FadeOut = FadeOut;
            SceneFlow.FadeIn = FadeIn;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Loc.Changed += OnLangChanged;
            GameSettings.TextScaleChanged += ApplyTextScale;
            ApplyTextScale();
        }

        /// <summary>
        /// 字體大小：根元素掛 fs-{百分比}，FontScale.uss 依此換掉所有 --fs-N 變數，整個 UI（含選單、通知、場景標籤）一起變。
        /// 不必重建畫面：樣式重算後版面自動重排。
        /// </summary>
        void ApplyTextScale()
        {
            var root = _doc.rootVisualElement;
            foreach (int pct in GameSettings.TextScales)
                root.EnableInClassList(GameSettings.TextScaleClass(pct), pct == GameSettings.TextScale);
        }

        void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Loc.Changed -= OnLangChanged;
            GameSettings.TextScaleChanged -= ApplyTextScale;
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

        void OnLangChanged()
        {
            _current?.Rebuild();
            _menu?.Rebuild();
        }

        void ShowFor(string sceneName)
        {
            _menu.Close(relock: false);
            _current?.Close();
            _current = null;
            _scene = sceneName;
            _menuScene = MenuScenes.Contains(sceneName);
            _wasLocked = false;
            _screenLayer.Clear();
            // 只有設施裡會鎖游標；其他畫面都要用滑鼠點（例如多人時別人按發射，自己還在走路）
            if (sceneName != SceneFlow.Facility)
            {
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
            if (!Screens.TryGetValue(sceneName, out var make)) return;
            if (sceneName != SceneFlow.Boot && sceneName != SceneFlow.MainMenu) GameSession.EnsureMission();
            _current = make();
            _current.Attach(_screenLayer);
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            UpdateMenu(dt);
            _current?.Tick(dt);
            _loading.Tick(dt);
        }

        /// <summary>開始進場過場（開房、加入房間、開始任務）。場景載入完會自己收起來。</summary>
        public static void BeginLoading(bool network) => _i?._loading.Begin(network);

        /// <summary>連線失敗：收起過場，回到原本的畫面。</summary>
        public static void EndLoading() => _i?._loading.End();

        // ---------------- 遊戲選單（Tab / Esc） ----------------

        bool MenuAllowed => _current != null && _menuScene && GameSession.HasMission && !SceneFlow.Loading;

        void UpdateMenu(float dt)
        {
            if (!MenuAllowed)
            {
                if (_menu.IsOpen) _menu.Close(relock: false);
                _wasLocked = false;
                return;
            }
            var kb = Keyboard.current;
            if (kb != null)
            {
                // Tab：開／關（在輸入框打字時不算）
                if (kb.tabKey.wasPressedThisFrame && !IsTyping) _menu.Toggle();
                // Esc：選單開著 → 回上一頁／關掉；畫面先處理（例如關面板）；都沒有 → 打開選單
                else if (kb.escapeKey.wasPressedThisFrame)
                {
                    // 瀏覽器：同一次 Esc 可能先放開游標（下面自動打開選單）、下一幀才送來按鍵，不要又把選單關掉
                    if (_menu.IsOpen) { if (Time.unscaledTime - _autoOpenedAt > 0.3f) _menu.Back(); }
                    else if (!_current.HandleEscape()) _menu.Open();
                }
            }

            // 瀏覽器按 Esc 會直接放開游標（遊戲不一定收得到 Esc）：
            // 游標從鎖住變成放開、又沒開面板 → 打開選單。剛關面板或選單時瀏覽器重新鎖定要一點時間，先等 0.6 秒。
            if (Hud.ClosedFrame != _seenClosedFrame)
            {
                _seenClosedFrame = Hud.ClosedFrame;
                _lockGrace = 0.6f;
            }
            _lockGrace -= dt;
            bool locked = UnityEngine.Cursor.lockState == CursorLockMode.Locked;
            if (_wasLocked && !locked && !_menu.IsOpen && !Hud.PanelOpen && _lockGrace <= 0 && Application.isFocused)
            {
                _menu.Open();
                _autoOpenedAt = Time.unscaledTime;
            }
            _wasLocked = locked;
        }

        /// <summary>正在輸入框裡打字（這時 Tab、E、T 等快捷鍵不作用）。</summary>
        public static bool IsTyping
        {
            get
            {
                var f = _i != null && _i._doc != null ? _i._doc.rootVisualElement?.panel?.focusController?.focusedElement as VisualElement : null;
                for (; f != null; f = f.hierarchy.parent)
                    if (f is TextField) return true;
                return false;
            }
        }

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

        // ---------------- 通知 ----------------

        public static void Toast(string text, float seconds = 2.6f)
        {
            if (_i == null) return;
            _i._toastText.text = text;
            _i._toast.AddToClassList("toast--on");
            _i._toastHide?.Pause();
            _i._toastHide = _i._toast.schedule.Execute(() => _i._toast.RemoveFromClassList("toast--on")).StartingIn((long)(seconds * 1000));
        }
    }
}
