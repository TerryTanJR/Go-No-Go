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
        public const int Suits = 8, Accents = 8, Helmets = 3, Visors = 4, Accessories = 5, Lowers = 5, MaxName = 12;
        public string Name { get; set; } = "";
        public int Suit { get; set; }
        public int Helmet { get; set; }
        public int Visor { get; set; }
        public int Accessory { get; set; }
        /// <summary>點綴色（條紋、脖圈、袖口…）。0 = 國際橘。</summary>
        public int Accent { get; set; }
        /// <summary>下半身：0 腳、1 輪子、2 懸浮、3 履帶、4 彈簧。</summary>
        public int Lower { get; set; }

        public AvatarLook Clone() => (AvatarLook)MemberwiseClone();

        public AvatarLook Sanitized()
        {
            int Wrap(int v, int n) => ((v % n) + n) % n;
            var n2 = (Name ?? "").Trim();
            if (n2.Length > MaxName) n2 = n2.Substring(0, MaxName);
            return new AvatarLook { Name = n2, Suit = Wrap(Suit, Suits), Helmet = Wrap(Helmet, Helmets), Visor = Wrap(Visor, Visors), Accessory = Wrap(Accessory, Accessories), Accent = Wrap(Accent, Accents), Lower = Wrap(Lower, Lowers) };
        }
    }

    public sealed class PlayerInfo
    {
        public int Id { get; set; }
        public AvatarLook Look { get; set; } = new AvatarLook();
        public Station Station { get; set; }
        /// <summary>大廳裡按了「準備」：準備好就不能換組（要換先取消準備）；全員準備好房主才能開始。</summary>
        public bool Ready { get; set; }
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

    /// <summary>一次採購（下單成功）：買了哪些零件。原價，沒有倍率；買了就不能退。</summary>
    public sealed class OrderRecord
    {
        public int N { get; set; }
        /// <summary>送單的人（組裝區）。</summary>
        public int By { get; set; }
        /// <summary>多人時批准的人（中控室）；個人模式是 0。</summary>
        public int ApprovedBy { get; set; }
        public List<string> Parts { get; set; } = new List<string>();
    }

    /// <summary>多人的採購單：組裝區送審，等中控室批准（下單）或退回。</summary>
    public sealed class OrderRequest
    {
        public int N { get; set; }
        public int By { get; set; }
        public List<string> Parts { get; set; } = new List<string>();
    }

    /// <summary>打過的電話：哪個處室、問了哪一題。</summary>
    public sealed class CallRecord
    {
        public string Department { get; set; } = "";
        public string LineId { get; set; } = "";
    }

    /// <summary>
    /// 多人的 Go/No-Go 點名：有人按發射就開始，每個人各喊 GO 或 NO-GO，全員 GO 才真的發射。
    /// 有人喊 NO-GO 就 HOLD（回到設計）。點名中整合台和中控室的設定都鎖住。
    /// </summary>
    public sealed class GoPoll
    {
        public int Id { get; set; }
        public int CalledBy { get; set; }
        /// <summary>玩家 id → true = GO、false = NO-GO。</summary>
        public Dictionary<int, bool> Votes { get; set; } = new Dictionary<int, bool>();
        /// <summary>結束後：全員 GO（發射）或 HOLD。</summary>
        public bool Done { get; set; }
        public bool Go { get; set; }
        /// <summary>HOLD 時是誰喊了 NO-GO（0 = 沒有）。</summary>
        public int NoGoBy { get; set; }
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
        /// <summary>
        /// 擁有的零件（不含地面站）：開局零件加上買過的。開發費 = 基本開發費 + 這些零件，裝不裝都一樣（採購機制 v2）。
        /// 組裝區的零件架上只放得到這些；其他零件要先下單採購。
        /// </summary>
        public List<string> Owned { get; set; } = new List<string>();
        /// <summary>組裝區採購終端的購物車（還沒下單）。</summary>
        public List<string> Cart { get; set; } = new List<string>();
        public List<OrderRecord> Orders { get; set; } = new List<OrderRecord>();
        /// <summary>多人：等中控室審查的採購單（null = 沒有）。一次只能有一張。</summary>
        public OrderRequest Request { get; set; }
        /// <summary>多人：最近一張被退回的採購單（組裝區顯示「中控室退回了」；下一次送審時清掉）。</summary>
        public OrderRequest Rejected { get; set; }
        public int Requests { get; set; }
        /// <summary>發射失敗後整局重來的次數（畫面用來跳出「任務重來」的提示）。</summary>
        public int Restarts { get; set; }
        /// <summary>誰手上拿著哪個零件（玩家 id → 零件 id）。</summary>
        public Dictionary<int, string> Held { get; set; } = new Dictionary<int, string>();

        public List<CallRecord> Calls { get; set; } = new List<CallRecord>();
        public List<LaunchRecord> Launches { get; set; } = new List<LaunchRecord>();

        /// <summary>嚴格分工（房主在大廳開）：組裝區的人不能操作中控室，反過來也一樣；設計階段兩區之間的門鎖住。</summary>
        public bool StrictRoles { get; set; }
        /// <summary>進行中的 Go/No-Go 點名（null = 沒有）。</summary>
        public GoPoll Poll { get; set; }
        /// <summary>上一次點名的結果（HUD 顯示 HOLD 的原因）。</summary>
        public GoPoll LastPoll { get; set; }
        public int Polls { get; set; }

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
