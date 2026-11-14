using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 主選單：左邊選單（新手訓練、個人任務、建立多人房間、加入房間、設定），右邊是自己的 avatar（3D，在 MainMenu 場景裡）。
    /// 右上角是自己的名字，點名字或直接點右邊的太空人就能改造型。第一次進遊戲先捏 avatar。
    /// 個人任務和建房：選任務（一般任務／歷史關卡）→ 選難度 → 開始。
    /// 選項的說明只在滑鼠指到時顯示在選單下方同一個位置，平常只列名稱。
    /// </summary>
    public sealed class MainMenuScreen : GameScreen
    {
        enum Page { Root, Missions, Join, Settings, Avatar }

        static Page _page = Page.Root;
        static bool _multi;
        static int _missionTab;
        static string _scenario;
        static string _code = "";
        static string _ip = "127.0.0.1";
        static AvatarLook _edit;
        static bool _busy;
        static bool _lan;
        static string _error = "";
        Label _desc;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            Sfx.SetMusic(Sfx.Music.Menu);
            PlayerControllerUnlock();
            if (!Profile.HasAvatar && _page != Page.Avatar) OpenAvatar();

            var top = Ui.El("topbar", "topbar--clear").AddTo(Root);
            Ui.Text("GO / NO-GO", "brand").AddTo(top);
            Ui.Spacer().AddTo(top);
            // 右上：自己的名字（改造型的按鈕在太空人腳下；語言在設定頁）
            if (_page != Page.Avatar)
            {
                Ui.Text(Profile.Look.Name, "menu-me", "menu-me-name").AddTo(top);
                AvatarHotspot();
            }

            var left = Ui.El("menu-col", "fade-in").AddTo(Root);
            Ui.AnimateIn(left, "fade-in--on", 40);
            switch (_page)
            {
                case Page.Root: RootMenu(left); break;
                case Page.Missions: Missions(left); break;
                case Page.Join: Join(left); break;
                case Page.Settings: Settings(left); break;
                case Page.Avatar: AvatarEditor(left); break;
            }

            if (!string.IsNullOrEmpty(_error)) Ui.Text(Loc.T(_error), "small", "over", "menu-error").AddTo(left);
        }

        /// <summary>
        /// 右邊 3D 太空人：整塊都能點；腳下的平台下方常駐一顆「編輯造型」按鈕（大廳遊戲的換裝入口都放在角色腳下），
        /// 滑鼠指到太空人時按鈕亮起來。
        /// </summary>
        void AvatarHotspot()
        {
            var hit = Ui.El("menu-avatar-hit").AddTo(Root);
            hit.RegisterCallback<ClickEvent>(_ => { Sfx.Click(); OpenAvatar(); });
            var btn = Ui.Btn("✎  " + Loc.T("ui.menu.edit_avatar"), OpenAvatar, "menu-avatar-btn").AddTo(Root);
            hit.RegisterCallback<PointerEnterEvent>(_ => btn.AddToClassList("menu-avatar-btn--hot"));
            hit.RegisterCallback<PointerLeaveEvent>(_ => btn.RemoveFromClassList("menu-avatar-btn--hot"));
        }

        /// <summary>選單下方的說明列：滑鼠指到哪一項就顯示那一項的說明，移開就回到 <paramref name="fallback"/>。</summary>
        void DescLine(VisualElement col, string fallback = "")
        {
            _desc = Ui.Text(fallback, "small", "dim", "menu-desc").AddTo(col);
            _desc.userData = fallback;
        }

        void Describe(VisualElement el, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            el.RegisterCallback<PointerEnterEvent>(_ => { if (_desc != null) _desc.text = text; });
            el.RegisterCallback<PointerLeaveEvent>(_ => { if (_desc != null) _desc.text = _desc.userData as string ?? ""; });
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
            Item(col, "ui.menu.tutorial", "ui.menu.tutorial_desc", () => { Sfx.Confirm(); UIRoot.BeginLoading(false); GameSession.StartSolo("earth_observation", "tutorial", true); });
            Item(col, "ui.menu.solo", "ui.menu.solo_desc", () => { _multi = false; Go(Page.Missions); });
            Item(col, "ui.menu.host", "ui.menu.host_desc", () => { _multi = true; Go(Page.Missions); });
            Item(col, "ui.menu.join", "ui.menu.join_desc", () => Go(Page.Join));
            Item(col, "ui.menu.settings", "", () => Go(Page.Settings));
            DescLine(col);
            Ui.Btn(Loc.T("ui.menu.title"), () => SceneFlow.Go(SceneFlow.Boot), "btn-ghost", "menu-back").AddTo(col);
        }

        void Item(VisualElement col, string key, string desc, System.Action click)
        {
            var b = Ui.Btn("", click, "menu-item").AddTo(col);
            b.Clear();
            Ui.Text(Loc.T(key), "h3").AddTo(b);
            if (!string.IsNullOrEmpty(desc)) Describe(b, Loc.T(desc));
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
                Ui.Text(Fmt.Money(GameData.Budget(sc)), "mono", "small").AddTo(stats);
                Ui.Text("·", "faint", "small", "scn-dot").AddTo(stats);
                Ui.Text(Fmt.Years(sc.TargetYears), "mono", "small").AddTo(stats);
                // 一關一個難度（採購機制 v2 §7）：卡片上直接標出來，點了就開始。歷史關卡不分難度，不標
                if (sc.Historical == null)
                {
                    Ui.Text("·", "faint", "small", "scn-dot").AddTo(stats);
                    Ui.Text(Loc.T(D.DifficultyFor(sc).Name), "small", "accent").AddTo(stats);
                }
                card.RegisterCallback<ClickEvent>(_ => { _scenario = sc.Id; Start(null); });
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

        /// <summary>開始任務。難度跟著關卡走：difficulty 傳 null 就用關卡的難度。</summary>
        void Start(string difficulty)
        {
            if (_busy) return;
            Sfx.Confirm();
            if (!_multi) { UIRoot.BeginLoading(false); GameSession.StartSolo(_scenario, difficulty); return; }
            if (!Multiplayer.Available) { _error = "ui.menu.no_net"; Rebuild(); return; }
            Connect(() => Multiplayer.Host(_scenario, difficulty, Done));
        }

        /// <summary>開始連線：蓋上過場（期間不能再點介面），成功時場景換掉、過場自己收起來。</summary>
        void Connect(System.Action go)
        {
            _busy = true;
            _error = "";
            UIRoot.BeginLoading(true);
            go();
        }

        void Done(string err)
        {
            _busy = false;
            _error = err ?? "";
            if (string.IsNullOrEmpty(_error)) return;
            UIRoot.EndLoading();
            Sfx.NoGo();
            Rebuild();
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
                Connect(() => Multiplayer.Join(_code, Done));
            }, "btn-primary").AddTo(row);

            // 同一個網路直接連（測試用）：收在「進階」裡
            Ui.Btn((_lan ? "▾  " : "▸  ") + Loc.T("ui.menu.advanced"), () => { _lan = !_lan; Rebuild(); }, "btn-ghost", "menu-sub", "menu-back").AddTo(col);
            if (!_lan) return;
            Ui.Text(Loc.T("ui.menu.lan"), "eyebrow").AddTo(col);
            var ip = new TextField { value = _ip };
            ip.AddToClassList("code-field");
            ip.RegisterValueChangedCallback(e => _ip = e.newValue.Trim());
            col.Add(ip);
            var row2 = Ui.Row().AddTo(col);
            Ui.Btn(Loc.T("ui.menu.lan_join"), () =>
            {
                if (Multiplayer.JoinDirect == null) { _error = "ui.menu.no_net"; Rebuild(); return; }
                Connect(() => Multiplayer.JoinDirect(_ip, Done));
            }, "menu-btn-gap").AddTo(row2);
            Ui.Btn(Loc.T("ui.menu.lan_host"), () =>
            {
                if (Multiplayer.HostDirect == null) { _error = "ui.menu.no_net"; Rebuild(); return; }
                Connect(() => Multiplayer.HostDirect(_scenario ?? "earth_observation", null, Done));
            }).AddTo(row2);
        }

        // ---------------- 設定 ----------------

        void Settings(VisualElement col)
        {
            Back(col, Page.Root);
            Ui.Text(Loc.T("ui.menu.settings"), "h2").AddTo(col);
            // 和遊戲中選單（Tab）同一組控制項
            var box = Ui.El("gm-col", "gm-settings").AddTo(col);
            GameMenu.LanguageControl(box);
            GameMenu.TextSizeControl(box);
            GameMenu.SoundControls(box);
            GameMenu.MouseControl(box);
            box.RegisterCallback<DetachFromPanelEvent>(_ => GameSettings.Save());
        }

        // ---------------- avatar ----------------

        void OpenAvatar()
        {
            _edit = Profile.Look.Clone();
            _avatarTab = 0;
            Profile.SetPreview(_edit);
            _page = Page.Avatar;
            _error = "";
            if (Root != null && Root.childCount > 0) Rebuild();
        }

        static int _avatarTab;

        void AvatarEditor(VisualElement col)
        {
            col.AddToClassList("menu-col--wide");
            Ui.Text(Loc.T(Profile.HasAvatar ? "ui.avatar.edit" : "ui.avatar.first"), "eyebrow").AddTo(col);
            Ui.Text(Loc.T("ui.avatar.title"), "h2").AddTo(col);

            var nameRow = Ui.Row("avatar-name").AddTo(col);
            var name = new TextField { value = _edit.Name, maxLength = AvatarLook.MaxName };
            name.AddToClassList("code-field");
            name.style.flexGrow = 1;
            name.RegisterValueChangedCallback(e => { _edit.Name = e.newValue; Profile.SetPreview(_edit); });
            nameRow.Add(name);
            Ui.Btn(Loc.T("ui.avatar.random"), () =>
            {
                var rnd = new System.Random();
                _edit.Suit = rnd.Next(AvatarLook.Suits); _edit.Accent = rnd.Next(AvatarLook.Accents);
                _edit.Helmet = rnd.Next(AvatarLook.Helmets); _edit.Visor = rnd.Next(AvatarLook.Visors);
                _edit.Lower = rnd.Next(AvatarLook.Lowers); _edit.Accessory = rnd.Next(AvatarLook.Accessories);
                Profile.SetPreview(_edit);
                Rebuild();
            }, "btn-small").AddTo(nameRow);

            // 分頁：切換時只重畫下面的內容，不整頁重建
            var tabs = Ui.Row("tabs").AddTo(col);
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("avatar-scroll");
            string[] tabKeys = { "ui.avatar.tab.look", "ui.avatar.tab.colors", "ui.avatar.tab.gear" };
            var tabBtns = new Button[tabKeys.Length];
            for (int i = 0; i < tabKeys.Length; i++)
            {
                int k = i;
                tabBtns[i] = Ui.Btn(Loc.T(tabKeys[i]), () =>
                {
                    _avatarTab = k;
                    for (int j = 0; j < tabBtns.Length; j++) tabBtns[j].Cls("tab--on", j == k);
                    scroll.contentContainer.Clear();
                    AvatarTab(scroll.contentContainer);
                }, "tab").Cls("tab--on", i == _avatarTab).AddTo(tabs);
            }
            col.Add(scroll);
            AvatarTab(scroll.contentContainer);

            var row = Ui.Row("modal-actions", "avatar-actions").AddTo(col);
            if (Profile.HasAvatar) Ui.Btn(Loc.T("ui.avatar.cancel"), () => { Profile.SetPreview(null); Go(Page.Root); }, "btn-big").AddTo(row);
            Ui.Spacer().AddTo(row);
            Ui.Btn(Loc.T("ui.avatar.ok"), () =>
            {
                Sfx.Confirm();
                Profile.Save(_edit);
                Profile.SetPreview(null);
                Go(Page.Root);
            }, "btn-primary", "btn-big").AddTo(row);
        }

        void AvatarTab(VisualElement c)
        {
            switch (_avatarTab)
            {
                case 0:
                    Cards(c, "ui.avatar.helmet", "helmet", "ui.avatar.helmet.", AvatarLook.Helmets, _edit.Helmet, i => _edit.Helmet = i);
                    Cards(c, "ui.avatar.lower", "lower", "ui.avatar.lower.", AvatarLook.Lowers, _edit.Lower, i => _edit.Lower = i);
                    break;
                case 1:
                    Swatches(c, "ui.avatar.suit", AvatarLookColors.Suits, _edit.Suit, i => _edit.Suit = i);
                    Swatches(c, "ui.avatar.accent", AvatarLookColors.Accents, _edit.Accent, i => _edit.Accent = i);
                    Swatches(c, "ui.avatar.visor", AvatarLookColors.Visors, _edit.Visor, i => _edit.Visor = i);
                    break;
                default:
                    Cards(c, "ui.avatar.accessory", "acc", "ui.avatar.acc.", AvatarLook.Accessories, _edit.Accessory, i => _edit.Accessory = i);
                    break;
            }
        }

        /// <summary>縮圖選項卡：圖 + 名稱，點了立刻換預覽（不重建整頁）。</summary>
        void Cards(VisualElement col, string titleKey, string prefix, string labelPrefix, int count, int current, System.Action<int> set)
        {
            Ui.Text(Loc.T(titleKey), "small", "dim", "avatar-section").AddTo(col);
            var grid = Ui.Row("opt-grid").AddTo(col);
            var cards = new Button[count];
            for (int i = 0; i < count; i++)
            {
                int k = i;
                var b = Ui.Btn("", () =>
                {
                    set(k); Sfx.Click(); Profile.SetPreview(_edit);
                    for (int j = 0; j < count; j++) cards[j].Cls("opt-card--on", j == k);
                }, "opt-card").Cls("opt-card--on", i == current).AddTo(grid);
                b.Clear();
                var img = Ui.El("opt-img").AddTo(b);
                var tex = Resources.Load<Texture2D>($"UI/AvatarOptions/{prefix}_{i}");
                if (tex != null) img.style.backgroundImage = new StyleBackground(tex);
                Ui.Text(Loc.T(labelPrefix + i), "small", "opt-label").AddTo(b);
                cards[i] = b;
            }
        }

        void Swatches(VisualElement col, string titleKey, string[] colors, int current, System.Action<int> set)
        {
            Ui.Text(Loc.T(titleKey), "small", "dim", "avatar-section").AddTo(col);
            var row = Ui.Row("swatches").AddTo(col);
            var all = new Button[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                int k = i;
                var b = Ui.Btn("", () =>
                {
                    set(k); Sfx.Click(); Profile.SetPreview(_edit);
                    for (int j = 0; j < all.Length; j++) all[j].Cls("swatch--on", j == k);
                }, "swatch", "swatch--big").Cls("swatch--on", i == current).AddTo(row);
                if (ColorUtility.TryParseHtmlString(colors[i], out var c)) b.style.backgroundColor = c;
                all[i] = b;
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
        public static readonly string[] Accents = { "#FF5C0F", "#0DB3F2", "#8CD926", "#8C4DF2", "#E61F2E", "#FABF1F", "#FF73B3", "#EBEDF5" };
        public static readonly string[] Visors = { "#F2B333", "#1A8CF2", "#0F1217", "#BF4DD9" };
    }
}
