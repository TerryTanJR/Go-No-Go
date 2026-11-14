using UnityEngine;
using UnityEngine.Rendering;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 物件啟用時換掉天空盒、霧與環境光，停用時還原。
    /// 發射場景的「太空段」用它：同一個場景裡從黃昏的發射台切到星空。
    /// </summary>
    public sealed class EnvironmentOverride : MonoBehaviour
    {
        public Material skybox;
        public bool fog;
        public Color ambientSky = new Color(0.02f, 0.025f, 0.035f);
        public Color ambientEquator = new Color(0.015f, 0.018f, 0.025f);
        public Color ambientGround = new Color(0.01f, 0.01f, 0.012f);

        Material _skybox;
        bool _fog;
        AmbientMode _mode;
        Color _sky, _eq, _gr;
        bool _saved;

        void OnEnable()
        {
            _skybox = RenderSettings.skybox;
            _fog = RenderSettings.fog;
            _mode = RenderSettings.ambientMode;
            _sky = RenderSettings.ambientSkyColor;
            _eq = RenderSettings.ambientEquatorColor;
            _gr = RenderSettings.ambientGroundColor;
            _saved = true;

            if (skybox != null) RenderSettings.skybox = skybox;
            RenderSettings.fog = fog;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
        }

        void OnDisable()
        {
            if (!_saved) return;
            RenderSettings.skybox = _skybox;
            RenderSettings.fog = _fog;
            RenderSettings.ambientMode = _mode;
            RenderSettings.ambientSkyColor = _sky;
            RenderSettings.ambientEquatorColor = _eq;
            RenderSettings.ambientGroundColor = _gr;
        }
    }
}
