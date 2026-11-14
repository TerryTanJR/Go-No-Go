using System;
using System.Collections.Generic;
using GoNoGo.Core;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 展示用的太空船：給它一個設計就重組模型，新裝上的零件「彈」進來（0.45 秒）。
    /// 每個零件都有網格碰撞體，準星可以對到船上的某個零件（拆下用）。
    /// </summary>
    public sealed class SatelliteRig : MonoBehaviour
    {
        [Tooltip("模型最長邊的目標尺寸（公尺），太空船再大也會縮到這個範圍內好取景")]
        public float maxSize = 3.2f;
        [Tooltip("固定放大倍率（整合台上用 2：真實尺寸的太空船對 Q 版 avatar 來說太小）；超過 maxSize 才縮小")]
        public float fixedScale = 1f;
        [Tooltip("模型底部對齊這個物件（放在台座上）；否則中心對齊")]
        public bool bottomAnchor;
        [Tooltip("加上碰撞體，讓準星對得到零件")]
        public bool pickable;

        GameObject _model;
        readonly HashSet<string> _lastParts = new HashSet<string>();
        readonly List<(Transform t, float t0)> _pops = new List<(Transform, float)>();
        SatPart[] _parts = new SatPart[0];
        int _shownHash;
        bool _hasShown;

        public Bounds ModelBounds { get; private set; }
        public Transform Model => _model != null ? _model.transform : null;
        public IReadOnlyList<SatPart> Parts => _parts;

        /// <summary>新裝上的零件（世界座標中心），安裝特效用。</summary>
        public event Action<SatPart> PartAdded;

        public void Show(Design design, GameData data, Evaluation ev, bool animate)
        {
            int h = design.StableHash();
            if (_hasShown && h == _shownHash) return;
            _shownHash = h;
            bool first = !_hasShown;
            _hasShown = true;

            if (_model != null) Destroy(_model);
            _model = SatelliteBuilder.Build(design, data, ev, transform);
            _parts = _model.GetComponentsInChildren<SatPart>(true);

            FitModel(_model);
            ModelBounds = ComputeBounds(_model);

            if (pickable)
                foreach (var p in _parts)
                {
                    foreach (var mf in p.GetComponentsInChildren<MeshFilter>())
                    {
                        var mc = mf.gameObject.AddComponent<MeshCollider>();
                        mc.sharedMesh = mf.sharedMesh;
                    }
                    AddPickProxy(p);
                }

            _pops.Clear();
            var now = new HashSet<string>();
            foreach (var p in _parts)
            {
                string key = p.category + "#" + p.partId;
                now.Add(key);
                if (animate && !first && !_lastParts.Contains(key) && p.Renderers.Length > 0)
                {
                    // 先通知（安裝特效要用零件完整大小時的位置），再縮小開始彈入；
                    // 順序反過來的話零件已經縮成一個點，火花會出現在船中心
                    PartAdded?.Invoke(p);
                    p.transform.localScale = Vector3.one * 0.01f;
                    _pops.Add((p.transform, Time.time));
                }
            }
            _lastParts.Clear();
            _lastParts.UnionWith(now);
        }

        /// <summary>準星對得到的最小尺寸（公尺）：比這個小的零件另外加一個放大的感應框。</summary>
        const float MinPick = 0.3f;
        /// <summary>最長邊超過這個尺寸（公尺）的零件不加感應框。</summary>
        const float MaxProxy = 0.7f;

        /// <summary>
        /// 小零件（薄板、小感測器）的網格碰撞體只有幾公分，準星很難對到：再加一個至少 <see cref="MinPick"/> 見方的感應框。
        /// 用 trigger，不會擋到角色，也不會擋住「中間有沒有隔著東西」的判斷；船身結構（沒有零件 id）不加。
        /// </summary>
        static void AddPickProxy(SatPart p)
        {
            if (string.IsNullOrEmpty(p.partId)) return;
            // 在零件那一組的局部座標裡量大小（整合台會轉、船會縮放，世界座標的外框不準）
            var t = p.transform;
            bool any = false;
            Bounds b = default;
            foreach (var mf in p.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var toLocal = t.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var q = toLocal.MultiplyPoint3x4(corner);
                    if (!any) { b = new Bounds(q, Vector3.zero); any = true; }
                    else b.Encapsulate(q);
                }
            }
            if (!any) return;
            float k = Mathf.Max(1e-4f, t.lossyScale.x);
            float min = MinPick / k;
            if (Mathf.Min(b.size.x, Mathf.Min(b.size.y, b.size.z)) >= min) return;
            // 只幫真的很小的零件加：大而薄的（太陽能翼、雷達天線）本來就好對，
            // 把它們加厚成一大片感應框反而會擋住後面的電池、電腦和旁邊的儀器
            if (Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) > MaxProxy / k) return;
            var box = p.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = b.center;
            box.size = Vector3.Max(b.size, Vector3.one * min);
        }

        /// <summary>
        /// 縮放到展示尺寸，並讓模型中心（或底部）對齊這個物件。台上的船和安裝預覽共用這一段，
        /// 預覽才會和裝上去之後的大小、位置一模一樣。
        /// </summary>
        public void FitModel(GameObject model)
        {
            var b = ComputeBounds(model);
            float longest = Mathf.Max(b.size.x, b.size.y, b.size.z);
            float k = longest * fixedScale > maxSize ? maxSize / longest : fixedScale;
            model.transform.localScale = Vector3.one * k;
            b = ComputeBounds(model);
            var anchor = bottomAnchor ? new Vector3(b.center.x, b.min.y, b.center.z) : b.center;
            model.transform.position += transform.position - anchor;
        }

        public static Bounds ComputeBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.3f);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        public SatPart PartOf(Collider c) => c != null ? c.GetComponentInParent<SatPart>() : null;

        public void Glow(string category, string partId, float g)
        {
            foreach (var p in _parts) p.SetGlow(p.category == category && (partId == null || p.partId == partId) ? g : 0f);
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
        }
    }
}
