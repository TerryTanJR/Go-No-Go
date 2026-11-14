using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>標記太空船上一組零件屬於哪個類別、哪個零件（滑鼠移到 UI 上的零件時用來發光提示）。</summary>
    public sealed class SatPart : MonoBehaviour
    {
        public string category;
        public string partId;
        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        float _glow;

        public Renderer[] Renderers => _renderers ??= GetComponentsInChildren<Renderer>(true);

        /// <summary>發光強度 0–1（用 MaterialPropertyBlock，不會複製材質）。</summary>
        public void SetGlow(float g)
        {
            if (Mathf.Abs(g - _glow) < 0.002f) return;
            _glow = g;
            _mpb ??= new MaterialPropertyBlock();
            var c = new Color(0.22f, 0.78f, 0.96f) * (g * 2.2f);
            foreach (var r in Renderers)
            {
                if (r == null) continue;
                r.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionId, c);
                r.SetPropertyBlock(_mpb);
            }
        }

        static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    }
}
