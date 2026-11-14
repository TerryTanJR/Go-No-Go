using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// Facility 場景（大廳、組裝區、中控室）的執行期總管：依狀態擺零件、更新整合台、
    /// 產生本機玩家與其他玩家的 avatar、處理階段切換時的傳送。
    /// </summary>
    public sealed class FacilityDirector : MonoBehaviour
    {
        public static FacilityDirector Instance { get; private set; }

        [Header("出生點（朝向 = 物件的 forward）")]
        public Transform assemblySpawn;
        public Transform controlSpawn;
        public Transform lobbySpawn;
        public Transform opsSpawn;

        [Header("場景物件")]
        public Camera playerCamera;
        public IntegrationStand stand;
        public PartRack[] racks;
        [Tooltip("組裝區與中控室的分界（世界座標 x）：x 小於它是組裝區")]
        public float areaSplitX = 11.5f;
        [Tooltip("大廳的 z 起點：z 大於它是大廳")]
        public float lobbyMinZ = 30f;

        PlayerController _player;
        Interactor _interactor;
        AvatarRig _localRig;
        readonly Dictionary<string, PartItem> _items = new Dictionary<string, PartItem>();
        readonly Dictionary<int, RemoteAvatar> _remotes = new Dictionary<int, RemoteAvatar>();
        Transform _localHeld;
        string _localHeldId;

        public PlayerController Player => _player;
        public AvatarRig LocalRig => _localRig;

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
            BuildShelves(m);
            SpawnLocal(m);
            m.Changed += OnChanged;
            m.PhaseChanged += OnPhase;
            Profile.Changed += OnProfile;
            stand.Refresh(m, false);
            OnChanged();
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
        }

        // ---------------- 零件架 ----------------

        void BuildShelves(MissionSession m)
        {
            var allowed = SessionRules.ShelfParts(m.Data, m.S);
            foreach (var rack in racks)
            {
                var cats = rack.Categories.ToList();
                var parts = allowed.Where(p => cats.Contains(p.Category))
                    .OrderBy(p => cats.IndexOf(p.Category)).ThenBy(p => p.Tier).ThenBy(p => p.Id).ToList();
                for (int i = 0; i < parts.Count; i++)
                {
                    var item = PartItem.Create(parts[i], m.Data, rack.transform, rack.Slot(i, parts.Count), rack.itemSize);
                    _items[parts[i].Id] = item;
                }
            }
        }

        // ---------------- 玩家 ----------------

        void SpawnLocal(MissionSession m)
        {
            var go = new GameObject("LocalPlayer");
            var cc = go.AddComponent<CharacterController>();
            cc.height = 1.2f;
            cc.radius = 0.3f;
            cc.center = new Vector3(0, 0.62f, 0);
            cc.stepOffset = 0.3f;
            _player = go.AddComponent<PlayerController>();
            _player.cam = playerCamera;
            _interactor = go.AddComponent<Interactor>();
            BuildLocalRig();

            var spawn = SpawnFor(m.S.Phase, m.S.Player(GameSession.LocalPlayer)?.Station ?? Station.Control, m.S.Multiplayer);
            _player.Teleport(spawn.position, spawn.eulerAngles.y);
        }

        void BuildLocalRig()
        {
            if (_localRig != null) Destroy(_localRig.gameObject);
            _localRig = AvatarBuilder.Build(Profile.Look, _player.transform);
            _player.SetRig(_localRig);
            _localHeldId = null;
        }

        void OnProfile()
        {
            BuildLocalRig();
            if (GameSession.Mission.S.Multiplayer) GameSession.Send(new Command { Type = CommandType.SetLook, Look = Profile.Look.Clone() });
        }

        Transform SpawnFor(SessionPhase phase, Station station, bool multiplayer)
        {
            switch (phase)
            {
                case SessionPhase.Lobby: return lobbySpawn;
                case SessionPhase.Ops:
                case SessionPhase.Report: return opsSpawn;
                case SessionPhase.Briefing: return multiplayer ? (station == Station.Assembly ? assemblySpawn : controlSpawn) : controlSpawn;
                default: return station == Station.Assembly ? assemblySpawn : controlSpawn;
            }
        }

        void OnPhase(SessionPhase before, SessionPhase now)
        {
            var m = GameSession.Mission;
            var me = m.S.Player(GameSession.LocalPlayer);
            bool move = before == SessionPhase.Lobby || now == SessionPhase.Ops;
            if (!move) return;
            var spawn = SpawnFor(now, me?.Station ?? Station.Control, m.S.Multiplayer);
            FadeTeleport(spawn);
        }

        public void FadeTeleport(Transform spawn)
        {
            if (spawn == null) return;
            if (SceneFlow.FadeOut == null) { _player.Teleport(spawn.position, spawn.eulerAngles.y); return; }
            _player.Frozen = true;
            SceneFlow.FadeOut(0.35f, () =>
            {
                _player.Teleport(spawn.position, spawn.eulerAngles.y);
                _player.Frozen = false;
                SceneFlow.FadeIn?.Invoke(0.35f);
            });
        }

        // ---------------- 狀態改變 ----------------

        void OnChanged()
        {
            var m = GameSession.Mission;
            stand.Refresh(m, true);
            var installed = new HashSet<string>(m.S.Bench.AllPartIds());
            var held = new HashSet<string>(m.S.Held.Values);
            foreach (var kv in _items) kv.Value.SetVisible(!installed.Contains(kv.Key) && !held.Contains(kv.Key));

            // 本機手上的零件
            var mine = m.S.HeldBy(GameSession.LocalPlayer);
            if (mine != _localHeldId)
            {
                _localHeldId = mine;
                if (_localHeld != null) Destroy(_localHeld.gameObject);
                _localHeld = mine != null ? SatelliteBuilder.BuildPart(mine, m.Data, _localRig.hold, 0.42f).transform : null;
            }
            _localRig.holding = mine != null;
            PoseFeed.Local.Held = mine;

            SyncRemotes(m);
        }

        void SyncRemotes(MissionSession m)
        {
            var others = m.S.Players.Where(p => p.Id != GameSession.LocalPlayer).ToList();
            foreach (var id in _remotes.Keys.ToList())
                if (others.All(p => p.Id != id))
                {
                    Destroy(_remotes[id].gameObject);
                    _remotes.Remove(id);
                }
            foreach (var p in others)
            {
                if (!_remotes.TryGetValue(p.Id, out var ra))
                {
                    var go = new GameObject("Remote_" + p.Id);
                    ra = go.AddComponent<RemoteAvatar>();
                    ra.playerId = p.Id;
                    _remotes[p.Id] = ra;
                    var start = SpawnFor(m.S.Phase, p.Station, true);
                    go.transform.position = start.position;
                }
                ra.Apply(p.Look, m.S.HeldBy(p.Id), m.Data);
            }
        }

        void Update()
        {
            if (_player == null) return;
            var p = _player.transform.position;
            Hud.Area = p.z > lobbyMinZ ? "lobby" : p.x < areaSplitX ? "assembly" : "control";
            if (_localRig != null) _localRig.installing = _interactor.Installing;
            PoseFeed.Local.Installing = _interactor.Installing;
        }
    }
}
