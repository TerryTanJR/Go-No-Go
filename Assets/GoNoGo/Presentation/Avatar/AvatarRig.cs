using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// Q 版 avatar 的程式動畫（不用 Animator）。上半身（手、頭、前傾）所有下半身共用；
    /// 下半身依 <see cref="lower"/> 不同：腳（交替擺動）、輪子（滾動＋前傾）、懸浮（漂浮＋噴焰）、履帶（滾輪＋微晃）、彈簧（跳）。
    /// 拿著零件時雙手往前抱；安裝時手往前伸。
    /// 由控制器每幀設定 <see cref="speed"/>、<see cref="holding"/>、<see cref="installing"/>。
    /// </summary>
    public sealed class AvatarRig : MonoBehaviour
    {
        public Transform body, head, legL, legR, armL, armR, hold;
        public int lower;
        public Transform flame, spring;
        public Transform[] spinners;
        public float spinRadius = 0.2f;

        [Tooltip("目前水平速度（m/s）")] public float speed;
        public bool holding;
        public bool installing;
        [Tooltip("第一人稱時隱藏頭（自己看不到自己的頭）")] public bool hideHead;

        const int Legs = 0, Wheels = 1, Hover = 2, Tracks = 3, Spring = 4;

        float _phase, _bodyY, _walk, _hold, _install, _idle, _spin, _fp;
        Vector3 _flameScale, _springScale;
        Renderer[] _headRenderers;
        bool _headHidden;

        public void Init()
        {
            _bodyY = body.localPosition.y;
            _headRenderers = head.GetComponentsInChildren<Renderer>(true);
            _idle = Random.value * 10f;
            if (flame != null) _flameScale = flame.localScale;
            if (spring != null) _springScale = spring.localScale;
        }

        void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>推進動畫 dt 秒（測試可以不進 Play 模式手動呼叫）。</summary>
        public void Tick(float dt)
        {
            float targetWalk = Mathf.Clamp01(speed / 2.2f);
            _walk = Mathf.MoveTowards(_walk, targetWalk, dt * 5f);
            _hold = Mathf.MoveTowards(_hold, holding ? 1f : 0f, dt * 6f);
            _install = Mathf.MoveTowards(_install, installing ? 1f : 0f, dt * 6f);
            _idle += dt;
            // 第一人稱：相機就在頸圈上方不遠，身體跳高或前傾會穿進相機近裁切面（破圖），所以把動作幅度縮小
            _fp = Mathf.MoveTowards(_fp, hideHead ? 1f : 0f, dt * 6f);
            float k = Mathf.Lerp(1f, 0.2f, _fp);      // 上下彈 / 跳 / 壓縮
            float kTilt = Mathf.Lerp(1f, 0.3f, _fp);  // 前傾 / 側擺

            float bodyDy, leanX, rollZ;
            float swing = 0f; // 手臂擺動用（腳型才有交替擺動）
            switch (lower)
            {
                case Wheels:
                case Tracks:
                {
                    // 輪子 / 履帶：身體幾乎不彈，加速時前傾，滾輪依實際移動距離轉
                    _spin += speed * dt / spinRadius * Mathf.Rad2Deg;
                    SpinAll(_spin);
                    bodyDy = Mathf.Sin(_idle * 2f) * 0.004f + (lower == Tracks ? Mathf.Sin(_idle * 23f) * 0.003f * _walk : 0f);
                    leanX = (lower == Wheels ? 9f : 4f) * _walk;
                    rollZ = Mathf.Sin(_idle * 1.3f) * 1.2f * (1 - _walk);
                    break;
                }
                case Hover:
                {
                    // 懸浮：一直輕輕漂，移動時前傾，噴焰變長、閃動
                    bodyDy = 0.05f + Mathf.Sin(_idle * 2.4f) * 0.022f;
                    leanX = 11f * _walk;
                    rollZ = Mathf.Sin(_idle * 1.1f) * 2.5f;
                    if (flame != null)
                    {
                        float f = 1f + 0.9f * _walk + Mathf.Sin(_idle * 31f) * 0.12f;
                        flame.localScale = new Vector3(_flameScale.x * (1f + 0.15f * _walk), _flameScale.y * f, _flameScale.z * (1f + 0.15f * _walk));
                    }
                    break;
                }
                case Spring:
                {
                    // 彈簧：站著慢慢壓縮回彈，移動時一蹦一蹦。sp>0 騰空（拉長），sp<0 落地壓縮；腳底始終在地上或離地 h
                    _phase += dt * Mathf.Lerp(2.6f, 8.5f, _walk);
                    float sp = Mathf.Sin(_phase);
                    float hop = Mathf.Lerp(0.015f, 0.16f, _walk) * k;
                    float squash = Mathf.Lerp(0.08f, 0.24f, _walk) * k;
                    float s = sp > 0 ? 1f + 0.08f * k * sp : 1f + squash * sp;
                    float h = sp > 0 ? sp * hop : 0f;
                    if (spring != null) spring.localScale = new Vector3(_springScale.x, _springScale.y * s, _springScale.z);
                    bodyDy = h + (s - 1f) * 0.32f * 0.89f;
                    leanX = 5f * _walk;
                    rollZ = Mathf.Sin(_phase * 0.5f) * 3f * _walk;
                    break;
                }
                default: // Legs
                {
                    _phase += dt * Mathf.Lerp(6f, 13f, Mathf.Clamp01(speed / 5f)) * (_walk > 0.01f ? 1f : 0f);
                    float s = Mathf.Sin(_phase);
                    bodyDy = Mathf.Abs(s) * 0.05f * _walk + Mathf.Sin(_idle * 2f) * 0.006f * (1 - _walk);
                    leanX = 4f * _walk;
                    rollZ = s * 3f * _walk;
                    swing = 28f * _walk * s;
                    float leg = 32f * _walk * s;
                    if (legL != null) legL.localRotation = Quaternion.Euler(leg, 0, 0);
                    if (legR != null) legR.localRotation = Quaternion.Euler(-leg, 0, 0);
                    break;
                }
            }

            // 彈簧的 bodyDy 已經帶著 k；其他下半身在這裡縮
            if (lower != Spring) bodyDy *= k;
            body.localPosition = new Vector3(0, _bodyY + bodyDy, 0);
            body.localRotation = Quaternion.Euler(leanX * kTilt, 0, rollZ * kTilt);

            // 手：（走路擺動）→ 抱著零件 → 往前伸（安裝）。手臂本來就向外張，Z 是往內收的角度
            float holdX = Mathf.Lerp(0, -72f, _hold);
            float reach = Mathf.Lerp(0, -95f, _install) + Mathf.Sin(_idle * 18f) * 4f * _install;
            float ax = Mathf.Lerp(Mathf.Lerp(-swing, holdX, _hold), reach, _install);
            float bx = Mathf.Lerp(Mathf.Lerp(swing, holdX, _hold), reach, _install);
            float inward = Mathf.Lerp(18f, 36f, Mathf.Max(_hold, _install));
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

        void SpinAll(float deg)
        {
            if (spinners == null) return;
            foreach (var t in spinners)
                if (t != null) t.localRotation = Quaternion.Euler(deg, 0, 0);
        }
    }
}
