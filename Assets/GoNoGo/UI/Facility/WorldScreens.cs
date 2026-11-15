using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 把場景裡的 <see cref="WorldScreen"/>（中控室前方的大屏幕、組裝區儀表板、失敗原因看板、組裝台螢幕、大廳螢幕）
    /// 畫成 UI Toolkit 面板：每塊螢幕一個 RenderTexture＋PanelSettings＋UIDocument，狀態或語言改變時重畫。
    /// 由 FacilityScreen 建立與釋放。前方大屏在選件、組裝階段畫的是選件的畫面，營運時換成地球縮景（opsMaterial）、
    /// 兩側的螢幕改投各子系統的遙測。
    /// </summary>
    public sealed class WorldScreens
    {
        sealed class Entry
        {
            public WorldScreen Screen;
            public RenderTexture Rt;
            public PanelSettings Panel;
            public UIDocument Doc;
            public Material Mat, Original;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly GameObject _host;
        float _dirtyAt = -1;

        public WorldScreens()
        {
            _host = new GameObject("[WorldScreens]");
            var baseSettings = Resources.Load<PanelSettings>("UI/PanelSettings");
            foreach (var ws in WorldScreen.All)
            {
                if (ws == null || ws.target == null) continue;
                var e = new Entry { Screen = ws };
                // 要有 stencil（深度 24）：UI Toolkit 用 stencil 裁切圓角的 overflow: hidden（進度條），沒有的話整條會畫成白的
                e.Rt = new RenderTexture(ws.resolution.x, ws.resolution.y, 24, RenderTextureFormat.ARGB32) { name = "WS_" + ws.id };
                e.Rt.Create();
                e.Panel = ScriptableObject.CreateInstance<PanelSettings>();
                if (baseSettings != null)
                {
                    e.Panel.themeStyleSheet = baseSettings.themeStyleSheet;
                    e.Panel.textSettings = baseSettings.textSettings;
                }
                e.Panel.targetTexture = e.Rt;
                e.Panel.scaleMode = PanelScaleMode.ConstantPixelSize;
                e.Panel.scale = ws.resolution.x / 1024f;
                e.Panel.clearColor = true;
                e.Panel.colorClearValue = new Color(0.035f, 0.045f, 0.06f, 1f);
                // 世界螢幕不接收滑鼠（按 E 打開的是一般面板）
                e.Panel.sortingOrder = -100;
                e.Panel.SetScreenToPanelSpaceFunction(_ => new Vector2(float.NaN, float.NaN));
                var go = new GameObject("WS_" + ws.id);
                go.transform.SetParent(_host.transform, false);
                e.Doc = go.AddComponent<UIDocument>();
                e.Doc.panelSettings = e.Panel;

                e.Original = ws.target.sharedMaterial;
                e.Mat = new Material(ws.target.sharedMaterial) { name = "WS_" + ws.id };
                e.Mat.SetTexture("_BaseMap", e.Rt);
                e.Mat.SetColor("_BaseColor", Color.white);
                ws.target.sharedMaterial = e.Mat;
                _entries.Add(e);
            }
            MarkDirty();
        }

        public void MarkDirty() => _dirtyAt = 0;

        /// <summary>每幀呼叫；狀態改變後最多每 0.25 秒重畫一次。</summary>
        public void Tick()
        {
            if (_dirtyAt < 0 || Time.unscaledTime < _dirtyAt) return;
            _dirtyAt = -1;
            foreach (var e in _entries) Draw(e);
        }

        void Draw(Entry e)
        {
            var root = e.Doc.rootVisualElement;
            if (root == null) return;
            root.Clear();
            root.AddToClassList("app");
            root.AddToClassList("wscreen");
            if (!GameSession.HasMission) return;
            var m = GameSession.Mission;
            bool ops = m.S.Phase == SessionPhase.Ops || m.S.Phase == SessionPhase.Report;
            // 營運時換貼圖的螢幕（前方大屏 → 地球縮景）：不畫介面
            if (e.Screen.opsMaterial != null && e.Screen.target != null)
            {
                var want = ops ? e.Screen.opsMaterial : e.Mat;
                if (e.Screen.target.sharedMaterial != want) e.Screen.target.sharedMaterial = want;
                if (ops) return;
            }
            string id = e.Screen.id ?? "";
            // ws-bigscreen、ws-wall_reqs、ws-bay、ws-bench-0…：每一面螢幕的實際大小不同，字級各自在 Theme.uss 調
            var body = Ui.El("wscreen-body", "ws-" + id.Replace(':', '-')).AddTo(root);
            if (id.StartsWith("bench:"))
            {
                // 組裝台背板上的螢幕：這一座的組裝單
                int.TryParse(id.Substring(6), out int bench);
                BenchOrderView.Build(body, bench, true);
                return;
            }
            switch (id)
            {
                case "failures":
                    // 還沒失敗過：整塊螢幕保持暗的，不放標題（Reveal）
                    if (!Reveal.Failures) break;
                    Ui.Text(Loc.T("ui.fail.title"), "eyebrow", "wscreen-title").AddTo(body);
                    FailureBoardView.Build(body);
                    break;
                case "bigscreen":
                    // 中控室前方的大屏幕：現在選了什麼、儀表板（操作按 E 打開）
                    Ui.Text(Loc.T("ui.sel.title"), "eyebrow", "wscreen-title").AddTo(body);
                    SelectView.WallMain(body);
                    break;
                case "wall_reqs":
                    // 大屏左側：選件、組裝時是任務需求；營運時是酬載、電力的遙測
                    if (ops) OpsWall(body, "payload", "power");
                    else SelectView.Requirements(body, true, false);
                    break;
                case "wall_asm":
                    // 大屏右側：選件、組裝時是失敗原因（有的話）或流程說明；營運時是姿態、通訊的遙測
                    if (ops) OpsWall(body, "attitude", "comms");
                    else if (Reveal.Failures && m.S.FailedLaunches > 0)
                    {
                        Ui.Text(Loc.T("ui.fail.title"), "eyebrow", "wscreen-title").AddTo(body);
                        FailureBoardView.Build(body);
                    }
                    else HowTo(body);
                    break;
                case "bay":
                    // 組裝區牆上的儀表板：等待確定設計／每個零件要用哪些小零件
                    Ui.Text(Loc.T("ui.bay.title"), "eyebrow", "wscreen-title").AddTo(body);
                    SelectView.Bay(body, true);
                    break;
                case "lobby":
                    LobbyView.Build(body, true);
                    break;
                case "lobby_select":
                    LobbyView.SideBoard(body, "select");
                    break;
                case "lobby_assemble":
                    LobbyView.SideBoard(body, "assemble");
                    break;
            }
        }

        /// <summary>流程說明（大屏右側）：找線索 → 填數值 → 選件 → 確定設計 → 組裝 → 發射。現在做到哪一步會標出來。</summary>
        static void HowTo(VisualElement body)
        {
            var s = GameSession.Mission.S;
            var d = GameSession.Data;
            Ui.Text(Loc.T("ui.wall.howto"), "eyebrow", "wscreen-title").AddTo(body);
            int left = SessionRules.NotInstalled(s, d).Count;
            int now = !s.Confirmed ? (Blanks.Cells(d, s).Count > 0 ? 0 : 2) : left > 0 ? 4 : 5;
            if (!s.Confirmed && Blanks.Cells(d, s).Any(c => Blanks.Filled(s, c.part, c.field) != null)) now = 1;
            for (int i = 0; i < 6; i++)
            {
                var row = Ui.Row("howto-row").Cls("howto-row--now", i == now).Cls("howto-row--done", i < now).AddTo(body);
                Ui.Text(i < now ? "✔" : (i + 1).ToString(), "howto-n", "mono").AddTo(row);
                Ui.Text(Loc.T("ui.wall.howto." + i), "howto-text").AddTo(row);
            }
        }

        /// <summary>營運時投在大屏兩側的遙測：指定的幾個子系統，讀數和營運席上看到的一樣，不用走到席位前也看得到。</summary>
        static void OpsWall(VisualElement body, params string[] consoles)
        {
            var m = GameSession.Mission;
            var d = GameSession.Data;
            var s = m.S;
            var e = s.Phase == SessionPhase.Ops ? m.CurrentEvent : null;
            var design = s.LastLaunch?.Design ?? s.Bench;
            var ev = Evaluator.EvaluateDesign(design, d, s.DifficultyId, false);
            var sample = e != null ? m.SampleAt(e.Day) : null;
            foreach (var c in consoles)
            {
                Ui.Text(Loc.T("ops.console." + c), "eyebrow", "wscreen-title").AddTo(body);
                var table = Ui.El("readings").AddTo(body);
                foreach (var r in OpsTelemetry.For(c, e, ev, design, d, sample))
                {
                    var row = Ui.Row("reading").Cls("reading--alert", r.Alert && e != null).AddTo(table);
                    string name = r.Key.StartsWith("part:") ? (d.Part(r.Key.Substring(5))?.Code + " " + Loc.Part(r.Key.Substring(5))) : Loc.T(r.Key);
                    Ui.Text(name, "reading-k").AddTo(row);
                    Ui.Spacer().AddTo(row);
                    string val = r.Value.StartsWith("opp.") || r.Value.StartsWith("site.") ? Loc.T(r.Value)
                        : r.Value == "NO DATA" ? Loc.T("ui.ops.no_data") : r.Value;
                    Ui.Text(val + (string.IsNullOrEmpty(r.Unit) ? "" : " " + r.Unit), "mono", "reading-v").AddTo(row);
                }
            }
        }

        public void Dispose()
        {
            foreach (var e in _entries)
            {
                if (e.Screen != null && e.Screen.target != null
                    && (e.Screen.target.sharedMaterial == e.Mat || (e.Screen.opsMaterial != null && e.Screen.target.sharedMaterial == e.Screen.opsMaterial)))
                    e.Screen.target.sharedMaterial = e.Original;
                if (e.Rt != null) { e.Rt.Release(); Object.Destroy(e.Rt); }
                if (e.Panel != null) Object.Destroy(e.Panel);
                if (e.Mat != null) Object.Destroy(e.Mat);
            }
            _entries.Clear();
            if (_host != null) Object.Destroy(_host);
        }
    }
}
