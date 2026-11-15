using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// Facility 場景（大廳、組裝區、中控室）的執行期總管：依狀態把零件放上收貨輸送帶、更新整合台、
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
        public DeliveryConveyor delivery;
        [Tooltip("組裝區與中控室的分界（世界座標 x）：x 小於它是組裝區")]
        public float areaSplitX = 11.5f;
        [Tooltip("大廳的 z 起點：z 大於它是大廳")]
        public float lobbyMinZ = 30f;

        PlayerController _player;
        Interactor _interactor;
        AvatarRig _localRig;
        readonly Dictionary<string, PartItem> _items = new Dictionary<string, PartItem>();
        /// <summary>上一次看到的已採購零件：新出現的就是剛到貨，從收貨窗口送出來。</summary>
        HashSet<string> _ownedBefore;
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
            BuildItems(m);
            PartPreviewStage.Create(transform);
            SpawnLocal(m);
            m.Changed += OnChanged;
            m.PhaseChanged += OnPhase;
            Profile.Changed += OnProfile;
            stand.Refresh(m, false);
            OnChanged();
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            Sfx.SetMusic(Sfx.Music.Facility);
        }

        // ---------------- 收貨輸送帶 ----------------

        /// <summary>這一局型錄上每個零件都先建好、藏起來；買到的才放上輸送帶。</summary>
        void BuildItems(MissionSession m)
        {
            foreach (var p in SessionRules.ShelfParts(m.Data, m.S).OrderBy(p => p.Category).ThenBy(p => p.Tier).ThenBy(p => p.Id))
            {
                var item = PartItem.Create(p, m.Data, delivery.items, delivery.start, delivery.itemSize);
                item.SetVisible(false);
                _items[p.Id] = item;
            }
        }

        /// <summary>
        /// 輸送帶上 = 買過（或開局就有）、沒裝在船上、沒人拿著的零件。
        /// 剛買到的從收貨窗口一件件送出來（照下單順序）；放下的零件直接回到隊伍最後面。
        /// </summary>
        void SyncDelivery(MissionSession m)
        {
            var installed = new HashSet<string>(m.S.Bench.AllPartIds());
            var held = new HashSet<string>(m.S.Held.Values);
            // 放在組裝工作台上的那一個（正在組、或組好了還沒拿走）不在輸送帶上
            string onBench = m.S.Workbench?.PartId;
            bool Loose(string id) => m.S.Owned.Contains(id) && !installed.Contains(id) && !held.Contains(id) && id != onBench;
            foreach (var kv in _items)
            {
                // 還沒組裝的零件是一箱小零件；在組裝工作台組好之後才變成零件本身
                kv.Value.SetBuilt(!SessionRules.NeedsAssembly(m.S, m.Data, kv.Key));
                if (!Loose(kv.Key))
                {
                    delivery.Remove(kv.Value);
                    kv.Value.SetVisible(false);
                }
            }
            foreach (var id in m.S.Owned)
                if (Loose(id) && _items.TryGetValue(id, out var item) && !delivery.Has(item))
                    delivery.Add(item, _ownedBefore != null && !_ownedBefore.Contains(id));
            _ownedBefore = new HashSet<string>(m.S.Owned);
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
            SyncDelivery(m);
            SyncWorkshop(m);

            // 本機手上的零件（或組裝工作台的小零件）
            var mine = m.S.HeldBy(GameSession.LocalPlayer);
            var mineComp = m.S.HeldCompBy(GameSession.LocalPlayer);
            // 手上拿的是箱子還是組好的零件（組裝完成時要換模型，所以一起記）
            bool mineKit = mine != null && SessionRules.NeedsAssembly(m.S, m.Data, mine);
            string mineKey = mine != null ? mine + (mineKit ? "#kit" : "") : mineComp != null ? "comp:" + mineComp : null;
            if (mineKey != _localHeldId)
            {
                _localHeldId = mineKey;
                if (_localHeld != null) Destroy(_localHeld.gameObject);
                _localHeld = mine != null ? HeldModel(mine, mineKit, m.Data, _localRig.hold)
                    : mineComp != null ? HeldComponent(mineComp, _localRig.hold) : null;
            }
            _localRig.holding = mine != null || mineComp != null;
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
                var held = m.S.HeldBy(p.Id);
                ra.Apply(p.Look, held, m.Data, held != null && SessionRules.NeedsAssembly(m.S, m.Data, held), m.S.HeldCompBy(p.Id));
            }
        }

        // ---------------- 組裝工作台與零件盤 ----------------

        int _fitsSeen = -1;
        string _fitsPart;

        /// <summary>工作台上的箱子／小零件／成品、零件盤每一格擺的小零件，都照狀態更新；有人裝了一個小零件就播效果。</summary>
        void SyncWorkshop(MissionSession m)
        {
            var bench = Workbench.Instance;
            if (bench != null) bench.Sync(m);
            foreach (var tray in CompTray.Trays) if (tray != null) tray.Sync(m);

            var job = m.S.Workbench;
            if (job == null) { _fitsSeen = -1; _fitsPart = null; return; }
            if (_fitsPart != job.PartId) { _fitsPart = job.PartId; _fitsSeen = job.Fits == 0 ? 0 : -1; }
            // 第一次看到（中途加入、換場景回來）不播；之後每多裝一次播一次
            if (_fitsSeen >= 0 && job.Fits > _fitsSeen)
            {
                if (bench != null) bench.FitFx(job.LastOk);
                if (job.LastOk) Sfx.Install(); else Sfx.NoGo();
            }
            _fitsSeen = job.Fits;
        }

        /// <summary>拿在手上的小零件（組裝工作台）。</summary>
        public static Transform HeldComponent(string compId, Transform hand)
        {
            var go = SatelliteBuilder.BuildComponent(compId, hand, 0.24f);
            // 模型的原點在底部：往下移一點，拿起來才在兩手中間
            go.transform.localPosition = new Vector3(0, -0.08f, 0);
            return go.transform;
        }

        /// <summary>拿在手上的模型：還沒組裝是一箱小零件，組好了是零件本身。</summary>
        public static Transform HeldModel(string partId, bool kit, GameData data, Transform hand) =>
            (kit ? SatelliteBuilder.BuildKit(data.Part(partId), hand, 0.4f) : SatelliteBuilder.BuildPart(partId, data, hand, 0.42f)).transform;

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
