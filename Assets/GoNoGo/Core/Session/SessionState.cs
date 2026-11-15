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
        /// <summary>批准後會是第幾單（跟採購紀錄的編號一致；退回再送還是同一個號碼）。</summary>
        public int N { get; set; }
        /// <summary>第幾次送審（每送一次加一），畫面用來分辨是不是新的一張單。</summary>
        public int Seq { get; set; }
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
    /// 組裝工作台上正在組的那一箱：玩家把箱子放上工作台，再到零件盤一個一個拿小零件回來裝。
    /// 全隊共用一張工作台（一次組一箱），誰拿、誰裝都可以。
    /// </summary>
    public sealed class BenchJob
    {
        public string PartId { get; set; } = "";
        /// <summary>已經裝好幾個小零件（照配方的順序）。</summary>
        public int Step { get; set; }
        /// <summary>這一箱裝錯了幾次。</summary>
        public int Mistakes { get; set; }
        /// <summary>不屬於這個零件、硬裝上去報廢掉的小零件（零件盤上那一格就空了）。</summary>
        public List<string> Scrapped { get; set; } = new List<string>();
        /// <summary>全部裝好了：成品放在工作台上，等人拿走。</summary>
        public bool Done { get; set; }
        /// <summary>裝了幾次（不管對錯）。畫面看這個數字有沒有變，決定要不要播音效、跳提示。</summary>
        public int Fits { get; set; }
        /// <summary>最近一次裝的是哪個小零件、誰裝的、對不對。</summary>
        public string LastComp { get; set; } = "";
        public int LastBy { get; set; }
        public bool LastOk { get; set; }
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
        /// <summary>
        /// 已經在組裝工作台組好的零件。買來的零件是一箱小零件，要先組好才能裝上船；
        /// 開局就在船上的零件本來就是組好的。組好之後拆下來、放回去都不用再組。
        /// </summary>
        public List<string> Built { get; set; } = new List<string>();
        /// <summary>組裝工作台上正在組的那一箱（null = 工作台是空的）。</summary>
        public BenchJob Workbench { get; set; }
        /// <summary>誰手上拿著哪個小零件（玩家 id → 小零件 id）。手上不是零件就是小零件，不會兩個都有。</summary>
        public Dictionary<int, string> HeldComp { get; set; } = new Dictionary<int, string>();
        /// <summary>
        /// 這一局學會的構造（<see cref="AssemblyRecipe.Key"/>）：同一種構造親手組過一次，之後同類的零件到貨就是成品，
        /// 不用重複做一樣的事（發射失敗重來也不用再組）。
        /// </summary>
        public List<string> Learned { get; set; } = new List<string>();
        /// <summary>一次都沒裝錯就組好的構造（<see cref="AssemblyRecipe.Key"/>）：知識卡升級成「精通」。</summary>
        public List<string> Flawless { get; set; } = new List<string>();
        /// <summary>
        /// 這一局總共裝錯了幾次小零件。每一次都要拆下來重裝，花掉時程（<see cref="Constants.ReworkMonths"/>）；
        /// 發射失敗重來也不會歸零（花掉的時間拿不回來）。
        /// </summary>
        public int Rework { get; set; }
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
        public string HeldCompBy(int player) => HeldComp.TryGetValue(player, out var id) ? id : null;
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
