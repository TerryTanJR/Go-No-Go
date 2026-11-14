using GoNoGo.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoNoGo.Game
{
    /// <summary>
    /// 跨場景存在的唯一物件：持有這一局（<see cref="MissionSession"/>）與操作的去處（<see cref="ISessionLink"/>），
    /// 並依階段切換場景。直接按 Play 開任何一個場景也會自動建立，方便單獨測試。
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

        public static GameData Data => GameDataProvider.Data;
        public static MissionSession Mission => Instance._mission;
        public static ISessionLink Link => Instance._link;
        public static bool HasMission => _instance != null && _instance._mission != null;
        public static int LocalPlayer => _instance?._link?.LocalPlayer ?? 1;
        public static bool IsHost => _instance?._link?.IsHost ?? true;

        MissionSession _mission;
        ISessionLink _link;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _instance = null; // 關掉 Domain Reload 時也安全

        static void Create()
        {
            var go = new GameObject("[GameSession]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameSession>();
        }

        // ---------------- 開局與結束 ----------------

        /// <summary>個人任務或新手訓練。</summary>
        public static void StartSolo(string scenarioId, string difficultyId, bool tutorial = false, bool loadScene = true)
        {
            var s = SessionRules.NewSession(Data, scenarioId, difficultyId, tutorial, false);
            var m = new MissionSession(Data, s);
            Begin(m, new LocalLink(m, 1));
            Send(new Command { Type = CommandType.Join, Look = Profile.Look.Clone() });
            if (loadScene) SceneFlow.Go(SceneFlow.Facility);
        }

        /// <summary>
        /// 沙盒模式：沒有預設限制的自由設計。designCode 有給就把那份設計（圖庫裡存的、或別人分享的）直接放上整合台。
        /// </summary>
        public static void StartSandbox(string designCode = null, bool loadScene = true)
        {
            StartSolo(Data.Sandbox.Scenario.Id, null, false, loadScene);
            if (!string.IsNullOrEmpty(designCode)) Send(CommandType.LoadDesign, designCode);
        }

        /// <summary>網頁版：瀏覽器把剪貼簿的文字送回來（見 <see cref="ClipboardUtil"/>）。</summary>
        public void OnClipboardText(string text) => ClipboardUtil.Deliver(text);

        /// <summary>多人（GoNoGo.Net 呼叫）：換上網路版的 link。</summary>
        public static void Begin(MissionSession m, ISessionLink link)
        {
            var i = Instance;
            i.Detach();
            i._link?.Shutdown();
            i._mission = m;
            i._link = link;
            Codex.BeginMission();
            m.PhaseChanged += i.OnPhaseChanged;
        }

        /// <summary>回主選單。</summary>
        public static void EndMission(bool loadMenu = true)
        {
            if (_instance == null) return;
            _instance.Detach();
            _instance._link?.Shutdown();
            _instance._link = null;
            _instance._mission = null;
            if (loadMenu) SceneFlow.Go(SceneFlow.MainMenu);
        }

        void Detach()
        {
            if (_mission != null) _mission.PhaseChanged -= OnPhaseChanged;
        }

        // ---------------- 送操作 ----------------

        public static void Send(Command c)
        {
            if (!HasMission) return;
            c.Player = LocalPlayer;
            _instance._link.Send(c);
        }

        public static void Send(CommandType t, string a = "", int n = 0) => Send(Command.Of(t, 0, a, n));

        /// <summary>直接開某個場景測試時，自動開一局預設任務（不換場景）。</summary>
        public static void EnsureMission()
        {
            if (HasMission) return;
            StartSolo("earth_observation", null, false, false);
            Send(CommandType.BeginDesign);
        }

        // ---------------- 依階段換場景 ----------------

        void OnPhaseChanged(SessionPhase before, SessionPhase now)
        {
            string target = SceneFor(now);
            if (target != null && SceneManager.GetActiveScene().name != target) SceneFlow.Go(target);
        }

        public static string SceneFor(SessionPhase p) => p switch
        {
            SessionPhase.Launch => SceneFlow.Launch,
            SessionPhase.Report => SceneFlow.Report,
            _ => SceneFlow.Facility,
        };

        void Update() => _link?.Tick(Time.unscaledDeltaTime);

        void OnDestroy()
        {
            if (_instance != this) return;
            _link?.Shutdown();
            _instance = null;
        }
    }
}
