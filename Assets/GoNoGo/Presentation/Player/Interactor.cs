using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 互動：第一人稱從畫面中央打射線；第三人稱挑角色身邊、鏡頭前方的物件（見 <see cref="Nearby"/>）。送出資訊框內容給 HUD；
    /// C 拿取／放下／拆下，V 長按 1.2 秒安裝，E 使用控制台。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class Interactor : MonoBehaviour
    {
        public const float HoldSeconds = 1.2f;

        PlayerController _pc;
        Interactable _current;
        float _hold;
        float _chargeTick;

        /// <summary>本機玩家手上的零件（null = 空手）。</summary>
        public string Holding => GameSession.HasMission ? GameSession.Mission.S.HeldBy(GameSession.LocalPlayer) : null;
        public bool Installing => _hold > 0;
        public PlayerController Controller => _pc;

        void Awake() => _pc = GetComponent<PlayerController>();

        void OnDisable()
        {
            SetCurrent(null);
            Hud.Target = null;
            Hud.Progress = 0;
        }

        void Update()
        {
            if (!GameSession.HasMission) return;
            var m = GameSession.Mission;
            var kb = Keyboard.current;
            bool active = !Hud.InputBlocked && !_pc.Frozen && Hud.ClosedFrame != Time.frameCount;
            string held = Holding;

            // ---- 對到什麼 ----
            Interactable found = null;
            Collider hitCol = null;
            if (active && _pc.FirstPerson)
            {
                var ray = _pc.AimRay;
                if (Physics.Raycast(ray, out var hit, 12f, ~(1 << 2), QueryTriggerInteraction.Collide))
                {
                    var it = hit.collider.GetComponentInParent<Interactable>();
                    if (it != null && it.isActiveAndEnabled && Vector3.Distance(_pc.Pivot, hit.point) <= it.Range)
                    {
                        found = it;
                        hitCol = hit.collider;
                    }
                }
            }
            else if (active) found = Nearby(out hitCol);
            if (found is IntegrationStand st) st.Aim(hitCol);
            SetCurrent(found);

            // ---- 整合台：拿著零件靠近 → 預覽與長按 V ----
            var stand = IntegrationStand.Instance;
            bool nearStand = held != null && stand != null && stand.DistanceTo(transform.position) <= stand.installRange
                             && m.S.Phase == SessionPhase.Design;
            string canInstall = nearStand ? SessionRules.CanInstall(m.S, m.Data, GameSession.LocalPlayer) : "x";
            if (stand != null) stand.ShowGhost(nearStand && canInstall == SessionRules.Ok ? held : null, m);

            HudTarget target = null;
            if (nearStand && active)
            {
                var def = m.Data.Part(held);
                target = PartItem.Info(def);
                if (canInstall == SessionRules.Ok)
                {
                    var old = SessionRules.WouldReplace(m.S, m.Data, held);
                    var label = old != null ? Loc.T("ui.hint.install_replace", ("code", m.Data.Part(old)?.Code ?? "")) : Loc.T("ui.hint.install");
                    target.Keys.Add(new KeyHint("V", label));
                }
                else target.Subtitle = Loc.T(canInstall);
                target.Keys.Add(new KeyHint("C", Loc.T("ui.hint.drop")));
                if (!_pc.FirstPerson) target.Anchor = TopOf(stand);
            }
            else if (_current != null)
            {
                target = _current.Describe(this);
                if (target != null && !_pc.FirstPerson) target.Anchor = TopOf(_current);
            }
            else if (held != null && active)
            {
                var def = m.Data.Part(held);
                target = PartItem.Info(def);
                target.Subtitle = Loc.T("ui.hint.holding");
                target.Keys.Add(new KeyHint("C", Loc.T("ui.hint.drop")));
            }
            Hud.Target = target;

            if (!active || kb == null)
            {
                CancelHold();
                return;
            }

            // ---- V：長按安裝 ----
            if (nearStand && canInstall == SessionRules.Ok && kb.vKey.isPressed)
            {
                _hold += Time.deltaTime;
                _chargeTick -= Time.deltaTime;
                if (_chargeTick <= 0) { Sfx.Charge(_hold / HoldSeconds); _chargeTick = 0.1f; }
                Hud.Progress = Mathf.Clamp01(_hold / HoldSeconds);
                if (_hold >= HoldSeconds)
                {
                    GameSession.Send(CommandType.Install);
                    Hud.Signal("install");
                    CancelHold();
                }
            }
            else CancelHold();

            // ---- C：拿取、放下、拆下 ----
            if (kb.cKey.wasPressedThisFrame)
            {
                if (_current != null && !(nearStand && held != null)) _current.OnKey(this, 'C');
                else if (held != null)
                {
                    GameSession.Send(CommandType.Drop);
                    Sfx.Drop();
                }
            }
            // ---- E：使用 ----
            if (kb.eKey.wasPressedThisFrame && _current != null) _current.OnKey(this, 'E');
        }

        /// <summary>
        /// 第三人稱：不靠準星，挑角色身邊、在鏡頭朝向前方的互動物件（開放世界遊戲的做法，不用低頭對準桌上的東西）。
        /// 分數 = 距離 + 偏離鏡頭水平朝向的角度 + 偏離畫面中央的角度；同一處有好幾個時，轉一下鏡頭就能換。
        /// 整合台另外挑離畫面中央最近的零件（拆零件用）。
        /// </summary>
        Interactable Nearby(out Collider col)
        {
            col = null;
            var pivot = _pc.Pivot;
            var aim = _pc.AimRay;
            var fwd = aim.direction;
            fwd.y = 0;
            fwd.Normalize();
            Interactable best = null;
            float bestScore = float.MaxValue;
            foreach (var it in Interactable.All)
            {
                if (it == null || !it.isActiveAndEnabled) continue;
                foreach (var c in it.Colliders)
                {
                    if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    var b = c.bounds;
                    var p = b.ClosestPoint(pivot);
                    float d = Vector3.Distance(pivot, p);
                    if (d > it.Range) continue;
                    var flat = p - pivot;
                    flat.y = 0;
                    float yaw = flat.sqrMagnitude < 0.0025f ? 0f : Vector3.Angle(fwd, flat);
                    if (yaw > 75f) continue;
                    float score = d * 0.6f + yaw / 60f + Vector3.Angle(aim.direction, b.center - aim.origin) / 30f;
                    if (score >= bestScore || Blocked(it, pivot, b.center)) continue;
                    best = it;
                    bestScore = score;
                    col = c;
                }
            }
            if (best is IntegrationStand stand)
            {
                // 船在角色前方偏下，鏡頭中央的射線常常從船上方穿過：改挑離畫面中央角度最小的零件（30° 內）
                col = null;
                float nearest = 30f;
                foreach (var c in stand.Colliders)
                {
                    var part = c != null && c.enabled && c.gameObject.activeInHierarchy ? c.GetComponentInParent<SatPart>() : null;
                    if (part == null || string.IsNullOrEmpty(part.partId)) continue; // 船身結構不能拆
                    float a = Vector3.Angle(aim.direction, c.bounds.center - aim.origin);
                    if (a < nearest)
                    {
                        nearest = a;
                        col = c;
                    }
                }
            }
            return best;
        }

        /// <summary>角色和物件之間隔著別的東西（牆、別的桌子）。</summary>
        static bool Blocked(Interactable it, Vector3 from, Vector3 to)
        {
            if (!Physics.Linecast(from, to, out var hit, ~(1 << 2), QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.GetComponentInParent<Interactable>() == it) return false;
            return hit.distance < Vector3.Distance(from, to) - 0.15f;
        }

        /// <summary>物件頂端上方一點：提示框貼在這裡。</summary>
        static Vector3 TopOf(Interactable it)
        {
            Bounds b = default;
            bool any = false;
            foreach (var c in it.Colliders)
            {
                if (c == null || !c.enabled) continue;
                if (!any) { b = c.bounds; any = true; }
                else b.Encapsulate(c.bounds);
            }
            if (!any) return it.transform.position + Vector3.up * 0.3f;
            return new Vector3(b.center.x, b.max.y + 0.15f, b.center.z);
        }

        void CancelHold()
        {
            _hold = 0;
            Hud.Progress = 0;
        }

        void SetCurrent(Interactable it)
        {
            if (it == _current) return;
            if (_current != null) _current.SetHighlight(false);
            _current = it;
            if (_current != null) _current.SetHighlight(true);
        }
    }
}
