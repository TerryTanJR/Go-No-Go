using System;
using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// Facility 場景（大廳、組裝區、中控室）的介面：準星、資訊框與按鍵提示、安裝進度環、場景標籤，
    /// 以及按 E 打開的各種面板（儀表板、手冊、電話、控制台…）。面板開著時角色不能動、滑鼠放開。
    /// Esc 先關面板；Tab／Esc 的遊戲選單在 <see cref="UIRoot"/>。個人模式按 T 叫出平板。
    /// </summary>
    public sealed class FacilityScreen : GameScreen
    {
        VisualElement _hud, _labels, _modal, _phaseBar;
        VisualElement _info, _infoKeys;
        Label _infoTitle, _infoSub, _infoBody, _area, _held, _hint;
        Button _mic;
        ProgressRing _ring;
        VisualElement _crosshair;
        FacPanel _panel;
        string _panelId;
        readonly Dictionary<WorldLabel, Label> _labelEls = new Dictionary<WorldLabel, Label>();
        SessionPhase _lastPhase;
        OpsBar _opsBar;
        TutorialCoach _coach;
        WorldScreens _screens;
        PollBanner _poll;

        protected override void Build()
        {
            _labelEls.Clear();
            GameSession.EnsureMission();
            _labels = Ui.El("world-labels").AddTo(Root);
            _labels.pickingMode = PickingMode.Ignore;
            _hud = Ui.El("hud").AddTo(Root);
            _hud.pickingMode = PickingMode.Ignore;

            // 左上：所在區域、手上的零件
            var tl = Ui.El("hud-tl").AddTo(_hud);
            tl.pickingMode = PickingMode.Ignore;
            _area = Ui.Text("", "hud-area").AddTo(tl);
            _held = Ui.Text("", "hud-held").AddTo(tl);
            _phaseBar = Ui.El("hud-phase").AddTo(tl);

            // 右上：麥克風（在語音頻道裡才顯示）、語言
            var tr = Ui.Row("hud-tr").AddTo(_hud);
            _mic = Ui.Btn("", ToggleMic, "btn-small", "btn-ghost").AddTo(tr);
            var lang = Ui.Btn(Loc.IsZh ? "EN" : "中文", Loc.Toggle, "btn-small", "btn-ghost").AddTo(tr);
            // 方向鍵是用來走路的：不讓 UI 拿方向鍵在這兩顆之間移動焦點（滑鼠還是點得到）
            _mic.focusable = false;
            lang.focusable = false;

            // 中央：準星與進度環
            _crosshair = Ui.El("crosshair").AddTo(_hud);
            _crosshair.pickingMode = PickingMode.Ignore;
            _ring = new ProgressRing().AddTo(_hud);
            _ring.AddToClassList("hold-ring");

            // 資訊框
            _info = Ui.El("info-box").AddTo(_hud);
            _info.pickingMode = PickingMode.Ignore;
            _infoTitle = Ui.Text("", "info-title").AddTo(_info);
            _infoSub = Ui.Text("", "small", "tight").AddTo(_info);
            _infoBody = Ui.Text("", "info-body").AddTo(_info);
            _infoKeys = Ui.Row("info-keys").AddTo(_info);

            // 左下：操作說明
            _hint = Ui.Text(Loc.T("ui.hud.controls" + (M.S.Multiplayer ? "" : "_solo")), "hud-controls").AddTo(_hud);

            _opsBar = new OpsBar(_hud);

            _modal = Ui.El("modal-layer").AddTo(Root);
            _modal.style.display = DisplayStyle.None;

            // 點名橫幅放在面板層上面：開著面板也看得到、按得到
            _poll = new PollBanner(Root);

            if (M.S.Tutorial) _coach = new TutorialCoach(Root);

            On(() => Hud.PanelRequested += OpenPanel, () => Hud.PanelRequested -= OpenPanel);
            On(() => Hud.ToastRequested += OnToast, () => Hud.ToastRequested -= OnToast);
            On(() => M.Changed += OnChanged, () => { if (GameSession.HasMission) M.Changed -= OnChanged; });
            On(() => M.Rejected += OnRejected, () => { if (GameSession.HasMission) M.Rejected -= OnRejected; });

            _screens ??= new WorldScreens();
            _screens.MarkDirty();
            _lastPhase = M.S.Phase;
            OnChanged();
            if (_panelId != null) { var id = _panelId; _panelId = null; OpenPanel(id, ""); }
            else ShowPhasePanel();
        }

        protected override void OnClose()
        {
            _coach?.Dispose();
            _screens?.Dispose();
            _screens = null;
            if (_panel != null) Hud.PanelOpen = false;
        }

        void OnToast(string text) => UIRoot.Toast(text);
        void OnRejected(string code) { Sfx.NoGo(); UIRoot.Toast(Loc.T(code)); }

        void OnChanged()
        {
            var phase = M.S.Phase;
            if (phase != _lastPhase)
            {
                _lastPhase = phase;
                ClosePanel();
                ShowPhasePanel();
            }
            _panel?.Refresh();
            _opsBar.Refresh();
            _poll?.Refresh();
            _screens?.MarkDirty();
            UpdatePhaseBar();
        }

        /// <summary>簡報、大廳：階段一開始就打開的面板。</summary>
        void ShowPhasePanel()
        {
            if (M.S.Phase == SessionPhase.Briefing) OpenPanel("briefing", "");
            else if (M.S.Phase == SessionPhase.Lobby) OpenPanel("lobby", "");
        }

        void UpdatePhaseBar()
        {
            _phaseBar.Clear();
            var s = M.S;
            if (s.Phase == SessionPhase.Design)
            {
                Ui.Text(Loc.T(M.Scenario.Name) + " · " + Loc.T(M.Difficulty.Name), "small", "dim").AddTo(_phaseBar);
                if (s.Launches.Count > 0)
                    Ui.Text(Loc.T("ui.hud.attempt", ("n", s.Launches.Count + 1)), "small", "tight").AddTo(_phaseBar);
                if (M.IsHistorical && s.DesignLocked(D)) Ui.Text(Loc.T("ui.hud.history_locked"), "small", "accent").AddTo(_phaseBar);
            }
        }

        // ---------------- 面板 ----------------

        void OpenPanel(string id, string arg)
        {
            ClosePanel();
            _panel = FacPanel.Create(id);
            if (_panel == null) return;
            _panelId = id;
            _modal.Clear();
            _modal.style.display = DisplayStyle.Flex;
            var card = Ui.El("modal", _panel.WideClass).AddTo(_modal);
            var head = Ui.Row("modal-head").AddTo(card);
            Ui.Text(_panel.Title, "eyebrow").AddTo(head);
            Ui.Spacer().AddTo(head);
            if (_panel.Closable) Ui.Btn(Loc.T("ui.fac.close") + "  [Esc]", ClosePanel, "btn-small", "btn-ghost").AddTo(head);
            var body = Ui.El("modal-body").AddTo(card);
            _panel.Attach(body, ClosePanel);
            _openedFrame = Time.frameCount;
            Hud.PanelOpen = true;
            PlayerControllerCursor(false);
        }

        void ClosePanel()
        {
            if (_panel == null) return;
            _panel.Detach();
            _panel = null;
            _panelId = null;
            _modal.Clear();
            _modal.style.display = DisplayStyle.None;
            Hud.PanelOpen = false;
            Hud.ClosedFrame = Time.frameCount;
            // 關掉面板直接回到遊戲：重新鎖住游標（瀏覽器在按鈕事件裡允許）
            PlayerControllerCursor(true);
        }

        static void PlayerControllerCursor(bool locked)
        {
            UnityEngine.Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !locked;
        }

        /// <summary>Esc：開著可以關的面板就關掉；否則交給遊戲選單。</summary>
        public override bool HandleEscape()
        {
            if (_panel == null || !_panel.Closable) return false;
            ClosePanel();
            return true;
        }

        // ---------------- 每幀 ----------------

        public override void Tick(float dt)
        {
            if (!GameSession.HasMission) return;
            _screens?.Tick();
            var kb = Keyboard.current;

            // 選單開著、或正在輸入框打字（例如筆記）時，快捷鍵都不作用
            bool keys = kb != null && !Hud.Paused && !UIRoot.IsTyping;

            // G / N：Go/No-Go 點名
            if (keys && _poll != null && _poll.CanVote && (kb.gKey.wasPressedThisFrame || kb.nKey.wasPressedThisFrame))
                _poll.Vote(kb.gKey.wasPressedThisFrame);
            // M：自己的麥克風靜音
            else if (keys && kb.mKey.wasPressedThisFrame && Multiplayer.VoiceConnected)
                ToggleMic();
            // T：個人模式的平板（手冊、儀表板）
            else if (keys && kb.tKey.wasPressedThisFrame && !M.S.Multiplayer)
            {
                if (_panelId == "tablet") ClosePanel();
                else if (_panel == null) OpenPanel("tablet", "");
            }
            // 在面板上按 E 也可以關（同一個鍵開關）
            else if (keys && kb.eKey.wasPressedThisFrame && _panel != null && _panel.Closable && _panel.CloseWithE && _openedFrame != Time.frameCount)
                ClosePanel();

            _panel?.Tick(dt);
            _opsBar.Tick(dt);
            _poll?.Tick(dt);
            _coach?.Tick(dt);

            bool playing = _panel == null && !Hud.Paused;
            // 第三人稱不靠準星選目標（角色的頭也剛好在畫面中央），只有第一人稱顯示
            _crosshair.style.display = playing && Hud.FirstPerson ? DisplayStyle.Flex : DisplayStyle.None;
            _hud.style.opacity = _panel == null ? 1f : 0.35f;

            // 資訊框
            var t = playing ? Hud.Target : null;
            _info.style.display = t != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (t != null)
            {
                _infoTitle.text = t.Title;
                _infoSub.text = t.Subtitle ?? "";
                _infoSub.style.display = string.IsNullOrEmpty(t.Subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
                _infoBody.text = t.Body ?? "";
                _infoBody.style.display = string.IsNullOrEmpty(t.Body) ? DisplayStyle.None : DisplayStyle.Flex;
                if (!string.IsNullOrEmpty(t.Color) && ColorUtility.TryParseHtmlString(t.Color, out var c)) _info.style.borderLeftColor = c;
                else _info.style.borderLeftColor = new Color(0.22f, 0.78f, 0.96f);
                PlaceInfo(t.Anchor);
                string sig = string.Join("|", t.Keys.ConvertAll(k => k.Key + k.Label));
                if (sig != _keysSig)
                {
                    _keysSig = sig;
                    _infoKeys.Clear();
                    foreach (var k in t.Keys)
                    {
                        var kh = Ui.Row("key-hint").AddTo(_infoKeys);
                        Ui.Text(k.Key, "keycap").AddTo(kh);
                        Ui.Text(k.Label, "key-label").AddTo(kh);
                    }
                }
            }
            _ring.Value = Hud.Progress;
            _ring.style.display = Hud.Progress > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            _mic.style.display = Multiplayer.VoiceConnected ? DisplayStyle.Flex : DisplayStyle.None;
            _mic.text = Loc.T(Multiplayer.MicMuted ? "ui.voice.muted" : "ui.voice.on") + "  [M]";
            _mic.EnableInClassList("btn-danger", Multiplayer.MicMuted);
            _mic.EnableInClassList("btn-ghost", !Multiplayer.MicMuted);

            _area.text = string.IsNullOrEmpty(Hud.Area) ? "" : Loc.T("ui.area." + Hud.Area) + (Hud.FirstPerson ? "  ·  " + Loc.T("ui.hud.first_person") : "");
            var held = M.S.HeldBy(GameSession.LocalPlayer);
            _held.text = held != null ? Loc.T("ui.hud.holding", ("code", D.Part(held)?.Code ?? "")) : "";

            DrawLabels();
        }

        int _openedFrame;
        string _keysSig;

        /// <summary>
        /// 資訊框位置：有錨點（第三人稱）就貼在物件上方、往右偏一點，不會跑出畫面；沒有就用 USS 的位置（準星右下）。
        /// </summary>
        void PlaceInfo(Vector3? anchor)
        {
            var cam = Camera.main;
            var panel = Root.panel;
            if (anchor == null || cam == null || panel == null || cam.WorldToViewportPoint(anchor.Value).z <= 0.1f)
            {
                _info.style.left = StyleKeyword.Null;
                _info.style.top = StyleKeyword.Null;
                _info.style.translate = StyleKeyword.Null;
                return;
            }
            var sp = RuntimePanelUtils.CameraTransformWorldToPanel(panel, anchor.Value, cam);
            var size = Root.layout.size;
            var box = _info.layout.size;
            if (float.IsNaN(box.x)) box = Vector2.zero;
            _info.style.left = Mathf.Clamp(sp.x + 24f, 16f, Mathf.Max(16f, size.x - box.x - 16f));
            _info.style.top = Mathf.Clamp(sp.y - box.y, 16f, Mathf.Max(16f, size.y - box.y - 16f));
            _info.style.translate = new Translate(0, 0);
        }

        static void ToggleMic() => Multiplayer.SetMicMuted?.Invoke(!Multiplayer.MicMuted);

        /// <summary>場景裡的文字標籤：投影到畫面上（太遠、在背後、被牆擋住的不畫）。</summary>
        void DrawLabels()
        {
            var cam = Camera.main;
            var panel = Root.panel;
            if (cam == null || panel == null) return;
            var seen = new HashSet<WorldLabel>();
            foreach (var wl in WorldLabel.All)
            {
                if (wl == null || !wl.isActiveAndEnabled) continue;
                var wp = wl.WorldPos;
                var v = cam.WorldToViewportPoint(wp);
                float dist = Vector3.Distance(cam.transform.position, wp);
                bool visible = v.z > 0.1f && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f && dist < wl.maxDistance;
                if (visible && Physics.Linecast(cam.transform.position, wp, out var hit, ~(1 << 2), QueryTriggerInteraction.Ignore)
                    && hit.distance < dist - 0.4f && hit.collider.GetComponentInParent<WorldLabel>() != wl)
                    visible = false;
                if (!_labelEls.TryGetValue(wl, out var el))
                {
                    el = Ui.Text("", "world-label", "world-label--" + wl.style).AddTo(_labels);
                    el.pickingMode = PickingMode.Ignore;
                    _labelEls[wl] = el;
                }
                seen.Add(wl);
                el.style.display = visible && _panel == null ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                el.text = wl.Resolve();
                if (!string.IsNullOrEmpty(wl.color) && ColorUtility.TryParseHtmlString(wl.color, out var c)) el.style.borderBottomColor = c;
                var sp = RuntimePanelUtils.CameraTransformWorldToPanel(panel, wp, cam);
                el.style.left = sp.x;
                el.style.top = sp.y;
                el.style.opacity = Mathf.Clamp01((wl.maxDistance - dist) / 2f);
            }
            var stale = new List<WorldLabel>();
            foreach (var kv in _labelEls) if (!seen.Contains(kv.Key)) stale.Add(kv.Key);
            foreach (var k in stale) { _labelEls[k].RemoveFromHierarchy(); _labelEls.Remove(k); }
        }
    }

    /// <summary>安裝進度環：長按 V 時繞著準星填滿。</summary>
    public sealed class ProgressRing : VisualElement
    {
        float _v;
        public float Value { get => _v; set { if (Mathf.Abs(value - _v) < 0.002f) return; _v = value; MarkDirtyRepaint(); } }

        public ProgressRing()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var p = ctx.painter2D;
            var r = contentRect;
            var c = r.center;
            float rad = Mathf.Min(r.width, r.height) / 2 - 4;
            p.lineWidth = 5;
            p.lineCap = LineCap.Round;
            p.strokeColor = new Color(1, 1, 1, 0.18f);
            p.BeginPath();
            p.Arc(c, rad, 0, 360);
            p.Stroke();
            if (_v <= 0) return;
            p.strokeColor = new Color(0.22f, 0.86f, 1f);
            p.BeginPath();
            p.Arc(c, rad, -90, -90 + 360 * _v);
            p.Stroke();
        }
    }
}
