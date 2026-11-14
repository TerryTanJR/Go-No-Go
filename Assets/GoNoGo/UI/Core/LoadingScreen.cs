using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 進場過場：一條進度條，小火箭沿著它往右飛。開房、加入房間、開始任務時蓋住整個畫面，期間點不到任何介面。
    /// 連線那段沒有真實進度，用越走越慢的假進度（停在 60% 前）；換場景那段接 <see cref="SceneFlow.Progress"/>。
    /// 場景載入完就跑到底、淡出。連線失敗時由呼叫端 <see cref="End"/> 收起來。
    /// </summary>
    public sealed class LoadingScreen
    {
        const float ConnectShare = 0.6f;

        readonly VisualElement _root, _fill;
        readonly RocketGlyph _rocket;
        readonly Label _title, _stage, _pct;
        bool _active, _network, _sawLoad;
        float _t, _shown, _doneFor;

        public bool Active => _active;

        public LoadingScreen(VisualElement parent)
        {
            _root = Ui.El("loading").AddTo(parent);
            _root.focusable = true;
            _root.style.display = DisplayStyle.None;
            var col = Ui.El("loading-col").AddTo(_root);
            _title = Ui.Text("", "loading-title").AddTo(col);
            var track = Ui.El("loading-track").AddTo(col);
            _fill = Ui.El("loading-fill").AddTo(track);
            _rocket = new RocketGlyph().AddTo(track);
            _rocket.AddToClassList("loading-rocket");
            var row = Ui.Row("loading-row").AddTo(col);
            _stage = Ui.Text("", "loading-stage").AddTo(row);
            Ui.Spacer().AddTo(row);
            _pct = Ui.Text("", "loading-pct", "mono").AddTo(row);
            // 過場期間吃掉所有點擊和鍵盤（介面在底下還看得到，但按不到）
            _root.RegisterCallback<PointerDownEvent>(e => e.StopPropagation(), TrickleDown.TrickleDown);
            _root.RegisterCallback<NavigationMoveEvent>(e => e.StopPropagation(), TrickleDown.TrickleDown);
            _root.RegisterCallback<NavigationSubmitEvent>(e => e.StopPropagation(), TrickleDown.TrickleDown);
        }

        /// <param name="network">要先連線（開房、加入）；false = 只換場景（個人任務、新手訓練）。</param>
        public void Begin(bool network)
        {
            _active = true;
            _network = network;
            _sawLoad = false;
            _t = 0;
            _shown = 0;
            _doneFor = -1;
            _title.text = Loc.T(network ? "ui.load.title_net" : "ui.load.title");
            _root.style.display = DisplayStyle.Flex;
            _root.RemoveFromClassList("loading--out");
            _root.Focus();
            Draw();
        }

        public void End()
        {
            if (!_active) return;
            _active = false;
            _root.AddToClassList("loading--out");
            _root.schedule.Execute(() => { if (!_active) _root.style.display = DisplayStyle.None; }).StartingIn(350);
        }

        public void Tick(float dt)
        {
            if (!_active) return;
            _t += dt;
            float start = _network ? ConnectShare : 0f;
            float target;
            string stage;
            if (SceneFlow.Loading)
            {
                _sawLoad = true;
                target = start + (1 - start) * SceneFlow.Progress;
                stage = "ui.load.scene";
            }
            else if (_sawLoad)
            {
                target = 1f;
                stage = "ui.load.scene";
            }
            else
            {
                // 還在連線：越走越慢地逼近 60%
                target = start * (1 - Mathf.Exp(-_t / 3f));
                stage = _t < 2.5f ? "ui.load.connect" : "ui.load.sync";
            }
            _shown = Mathf.MoveTowards(_shown, Mathf.Max(_shown, target), dt * 0.8f);
            _stage.text = Loc.T(stage);
            Draw();
            _rocket.MarkDirtyRepaint();

            if (_sawLoad && !SceneFlow.Loading && _shown >= 0.999f)
            {
                if (_doneFor < 0) _doneFor = 0.35f;
                _doneFor -= dt;
                if (_doneFor <= 0) End();
            }
        }

        void Draw()
        {
            float p = Mathf.Clamp01(_shown);
            _fill.style.width = Length.Percent(p * 100);
            _rocket.style.left = Length.Percent(p * 100);
            _pct.text = Mathf.RoundToInt(p * 100) + "%";
        }

        /// <summary>往右飛的小火箭：機身、鼻錐、尾翼，尾巴一團會閃的火焰。</summary>
        sealed class RocketGlyph : VisualElement
        {
            static readonly Color Body = new Color(0.9f, 0.93f, 0.97f);
            static readonly Color Accent = new Color(0.22f, 0.78f, 0.96f);
            static readonly Color Flame = new Color(1f, 0.62f, 0.2f);
            static readonly Color FlameCore = new Color(1f, 0.9f, 0.55f);

            public RocketGlyph()
            {
                pickingMode = PickingMode.Ignore;
                generateVisualContent += Paint;
            }

            void Paint(MeshGenerationContext ctx)
            {
                var p = ctx.painter2D;
                var r = contentRect;
                float h = r.height, w = r.width, cy = r.center.y;
                float flick = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 40f);

                // 火焰（在機身左邊）
                p.fillColor = Flame;
                p.BeginPath();
                p.MoveTo(new Vector2(w * 0.32f, cy - h * 0.2f));
                p.LineTo(new Vector2(w * 0.32f - w * 0.32f * flick, cy));
                p.LineTo(new Vector2(w * 0.32f, cy + h * 0.2f));
                p.ClosePath();
                p.Fill();
                p.fillColor = FlameCore;
                p.BeginPath();
                p.MoveTo(new Vector2(w * 0.32f, cy - h * 0.1f));
                p.LineTo(new Vector2(w * 0.32f - w * 0.16f * flick, cy));
                p.LineTo(new Vector2(w * 0.32f, cy + h * 0.1f));
                p.ClosePath();
                p.Fill();

                // 尾翼
                p.fillColor = Accent;
                p.BeginPath();
                p.MoveTo(new Vector2(w * 0.36f, cy - h * 0.18f));
                p.LineTo(new Vector2(w * 0.3f, cy - h * 0.45f));
                p.LineTo(new Vector2(w * 0.5f, cy - h * 0.18f));
                p.ClosePath();
                p.Fill();
                p.BeginPath();
                p.MoveTo(new Vector2(w * 0.36f, cy + h * 0.18f));
                p.LineTo(new Vector2(w * 0.3f, cy + h * 0.45f));
                p.LineTo(new Vector2(w * 0.5f, cy + h * 0.18f));
                p.ClosePath();
                p.Fill();

                // 機身＋鼻錐
                p.fillColor = Body;
                p.BeginPath();
                p.MoveTo(new Vector2(w * 0.32f, cy - h * 0.2f));
                p.LineTo(new Vector2(w * 0.78f, cy - h * 0.2f));
                p.LineTo(new Vector2(w, cy));
                p.LineTo(new Vector2(w * 0.78f, cy + h * 0.2f));
                p.LineTo(new Vector2(w * 0.32f, cy + h * 0.2f));
                p.ClosePath();
                p.Fill();

                // 窗
                p.fillColor = Accent;
                p.BeginPath();
                p.Arc(new Vector2(w * 0.66f, cy), h * 0.09f, 0, 360);
                p.Fill();
            }
        }
    }
}
