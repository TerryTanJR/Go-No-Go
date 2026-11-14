using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 手冊的散布圖（Painter2D）：每個零件一個點，橫軸成本、縱軸關鍵數值，一眼看出哪個划算。
    /// 文字（刻度、代號、軸名）用絕對定位的 Label 疊在上面，版面變動時重新排。數值差距很大的軸用對數刻度。
    /// </summary>
    public sealed class ScatterChart : VisualElement
    {
        public struct Point
        {
            public float X, Y;
            public string Label;
            public bool On;
        }

        const float PadR = 28, PadT = 44, PadB = 58;
        // 點和軸線之間留一點空：最小值的點不會壓在 X 軸、Y 軸上
        const float Inset = 0.07f;
        // 最大值是最小值的這麼多倍以上就改用對數刻度（例如天線 10 Mbps 到 10 Gbps），不然小的全擠在軸上
        const float LogRatio = 30;

        readonly List<Point> _pts = new List<Point>();
        readonly VisualElement _text;
        readonly string _xTitle, _yTitle;
        readonly Func<float, string> _xFmt, _yFmt;
                readonly Color _color;
        float _xMax = 1, _yMax = 1;
        float _xMin = float.MaxValue, _yMin = float.MaxValue;
        float _padL = 70;

        static readonly Color Grid = new Color(1f, 1f, 1f, 0.07f);
        static readonly Color Axis = new Color(1f, 1f, 1f, 0.28f);
        static readonly Color On = new Color(0.24f, 0.86f, 0.52f);
        // 已安裝的點外面那一圈用白色：航電章本身就是綠色，用綠圈分不出來
        static readonly Color OnRing = new Color(1f, 1f, 1f, 0.9f);

        public ScatterChart(string xTitle, string yTitle, Func<float, string> xFmt, Func<float, string> yFmt, Color color)
        {
            _xTitle = xTitle;
            _yTitle = yTitle;
            _xFmt = xFmt;
            _yFmt = yFmt;
            _color = color;
            AddToClassList("mchart");
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            _text = new VisualElement { pickingMode = PickingMode.Ignore };
            _text.AddToClassList("mchart-text");
            Add(_text);
            RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        public void AddPoint(float x, float y, string label, bool on)
        {
            _pts.Add(new Point { X = x, Y = y, Label = label, On = on });
            _xMax = Mathf.Max(_xMax, x);
            _yMax = Mathf.Max(_yMax, y);
            if (x > 0) _xMin = Mathf.Min(_xMin, x);
            if (y > 0) _yMin = Mathf.Min(_yMin, y);
            MarkDirtyRepaint();
        }

        Rect Plot => new Rect(_padL, PadT, Mathf.Max(1, contentRect.width - _padL - PadR), Mathf.Max(1, contentRect.height - PadT - PadB));
        bool LogX => _xMin < float.MaxValue && _xMax / _xMin > LogRatio;
        bool LogY => _yMin < float.MaxValue && _yMax / _yMin > LogRatio;

        Vector2 P(float x, float y)
        {
            var r = Plot;
            float fx = Inset + (1 - Inset) * Frac(x, _xMin, _xMax, LogX, 1.12f);
            float fy = Inset + (1 - Inset) * Frac(y, _yMin, _yMax, LogY, 1.15f);
            return new Vector2(r.x + fx * r.width, r.yMax - fy * r.height);
        }

        /// <summary>數值在軸上的位置 0–1。線性從 0 開始；對數從最小值那個 10 的次方開始。</summary>
        static float Frac(float v, float min, float max, bool log, float headroom)
        {
            if (!log) return v / (max * headroom);
            float lo = Mathf.Floor(Mathf.Log10(min)), hi = Mathf.Ceil(Mathf.Log10(max));
            if (hi <= lo) hi = lo + 1;
            return (Mathf.Log10(Mathf.Max(v, Mathf.Pow(10, lo))) - lo) / (hi - lo);
        }

        /// <summary>刻度：線性是 0 到頂端分四格；對數是每個 10 的次方。</summary>
        static List<float> Ticks(float min, float max, bool log, float headroom)
        {
            var t = new List<float>();
            if (!log)
            {
                for (int i = 0; i <= 4; i++) t.Add(max * headroom * i / 4f);
                return t;
            }
            int lo = Mathf.FloorToInt(Mathf.Log10(min)), hi = Mathf.CeilToInt(Mathf.Log10(max));
            if (hi <= lo) hi = lo + 1;
            for (int e = lo; e <= hi; e++) t.Add(Mathf.Pow(10, e));
            return t;
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = Plot;
            if (r.width < 8 || r.height < 8) return;
            var p = ctx.painter2D;

            p.lineWidth = 1;
            p.strokeColor = Grid;
            foreach (var v in Ticks(_yMin, _yMax, LogY, 1.15f))
            {
                float gy = P(0, v).y;
                p.BeginPath(); p.MoveTo(new Vector2(r.x, gy)); p.LineTo(new Vector2(r.xMax, gy)); p.Stroke();
            }
            foreach (var v in Ticks(_xMin, _xMax, LogX, 1.12f))
            {
                float gx = P(v, 0).x;
                p.BeginPath(); p.MoveTo(new Vector2(gx, r.y)); p.LineTo(new Vector2(gx, r.yMax)); p.Stroke();
            }
            p.strokeColor = Axis;
            p.lineWidth = 2;
            p.BeginPath(); p.MoveTo(new Vector2(r.x, r.y)); p.LineTo(new Vector2(r.x, r.yMax)); p.LineTo(new Vector2(r.xMax, r.yMax)); p.Stroke();

            foreach (var pt in _pts)
            {
                var c = P(pt.X, pt.Y);
                if (pt.On)
                {
                    p.strokeColor = OnRing;
                    p.lineWidth = 3;
                    p.BeginPath(); p.Arc(c, 13, 0, 360); p.Stroke();
                }
                p.fillColor = pt.On ? On : _color;
                p.BeginPath(); p.Arc(c, 8, 0, 360); p.Fill();
            }
        }

        void Layout()
        {
            _text.Clear();
            // 左邊留白照最長的縱軸刻度字算（等寬字約 9px 一個字），改了就重畫
            var yTicks = Ticks(_yMin, _yMax, LogY, 1.15f);
            float pad = 24 + yTicks.Max(v => _yFmt(v).Length) * 11f;
            if (Mathf.Abs(pad - _padL) > 1) { _padL = pad; MarkDirtyRepaint(); }
            var r = Plot;
            if (r.width < 8) return;
            foreach (var v in yTicks)
                Place(Text(_yFmt(v), "mchart-tick", "mono"), new Vector2(r.x - 8, P(0, v).y), new Vector2(-100, -50));
            foreach (var v in Ticks(_xMin, _xMax, LogX, 1.12f))
                Place(Text(_xFmt(v), "mchart-tick", "mono"), new Vector2(P(v, 0).x, r.yMax + 8), new Vector2(-50, 0));
            // 軸名先放，代號避開它們
            const float Cjk = 19;
            Place(Text(_yTitle, "mchart-axis"), new Vector2(r.x - 8, r.y - 16), new Vector2(0, -100));
            Place(Text(_xTitle, "mchart-axis"), new Vector2(r.xMax, r.yMax - 6), new Vector2(-100, -100));
            var taken = new List<Rect>
            {
                new Rect(r.xMax - _xTitle.Length * Cjk, r.yMax - 30, _xTitle.Length * Cjk, 24),
            };
            // 代號先放右上；擠在一起時換個方向（右、左上、上、左、下），都撞到就放回右上
            foreach (var pt in _pts)
            {
                var c = P(pt.X, pt.Y);
                var size = new Vector2(pt.Label.Length * 12 + 4, 26);
                Rect pick = default;
                bool found = false;
                foreach (var off in LabelSpots(size))
                {
                    var rc = new Rect(c + off, size);
                    if (taken.Exists(t => t.Overlaps(rc))) continue;
                    pick = rc;
                    found = true;
                    break;
                }
                if (!found) pick = new Rect(c + new Vector2(10, -6 - size.y), size);
                taken.Add(pick);
                Place(Text(pt.Label, "mchart-pt", pt.On ? "mchart-pt--on" : null), pick.position, Vector2.zero);
            }
        }

        static IEnumerable<Vector2> LabelSpots(Vector2 size)
        {
            yield return new Vector2(10, -6 - size.y);
            yield return new Vector2(14, -size.y / 2);
            yield return new Vector2(-10 - size.x, -6 - size.y);
            yield return new Vector2(-size.x / 2, -14 - size.y);
            yield return new Vector2(-14 - size.x, -size.y / 2);
            yield return new Vector2(-size.x / 2, 12);
        }

        Label Text(string s, params string[] cls)
        {
            var l = Ui.Text(s, cls);
            l.pickingMode = PickingMode.Ignore;
            _text.Add(l);
            return l;
        }

        /// <param name="anchor">以百分比 translate 對齊（-100 = 靠右／靠下）。</param>
        static void Place(VisualElement e, Vector2 at, Vector2 anchor)
        {
            e.style.position = Position.Absolute;
            e.style.left = at.x;
            e.style.top = at.y;
            e.style.translate = new Translate(Length.Percent(anchor.x), Length.Percent(anchor.y));
        }
    }

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
        float Slot => Plot.width / Mathf.Max(1, _items.Count);

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
            float w = Mathf.Min(44, Slot * 0.6f);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                float h = r.height * Mathf.Clamp(it.Severity, 1, 3) / 3f;
                float x = r.x + Slot * (i + 0.5f) - w / 2;
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
            for (int i = 0; i < _items.Count; i++)
            {
                var d = _items[i].Date ?? "";
                var l = Ui.Text(d.Length >= 7 ? d.Substring(0, 7) : d, "mchart-tick", "mono");
                l.pickingMode = PickingMode.Ignore;
                l.style.position = Position.Absolute;
                l.style.left = r.x + Slot * (i + 0.5f);
                l.style.top = r.yMax + 8;
                l.style.translate = new Translate(Length.Percent(-50), 0);
                _text.Add(l);
            }
        }
    }
}
