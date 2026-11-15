using System;
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
    /// 新手訓練：左上角的任務追蹤，平常只有一行「3/12 放大看一則線索」；換步驟時展開說明 8 秒，按 H 可以再展開或收起。
    /// 目標物件上方有標記和距離（不在畫面裡時貼在畫面邊緣），不必靠文字找位置。
    /// 做到了（收到 <see cref="Hud.Signal"/>）就打勾、換下一步。步驟跨場景保留（發射畫面回來後接著做營運的步驟）。
    /// <para>
    /// guide 模式（學員難度的一般局）：同一個框和標記，但不照固定步驟打勾，每半秒問 <see cref="MissionGuide"/> 現在該做什麼，
    /// 下一步變了才展開說明。
    /// </para>
    /// </summary>
    public sealed class TutorialCoach : IDisposable
    {
        sealed class Step
        {
            public string Key;
            public Func<string, bool> Done;
            /// <summary>
            /// 看狀態也判斷得出這一步做完了（不一定經過平常的操作：例如沒填數值就先確定設計、成品已經拿在手上）。
            /// 輪到這一步時狀態已經成立就直接往下，提示不會卡在做過的事情上。
            /// </summary>
            public Func<SessionState, GameData, bool> State;
            public SessionPhase Phase;
            /// <summary>標記要指的場景物件名稱；"clue" = 這一局放了線索的位置；空 = 不標。</summary>
            public string Target = "";
        }

        // 順序照流程：放大看一則線索 → 把數值輸入設計主螢幕 → 選件、確定設計 → 到架子拿小零件 →
        // 放到組裝台左側 → 照提示在右側組裝 → 裝上衛星 → 回中控室發射 → 營運。玩家一開始就站在中控室。
        // 新手訓練只挖空一格、只留一個零件（展開式太陽能翼）要親手組，其他零件開局就在衛星上。
        static readonly Step[] Steps =
        {
            new Step { Key = "move", Phase = SessionPhase.Design, Done = s => s == "move" },
            new Step { Key = "view", Phase = SessionPhase.Design, Done = s => s == "view" },
            new Step { Key = "clue", Phase = SessionPhase.Design, Done = s => s == "clue", Target = "clue" },
            new Step { Key = "fill", Phase = SessionPhase.Design, Done = s => s == "fill", Target = "BigScreen",
                State = (s, d) => s.Confirmed || Blanks.Cells(d, s).All(c => Blanks.Filled(s, c.part, c.field) != null) },
            new Step { Key = "confirm", Phase = SessionPhase.Design, Done = s => s == "confirm", Target = "BigScreen", State = (s, d) => s.Confirmed },
            new Step { Key = "shelf", Phase = SessionPhase.Design, Done = s => s == "comp_pick", Target = "CompShelf",
                State = (s, d) => s.CompsOf(Me).Count > 0 || s.Benches.Any(b => b.Left.Count > 0) || Recognised(s, d) },
            // 組裝分兩步：小零件放到左側（放齊了系統就認得出配方），再照提示一個一個拿到右側裝（組好才算完成）
            new Step { Key = "bench", Phase = SessionPhase.Design, Done = s => s == "recognised", Target = "AssemblyBench0", State = Recognised },
            new Step { Key = "assemble", Phase = SessionPhase.Design, Done = s => s == "assemble", Target = "AssemblyBench0", State = Built },
            new Step { Key = "install", Phase = SessionPhase.Design, Done = s => s == "install", Target = "IntegrationStand",
                State = (s, d) => SessionRules.NotInstalled(s, d).Count == 0 },
            new Step { Key = "launch", Phase = SessionPhase.Design, Done = s => s == "launch", Target = "LaunchButton" },
            new Step { Key = "console", Phase = SessionPhase.Ops, Done = s => s == "open:ops:power" || s.StartsWith("act:"), Target = "Screen_ops-power" },
            new Step { Key = "judge", Phase = SessionPhase.Ops, Done = s => s == "end_turn", Target = "Screen_ops_desk" },
        };

        static int Me => GameSession.LocalPlayer;

        /// <summary>組好了（成品在組裝台上、在手上，或已經都裝上衛星）。</summary>
        static bool Built(SessionState s, GameData d) =>
            s.Benches.Any(b => b.Job != null && b.Job.Done) || s.HeldBy(Me) != null || SessionRules.NotInstalled(s, d).Count == 0;

        /// <summary>組裝台認出配方了（或已經過了這一步）。</summary>
        static bool Recognised(SessionState s, GameData d) => s.Benches.Any(b => b.Job != null) || Built(s, d);

        const float ExpandSeconds = 8f;

        static int _step;
        static object _mission;

        readonly VisualElement _box, _marker;
        readonly Label _count, _title, _body, _more, _dist;
        float _advanceIn = -1;
        float _expandFor;
        bool _pinned;
        Transform _target;
        float _findIn;

        readonly bool _guide;
        MissionGuide.Step? _now;
        float _guideIn;

        // 每幀都會碰到的幾樣東西：內容沒變就不重組字串、不重算
        bool? _moreOpen;
        int _distShown = int.MinValue;
        /// <summary>上一次用哪一份狀態（哪個版本）、替哪一步、哪個玩家判斷過 <see cref="Step.State"/>：都一樣就不用再算。</summary>
        SessionState _checkedState;
        int _checkedRev, _checkedStep = -1, _checkedMe;
        static readonly List<Renderer> Renderers = new List<Renderer>();

        /// <param name="parent">提示框放在這裡（左上角那一欄）。</param>
        /// <param name="markerLayer">標記畫在這層（整個畫面）。</param>
        /// <param name="guide">學員難度的一般局：依狀態給下一步（見類別說明）。</param>
        public TutorialCoach(VisualElement parent, VisualElement markerLayer, bool guide = false)
        {
            _guide = guide;
            if (!ReferenceEquals(_mission, GameSession.Mission))
            {
                _mission = GameSession.Mission;
                _step = 0;
            }
            _marker = Ui.El("coach-marker").AddTo(markerLayer);
            _marker.pickingMode = PickingMode.Ignore;
            Ui.El("coach-marker-pin").AddTo(_marker).pickingMode = PickingMode.Ignore;
            _dist = Ui.Text("", "coach-marker-dist", "mono").AddTo(_marker);

            _box = Ui.El("coach").AddTo(parent);
            _box.pickingMode = PickingMode.Ignore;
            var head = Ui.Row("coach-head").AddTo(_box);
            _count = Ui.Text("", "coach-count", "mono").AddTo(head);
            _title = Ui.Text("", "coach-title").AddTo(head);
            Ui.Spacer().AddTo(head);
            _more = Ui.Text("", "coach-more").AddTo(head);
            _body = Ui.Text("", "body", "coach-body").AddTo(_box);
            Hud.TutorialSignal += OnSignal;
            if (GameSession.HasMission) GameSession.Mission.PhaseChanged += OnPhase;
            SkipToPhase();
            Show();
        }

        public void Dispose()
        {
            Hud.TutorialSignal -= OnSignal;
            if (GameSession.HasMission) GameSession.Mission.PhaseChanged -= OnPhase;
            _box.RemoveFromHierarchy();
            _marker.RemoveFromHierarchy();
        }

        void OnPhase(SessionPhase before, SessionPhase now)
        {
            SkipToPhase();
            Show();
        }

        /// <summary>直接發射或營運開始了：跳到那個階段的第一步（還沒做完的設計步驟就略過）。</summary>
        void SkipToPhase()
        {
            if (!GameSession.HasMission) return;
            var phase = GameSession.Mission.S.Phase;
            if (phase == SessionPhase.Ops && _step < Steps.Length && Steps[_step].Phase != SessionPhase.Ops)
                _step = Array.FindIndex(Steps, s => s.Phase == SessionPhase.Ops);
        }

        void OnSignal(string what)
        {
            if (_guide) return;
            if (_step >= Steps.Length || _advanceIn >= 0) return;
            if (!Steps[_step].Done(what)) return;
            Complete();
        }

        /// <summary>這一步做完了：打勾，停一下再換下一步。</summary>
        void Complete()
        {
            Sfx.Confirm();
            _box.AddToClassList("coach--done");
            _title.text = "✓ " + _title.text;
            _advanceIn = 0.9f;
        }

        /// <param name="visible">沒開面板、沒開選單：開著時整個收起來，不要蓋到面板。</param>
        public void Tick(float dt, bool visible)
        {
            if (_guide)
            {
                UpdateGuide(dt);
                visible &= _now != null;
            }
            _box.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            var kb = Keyboard.current;
            if (visible && kb != null && kb.hKey.wasPressedThisFrame && !UIRoot.IsTyping)
            {
                _pinned = !Expanded;
                _expandFor = 0;
            }
            // 面板開著的時候不倒數：關掉面板回來還看得到新步驟的說明
            if (visible) _expandFor = Mathf.Max(0, _expandFor - dt);
            bool open = Expanded;
            _body.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            if (_moreOpen != open)
            {
                _moreOpen = open;
                _more.text = Loc.T(open ? "ui.tut.less" : "ui.tut.more");
            }
            DrawMarker(visible, dt);

            // 看狀態就知道做完的步驟（見 Step.State）。判斷只看這一局的狀態和自己是誰：
            // 狀態沒變（同一份、同一個版本）、還是同一步，結果就和上一幀一樣，不用重算
            if (!_guide && _advanceIn < 0 && _step < Steps.Length && Steps[_step].State != null && GameSession.HasMission)
            {
                var s = GameSession.Mission.S;
                if (!ReferenceEquals(s, _checkedState) || s.Revision != _checkedRev || _step != _checkedStep || Me != _checkedMe)
                {
                    _checkedState = s;
                    _checkedRev = s.Revision;
                    _checkedStep = _step;
                    _checkedMe = Me;
                    if (s.Phase == Steps[_step].Phase && Steps[_step].State(s, GameSession.Data)) Complete();
                }
            }

            if (_advanceIn < 0) return;
            _advanceIn -= dt;
            if (_advanceIn > 0) return;
            _advanceIn = -1;
            _step++;
            _box.RemoveFromClassList("coach--done");
            Show();
        }

        bool Expanded => _pinned || _expandFor > 0;

        /// <summary>guide 模式：每半秒重新判斷；換了一步就展開說明、換標記，同一步只更新說明裡的數字。</summary>
        void UpdateGuide(float dt)
        {
            _guideIn -= dt;
            if (_guideIn > 0) return;
            _guideIn = 0.5f;
            var next = MissionGuide.Next();
            if (next == null) { _now = null; return; }
            var n = next.Value;
            bool changed = _now == null || _now.Value.Key != n.Key;
            _now = n;
            _body.text = n.Body;
            if (!changed) return;
            _target = null;
            _findIn = 0;
            _pinned = false;
            _expandFor = ExpandSeconds;
            _count.text = Loc.T("ui.guide.next");
            _title.text = Loc.T("ui.guide." + n.Key + ".t");
        }

        void Show()
        {
            if (_guide) { _now = null; _guideIn = 0; return; }
            _target = null;
            _findIn = 0;
            _pinned = false;
            _expandFor = ExpandSeconds;
            if (_step >= Steps.Length)
            {
                _count.text = "";
                _title.text = Loc.T("ui.tut.done.t");
                _body.text = Loc.T("ui.tut.done");
                return;
            }
            var s = Steps[_step];
            _count.text = (_step + 1) + "/" + Steps.Length;
            _title.text = Loc.T("ui.tut." + s.Key + ".t");
            _body.text = Loc.T("ui.tut." + s.Key);
        }

        // ---------------- 目標標記 ----------------

        void DrawMarker(bool visible, float dt)
        {
            var cam = Camera.main;
            var panel = _marker.panel;
            var pos = visible && cam != null && panel != null ? TargetPos(dt) : null;
            if (pos == null)
            {
                _marker.style.display = DisplayStyle.None;
                return;
            }
            var wp = pos.Value;
            var player = PoseFeed.Local.Position;
            float dist = Vector3.Distance(new Vector3(player.x, 0, player.z), new Vector3(wp.x, 0, wp.z));
            // 已經走到旁邊：互動提示會出現，標記就不需要了
            if (dist < 2.2f)
            {
                _marker.style.display = DisplayStyle.None;
                return;
            }
            _marker.style.display = DisplayStyle.Flex;
            int metres = Mathf.RoundToInt(dist);
            if (metres != _distShown)
            {
                _distShown = metres;
                _dist.text = metres + " m";
            }

            // 在背後或畫面外：貼在畫面邊緣，往目標的方向
            var size = _marker.parent.layout.size;
            var vp = cam.WorldToViewportPoint(wp);
            bool behind = vp.z < 0;
            if (behind) { vp.x = 1 - vp.x; vp.y = 1 - vp.y; }
            bool inside = !behind && vp.x > 0.04f && vp.x < 0.96f && vp.y > 0.06f && vp.y < 0.94f;
            Vector2 sp;
            if (inside) sp = RuntimePanelUtils.CameraTransformWorldToPanel(panel, wp, cam);
            else
            {
                var d = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                if (d.sqrMagnitude < 1e-4f) d = Vector2.down;
                float k = 0.44f / Mathf.Max(Mathf.Abs(d.x), Mathf.Abs(d.y));
                var e = new Vector2(0.5f, 0.5f) + d * k;
                sp = new Vector2(e.x * size.x, (1 - e.y) * size.y);
            }
            _marker.EnableInClassList("coach-marker--edge", !inside);
            _marker.style.left = sp.x;
            _marker.style.top = sp.y;
        }

        Vector3? TargetPos(float dt)
        {
            string name = _guide ? _now?.Target : _step < Steps.Length ? Steps[_step].Target : null;
            if (string.IsNullOrEmpty(name)) return null;
            if (name == "clue") name = ClueObject();
            if (name == null) return null;
            if (_target == null)
            {
                _findIn -= dt;
                if (_findIn > 0) return null;
                _findIn = 0.5f;
                var go = GameObject.Find(name);
                if (go == null) return null;
                _target = go.transform;
            }
            return Top(_target);
        }

        /// <summary>這一局放了線索的位置（場景物件叫 Clue_ 加位置 id）：挑第一個有線索的。</summary>
        static string ClueObject()
        {
            if (!GameSession.HasMission) return null;
            foreach (var spot in Clues.Spots)
                if (GameSession.Mission.CluesAt(spot.Id).Count > 0) return "Clue_" + spot.Id;
            return null;
        }

        /// <summary>物件頂端上方一點。</summary>
        static Vector3 Top(Transform t)
        {
            Bounds b = default;
            bool any = false;
            // 每幀都要量一次（目標上面的東西會變）：用同一個清單接結果，不要每幀配一個新陣列
            t.GetComponentsInChildren(false, Renderers);
            foreach (var r in Renderers)
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            Renderers.Clear();
            return any ? new Vector3(b.center.x, b.max.y + 0.25f, b.center.z) : t.position + Vector3.up;
        }
    }
}
