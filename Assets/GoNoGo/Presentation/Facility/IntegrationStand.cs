using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 組裝區中央的整合台：台上的太空船就是 <see cref="SessionState.Bench"/>，裝上的零件即時出現。
    /// 拿著零件靠近時，船上對應位置出現半透明預覽；準星對著船上的零件按 C 可以拆下來。
    /// </summary>
    public sealed class IntegrationStand : Interactable
    {
        public static IntegrationStand Instance { get; private set; }

        public SatelliteRig rig;
        [Tooltip("安裝時的火花")]
        public ParticleSystem sparks;
        [Tooltip("安裝時擴散的光環")]
        public Transform ringFx;
        public float installRange = 2.6f;

        Transform _ghost;
        string _ghostFor;
        Material _ghostMat;
        float _ringT = -1;
        Renderer _ringRenderer;
        SatPart _aimed;
        int _testSeen = -1;
        float _shakeT = -1;
        Vector3 _rigPos;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (ringFx != null)
            {
                _ringRenderer = ringFx.GetComponentInChildren<Renderer>();
                ringFx.gameObject.SetActive(false);
            }
            rig.PartAdded += OnPartAdded;
            _rigPos = rig.transform.localPosition;
        }

        /// <summary>依狀態更新台上的船（FacilityDirector 在狀態改變時呼叫）。</summary>
        public void Refresh(MissionSession m, bool animate)
        {
            rig.Show(m.S.Bench, m.Data, m.BenchEval, animate);
            _ghostFor = null; // 船變了，預覽重算
            // 新的環境測試：船在台上抖一陣（振動測試），最後一圈光環（熱真空）
            int n = m.S.LastTest?.N ?? 0;
            if (_testSeen >= 0 && n > _testSeen) { _shakeT = 0; Sfx.Whoosh(); }
            _testSeen = n;
        }

        public float DistanceTo(Vector3 p)
        {
            var c = transform.position;
            return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(c.x, c.z));
        }

        /// <summary>手上拿著 partId、人在旁邊：顯示預覽（null = 不顯示）。</summary>
        public void ShowGhost(string partId, MissionSession m)
        {
            if (partId == _ghostFor) return;
            _ghostFor = partId;
            if (_ghost != null) Destroy(_ghost.gameObject);
            _ghost = null;
            if (partId == null || m == null) return;

            var part = m.Data.Part(partId);
            if (part == null) return;
            var d = m.S.Bench.Clone();
            if (part.Category == PartCategory.Instrument)
            {
                if (!d.Instruments.Contains(partId)) d.Instruments.Add(partId);
            }
            else d.Parts[part.Category] = partId;

            // 用和台上一樣的縮放、位置組出整艘船，只留這個零件那一組
            var tmp = SatelliteBuilder.Build(d, m.Data, Evaluator.EvaluateDesign(d, m.Data), rig.transform);
            var model = rig.Model;
            if (model != null)
            {
                tmp.transform.localScale = model.localScale;
                tmp.transform.position = model.position;
            }
            Transform keep = null;
            foreach (var sp in tmp.GetComponentsInChildren<SatPart>())
                if (sp.partId == partId && sp.category == part.Category) { keep = sp.transform; break; }
            if (keep == null) { Destroy(tmp); return; }
            keep.SetParent(rig.transform, true);
            Destroy(tmp);
            _ghostMat ??= MakeGhostMaterial();
            foreach (var r in keep.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = _ghostMat;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            _ghost = keep;
        }

        static Material MakeGhostMaterial()
        {
            var baseMat = SatMaterials.Load()?.accent;
            var m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            m.name = "Ghost";
            // 半透明（URP Lit：Surface Type = Transparent）
            if (m.HasProperty("_Surface"))
            {
                m.SetFloat("_Surface", 1);
                m.SetFloat("_Blend", 0);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            var c = new Color(0.3f, 0.85f, 1f, 0.35f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.2f, 0.7f, 1f) * 1.2f); }
            return m;
        }

        void OnPartAdded(SatPart p)
        {
            var b = SatelliteRig.ComputeBounds(p.gameObject);
            if (sparks != null)
            {
                sparks.transform.position = b.center;
                sparks.Emit(40);
            }
            if (ringFx != null)
            {
                ringFx.position = new Vector3(transform.position.x, ringFx.position.y, transform.position.z);
                ringFx.gameObject.SetActive(true);
                _ringT = 0;
            }
            Sfx.Install();
        }

        void Update()
        {
            if (_shakeT >= 0)
            {
                _shakeT += Time.deltaTime;
                const float shake = 2.2f;
                float k = _shakeT < shake ? Mathf.Sin(_shakeT / shake * Mathf.PI) : 0;
                rig.transform.localPosition = _rigPos + new Vector3(Mathf.Sin(_shakeT * 71f), Mathf.Sin(_shakeT * 53f + 1.3f) * 0.6f, Mathf.Sin(_shakeT * 61f + 2.1f)) * 0.02f * k;
                if (_shakeT >= shake && _shakeT - Time.deltaTime < shake && ringFx != null)
                {
                    ringFx.gameObject.SetActive(true);
                    _ringT = 0;
                }
                if (_shakeT >= shake) { rig.transform.localPosition = _rigPos; _shakeT = -1; }
            }
            if (_ghost != null)
            {
                float a = 0.25f + 0.15f * Mathf.Sin(Time.time * 6f);
                if (_ghostMat != null && _ghostMat.HasProperty("_BaseColor")) _ghostMat.SetColor("_BaseColor", new Color(0.3f, 0.85f, 1f, a));
            }
            if (_ringT >= 0 && ringFx != null)
            {
                _ringT += Time.deltaTime;
                float x = Mathf.Clamp01(_ringT / 0.9f);
                ringFx.localScale = Vector3.one * Mathf.Lerp(0.6f, 3.4f, 1 - (1 - x) * (1 - x));
                if (_ringRenderer != null && _ringRenderer.material.HasProperty("_BaseColor"))
                {
                    var c = _ringRenderer.material.GetColor("_BaseColor");
                    c.a = 1 - x;
                    _ringRenderer.material.SetColor("_BaseColor", c);
                }
                if (x >= 1) { ringFx.gameObject.SetActive(false); _ringT = -1; }
            }
        }

        // ---------------- 準星對著船上的零件 ----------------

        public SatPart Aimed => _aimed;

        public void Aim(Collider c)
        {
            var p = rig.PartOf(c);
            if (p == _aimed) return;
            if (_aimed != null) _aimed.SetGlow(0);
            _aimed = p;
            if (_aimed != null) _aimed.SetGlow(0.9f);
        }

        public override HudTarget Describe(Interactor who)
        {
            var m = GameSession.Mission;
            if (_aimed == null || string.IsNullOrEmpty(_aimed.partId)) return new HudTarget { Title = Loc.T("ui.fac.stand"), Body = Loc.T("ui.fac.stand_body") };
            var def = m.Data.Part(_aimed.partId);
            if (def == null) return null;
            var t = PartItem.Info(def);
            t.Subtitle = Loc.T("ui.fac.installed");
            if (m.S.DesignLocked(m.Data)) t.Subtitle = Loc.T("err.locked");
            else if (who.Holding == null) t.Keys.Add(new KeyHint("C", Loc.T("ui.hint.remove")));
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (key != 'C' || _aimed == null || string.IsNullOrEmpty(_aimed.partId) || who.Holding != null) return;
            GameSession.Send(CommandType.Pick, _aimed.partId);
            Sfx.Pick();
            Hud.Signal("remove");
        }

        public override void SetHighlight(bool on)
        {
            if (_aimed != null) _aimed.SetGlow(on ? 0.9f : 0f);
            if (!on && _aimed != null) _aimed = null;
        }

        public override float Range => 3.2f;
    }
}
