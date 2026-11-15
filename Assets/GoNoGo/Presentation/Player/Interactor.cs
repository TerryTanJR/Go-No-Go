using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 互動：第一人稱從畫面中央打射線；第三人稱挑角色身邊、鏡頭前方的物件（見 <see cref="Nearby"/>）。送出資訊框內容給 HUD；
    /// E 使用（大屏幕、線索、發射鈕…；手上有小零件時對著組裝台 = 放到左側），C 拿取／放回，
    /// V 長按：拿著小零件對著組裝台右側裝上去（0.6 秒）、拿著組好的零件在整合台旁邊裝上衛星（1.2 秒）。
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public sealed class Interactor : MonoBehaviour
    {
        public const float HoldSeconds = 1.2f;
        /// <summary>在組裝台裝一個小零件要按多久（一個零件要裝好幾次，比安裝短）。</summary>
        public const float FitSeconds = 0.6f;

        static readonly List<string> NoComps = new List<string>();

        PlayerController _pc;
        Interactable _current;
        float _hold;
        float _chargeTick;
        bool _holdDone;

        /// <summary>本機玩家手上組好的零件（null = 沒有）。</summary>
        public string Holding => GameSession.HasMission ? GameSession.Mission.S.HeldBy(GameSession.LocalPlayer) : null;
        /// <summary>本機玩家手上的小零件（單人的籃子最多 4 個，多人一次一個；沒有 = 空清單）。</summary>
        public List<string> Comps => GameSession.HasMission ? GameSession.Mission.S.CompsOf(GameSession.LocalPlayer) : NoComps;
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
            var comps = Comps;

            // ---- 對到什麼 ----
            Interactable found = null;
            Collider hitCol = null;
            if (active && _pc.FirstPerson)
            {
                var ray = _pc.AimRay;
                if (Physics.Raycast(ray, out var hit, 16f, ~(1 << 2), QueryTriggerInteraction.Collide))
                {
                    var it = hit.collider.GetComponentInParent<Interactable>();
                    if (it != null && it.isActiveAndEnabled && Vector3.Distance(_pc.Pivot, hit.point) <= it.Range)
                    {
                        found = it;
                        hitCol = hit.collider;
                    }
                }
                if (found == null) found = Assist(out hitCol);
            }
            else if (active) found = Nearby(out hitCol);
            if (found is IntegrationStand st) st.Aim(hitCol);
            SetCurrent(found);

            // ---- 整合台：拿著組好的零件靠近 → 預覽與長按 V ----
            var stand = IntegrationStand.Instance;
            bool nearStand = held != null && stand != null && stand.DistanceTo(transform.position) <= stand.installRange
                             && m.S.Phase == SessionPhase.Design;
            string canInstall = nearStand ? SessionRules.CanInstall(m.S, m.Data, GameSession.LocalPlayer) : "x";
            if (stand != null) stand.ShowGhost(nearStand && canInstall == SessionRules.Ok ? held : null, m);

            HudTarget target = null;
            if (nearStand && active)
            {
                target = PartHud.Info(m.Data.Part(held));
                if (canInstall == SessionRules.Ok) target.Keys.Add(new KeyHint("V", Loc.T("ui.hint.install")));
                else target.Subtitle = Loc.T(canInstall);
                if (!_pc.FirstPerson) target.Anchor = TopOf(stand);
            }
            else if (_current != null)
            {
                target = _current.Describe(this);
                if (target != null && !_pc.FirstPerson) target.Anchor = TopOf(_current);
            }
            else if (comps.Count > 0 && active)
            {
                // 手上拿著小零件、沒對著東西：提醒拿去組裝台
                target = new HudTarget { Title = CompNames(m.Data, comps), Subtitle = Loc.T("ui.hint.holding_comp") };
                target.Keys.Add(new KeyHint("C", Loc.T(comps.Count > 1 ? "ui.hint.drop_all" : "ui.hint.drop")));
            }
            else if (held != null && active)
            {
                target = PartHud.Info(m.Data.Part(held));
                target.Subtitle = Loc.T("ui.hint.holding");
            }
            Hud.Target = target;

            if (!active || kb == null)
            {
                CancelHold();
                return;
            }

            // ---- V：長按把小零件裝到組裝台右側／把組好的零件裝上衛星 ----
            var jig = _current as BenchJig;
            bool canFit = comps.Count > 0 && jig != null && SessionRules.BenchRecipe(m.S, m.Data, jig.bench) != null;
            bool canHold = canFit || (nearStand && canInstall == SessionRules.Ok);
            if (canHold && kb.vKey.isPressed && !_holdDone)
            {
                float need = canFit ? FitSeconds : HoldSeconds;
                _hold += Time.deltaTime;
                _chargeTick -= Time.deltaTime;
                if (_chargeTick <= 0) { Sfx.Charge(_hold / need); _chargeTick = 0.1f; }
                Hud.Progress = Mathf.Clamp01(_hold / need);
                if (_hold >= need)
                {
                    if (canFit)
                    {
                        GameSession.Send(CommandType.CompFit, "", jig.bench);
                        Hud.Signal("comp_fit");
                    }
                    else
                    {
                        GameSession.Send(CommandType.Install);
                        Hud.Signal("install");
                    }
                    CancelHold();
                    // 放開 V 之前不再開始下一次（多人時狀態晚一點才回來，不要連續送兩次）
                    _holdDone = true;
                }
            }
            else
            {
                CancelHold();
                if (!kb.vKey.isPressed) _holdDone = false;
            }

            // ---- C：拿取、放回 ----
            if (kb.cKey.wasPressedThisFrame)
            {
                if (_current != null && !(nearStand && held != null)) _current.OnKey(this, 'C');
                else if (comps.Count > 0)
                {
                    // 沒對著架子或組裝台：手上的小零件全部放回架子
                    GameSession.Send(CommandType.CompDrop);
                    Sfx.Drop();
                }
                else if (held != null) Hud.Toast(Loc.T("err.carry_part"));
            }
            // ---- E：使用 ----
            if (kb.eKey.wasPressedThisFrame && _current != null) _current.OnKey(this, 'E');
        }

        /// <summary>手上小零件的名稱（籃子裡好幾個就用頓號接起來）。</summary>
        public static string CompNames(GameData data, List<string> comps)
        {
            var names = new List<string>();
            foreach (var id in comps)
            {
                var c = data.Component(id);
                if (c != null) names.Add(Loc.T(c.Name));
            }
            return string.Join(Loc.IsZh ? "、" : ", ", names);
        }

        /// <summary>
        /// 第三人稱：不靠準星，挑角色身邊、在鏡頭朝向前方的互動物件（開放世界遊戲的做法，不用低頭對準桌上的東西）。
        /// 分數 = 距離 + 偏離鏡頭水平朝向的角度 + 偏離畫面中央的角度；同一處有好幾個時，轉一下鏡頭就能換。
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
                    // 很大的東西（前方大屏幕）用「離畫面中央最近的那一點」算角度：站在它前面看哪裡都算對著它
                    var look = b.ClosestPoint(aim.origin + aim.direction * Mathf.Max(0, Vector3.Dot(b.center - aim.origin, aim.direction)));
                    float score = d * 0.6f + yaw / 60f + Vector3.Angle(aim.direction, look - aim.origin) / 30f;
                    if (score >= bestScore || Blocked(it, pivot, look)) continue;
                    best = it;
                    bestScore = score;
                    col = c;
                }
            }
            // 衛星在角色前方偏下，鏡頭中央的射線常常從上方穿過：改挑離畫面中央角度最小的零件（30° 內）
            if (best is IntegrationStand stand) col = stand.NearestPart(aim, 30f, true);
            return best;
        }

        /// <summary>
        /// 第一人稱的吸附：準星差一點沒對到（偏 8° 以內）也算，架上的小零件、牆上的草稿紙不用對得剛剛好。
        /// </summary>
        Interactable Assist(out Collider col)
        {
            col = null;
            var aim = _pc.AimRay;
            float best = 8f;
            Interactable pick = null;
            foreach (var it in Interactable.All)
            {
                if (it == null || !it.isActiveAndEnabled) continue;
                foreach (var c in it.Colliders)
                {
                    if (c == null || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    var b = c.bounds;
                    // 碰撞框上離準星射線最近的點
                    var p = b.ClosestPoint(aim.origin + aim.direction * Mathf.Max(0, Vector3.Dot(b.center - aim.origin, aim.direction)));
                    if (Vector3.Distance(_pc.Pivot, p) > it.Range) continue;
                    float a = Vector3.Angle(aim.direction, p - aim.origin);
                    if (a >= best || Blocked(it, aim.origin, p)) continue;
                    best = a;
                    pick = it;
                    col = c;
                }
            }
            return pick;
        }

        /// <summary>角色和物件之間隔著別的東西（牆、別的桌子）。</summary>
        static bool Blocked(Interactable it, Vector3 from, Vector3 to)
        {
            if (!Physics.Linecast(from, to, out var hit, ~(1 << 2), QueryTriggerInteraction.Ignore)) return false;
            if (hit.collider.GetComponentInParent<Interactable>() == it) return false;
            return hit.distance < Vector3.Distance(from, to) - 0.15f;
        }

        /// <summary>物件頂端上方一點：提示框貼在這裡。用看得到的模型算（碰撞框常常比模型矮，提示框會蓋住模型）。</summary>
        static Vector3 TopOf(Interactable it)
        {
            Bounds b = default;
            bool any = false;
            foreach (var r in it.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            if (!any)
                foreach (var c in it.Colliders)
                {
                    if (c == null || !c.enabled) continue;
                    if (!any) { b = c.bounds; any = true; }
                    else b.Encapsulate(c.bounds);
                }
            if (!any) return it.transform.position + Vector3.up * 0.3f;
            // 整面牆那麼大的東西（前方大屏幕）：提示框放在角色面前的高度，不要貼到天花板上
            if (b.size.y > 3f) return new Vector3(b.center.x, Mathf.Min(b.max.y, 2.6f), b.center.z);
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
