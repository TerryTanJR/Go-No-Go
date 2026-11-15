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
    /// 以及按 E 打開的各種面板（設計主螢幕選件、線索、組裝區儀表板、發射、營運席…）。面板開著時角色不能動、滑鼠放開。
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
        readonly Dictionary<WorldLabel, LabelView> _labelEls = new Dictionary<WorldLabel, LabelView>();
        SessionPhase _lastPhase;
        OpsBar _opsBar;
        readonly EmoteWheel _emotes = new EmoteWheel();
        TutorialCoach _coach;
        WorldScreens _screens;
        PollBanner _poll;
        VisualElement _briefHint;
        /// <summary>組裝單（右上）：人走到某一座組裝台旁邊時顯示那一座的。</summary>
        VisualElement _order;
        /// <summary>組裝單要重畫：這一局的狀態或語言變了。走到另一座組裝台旁邊（<see cref="_orderBench"/> 不同）也會重畫。</summary>
        bool _orderDirty;
        int _orderBench = -1;
        /// <summary>這一局本機已經自動打開過簡報（多人）；回到場景不再重開，要看按 B。</summary>
        static object _briefedFor;
        /// <summary>房主把「全員就緒」的開始視窗關掉了：準備狀態改變前不再自動跳出來。</summary>
        bool _startDismissed;

        protected override void Build()
        {
            _labelEls.Clear();
            _labelsHidden = false;
            _shownArea = null;
            // 整個畫面重建（切語言）：每幀比對用的快取全部作廢
            _keysShown.Clear();
            _infoColor = null;
            _micShown = null;
            _heldDirty = true;
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
            _orderDirty = true;

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

            // 營運橫條的狀態（這回合剩幾秒、警報響過沒）留在同一個物件裡：切語言重建畫面時倒數不會重來
            _opsBar ??= new OpsBar();
            _opsBar.Attach(_hud);
            _emotes.Attach(_hud);

            _modal = Ui.El("modal-layer").AddTo(Root);
            _modal.style.display = DisplayStyle.None;

            // 點名橫幅放在面板層上面：開著面板也看得到、按得到
            _poll = new PollBanner(Root);

            // 切語言重建畫面：先放掉舊的引導（不然它還訂著事件，步驟完成的音效會響兩次）
            _coach?.Dispose();
            _coach = null;
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
            else if (_panel != null) Mount(_panel, _panelId, true);   // 切語言：開著的面板留著（玩到一半的小遊戲不會重來）
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
            _orderDirty = true;
            _heldDirty = true;
        }

        // 發射失敗重來的提示要跨場景（失敗後從發射場景回來才看得到），所以記在靜態欄位，換一局就重算
        static object _noticeMission;
        static int _restartsShown;
        int _confirmsSeen = -1;

        /// <summary>狀態變化的提示：任務重來；有人在設計主螢幕上確定了設計（該去組裝了）。</summary>
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
            // 大廳不自動擋畫面：玩家在大廳裡走動，到大廳中央按準備按鈕（場景裡的互動物件 LobbyButton）
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
            // 大廳：房間碼放在左上角（牆上的大螢幕一進門會被中央的地球擋住）
            if (s.Phase == SessionPhase.Lobby) LobbyView.RoomCode(_phaseBar);
            else if (s.Phase == SessionPhase.Design)
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
            var panel = FacPanel.Create(id);
            if (panel == null) return;
            Mount(panel, id, false);
            _openedFrame = Time.frameCount;
        }

        /// <summary>把面板放進面板層。reattach = 切語言後把原本開著的那一個接回來（不是新開）。</summary>
        void Mount(FacPanel panel, string id, bool reattach)
        {
            _panel = panel;
            _panelId = id;
            _modal.Clear();
            _modal.style.display = DisplayStyle.Flex;
            var card = Ui.El("modal", _panel.WideClass).AddTo(_modal);
            var head = Ui.Row("modal-head").AddTo(card);
            Ui.Text(_panel.Title, "eyebrow").AddTo(head);
            Ui.Spacer().AddTo(head);
            if (_panel.Closable) Ui.Btn("Esc  ×", ClosePanel, "modal-close").AddTo(head);
            var body = Ui.El("modal-body").AddTo(card);
            if (reattach) _panel.Reattach(body, ClosePanel);
            else _panel.Attach(body, ClosePanel);
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
            else if (keys && _panel == null && M.S.Phase == SessionPhase.Lobby && GameSession.IsHost
                     && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) && LobbyView.AllReady(M.S))
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
            _emotes.Tick(keys && _panel == null && !Hud.InputBlocked && M.S.Phase != SessionPhase.Launch);
            _poll?.Tick(dt);
            _coach?.Tick(dt, _panel == null && !Hud.Paused);

            bool playing = _panel == null && !Hud.Paused;
            // 組裝單：人走到某一座組裝台旁邊才顯示那一座的（中控室的人不需要看）
            int near = Hud.NearBench;
            var nb = M.S.BenchAt(near);
            bool showOrder = playing && nb != null && M.S.Phase == SessionPhase.Design && !M.S.IsSandbox(D);
            _order.style.display = showOrder ? DisplayStyle.Flex : DisplayStyle.None;
            // 內容只跟「哪一座」、這一局的狀態和語言有關：狀態一變 OnChanged 就標記重畫，不用每幀組字串比對
            if (showOrder && (_orderDirty || near != _orderBench))
            {
                _orderDirty = false;
                _orderBench = near;
                _order.Clear();
                BenchOrderView.Build(_order, near, false);
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
                // 邊框顏色：顏色字串和上一幀一樣就不重新解析
                string color = t.Color ?? "";
                if (color != _infoColor)
                {
                    _infoColor = color;
                    if (color.Length > 0 && ColorUtility.TryParseHtmlString(color, out var c)) _info.style.borderLeftColor = c;
                    else _info.style.borderLeftColor = new Color(0.22f, 0.78f, 0.96f);
                }
                if (!SameKeys(t.Keys))
                {
                    _keysShown.Clear();
                    _keysShown.AddRange(t.Keys);
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
            // 文字和顏色只跟有沒有靜音有關：變了才重組
            bool muted = Multiplayer.MicMuted;
            if (_micShown != muted)
            {
                _micShown = muted;
                _mic.text = Loc.T(muted ? "ui.voice.muted" : "ui.voice.on") + "  [M]";
                _mic.EnableInClassList("btn-danger", muted);
                _mic.EnableInClassList("btn-ghost", !muted);
            }

            // 區域名：換區域時顯示 2.5 秒再淡出（像開放世界遊戲進入新地區）
            if (Hud.Area != _shownArea)
            {
                _shownArea = Hud.Area;
                _areaTime = string.IsNullOrEmpty(Hud.Area) ? 0f : 3f;
                _area.text = string.IsNullOrEmpty(Hud.Area) ? "" : Loc.T("ui.area." + Hud.Area);
            }
            _areaTime = Mathf.Max(0, _areaTime - dt);
            _area.style.opacity = Mathf.Clamp01(_areaTime / 0.5f);
            // 下方中央那一行（手上的東西、大廳的提示）只跟這一局的狀態、自己是誰、是不是房主有關：變了才重組文字
            int me = GameSession.LocalPlayer;
            bool host = GameSession.IsHost;
            if (_heldDirty || me != _heldPlayer || host != _heldHost)
            {
                _heldDirty = false;
                _heldPlayer = me;
                _heldHost = host;
                var held = M.S.HeldBy(me);
                // 手上是小零件：列出名稱（單人的籃子有容量上限 GameData.BasketSize，寫幾個／幾個）
                var comps = held == null ? M.S.CompsOf(me) : null;
                bool lobby = M.S.Phase == SessionPhase.Lobby;
                string compNames = comps != null && comps.Count > 0 ? string.Join(Loc.IsZh ? "、" : ", ", comps.ConvertAll(id => Loc.T(D.Component(id)?.Name))) : null;
                _held.text = held != null ? Loc.T("ui.hud.holding", ("code", D.Part(held)?.Code ?? ""))
                    : compNames != null ? (M.S.Alone() ? Loc.T("ui.hud.basket", ("list", compNames), ("n", comps.Count), ("max", GameData.BasketSize))
                        : Loc.T("ui.hud.holding", ("code", compNames)))
                    : lobby ? LobbyHint() : "";
                _heldAny = held != null || compNames != null || lobby;
            }
            _held.style.display = _heldAny && playing ? DisplayStyle.Flex : DisplayStyle.None;

            DrawLabels();
        }

        int _openedFrame;
        string _shownArea;
        float _areaTime;
        /// <summary>資訊框上現在畫著的按鍵提示、邊框顏色字串：和準星對到的一樣就不重畫。</summary>
        readonly List<KeyHint> _keysShown = new List<KeyHint>();
        string _infoColor;
        /// <summary>麥克風按鈕現在畫的是哪個狀態（null = 還沒畫過）。</summary>
        bool? _micShown;
        /// <summary>下方中央那一行要重組（狀態或語言變了）；另外記著上次是替哪個玩家、是不是房主時組的（換了也重組）。</summary>
        bool _heldDirty = true, _heldAny, _heldHost;
        int _heldPlayer;

        bool SameKeys(List<KeyHint> keys)
        {
            if (keys.Count != _keysShown.Count) return false;
            for (int i = 0; i < keys.Count; i++)
                if (keys[i].Key != _keysShown[i].Key || keys[i].Label != _keysShown[i].Label) return false;
            return true;
        }

        static void ToggleMic() => Multiplayer.SetMicMuted?.Invoke(!Multiplayer.MicMuted);

        /// <summary>控制台、玩家的名牌（tag）：角色走到這麼近才顯示。零件編號這類其他樣式照各自的 maxDistance。</summary>
        const float TagNear = 3f;
        /// <summary>看板（整合台、小零件架、發射台這些機台的名稱）：走到這麼近才出現，不會隔著半個房間一直掛在畫面上。</summary>
        const float SignNear = 5f;
        /// <summary>架上、組裝台上小零件的名稱：離鏡頭這麼近才出現；<see cref="CompFull"/> 公尺內是原本的大小，再遠就縮小。</summary>
        const float CompNear = 3.4f;
        const float CompFull = 1.7f;

        /// <summary>一個場景標籤在畫面上的元素。</summary>
        sealed class LabelView
        {
            public Label El;
            /// <summary>上一次套用的顏色字串（沒變就不重新解析）。</summary>
            public string ColorKey;
            /// <summary>最後一次在 <see cref="WorldLabel.All"/> 裡看到它是第幾輪；沒跟上最新一輪的就是已經不在場景裡的。</summary>
            public int Pass;
        }

        readonly List<WorldLabel> _staleLabels = new List<WorldLabel>();
        int _labelPass;
        /// <summary>面板開著、標籤已經全部收起來了（不用每幀再收一次）。</summary>
        bool _labelsHidden;

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
            // 面板開著時標籤一律不顯示：收起來一次就好，位置和遮擋等面板關掉的那一幀再算
            if (_panel != null)
            {
                if (_labelsHidden) return;
                _labelsHidden = true;
                foreach (var shown in _labelEls.Values) shown.El.style.display = DisplayStyle.None;
                return;
            }
            _labelsHidden = false;
            var camPos = cam.transform.position;
            var me = PoseFeed.Local.Position;
            var target = Hud.Target;
            int pass = ++_labelPass, live = 0;
            foreach (var wl in WorldLabel.All)
            {
                if (wl == null || !wl.isActiveAndEnabled) continue;
                var wp = wl.WorldPos;
                var v = cam.WorldToViewportPoint(wp);
                float dist = Vector3.Distance(camPos, wp);
                bool visible = v.z > 0.1f && v.x > -0.05f && v.x < 1.05f && v.y > -0.05f && v.y < 1.05f && dist < wl.maxDistance;
                float k = 1f;
                string text = null;
                if (visible && (wl.style == "tag" || wl.style == "sign"))
                {
                    float near = Vector2.Distance(new Vector2(me.x, me.z), new Vector2(wp.x, wp.z));
                    visible = near < (wl.style == "tag" ? TagNear : SignNear);
                    if (visible && target != null) { text = wl.Resolve(); visible = target.Title != text; }
                }
                else if (visible && wl.style == "comp")
                {
                    // 小零件的名稱：近的才顯示，越遠字越小；準星對到的那一格看提示框就好
                    visible = dist < CompNear;
                    if (visible && target != null) { text = wl.Resolve(); visible = target.Title != text; }
                    k = Mathf.Clamp(CompFull / Mathf.Max(0.1f, dist), 0.62f, 1f);
                }
                if (visible && Physics.Linecast(camPos, wp, out var hit, ~(1 << 2), QueryTriggerInteraction.Ignore)
                    && hit.distance < dist - 0.4f && hit.collider.GetComponentInParent<WorldLabel>() != wl)
                    visible = false;
                if (!_labelEls.TryGetValue(wl, out var view))
                {
                    view = new LabelView { El = Ui.Text("", "world-label", "world-label--" + wl.style).AddTo(_labels) };
                    view.El.pickingMode = PickingMode.Ignore;
                    _labelEls[wl] = view;
                }
                if (view.Pass != pass) { view.Pass = pass; live++; }
                var el = view.El;
                el.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible) continue;
                el.text = text ?? wl.Resolve();
                if (wl.color != view.ColorKey)
                {
                    view.ColorKey = wl.color;
                    if (!string.IsNullOrEmpty(wl.color) && ColorUtility.TryParseHtmlString(wl.color, out var c)) el.style.borderBottomColor = c;
                }
                var sp = RuntimePanelUtils.CameraTransformWorldToPanel(panel, wp, cam);
                el.style.left = sp.x;
                el.style.top = sp.y;
                el.style.scale = new Scale(new Vector3(k, k, 1f));
                el.style.opacity = wl.style == "comp" ? Mathf.Clamp01((CompNear - dist) / 0.6f) : Mathf.Clamp01((wl.maxDistance - dist) / 2f);
            }
            // 每個元素這一輪都看到了 = 沒有標籤從場景裡消失，不用清
            if (live == _labelEls.Count) return;
            _staleLabels.Clear();
            foreach (var kv in _labelEls) if (kv.Value.Pass != pass) _staleLabels.Add(kv.Key);
            foreach (var gone in _staleLabels) { _labelEls[gone].El.RemoveFromHierarchy(); _labelEls.Remove(gone); }
            _staleLabels.Clear();
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
