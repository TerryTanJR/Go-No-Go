using System;
using GoNoGo.Core;
using GoNoGo.Game;
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

        public virtual void Refresh()
        {
            if (Body == null) return;
            Body.Clear();
            Build(Body);
        }

        protected abstract void Build(VisualElement body);
        public virtual void Tick(float dt) { }
        public virtual void Detach() { Body = null; }

        public static FacPanel Create(string id)
        {
            if (id != null && id.StartsWith("ops:")) return new OpsConsolePanel(id.Substring(4));
            if (id != null && id.StartsWith("manual:")) return new ManualPanel(id.Substring(7));
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
                "dashboard" => new DashboardPanel(),
                "goals" => new GoalsPanel(),
                "procure" => new ProcurePanel(),
                "manual" => new ManualIndexPanel(),
                "phone" => new PhonePanel(),
                "notes" => new NotesPanel(),
                "mission" => new MissionConsolePanel(),
                "launch" => new LaunchPanel(),
                "failures" => new FailuresPanel(),
                "tablet" => new TabletPanel(),
                "ops_desk" => new OpsDeskPanel(),
                "minigame" => new MinigamePanel(),
                "minigame_debug" => new MinigameDebugPanel(),
                // 組裝區的手冊：嚴格分工時只有代號與用途（數字在中控室）
                "bay_manual" => M.S.StrictRoles && M.S.Multiplayer ? new BayManualPanel() : new ManualPanel(),
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
            parent.Add(sv);
            return sv;
        }
    }
}
