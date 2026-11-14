using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 繞著目標轉的鏡頭：左鍵拖曳旋轉、滾輪縮放（在 UI 上不作用），閒置時自己慢慢轉。
    /// 鏡頭稍微往左偏，讓太空船在左右兩個 UI 面板之間置中。
    /// </summary>
    public sealed class OrbitCameraRig : MonoBehaviour
    {
        public Transform target;
        public SatelliteRig satellite;
        public float yaw = 35f, pitch = 14f;
        public float distance = 7.5f;
        public float minDistance = 3.5f, maxDistance = 14f;
        public float idleSpin = 6f;
        public Vector3 lookOffset = Vector3.zero;

        float _yawVel, _pitchVel;
        float _targetDistance;
        float _idle;
        bool _dragging;

        void Start()
        {
            _targetDistance = distance;
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            float dt = Time.unscaledDeltaTime;
            if (mouse != null)
            {
                var pos = mouse.position.ReadValue();
                bool overUI = PointerHooks.IsOverUI(pos);
                if (mouse.leftButton.wasPressedThisFrame && !overUI) _dragging = true;
                if (!mouse.leftButton.isPressed) _dragging = false;
                if (_dragging)
                {
                    var d = mouse.delta.ReadValue();
                    _yawVel = d.x * 0.25f / Mathf.Max(dt, 1e-3f) * 0.016f;
                    _pitchVel = -d.y * 0.2f / Mathf.Max(dt, 1e-3f) * 0.016f;
                    _idle = 0;
                }
                float scroll = mouse.scroll.ReadValue().y;
                if (!overUI && Mathf.Abs(scroll) > 0.01f)
                {
                    _targetDistance = Mathf.Clamp(_targetDistance * (scroll > 0 ? 0.9f : 1.1f), minDistance, maxDistance);
                    _idle = 0;
                }
            }

            _idle += dt;
            if (!_dragging)
            {
                _yawVel = Mathf.Lerp(_yawVel, _idle > 2.5f ? idleSpin : 0f, 1f - Mathf.Exp(-dt * 1.5f));
                _pitchVel = Mathf.Lerp(_pitchVel, 0, 1f - Mathf.Exp(-dt * 4f));
            }
            yaw += _yawVel * dt * (_dragging ? 60f : 1f);
            pitch = Mathf.Clamp(pitch + _pitchVel * dt * (_dragging ? 60f : 1f), -25f, 60f);

            // 太空船變大時自動拉遠
            if (satellite != null)
            {
                float size = satellite.ModelBounds.size.magnitude;
                float want = Mathf.Clamp(size * 1.6f + 2f, minDistance, maxDistance);
                if (_idle > 0.5f) _targetDistance = Mathf.Lerp(_targetDistance, Mathf.Max(_targetDistance, want), 1f - Mathf.Exp(-dt));
            }
            distance = Mathf.Lerp(distance, _targetDistance, 1f - Mathf.Exp(-dt * 6f));

            var center = (target != null ? target.position : Vector3.zero) + lookOffset;
            var rot = Quaternion.Euler(pitch, yaw, 0);
            transform.position = center - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }
    }
}
