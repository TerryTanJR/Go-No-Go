using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>軌道一圈的日照與地影：圓環上琥珀色是日照、深色是地影，中間寫地影幾分鐘。</summary>
    public sealed class OrbitRing : VisualElement
    {
        readonly float _eclipse;
        static readonly Color Sun = new Color(1f, 0.72f, 0.28f);
        static readonly Color Shadow = new Color(0.16f, 0.22f, 0.34f);

        /// <param name="eclipseFraction">地影佔一圈的比例 0–1。</param>
        public OrbitRing(float eclipseFraction, string center, string sub)
        {
            _eclipse = Mathf.Clamp01(eclipseFraction);
            AddToClassList("mring");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            var mid = Ui.El("mring-mid").AddTo(this);
            mid.pickingMode = PickingMode.Ignore;
            Ui.Text(center, "mring-v", "mono").AddTo(mid);
            Ui.Text(sub, "mring-k").AddTo(mid);
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            float rad = Mathf.Min(r.width, r.height) / 2 - 10;
            if (rad < 4) return;
            var p = ctx.painter2D;
            p.lineWidth = 16;
            p.lineCap = LineCap.Butt;
            float split = 360f * (1 - _eclipse);
            p.strokeColor = Sun;
            p.BeginPath(); p.Arc(r.center, rad, -90, -90 + split); p.Stroke();
            p.strokeColor = Shadow;
            p.BeginPath(); p.Arc(r.center, rad, -90 + split, 270); p.Stroke();
            // 地球在中間的感覺：一圈細線
            p.lineWidth = 1;
            p.strokeColor = new Color(1, 1, 1, 0.12f);
            p.BeginPath(); p.Arc(r.center, rad - 14, 0, 360); p.Stroke();
        }
    }

    /// <summary>太空天氣時間軸：照日期由左到右，長條高度是嚴重程度 1–3，顏色分閃焰、日冕噴發、地磁暴。</summary>
    public sealed class StormTimeline : VisualElement
    {
        public struct Item
        {
            public string Date, Type;
            public int Severity;
        }

        const float PadL = 24, PadR = 24, PadT = 16, PadB = 44;
        readonly List<Item> _items;
        readonly VisualElement _text;

        public static Color TypeColor(string type) => type switch
        {
            "FLR" => new Color(1f, 0.72f, 0.28f),
            "CME" => new Color(0.95f, 0.41f, 0.88f),
            _ => new Color(0.22f, 0.78f, 0.96f),
        };

        public StormTimeline(List<Item> items)
        {
            _items = items;
            AddToClassList("mtime");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            _text = new VisualElement { pickingMode = PickingMode.Ignore };
            _text.AddToClassList("mchart-text");
            Add(_text);
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        Rect Plot => new Rect(PadL, PadT, Mathf.Max(1, contentRect.width - PadL - PadR), Mathf.Max(1, contentRect.height - PadT - PadB));

        void Draw(MeshGenerationContext ctx)
        {
            var r = Plot;
            if (r.width < 8 || _items.Count == 0) return;
            var p = ctx.painter2D;
            p.lineWidth = 1;
            p.strokeColor = new Color(1, 1, 1, 0.08f);
            for (int s = 1; s <= 3; s++)
            {
                float y = r.yMax - r.height * s / 3f;
                p.BeginPath(); p.MoveTo(new Vector2(r.x, y)); p.LineTo(new Vector2(r.xMax, y)); p.Stroke();
            }
            // 每一格的寬度在這一次繪製裡不會變：算一次就好
            float slot = r.width / Mathf.Max(1, _items.Count);
            float w = Mathf.Min(44, slot * 0.6f);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                float h = r.height * Mathf.Clamp(it.Severity, 1, 3) / 3f;
                float x = r.x + slot * (i + 0.5f) - w / 2;
                p.fillColor = TypeColor(it.Type);
                p.BeginPath();
                p.MoveTo(new Vector2(x, r.yMax)); p.LineTo(new Vector2(x, r.yMax - h));
                p.LineTo(new Vector2(x + w, r.yMax - h)); p.LineTo(new Vector2(x + w, r.yMax));
                p.ClosePath(); p.Fill();
            }
            p.strokeColor = new Color(1, 1, 1, 0.3f);
            p.lineWidth = 2;
            p.BeginPath(); p.MoveTo(new Vector2(r.x, r.yMax)); p.LineTo(new Vector2(r.xMax, r.yMax)); p.Stroke();
        }

        void Layout()
        {
            _text.Clear();
            var r = Plot;
            if (r.width < 8) return;
            float slot = r.width / Mathf.Max(1, _items.Count);
            for (int i = 0; i < _items.Count; i++)
            {
                var d = _items[i].Date ?? "";
                var l = Ui.Text(d.Length >= 7 ? d.Substring(0, 7) : d, "mchart-tick", "mono");
                l.pickingMode = PickingMode.Ignore;
                l.style.position = Position.Absolute;
                l.style.left = r.x + slot * (i + 0.5f);
                l.style.top = r.yMax + 8;
                l.style.translate = new Translate(Length.Percent(-50), 0);
                _text.Add(l);
            }
        }
    }
}
