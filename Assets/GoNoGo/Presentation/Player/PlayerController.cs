using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 本機玩家：WASD 移動、Shift 跑（按住或切換，見 <see cref="GameSettings.RunToggle"/>）、滑鼠轉視角、
    /// Q 切換第一／第三人稱（預設第三人稱，選單也能切）。
    /// 第三人稱鏡頭在角色正後上方約 3.8 m（角色在畫面正中偏下，準星在頭上方），撞到牆時自動拉近。
    /// 瀏覽器會鎖住滑鼠游標，Esc 放開（打開選單）。桌面版和編輯器在視窗重新拿到焦點時自動鎖回去。
    /// 第一人稱視野比較廣（<see cref="firstPersonFov"/>），近距離看控制台不會只看到一小塊。
    /// 滑鼠靈敏度乘上選單裡的 <see cref="GameSettings.MouseSensitivity"/>。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        public float walkSpeed = 2.4f;
        public float runSpeed = 4.4f;
        public float mouseSensitivity = 0.11f;
        public float thirdPersonDistance = 3.8f;
        public float firstPersonFov = 72f;
        [Tooltip("第一人稱的眼睛高度（m）。角色是 1.2 m 的 Q 版太空人，但中控台、螢幕是照真人尺寸做的，用成人視線高度才看得到桌面和螢幕。")]
        public float eyeHeight = 1.55f;
        public Camera cam;
        public AvatarRig rig;

        CharacterController _cc;
        float _yaw, _pitch = 12f, _vy, _camDist;
        Vector3 _lastPos;
        bool _firstPerson;
        bool _refocused;
        /// <summary>奔跑方式是「切換」時：現在是不是在跑（按一下 Shift 開、再按一下關，停下來自動關）。</summary>
        bool _running;
        /// <summary>切換模式：開始跑之後有沒有動過、已經停了多久（停下來超過一小段時間才取消跑步）。</summary>
        bool _ranMoving;
        float _stillFor;
        float _baseFov = -1;

        public float Speed { get; private set; }
        public bool FirstPerson => _firstPerson;
        public float Yaw => _yaw;
        /// <summary>第一人稱鏡頭的位置。</summary>
        public Vector3 Eye => transform.position + Vector3.up * eyeHeight;
        /// <summary>鏡頭樞紐（大約在頭的高度）。</summary>
        public Vector3 Pivot => transform.position + Vector3.up * 1.0f;
        /// <summary>鏡頭動作被鎖住（看動畫、過場）。</summary>
        public bool Frozen { get; set; }

        static readonly int IgnoreRaycast = 2;
        /// <summary>第三人稱鏡頭相對視角的方向：x = 0 → 正後方不偏左右；y → 稍微高過頭。</summary>
        static readonly Vector3 ThirdPersonOffset = new Vector3(0f, 0.25f, -1f).normalized;

        void Awake()
        {
            _cc = GetComponent<CharacterController>();
            gameObject.layer = IgnoreRaycast; // 準星和鏡頭碰撞都不會打到自己
            _camDist = thirdPersonDistance;
            _firstPerson = Hud.FirstPerson; // 換場景後保留玩家選的視角
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

        /// <summary>
        /// 切到別的視窗再回來（編輯器裡點了 Console 也算）：下一幀自動把游標鎖回去，不必再點一次畫面。
        /// 瀏覽器規定一定要使用者點一下才能鎖，所以網頁版不做。
        /// </summary>
        void OnApplicationFocus(bool focus)
        {
            if (focus && Application.platform != RuntimePlatform.WebGLPlayer) _refocused = true;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            bool active = !Hud.InputBlocked && !Frozen;

            if (_refocused)
            {
                _refocused = false;
                if (active && !CursorLocked) LockCursor(true);
            }

            // 點一下畫面就鎖住游標（瀏覽器規定要使用者動作才能鎖）
            if (active && !CursorLocked && mouse != null && mouse.leftButton.wasPressedThisFrame && !PointerHooks.IsOverUI(mouse.position.ReadValue()))
                LockCursor(true);
            bool look = active && CursorLocked;

            if (look && mouse != null)
            {
                var d = mouse.delta.ReadValue();
                float sens = mouseSensitivity * GameSettings.MouseSensitivity;
                _yaw += d.x * sens;
                _pitch = Mathf.Clamp(_pitch - d.y * sens, -70f, 75f);
            }
            // 選單裡切換的視角（Q 也會同時改兩邊）
            if (_firstPerson != Hud.FirstPerson) _firstPerson = Hud.FirstPerson;
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
                if (GameSettings.RunToggle)
                {
                    // 切換：按一下 Shift 開始跑，再按一下改回走
                    if (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame) _running = !_running;
                }
                else
                {
                    // 按住：按住 Shift 跑，放開就走
                    _running = false;
                    run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
                }
            }
            // 切換模式：跑起來之後停下來（沒有按方向鍵，或開著面板、選單不能動）就自動改回走。
            // 站著先按 Shift 再走也算數（還沒動過不取消）；換方向時放開按鍵的那一瞬間不算停（0.25 秒內）。
            if (!_running) { _ranMoving = false; _stillFor = 0; }
            else if (input.sqrMagnitude >= 0.01f) { _ranMoving = true; _stillFor = 0; }
            else if (_ranMoving)
            {
                _stillFor += Time.deltaTime;
                if (_stillFor > 0.25f) _running = false;
            }
            if (GameSettings.RunToggle) run = _running;
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
            if (_baseFov < 0) _baseFov = cam.fieldOfView;
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, _firstPerson ? firstPersonFov : _baseFov, 1f - Mathf.Exp(-Time.deltaTime * 10f));
            var rot = Quaternion.Euler(_pitch, _yaw, 0);
            if (_firstPerson)
            {
                cam.transform.SetPositionAndRotation(Eye + (Quaternion.Euler(0, _yaw, 0) * Vector3.forward) * 0.12f, rot);
                cam.nearClipPlane = 0.05f;
                _camDist = 0.3f;
                return;
            }
            cam.nearClipPlane = 0.1f;
            // 第三人稱：正後上方（左右置中）；撞到牆就拉近
            var pivot = Pivot + Vector3.up * 0.15f;
            var dir = rot * ThirdPersonOffset;
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
