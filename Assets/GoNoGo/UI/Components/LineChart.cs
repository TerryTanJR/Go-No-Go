using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 營運畫面的小折線圖（Painter2D）：資料點 0–1、目前播放位置之後的部分不畫。
    /// 可以標出事件日期（直線）與警戒線。
    /// </summary>
    public sealed class LineChart : VisualElement
    {
        readonly List<Vector2> _points = new List<Vector2>(); // x: 0–1 時間、y: 0–1 數值
        readonly List<float> _markers = new List<float>();
        float _cursor = 1f;
        float _warnLine = -1f;
        Color _color = new Color(0.22f, 0.78f, 0.96f);

        static readonly Color Grid = new Color(1f, 1f, 1f, 0.06f);
        static readonly Color Warn = new Color(1f, 0.30f, 0.37f, 0.55f);
        static readonly Color Marker = new Color(1f, 0.71f, 0.28f, 0.7f);

        public LineChart()
        {
            AddToClassList("chart");
            generateVisualContent += Draw;
        }

        public void SetColor(Color c) { _color = c; MarkDirtyRepaint(); }

        public void SetData(IList<Vector2> pts)
        {
            _points.Clear();
            _points.AddRange(pts);
            MarkDirtyRepaint();
        }

        public void SetMarkers(IEnumerable<float> xs)
        {
            _markers.Clear();
            _markers.AddRange(xs);
            MarkDirtyRepaint();
        }

        public void SetWarnLine(float y) { _warnLine = y; MarkDirtyRepaint(); }

        public void SetCursor(float x)
        {
            if (Mathf.Abs(x - _cursor) < 0.0005f) return;
            _cursor = Mathf.Clamp01(x);
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            var r = contentRect;
            if (r.width < 4 || r.height < 4) return;
            var p = ctx.painter2D;
            float W = r.width, H = r.height;
            Vector2 P(float x, float y) => new Vector2(x * W, (1f - Mathf.Clamp01(y)) * (H - 2) + 1);

            // 底線格
            p.lineWidth = 1f;
            p.strokeColor = Grid;
            for (int i = 1; i < 4; i++)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(0, H * i / 4f));
                p.LineTo(new Vector2(W, H * i / 4f));
                p.Stroke();
            }

            if (_warnLine >= 0)
            {
                p.strokeColor = Warn;
                p.BeginPath();
                p.MoveTo(P(0, _warnLine));
                p.LineTo(P(1, _warnLine));
                p.Stroke();
            }

            p.strokeColor = Marker;
            foreach (var m in _markers)
            {
                if (m > _cursor) continue;
                p.BeginPath();
                p.MoveTo(new Vector2(m * W, 0));
                p.LineTo(new Vector2(m * W, H));
                p.Stroke();
            }

            if (_points.Count < 2) return;

            // 收集到目前播放位置為止的點（最後一段插值，讓線平滑地往前長）
            var vis = new List<Vector2>(_points.Count);
            for (int i = 0; i < _points.Count; i++)
            {
                var pt = _points[i];
                if (pt.x <= _cursor) { vis.Add(pt); continue; }
                if (i > 0)
                {
                    var a = _points[i - 1];
                    float t = (_cursor - a.x) / Mathf.Max(1e-6f, pt.x - a.x);
                    vis.Add(Vector2.Lerp(a, pt, t));
                }
                break;
            }
            if (vis.Count < 2) return;

            // 線下面積：用頂點色做由上往下的漸層（Painter2D 不支援漸層填色，所以自己組網格）
            var fillTop = new Color(_color.r, _color.g, _color.b, 0.28f);
            var fillBot = new Color(_color.r, _color.g, _color.b, 0.0f);
            int n = vis.Count;
            var mesh = ctx.Allocate(n * 2, (n - 1) * 6);
            for (int i = 0; i < n; i++)
            {
                var top = P(vis[i].x, vis[i].y);
                mesh.SetNextVertex(new Vertex { position = new Vector3(top.x, top.y, Vertex.nearZ), tint = fillTop });
                mesh.SetNextVertex(new Vertex { position = new Vector3(top.x, H, Vertex.nearZ), tint = fillBot });
            }
            for (int i = 0; i < n - 1; i++)
            {
                ushort a = (ushort)(i * 2), b = (ushort)(i * 2 + 1), c = (ushort)(i * 2 + 2), d = (ushort)(i * 2 + 3);
                mesh.SetNextIndex(a); mesh.SetNextIndex(c); mesh.SetNextIndex(b);
                mesh.SetNextIndex(b); mesh.SetNextIndex(c); mesh.SetNextIndex(d);
            }

            p.strokeColor = _color;
            p.lineWidth = 2f;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;
            p.BeginPath();
            p.MoveTo(P(vis[0].x, vis[0].y));
            for (int i = 1; i < n; i++) p.LineTo(P(vis[i].x, vis[i].y));
            p.Stroke();

            // 目前位置的亮點
            var last = P(vis[n - 1].x, vis[n - 1].y);
            p.fillColor = Color.white;
            p.BeginPath();
            p.Arc(last, 3f, Angle.Degrees(0f), Angle.Degrees(360f));
            p.Fill();
        }
    }
}
