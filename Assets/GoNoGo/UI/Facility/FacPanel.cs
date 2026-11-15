using System;
using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>Facility 裡按 E 打開的面板。內容依狀態重建（<see cref="Refresh"/>）。</summary>
    public abstract class FacPanel
    {
        protected static MissionSession M => GameSession.Mission;
        protected static GameData D => GameSession.Data;
        protected VisualElement Body { get; private set; }
        protected Action Close { get; private set; }

        public abstract string Title { get; }
        public virtual bool Closable => true;
        public virtual bool CloseWithE => true;
        public virtual string WideClass => "";

        public void Attach(VisualElement body, Action close)
        {
            Body = body;
            Close = close;
            Refresh();
        }

        /// <summary>
        /// 重畫之後要不要留住捲動位置：回傳 null = 不留（每次重畫回到最上面）；回傳字串 = 和上一次一樣才留
        /// （例如同一個分頁、同一類零件：填了一格數值、勾了一個零件，清單不會跳回最上面；換分頁就從頭開始）。
        /// </summary>
        protected virtual string ScrollKey => null;
        string _scrollKey;

        public virtual void Refresh()
        {
            if (Body == null) return;
            string key = ScrollKey;
            List<Vector2> offsets = null;
            if (key != null && key == _scrollKey)
            {
                offsets = new List<Vector2>();
                Body.Query<ScrollView>(className: KeepScrollClass).ForEach(sv => offsets.Add(sv.scrollOffset));
            }
            _scrollKey = key;
            Body.Clear();
            Build(Body);
            if (offsets == null) return;
            var now = Body.Query<ScrollView>(className: KeepScrollClass).ToList();
            if (now.Count != offsets.Count) return;
            for (int i = 0; i < now.Count; i++) RestoreScroll(now[i], offsets[i]);
        }

        /// <summary>有這個 class 的 ScrollView 重畫後會回到原本的捲動位置（見 <see cref="ScrollKey"/>）。</summary>
        public const string KeepScrollClass = "keep-scroll";

        /// <summary>內容排好版之後才捲得過去：等第一次排版完再設，下一幀再設一次（捲軸的範圍晚一點才更新）。</summary>
        static void RestoreScroll(ScrollView sv, Vector2 offset)
        {
            if (offset == Vector2.zero) return;
            EventCallback<GeometryChangedEvent> once = null;
            once = _ =>
            {
                sv.contentContainer.UnregisterCallback(once);
                sv.scrollOffset = offset;
                sv.schedule.Execute(() => sv.scrollOffset = offset);
            };
            sv.contentContainer.RegisterCallback(once);
        }

        protected abstract void Build(VisualElement body);
        public virtual void Tick(float dt) { }
        public virtual void Detach() { Body = null; }

        public static FacPanel Create(string id)
        {
            if (id != null && id.StartsWith("ops:")) return new OpsConsolePanel(id.Substring(4));
            if (id != null && id.StartsWith("manual:")) return new ManualPanel(id.Substring(7));
            if (id != null && id.StartsWith("clue:")) return new CluePanel(id.Substring(5));
            if (id != null && id.StartsWith("minigame:"))
            {
                var parts = id.Split(':');
                int.TryParse(parts.Length > 2 ? parts[2] : "2", out var sev);
                return new MinigamePanel(parts[1], sev);
            }
            return id switch
            {
                "briefing" => new BriefingPanel(),
                "lobby" => new LobbyPanel(),
                "lobby_start" => new LobbyStartPanel(),
                "select" => new SelectPanel(),
                "bay" => new BayPanel(),
                "codex" => new CodexPanel(),
                "designs" => new DesignsPanel(),
                "launch" => new LaunchPanel(),
                "failures" => new FailuresPanel(),
                "tablet" => new TabletPanel(),
                "ops_desk" => new OpsDeskPanel(),
                "minigame" => new MinigamePanel(),
                "minigame_debug" => new MinigameDebugPanel(),
                _ => null,
            };
        }

        protected static VisualElement Tabs(VisualElement parent, string[] keys, int current, Action<int> pick)
        {
            var row = Ui.Row("tabs").AddTo(parent);
            for (int i = 0; i < keys.Length; i++)
            {
                int k = i;
                Ui.Btn(Loc.T(keys[i]), () => pick(k), "tab").Cls("tab--on", i == current).AddTo(row);
            }
            return row;
        }

        protected static ScrollView Scroll(VisualElement parent, string cls = "panel-scroll")
        {
            var sv = new ScrollView(ScrollViewMode.Vertical);
            sv.AddToClassList(cls);
            sv.AddToClassList(KeepScrollClass);
            parent.Add(sv);
            return sv;
        }
    }
}
