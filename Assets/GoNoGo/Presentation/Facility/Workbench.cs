using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 一座組裝台（共兩座，不分配，誰都能用，可以同時組兩個零件）。分左右兩側：
    /// 左側六格（<see cref="BenchSlot"/>）放從架子拿來的小零件，系統比對這個組合認配方；
    /// 右側的組裝墊（<see cref="BenchJig"/>）照提示的順序一個一個裝上去，組好的成品留在墊上等人拿。
    /// 這個元件只負責把狀態畫出來：右側已經裝好的小零件、現在要裝的那一格、成品。
    /// </summary>
    public sealed class Workbench : MonoBehaviour
    {
        public static readonly List<Workbench> All = new List<Workbench>();

        [Tooltip("第幾座（0 或 1），對應 SessionState.Benches")]
        public int index;
        [Tooltip("右側組裝墊的中心（裝好的小零件和成品擺在這裡）")]
        public Transform jig;
        [Tooltip("左側的格子")]
        public BenchSlot[] slots;
        [Tooltip("裝對時的火花")]
        public ParticleSystem sparks;

        Transform _models;
        string _key;
        MaterialPropertyBlock _mpb;
        float _pulse;
        Renderer _nowPad;
        int _fitsSeen = -1, _errorsSeen = -1;
        string _fitsPart;

        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        void Awake() => All.Add(this);
        void OnDestroy() => All.Remove(this);

        /// <summary>依狀態更新台上的東西（FacilityDirector 在狀態改變時呼叫）；有人裝了一個小零件、或左側放錯組合就播效果。</summary>
        public void Sync(MissionSession m)
        {
            if (slots != null)
                foreach (var sl in slots) if (sl != null) sl.Sync(m);
            SyncJig(m);
            Effects(m);
        }

        void SyncJig(MissionSession m)
        {
            var job = m.S.BenchAt(index)?.Job;
            string key = job == null ? "" : job.PartId + "/" + job.Step + "/" + job.Done;
            if (key == _key) return;
            _key = key;
            if (_models != null) Destroy(_models.gameObject);
            _models = null;
            _nowPad = null;
            if (job == null || jig == null) return;
            var part = m.Data.Part(job.PartId);
            if (part == null) return;

            _models = new GameObject("BenchModels").transform;
            _models.SetParent(jig, false);
            var mats = SatMaterials.Load();
            if (job.Done)
            {
                // 成品：放在組裝墊中央
                var built = SatelliteBuilder.BuildPart(job.PartId, m.Data, _models, 0.5f);
                LayFlat(built);
                Seat(built, 0.02f);
                return;
            }
            // 組裝墊上一格一個小零件的位置：裝好的放上模型，現在這一格亮著
            var recipe = m.Data.Recipe(part);
            int n = recipe != null ? recipe.Components.Count : 0;
            for (int i = 0; i < n; i++)
            {
                float x = (i - (n - 1) / 2f) * 0.2f;
                var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(pad.GetComponent<Collider>());
                pad.name = "Pad" + i;
                pad.transform.SetParent(_models, false);
                pad.transform.localPosition = new Vector3(x, 0.006f, 0);
                pad.transform.localScale = new Vector3(0.17f, 0.008f, 0.17f);
                var r = pad.GetComponent<Renderer>();
                if (mats != null) r.sharedMaterial = i < job.Step ? mats.metal : mats.frameDark;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                if (i < job.Step)
                {
                    var c = SatelliteBuilder.BuildComponent(recipe.Components[i].Id, _models, 0.15f);
                    c.transform.localPosition = new Vector3(x, 0.012f, 0);
                }
                else if (i == job.Step)
                {
                    if (mats != null) r.sharedMaterial = mats.accent;
                    _nowPad = r;
                }
            }
        }

        /// <summary>有人裝了一個小零件（對：火花和音效；錯：警示音）、左側放了一組認不出來的組合（警示音）。第一次看到不補播。</summary>
        void Effects(MissionSession m)
        {
            var b = m.S.BenchAt(index);
            if (b == null) return;
            var job = b.Job;
            if (job == null) { _fitsSeen = -1; _fitsPart = null; }
            else
            {
                if (_fitsPart != job.PartId) { _fitsPart = job.PartId; _fitsSeen = job.Fits == 0 ? 0 : -1; }
                if (_fitsSeen >= 0 && job.Fits > _fitsSeen)
                {
                    FitFx(job.LastOk);
                    if (job.LastOk) Sfx.Install(); else Sfx.NoGo();
                }
                _fitsSeen = job.Fits;
            }
            if (_errorsSeen >= 0 && b.Errors > _errorsSeen) Sfx.NoGo();
            _errorsSeen = b.Errors;
        }

        /// <summary>
        /// 薄板狀的零件（電池、電腦板、太陽能板…）立著放時從正面看只剩一條線：讓最薄的那一軸朝上，平躺在組裝墊上。
        /// </summary>
        void LayFlat(GameObject model)
        {
            var b = SatelliteRig.ComputeBounds(model);
            var sz = jig.InverseTransformVector(b.size);
            sz = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
            float thin = Mathf.Min(sz.x, sz.y, sz.z), big = Mathf.Max(sz.x, sz.y, sz.z);
            if (thin >= big * 0.45f || thin == sz.y) return;
            var turn = thin == sz.x ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
            model.transform.localRotation = turn * model.transform.localRotation;
            // 轉完之後中心會跑掉：移回組裝墊中央
            var nb = SatelliteRig.ComputeBounds(model);
            var off = jig.position - nb.center;
            model.transform.position += new Vector3(off.x, 0, off.z);
        }

        /// <summary>底部貼在組裝墊上。</summary>
        void Seat(GameObject model, float lift)
        {
            var b = SatelliteRig.ComputeBounds(model);
            model.transform.position += new Vector3(0, jig.position.y - b.min.y + lift, 0);
        }

        /// <summary>裝上一個小零件的效果（對：火花；錯：不放）。</summary>
        public void FitFx(bool ok)
        {
            if (ok && sparks != null && jig != null)
            {
                sparks.transform.position = jig.position + Vector3.up * 0.1f;
                sparks.Emit(24);
            }
        }

        void Update()
        {
            // 現在要裝的那一格：一明一暗地閃
            if (_nowPad == null) return;
            _pulse += Time.deltaTime * 4f;
            _mpb ??= new MaterialPropertyBlock();
            float k = 0.5f + 0.5f * Mathf.Sin(_pulse);
            _mpb.SetColor(BaseColorId, Color.Lerp(new Color(0.1f, 0.35f, 0.45f), new Color(0.3f, 0.9f, 1f), k));
            _mpb.SetColor(EmissionColorId, new Color(0.22f, 0.78f, 0.96f) * (0.4f + 1.6f * k));
            _nowPad.SetPropertyBlock(_mpb);
        }
    }
}
