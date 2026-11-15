using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 組裝區中央的整合台：台上的衛星只有已經組好裝上去的零件（<see cref="SessionState.Installed"/>），裝上的零件即時出現。
    /// 拿著組好的零件靠近時，衛星上對應位置出現半透明預覽，長按 V 裝上去。
    /// 零件不用手拆：在中控室的設計主螢幕改選，換掉的零件會自動從衛星拆下。
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

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

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
        }

        /// <summary>依狀態更新台上的船（FacilityDirector 在狀態改變時呼叫）。</summary>
        public void Refresh(MissionSession m, bool animate)
        {
            // 船上的零件沒變（改的是別的狀態）：船沒有重組，預覽和碰撞體清單都還能用
            if (!rig.Show(m.Satellite, m.Data, m.SatelliteEval, animate)) return;
            _ghostFor = null; // 船變了，預覽重算
            // 船整艘重組了：舊零件的碰撞體都換掉了，準星吸附（Interactor.Assist）用的清單要重新收集
            InvalidateColliders();
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
            var d = m.Satellite.Clone();
            if (part.Category == PartCategory.Instrument)
            {
                if (!d.Instruments.Contains(partId)) d.Instruments.Add(partId);
            }
            else d.Parts[part.Category] = partId;

            // 組出裝上之後的整艘船，用和台上同一套縮放與對位（FitModel），只留這個零件那一組：
            // 預覽就是零件裝上去之後會在的位置與大小（船身會依質量變大、整艘再縮進展示範圍，不能沿用台上現在的縮放）
            var tmp = SatelliteBuilder.Build(d, m.Data, Evaluator.EvaluateDesign(d, m.Data, null, false), rig.transform);
            rig.FitModel(tmp);
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
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(0.2f, 0.7f, 1f) * 0.6f); }
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
            if (_ghost != null)
            {
                float a = 0.25f + 0.15f * Mathf.Sin(Time.time * 6f);
                if (_ghostMat != null && _ghostMat.HasProperty(BaseColorId)) _ghostMat.SetColor(BaseColorId, new Color(0.3f, 0.85f, 1f, a));
            }
            if (_ringT >= 0 && ringFx != null)
            {
                _ringT += Time.deltaTime;
                float x = Mathf.Clamp01(_ringT / 0.9f);
                ringFx.localScale = Vector3.one * Mathf.Lerp(0.6f, 3.4f, 1 - (1 - x) * (1 - x));
                if (_ringRenderer != null)
                {
                    var ringMat = _ringRenderer.material;
                    if (ringMat.HasProperty(BaseColorId))
                    {
                        var c = ringMat.GetColor(BaseColorId);
                        c.a = 1 - x;
                        ringMat.SetColor(BaseColorId, c);
                    }
                }
                if (x >= 1) { ringFx.gameObject.SetActive(false); _ringT = -1; }
            }
        }

        // ---------------- 準星對著船上的零件 ----------------

        /// <summary>準星對到船上的哪個碰撞體（null = 沒對到）：那個零件發光，資訊框顯示它。</summary>
        public void Aim(Collider c)
        {
            var p = rig.PartOf(c);
            if (p != null && string.IsNullOrEmpty(p.partId)) p = null;   // 船身不是零件，拆不下來，不發光
            if (p == _aimed) return;
            if (_aimed != null) _aimed.SetGlow(0);
            _aimed = p;
            if (_aimed != null) _aimed.SetGlow(0.9f);
        }

        public override HudTarget Describe(Interactor who)
        {
            var m = GameSession.Mission;
            if (_aimed == null || string.IsNullOrEmpty(_aimed.partId))
            {
                // 還缺幾個零件沒裝上（設計確定之後才算）
                int left = m.S.Confirmed ? SessionRules.NotInstalled(m.S, m.Data).Count : 0;
                return new HudTarget
                {
                    Title = Loc.T("ui.fac.stand"),
                    Subtitle = left > 0 ? Loc.T("ui.fac.stand_left", ("n", left)) : "",
                    Body = Loc.T("ui.fac.stand_body"),
                };
            }
            var def = m.Data.Part(_aimed.partId);
            if (def == null) return null;
            var t = PartHud.Info(def);
            t.Subtitle = Loc.T("ui.fac.installed");
            return t;
        }

        public override void SetHighlight(bool on)
        {
            if (_aimed != null) _aimed.SetGlow(on ? 0.9f : 0f);
            if (!on && _aimed != null) _aimed = null;
        }

        public override float Range => 3.2f;
    }
}
