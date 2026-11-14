using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 主選單：左邊選單（新手訓練、個人任務、建立多人房間、加入房間、設定），右邊是自己的 avatar（3D，在 MainMenu 場景裡）。
    /// 第一次進遊戲先捏 avatar。個人任務和建房：選任務（一般任務／歷史關卡）→ 選難度 → 開始。
    /// </summary>
    public sealed class MainMenuScreen : GameScreen
    {
        enum Page { Root, Missions, Difficulty, Join, Settings, Avatar }

        static Page _page = Page.Root;
        static bool _multi;
        static int _missionTab;
        static string _scenario;
        static string _code = "";
        static string _ip = "127.0.0.1";
        static AvatarLook _edit;
        static bool _busy;
        static string _error = "";

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            PlayerControllerUnlock();
            if (!Profile.HasAvatar && _page != Page.Avatar) OpenAvatar();

            var top = Ui.El("topbar", "topbar--clear").AddTo(Root);
            Ui.Text("GO / NO-GO", "brand").AddTo(top);
            Ui.Spacer().AddTo(top);
            Ui.Btn(Loc.IsZh ? "EN" : "中文", Loc.Toggle, "btn-small", "btn-ghost").AddTo(top);

            var left = Ui.El("menu-col", "fade-in").AddTo(Root);
            Ui.AnimateIn(left, "fade-in--on", 40);
            switch (_page)
            {
                case Page.Root: RootMenu(left); break;
                case Page.Missions: Missions(left); break;
                case Page.Difficulty: Difficulty(left); break;
                case Page.Join: Join(left); break;
                case Page.Settings: Settings(left); break;
                case Page.Avatar: AvatarEditor(left); break;
            }

            // 右下：avatar 名字與修改鈕（avatar 本身是 3D）
            if (_page != Page.Avatar)
            {
                var me = Ui.El("menu-me").AddTo(Root);
                Ui.Text(Profile.Look.Name, "h2").AddTo(me);
                Ui.Btn(Loc.T("ui.menu.edit_avatar"), OpenAvatar, "btn-ghost").AddTo(me);
            }
            if (!string.IsNullOrEmpty(_error)) Ui.Text(Loc.T(_error), "small", "over", "menu-error").AddTo(left);
        }

        static void PlayerControllerUnlock()
        {
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }

        void Go(Page p)
        {
            _page = p;
            _error = "";
            Rebuild();
        }

        // ---------------- 根選單 ----------------

        void RootMenu(VisualElement col)
        {
            Ui.Text(Loc.T("ui.menu.hello", ("name", Profile.Look.Name)), "eyebrow").AddTo(col);
            Item(col, "ui.menu.tutorial", "ui.menu.tutorial_desc", () => { Sfx.Confirm(); GameSession.StartSolo("earth_observation", "tutorial", true); });
            Item(col, "ui.menu.solo", "ui.menu.solo_desc", () => { _multi = false; Go(Page.Missions); });
            Item(col, "ui.menu.host", "ui.menu.host_desc", () => { _multi = true; Go(Page.Missions); });
            Item(col, "ui.menu.join", "ui.menu.join_desc", () => Go(Page.Join));
            Item(col, "ui.menu.settings", "", () => Go(Page.Settings));
            Ui.Btn(Loc.T("ui.menu.title"), () => SceneFlow.Go(SceneFlow.Boot), "btn-ghost", "menu-back").AddTo(col);
        }

        static void Item(VisualElement col, string key, string desc, System.Action click)
        {
            var b = Ui.Btn("", click, "menu-item").AddTo(col);
            b.Clear();
            Ui.Text(Loc.T(key), "h3").AddTo(b);
            if (!string.IsNullOrEmpty(desc)) Ui.Text(Loc.T(desc), "small", "dim").AddTo(b);
        }

        void Back(VisualElement col, Page to) => Ui.Btn("← " + Loc.T("ui.menu.back"), () => Go(to), "btn-ghost", "menu-back").AddTo(col);

        // ---------------- 選任務 ----------------

        void Missions(VisualElement col)
        {
            Back(col, Page.Root);
            Ui.Text(Loc.T(_multi ? "ui.menu.host" : "ui.menu.solo"), "eyebrow").AddTo(col);
            Ui.Text(Loc.T("ui.menu.pick_mission"), "h2").AddTo(col);
            var tabs = Ui.Row("tabs").AddTo(col);
            Ui.Btn(Loc.T("ui.menu.tab_normal"), () => { _missionTab = 0; Rebuild(); }, "tab").Cls("tab--on", _missionTab == 0).AddTo(tabs);
            Ui.Btn(Loc.T("ui.menu.tab_history"), () => { _missionTab = 1; Rebuild(); }, "tab").Cls("tab--on", _missionTab == 1).AddTo(tabs);
            foreach (var s in D.Scenarios.Where(x => (x.Historical != null) == (_missionTab == 1)))
            {
                var sc = s;
                var card = Ui.El("scn-card", "menu-card").AddTo(col);
                if (sc.Historical != null) Ui.Text(Loc.T("ui.title.history_badge") + " · " + sc.Historical.Year, "scn-badge").AddTo(card);
                else Ui.Text(Loc.T("ui.objective." + sc.Objective), "scn-badge", "scn-badge--plain").AddTo(card);
                Ui.Text(Loc.T(sc.Name), "h3", "scn-name").AddTo(card);
                Ui.Text(Summary(Loc.T(sc.Briefing)), "small", "dim", "scn-desc").AddTo(card);
                var stats = Ui.Row("scn-stats").AddTo(card);
                Ui.Text(Fmt.Money(sc.BudgetMusd * (sc.Historical != null ? sc.Historical.BudgetCap : 1)), "mono", "small").AddTo(stats);
                Ui.Text("·", "faint", "small", "scn-dot").AddTo(stats);
                Ui.Text(Fmt.Years(sc.TargetYears), "mono", "small").AddTo(stats);
                card.RegisterCallback<ClickEvent>(_ => { _scenario = sc.Id; Sfx.Click(); Go(Page.Difficulty); });
                card.RegisterCallback<PointerEnterEvent>(_ => Sfx.Hover());
            }
        }

        static string Summary(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int cut = text.IndexOfAny(new[] { '.', '。' });
            string s = cut > 0 ? text.Substring(0, cut + 1) : text;
            return s.Length > 140 ? s.Substring(0, 137) + "…" : s;
        }

        // ---------------- 選難度 ----------------

        void Difficulty(VisualElement col)
        {
            Back(col, Page.Missions);
            Ui.Text(Loc.T(D.Scenario(_scenario)?.Name), "eyebrow").AddTo(col);
            Ui.Text(Loc.T("ui.menu.pick_difficulty"), "h2").AddTo(col);
            foreach (var df in D.SelectableDifficulties)
            {
                var id = df.Id;
                var b = Ui.Btn("", () => Start(id), "menu-item").AddTo(col);
                b.Clear();
                var head = Ui.Row().AddTo(b);
                Ui.Text(Loc.T(df.Name), "h3").AddTo(head);
                if (id == "engineer") Ui.Text(Loc.T("ui.menu.default"), "small", "accent", "menu-tag").AddTo(head);
                Ui.Text(Loc.T(df.Description), "small", "dim").AddTo(b);
            }
            if (_multi && !Multiplayer.Available) Ui.Text(Loc.T("ui.menu.no_net"), "small", "tight").AddTo(col);
            if (_busy) Ui.Text(Loc.T("ui.menu.connecting"), "small", "accent").AddTo(col);
        }

        void Start(string difficulty)
        {
            if (_busy) return;
            Sfx.Confirm();
            if (!_multi) { GameSession.StartSolo(_scenario, difficulty); return; }
            if (!Multiplayer.Available) { _error = "ui.menu.no_net"; Rebuild(); return; }
            _busy = true;
            Rebuild();
            Multiplayer.Host(_scenario, difficulty, Done);
        }

        void Done(string err)
        {
            _busy = false;
            _error = err ?? "";
            if (!string.IsNullOrEmpty(_error)) { Sfx.NoGo(); Rebuild(); }
        }

        // ---------------- 加入房間 ----------------

        void Join(VisualElement col)
        {
            Back(col, Page.Root);
            Ui.Text(Loc.T("ui.menu.join"), "h2").AddTo(col);
            Ui.Text(Loc.T("ui.menu.join_hint"), "small", "dim").AddTo(col);
            var code = new TextField { value = _code, maxLength = 8 };
            code.AddToClassList("code-field");
            code.RegisterValueChangedCallback(e => _code = e.newValue.Trim().ToUpperInvariant());
            col.Add(code);
            var row = Ui.Row().AddTo(col);
            Ui.Btn(Loc.T("ui.menu.join_go"), () =>
            {
                if (!Multiplayer.Available) { _error = "ui.menu.no_net"; Rebuild(); return; }
                if (string.IsNullOrEmpty(_code)) return;
                _busy = true; Rebuild();
                Multiplayer.Join(_code, Done);
            }, "btn-primary").AddTo(row);

            Ui.Text(Loc.T("ui.menu.lan"), "eyebrow", "menu-sub").AddTo(col);
            var ip = new TextField { value = _ip };
            ip.AddToClassList("code-field");
            ip.RegisterValueChangedCallback(e => _ip = e.newValue.Trim());
            col.Add(ip);
            var row2 = Ui.Row().AddTo(col);
            Ui.Btn(Loc.T("ui.menu.lan_join"), () =>
            {
                if (Multiplayer.JoinDirect == null) { _error = "ui.menu.no_net"; Rebuild(); return; }
                _busy = true; Rebuild();
                Multiplayer.JoinDirect(_ip, Done);
            }, "btn-ghost").AddTo(row2);
            Ui.Btn(Loc.T("ui.menu.lan_host"), () =>
            {
                if (Multiplayer.HostDirect == null) { _error = "ui.menu.no_net"; Rebuild(); return; }
                _busy = true; Rebuild();
                Multiplayer.HostDirect(_scenario ?? "earth_observation", "engineer", Done);
            }, "btn-ghost").AddTo(row2);
            if (_busy) Ui.Text(Loc.T("ui.menu.connecting"), "small", "accent").AddTo(col);
        }

        // ---------------- 設定 ----------------

        void Settings(VisualElement col)
        {
            Back(col, Page.Root);
            Ui.Text(Loc.T("ui.menu.settings"), "h2").AddTo(col);
            Ui.Btn(Loc.IsZh ? "English" : "中文", Loc.Toggle, "btn-ghost").AddTo(col);
            // 和遊戲中選單（Tab）同一組控制項
            var box = Ui.El("gm-col", "gm-settings").AddTo(col);
            GameMenu.TextSizeControl(box);
            GameMenu.SoundControls(box);
            GameMenu.MouseControl(box);
            box.RegisterCallback<DetachFromPanelEvent>(_ => GameSettings.Save());
        }

        // ---------------- avatar ----------------

        void OpenAvatar()
        {
            _edit = Profile.Look.Clone();
            Profile.SetPreview(_edit);
            _page = Page.Avatar;
            _error = "";
            if (Root != null && Root.childCount > 0) Rebuild();
        }

        void AvatarEditor(VisualElement col)
        {
            Ui.Text(Loc.T(Profile.HasAvatar ? "ui.avatar.edit" : "ui.avatar.first"), "eyebrow").AddTo(col);
            Ui.Text(Loc.T("ui.avatar.title"), "h2").AddTo(col);

            Ui.Text(Loc.T("ui.avatar.name"), "small", "dim").AddTo(col);
            var name = new TextField { value = _edit.Name, maxLength = AvatarLook.MaxName };
            name.AddToClassList("code-field");
            name.RegisterValueChangedCallback(e => { _edit.Name = e.newValue; Profile.SetPreview(_edit); });
            col.Add(name);

            Ui.Text(Loc.T("ui.avatar.suit"), "small", "dim").AddTo(col);
            Swatches(col, AvatarLookColors.Suits, _edit.Suit, i => _edit.Suit = i);
            Ui.Text(Loc.T("ui.avatar.helmet"), "small", "dim").AddTo(col);
            Options(col, new[] { "ui.avatar.helmet.0", "ui.avatar.helmet.1", "ui.avatar.helmet.2" }, _edit.Helmet, i => _edit.Helmet = i);
            Ui.Text(Loc.T("ui.avatar.visor"), "small", "dim").AddTo(col);
            Swatches(col, AvatarLookColors.Visors, _edit.Visor, i => _edit.Visor = i);
            Ui.Text(Loc.T("ui.avatar.accessory"), "small", "dim").AddTo(col);
            Options(col, new[] { "ui.avatar.acc.0", "ui.avatar.acc.1", "ui.avatar.acc.2", "ui.avatar.acc.3" }, _edit.Accessory, i => _edit.Accessory = i);

            var row = Ui.Row("modal-actions").AddTo(col);
            if (Profile.HasAvatar) Ui.Btn(Loc.T("ui.avatar.cancel"), () => { Profile.SetPreview(null); Go(Page.Root); }, "btn-ghost").AddTo(row);
            Ui.Spacer().AddTo(row);
            Ui.Btn(Loc.T("ui.avatar.ok"), () =>
            {
                Sfx.Confirm();
                Profile.Save(_edit);
                Profile.SetPreview(null);
                Go(Page.Root);
            }, "btn-primary", "btn-big").AddTo(row);
        }

        void Swatches(VisualElement col, string[] colors, int current, System.Action<int> set)
        {
            var row = Ui.Row("swatches").AddTo(col);
            for (int i = 0; i < colors.Length; i++)
            {
                int k = i;
                var b = Ui.Btn("", () => { set(k); Profile.SetPreview(_edit); Rebuild(); }, "swatch").Cls("swatch--on", i == current).AddTo(row);
                if (ColorUtility.TryParseHtmlString(colors[i], out var c)) b.style.backgroundColor = c;
            }
        }

        void Options(VisualElement col, string[] keys, int current, System.Action<int> set)
        {
            var row = Ui.Row().AddTo(col);
            for (int i = 0; i < keys.Length; i++)
            {
                int k = i;
                Ui.Btn(Loc.T(keys[i]), () => { set(k); Profile.SetPreview(_edit); Rebuild(); }, "chip").Cls("chip--on", i == current).AddTo(row);
            }
        }

        protected override void OnClose()
        {
            _busy = false;
            Profile.SetPreview(null);
            if (_page == Page.Avatar && Profile.HasAvatar) _page = Page.Root;
        }
    }

    /// <summary>avatar 顏色（和 Presentation 的 AvatarBuilder 同一組，UI 組件看不到 Presentation，所以用十六進位字串再寫一次）。</summary>
    public static class AvatarLookColors
    {
        public static readonly string[] Suits = { "#EFF0F5", "#FF8021", "#2973F2", "#33BF73", "#F2C726", "#9E61EB", "#EB475C", "#383D4D" };
        public static readonly string[] Visors = { "#F2B333", "#1A8CF2", "#0F1217", "#BF4DD9" };
    }
}
