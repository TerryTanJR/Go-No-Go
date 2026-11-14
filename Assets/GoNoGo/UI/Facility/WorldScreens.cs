using System.Collections.Generic;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 把場景裡的 <see cref="WorldScreen"/>（失敗原因看板、儀表板大螢幕、大廳螢幕）畫成 UI Toolkit 面板：
    /// 每塊螢幕一個 RenderTexture＋PanelSettings＋UIDocument，狀態或語言改變時重畫。由 FacilityScreen 建立與釋放。
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
                e.Rt = new RenderTexture(ws.resolution.x, ws.resolution.y, 0, RenderTextureFormat.ARGB32) { name = "WS_" + ws.id };
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
            var body = Ui.El("wscreen-body").AddTo(root);
            switch (e.Screen.id)
            {
                case "failures":
                    Ui.Text(Loc.T("ui.fail.title"), "eyebrow", "wscreen-title").AddTo(body);
                    FailureBoardView.Build(body);
                    break;
                case "dashboard":
                    Ui.Text(Loc.T("ui.dash.title"), "eyebrow", "wscreen-title").AddTo(body);
                    DashboardView.Build(body, true);
                    break;
                case "notes":
                    NotesView.Build(body, true);
                    break;
                case "lobby":
                    LobbyView.Build(body, true);
                    break;
            }
        }

        public void Dispose()
        {
            foreach (var e in _entries)
            {
                if (e.Screen != null && e.Screen.target != null && e.Screen.target.sharedMaterial == e.Mat) e.Screen.target.sharedMaterial = e.Original;
                if (e.Rt != null) { e.Rt.Release(); Object.Destroy(e.Rt); }
                if (e.Panel != null) Object.Destroy(e.Panel);
                if (e.Mat != null) Object.Destroy(e.Mat);
            }
            _entries.Clear();
            if (_host != null) Object.Destroy(_host);
        }
    }
}
