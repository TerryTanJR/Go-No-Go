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
    /// 以及按 E 打開的各種面板（大屏幕選件、線索、組裝區儀表板、發射、營運席…）。面板開著時角色不能動、滑鼠放開。
    /// Esc 先關面板；Tab／Esc 的遊戲選單在 <see cref="UIRoot"/>。個人模式按 T 叫出平板。
    /// </summary>
    public sealed class FacilityScreen : GameScreen
    {
        VisualElement _hud, _labels, _modal, _phaseBar, _tl;
        VisualElement _info, _infoKeys;
        Label _infoTitle, _infoSub, _infoBody, _area, _held;
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
        VisualElement _briefHint;
        /// <summary>組裝單（右上）：人走到某一座組裝台旁邊時顯示那一座的。</summary>
        VisualElement _order;
        string _orderSig;
        /// <summary>這一局本機已經自動打開過簡報（多人）；回到場景不再重開，要看按 B。</summary>
        static object _briefedFor;
        /// <summary>房主把「全員就緒」的開始視窗關掉了：準備狀態改變前不再自動跳出來。</summary>
        bool _startDismissed;

        protected override void Build()
        {
            _labelEls.Clear();
            _shownArea = null;
            GameSession.EnsureMission();
            _labels = Ui.El("world-labels").AddTo(Root);
            _labels.pickingMode = PickingMode.Ignore;
            _hud = Ui.El("hud").AddTo(Root);
            _hud.pickingMode = PickingMode.Ignore;

            // 左上：進入新區域時淡入一下的區域名、第幾次發射這類狀態（任務名稱、難度在 Tab 選單）
            var tl = _tl = Ui.El("hud-tl").AddTo(_hud);
            tl.pickingMode = PickingMode.Ignore;
            _area = Ui.Text("", "hud-area").AddTo(tl);
            _phaseBar = Ui.El("hud-phase").AddTo(tl);

            // 下方中央：手上的零件
            _held = Ui.Text("", "hud-held").AddTo(_hud);

            // 右上：麥克風（在語音頻道裡才顯示）。語言在 Tab 選單的基本設定
            var tr = Ui.Row("hud-tr").AddTo(_hud);
            _mic = Ui.Btn("", ToggleMic, "btn-small", "btn-ghost").AddTo(tr);
            // 方向鍵是用來走路的：不讓 UI 拿方向鍵移動焦點（滑鼠還是點得到）
            _mic.focusable = false;
            // 任務說明：隨時按 B 打開來再看一次（每個人各自看自己的）。鍵帽 + 文字，一眼看得出是可以按的鍵
            _briefHint = Ui.Row("brief-chip").AddTo(tr);
            _briefHint.pickingMode = PickingMode.Ignore;
            Ui.Text("B", "keycap").AddTo(_briefHint);
            Ui.Text(Loc.T("ui.brief.review_hint"), "brief-chip-label").AddTo(_briefHint);

            // 組裝單：旁邊那座組裝台左側的狀態、正在組的零件、下一個小零件要做什麼事
            _order = Ui.El("ord-hud").AddTo(_hud);
            _order.pickingMode = PickingMode.Ignore;
            _order.style.display = DisplayStyle.None;
            _orderSig = null;

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

            _opsBar = new OpsBar(_hud);

            _modal = Ui.El("modal-layer").AddTo(Root);
            _modal.style.display = DisplayStyle.None;

            // 點名橫幅放在面板層上面：開著面板也看得到、按得到
            _poll = new PollBanner(Root);

            if (M.S.Tutorial) _coach = new TutorialCoach(_tl, _labels);
            else if (M.Difficulty.NextStepGuide) _coach = new TutorialCoach(_tl, _labels, guide: true);

            On(() => Hud.PanelRequested += OpenPanel, () => Hud.PanelRequested -= OpenPanel);
            On(() => Hud.ToastRequested += OnToast, () => Hud.ToastRequested -= OnToast);
            On(() => M.Changed += OnChanged, () => { if (GameSession.HasMission) M.Changed -= OnChanged; });
            On(() => M.Rejected += OnRejected, () => { if (GameSession.HasMission) M.Rejected -= OnRejected; });
            On(() => Codex.Unlocked += OnCard, () => Codex.Unlocked -= OnCard);
            _installedSeen = null;
            _benchSeen = null;

            _screens ??= new WorldScreens();
            _screens.MarkDirty();
            _lastPhase = M.S.Phase;
            OnChanged();
            if (MinigameDev.PickerOnStart) { MinigameDev.PickerOnStart = false; OpenPanel("minigame_debug", ""); }
            else if (_panelId != null) { var id = _panelId; _panelId = null; OpenPanel(id, ""); }
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

        /// <summary>收到一張新的知識卡：跳通知（想看按 K）。</summary>
        void OnCard(string partId)
        {
            var p = D.Part(partId);
            if (p == null) return;
            Sfx.Confirm();
            UIRoot.Toast(Loc.T("ui.codex.toast", ("code", p.Code), ("name", Loc.T(p.Name))), 3.4f);
        }

        HashSet<string> _installedSeen;

        /// <summary>
        /// 新裝上衛星的零件：知識卡收進圖鑑（開局就在衛星上的不算，要自己動手裝過）。
        /// 在組裝台組好的那一刻已經收過了，這裡是給不用組裝的零件和沙盒用的。
        /// </summary>
        void CollectCards()
        {
            var now = new HashSet<string>(M.S.Installed);
            if (_installedSeen != null && M.S.Phase == SessionPhase.Design && M.S.Restarts == _restartsShown)
                foreach (var id in now)
                {
                    if (_installedSeen.Contains(id)) continue;
                    Codex.Unlock(D, id);
                    var recipe = D.Recipe(D.Part(id));
                    if (recipe != null && M.S.Flawless.Contains(recipe.Key)) Codex.Master(D, id);
                }
            _installedSeen = now;
        }

        // ---------------- 組裝台的提示 ----------------

        /// <summary>每座組裝台上一次看到的樣子（正在組哪個零件、裝了幾次、組好了沒、左側被判定錯誤幾次）。</summary>
        sealed class BenchSeen
        {
            public string Part;
            public int Fits = -1, Errors = -1;
            public bool Done;
        }

        BenchSeen[] _benchSeen;

        /// <summary>
        /// 組裝台：左側放了一組認不出來的組合（組合錯誤／不是這次設計選的零件／設計還沒確定）、
        /// 有人裝了一個小零件（對：說出它叫什麼、做什麼；錯：說出那個小零件其實是做什麼的）、
        /// 組好了（收知識卡，零失誤就升級成精通）。裝錯沒有懲罰，但所有人照樣看得到誰裝錯。
        /// 規則都在房主那邊算，這裡只看狀態前後的差別，所以多人時每個人都看得到。
        /// </summary>
        void BenchNotices()
        {
            var s = M.S;
            if (_benchSeen == null || _benchSeen.Length != s.Benches.Count)
            {
                // 第一次看到（剛進場景、中途加入）：只記下現在的樣子，不補播
                _benchSeen = new BenchSeen[s.Benches.Count];
                for (int i = 0; i < s.Benches.Count; i++)
                {
                    var b0 = s.Benches[i];
                    _benchSeen[i] = new BenchSeen { Part = b0.Job?.PartId, Fits = b0.Job?.Fits ?? -1, Done = b0.Job?.Done ?? false, Errors = b0.Errors };
                }
                return;
            }
            for (int i = 0; i < s.Benches.Count; i++)
            {
                var b = s.Benches[i];
                var seen = _benchSeen[i];
                var job = b.Job;

                // 左側：放錯組合
                if (b.Errors > seen.Errors && seen.Errors >= 0 && b.LastBy == GameSession.LocalPlayer)
                    UIRoot.Toast(Loc.T("ui.bench.status." + b.LastError.ToString().ToLowerInvariant()), 5f);
                seen.Errors = b.Errors;

                if (job == null) { seen.Part = null; seen.Fits = -1; seen.Done = false; continue; }
                if (seen.Part != job.PartId)
                {
                    // 剛認出配方：右側開始組
                    seen.Part = job.PartId;
                    seen.Fits = job.Fits == 0 ? 0 : -1;
                    seen.Done = job.Done;
                    if (job.Fits == 0 && !job.Done)
                    {
                        Sfx.Confirm();
                        UIRoot.Toast(Loc.T("ui.asm.recognised", ("code", D.Part(job.PartId)?.Code ?? ""), ("name", SelectView.PartName(D.Part(job.PartId)))), 4f);
                        Hud.Signal("recognised");
                    }
                }
                var part = D.Part(job.PartId);
                bool fitted = seen.Fits >= 0 && job.Fits > seen.Fits;
                seen.Fits = job.Fits;

                if (job.Done && !seen.Done)
                {
                    seen.Done = true;
                    bool flawless = job.Mistakes == 0;
                    Codex.Unlock(D, job.PartId, true);
                    if (flawless) Codex.Master(D, job.PartId);
                    Sfx.Confirm();
                    UIRoot.Toast(Loc.T(flawless ? "ui.asm.built_flawless" : "ui.asm.built", ("code", part?.Code ?? "")), 5f);
                    Hud.Signal("assemble");
                    continue;
                }
                if (!fitted) continue;
                var comp = D.Component(job.LastComp);
                if (comp == null) continue;
                bool mine = job.LastBy == GameSession.LocalPlayer;
                if (job.LastOk)
                {
                    // 裝對了：把名稱和功能再說一次（這時候看最記得住）
                    if (mine) UIRoot.Toast(Loc.T("ui.asm.right", ("name", Loc.T(comp.Name)), ("role", Loc.T(comp.Role))), 3.5f);
                }
                else if (mine)
                {
                    bool belongs = D.Recipe(part)?.Components.Exists(c => c.Id == comp.Id) == true;
                    UIRoot.Toast(Loc.T(belongs ? "ui.asm.wrong_order" : "ui.asm.wrong", ("name", Loc.T(comp.Name)), ("role", Loc.T(comp.Role))), 6f);
                }
                else UIRoot.Toast(Loc.T("ui.asm.mate_wrong", ("who", SelectView.PlayerName(job.LastBy))), 4f);
            }
        }
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
            LobbyFlow();
            _panel?.Refresh();
            _opsBar.Refresh();
            _poll?.Refresh();
            _screens?.MarkDirty();
            UpdatePhaseBar();
            Notices();
            CollectCards();
            BenchNotices();
            _orderSig = null;
        }

        // 發射失敗重來的提示要跨場景（失敗後從發射場景回來才看得到），所以記在靜態欄位，換一局就重算
        static object _noticeMission;
        static int _restartsShown;
        int _confirmsSeen = -1;

        /// <summary>狀態變化的提示：任務重來；有人在大屏幕上確定了設計（該去組裝了）。</summary>
        void Notices()
        {
            var s = M.S;
            if (!ReferenceEquals(_noticeMission, M)) { _noticeMission = M; _restartsShown = s.Restarts; }
            if (s.Restarts > _restartsShown && s.Phase == SessionPhase.Design)
            {
                _restartsShown = s.Restarts;
                Sfx.NoGo();
                UIRoot.Toast(Loc.T("ui.restart.toast"), 6f);
            }
            if (_confirmsSeen >= 0 && s.Confirms > _confirmsSeen && s.Confirmed && s.Phase == SessionPhase.Design && !s.IsSandbox(D))
            {
                Sfx.Confirm();
                UIRoot.Toast(Loc.T("ui.sel.confirmed_toast"), 5f);
            }
            _confirmsSeen = s.Confirms;
        }

        /// <summary>簡報、大廳：階段一開始就打開的面板。</summary>
        void ShowPhasePanel()
        {
            // 大廳不自動擋畫面：玩家在大廳裡走動，按兩邊的按鈕準備（場景裡的互動物件 LobbyButton）
            if (M.S.Phase == SessionPhase.Briefing) OpenPanel("briefing", "");
            else if (M.S.Phase == SessionPhase.Design && M.S.Multiplayer && !ReferenceEquals(_briefedFor, M))
            {
                // 多人開始任務後：每個人各自看一次簡報（和隊友無關，不用等人）
                _briefedFor = M;
                OpenPanel("briefing", "");
            }
        }

        /// <summary>大廳：全員準備好 → 房主跳出開始視窗；有人取消準備 → 收起來。</summary>
        void LobbyFlow()
        {
            var s = M.S;
            bool all = s.Phase == SessionPhase.Lobby && LobbyView.AllReady(s);
            if (!all)
            {
                if (_panelId == "lobby_start") ClosePanel();
                _startDismissed = false;
                return;
            }
            if (GameSession.IsHost && _panel == null && !_startDismissed && !Hud.Paused) OpenPanel("lobby_start", "");
        }

        string LobbyHint()
        {
            var s = M.S;
            var me = s.Player(GameSession.LocalPlayer);
            if (LobbyView.Alone(s)) return Loc.T("ui.lobby.hint_alone");
            if (LobbyView.AllReady(s)) return Loc.T(GameSession.IsHost ? "ui.lobby.hint_host" : "ui.lobby.hint_wait");
            return Loc.T(me != null && me.Ready ? "ui.lobby.hint_ready" : "ui.lobby.hint");
        }

        void UpdatePhaseBar()
        {
            _phaseBar.Clear();
            var s = M.S;
            if (s.Phase == SessionPhase.Design)
            {
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
            if (_panel.Closable) Ui.Btn("Esc  ✕", ClosePanel, "modal-close").AddTo(head);
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
            if (_panelId == "lobby_start") _startDismissed = true;
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

            // F9：小遊戲除錯選單（直接跳進任一個營運小遊戲；只在 Editor 和 Development Build）
            if (keys && UnityEngine.Debug.isDebugBuild && kb.f9Key.wasPressedThisFrame && (_panel == null || _panelId == "minigame_debug"))
            {
                if (_panel == null) OpenPanel("minigame_debug", ""); else ClosePanel();
                return;
            }
            // G / N：Go/No-Go 點名
            if (keys && _poll != null && _poll.CanVote && (kb.gKey.wasPressedThisFrame || kb.nKey.wasPressedThisFrame))
                _poll.Vote(kb.gKey.wasPressedThisFrame);
            // 大廳：全員準備好之後，房主按 Enter 直接開始
            else if (keys && _panel == null && M.S.Phase == SessionPhase.Lobby && GameSession.IsHost && LobbyView.AllReady(M.S)
                     && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
            {
                Sfx.Go();
                GameSession.Send(CommandType.StartMission);
            }
            // B：任務簡報（隨時複習；開著就關掉）
            else if (keys && kb.bKey.wasPressedThisFrame && M.S.Phase != SessionPhase.Lobby && M.S.Phase != SessionPhase.Launch)
            {
                if (_panelId == "briefing") { if (_panel.Closable) ClosePanel(); }
                else if (_panel == null) OpenPanel("briefing", "");
            }
            // M：自己的麥克風靜音
            else if (keys && kb.mKey.wasPressedThisFrame && Multiplayer.VoiceConnected)
                ToggleMic();
            // T：個人模式的平板（儀表板、任務需求、組裝清單）
            else if (keys && kb.tKey.wasPressedThisFrame && !M.S.Multiplayer)
            {
                if (_panelId == "tablet") ClosePanel();
                else if (_panel == null) OpenPanel("tablet", "");
            }
            // K：知識卡圖鑑；L：設計圖庫（儲存、分享、比較）。開著就關掉
            else if (keys && kb.kKey.wasPressedThisFrame && M.S.Phase != SessionPhase.Launch)
            {
                if (_panelId == "codex") ClosePanel();
                else if (_panel == null) OpenPanel("codex", "");
            }
            else if (keys && kb.lKey.wasPressedThisFrame && M.S.Phase != SessionPhase.Launch && M.S.Phase != SessionPhase.Lobby)
            {
                if (_panel == null) OpenPanel("designs", "");
            }
            // 在面板上按 E 也可以關（同一個鍵開關）
            else if (keys && kb.eKey.wasPressedThisFrame && _panel != null && _panel.Closable && _panel.CloseWithE && _openedFrame != Time.frameCount)
                ClosePanel();

            _briefHint.style.display = (M.S.Phase == SessionPhase.Design || M.S.Phase == SessionPhase.Ops) && _panel == null && !Hud.Paused
                ? DisplayStyle.Flex : DisplayStyle.None;
            _panel?.Tick(dt);
            _opsBar.Tick(dt);
            _poll?.Tick(dt);
            _coach?.Tick(dt, _panel == null && !Hud.Paused);

            bool playing = _panel == null && !Hud.Paused;
            // 組裝單：人走到某一座組裝台旁邊才顯示那一座的（中控室的人不需要看）
            int near = Hud.NearBench;
            var nb = M.S.BenchAt(near);
            bool showOrder = playing && nb != null && M.S.Phase == SessionPhase.Design && !M.S.IsSandbox(D);
            _order.style.display = showOrder ? DisplayStyle.Flex : DisplayStyle.None;
            if (showOrder)
            {
                var job = nb.Job;
                string sig = near + "/" + string.Join(",", nb.Left) + "/" + (job == null ? "" : job.PartId + "/" + job.Step + "/" + job.Done + "/" + job.Mistakes)
                             + "/" + M.S.Confirmed + "/" + M.S.Revision + "/" + Loc.Lang;
                if (sig != _orderSig)
                {
                    _orderSig = sig;
                    _order.Clear();
                    BenchOrderView.Build(_order, near, false);
                }
            }
            // 兩種視角都靠準星選目標（第三人稱的鏡頭比頭頂高，準星不會被角色擋住）
            _crosshair.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            // 對到能互動的東西：準星變成一個圈
            _crosshair.EnableInClassList("crosshair--on", Hud.Target != null && Hud.Target.Keys.Count > 0);
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

            // 區域名：換區域時顯示 2.5 秒再淡出（像開放世界遊戲進入新地區）
            if (Hud.Area != _shownArea)
            {
                _shownArea = Hud.Area;
                _areaTime = string.IsNullOrEmpty(Hud.Area) ? 0f : 3f;
                _area.text = string.IsNullOrEmpty(Hud.Area) ? "" : Loc.T("ui.area." + Hud.Area);
            }
            _areaTime = Mathf.Max(0, _areaTime - dt);
            _area.style.opacity = Mathf.Clamp01(_areaTime / 0.5f);
            var held = M.S.HeldBy(GameSession.LocalPlayer);
            // 手上是小零件：列出名稱（單人的籃子最多四個，寫幾個／幾個）
            var comps = held == null ? M.S.CompsOf(GameSession.LocalPlayer) : null;
            bool lobby = M.S.Phase == SessionPhase.Lobby;
            string compNames = comps != null && comps.Count > 0 ? string.Join(Loc.IsZh ? "、" : ", ", comps.ConvertAll(id => Loc.T(D.Component(id)?.Name))) : null;
            _held.text = held != null ? Loc.T("ui.hud.holding", ("code", D.Part(held)?.Code ?? ""))
                : compNames != null ? (M.S.Alone() ? Loc.T("ui.hud.basket", ("list", compNames), ("n", comps.Count), ("max", GameData.BasketSize))
                    : Loc.T("ui.hud.holding", ("code", compNames)))
                : lobby ? LobbyHint() : "";
            _held.style.display = (held != null || compNames != null || lobby) && playing ? DisplayStyle.Flex : DisplayStyle.None;

            DrawLabels();
        }

        int _openedFrame;
        string _keysSig;
        string _shownArea;
        float _areaTime;

        /// <summary>
        /// 資訊框位置：有錨點（第三人稱）就置中放在物件上方，不蓋住物件、不會跑出畫面；沒有就用 USS 的位置（準星右下）。
        /// </summary>
        const float InfoTop = 64f;

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
            _info.style.left = Mathf.Clamp(sp.x - box.x / 2f, 16f, Mathf.Max(16f, size.x - box.x - 16f));
            // 上緣留給左上的階段、右上的「B 任務說明」鍵帽：俯視鏡頭時目標在畫面頂端，資訊框不要蓋上去
            _info.style.top = Mathf.Clamp(sp.y - box.y - 8f, InfoTop, Mathf.Max(InfoTop, size.y - box.y - 16f));
            _info.style.translate = new Translate(0, 0);
        }

        static void ToggleMic() => Multiplayer.SetMicMuted?.Invoke(!Multiplayer.MicMuted);

        /// <summary>控制台名稱（tag）只在角色走到這麼近時才顯示；看板（sign）、零件編號照各自的 maxDistance。</summary>
        const float TagNear = 3f;
        /// <summary>看板（整合台、小零件架、發射台這些機台的名稱）：走到這麼近才出現，不會隔著半個房間一直掛在畫面上。</summary>
        const float SignNear = 5f;
        /// <summary>架上、組裝台上小零件的名稱：離鏡頭這麼近才出現；<see cref="CompFull"/> 公尺內是原本的大小，再遠就縮小。</summary>
        const float CompNear = 3.4f;
        const float CompFull = 1.7f;

        /// <summary>
        /// 場景裡的文字標籤：投影到畫面上（太遠、在背後、被牆擋住的不畫）。
        /// 機台、看板的名稱都不常駐：走到旁邊才出現（名牌 <see cref="TagNear"/>、看板 <see cref="SignNear"/> 公尺內），
        /// 正對著的那一個已經有提示框，就不重複。架上小零件的名稱只在 <see cref="CompNear"/> 公尺內出現，
        /// 字跟著距離縮小（遠一點的不會疊在一起）。
        /// </summary>
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
                float k = 1f;
                if (visible && (wl.style == "tag" || wl.style == "sign"))
                {
                    var me = PoseFeed.Local.Position;
                    float near = Vector2.Distance(new Vector2(me.x, me.z), new Vector2(wp.x, wp.z));
                    visible = near < (wl.style == "tag" ? TagNear : SignNear) && (Hud.Target == null || Hud.Target.Title != wl.Resolve());
                }
                else if (visible && wl.style == "comp")
                {
                    // 小零件的名稱：近的才顯示，越遠字越小；準星對到的那一格看提示框就好
                    visible = dist < CompNear && (Hud.Target == null || Hud.Target.Title != wl.Resolve());
                    k = Mathf.Clamp(CompFull / Mathf.Max(0.1f, dist), 0.62f, 1f);
                }
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
                el.style.scale = new Scale(new Vector3(k, k, 1f));
                el.style.opacity = wl.style == "comp" ? Mathf.Clamp01((CompNear - dist) / 0.6f) : Mathf.Clamp01((wl.maxDistance - dist) / 2f);
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
