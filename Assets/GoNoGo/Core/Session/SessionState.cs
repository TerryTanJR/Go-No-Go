using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GoNoGo.Core
{
    public enum SessionPhase { Lobby, Briefing, Design, Launch, Ops, Report }

    /// <summary>玩家的起始位置（也是發射失敗後回去的地方）。</summary>
    public enum Station { Assembly, Control }

    /// <summary>Q 版 avatar 的外型：幾個整數加名字，多人時同步給其他人。</summary>
    public sealed class AvatarLook
    {
        public const int Suits = 8, Helmets = 3, Visors = 4, Accessories = 4, MaxName = 12;
        public string Name { get; set; } = "";
        public int Suit { get; set; }
        public int Helmet { get; set; }
        public int Visor { get; set; }
        public int Accessory { get; set; }

        public AvatarLook Clone() => (AvatarLook)MemberwiseClone();

        public AvatarLook Sanitized()
        {
            int Wrap(int v, int n) => ((v % n) + n) % n;
            var n2 = (Name ?? "").Trim();
            if (n2.Length > MaxName) n2 = n2.Substring(0, MaxName);
            return new AvatarLook { Name = n2, Suit = Wrap(Suit, Suits), Helmet = Wrap(Helmet, Helmets), Visor = Wrap(Visor, Visors), Accessory = Wrap(Accessory, Accessories) };
        }
    }

    public sealed class PlayerInfo
    {
        public int Id { get; set; }
        public AvatarLook Look { get; set; } = new AvatarLook();
        public Station Station { get; set; }
    }

    /// <summary>一次發射：用了哪個設計、哪個種子、結果如何。因果鏈需要時用同樣的輸入重算（結果一模一樣）。</summary>
    public sealed class LaunchRecord
    {
        public int Attempt { get; set; }
        public Design Design { get; set; } = new Design();
        public int Seed { get; set; }
        public int FailedBefore { get; set; }
        public LaunchOutcome Outcome { get; set; }
        public string ReasonCode { get; set; } = "";
        public bool Reached { get; set; }
    }

    /// <summary>打過的電話：哪個處室、問了哪一題。</summary>
    public sealed class CallRecord
    {
        public string Department { get; set; } = "";
        public string LineId { get; set; } = "";
    }

    /// <summary>
    /// 一局的完整狀態（純資料，可以整包序列化）。多人時房主持有正本，每次改變後把整包廣播給其他人；
    /// 評估、模擬結果都不放在這裡，各台電腦用一樣的輸入自己算（核心是決定性的）。
    /// </summary>
    public sealed class SessionState
    {
        public int Revision { get; set; }
        public string ScenarioId { get; set; } = "";
        public string DifficultyId { get; set; } = "";
        public bool Tutorial { get; set; }
        public bool Multiplayer { get; set; }
        public SessionPhase Phase { get; set; } = SessionPhase.Lobby;
        public List<PlayerInfo> Players { get; set; } = new List<PlayerInfo>();

        /// <summary>整合台上實際裝的設計（發射的就是它）。軌道、火箭、地面站、年數由中控室設定，兩份設計同步。</summary>
        public Design Bench { get; set; } = new Design();
        /// <summary>最後一次從回報台送到中控室的設計（儀表板看的是它）。</summary>
        public Design Reported { get; set; } = new Design();
        public int Reports { get; set; }
        /// <summary>誰手上拿著哪個零件（玩家 id → 零件 id）。</summary>
        public Dictionary<int, string> Held { get; set; } = new Dictionary<int, string>();

        public List<CallRecord> Calls { get; set; } = new List<CallRecord>();
        public List<LaunchRecord> Launches { get; set; } = new List<LaunchRecord>();

        /// <summary>營運：目前第幾回合、各事件的判斷與操作、這回合是否已結算。</summary>
        public int OpsTurn { get; set; }
        public bool TurnResolved { get; set; }
        public Dictionary<string, EventResponse> Responses { get; set; } = new Dictionary<string, EventResponse>();

        [JsonIgnore] public LaunchRecord LastLaunch => Launches.Count > 0 ? Launches[Launches.Count - 1] : null;
        [JsonIgnore] public int FailedLaunches => Launches.Count(l => !l.Reached);

        public PlayerInfo Player(int id) => Players.FirstOrDefault(p => p.Id == id);
        public string HeldBy(int player) => Held.TryGetValue(player, out var id) ? id : null;
        public int? HolderOf(string partId) => Held.Where(kv => kv.Value == partId).Select(kv => (int?)kv.Key).FirstOrDefault();

        static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
        };

        public string ToJson() => JsonConvert.SerializeObject(this, Json);
        public static SessionState FromJson(string json) => JsonConvert.DeserializeObject<SessionState>(json, Json);
        public SessionState Clone() => FromJson(ToJson());
    }
}
