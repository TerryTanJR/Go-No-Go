using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// Facility 場景（大廳、組裝區、中控室）的執行期總管：依狀態更新整合台上的衛星和兩座組裝台、
    /// 產生本機玩家與其他玩家的 avatar、處理階段切換時的傳送。
    /// 不分組：所有人開局都生成在中控室，組裝區和中控室連通，自由走動。
    /// </summary>
    public sealed class FacilityDirector : MonoBehaviour
    {
        public static FacilityDirector Instance { get; private set; }

        [Header("出生點（朝向 = 物件的 forward）")]
        [Tooltip("開局：所有人都在中控室")]
        public Transform controlSpawn;
        public Transform lobbySpawn;
        [Tooltip("營運開始時所有人移到這裡（中控室）")]
        public Transform opsSpawn;

        [Header("場景物件")]
        public Camera playerCamera;
        public IntegrationStand stand;
        [Tooltip("組裝區與中控室的分界（世界座標 x）：x 小於它是組裝區")]
        public float areaSplitX = 11.5f;
        [Tooltip("大廳的 z 起點：z 大於它是大廳")]
        public float lobbyMinZ = 30f;

        PlayerController _player;
        Interactor _interactor;
        AvatarRig _localRig;
        readonly Dictionary<int, RemoteAvatar> _remotes = new Dictionary<int, RemoteAvatar>();
        readonly List<int> _gone = new List<int>();
        Transform _localHeld;
        string _localHeldId;
        /// <summary>所有放線索的位置（包括這一局收起來的，所以開場就先找好）。</summary>
        ClueSpot[] _clueSpots;

        public PlayerController Player => _player;

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (GameSession.HasMission)
            {
                GameSession.Mission.Changed -= OnChanged;
                GameSession.Mission.PhaseChanged -= OnPhase;
            }
            Profile.Changed -= OnProfile;
            PlayerController.LockCursor(false);
        }

        void Start()
        {
            GameSession.EnsureMission();
            var m = GameSession.Mission;
            SpawnLocal(m);
            m.Changed += OnChanged;
            m.PhaseChanged += OnPhase;
            Profile.Changed += OnProfile;
            stand.Refresh(m, false);
            _clueSpots = FindObjectsByType<ClueSpot>(FindObjectsInactive.Include);
            OnChanged();
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            Sfx.SetMusic(Sfx.Music.Facility);
        }

        // ---------------- 玩家 ----------------

        void SpawnLocal(MissionSession m)
        {
            var go = new GameObject("LocalPlayer");
            var cc = go.AddComponent<CharacterController>();
            cc.height = AvatarBuilder.WorldHeight;
            cc.radius = 0.3f;
            cc.center = new Vector3(0, AvatarBuilder.WorldHeight / 2 + 0.02f, 0);
            cc.stepOffset = 0.3f;
            _player = go.AddComponent<PlayerController>();
            _player.cam = playerCamera;
            _interactor = go.AddComponent<Interactor>();
            BuildLocalRig();

            var spawn = SpawnFor(m.S.Phase);
            _player.Teleport(SpawnPos(spawn, m.S, GameSession.LocalPlayer), spawn.eulerAngles.y);
        }

        void BuildLocalRig()
        {
            if (_localRig != null) Destroy(_localRig.gameObject);
            _localRig = AvatarBuilder.Build(Profile.Look, _player.transform);
            AvatarBuilder.ApplyWorldScale(_localRig);
            _player.SetRig(_localRig);
            _localHeldId = null;
        }

        void OnProfile()
        {
            BuildLocalRig();
            if (GameSession.Mission.S.Multiplayer) GameSession.Send(new Command { Type = CommandType.SetLook, Look = Profile.Look.Clone() });
        }

        /// <summary>大廳 → 大廳；營運、報告 → 中控室的營運位置；其他（簡報、選件、組裝）都從中控室開始。</summary>
        Transform SpawnFor(SessionPhase phase)
        {
            switch (phase)
            {
                case SessionPhase.Lobby: return lobbySpawn;
                case SessionPhase.Ops:
                case SessionPhase.Report: return opsSpawn;
                default: return controlSpawn;
            }
        }

        /// <summary>所有人生成在同一個地方：照在房間裡的順序左右排開，不會疊在一起。</summary>
        static Vector3 SpawnPos(Transform spawn, SessionState s, int player)
        {
            int n = Mathf.Max(1, s.Players.Count);
            int i = Mathf.Max(0, s.Players.FindIndex(p => p.Id == player));
            return spawn.position + spawn.right * ((i - (n - 1) / 2f) * 0.9f);
        }

        void OnPhase(SessionPhase before, SessionPhase now)
        {
            var m = GameSession.Mission;
            // 離開大廳（任務開始）、進入營運：所有人移到中控室
            bool move = before == SessionPhase.Lobby || now == SessionPhase.Ops;
            if (!move) return;
            var spawn = SpawnFor(now);
            FadeTeleport(SpawnPos(spawn, m.S, GameSession.LocalPlayer), spawn.eulerAngles.y);
        }

        void FadeTeleport(Vector3 pos, float yaw)
        {
            if (SceneFlow.FadeOut == null) { _player.Teleport(pos, yaw); return; }
            _player.Frozen = true;
            SceneFlow.FadeOut(0.35f, () =>
            {
                _player.Teleport(pos, yaw);
                _player.Frozen = false;
                SceneFlow.FadeIn?.Invoke(0.35f);
            });
        }

        // ---------------- 狀態改變 ----------------

        void OnChanged()
        {
            var m = GameSession.Mission;
            stand.Refresh(m, true);
            foreach (var bench in Workbench.All) if (bench != null) bench.Sync(m);
            foreach (var spot in _clueSpots) spot.Sync(m);

            // 本機手上的東西：組好的零件，或小零件（單人是一籃）
            var mine = m.S.HeldBy(GameSession.LocalPlayer);
            var comps = m.S.CompsOf(GameSession.LocalPlayer);
            string mineKey = mine != null ? mine : comps.Count > 0 ? "comp:" + string.Join(",", comps) : null;
            if (mineKey != _localHeldId)
            {
                _localHeldId = mineKey;
                if (_localHeld != null) Destroy(_localHeld.gameObject);
                _localHeld = mine != null ? HeldModel(mine, m.Data, _localRig.hold)
                    : comps.Count > 0 ? HeldComponents(comps, _localRig.hold, m.S.Alone()) : null;
            }
            _localRig.holding = mine != null || comps.Count > 0;

            SyncRemotes(m);
        }

        /// <summary>其他玩家的 avatar：離開房間的拿掉，新來的建立，外型和手上的東西照狀態更新。</summary>
        void SyncRemotes(MissionSession m)
        {
            var players = m.S.Players;
            int me = GameSession.LocalPlayer;
            if (_remotes.Count > 0)
            {
                _gone.Clear();
                foreach (var id in _remotes.Keys)
                    if (id == me || !InRoom(players, id)) _gone.Add(id);
                foreach (var id in _gone)
                {
                    Destroy(_remotes[id].gameObject);
                    _remotes.Remove(id);
                }
            }
            foreach (var p in players)
            {
                if (p.Id == me) continue;
                if (!_remotes.TryGetValue(p.Id, out var ra))
                {
                    var go = new GameObject("Remote_" + p.Id);
                    ra = go.AddComponent<RemoteAvatar>();
                    ra.playerId = p.Id;
                    _remotes[p.Id] = ra;
                    go.transform.position = SpawnPos(SpawnFor(m.S.Phase), m.S, p.Id);
                }
                ra.Apply(p.Look, m.S.HeldBy(p.Id), m.Data, m.S.CompsOf(p.Id));
            }
        }

        static bool InRoom(List<PlayerInfo> players, int id)
        {
            foreach (var p in players)
                if (p.Id == id) return true;
            return false;
        }

        /// <summary>
        /// 拿在手上的小零件。單人有籃子（<paramref name="basket"/>）：小零件放在籃子裡，最多四個；
        /// 兩人以上不給籃子，一次拿一個。
        /// </summary>
        public static Transform HeldComponents(List<string> comps, Transform hand, bool basket)
        {
            if (!basket && comps.Count == 1)
            {
                var go = SatelliteBuilder.BuildComponent(comps[0], hand, 0.24f);
                // 模型的原點在底部：往下移一點，拿起來才在兩手中間
                go.transform.localPosition = new Vector3(0, -0.08f, 0);
                return go.transform;
            }
            var box = SatelliteBuilder.BuildBasket(hand, 0.46f);
            box.transform.localPosition = new Vector3(0, -0.1f, 0);
            for (int i = 0; i < comps.Count && i < GameData.BasketSize; i++)
            {
                var slot = box.transform.Find("Slot" + i);
                if (slot != null) SatelliteBuilder.BuildComponent(comps[i], slot, 0.16f);
            }
            return box.transform;
        }

        /// <summary>拿在手上的組好的零件。</summary>
        public static Transform HeldModel(string partId, GameData data, Transform hand) =>
            SatelliteBuilder.BuildPart(partId, data, hand, 0.42f).transform;

        void Update()
        {
            if (_player == null) return;
            var p = _player.transform.position;
            Hud.Area = p.z > lobbyMinZ ? "lobby" : p.x < areaSplitX ? "assembly" : "control";
            // 站在哪一座組裝台旁邊（4.5 公尺內最近的那一座）：畫面右上角顯示它的組裝單
            int near = -1;
            float best = 4.5f;
            foreach (var bench in Workbench.All)
            {
                if (bench == null) continue;
                float dist = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(bench.transform.position.x, bench.transform.position.z));
                if (dist < best) { best = dist; near = bench.index; }
            }
            Hud.NearBench = near;
            if (_localRig != null) _localRig.installing = _interactor.Installing;
            PoseFeed.Local.Installing = _interactor.Installing;
        }
    }
}
