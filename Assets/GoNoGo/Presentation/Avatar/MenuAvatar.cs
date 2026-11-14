using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 主選單右邊的自己：顯示 <see cref="Profile.Shown"/>（編輯時即時預覽），慢慢轉；滑鼠在右半邊拖曳可以轉身。
    /// 換外型時跳一下。
    /// </summary>
    public sealed class MenuAvatar : MonoBehaviour
    {
        public float scale = 1.6f;
        [Tooltip("沒拖曳時左右擺動的幅度（度）；正面朝鏡頭")]
        public float idleSwing = 28f;

        AvatarRig _rig;
        float _yaw = 180f, _drag, _vel, _swing = 1f, _pop, _t;
        bool _dragging;

        void OnEnable()
        {
            Profile.PreviewChanged += Rebuild;
            Profile.Changed += Rebuild;
            Rebuild();
        }

        void OnDisable()
        {
            Profile.PreviewChanged -= Rebuild;
            Profile.Changed -= Rebuild;
        }

        void Rebuild()
        {
            if (_rig != null) Destroy(_rig.gameObject);
            _rig = AvatarBuilder.Build(Profile.Shown, transform);
            _rig.transform.localScale = Vector3.one * scale;
            _pop = 1f;
        }

        void Update()
        {
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;
            // 按下時就要在右半邊且不在 UI 上；之後不管滑鼠拖到哪都算拖曳，直到放開
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var p = mouse.position.ReadValue();
                _dragging = p.x > Screen.width * 0.55f && (PointerHooks.OverUI == null || !PointerHooks.OverUI(p));
                _vel = 0f;
            }
            if (mouse == null || !mouse.leftButton.isPressed) _dragging = false;

            _t += dt;
            if (_dragging)
            {
                float d = -mouse.delta.ReadValue().x * 0.35f;
                _drag += d;
                _vel = Mathf.Lerp(_vel, d / Mathf.Max(dt, 1e-4f), 1f - Mathf.Exp(-20f * dt));
            }
            else
            {
                // 放開：先帶一點慣性，再順順地回正面（用最短方向，不會瞬間跳）
                _drag += _vel * dt;
                _vel *= Mathf.Exp(-5f * dt);
                if (Mathf.Abs(_vel) < 5f)
                {
                    _drag = Mathf.Repeat(_drag + 180f, 360f) - 180f;
                    _drag = Mathf.Lerp(_drag, 0f, 1f - Mathf.Exp(-2.5f * dt));
                }
            }
            // 拖曳時暫停擺動（不然會跟手打架），放開後慢慢恢復
            _swing = Mathf.Lerp(_swing, _dragging ? 0f : 1f, 1f - Mathf.Exp(-4f * dt));
            _yaw = 180f + Mathf.Sin(_t * 0.45f) * idleSwing * _swing + _drag;
            transform.localRotation = Quaternion.Euler(0, _yaw, 0);

            if (_rig != null)
            {
                _pop = Mathf.MoveTowards(_pop, 0, dt * 3f);
                float k = 1f + Mathf.Sin(_pop * Mathf.PI) * 0.08f;
                _rig.transform.localScale = Vector3.one * scale * k;
            }
        }
    }
}
