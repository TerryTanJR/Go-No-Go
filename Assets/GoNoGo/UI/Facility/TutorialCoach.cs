using System;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 新手訓練：左上角的任務追蹤，平常只有一行「3/13 打一通電話」；換步驟時展開說明 8 秒，按 H 可以再展開或收起。
    /// 目標物件上方有標記和距離（不在畫面裡時貼在畫面邊緣），不必靠文字找位置。
    /// 做到了（收到 <see cref="Hud.Signal"/>）就打勾、換下一步。步驟跨場景保留（發射畫面回來後接著做營運的步驟）。
    /// </summary>
    public sealed class TutorialCoach : IDisposable
    {
        sealed class Step
        {
            public string Key;
            public Func<string, bool> Done;
            public SessionPhase Phase;
            /// <summary>標記要指的場景物件名稱；"code" = 零件架上的任一個零件；空 = 不標。</summary>
            public string Target = "";
        }

        // 順序照遊戲的核心：先問需求（電話、筆記板），再到組裝區採購、安裝、測試、看大螢幕，
        // 回中控室到後排查型錄（價格與規格），最後發射。玩家一開始就站在中控室。
        static readonly Step[] Steps =
        {
            new Step { Key = "move", Phase = SessionPhase.Design, Done = s => s == "move" },
            new Step { Key = "view", Phase = SessionPhase.Design, Done = s => s == "view" },
            new Step { Key = "call", Phase = SessionPhase.Design, Done = s => s.StartsWith("call:"), Target = "Screen_phone" },
            new Step { Key = "notes", Phase = SessionPhase.Design, Done = s => s == "open:notes", Target = "Screen_notes" },
            new Step { Key = "procure", Phase = SessionPhase.Design, Done = s => s == "order", Target = "ProcureConsole" },
            new Step { Key = "pick", Phase = SessionPhase.Design, Done = s => s.StartsWith("pick:"), Target = "code" },
            new Step { Key = "install", Phase = SessionPhase.Design, Done = s => s == "install", Target = "IntegrationStand" },
            new Step { Key = "test", Phase = SessionPhase.Design, Done = s => s == "test", Target = "TestConsole" },
            new Step { Key = "dashboard", Phase = SessionPhase.Design, Done = s => s == "open:dashboard" || s == "open:tablet", Target = "BayDashboard" },
            // 後排北座的手冊 2・電力（組裝區的完整手冊也算，裡面一樣翻得到第 2 章）
            new Step { Key = "manual", Phase = SessionPhase.Design, Done = s => s == "open:manual:power" || s == "open:bay_manual", Target = "Screen_manual-power" },
            new Step { Key = "launch", Phase = SessionPhase.Design, Done = s => s == "launch", Target = "LaunchButton" },
            new Step { Key = "console", Phase = SessionPhase.Ops, Done = s => s == "open:ops:power" || s.StartsWith("act:"), Target = "Screen_ops-power" },
            new Step { Key = "judge", Phase = SessionPhase.Ops, Done = s => s == "end_turn", Target = "Screen_ops_desk" },
        };

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

        /// <param name="parent">提示框放在這裡（左上角那一欄）。</param>
        /// <param name="markerLayer">標記畫在這層（整個畫面）。</param>
        public TutorialCoach(VisualElement parent, VisualElement markerLayer)
        {
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
            if (_step >= Steps.Length || _advanceIn >= 0) return;
            if (!Steps[_step].Done(what)) return;
            Sfx.Confirm();
            _box.AddToClassList("coach--done");
            _title.text = "✔ " + _title.text;
            _advanceIn = 0.9f;
        }

        /// <param name="visible">沒開面板、沒開選單：開著時整個收起來，不要蓋到面板。</param>
        public void Tick(float dt, bool visible)
        {
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
            _more.text = Loc.T(open ? "ui.tut.less" : "ui.tut.more");
            DrawMarker(visible, dt);

            if (_advanceIn < 0) return;
            _advanceIn -= dt;
            if (_advanceIn > 0) return;
            _advanceIn = -1;
            _step++;
            _box.RemoveFromClassList("coach--done");
            Show();
        }

        bool Expanded => _pinned || _expandFor > 0;

        void Show()
        {
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
            _dist.text = Mathf.RoundToInt(dist) + " m";

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
            if (_step >= Steps.Length || string.IsNullOrEmpty(Steps[_step].Target)) return null;
            if (Steps[_step].Target == "code") return PartOnRack();
            if (_target == null)
            {
                _findIn -= dt;
                if (_findIn > 0) return null;
                _findIn = 0.5f;
                var go = GameObject.Find(Steps[_step].Target);
                if (go == null) return null;
                _target = go.transform;
            }
            return Top(_target);
        }

        /// <summary>零件架上看得到的零件（零件編號標籤）。手上已經拿著就不標。</summary>
        static Vector3? PartOnRack()
        {
            if (GameSession.HasMission && GameSession.Mission.S.HeldBy(GameSession.LocalPlayer) != null) return null;
            foreach (var wl in WorldLabel.All)
                if (wl != null && wl.isActiveAndEnabled && wl.style == "code") return wl.WorldPos + Vector3.up * 0.15f;
            return null;
        }

        /// <summary>物件頂端上方一點。</summary>
        static Vector3 Top(Transform t)
        {
            Bounds b = default;
            bool any = false;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any ? new Vector3(b.center.x, b.max.y + 0.25f, b.center.z) : t.position + Vector3.up;
        }
    }
}
