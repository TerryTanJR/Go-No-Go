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
        float _yaw = 180f, _drag, _pop, _t;

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
            bool drag = mouse != null && mouse.leftButton.isPressed && mouse.position.ReadValue().x > Screen.width * 0.55f
                        && (PointerHooks.OverUI == null || !PointerHooks.OverUI(mouse.position.ReadValue()));
            // 拖曳時自由轉；放開後慢慢回到正面，左右輕輕擺
            _t += Time.deltaTime;
            if (drag) _drag += -mouse.delta.ReadValue().x * 0.4f;
            else _drag = Mathf.LerpAngle(_drag, 0f, Time.deltaTime * 1.5f);
            _yaw = 180f + Mathf.Sin(_t * 0.45f) * idleSwing + _drag;
            transform.localRotation = Quaternion.Euler(0, _yaw, 0);

            if (_rig != null)
            {
                _pop = Mathf.MoveTowards(_pop, 0, Time.deltaTime * 3f);
                float k = 1f + Mathf.Sin(_pop * Mathf.PI) * 0.08f;
                _rig.transform.localScale = Vector3.one * scale * k;
            }
        }
    }
}
