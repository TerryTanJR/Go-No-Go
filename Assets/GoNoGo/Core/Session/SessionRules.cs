using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>
    /// 操作的編號。多人時用編號傳給房主，所以留著的操作編號不變；刪掉的操作（選組、回報、電話、採購…）編號空著不重複使用。
    /// </summary>
    public enum CommandType
    {
        // 房間（3 = 已刪除的選組 SetStation）
        Join = 0, Leave = 1, SetLook = 2, StartMission = 4, BeginDesign = 5,
        // 成品：從組裝台拿走組好的零件、裝上衛星（9 = 已刪除的回報 Report）
        Pick = 6, Drop = 7, Install = 8,
        // 大屏幕的任務設定與發射（14 = 已刪除的電話 Call）
        SetOrbit = 10, SetLauncher = 11, SetGroundStation = 12, SetYears = 13, Launch = 15, FinishLaunch = 16,
        // 營運
        Judge = 17, Act = 18, EndTurn = 19, NextTurn = 20,
        // Go/No-Go 點名（21、22 = 已刪除的嚴格分工、環境測試）
        Vote = 23,
        // 大廳（26–28、30–32 = 已刪除的採購：購物車、下單、批准、退回、撤回）
        SetReady = 29,
        // 營運小遊戲：交出自己的成績（A = 遊戲 id，N = 分數 0 ~ 1000）
        MiniDone = 33,
        // 沙盒：把一份存過（或別人分享）的設計直接放上大屏幕（A = 設計分享碼）。34 = 已刪除的舊組裝
        LoadDesign = 35,
        // 小零件：從架子拿一個（A = 小零件 id）；把手上的放回架子（A = 小零件 id，空白 = 全部）；
        // 把手上的小零件裝到組裝台右側（N = 第幾座組裝台）。36 = 已刪除的「把箱子放上工作台」
        CompPick = 37, CompDrop = 38, CompFit = 39,
        // 流程改版：大屏幕選件（A = 零件 id，N = 1 勾選 / 0 取消）、填數值（A = 零件 id|欄位|數值）、
        // 確定設計（N = 1 確定 / 0 改選）、把手上的小零件放到組裝台左側（N = 第幾座）、
        // 從組裝台左側拿一個小零件（N = 第幾座，A = 小零件 id）
        SelectPart = 40, Fill = 41, Confirm = 42, BenchPut = 43, BenchTake = 44,
    }

    /// <summary>
    /// 玩家的一個操作。個人模式在本機直接套用；多人模式送給房主，房主套用後把新狀態廣播出去。
    /// 欄位刻意很少，方便用網路傳。
    /// </summary>
    public sealed class Command
    {
        public CommandType Type { get; set; }
        public int Player { get; set; }
        public string A { get; set; } = "";
        public int N { get; set; }
        public AvatarLook Look { get; set; }

        public static Command Of(CommandType t, int player, string a = "", int n = 0) => new Command { Type = t, Player = player, A = a ?? "", N = n };
    }

    /// <summary>
    /// 這一局的規則：操作能不能做、做了狀態怎麼變。純函式，房主和個人模式都呼叫這裡，所以兩邊行為一模一樣。
    /// 回傳空字串表示成功，否則是「為什麼不行」的字串鍵（畫面顯示成通知）。
    ///
    /// 流程：選件（中控室大屏幕勾選零件、找線索把挖空的數值填回去）→ 確定設計 → 組裝（組裝區：架子拿小零件 →
    /// 組裝台左側認配方、右側照順序組 → 裝上衛星）→ 回中控室按發射（多人先 Go/No-Go 點名）→ 入軌就營運，失敗回到選件重做。
    /// </summary>
    public static class SessionRules
    {
        public const string Ok = "";

        // ---------------------------------------------------------------- 開局

        /// <summary>
        /// 開一局。難度跟著關卡走（一關一個難度）：difficultyId 傳 null 就用關卡的難度；
        /// 只有新手訓練（和測試）會指定別的難度。clueSeed 決定線索擺在哪裡（內容每局固定，位置每局隨機）。
        /// </summary>
        public static SessionState NewSession(GameData data, string scenarioId, string difficultyId, bool tutorial, bool multiplayer, int clueSeed = 0)
        {
            var scenario = data.Scenario(scenarioId) ?? data.Scenarios[0];
            // 一關一個難度。歷史關卡不分難度：不管傳進什麼，都用關卡指定的
            var diff = (scenario.Historical != null ? null : data.Difficulty(difficultyId)) ?? data.DifficultyFor(scenario);
            var s = new SessionState
            {
                ScenarioId = scenario.Id,
                DifficultyId = diff.Id,
                Tutorial = tutorial,
                Multiplayer = multiplayer,
                Phase = multiplayer ? SessionPhase.Lobby : SessionPhase.Briefing,
                Solo = true,
                ClueSeed = clueSeed,
                Bench = StartDesign(scenario, diff),
            };
            StartAssembly(s, data);
            return s;
        }

        /// <summary>開局設計（大屏幕上一開始勾的）：關卡（或這個難度）的預設設計，再換上難度給的零件。歷史關卡用當年的設計。發射失敗重來也回到這裡。</summary>
        public static Design StartDesign(ScenarioDef scenario, DifficultyDef diff)
        {
            var start = (scenario.Historical != null ? scenario.Historical.PresetDesign : GameData.DefaultDesign(scenario, diff.Id)).Clone();
            start.ScenarioId = scenario.Id;
            if (scenario.Historical == null)
            {
                // 一般關卡：零件一個都不先勾，全部由玩家自己選（軌道、火箭、地面站、年數留預設，儀表板才算得出東西）。
                // 新手訓練（難度有先組好的類別）維持整套預設設計。
                if (diff.PrebuiltCategories.Count == 0)
                {
                    start.Instruments.Clear();
                    foreach (var cat in start.Parts.Keys.Where(k => k != PartCategory.GroundStation).ToList()) start.Parts.Remove(cat);
                }
                foreach (var kv in diff.StartParts) start.Parts[kv.Key] = kv.Value;
            }
            return start;
        }

        /// <summary>
        /// 開局時的衛星：一般關卡是空的（選好、確定設計之後才一個一個組起來裝上去）；
        /// 歷史關卡是當年的衛星（整艘組好了，設計也算確定，簡報完直接發射）；
        /// 難度可以指定幾類零件先組好（新手訓練）。沙盒不用組裝，選了就在衛星上。
        /// </summary>
        static void StartAssembly(SessionState s, GameData data)
        {
            var sc = s.Scenario(data);
            var diff = s.Difficulty(data);
            s.Built = new List<string>();
            s.Installed = new List<string>();
            s.Benches = Enumerable.Range(0, GameData.BenchCount).Select(_ => new BenchState()).ToList();
            s.Comps.Clear();
            s.Held.Clear();
            s.Confirmed = false;
            if (sc?.Historical != null)
            {
                s.Built = SelectedParts(s, data);
                s.Confirmed = true;
            }
            else
                foreach (var id in SelectedParts(s, data))
                    if (diff.PrebuiltCategories.Contains(data.Part(id).Category)) s.Built.Add(id);
            SyncAssembly(s, data);
        }

        /// <summary>
        /// 任務年數上限 = 關卡目標年數 + 2。科學點數隨年數線性成長，不設上限的話「把年數調到壽命允許的最大值」永遠最好。
        /// </summary>
        public static int MaxMissionYears(ScenarioDef scenario) =>
            scenario != null && scenario.Sandbox ? SandboxMaxYears
            : Math.Max(1, (int)Math.Floor(scenario?.TargetYears ?? 8) + 2);

        /// <summary>沙盒的任務年數上限（不看目標年數）。</summary>
        public const int SandboxMaxYears = 15;

        public static ScenarioDef Scenario(this SessionState s, GameData data) => data.Scenario(s.ScenarioId);
        public static DifficultyDef Difficulty(this SessionState s, GameData data) =>
            data.Difficulty(s.DifficultyId) ?? data.SelectableDifficulties.First();

        public static bool IsSandbox(this SessionState s, GameData data) => s.Scenario(data)?.Sandbox == true;

        /// <summary>歷史關卡：第一次發射（重演歷史）之前，大屏幕鎖在當年的選擇，不能改。</summary>
        public static bool DesignLocked(this SessionState s, GameData data) =>
            s.Scenario(data)?.Historical != null && s.Launches.Count == 0;

        /// <summary>現在是不是單人（個人任務，或多人房只剩一個人）：小零件用籃子裝，一次最多 <see cref="GameData.BasketSize"/> 個。</summary>
        public static bool Alone(this SessionState s) => !s.Multiplayer || s.Players.Count <= 1;

        /// <summary>一次能拿幾個小零件：單人有籃子（4 個），兩人以上每人一次拿一個。</summary>
        public static int CarryCapacity(SessionState s) => s.Alone() ? GameData.BasketSize : 1;

        public static double Budget(SessionState s, GameData data) => GameData.Budget(s.Scenario(data));

        /// <summary>
        /// 營運經費撐得了幾年：用大屏幕上選的設計算（預算 − 開發費 − 火箭，再除以每年營運費）。
        /// 成本只算目前勾選的零件：拿過的小零件、換掉的零件都不扣錢。
        /// </summary>
        public static double FundedYears(SessionState s, GameData data) =>
            Simulator.FundedYears(Evaluator.EvaluateDesign(s.Bench, data, s.DifficultyId, false));

        /// <summary>
        /// 這一關大屏幕上列得出來的零件（不含地面站）：這個難度開放的，加上目前設計和開局設計裡的。
        /// </summary>
        public static List<PartDef> Catalog(GameData data, SessionState s)
        {
            var sc = s.Scenario(data);
            var diff = s.Difficulty(data);
            var ids = new HashSet<string>(data.ShelfFor(sc, diff).Select(p => p.Id));
            ids.UnionWith(s.Bench.AllPartIds());
            if (sc != null) ids.UnionWith(StartDesign(sc, diff).AllPartIds());
            return data.Parts.Where(p => p.Category != PartCategory.GroundStation && ids.Contains(p.Id)).ToList();
        }

        /// <summary>
        /// 共乘只能去主酬載的軌道：選了共乘就不能換到別的軌道，在別的軌道也不能選共乘。
        /// 回傳 Ok 或 err.rideshare_orbit。
        /// </summary>
        public static string OrbitLauncherCheck(GameData data, string orbitId, string launcherId)
        {
            var l = data.Launcher(launcherId);
            if (l != null && !string.IsNullOrEmpty(l.OrbitLock) && l.OrbitLock != orbitId) return "err.rideshare_orbit";
            return Ok;
        }

        public static MissionContext Context(GameData data, SessionState s, int failedBefore) => new MissionContext
        {
            DifficultyId = s.DifficultyId,
            Requirements = Departments.Active(s.Scenario(data), s.Difficulty(data)),
            FailedLaunchesBefore = failedBefore,
        };

        // ---------------------------------------------------------------- 套用操作

        public static string Apply(SessionState s, GameData data, Command c)
        {
            string r = ApplyInner(s, data, c);
            if (r == Ok) s.Revision++;
            return r;
        }

        /// <summary>房主的玩家編號（多人時 = Netcode clientId 0 + 1；個人模式只有 1）。</summary>
        public const int HostPlayer = 1;

        /// <summary>點名中不能改設計、不能動衛星（要改就喊 NO-GO）。填數值不算改設計，點名中也可以。</summary>
        static bool ChangesDesign(CommandType t) =>
            t == CommandType.SelectPart || t == CommandType.Confirm || t == CommandType.LoadDesign || t == CommandType.Install
            || t == CommandType.SetOrbit || t == CommandType.SetLauncher || t == CommandType.SetGroundStation || t == CommandType.SetYears;

        static bool DesignPhase(SessionState s) => s.Phase == SessionPhase.Design || s.Phase == SessionPhase.Briefing;

        static string ApplyInner(SessionState s, GameData data, Command c)
        {
            if (s.Poll != null && ChangesDesign(c.Type)) return "err.poll_active";
            switch (c.Type)
            {
                case CommandType.Join:
                    if (s.Player(c.Player) != null) return Ok;
                    if (s.Players.Count >= 4) return "err.room_full";
                    s.Players.Add(new PlayerInfo { Id = c.Player, Look = (c.Look ?? new AvatarLook()).Sanitized() });
                    return Ok;
                case CommandType.Leave:
                    s.Players.RemoveAll(p => p.Id == c.Player);
                    s.Held.Remove(c.Player);
                    s.Comps.Remove(c.Player);
                    // 手上拿著組好的零件就離開：零件直接裝上衛星，不會跟著人一起消失
                    SyncAssembly(s, data);
                    if (s.Poll != null)
                    {
                        s.Poll.Votes.Remove(c.Player);
                        CheckPoll(s, data);
                    }
                    return Ok;
                case CommandType.SetLook:
                    var pl = s.Player(c.Player);
                    if (pl == null || c.Look == null) return "err.no_player";
                    pl.Look = c.Look.Sanitized();
                    return Ok;
                case CommandType.SetReady:
                    var pr = s.Player(c.Player);
                    if (pr == null) return "err.no_player";
                    if (s.Phase != SessionPhase.Lobby) return "err.phase";
                    pr.Ready = c.N == 1;
                    return Ok;
                case CommandType.StartMission:
                    if (s.Phase != SessionPhase.Lobby) return "err.phase";
                    if (c.Player != HostPlayer) return "err.host_only";
                    // 多人房只有房主一個人：等人加入（一個人玩走單人模式）
                    if (s.Players.Count < 2) return "err.need_players";
                    if (s.Players.Any(p => !p.Ready)) return "err.not_ready";
                    // 多人：簡報是每個人自己看（本機面板），不卡全隊；直接進設計階段。所有人都從中控室開始
                    s.Solo = false;
                    s.Phase = SessionPhase.Design;
                    return Ok;
                case CommandType.BeginDesign:
                    if (s.Phase == SessionPhase.Design) return Ok;
                    if (s.Phase != SessionPhase.Briefing) return "err.phase";
                    s.Phase = SessionPhase.Design;
                    // 歷史關卡：簡報完直接照當年的設計發射（設計本來就鎖住，不用點名）
                    if (s.DesignLocked(data)) return Launch(s, data);
                    return Ok;

                case CommandType.SelectPart: return SelectPart(s, data, c.A, c.N == 1);
                case CommandType.Fill: return Fill(s, data, c.Player, c.A);
                case CommandType.Confirm: return Confirm(s, data, c.N == 1);
                case CommandType.LoadDesign: return LoadDesign(s, data, c.A);

                case CommandType.SetOrbit:
                {
                    var lock_ = OrbitLauncherCheck(data, c.A, s.Bench.LauncherId);
                    if (lock_ != Ok && s.Scenario(data).AllowedOrbits.Contains(c.A)) return lock_;
                    return Control(s, data, d => d.OrbitId = c.A, () => s.Scenario(data).AllowedOrbits.Contains(c.A));
                }
                case CommandType.SetLauncher:
                {
                    var lock_ = OrbitLauncherCheck(data, s.Bench.OrbitId, c.A);
                    if (lock_ != Ok && data.LaunchersFor(s.Scenario(data)).Any(l => l.Id == c.A)) return lock_;
                    return Control(s, data, d => d.LauncherId = c.A, () => data.LaunchersFor(s.Scenario(data)).Any(l => l.Id == c.A));
                }
                case CommandType.SetGroundStation:
                    return Control(s, data, d => d.Parts[PartCategory.GroundStation] = c.A, () => data.Part(c.A)?.Category == PartCategory.GroundStation);
                case CommandType.SetYears:
                    return Control(s, data, d => d.MissionYears = Math.Max(1, Math.Min(MaxMissionYears(s.Scenario(data)), c.N)), () => true);

                case CommandType.CompPick: return CompPick(s, data, c.Player, c.A);
                case CommandType.CompDrop: return CompDrop(s, c.Player, c.A);
                case CommandType.BenchPut: return BenchPut(s, data, c.Player, c.N);
                case CommandType.BenchTake: return BenchTake(s, data, c.Player, c.N, c.A);
                case CommandType.CompFit: return CompFit(s, data, c.Player, c.N);
                case CommandType.Pick: return Pick(s, data, c.Player, c.A);
                case CommandType.Drop:
                    // 組好的零件沒有地方放：拿起來就是要裝上衛星
                    return s.HeldBy(c.Player) != null ? "err.carry_part" : "err.nothing_held";
                case CommandType.Install: return Install(s, data, c.Player);

                case CommandType.Launch: return RequestLaunch(s, data, c.Player);
                case CommandType.Vote: return Vote(s, data, c.Player, c.N == 1);
                case CommandType.FinishLaunch:
                    if (s.Phase != SessionPhase.Launch) return "err.phase";
                    if (s.LastLaunch.Reached) { StartOps(s, data); return Ok; }
                    s.Phase = SessionPhase.Design;
                    // 失敗重來（歷史關卡和沙盒不重來：保留設計和衛星，回大屏幕接著改）
                    if (s.Scenario(data)?.Historical == null && !s.IsSandbox(data)) ResetAfterFailure(s, data);
                    else
                    {
                        s.Held.Clear();
                        s.Comps.Clear();
                        s.Confirmed = false;
                        SyncAssembly(s, data);
                    }
                    return Ok;

                case CommandType.Judge:
                case CommandType.Act:
                    return Respond(s, data, c);
                case CommandType.MiniDone:
                    return MiniDone(s, data, c);
                case CommandType.EndTurn:
                    if (s.Phase != SessionPhase.Ops) return "err.phase";
                    if (s.TurnResolved) return Ok;
                    // 有人打了小遊戲但還沒全員交成績就被結算：沒交的算 0 分
                    var cur = CurrentEvent(s, data);
                    if (cur != null && Response(s, cur.Id).Mini.Count > 0) ResolveMini(s, Response(s, cur.Id));
                    s.TurnResolved = true;
                    return Ok;
                case CommandType.NextTurn:
                    if (s.Phase != SessionPhase.Ops) return "err.phase";
                    if (!s.TurnResolved || c.N != s.OpsTurn) return Ok; // 重複送出（兩個人同時按）
                    s.OpsTurn++;
                    s.TurnResolved = false;
                    if (CurrentEvent(s, data) == null) s.Phase = SessionPhase.Report;
                    else Reveal(s, data);
                    return Ok;
            }
            return "err.unknown";
        }

        // ---------------------------------------------------------------- 選件（中控室大屏幕）
        //
        // 所有人都可以操作；選擇狀態在房主那裡，所有人看到的畫面一致。
        // 確定設計之後，大屏幕改成顯示每個零件要用哪些小零件；要改選先按「改選」（Confirm N = 0）。

        /// <summary>設計還缺什麼（類別）：每個單選類別都要選一個，酬載儀器至少一台。空清單 = 選齊了，可以確定。</summary>
        public static List<string> DesignGaps(SessionState s, GameData data)
        {
            var gaps = new List<string>();
            if (s.Bench.Instruments.Count == 0) gaps.Add(PartCategory.Instrument);
            foreach (var cat in PartCategory.Single)
                if (data.Part(s.Bench.Get(cat))?.Category != cat) gaps.Add(cat);
            return gaps;
        }

        /// <summary>設計裡選的零件（不含地面站），照固定順序。</summary>
        public static List<string> SelectedParts(SessionState s, GameData data) =>
            s.Bench.AllPartIds().Where(id => data.Part(id) != null && data.Part(id).Category != PartCategory.GroundStation).Distinct().ToList();

        /// <summary>現在能不能改大屏幕上的選擇。回傳 Ok 或原因。</summary>
        public static string CanEditDesign(SessionState s, GameData data)
        {
            if (!DesignPhase(s)) return "err.phase";
            if (s.DesignLocked(data)) return "err.locked";
            if (s.Poll != null) return "err.poll_active";
            if (s.Confirmed) return "err.confirmed";
            return Ok;
        }

        static string SelectPart(SessionState s, GameData data, string partId, bool on)
        {
            var why = CanEditDesign(s, data);
            if (why != Ok) return why;
            var part = data.Part(partId);
            if (part == null || part.Category == PartCategory.GroundStation) return "err.unknown";
            if (Catalog(data, s).All(p => p.Id != partId)) return "err.not_allowed";
            if (part.Category == PartCategory.Instrument)
            {
                if (!on) s.Bench.Instruments.Remove(partId);
                else if (!s.Bench.Instruments.Contains(partId))
                {
                    if (s.Bench.Instruments.Count >= (s.Scenario(data)?.MaxInstruments ?? 3)) return "err.instruments_full";
                    s.Bench.Instruments.Add(partId);
                }
            }
            else
            {
                // 每類只選一個：只能換，不能取消
                if (!on) return "err.single_required";
                s.Bench.Parts[part.Category] = partId;
            }
            SyncAssembly(s, data);
            return Ok;
        }

        /// <summary>軌道、火箭、地面站、年數（大屏幕的任務設定）。</summary>
        static string Control(SessionState s, GameData data, Action<Design> set, Func<bool> valid)
        {
            var why = CanEditDesign(s, data);
            if (why != Ok) return why;
            if (!valid()) return "err.not_allowed";
            set(s.Bench);
            return Ok;
        }

        /// <summary>
        /// 填數值：玩家找到線索後，自己在大屏幕上對應的空格輸入數字。填錯不擋也不提示；
        /// 兩個人填同一格，後填的蓋掉先填的。數值空白 = 清掉這一格。
        /// </summary>
        static string Fill(SessionState s, GameData data, int player, string arg)
        {
            if (!DesignPhase(s)) return "err.phase";
            if (!Blanks.TryParseFill(arg, out var partId, out var field, out var value)) return "err.fill_format";
            if (!Blanks.IsBlank(data, s, partId, field) || Catalog(data, s).All(p => p.Id != partId)) return "err.not_blank";
            string key = Blanks.Key(partId, field);
            if (value == null) { s.Filled.Remove(key); return Ok; }
            if (value.Value < 0 || value.Value > Blanks.MaxValue) return "err.fill_range";
            s.Filled[key] = new FilledValue { V = value.Value, By = player };
            return Ok;
        }

        /// <summary>
        /// 確定設計（on）：選齊了才能確定；確定後組裝台開始認配方。改選（off）：回到勾選畫面；
        /// 沒換掉的零件留在衛星上，換掉的自動拆下。
        /// </summary>
        static string Confirm(SessionState s, GameData data, bool on)
        {
            if (!DesignPhase(s)) return "err.phase";
            if (s.DesignLocked(data)) return "err.locked";
            if (!on) { s.Confirmed = false; return Ok; }
            if (s.Confirmed) return Ok;
            if (DesignGaps(s, data).Count > 0) return "err.design_incomplete";
            s.Confirmed = true;
            s.Confirms++;
            // 左側已經擺好一整組小零件的組裝台：現在認得出來了
            for (int i = 0; i < s.Benches.Count; i++) Recheck(s, data, i, 0, false);
            return Ok;
        }

        // ---------------------------------------------------------------- 沙盒：載入設計

        /// <summary>
        /// 把分享碼裡的設計放上大屏幕（只有沙盒可以）。
        /// 設計可以來自別的關卡：零件、軌道、火箭照用，儀器超過上限的部分和超過上限的年數裁掉。
        /// </summary>
        static string LoadDesign(SessionState s, GameData data, string code)
        {
            var sc = s.Scenario(data);
            if (sc == null || !sc.Sandbox) return "err.sandbox_only";
            if (!DesignPhase(s)) return "err.phase";
            if (!DesignCode.TryDecode(code, data, out var d, out _, out var error)) return error;
            d.ScenarioId = sc.Id;
            if (!sc.AllowedOrbits.Contains(d.OrbitId)) d.OrbitId = s.Bench.OrbitId;
            if (!data.LaunchersFor(sc).Any(l => l.Id == d.LauncherId)) d.LauncherId = s.Bench.LauncherId;
            if (OrbitLauncherCheck(data, d.OrbitId, d.LauncherId) != Ok) d.LauncherId = s.Bench.LauncherId;
            if (OrbitLauncherCheck(data, d.OrbitId, d.LauncherId) != Ok) d.OrbitId = s.Bench.OrbitId;
            if (d.Instruments.Count > sc.MaxInstruments) d.Instruments = d.Instruments.Take(sc.MaxInstruments).ToList();
            d.MissionYears = Math.Max(1, Math.Min(MaxMissionYears(sc), d.MissionYears));
            if (string.IsNullOrEmpty(d.Get(PartCategory.GroundStation)))
                d.Parts[PartCategory.GroundStation] = s.Bench.Get(PartCategory.GroundStation) ?? data.PartsIn(PartCategory.GroundStation).First().Id;
            s.Bench = d;
            s.Confirmed = false;
            SyncAssembly(s, data);
            return Ok;
        }

        // ---------------------------------------------------------------- 組裝（組裝區：架子 → 組裝台 → 衛星）
        //
        // 架子上放著所有配方會用到的小零件（不限數量，不提示該拿什麼）。把小零件放到組裝台左側，系統比對左側的組合：
        // 對得上設計裡還沒組好的零件的配方 → 右側開始組（照提示的順序，一個一個裝）；對不上 → 組合錯誤，小零件留在左側調整。
        // 組好的零件拿去整合台裝上衛星。裝錯沒有懲罰：不屬於這個零件的小零件報廢、順序錯的退回左側，再去架子拿就好。

        /// <summary>這個零件要不要組裝：有配方的都要（沙盒不用；封板這類沒有小零件的也不用）。</summary>
        public static bool HasAssembly(SessionState s, GameData data, string partId) =>
            !s.IsSandbox(data) && data.Recipe(data.Part(partId)) != null;

        /// <summary>這個零件還沒組好（設計裡選了、要組裝、還沒有成品）。</summary>
        public static bool NeedsAssembly(SessionState s, GameData data, string partId) =>
            HasAssembly(s, data, partId) && !s.Built.Contains(partId);

        /// <summary>設計裡選了、但還沒裝上衛星的零件。空清單才能發射。</summary>
        public static List<string> NotInstalled(SessionState s, GameData data) =>
            SelectedParts(s, data).Where(id => !s.Installed.Contains(id)).ToList();

        /// <summary>
        /// 衛星跟著大屏幕上的選擇對齊（每次選擇改變、有人離開之後呼叫）：
        /// 換掉的零件自動從衛星拆下（成品留著，改選回來就直接裝回去），沒換的留在衛星上；
        /// 不用組裝的零件（沙盒的全部、沒有小零件的封板）選了就在衛星上；
        /// 手上拿的、組裝台上正在組的零件如果被換掉了，就放下、取消（已經拿的小零件不扣錢）。
        /// </summary>
        static void SyncAssembly(SessionState s, GameData data)
        {
            var selected = SelectedParts(s, data);
            // 被換掉的：手上的放下，組裝台上的取消
            foreach (var kv in s.Held.Where(kv => !selected.Contains(kv.Value)).ToList()) s.Held.Remove(kv.Key);
            foreach (var b in s.Benches)
            {
                if (b.Job == null || selected.Contains(b.Job.PartId)) continue;
                if (!b.Job.Done)
                {
                    // 組到一半：已經裝上去的小零件拆回左側
                    var recipe = data.Recipe(data.Part(b.Job.PartId));
                    if (recipe != null)
                        foreach (var c in recipe.Components.Take(b.Job.Step))
                            if (!b.Left.Contains(c.Id)) b.Left.Add(c.Id);
                }
                b.Job = null;
            }
            s.Installed.RemoveAll(id => !selected.Contains(id));
            var busy = new HashSet<string>(s.Held.Values);
            foreach (var b in s.Benches) if (b.Job != null) busy.Add(b.Job.PartId);
            foreach (var id in selected)
            {
                if (!HasAssembly(s, data, id) && !s.Built.Contains(id)) s.Built.Add(id);
                if (s.Built.Contains(id) && !s.Installed.Contains(id) && !busy.Contains(id)) s.Installed.Add(id);
            }
            // 設計確定著的時候（沙盒、有人離開）：左側擺好的組合重新認一次
            if (s.Confirmed)
                for (int i = 0; i < s.Benches.Count; i++) Recheck(s, data, i, 0, false);
        }

        static string AssemblyPhase(SessionState s, GameData data)
        {
            if (!DesignPhase(s)) return "err.phase";
            if (s.DesignLocked(data)) return "err.locked";
            if (s.IsSandbox(data)) return "err.sandbox_no_assembly";
            return Ok;
        }

        static void AddComp(SessionState s, int player, string compId)
        {
            if (!s.Comps.TryGetValue(player, out var list) || list == null) s.Comps[player] = list = new List<string>();
            list.Add(compId);
        }

        static void RemoveComp(SessionState s, int player, string compId)
        {
            if (!s.Comps.TryGetValue(player, out var list) || list == null) return;
            list.Remove(compId);
            if (list.Count == 0) s.Comps.Remove(player);
        }

        /// <summary>手上還拿不拿得下一個小零件。回傳 Ok 或原因。</summary>
        public static string CanCarry(SessionState s, int player, string compId)
        {
            if (s.HeldBy(player) != null) return "err.hands_full";
            var mine = s.CompsOf(player);
            if (mine.Count >= CarryCapacity(s)) return "err.hands_full";
            if (mine.Contains(compId)) return "err.comp_dup";
            return Ok;
        }

        /// <summary>從架子拿一個小零件（架子不限數量，拿走會補）。</summary>
        static string CompPick(SessionState s, GameData data, int player, string compId)
        {
            var why = AssemblyPhase(s, data);
            if (why != Ok) return why;
            if (data.Component(compId) == null) return "err.unknown";
            why = CanCarry(s, player, compId);
            if (why != Ok) return why;
            AddComp(s, player, compId);
            return Ok;
        }

        /// <summary>把手上的小零件放回架子（compId 空白 = 全部）。</summary>
        static string CompDrop(SessionState s, int player, string compId)
        {
            var mine = s.CompsOf(player);
            if (mine.Count == 0) return "err.nothing_held";
            if (string.IsNullOrEmpty(compId)) { s.Comps.Remove(player); return Ok; }
            if (!mine.Contains(compId)) return "err.nothing_held";
            RemoveComp(s, player, compId);
            return Ok;
        }

        /// <summary>
        /// 設計裡還沒組好、用這個配方的零件（沒有 = null）。同一類的小、中、大型共用一個配方：每類只選一個，
        /// 所以認出配方就知道是哪個零件；酬載儀器每台都有自己的配方。另一座組裝台正在組的不算。
        /// </summary>
        public static string PendingPart(SessionState s, GameData data, AssemblyRecipe recipe)
        {
            if (recipe == null) return null;
            foreach (var id in SelectedParts(s, data))
            {
                if (s.Built.Contains(id) || !HasAssembly(s, data, id)) continue;
                if (data.Recipe(data.Part(id)) != recipe) continue;
                if (s.Benches.Any(b => b.Job != null && b.Job.PartId == id)) continue;
                return id;
            }
            return null;
        }

        /// <summary>這座組裝台左側的組合現在的狀態。</summary>
        public static BenchStatus BenchStatusOf(SessionState s, GameData data, int index)
        {
            var b = s.BenchAt(index);
            if (b == null) return BenchStatus.Empty;
            if (b.Job != null) return b.Job.Done ? BenchStatus.Done : BenchStatus.Active;
            if (b.Left.Count == 0) return BenchStatus.Empty;
            var recipe = data.RecipeOf(b.Left);
            if (recipe != null)
                return PendingPart(s, data, recipe) == null ? BenchStatus.NotInDesign
                    : s.Confirmed ? BenchStatus.Active : BenchStatus.Waiting;
            return data.PartOfRecipe(b.Left) ? BenchStatus.Partial : BenchStatus.Unknown;
        }

        /// <summary>
        /// 比對左側的小零件組合：對得上設計裡還沒組好的零件 → 右側開始組。
        /// placed = 剛放了東西上去：對不上的話記一次錯誤（畫面跳提示）。還沒放齊（是某個配方的一部分）不算錯。
        /// </summary>
        static void Recheck(SessionState s, GameData data, int index, int by, bool placed)
        {
            var b = s.Benches[index];
            if (b.Job != null) return;
            var status = BenchStatusOf(s, data, index);
            if (status == BenchStatus.Active)
            {
                b.Job = new BenchJob { PartId = PendingPart(s, data, data.RecipeOf(b.Left)) };
                return;
            }
            if (placed && (status == BenchStatus.Unknown || status == BenchStatus.NotInDesign || status == BenchStatus.Waiting))
            {
                b.Errors++;
                b.LastError = status;
                b.LastBy = by;
            }
        }

        /// <summary>把手上的小零件放到這座組裝台的左側。回傳 Ok 或原因。</summary>
        public static string CanPut(SessionState s, GameData data, int player, int index)
        {
            var why = AssemblyPhase(s, data);
            if (why != Ok) return why;
            var b = s.BenchAt(index);
            if (b == null) return "err.unknown";
            var mine = s.CompsOf(player);
            if (mine.Count == 0) return "err.nothing_held";
            // 右側正在組（或成品還沒拿走）：左側只收這個零件還沒裝上去的小零件（拿錯了放回去換一個），不能再加別的
            if (b.Job != null)
            {
                if (b.Job.Done) return "err.bench_take_first";
                return mine.Any(id => Returnable(s, data, index, id)) ? Ok : "err.bench_busy";
            }
            if (mine.All(b.Left.Contains)) return "err.comp_dup";
            if (b.Left.Count >= GameData.BenchSlots) return "err.bench_full";
            return Ok;
        }

        /// <summary>右側正在組的時候，這個小零件能不能放回左側：是這個零件的小零件、還沒裝上去、左側也沒有。</summary>
        static bool Returnable(SessionState s, GameData data, int index, string compId)
        {
            var b = s.BenchAt(index);
            var recipe = BenchRecipe(s, data, index);
            if (b == null || recipe == null || b.Left.Contains(compId)) return false;
            return recipe.Components.FindIndex(c => c.Id == compId) >= b.Job.Step;
        }

        static string BenchPut(SessionState s, GameData data, int player, int index)
        {
            var why = CanPut(s, data, player, index);
            if (why != Ok) return why;
            var b = s.Benches[index];
            // 手上的（籃子裡的）一次全部放上去；台上已經有的、放不下的留在手上
            foreach (var id in s.CompsOf(player).ToList())
            {
                if (b.Left.Contains(id) || b.Left.Count >= GameData.BenchSlots) continue;
                if (b.Job != null && !Returnable(s, data, index, id)) continue;
                b.Left.Add(id);
                RemoveComp(s, player, id);
            }
            Recheck(s, data, index, player, true);
            return Ok;
        }

        /// <summary>從組裝台左側拿一個小零件（調整組合，或拿去右側裝）。</summary>
        static string BenchTake(SessionState s, GameData data, int player, int index, string compId)
        {
            var why = AssemblyPhase(s, data);
            if (why != Ok) return why;
            var b = s.BenchAt(index);
            if (b == null) return "err.unknown";
            if (!b.Left.Contains(compId)) return "err.comp_gone";
            // 右側正在組：一次拿一個去裝（手上有別的小零件時，裝上去的會是哪一個就說不清了）
            if (b.Job != null && s.CompsOf(player).Count > 0) return "err.hands_full";
            why = CanCarry(s, player, compId);
            if (why != Ok) return why;
            b.Left.Remove(compId);
            AddComp(s, player, compId);
            // 拿走多放的那一個之後剛好是一整組：認得出來了（拿東西不算一次新的錯誤）
            Recheck(s, data, index, player, false);
            return Ok;
        }

        /// <summary>這座組裝台正在組的零件的配方（沒有在組、或已經組好 = null）。</summary>
        public static AssemblyRecipe BenchRecipe(SessionState s, GameData data, int index)
        {
            var job = s.BenchAt(index)?.Job;
            return job == null || job.Done ? null : data.Recipe(data.Part(job.PartId));
        }

        /// <summary>這一步要裝的小零件（沒有在組 = null）。</summary>
        public static AssemblyComponent BenchWants(SessionState s, GameData data, int index)
        {
            var r = BenchRecipe(s, data, index);
            var job = s.BenchAt(index)?.Job;
            return r != null && job.Step < r.Components.Count ? r.Components[job.Step] : null;
        }

        /// <summary>
        /// 把手上的小零件裝到右側。對的（這一步要的那一個）往下一步；錯的沒有懲罰：
        /// 不屬於這個零件的直接報廢，順序錯的退回左側。對錯都回傳 Ok（狀態有變），畫面看 <see cref="BenchJob.LastOk"/>。
        /// </summary>
        static string CompFit(SessionState s, GameData data, int player, int index)
        {
            var why = AssemblyPhase(s, data);
            if (why != Ok) return why;
            var b = s.BenchAt(index);
            if (b == null) return "err.unknown";
            var recipe = BenchRecipe(s, data, index);
            if (recipe == null) return "err.no_job";
            string id = s.HeldCompBy(player);
            if (id == null) return "err.nothing_held";
            var job = b.Job;
            var want = recipe.Components[job.Step];
            RemoveComp(s, player, id);
            job.Fits++;
            job.LastComp = id;
            job.LastBy = player;
            job.LastOk = id == want.Id;
            if (!job.LastOk)
            {
                job.Mistakes++;
                s.Mistakes++;
                int at = recipe.Components.FindIndex(c => c.Id == id);
                if (at < 0) job.Scrapped.Add(id);
                else if (at >= job.Step && !b.Left.Contains(id)) b.Left.Add(id);
                return Ok;
            }
            b.Left.Remove(id);
            job.Step++;
            if (job.Step < recipe.Components.Count) return Ok;
            // 組好了：成品留在右側，等人拿去裝上衛星
            job.Done = true;
            b.Left.Clear();
            if (!s.Built.Contains(job.PartId)) s.Built.Add(job.PartId);
            if (job.Mistakes == 0 && !s.Flawless.Contains(recipe.Key)) s.Flawless.Add(recipe.Key);
            if (!s.Learned.Contains(recipe.Key)) s.Learned.Add(recipe.Key);
            return Ok;
        }

        /// <summary>哪一座組裝台上放著這個組好的零件（沒有 = -1）。</summary>
        public static int BenchHolding(SessionState s, string partId)
        {
            for (int i = 0; i < s.Benches.Count; i++)
                if (s.Benches[i].Job != null && s.Benches[i].Job.Done && s.Benches[i].Job.PartId == partId) return i;
            return -1;
        }

        /// <summary>從組裝台拿走組好的零件。</summary>
        static string Pick(SessionState s, GameData data, int player, string partId)
        {
            var why = AssemblyPhase(s, data);
            if (why != Ok) return why;
            if (s.HeldBy(player) != null || s.CompsOf(player).Count > 0) return "err.hands_full";
            int i = BenchHolding(s, partId);
            if (i < 0) return "err.taken";
            s.Benches[i].Job = null;
            s.Held[player] = partId;
            return Ok;
        }

        public static string CanInstall(SessionState s, GameData data, int player)
        {
            if (!DesignPhase(s)) return "err.phase";
            if (s.DesignLocked(data)) return "err.locked";
            if (s.Poll != null) return "err.poll_active";
            if (s.HeldBy(player) == null) return "err.nothing_held";
            return Ok;
        }

        /// <summary>把手上組好的零件裝上整合台的衛星。</summary>
        static string Install(SessionState s, GameData data, int player)
        {
            var why = CanInstall(s, data, player);
            if (why != Ok) return why;
            var id = s.HeldBy(player);
            s.Held.Remove(player);
            if (!s.Installed.Contains(id)) s.Installed.Add(id);
            return Ok;
        }

        // ---------------------------------------------------------------- 發射

        /// <summary>真實價格算出來的總開銷有沒有超出預算（價格不挖空，所以大屏幕上看到的也是同一個數字）。</summary>
        public static bool OverBudget(SessionState s, GameData data) =>
            Evaluator.EvaluateDesign(s.Bench, data, s.DifficultyId, false)[ResourceId.Budget].Margin < -1e-9;

        /// <summary>
        /// 現在不能發射的原因（字串鍵；空清單 = 可以發射）。發射條件：設計選齊並確定、每個零件都組好裝上衛星、沒有超出預算。
        /// 系統不判斷設計好不好，結果交給發射模擬。
        /// </summary>
        public static List<string> LaunchBlockers(SessionState s, GameData data)
        {
            var list = new List<string>();
            if (s.Phase != SessionPhase.Design) { list.Add("err.phase"); return list; }
            if (s.Poll != null) list.Add("err.poll_active");
            if (DesignGaps(s, data).Count > 0) list.Add("err.design_incomplete");
            else if (!s.Confirmed && !s.IsSandbox(data)) list.Add("err.not_confirmed");
            if (NotInstalled(s, data).Count > 0) list.Add("err.not_installed");
            if (OverBudget(s, data)) list.Add("err.over_budget");
            return list;
        }

        /// <summary>現在能不能按發射。回傳 Ok 或第一個原因。</summary>
        public static string CanLaunch(SessionState s, GameData data)
        {
            var list = LaunchBlockers(s, data);
            return list.Count == 0 ? Ok : list[0];
        }

        /// <summary>
        /// 按下發射。個人模式直接發射；多人時開始 Go/No-Go 點名，全員 GO 才發射。
        /// </summary>
        static string RequestLaunch(SessionState s, GameData data, int player)
        {
            var why = CanLaunch(s, data);
            if (why != Ok) return why;
            if (!s.Multiplayer || s.Players.Count <= 1) return Launch(s, data);
            s.Poll = new GoPoll { Id = ++s.Polls, CalledBy = player };
            return Ok;
        }

        static string Vote(SessionState s, GameData data, int player, bool go)
        {
            if (s.Poll == null || s.Phase != SessionPhase.Design) return "err.no_poll";
            if (s.Player(player) == null) return "err.no_player";
            s.Poll.Votes[player] = go;
            CheckPoll(s, data);
            return Ok;
        }

        /// <summary>有人喊 NO-GO：HOLD；所有在場的人都喊 GO：發射。</summary>
        static void CheckPoll(SessionState s, GameData data)
        {
            var poll = s.Poll;
            if (poll == null) return;
            var nogo = poll.Votes.Where(kv => !kv.Value).Select(kv => kv.Key).FirstOrDefault();
            if (nogo != 0)
            {
                poll.Done = true;
                poll.Go = false;
                poll.NoGoBy = nogo;
                s.LastPoll = poll;
                s.Poll = null;
                return;
            }
            if (s.Players.Count == 0 || !s.Players.All(p => poll.Votes.TryGetValue(p.Id, out var v) && v)) return;
            poll.Done = true;
            poll.Go = true;
            s.LastPoll = poll;
            s.Poll = null;
            Launch(s, data);
        }

        static string Launch(SessionState s, GameData data)
        {
            if (s.Phase != SessionPhase.Design) return "err.phase";
            var design = s.Bench.Clone();
            int failed = FailedBeforeForScore(s, data);   // 歷史關卡的重演不算玩家的失敗
            // 種子由設計決定（同樣的設計結果一樣），再混入第幾次發射：
            // 失敗重來後照原樣組回同一個設計，才不會一直擲出同一個失敗
            int seed = design.StableHash();
            if (s.Launches.Count > 0) seed = unchecked(seed * 31 + s.Launches.Count);
            // 發射、營運、失敗原因和分數一律用零件的真實數值（data），不看玩家在大屏幕上填的數字
            var r = Simulator.SimulateMission(design, data, seed, new NoDecisions(), Context(data, s, failed));
            s.Launches.Add(new LaunchRecord
            {
                Attempt = s.Launches.Count + 1,
                Design = design,
                Seed = seed,
                FailedBefore = failed,
                Outcome = r.Launch.Outcome,
                ReasonCode = r.Launch.ReasonCode,
                Reached = r.ReachedOrbit,
            });
            s.Held.Clear();
            s.Comps.Clear();
            s.Responses.Clear();
            s.OpsTurn = 0;
            s.TurnResolved = false;
            s.Phase = SessionPhase.Launch;
            return Ok;
        }

        /// <summary>
        /// 發射失敗（沒入軌）：整局重來。衛星、零件和任務設定回到開局，回大屏幕重選、重新組裝；
        /// 保留組過的構造（Learned）、發射紀錄、玩家填的數值。歷史關卡和沙盒另外處理，不重來。
        /// </summary>
        static void ResetAfterFailure(SessionState s, GameData data)
        {
            s.Bench = StartDesign(s.Scenario(data), s.Difficulty(data));
            StartAssembly(s, data);
            s.Poll = null;
            s.Restarts++;
        }

        /// <summary>
        /// 計分用的「之前失敗了幾次」：歷史關卡第一次發射是重演歷史（玩家不能改設計），不算玩家的失敗。
        /// </summary>
        public static int FailedBeforeForScore(SessionState s, GameData data)
        {
            int failed = s.FailedLaunches;
            if (s.Scenario(data)?.Historical != null && s.Launches.Count > 0 && !s.Launches[0].Reached) failed--;
            return Math.Max(0, failed);
        }

        // ---------------------------------------------------------------- 營運

        static void StartOps(SessionState s, GameData data)
        {
            s.Phase = SessionPhase.Ops;
            s.OpsTurn = 0;
            s.TurnResolved = false;
            s.Responses.Clear();
            if (CurrentEvent(s, data) == null) s.Phase = SessionPhase.Report;
            else Reveal(s, data);
        }

        /// <summary>學員難度直接告訴玩家是什麼事件（自動判斷正確）。外星人彩蛋也一樣：不用查手冊，直接進遊戲。</summary>
        static void Reveal(SessionState s, GameData data)
        {
            var e = CurrentEvent(s, data);
            if (e != null && (e.Kind == EventKind.Alien || s.Difficulty(data).EventReveal)) Response(s, e.Id).Judged = OpsEvents.Key(e.Kind);
        }

        /// <summary>
        /// 小遊戲成績：玩的是「判斷的事件」對應的遊戲；全員交完（或房主強制結算）時算全隊有沒有過。
        /// 過了 = 做完手冊上那種事件的全部步驟；沒過 = 一個步驟都沒做。判斷錯的話步驟對不上，模擬會照原本的規則扣分。
        /// </summary>
        static string MiniDone(SessionState s, GameData data, Command c)
        {
            if (s.Phase != SessionPhase.Ops || s.TurnResolved) return "err.phase";
            var e = CurrentEvent(s, data);
            if (e == null) return "err.phase";
            var r = Response(s, e.Id);
            var def = Minigames.For(r.Judged);
            if (def == null || def.Id != c.A) return "err.minigame";
            if (r.Mini.ContainsKey(c.Player)) return Ok; // 重複送出
            r.Mini[c.Player] = Math.Max(0, Math.Min(1000, c.N));
            if (r.Mini.Count >= Math.Max(1, s.Players.Count)) ResolveMini(s, r);
            return Ok;
        }

        static void ResolveMini(SessionState s, EventResponse r)
        {
            var def = Minigames.For(r.Judged);
            var kind = OpsEvents.Parse(r.Judged);
            r.MiniPassed = def != null && MinigameRules.TeamPassed(def, r.Mini.Values, Math.Max(1, s.Players.Count));
            if (r.MiniPassed && kind.HasValue)
                foreach (var a in Procedures.Required(kind.Value))
                    if (!r.Actions.Contains(a)) r.Actions.Add(a);
            s.TurnResolved = true;
        }

        static EventResponse Response(SessionState s, string id)
        {
            if (!s.Responses.TryGetValue(id, out var r)) s.Responses[id] = r = new EventResponse();
            return r;
        }

        static string Respond(SessionState s, GameData data, Command c)
        {
            if (s.Phase != SessionPhase.Ops || s.TurnResolved) return "err.phase";
            var e = CurrentEvent(s, data);
            if (e == null) return "err.phase";
            var r = Response(s, e.Id);
            if (c.Type == CommandType.Judge)
            {
                if (e.Kind == EventKind.Alien) { r.Judged = OpsEvents.Key(e.Kind); return Ok; } // 彩蛋：自動判定，選什麼都一樣
                if (s.Difficulty(data).EventReveal) return Ok;
                if (OpsEvents.Parse(c.A) == null) return "err.unknown";
                if (r.Mini.Count > 0) return "err.locked"; // 已經有人開始處置了，不能改判斷
                r.Judged = c.A;
            }
            else
            {
                if (!OpsActions.All.Contains(c.A)) return "err.unknown";
                if (!r.Actions.Contains(c.A)) r.Actions.Add(c.A);
            }
            return Ok;
        }

        // ---------------------------------------------------------------- 由狀態算出來的東西

        public static MissionPlan Plan(SessionState s, GameData data)
        {
            var l = s.LastLaunch;
            return l == null ? null : Simulator.PlanMission(l.Design, data, l.Seed, s.DifficultyId);
        }

        /// <summary>目前（這次發射）的任務結果，套用到目前為止玩家的所有處置。</summary>
        public static MissionResult Result(SessionState s, GameData data)
        {
            var l = s.LastLaunch;
            if (l == null) return null;
            return Simulator.SimulateMission(l.Design, data, l.Seed, new FixedDecisions(s.Responses), Context(data, s, l.FailedBefore));
        }

        /// <summary>某一次發射的結果（失敗原因看板用）。</summary>
        public static MissionResult ResultOf(LaunchRecord l, SessionState s, GameData data) =>
            Simulator.SimulateMission(l.Design, data, l.Seed, new NoDecisions(), Context(data, s, l.FailedBefore));

        /// <summary>這回合的事件；任務已經結束（衛星提早失效、或回合都跑完）時是 null。</summary>
        public static MissionEvent CurrentEvent(SessionState s, GameData data)
        {
            var plan = Plan(s, data);
            if (plan == null || s.OpsTurn >= plan.Events.Count) return null;
            var e = plan.Events[s.OpsTurn];
            var r = Result(s, data);
            // 事件那天衛星已經不在了：任務結束
            return r != null && r.Events.Any(o => o.Event.Id == e.Id) ? e : null;
        }
    }
}
