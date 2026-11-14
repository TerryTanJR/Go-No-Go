using System;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 遊戲中的選單（設施、發射、報告場景按 Tab 或 Esc 打開）：繼續遊戲、聲音、基本設定、按鍵說明、返回主選單。
    /// 由 <see cref="UIRoot"/> 擁有，蓋在所有畫面之上，切換場景時自動關閉。
    /// 開著時 <see cref="Hud.Paused"/> = true：角色不能動、營運回合計時（個人模式）暫停；多人時其他人的遊戲照常進行。
    /// </summary>
    public sealed class GameMenu
    {
        enum Page { Main, Sound, Settings, Controls, Quit }

        readonly VisualElement _layer;
        readonly Func<bool> _canRelockCursor;
        Page _page;

        public bool IsOpen { get; private set; }

        /// <param name="parent">選單層（在畫面層之上、通知層之下）。</param>
        /// <param name="canRelockCursor">關選單時要不要把游標鎖回去（設施場景、沒開面板時）。</param>
        public GameMenu(VisualElement parent, Func<bool> canRelockCursor)
        {
            _canRelockCursor = canRelockCursor;
            _layer = Ui.El("modal-layer", "game-menu-layer").AddTo(parent);
            _layer.style.display = DisplayStyle.None;
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _page = Page.Main;
            Hud.Paused = true;
            UIRoot.HideTip();
            SetCursor(false);
            Render();
            _layer.style.display = DisplayStyle.Flex;
            Sfx.Whoosh();
        }

        /// <summary>關掉選單回到遊戲。<paramref name="relock"/> = false：不要鎖游標（換場景、回主選單）。</summary>
        public void Close(bool relock = true)
        {
            if (!IsOpen) return;
            IsOpen = false;
            _layer.style.display = DisplayStyle.None;
            _layer.Clear();
            Hud.Paused = false;
            Hud.ClosedFrame = Time.frameCount; // 同一幀的 E / 點擊不要又觸發別的東西
            GameSettings.Save();
            if (relock && _canRelockCursor()) SetCursor(true);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Esc：子頁面回主頁，主頁就關掉。</summary>
        public void Back()
        {
            if (!IsOpen) return;
            if (_page == Page.Main) Close();
            else Show(Page.Main);
        }

        /// <summary>切換語言後重畫（停在同一頁）。</summary>
        public void Rebuild()
        {
            if (IsOpen) Render();
        }

        void Show(Page p)
        {
            _page = p;
            Render();
        }

        static void SetCursor(bool locked)
        {
            UnityEngine.Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !locked;
        }

        // ---------------- 畫面 ----------------

        void Render()
        {
            _layer.Clear();
            var card = Ui.El("modal", "modal--narrow", "game-menu").AddTo(_layer);
            var head = Ui.Row("modal-head").AddTo(card);
            Ui.Text(Title(), "eyebrow").AddTo(head);
            Ui.Spacer().AddTo(head);
            Ui.Text(Loc.T("ui.gmenu.keys"), "small", "dim").AddTo(head);
            var body = Ui.El("gm-col").AddTo(card);
            switch (_page)
            {
                case Page.Main: BuildMain(body); break;
                case Page.Sound: BuildSound(body); break;
                case Page.Settings: BuildSettings(body); break;
                case Page.Controls: BuildControls(body); break;
                case Page.Quit: BuildQuit(body); break;
            }
        }

        string Title() => _page switch
        {
            Page.Sound => Loc.T("ui.gmenu.sound"),
            Page.Settings => Loc.T("ui.gmenu.settings"),
            Page.Controls => Loc.T("ui.gmenu.controls"),
            Page.Quit => Loc.T("ui.gmenu.quit"),
            _ => Loc.T("ui.gmenu.title"),
        };

        static bool Multiplayer => GameSession.HasMission && GameSession.Mission.S.Multiplayer;

        void BuildMain(VisualElement body)
        {
            // 這一局的任務和難度（HUD 上不再常駐顯示）
            if (GameSession.HasMission)
            {
                var m = GameSession.Mission;
                Ui.Text(Loc.T(m.Scenario.Name) + (m.IsHistorical ? "" : " · " + Loc.T(m.Difficulty.Name)), "h3", "gm-mission").AddTo(body);
            }
            if (Multiplayer) Ui.Text(Loc.T("ui.gmenu.hint_mp"), "small", "dim").AddTo(body);
            Ui.Btn(Loc.T("ui.gmenu.resume"), () => Close(), "btn-primary", "gm-btn").AddTo(body);
            Ui.Btn(Loc.T("ui.gmenu.sound"), () => Show(Page.Sound), "gm-btn").AddTo(body);
            Ui.Btn(Loc.T("ui.gmenu.settings"), () => Show(Page.Settings), "gm-btn").AddTo(body);
            if (GameSession.HasMission) Ui.Btn(Loc.T("ui.gmenu.controls"), () => Show(Page.Controls), "gm-btn").AddTo(body);
            Ui.Btn(Loc.T("ui.gmenu.quit"), () => Show(Page.Quit), "btn-danger", "gm-btn").AddTo(body);
        }

        void BuildSound(VisualElement body)
        {
            SoundControls(body);
            BackButton(body);
        }

        /// <summary>三條音量拉桿（主選單的設定頁也用）。</summary>
        public static void SoundControls(VisualElement body)
        {
            SliderRow(body, Loc.T("ui.gmenu.vol.master"), 0f, 1f, Sfx.MasterVolume, v => Sfx.MasterVolume = v, Percent);
            SliderRow(body, Loc.T("ui.gmenu.vol.music"), 0f, 1f, Sfx.MusicVolume, v => Sfx.MusicVolume = v, Percent);
            SliderRow(body, Loc.T("ui.gmenu.vol.ambience"), 0f, 1f, Sfx.AmbienceVolume, v => Sfx.AmbienceVolume = v, Percent);
            SliderRow(body, Loc.T("ui.gmenu.vol.effects"), 0f, 1f, Sfx.EffectsVolume, v => Sfx.EffectsVolume = v, Percent, Sfx.Click);
            Ui.Text(Loc.T("ui.gmenu.vol.note"), "small", "dim").AddTo(body);
        }

        /// <summary>滑鼠靈敏度拉桿（主選單的設定頁也用）。</summary>
        public static void MouseControl(VisualElement body) =>
            SliderRow(body, Loc.T("ui.gmenu.mouse"), GameSettings.MinMouse, GameSettings.MaxMouse, GameSettings.MouseSensitivity,
                v => GameSettings.MouseSensitivity = v, v => v.ToString("0.0") + "×");

        /// <summary>字體大小（主選單的設定頁也用）。點下去立刻套用，選單本身就是預覽。</summary>
        public static void TextSizeControl(VisualElement body)
        {
            var row = ChoiceRow(body, Loc.T("ui.gmenu.textsize"));
            var chips = new System.Collections.Generic.List<(Button b, int pct)>();
            foreach (int pct in GameSettings.TextScales)
            {
                int p = pct;
                var b = Ui.Btn(p + "%", null, "chip").AddTo(row);
                b.clicked += () =>
                {
                    GameSettings.TextScale = p;
                    foreach (var c in chips) c.b.EnableInClassList("chip--on", c.pct == GameSettings.TextScale);
                };
                b.EnableInClassList("chip--on", p == GameSettings.TextScale);
                chips.Add((b, p));
            }
            Ui.Text(Loc.T("ui.gmenu.textsize.note"), "small", "dim").AddTo(body);
        }

        /// <summary>語言（主選單的設定頁也用）。</summary>
        public static void LanguageControl(VisualElement body)
        {
            var lang = ChoiceRow(body, Loc.T("ui.lang"));
            Chip(lang, "中文", Loc.IsZh, () => { if (!Loc.IsZh) Loc.Toggle(); });
            Chip(lang, "English", !Loc.IsZh, () => { if (Loc.IsZh) Loc.Toggle(); });
        }

        void BuildSettings(VisualElement body)
        {
            TextSizeControl(body);

            LanguageControl(body);

            // 視角
            var view = ChoiceRow(body, Loc.T("ui.gmenu.view"));
            Chip(view, Loc.T("ui.gmenu.view.third"), !Hud.FirstPerson, () => SetView(false));
            Chip(view, Loc.T("ui.gmenu.view.first"), Hud.FirstPerson, () => SetView(true));
            Ui.Text(Loc.T("ui.gmenu.view.note"), "small", "dim").AddTo(body);

            MouseControl(body);
            BackButton(body);
        }

        /// <summary>按鍵說明（原本常駐在畫面左下角，改放這裡）。只列這一局用得到的鍵。</summary>
        void BuildControls(VisualElement body)
        {
            var s = GameSession.HasMission ? GameSession.Mission.S : null;
            bool mp = s != null && s.Multiplayer;
            KeyRow(body, "W A S D", "ui.keys.move");
            KeyRow(body, "Shift", "ui.keys.run");
            KeyRow(body, Loc.T("ui.keys.mouse"), "ui.keys.look");
            KeyRow(body, "Q", "ui.keys.view");
            KeyRow(body, "E", "ui.keys.use");
            KeyRow(body, "C", "ui.keys.pick");
            KeyRow(body, "V", "ui.keys.install");
            if (!mp) KeyRow(body, "T", "ui.keys.tablet");
            if (mp) KeyRow(body, "M", "ui.keys.mic");
            if (mp) KeyRow(body, "G / N", "ui.keys.vote");
            if (s != null && s.Tutorial) KeyRow(body, "H", "ui.keys.tut");
            KeyRow(body, "Tab", "ui.keys.menu");
            BackButton(body);
        }

        static void KeyRow(VisualElement body, string key, string label)
        {
            var row = Ui.Row("gm-key-row").AddTo(body);
            Ui.Text(key, "keycap", "gm-keycap").AddTo(row);
            Ui.Text(Loc.T(label), "gm-key-label").AddTo(row);
        }

        void SetView(bool firstPerson)
        {
            if (Hud.FirstPerson == firstPerson) return;
            Hud.FirstPerson = firstPerson;
            Hud.Signal("view");
            Render();
        }

        void BuildQuit(VisualElement body)
        {
            Ui.Text(Loc.T("ui.gmenu.quit.ask"), "h3").AddTo(body);
            Ui.Text(Loc.T(QuitWarningKey()), "body", "dim").AddTo(body);
            var row = Ui.Row("gm-actions").AddTo(body);
            Ui.Btn(Loc.T("ui.gmenu.cancel"), () => Show(Page.Main), "btn-ghost").AddTo(row);
            Ui.Spacer().AddTo(row);
            Ui.Btn(Loc.T("ui.gmenu.quit.confirm"), QuitToMenu, "btn-danger").AddTo(row);
        }

        static string QuitWarningKey()
        {
            if (!GameSession.HasMission) return "ui.gmenu.quit.solo";
            var s = GameSession.Mission.S;
            if (s.Multiplayer) return GameSession.IsHost ? "ui.gmenu.quit.host" : "ui.gmenu.quit.client";
            return s.Tutorial ? "ui.gmenu.quit.tutorial" : "ui.gmenu.quit.solo";
        }

        void QuitToMenu()
        {
            if (SceneFlow.Loading) return;
            Close(relock: false);
            SetCursor(false);
            if (GameSession.HasMission) GameSession.EndMission();
            else SceneFlow.Go(SceneFlow.MainMenu);
        }

        // ---------------- 小元件 ----------------

        void BackButton(VisualElement body)
        {
            var row = Ui.Row("gm-actions").AddTo(body);
            Ui.Btn("‹  " + Loc.T("ui.gmenu.back"), () => Show(Page.Main), "btn-ghost").AddTo(row);
        }

        static VisualElement ChoiceRow(VisualElement body, string label)
        {
            var row = Ui.Row("gm-row").AddTo(body);
            Ui.Text(label, "gm-label").AddTo(row);
            return Ui.Row("gm-choices").AddTo(row);
        }

        static void Chip(VisualElement parent, string text, bool on, Action pick)
        {
            var b = Ui.Btn(text, pick, "chip").AddTo(parent);
            b.EnableInClassList("chip--on", on);
        }

        static string Percent(float v) => Mathf.RoundToInt(v * 100) + "%";

        /// <summary>一列：名稱、拉桿、目前數值。<paramref name="onRelease"/>：放開拉桿時（例如播一聲試聽）。</summary>
        static void SliderRow(VisualElement body, string label, float min, float max, float value, Action<float> set,
            Func<float, string> fmt, Action onRelease = null)
        {
            var row = Ui.Row("gm-row").AddTo(body);
            Ui.Text(label, "gm-label").AddTo(row);
            var slider = new Slider(min, max) { value = Mathf.Clamp(value, min, max) };
            slider.AddToClassList("gm-slider");
            slider.AddTo(row);
            var val = Ui.Text(fmt(slider.value), "gm-value", "mono").AddTo(row);
            slider.RegisterValueChangedCallback(e =>
            {
                set(e.newValue);
                val.text = fmt(e.newValue);
            });
            if (onRelease != null) slider.RegisterCallback<PointerUpEvent>(_ => onRelease(), TrickleDown.TrickleDown);
        }
    }
}
