using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 小遊戲畫布：把 Core 遊戲輸出的形狀（<see cref="MiniShape"/>，800 × 450 的虛擬座標）用 Painter2D 畫出來，
    /// 文字用 Label（會跟著縮放）。同時收集滑鼠輸入（轉成虛擬座標）。
    /// </summary>
    public sealed class MiniCanvas : VisualElement
    {
        readonly List<MiniShape> _shapes = new List<MiniShape>();
        readonly List<Label> _labels = new List<Label>();
        readonly Label _banner;
        bool _pressed;

        /// <summary>滑鼠在虛擬畫布上的位置、是否按住。</summary>
        public Vector2 Pointer { get; private set; } = new Vector2(MiniField.W / 2, MiniField.H / 2);
        public bool Down { get; private set; }

        public MiniCanvas()
        {
            AddToClassList("mini-canvas");
            style.overflow = Overflow.Hidden;
            generateVisualContent += Generate;
            RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float w = resolvedStyle.width;
                if (w > 1 && Mathf.Abs(resolvedStyle.height - w * MiniField.H / MiniField.W) > 1f) style.height = w * MiniField.H / MiniField.W;
            });
            RegisterCallback<PointerMoveEvent>(e => Pointer = ToField(e.localPosition));
            RegisterCallback<PointerDownEvent>(e =>
            {
                Pointer = ToField(e.localPosition);
                Down = true;
                _pressed = true;
            });
            RegisterCallback<PointerUpEvent>(e => { Pointer = ToField(e.localPosition); Down = false; });
            RegisterCallback<PointerLeaveEvent>(_ => Down = false);
            // 畫布中央的大字（「準備好了嗎」「暫停」之類）
            _banner = Ui.Text("", "mini-banner");
            _banner.pickingMode = PickingMode.Ignore;
            _banner.style.display = DisplayStyle.None;
            Add(_banner);
        }

        float Scale => Mathf.Max(0.01f, Mathf.Min(contentRect.width / MiniField.W, contentRect.height / MiniField.H));
        Vector2 Offset => new Vector2((contentRect.width - MiniField.W * Scale) / 2, (contentRect.height - MiniField.H * Scale) / 2);

        Vector2 ToField(Vector2 local)
        {
            float s = Scale;
            var o = Offset;
            return new Vector2(Mathf.Clamp((local.x - o.x) / s, 0, MiniField.W), Mathf.Clamp((local.y - o.y) / s, 0, MiniField.H));
        }

        /// <summary>取走「這一格剛按下」的訊號（只會是 true 一次）。</summary>
        public bool ConsumePressed()
        {
            bool p = _pressed;
            _pressed = false;
            return p;
        }

        public void SetBanner(string text)
        {
            _banner.text = text ?? "";
            _banner.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        /// <summary>換上這一格要畫的東西。</summary>
        public void Present(List<MiniShape> shapes)
        {
            _shapes.Clear();
            _shapes.AddRange(shapes);
            UpdateLabels();
            MarkDirtyRepaint();
        }

        static readonly Dictionary<string, Texture2D> Textures = new Dictionary<string, Texture2D>();

        /// <summary>Resources/Minigames/ 底下的貼圖（找不到會記住，不會每格重找）。</summary>
        public static Texture2D TextureOf(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!Textures.TryGetValue(key, out var t)) Textures[key] = t = Resources.Load<Texture2D>("Minigames/" + key);
            return t;
        }

        static Color Col(uint rgb, float alpha) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, Mathf.Clamp01(alpha));

        void UpdateLabels()
        {
            float s = Scale;
            var o = Offset;
            int n = 0;
            foreach (var sh in _shapes)
            {
                if (sh.Kind != MiniShapeKind.Text) continue;
                if (n >= _labels.Count)
                {
                    var l = Ui.Text("", "mini-label");
                    l.pickingMode = PickingMode.Ignore;
                    l.style.position = Position.Absolute;
                    l.style.whiteSpace = WhiteSpace.NoWrap;
                    l.style.translate = new Translate(Length.Percent(-50), Length.Percent(-50));
                    Insert(0, l); // 文字放在畫布底下的圖層上方即可；Banner 在最上面
                    _labels.Add(l);
                }
                var lab = _labels[n++];
                lab.style.display = DisplayStyle.Flex;
                var str = sh.Str ?? "";
                lab.text = str.Length > 1 && str[0] == '@' ? Loc.T(str.Substring(1)) : str; // "@鍵" = 字串鍵
                lab.style.left = o.x + sh.X * s;
                lab.style.top = o.y + sh.Y * s;
                lab.style.fontSize = Mathf.Max(6f, sh.A * s);
                lab.style.color = Col(sh.Color, sh.Alpha <= 0 ? 1 : sh.Alpha);
            }
            for (int i = n; i < _labels.Count; i++) _labels[i].style.display = DisplayStyle.None;
        }

        void Generate(MeshGenerationContext mgc)
        {
            var p = mgc.painter2D;
            float s = Scale;
            var o = Offset;
            Vector2 P(float x, float y) => new Vector2(o.x + x * s, o.y + y * s);
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;
            foreach (var sh in _shapes)
            {
                float alpha = sh.Alpha <= 0 && sh.Kind != MiniShapeKind.Text ? 1 : sh.Alpha;
                var c = Col(sh.Color, alpha);
                switch (sh.Kind)
                {
                    case MiniShapeKind.Rect:
                        {
                            float hw = sh.A / 2, hh = sh.B / 2, cos = Mathf.Cos(sh.Rot), sin = Mathf.Sin(sh.Rot);
                            Vector2 Corner(float dx, float dy) => P(sh.X + dx * cos - dy * sin, sh.Y + dx * sin + dy * cos);
                            p.fillColor = c;
                            p.BeginPath();
                            p.MoveTo(Corner(-hw, -hh));
                            p.LineTo(Corner(hw, -hh));
                            p.LineTo(Corner(hw, hh));
                            p.LineTo(Corner(-hw, hh));
                            p.ClosePath();
                            p.Fill();
                            break;
                        }
                    case MiniShapeKind.Circle:
                        {
                            p.fillColor = c;
                            p.BeginPath();
                            float r = Mathf.Max(0.5f, sh.A * s);
                            if (r < 2f)
                            {
                                // 很小的圓用方塊代替（少很多頂點）
                                var q = P(sh.X, sh.Y);
                                p.MoveTo(new Vector2(q.x - r, q.y - r));
                                p.LineTo(new Vector2(q.x + r, q.y - r));
                                p.LineTo(new Vector2(q.x + r, q.y + r));
                                p.LineTo(new Vector2(q.x - r, q.y + r));
                                p.ClosePath();
                            }
                            else p.Arc(P(sh.X, sh.Y), r, Angle.Degrees(0), Angle.Degrees(360));
                            p.Fill();
                            break;
                        }
                    case MiniShapeKind.Ring:
                        p.strokeColor = c;
                        p.lineWidth = Mathf.Max(1f, sh.W * s);
                        p.BeginPath();
                        p.Arc(P(sh.X, sh.Y), Mathf.Max(0.5f, sh.A * s), Angle.Degrees(0), Angle.Degrees(360));
                        p.Stroke();
                        break;
                    case MiniShapeKind.Line:
                        p.strokeColor = c;
                        p.lineWidth = Mathf.Max(1f, sh.W * s);
                        p.BeginPath();
                        p.MoveTo(P(sh.X, sh.Y));
                        p.LineTo(P(sh.A, sh.B));
                        p.Stroke();
                        break;
                    case MiniShapeKind.Sprite:
                        {
                            var tex = TextureOf(sh.Str);
                            float hw = sh.A / 2, hh = sh.B / 2, cos = Mathf.Cos(sh.Rot), sin = Mathf.Sin(sh.Rot);
                            Vector2 Corner(float dx, float dy) => P(sh.X + dx * cos - dy * sin, sh.Y + dx * sin + dy * cos);
                            Vector2 tl = Corner(-hw, -hh), tr = Corner(hw, -hh), br = Corner(hw, hh), bl = Corner(-hw, hh);
                            if (tex == null)
                            {
                                // 缺圖：畫一個洋紅色的框，一眼就看得出來
                                p.strokeColor = new Color(1, 0, 1, 0.9f);
                                p.lineWidth = 2;
                                p.BeginPath(); p.MoveTo(tl); p.LineTo(tr); p.LineTo(br); p.LineTo(bl); p.ClosePath(); p.Stroke();
                                break;
                            }
                            var md = mgc.Allocate(4, 6, tex);
                            Color32 tint = Col(sh.Color, alpha);
                            md.SetNextVertex(new Vertex { position = new Vector3(tl.x, tl.y, Vertex.nearZ), uv = new Vector2(0, 1), tint = tint });
                            md.SetNextVertex(new Vertex { position = new Vector3(tr.x, tr.y, Vertex.nearZ), uv = new Vector2(1, 1), tint = tint });
                            md.SetNextVertex(new Vertex { position = new Vector3(br.x, br.y, Vertex.nearZ), uv = new Vector2(1, 0), tint = tint });
                            md.SetNextVertex(new Vertex { position = new Vector3(bl.x, bl.y, Vertex.nearZ), uv = new Vector2(0, 0), tint = tint });
                            md.SetAllIndices(new ushort[] { 0, 1, 2, 2, 3, 0 });
                            break;
                        }
                    case MiniShapeKind.Tri:
                        {
                            float cos = Mathf.Cos(sh.Rot), sin = Mathf.Sin(sh.Rot);
                            Vector2 V(float dx, float dy) => P(sh.X + dx * cos - dy * sin, sh.Y + dx * sin + dy * cos);
                            p.fillColor = c;
                            p.BeginPath();
                            p.MoveTo(V(0, -sh.A));
                            p.LineTo(V(sh.A * 0.866f, sh.A * 0.5f));
                            p.LineTo(V(-sh.A * 0.866f, sh.A * 0.5f));
                            p.ClosePath();
                            p.Fill();
                            break;
                        }
                }
            }
        }
    }

    /// <summary>
    /// 營運小遊戲面板：說明 → 玩 → 結果 → 送出成績。
    /// 事件模式（id = "minigame"）：玩的是「飛行指揮台判斷的事件」對應的遊戲，送出成績後回到飛行指揮台。
    /// 除錯模式（id = "minigame:遊戲id:嚴重度"）：直接玩任一個遊戲，不會送出成績，可以重玩、看自動駕駛示範。
    /// </summary>
    public sealed class MinigamePanel : FacPanel
    {
        enum Stage { Intro, Playing, Result }

        readonly MinigameDef _def;
        readonly bool _debug;
        readonly string _eventId;
        MinigameContext _ctx;
        int _severity;
        int _seed;
        int _tries;
        MiniGame _game;
        MiniCanvas _canvas;
        Stage _stage = Stage.Intro;
        bool _submitted, _auto;
        int _startFrame;
        readonly List<MiniShape> _shapes = new List<MiniShape>();
        readonly List<Label> _statLabels = new List<Label>();
        VisualElement _statRow;
        Label _caption;
        string _captionKey = "";

        /// <summary>事件模式。</summary>
        public MinigamePanel()
        {
            var e = M.CurrentEvent;
            if (e != null) _def = Minigames.For(M.ResponseTo(e.Id).Judged);
            _eventId = e?.Id;
            if (e != null && _def != null)
            {
                _severity = e.Severity;
                _ctx = MinigameContext.From(M.S.LastLaunch?.Design ?? M.S.Bench, D, e, M.S.Players.Count);
                _seed = unchecked((M.S.LastLaunch?.Seed ?? 1) * 31 + e.Turn * 7919 + GameSession.LocalPlayer * 104729);
            }
        }

        /// <summary>除錯模式。</summary>
        public MinigamePanel(string id, int severity)
        {
            _debug = true;
            _def = Minigames.Def(id);
            _severity = Mathf.Clamp(severity, 1, 3);
            _auto = MiniDebugOptions.Auto;
            _seed = 1000 + (int)(Time.realtimeSinceStartup * 1000) % 9000;
            Reroll();
        }

        void Reroll()
        {
            _ctx = new MinigameContext { Severity = _severity, Players = 1, Hull = _debug && MiniDebugOptions.Hull ? 2 : 0, Steady = 0.5, Margin = 0.5, RefId = RefFor(_def, _tries) };
        }

        static string RefFor(MinigameDef def, int tries)
        {
            if (def == null) return "";
            switch (def.Kind)
            {
                case EventKind.ScienceOpportunity: return new[] { "wildfire", "volcano", "flood", "hurricane" }[tries % 4];
                case EventKind.BigFire: return tries % 2 == 0 ? "forest" : "grassland";
                case EventKind.FalseFire: return tries % 2 == 0 ? "desert" : "glint";
                default: return "";
            }
        }

        public override string Title => _def != null ? Loc.T("mini.title", ("name", Loc.T("mini." + _def.Id + ".name"))) : Loc.T("mini.title", ("name", "—"));
        public override string WideClass => "modal--wide";
        public override bool Closable => _stage == Stage.Intro || _debug || (_stage == Stage.Result && _submitted);
        public override bool CloseWithE => false;

        /// <summary>玩的時候不要因為房主廣播的新狀態把畫布重建掉。</summary>
        public override void Refresh()
        {
            if (_stage == Stage.Playing) return;
            base.Refresh();
        }

        protected override void Build(VisualElement body)
        {
            if (_def == null)
            {
                Ui.Text(Loc.T("mini.none"), "body").AddTo(body);
                return;
            }
            if (!_debug && _stage != Stage.Playing && M.S.TurnResolved)
            {
                // 別人（或房主強制結算）已經讓這回合結算了
                Ui.Text(Loc.T("mini.resolved"), "body").AddTo(body);
                var r0 = Ui.Row("modal-actions").AddTo(body);
                Ui.Spacer().AddTo(r0);
                Ui.Btn(Loc.T("mini.to_desk"), () => { Close(); Hud.Open("ops_desk"); }, "btn-primary", "btn-big").AddTo(r0);
                return;
            }
            switch (_stage)
            {
                case Stage.Intro: BuildIntro(body); break;
                case Stage.Playing: BuildPlaying(body); break;
                default: BuildResult(body); break;
            }
        }

        // ---------------------------------------------------------------- 說明

        void BuildIntro(VisualElement body)
        {
            string id = _def.Id;
            Ui.Text(Loc.T("mini." + id + ".name"), "h2").AddTo(body);
            Ui.Text(Loc.T("mini.goal_label"), "eyebrow").AddTo(body);
            Ui.Text(Loc.T("mini." + id + ".goal"), "body").AddTo(body);
            Ui.Text(Loc.T("mini.controls_label"), "eyebrow", "mini-gap").AddTo(body);
            Ui.Text(Loc.T("mini." + id + ".controls"), "body").AddTo(body);
            var link = Ui.El("mini-link").AddTo(body);
            Ui.Text(Loc.T("mini.link_label"), "eyebrow").AddTo(link);
            Ui.Text(Loc.T("mini." + id + ".link"), "small", "dim").AddTo(link);

            // 多人規則
            int players = Mathf.Max(1, _debug ? 1 : M.S.Players.Count);
            string rule = _def.Rule == MiniRule.Sum
                ? Loc.T("mini.rule.sum", ("pct", _def.PassScore / 10))
                : Loc.T("mini.rule.ratio", ("need", Mathf.Max(1, Mathf.CeilToInt((float)(_def.RatioNeeded * players) - 1e-4f))), ("n", players));
            Ui.Text(players > 1 || _debug ? rule : Loc.T("mini.rule.solo"), "small", "faint").AddTo(body);
            if (!_debug) Ui.Text(Loc.T("mini.no_retry"), "small", "tight").AddTo(body);

            var actions = Ui.Row("modal-actions").AddTo(body);
            Ui.Btn(Loc.T(_debug ? "mini.debug.back" : "ui.gmenu.cancel"), () => { Close(); if (_debug) Hud.Open("minigame_debug"); else Hud.Open("ops_desk"); }, "btn-ghost").AddTo(actions);
            Ui.Spacer().AddTo(actions);
            Ui.Text("Enter", "keycap").AddTo(actions);
            Ui.Btn(Loc.T("mini.start"), StartGame, "btn-go", "btn-big").AddTo(actions);
        }

        void StartGame()
        {
            _game = Minigames.Create(_def.Id, _ctx, _seed);
            _stage = Stage.Playing;
            _startFrame = Time.frameCount;
            _submitted = false;
            Sfx.Go();
            Refresh2();
        }

        // Refresh() 在玩的時候被擋掉，所以開始遊戲時自己重建
        void Refresh2()
        {
            if (Body == null) return;
            Body.Clear();
            Build(Body);
        }

        // ---------------------------------------------------------------- 遊玩

        void BuildPlaying(VisualElement body)
        {
            _statRow = Ui.Row("mini-stats").AddTo(body);
            _statLabels.Clear();
            _caption = Ui.Text("", "mini-caption").AddTo(body);
            _canvas = new MiniCanvas().AddTo(body);
            var foot = Ui.Row("mini-foot").AddTo(body);
            Ui.Text(Loc.T("mini." + _def.Id + ".controls"), "small", "dim").AddTo(foot);
            Ui.Spacer().AddTo(foot);
            var give = Ui.Btn(Loc.T("mini.forfeit"), Forfeit, "btn-ghost").AddTo(foot);
            give.focusable = false; // 不然按空白鍵會「按到」它
            TickFrame(0); // 先畫第一格，不要空白一下
        }

        void Forfeit()
        {
            if (_stage != Stage.Playing || _game == null) return;
            Finish(forfeit: true);
        }

        public override void Tick(float dt)
        {
            if (_stage == Stage.Intro)
            {
                var kb0 = Keyboard.current;
                if (kb0 != null && !UIRoot.IsTyping && _def != null && (kb0.enterKey.wasPressedThisFrame || kb0.numpadEnterKey.wasPressedThisFrame)
                    && Time.frameCount != _startFrame && !(!_debug && M.S.TurnResolved))
                    StartGame();
                return;
            }
            if (_stage != Stage.Playing || _game == null) return;
            _canvas?.SetBanner(Hud.Paused ? Loc.T("mini.paused") : null);
            if (Hud.Paused) return;
            TickFrame(dt);
        }

        void TickFrame(float dt)
        {
            if (_game == null || _canvas == null) return;
            MiniInput inp;
            if (_auto && _debug) inp = _game.Autopilot(1f);
            else
            {
                inp = new MiniInput();
                var kb = Keyboard.current;
                if (kb != null && !UIRoot.IsTyping)
                {
                    inp.MoveX = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
                    inp.MoveY = (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1 : 0) - (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0);
                    inp.Fire = kb.spaceKey.isPressed;
                    // 剛用 Enter 開始遊戲的那一格不算「按下」
                    if (Time.frameCount != _startFrame)
                        inp.Action = kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame;
                    inp.Number = DigitPressed(kb);
                }
                inp.PX = _canvas.Pointer.x;
                inp.PY = _canvas.Pointer.y;
                inp.Down = _canvas.Down;
                inp.Pressed = _canvas.ConsumePressed();
            }
            _game.Step(dt, inp);
            _shapes.Clear();
            _game.Draw(_shapes);
            _canvas.Present(_shapes);
            UpdateStats();
            UpdateCaption();
            if (_game.State != MiniState.Running) Finish(false);
        }

        static int DigitPressed(Keyboard kb)
        {
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) return 1;
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) return 2;
            if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) return 3;
            if (kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame) return 4;
            if (kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame) return 5;
            if (kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame) return 6;
            if (kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame) return 7;
            if (kb.digit8Key.wasPressedThisFrame || kb.numpad8Key.wasPressedThisFrame) return 8;
            if (kb.digit9Key.wasPressedThisFrame || kb.numpad9Key.wasPressedThisFrame) return 9;
            return 0;
        }

        void UpdateCaption()
        {
            if (_caption == null) return;
            var c = _game.Caption;
            string key = c.Key ?? "";
            string sig = key + "|" + c.Tone;
            if (sig == _captionKey) return;
            _captionKey = sig;
            _caption.text = key == "" ? "" : Loc.T(key);
            _caption.style.display = key == "" ? DisplayStyle.None : DisplayStyle.Flex;
            _caption.EnableInClassList("mini-caption--good", c.Tone == 1);
            _caption.EnableInClassList("mini-caption--warn", c.Tone == 2);
            _caption.EnableInClassList("mini-caption--bad", c.Tone == 3);
        }

        void UpdateStats()
        {
            if (_statRow == null) return;
            int i = 0;
            foreach (var st in _game.Stats)
            {
                if (i >= _statLabels.Count)
                {
                    var l = Ui.Text("", "mini-stat", "mono").AddTo(_statRow);
                    _statLabels.Add(l);
                }
                string text = Loc.T(st.Key) + "  " + st.Value;
                if (_statLabels[i].text != text) _statLabels[i].text = text;
                i++;
            }
            for (; i < _statLabels.Count; i++) _statLabels[i].style.display = DisplayStyle.None;
        }

        // ---------------------------------------------------------------- 結果

        void Finish(bool forfeit)
        {
            if (_game == null) return;
            if (forfeit && _game.State == MiniState.Running) _game.Forfeit();
            _stage = Stage.Result;
            if (_game.Passed) Sfx.Go(); else Sfx.Alert();
            Refresh2();
        }

        void BuildResult(VisualElement body)
        {
            bool pass = _game.Passed;
            int score = _game.FinalScore;
            // 最後一格畫面留在上面（輸贏一眼看得出來）
            _canvas = new MiniCanvas().AddTo(body);
            _shapes.Clear();
            _game.Draw(_shapes);
            _canvas.Present(_shapes);
            _canvas.SetBanner(Loc.T(pass ? "mini.pass" : "mini.fail"));
            _canvas.EnableInClassList("mini-canvas--pass", pass);
            _canvas.EnableInClassList("mini-canvas--fail", !pass);

            var box = Ui.El("mini-result").AddTo(body);
            Ui.Text(Loc.T(pass ? "mini.pass" : "mini.fail"), "h2", pass ? "go" : "over").AddTo(box);
            Ui.Text(Loc.T("mini.score", ("v", score / 10), ("need", _def.PassScore / 10)), "body").AddTo(box);
            foreach (var st in _game.Stats) Ui.Text(Loc.T(st.Key) + "  " + st.Value, "small", "dim", "mono").AddTo(box);
            if (!_debug)
            {
                int players = M.S.Players.Count;
                Ui.Text(Loc.T(players > 1 ? "mini.team_note" : "mini.solo_note"), "small", "faint").AddTo(box);
                Ui.Text(Loc.T(pass ? "mini.pass_note" : "mini.fail_note"), "small", pass ? "go" : "tight").AddTo(box);
            }

            var actions = Ui.Row("modal-actions").AddTo(body);
            if (_debug)
            {
                Ui.Btn(Loc.T("mini.debug.back"), () => { Close(); Hud.Open("minigame_debug"); }, "btn-ghost").AddTo(actions);
                Ui.Spacer().AddTo(actions);
                Ui.Btn(Loc.T("mini.debug.again"), () =>
                {
                    _tries++;
                    _seed += 7;
                    _ctx.RefId = RefFor(_def, _tries);
                    StartGame();
                }, "btn-primary", "btn-big").AddTo(actions);
                return;
            }
            Ui.Spacer().AddTo(actions);
            Ui.Btn(Loc.T("mini.submit"), Submit, "btn-primary", "btn-big").AddTo(actions);
        }

        void Submit()
        {
            if (_submitted) return;
            _submitted = true;
            GameSession.Send(CommandType.MiniDone, _def.Id, _game.FinalScore);
            Sfx.Confirm();
            Hud.Signal("end_turn"); // 新手教學：判斷並處置完這一回合
            Close();
            Hud.Open("ops_desk");
        }
    }

    /// <summary>除錯選單的選項（只有除錯選單看得到；正式遊戲的嚴重度由事件決定，玩家不能選）。</summary>
    public static class MiniDebugOptions
    {
        public static int Severity = 2;
        public static bool Hull, Auto;
    }

    /// <summary>除錯選單（F9，只在 Editor 和 Development Build）：列出所有小遊戲，直接跳進去玩。</summary>
    public sealed class MinigameDebugPanel : FacPanel
    {
        public override string Title => Loc.T("mini.debug.title");
        public override string WideClass => "modal--wide";

        protected override void Build(VisualElement body)
        {
            Ui.Text(Loc.T("mini.debug.hint"), "small", "dim").AddTo(body);
            var row = Ui.Row("judge-row").AddTo(body);
            Ui.Text(Loc.T("mini.debug.severity"), "small", "dim").AddTo(row);
            for (int s = 1; s <= 3; s++)
            {
                int sv = s;
                Ui.Btn(sv.ToString(), () => { MiniDebugOptions.Severity = sv; Refresh(); }, "chip").Cls("chip--on", MiniDebugOptions.Severity == sv).AddTo(row);
            }
            Ui.Btn(Loc.T("mini.debug.hull"), () => { MiniDebugOptions.Hull = !MiniDebugOptions.Hull; Refresh(); }, "chip").Cls("chip--on", MiniDebugOptions.Hull).AddTo(row);
            Ui.Btn(Loc.T("mini.debug.auto"), () => { MiniDebugOptions.Auto = !MiniDebugOptions.Auto; Refresh(); }, "chip").Cls("chip--on", MiniDebugOptions.Auto).AddTo(row);
            var list = Ui.El("mini-debug-list").AddTo(body);
            foreach (var def in Minigames.All)
            {
                var d = def;
                var r = Ui.Row("mini-debug-row").AddTo(list);
                var col = Ui.El().AddTo(r);
                col.style.flexGrow = 1;
                Ui.Text(Loc.T("mini." + d.Id + ".name"), "h3").AddTo(col);
                Ui.Text(Loc.T("event.title." + OpsEvents.Key(d.Kind)) + " · " + Loc.T(d.Rule == MiniRule.Sum ? "mini.debug.rule_sum" : "mini.debug.rule_ratio"), "small", "dim").AddTo(col);
                Ui.Btn(Loc.T("mini.debug.play"), () => Hud.Open("minigame:" + d.Id + ":" + MiniDebugOptions.Severity), "btn-primary").AddTo(r);
            }
            var actions = Ui.Row("modal-actions").AddTo(body);
            Ui.Spacer().AddTo(actions);
            Ui.Btn(Loc.T("ui.fac.close"), Close, "btn-ghost").AddTo(actions);
        }
    }
}
