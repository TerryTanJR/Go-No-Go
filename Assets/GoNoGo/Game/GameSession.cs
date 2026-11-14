using GoNoGo.Game.Rooms;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 跨場景存在的唯一物件：持有 GameState 與房間同步。
    /// 直接按 Play 開任何一個場景也會自動建立，方便單獨測試場景。
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        static GameSession _instance;

        public static GameSession Instance
        {
            get
            {
                if (_instance == null) Create();
                return _instance;
            }
        }

        /// <summary>已經建立過（OnDisable/OnDestroy 裡用這個判斷，避免關閉遊戲時又建立新物件）。</summary>
        public static bool Exists => _instance != null;

        public static GameState State => Instance._state;
        public static IRoomSync Room => Instance._room;

        GameState _state;
        IRoomSync _room;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null; // 關掉 Domain Reload 時也安全

        static void Create()
        {
            var go = new GameObject("[GameSession]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameSession>();
            _instance._state = new GameState(GameDataProvider.Data);
            // 單人版：本機房間。線上四人版改成 FirebaseRestRoomSync（見 Rooms/）。
            _instance._room = new LocalRoomSync();
            _instance._room.CreateRoom(null, null, _ => { });
            _instance.MirrorToRoom();
        }

        /// <summary>把本機狀態同步到房間（單人版寫進記憶體；線上版會送到 Firebase）。</summary>
        void MirrorToRoom()
        {
            _state.DesignChanged += _ => _room.PushDesign(_state.Design);
            _state.PhaseChanged += p => _room.PushPhase(p.ToString().ToLowerInvariant());
            _state.RollCallCompleted += votes =>
            {
                foreach (var v in votes) _room.PushVote(v.Role, v.Vote, v.ReasonCode);
            };
            _state.ChoiceMade += (id, c) => _room.PushChoice(id, c);
        }

        void Update() => _room?.Tick(Time.unscaledDeltaTime);

        /// <summary>直接開某個場景測試時，自動開一局預設情境。</summary>
        public static void EnsureScenario()
        {
            if (State.Scenario == null)
                State.StartScenario("earth_observation", "engineer", 1);
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
