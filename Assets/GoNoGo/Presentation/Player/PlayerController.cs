using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 本機玩家：WASD 移動、Shift 跑、滑鼠轉視角、Q 切換第一／第三人稱（預設第三人稱）。
    /// 第三人稱鏡頭在角色後上方約 3 m，撞到牆時自動拉近。瀏覽器會鎖住滑鼠游標，Esc 放開（暫停）。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public float walkSpeed = 2.4f;
        public float runSpeed = 4.4f;
        public float mouseSensitivity = 0.11f;
        public float thirdPersonDistance = 3.8f;
        public Camera cam;
        public AvatarRig rig;

        CharacterController _cc;
        float _yaw, _pitch = 12f, _vy, _camDist;
        Vector3 _lastPos;
        bool _firstPerson;

        public float Speed { get; private set; }
        public bool FirstPerson => _firstPerson;
        public float Yaw => _yaw;
        /// <summary>鏡頭樞紐（大約在頭的高度）。</summary>
        public Vector3 Pivot => transform.position + Vector3.up * 1.0f;
        /// <summary>鏡頭動作被鎖住（看動畫、過場）。</summary>
        public bool Frozen { get; set; }

        static readonly int IgnoreRaycast = 2;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            gameObject.layer = IgnoreRaycast; // 準星和鏡頭碰撞都不會打到自己
            _camDist = thirdPersonDistance;
            _yaw = transform.eulerAngles.y;
            _lastPos = transform.position;
        }

        public void SetRig(AvatarRig r)
        {
            rig = r;
            foreach (var t in r.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = IgnoreRaycast;
        }

        /// <summary>瞬間移到某個位置（過門、開局、營運開始）。</summary>
        public void Teleport(Vector3 pos, float yaw)
        {
            _cc.enabled = false;
            transform.position = pos;
            _cc.enabled = true;
            _yaw = yaw;
            _pitch = 10f;
            _lastPos = pos;
            if (rig != null) rig.transform.rotation = Quaternion.Euler(0, yaw, 0);
        }

        public static bool CursorLocked => Cursor.lockState == CursorLockMode.Locked;

        public static void LockCursor(bool on)
        {
            Cursor.lockState = on ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !on;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool active = !Hud.InputBlocked && !Frozen;

            // 點一下畫面就鎖住游標（瀏覽器規定要使用者動作才能鎖）
            if (active && !CursorLocked && mouse != null && mouse.leftButton.wasPressedThisFrame && !PointerHooks.IsOverUI(mouse.position.ReadValue()))
                LockCursor(true);
            bool look = active && CursorLocked;

            if (look && mouse != null)
            {
                var d = mouse.delta.ReadValue();
                _yaw += d.x * mouseSensitivity;
                _pitch = Mathf.Clamp(_pitch - d.y * mouseSensitivity, -70f, 75f);
            }
            if (active && kb != null && kb.qKey.wasPressedThisFrame)
            {
                _firstPerson = !_firstPerson;
                Hud.FirstPerson = _firstPerson;
                Hud.Signal("view");
            }

            // 移動：相對鏡頭方向
            Vector2 input = Vector2.zero;
            bool run = false;
            if (active && kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) input.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) input.y -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) input.x -= 1;
                run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
            if (input.sqrMagnitude > 1) input.Normalize();
            var fwd = Quaternion.Euler(0, _yaw, 0) * Vector3.forward;
            var right = Quaternion.Euler(0, _yaw, 0) * Vector3.right;
            var move = (fwd * input.y + right * input.x) * (run ? runSpeed : walkSpeed);

            _vy = _cc.isGrounded ? -1f : _vy - 18f * Time.deltaTime;
            if (_cc.enabled) _cc.Move((move + Vector3.up * _vy) * Time.deltaTime);

            var delta = transform.position - _lastPos;
            delta.y = 0;
            Speed = Time.deltaTime > 0 ? delta.magnitude / Time.deltaTime : 0;
            _lastPos = transform.position;
            if (input.sqrMagnitude > 0.01f) Hud.Signal("move");

            if (rig != null)
            {
                // 第一人稱：身體跟著視角；第三人稱：身體轉向移動方向
                var face = _firstPerson ? fwd : move;
                if (face.sqrMagnitude > 0.01f)
                {
                    var target = Quaternion.LookRotation(new Vector3(face.x, 0, face.z));
                    rig.transform.rotation = Quaternion.Slerp(rig.transform.rotation, target, 1f - Mathf.Exp(-Time.deltaTime * 12f));
                }
                rig.speed = Speed;
                rig.hideHead = _firstPerson;
            }

            PoseFeed.Local.Position = transform.position;
            PoseFeed.Local.Yaw = rig != null ? rig.transform.eulerAngles.y : _yaw;
            PoseFeed.Local.Speed = Speed;
        }

        void LateUpdate()
        {
            if (cam == null) return;
            var rot = Quaternion.Euler(_pitch, _yaw, 0);
            if (_firstPerson)
            {
                cam.transform.SetPositionAndRotation(transform.position + Vector3.up * 1.0f + (Quaternion.Euler(0, _yaw, 0) * Vector3.forward) * 0.12f, rot);
                cam.nearClipPlane = 0.05f;
                _camDist = 0.3f;
                return;
            }
            cam.nearClipPlane = 0.1f;
            // 第三人稱：肩後方；撞到牆就拉近
            var pivot = Pivot + Vector3.up * 0.15f;
            var dir = rot * new Vector3(0.55f, 0.42f, -1f).normalized;
            float want = thirdPersonDistance;
            if (Physics.SphereCast(pivot, 0.22f, dir, out var hit, want, ~(1 << IgnoreRaycast), QueryTriggerInteraction.Ignore))
                want = Mathf.Max(0.4f, hit.distance - 0.05f);
            _camDist = want < _camDist ? want : Mathf.Lerp(_camDist, want, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            cam.transform.SetPositionAndRotation(pivot + dir * _camDist, rot);
        }

        /// <summary>準星的射線（畫面中央）。</summary>
        public Ray AimRay => cam != null ? new Ray(cam.transform.position, cam.transform.forward) : new Ray(Pivot, transform.forward);
    }
}
