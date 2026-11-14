using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 準星互動：從畫面中央打射線，找 2.5 m 內的可互動物件，送出資訊框內容給 HUD；
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

            // ---- 準星對到什麼 ----
            Interactable found = null;
            Collider hitCol = null;
            if (active)
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
            }
            else if (_current != null) target = _current.Describe(this);
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
