using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// Q 版 avatar 的程式動畫（不用 Animator）：走路時身體上下彈、手腳擺動；拿著零件時雙手往前抱；安裝時手往前伸。
    /// 由控制器每幀設定 <see cref="speed"/>、<see cref="holding"/>、<see cref="installing"/>。
    /// </summary>
    public sealed class AvatarRig : MonoBehaviour
    {
        public Transform body, head, legL, legR, armL, armR, hold;

        [Tooltip("目前水平速度（m/s）")] public float speed;
        public bool holding;
        public bool installing;
        [Tooltip("第一人稱時隱藏頭（自己看不到自己的頭）")] public bool hideHead;

        float _phase, _bodyY, _walk, _hold, _install, _idle;
        Renderer[] _headRenderers;
        bool _headHidden;

        public void Init()
        {
            _bodyY = body.localPosition.y;
            _headRenderers = head.GetComponentsInChildren<Renderer>(true);
            _idle = Random.value * 10f;
        }

        void LateUpdate()
        {
            float dt = Time.deltaTime;
            float targetWalk = Mathf.Clamp01(speed / 2.2f);
            _walk = Mathf.MoveTowards(_walk, targetWalk, dt * 5f);
            _hold = Mathf.MoveTowards(_hold, holding ? 1f : 0f, dt * 6f);
            _install = Mathf.MoveTowards(_install, installing ? 1f : 0f, dt * 6f);
            _phase += dt * Mathf.Lerp(6f, 13f, Mathf.Clamp01(speed / 5f)) * (_walk > 0.01f ? 1f : 0f);
            _idle += dt;

            float s = Mathf.Sin(_phase);
            float bob = Mathf.Abs(Mathf.Sin(_phase)) * 0.05f * _walk + Mathf.Sin(_idle * 2f) * 0.006f * (1 - _walk);
            body.localPosition = new Vector3(0, _bodyY + bob, 0);
            body.localRotation = Quaternion.Euler(4f * _walk, 0, Mathf.Sin(_phase) * 3f * _walk);

            float leg = 32f * _walk * s;
            legL.localRotation = Quaternion.Euler(leg, 0, 0);
            legR.localRotation = Quaternion.Euler(-leg, 0, 0);

            // 手：走路擺動 → 抱著零件 → 往前伸（安裝）
            float swing = 28f * _walk * s;
            float holdX = Mathf.Lerp(0, -72f, _hold);
            float reach = Mathf.Lerp(0, -95f, _install) + Mathf.Sin(_idle * 18f) * 4f * _install;
            float ax = Mathf.Lerp(Mathf.Lerp(-swing, holdX, _hold), reach, _install);
            float bx = Mathf.Lerp(Mathf.Lerp(swing, holdX, _hold), reach, _install);
            float inward = Mathf.Lerp(8f, 22f, Mathf.Max(_hold, _install));
            armL.localRotation = Quaternion.Euler(ax, 0, -inward);
            armR.localRotation = Quaternion.Euler(bx, 0, inward);

            head.localRotation = Quaternion.Euler(Mathf.Sin(_idle * 0.7f) * 3f * (1 - _walk), Mathf.Sin(_idle * 0.45f) * 8f * (1 - _walk), 0);

            if (hideHead != _headHidden)
            {
                _headHidden = hideHead;
                foreach (var r in _headRenderers)
                    if (r != null) r.shadowCastingMode = hideHead ? UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly : UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }
    }
}
