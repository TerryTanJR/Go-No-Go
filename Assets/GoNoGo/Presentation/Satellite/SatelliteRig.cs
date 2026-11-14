using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 設計室裡的太空船：設計一改就重組模型，換掉的零件會「彈」進來；
    /// 滑鼠移到 UI 上某個零件時，太空船上對應的部位發光。
    /// </summary>
    public sealed class SatelliteRig : MonoBehaviour
    {
        [Tooltip("模型最長邊的目標尺寸（公尺），太空船再大也會縮到這個範圍內好取景")]
        public float maxSize = 3.2f;
        public bool reactToHover = true;

        GameObject _model;
        readonly Dictionary<string, string> _lastParts = new Dictionary<string, string>();
        readonly List<(Transform t, float t0)> _pops = new List<(Transform, float)>();
        SatPart[] _parts = new SatPart[0];

        public Bounds ModelBounds { get; private set; }

        void OnEnable()
        {
            GameSession.EnsureScenario();
            GameSession.State.DesignChanged += OnDesign;
            Rebuild(false);
        }

        void OnDisable()
        {
            if (GameSession.Exists) GameSession.State.DesignChanged -= OnDesign;
        }

        void OnDesign(Evaluation ev) => Rebuild(true);

        public void Rebuild(bool animate)
        {
            var s = GameSession.State;
            if (_model != null) Destroy(_model);
            _model = SatelliteBuilder.Build(s.Design, s.Data, s.Evaluation, transform);
            _parts = _model.GetComponentsInChildren<SatPart>(true);

            // 縮放到展示尺寸，並讓模型中心對齊這個物件
            var b = ComputeBounds(_model);
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            float k = longest > maxSize ? maxSize / longest : 1f;
            _model.transform.localScale = Vector3.one * k;
            b = ComputeBounds(_model);
            _model.transform.position += transform.position - b.center;
            ModelBounds = ComputeBounds(_model);

            _pops.Clear();
            foreach (var p in _parts)
            {
                bool changed = !_lastParts.TryGetValue(p.category + "#" + p.partId, out _);
                if (animate && changed && p.Renderers.Length > 0)
                {
                    p.transform.localScale = Vector3.one * 0.01f;
                    _pops.Add((p.transform, Time.time));
                }
            }
            _lastParts.Clear();
            foreach (var p in _parts) _lastParts[p.category + "#" + p.partId] = p.partId;
        }

        static Bounds ComputeBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        void Update()
        {
            // 彈入動畫：0.45 秒，稍微超過再回來
            for (int i = _pops.Count - 1; i >= 0; i--)
            {
                var (t, t0) = _pops[i];
                if (t == null) { _pops.RemoveAt(i); continue; }
                float x = Mathf.Clamp01((Time.time - t0) / 0.45f);
                float e = 1f + 2.70158f * Mathf.Pow(x - 1, 3) + 1.70158f * Mathf.Pow(x - 1, 2); // easeOutBack
                t.localScale = Vector3.one * Mathf.Max(0.01f, e);
                if (x >= 1) { t.localScale = Vector3.one; _pops.RemoveAt(i); }
            }

            if (!reactToHover) return;
            var s = GameSession.State;
            string cat = s.HoverCategory;
            string id = s.HoverPartId;
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * 6f);
            foreach (var p in _parts)
            {
                bool hit = cat != null && p.category == cat && (cat != PartCategory.Instrument || p.partId == id || !s.Design.Instruments.Contains(id));
                p.SetGlow(hit ? pulse : 0f);
            }
        }
    }
}
