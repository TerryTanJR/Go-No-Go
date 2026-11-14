using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 採購終端的零件預覽台：在場景很下方（看不到的地方）放一個轉台、一台相機和兩盞小燈，
    /// 把選到的零件模型畫進 RenderTexture，給 UI 當背景圖。由 FacilityDirector 建立。
    /// </summary>
    public sealed class PartPreviewStage : MonoBehaviour
    {
        static readonly Vector3 StagePos = new Vector3(0, -900f, 900f);
        /// <summary>預覽專用的圖層：相機只畫這一層，場景其他東西（例如地底下營運畫面的地球）不會入鏡。</summary>
        const int Layer = 30;
        const int Size = 640;
        const float DegPerSec = 28f;

        Camera _cam;
        RenderTexture _rt;
        Transform _turntable;
        GameObject _model;
        string _shown;

        public static PartPreviewStage Create(Transform parent)
        {
            var go = new GameObject("[PartPreview]");
            go.transform.SetParent(parent, false);
            go.transform.position = StagePos;
            return go.AddComponent<PartPreviewStage>();
        }

        void Awake()
        {
            _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { name = "RT_PartPreview", antiAliasing = 4 };
            _rt.Create();

            _turntable = new GameObject("Turntable").transform;
            _turntable.SetParent(transform, false);

            var camGo = new GameObject("PreviewCamera");
            camGo.transform.SetParent(transform, false);
            _cam = camGo.AddComponent<Camera>();
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.035f, 0.05f, 0.075f, 1f);
            _cam.fieldOfView = 30f;
            _cam.nearClipPlane = 0.05f;
            _cam.farClipPlane = 30f;
            _cam.cullingMask = 1 << Layer;
            _cam.enabled = false;

            // 主光（暖）＋補光（冷），範圍小，照不到上面的設施
            Light("Key", new Vector3(1.6f, 2.2f, -2.4f), new Color(1f, 0.94f, 0.86f), 6f);
            Light("Fill", new Vector3(-2.2f, 0.6f, -1.2f), new Color(0.55f, 0.75f, 1f), 3f);

            PartPreview.Show = Show;
            PartPreview.Hide = Hide;
        }

        void Light(string name, Vector3 local, Color c, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = local;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = 8f;
            l.shadows = LightShadows.None;
        }

        Texture Show(string partId)
        {
            if (partId != _shown)
            {
                _shown = partId;
                if (_model != null) Destroy(_model);
                _model = null;
                if (!string.IsNullOrEmpty(partId) && GameSession.Data?.Part(partId) != null)
                {
                    _model = SatelliteBuilder.BuildPart(partId, GameSession.Data, _turntable, 1f);
                    foreach (var t in _model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
                    Frame();
                }
            }
            _cam.enabled = true;
            return _rt;
        }

        /// <summary>把模型放在轉台中心，相機退到看得到整個零件的距離。</summary>
        void Frame()
        {
            _turntable.localRotation = Quaternion.Euler(0, 30f, 0);
            var b = SatelliteRig.ComputeBounds(_model);
            _model.transform.position += _turntable.position - b.center;
            float radius = Mathf.Max(0.05f, b.extents.magnitude);
            float dist = radius / Mathf.Sin(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
            var dir = new Vector3(0, -0.22f, -1f).normalized; // 稍微從下面看：儀器的鏡頭朝下（對地）
            _cam.transform.position = _turntable.position + dir * dist;
            _cam.transform.LookAt(_turntable.position);
            _cam.farClipPlane = dist * 3f;
        }

        void Hide()
        {
            if (_cam != null) _cam.enabled = false;
        }

        void Update()
        {
            if (_cam != null && _cam.enabled) _turntable.Rotate(0, DegPerSec * Time.unscaledDeltaTime, 0, Space.Self);
        }

        void OnDestroy()
        {
            if (PartPreview.Show == (System.Func<string, Texture>)Show) PartPreview.Show = null;
            if (PartPreview.Hide == (System.Action)Hide) PartPreview.Hide = null;
            if (_rt != null) { _rt.Release(); Destroy(_rt); }
        }
    }
}
