using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GoNoGo.Core
{
    public enum SessionPhase { Lobby, Briefing, Design, Launch, Ops, Report }

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

    /// <summary>一個玩家。不分組：所有人開局都在中控室，自由走動，分工自己決定。</summary>
    public sealed class PlayerInfo
    {
        public int Id { get; set; }
        public AvatarLook Look { get; set; } = new AvatarLook();
        /// <summary>大廳裡按了「準備」；全員準備好房主才能開始。</summary>
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

    /// <summary>
    /// 多人的 Go/No-Go 點名：有人按發射就開始，每個人各喊 GO 或 NO-GO，全員 GO 才真的發射。
    /// 有人喊 NO-GO 就 HOLD（回到設計）。點名中設計主螢幕和整合台都鎖住。
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
    /// 組裝台右側正在組的零件：左側的小零件組合被認出是某個配方之後，照提示的順序把小零件一個一個裝到右側。
    /// </summary>
    public sealed class BenchJob
    {
        public string PartId { get; set; } = "";
        /// <summary>已經裝好幾個小零件（照配方的順序）。</summary>
        public int Step { get; set; }
        /// <summary>這個零件裝錯了幾次。</summary>
        public int Mistakes { get; set; }
        /// <summary>不屬於這個零件、硬裝上去報廢掉的小零件（再去架子拿就有）。</summary>
        public List<string> Scrapped { get; set; } = new List<string>();
        /// <summary>全部裝好了：成品放在組裝台右側，等人拿去裝上衛星。</summary>
        public bool Done { get; set; }
        /// <summary>裝了幾次（不管對錯）。畫面看這個數字有沒有變，決定要不要播音效、跳提示。</summary>
        public int Fits { get; set; }
        /// <summary>最近一次裝的是哪個小零件、誰裝的、對不對。</summary>
        public string LastComp { get; set; } = "";
        public int LastBy { get; set; }
        public bool LastOk { get; set; }
    }

    /// <summary>左側擺的小零件組合現在的狀態（<see cref="SessionRules.BenchStatusOf"/>）。</summary>
    public enum BenchStatus
    {
        /// <summary>左側是空的。</summary>
        Empty,
        /// <summary>是某個配方的一部分，還沒放齊。</summary>
        Partial,
        /// <summary>對不上任何配方：組合錯誤，小零件留在左側讓玩家調整。</summary>
        Unknown,
        /// <summary>對得上某個配方，但設計裡沒有零件用它，或用它的零件都組好了：這不是這次設計選的零件。</summary>
        NotInDesign,
        /// <summary>對得上配方，也是設計選的零件，但設計還沒在設計主螢幕上確定。</summary>
        Waiting,
        /// <summary>認出配方了：照提示的順序在右側組裝。</summary>
        Active,
        /// <summary>組好了，成品等人拿走。</summary>
        Done,
    }

    /// <summary>
    /// 一座組裝台（共兩座，不分配，誰都能用）。分左右兩側：小零件先放到左側，系統認出配方後，
    /// 照提示的順序拿到右側一個一個裝上去。
    /// </summary>
    public sealed class BenchState
    {
        /// <summary>
        /// 左側的格子各擺什麼小零件（第幾格 → 小零件 id，空字串 = 空格；最多 <see cref="GameData.BenchSlots"/> 格）。
        /// 玩家自己挑格子放，拿走一個也不會讓別格往前補。
        /// </summary>
        public List<string> Slots { get; set; } = new List<string>();

        /// <summary>左側擺著的小零件（照格子順序，不重複）。配方只看有哪些，不看擺在哪一格。</summary>
        [JsonIgnore]
        public List<string> Left => Slots.Where(id => !string.IsNullOrEmpty(id)).ToList();

        public bool Holds(string compId) => Slots.Contains(compId);

        /// <summary>第幾格擺的小零件（空格 = null）。</summary>
        public string At(int slot) => slot >= 0 && slot < Slots.Count && !string.IsNullOrEmpty(Slots[slot]) ? Slots[slot] : null;

        /// <summary>第一個空格（都滿了 = -1）。</summary>
        public int FirstFree()
        {
            for (int i = 0; i < GameData.BenchSlots; i++)
                if (At(i) == null) return i;
            return -1;
        }

        public void Place(string compId, int slot)
        {
            while (Slots.Count < GameData.BenchSlots) Slots.Add("");
            Slots[slot] = compId;
        }

        /// <summary>放到第一個空格；滿了就放不下（回傳 false）。</summary>
        public bool PlaceInFirstFree(string compId)
        {
            int slot = FirstFree();
            if (slot < 0) return false;
            Place(compId, slot);
            return true;
        }

        public void Take(string compId)
        {
            int i = Slots.IndexOf(compId);
            if (i >= 0) Slots[i] = "";
        }

        public void ClearSlots() => Slots.Clear();
        /// <summary>右側正在組的零件（null = 還沒認出配方）。</summary>
        public BenchJob Job { get; set; }
        /// <summary>左側放了東西之後被判定為錯誤的次數。畫面看這個數字有沒有變，決定要不要跳「組合錯誤」。</summary>
        public int Errors { get; set; }
        /// <summary>最近一次錯誤是哪一種、誰放的。</summary>
        public BenchStatus LastError { get; set; }
        public int LastBy { get; set; }
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

        /// <summary>
        /// 單人模式（個人任務，或多人房開始時只有一個人）：挖空的數值比多人少。開始任務時定下來，之後有人離開也不變
        /// （挖空的格子中途不會換一批）。
        /// </summary>
        public bool Solo { get; set; } = true;
        /// <summary>線索擺放位置的亂數種子（內容每局固定，位置每局隨機）。房主開局時決定。</summary>
        public int ClueSeed { get; set; }

        /// <summary>
        /// 設計主螢幕上選的設計：零件、軌道、火箭、地面站、年數。發射的就是它
        /// （發射條件：選的每個零件都組好、裝上衛星）。
        /// </summary>
        public Design Bench { get; set; } = new Design();
        /// <summary>設計確定了：設計主螢幕和組裝區儀表板改成顯示每個零件要用哪些小零件，組裝台開始認配方。</summary>
        public bool Confirmed { get; set; }
        /// <summary>確定過幾次（畫面用來跳「設計確定了」的提示）。</summary>
        public int Confirms { get; set; }
        /// <summary>玩家在設計主螢幕上填的數值（"零件 id/欄位" → 數值和誰填的）。兩個人填同一格，後填的蓋掉先填的。發射失敗重做也保留。</summary>
        public Dictionary<string, FilledValue> Filled { get; set; } = new Dictionary<string, FilledValue>();

        /// <summary>
        /// 組好的零件：在衛星上的、手上拿著的、組裝台上等人拿的，還有組好之後又被換掉的（留著，改選回來就直接裝回衛星）。
        /// </summary>
        public List<string> Built { get; set; } = new List<string>();
        /// <summary>裝在整合台衛星上的零件。</summary>
        public List<string> Installed { get; set; } = new List<string>();
        /// <summary>兩座組裝台。</summary>
        public List<BenchState> Benches { get; set; } = new List<BenchState>();
        /// <summary>
        /// 誰手上拿著哪些小零件（玩家 id → 小零件 id）。單人有籃子，最多 <see cref="GameData.BasketSize"/> 個；
        /// 兩人以上每人一次拿一個。手上不是零件就是小零件，不會兩種都有。
        /// </summary>
        public Dictionary<int, List<string>> Comps { get; set; } = new Dictionary<int, List<string>>();
        /// <summary>誰手上拿著哪個組好的零件（玩家 id → 零件 id），正要拿去裝上衛星。</summary>
        public Dictionary<int, string> Held { get; set; } = new Dictionary<int, string>();
        /// <summary>這一局組過的構造（<see cref="AssemblyRecipe.Key"/>）。發射失敗重做也保留。</summary>
        public List<string> Learned { get; set; } = new List<string>();
        /// <summary>一次都沒裝錯就組好的構造（<see cref="AssemblyRecipe.Key"/>）：知識卡升級成「精通」。</summary>
        public List<string> Flawless { get; set; } = new List<string>();
        /// <summary>這一局總共裝錯了幾次小零件。沒有懲罰（不扣分），只是紀錄；所有人照樣看得到誰裝錯。</summary>
        public int Mistakes { get; set; }
        /// <summary>發射失敗後整局重來的次數（畫面用來跳出「任務重來」的提示）。</summary>
        public int Restarts { get; set; }

        public List<LaunchRecord> Launches { get; set; } = new List<LaunchRecord>();

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

        static readonly List<string> NoComps = new List<string>();
        /// <summary>這個玩家手上的小零件（沒有 = 空清單；不要改回傳的清單）。</summary>
        public List<string> CompsOf(int player) => Comps.TryGetValue(player, out var list) && list != null ? list : NoComps;
        /// <summary>這個玩家手上的第一個小零件（裝上去的就是它）；沒有 = null。</summary>
        public string HeldCompBy(int player) => CompsOf(player).Count > 0 ? CompsOf(player)[0] : null;

        public BenchState BenchAt(int index) => index >= 0 && index < Benches.Count ? Benches[index] : null;

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
