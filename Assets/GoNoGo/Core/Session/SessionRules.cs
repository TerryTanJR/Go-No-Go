using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    public enum CommandType
    {
        // 房間
        Join, Leave, SetLook, SetStation, StartMission, BeginDesign,
        // 組裝區
        Pick, Drop, Install, Report,
        // 中控室
        SetOrbit, SetLauncher, SetGroundStation, SetYears, Call, Launch, FinishLaunch,
        // 營運
        Judge, Act, EndTurn, NextTurn,
        // 團隊合作（加在最後，舊的編號不變）
        // RunTest（環境測試）已刪除，留著編號讓後面的不變
        SetStrict, RunTest, Vote,
        // 採購（組裝區）。中間空兩號給已刪除的發射播放速度、跳過發射，後面的編號不變
        CartAdd = Vote + 3, CartRemove, PlaceOrder,
        // 大廳（加在最後，舊的編號不變）
        SetReady,
        // 採購審查（多人：組裝區送審、中控室批准或退回、組裝區撤回）
        ApproveOrder, RejectOrder, WithdrawOrder,
        // 營運小遊戲：交出自己的成績（A = 遊戲 id，N = 分數 0 ~ 1000）。加在最後，其他人的編號不變
        MiniDone,
        // 組裝工作台：把手上那一箱小零件組成零件（A = 零件 id）。加在最後，其他人的編號不變
        Assemble,
        // 沙盒：把一份存過（或別人分享）的設計直接放上整合台（A = 設計分享碼）
        LoadDesign,
        // 組裝工作台（手動組裝）：把手上那一箱放上工作台；從零件盤拿一個小零件（A = 小零件 id）；
        // 把手上的小零件放回去；把手上的小零件裝上去。加在最後，其他人的編號不變。
        // （上面的 Assemble 是舊的「在面板上點一點就組好」，已經不能用，留著編號）
        BenchPlace, CompPick, CompDrop, CompFit,
        // 發射前的逐項點名（docs/go_no_go_poll.md）。加在最後，其他人的編號不變。
        // PollCall：喊一項（A = 項目，N = 1 GO / 0 NO-GO）；PollCancel：飛行總監取消（N = 1：房主的計時器逾時）；
        // PollLaunch：六項都喊完後飛行總監送出（都 GO 就發射，有 NO-GO 就 HOLD）；PollProxy：房主的計時器開放代喊。
        // （上面的 Vote 是舊的「每人喊一次同不同意」，已經不能用，留著編號）
        PollCall, PollCancel, PollLaunch, PollProxy,
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
    /// </summary>
    public static class SessionRules
    {
        public const string Ok = "";

        // ---------------------------------------------------------------- 開局

        /// <summary>
        /// 開一局。難度跟著關卡走（一關一個難度，見 docs/procurement_v2.md §7）：difficultyId 傳 null 就用關卡的難度；
        /// 只有新手訓練（和測試）會指定別的難度。
        /// </summary>
        public static SessionState NewSession(GameData data, string scenarioId, string difficultyId, bool tutorial, bool multiplayer)
        {
            var scenario = data.Scenario(scenarioId) ?? data.Scenarios[0];
            // 一關一個難度（採購機制 v2 §7）。歷史關卡不分難度：不管傳進什麼，都用關卡指定的
            var diff = (scenario.Historical != null ? null : data.Difficulty(difficultyId)) ?? data.DifficultyFor(scenario);
            var start = StartDesign(scenario, diff);
            return new SessionState
            {
                ScenarioId = scenario.Id,
                DifficultyId = diff.Id,
                Tutorial = tutorial,
                Multiplayer = multiplayer,
                Phase = multiplayer ? SessionPhase.Lobby : SessionPhase.Briefing,
                Bench = start.Clone(),
                Reported = start.Clone(),
                Owned = StartOwned(data, start),
                Built = StartOwned(data, start),
            };
        }

        /// <summary>開局設計：關卡（或這個難度）的預設設計，再換上難度給的零件。歷史關卡用當年的設計。發射失敗重來也回到這裡。</summary>
        public static Design StartDesign(ScenarioDef scenario, DifficultyDef diff)
        {
            var start = (scenario.Historical != null ? scenario.Historical.PresetDesign : GameData.DefaultDesign(scenario, diff.Id)).Clone();
            start.ScenarioId = scenario.Id;
            start.ProcurementMusd = 0;
            if (scenario.Historical == null)
                foreach (var kv in diff.StartParts) start.Parts[kv.Key] = kv.Value;
            return start;
        }

        /// <summary>開局零件（不含地面站）。視為已經花掉的錢，拆下來不用也一樣算。</summary>
        static List<string> StartOwned(GameData data, Design start) =>
            start.AllPartIds().Where(id => data.Part(id)?.Category != PartCategory.GroundStation).Distinct().ToList();

        /// <summary>
        /// 任務年數上限 = 關卡目標年數 + 2。科學點數隨年數線性成長，不設上限的話「把年數調到壽命允許的最大值」永遠最好。
        /// </summary>
        public static int MaxMissionYears(ScenarioDef scenario) =>
            scenario != null && scenario.Sandbox ? SandboxMaxYears
            : Math.Max(1, (int)Math.Floor(scenario?.TargetYears ?? 8) + 2);

        /// <summary>沙盒的任務年數上限（不看目標年數）。</summary>
        public const int SandboxMaxYears = 15;

        // ---------------------------------------------------------------- 採購（採購機制 v2：只用錢當代價，見 docs/procurement_v2.md）

        /// <summary>多人而且不只一個人：組裝區送出的是採購單，要中控室批准才下單。</summary>
        public static bool NeedsApproval(SessionState s) => s.Multiplayer && s.Players.Count > 1;

        /// <summary>這些零件的價錢（重複的、已經擁有的不算）。</summary>
        public static double PartsMusd(SessionState s, GameData data, IEnumerable<string> parts) =>
            (parts ?? Enumerable.Empty<string>()).Distinct().Where(id => !s.Owned.Contains(id)).Sum(id => data.Part(id)?.CostMusd ?? 0);

        /// <summary>
        /// 目前的開發費 = 基本開發費 + 擁有的所有零件（開局零件和買過的）。
        /// 錢只跟「買」有關、跟「裝」無關：裝上、拆下、換來換去都不會改變。
        /// </summary>
        public static double DevelopmentMusd(SessionState s, GameData data) =>
            data.Constants.BusBaseCostMusd + s.Owned.Distinct().Sum(id => data.Part(id)?.CostMusd ?? 0);

        public static double Budget(SessionState s, GameData data) => GameData.Budget(s.Scenario(data));

        /// <summary>採購上限預留的火箭：這一關允許、飛得到目前軌道的最便宜火箭（不看質量）。</summary>
        public static LauncherDef ReserveLauncher(SessionState s, GameData data) => data.ReserveLauncher(s.Scenario(data), s.Bench.OrbitId);

        /// <summary>零件還能買多少：預算 − 目前的開發費 − 預留的火箭。可能是負的（中控室換了比較遠的軌道）。</summary>
        public static double Headroom(SessionState s, GameData data) =>
            Budget(s, data) - DevelopmentMusd(s, data) - (ReserveLauncher(s, data)?.CostMusd ?? 0);

        /// <summary>
        /// 營運經費撐得了幾年：用目前整合台上的火箭和地面站算（預算 − 開發費 − 火箭，再除以每年營運費）。
        /// extraMusd = 還沒買的單（審查、購物車）會再花掉的錢。
        /// </summary>
        public static double FundedYears(SessionState s, GameData data, double extraMusd = 0)
        {
            var ev = Evaluator.EvaluateDesign(s.Bench, data, s.DifficultyId, false);
            if (extraMusd != 0) ev.Cost.DevelopmentMusd += extraMusd;
            return Simulator.FundedYears(ev);
        }

        /// <summary>下單條件：目前的開發費 + 這張單 + 預留的火箭 ≤ 預算。回傳 Ok 或 err.order_over_cap。</summary>
        public static string CanOrder(SessionState s, GameData data, IEnumerable<string> parts) =>
            PartsMusd(s, data, parts) > Headroom(s, data) + 1e-9 ? "err.order_over_cap" : Ok;

        /// <summary>擁有但沒裝在船上的零件寫進整合台的設計，評估時算進開發費（開發費 = 基本 + 擁有的所有零件）。</summary>
        static void RefreshProcurement(SessionState s, GameData data)
        {
            var installed = new HashSet<string>(s.Bench.AllPartIds());
            s.Bench.ProcurementMusd = s.Owned.Distinct().Where(id => !installed.Contains(id)).Sum(id => data.Part(id)?.CostMusd ?? 0);
            s.Bench.CallMonths = CallMonths(s, data);
            s.Bench.ReworkMonths = ReworkMonths(s, data);
        }

        /// <summary>組裝工作台裝錯小零件、返工花掉的時程：裝錯的次數 × 每次的月數。</summary>
        public static double ReworkMonths(SessionState s, GameData data) => s.Rework * data.Constants.ReworkMonths;

        /// <summary>打電話花掉的時程：問過的不同問題數 × 每題的月數（同一題再問一次不算）。</summary>
        public static double CallMonths(SessionState s, GameData data) => s.Calls.Count * data.Constants.CallMonths;

        static string CartChange(SessionState s, GameData data, string partId, bool add)
        {
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) return "err.phase";
            if (s.DesignLocked(data)) return "err.locked";
            if (!add) { s.Cart.Remove(partId); return Ok; }
            var part = data.Part(partId);
            if (part == null || part.Category == PartCategory.GroundStation) return "err.unknown";
            if (s.Owned.Contains(partId)) return "err.owned";
            if (s.Request != null && s.Request.Parts.Contains(partId)) return "err.in_request";
            if (!ShelfParts(data, s).Any(p => p.Id == partId)) return "err.not_allowed";
            if (!s.Cart.Contains(partId)) s.Cart.Add(partId);
            return Ok;
        }

        static string OrderPhase(SessionState s, GameData data)
        {
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) return "err.phase";
            if (s.DesignLocked(data)) return "err.locked";
            return Ok;
        }

        /// <summary>
        /// 按「下單」。個人模式：檢查上限，過了就立刻扣錢、零件上架。
        /// 多人：送出採購單給中控室審查（不檢查上限，否則錯誤訊息會讓組裝區知道錢的狀況）。
        /// </summary>
        static string PlaceOrder(SessionState s, GameData data, int player)
        {
            var why = OrderPhase(s, data);
            if (why != Ok) return why;
            if (s.Cart.Count == 0) return "err.cart_empty";
            if (NeedsApproval(s))
            {
                if (s.Request != null) return "err.request_pending";
                s.Request = new OrderRequest { N = s.Orders.Count + 1, Seq = ++s.Requests, By = player, Parts = new List<string>(s.Cart) };
                s.Rejected = null;
                s.Cart.Clear();
                return Ok;
            }
            why = CanOrder(s, data, s.Cart);
            if (why != Ok) return why;
            Buy(s, player, 0, s.Cart);
            s.Cart.Clear();
            return Ok;
        }

        /// <summary>中控室批准採購單：同樣檢查上限，過了才下單。</summary>
        static string ApproveOrder(SessionState s, GameData data, int player)
        {
            var why = OrderPhase(s, data);
            if (why != Ok) return why;
            if (s.Request == null) return "err.no_request";
            why = CanOrder(s, data, s.Request.Parts);
            if (why != Ok) return why;
            Buy(s, s.Request.By, player, s.Request.Parts);
            s.Request = null;
            return Ok;
        }

        /// <summary>中控室退回（reject = true）或組裝區撤回：單上的零件回到購物車，可以改了再送。</summary>
        static string ReturnOrder(SessionState s, GameData data, bool reject)
        {
            var why = OrderPhase(s, data);
            if (why != Ok) return why;
            if (s.Request == null) return "err.no_request";
            foreach (var id in s.Request.Parts)
                if (!s.Owned.Contains(id) && !s.Cart.Contains(id)) s.Cart.Add(id);
            s.Rejected = reject ? s.Request : null;
            s.Request = null;
            return Ok;
        }

        static void Buy(SessionState s, int by, int approvedBy, IEnumerable<string> parts)
        {
            var bought = parts.Distinct().Where(id => !s.Owned.Contains(id)).ToList();
            if (bought.Count == 0) return;
            s.Orders.Add(new OrderRecord { N = s.Orders.Count + 1, By = by, ApprovedBy = approvedBy, Parts = bought });
            s.Owned.AddRange(bought);
        }

        /// <summary>
        /// 發射失敗（沒入軌）：整局重來。預算、衛星、零件、採購、中控室的設定都回到開局；
        /// 發射紀錄、電話問到的需求、玩家位置保留（評價要用第幾次才成功）。歷史關卡另外處理，不重來。
        /// </summary>
        static void ResetAfterFailure(SessionState s, GameData data)
        {
            var start = StartDesign(s.Scenario(data), s.Difficulty(data));
            s.Bench = start.Clone();
            s.Reported = start.Clone();
            s.Owned = StartOwned(data, start);
            s.Built = StartOwned(data, start);
            // 工作台清空；學會的構造（Learned）和返工花掉的時間（Rework）留著
            s.Workbench = null;
            s.HeldComp.Clear();
            s.Cart.Clear();
            s.Orders.Clear();
            s.Request = null;
            s.Rejected = null;
            s.Held.Clear();
            s.Poll = null;
            s.Restarts++;
        }

        public static ScenarioDef Scenario(this SessionState s, GameData data) => data.Scenario(s.ScenarioId);
        public static DifficultyDef Difficulty(this SessionState s, GameData data) =>
            data.Difficulty(s.DifficultyId) ?? data.SelectableDifficulties.First();

        /// <summary>歷史關卡：第一次發射（重演歷史）之前，整合台上是當年的設計，不能改。</summary>
        public static bool DesignLocked(this SessionState s, GameData data) =>
            s.Scenario(data)?.Historical != null && s.Launches.Count == 0;

        /// <summary>這一局的型錄：這個難度可以採購的零件，加上手上有的。零件架上只放得到 Owned 的。</summary>
        public static List<PartDef> ShelfParts(GameData data, SessionState s)
        {
            var shelf = new HashSet<string>(data.ShelfFor(s.Scenario(data), s.Difficulty(data)).Select(p => p.Id));
            var onShip = new HashSet<string>(s.Bench.AllPartIds().Concat(s.Owned));
            return data.Parts.Where(p => p.Category != PartCategory.GroundStation && (shelf.Contains(p.Id) || onShip.Contains(p.Id))).ToList();
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

        /// <summary>某一次發射的評分條件，包含那一次點名喊錯的項目（放行錯誤、誤報）。</summary>
        public static MissionContext Context(GameData data, SessionState s, LaunchRecord l)
        {
            var ctx = Context(data, s, l.FailedBefore);
            ctx.PollBadReleases = BadReleases(l).Count;
            ctx.PollFalseAlarms = FalseAlarms(s, l).Count;
            return ctx;
        }

        // ---------------------------------------------------------------- 套用操作

        public static string Apply(SessionState s, GameData data, Command c)
        {
            string r = ApplyInner(s, data, c);
            if (r == Ok)
            {
                // 儀表板在組裝區的大螢幕上，直接看整合台：中控室那份設計（Reported）永遠跟著整合台
                if (s.Phase == SessionPhase.Design || s.Phase == SessionPhase.Briefing || s.Phase == SessionPhase.Lobby)
                {
                    RefreshProcurement(s, data);
                    s.Reported = s.Bench.Clone();
                }
                s.Revision++;
            }
            return r;
        }

        /// <summary>房主的玩家編號（多人時 = Netcode clientId 0 + 1；個人模式只有 1）。</summary>
        public const int HostPlayer = 1;

        /// <summary>組裝區的操作（嚴格分工時只有組裝區的人能做）。</summary>
        public static bool IsAssemblyCommand(CommandType t) =>
            t == CommandType.Pick || t == CommandType.Drop || t == CommandType.Install || t == CommandType.Report
            || t == CommandType.Assemble || t == CommandType.BenchPlace || t == CommandType.CompPick
            || t == CommandType.CompDrop || t == CommandType.CompFit
            || t == CommandType.CartAdd || t == CommandType.CartRemove || t == CommandType.PlaceOrder || t == CommandType.WithdrawOrder;

        /// <summary>中控室的操作（嚴格分工時只有中控室的人能做）。</summary>
        public static bool IsControlCommand(CommandType t) =>
            t == CommandType.SetOrbit || t == CommandType.SetLauncher || t == CommandType.SetGroundStation || t == CommandType.SetYears
            || t == CommandType.Call || t == CommandType.Launch || t == CommandType.ApproveOrder || t == CommandType.RejectOrder;

        /// <summary>嚴格分工是否正在限制（只有設計前後：簡報與設計階段；營運時全員都在中控室）。</summary>
        public static bool StrictNow(this SessionState s) =>
            s.Multiplayer && s.StrictRoles && (s.Phase == SessionPhase.Briefing || s.Phase == SessionPhase.Design);

        /// <summary>這個玩家現在能不能做這個操作（嚴格分工）。回傳 Ok 或原因字串鍵。</summary>
        public static string RoleCheck(SessionState s, int player, CommandType t)
        {
            if (!s.StrictNow()) return Ok;
            var p = s.Player(player);
            if (p == null) return Ok;
            if (IsAssemblyCommand(t) && p.Station != Station.Assembly) return "err.strict_assembly";
            if (IsControlCommand(t) && p.Station != Station.Control) return "err.strict_control";
            return Ok;
        }

        /// <summary>
        /// 點名中不能改設計（要改就喊 NO-GO）。裝小零件也算：裝錯會返工，時程跟著變，點名開始時存下的答案就不準了。
        /// 沒問過的電話題目同理，在 Call 那邊擋。
        /// </summary>
        static bool ChangesDesign(CommandType t) =>
            t == CommandType.Install || t == CommandType.Report || t == CommandType.PlaceOrder || t == CommandType.ApproveOrder
            || t == CommandType.LoadDesign || t == CommandType.CompFit
            || t == CommandType.SetOrbit || t == CommandType.SetLauncher || t == CommandType.SetGroundStation || t == CommandType.SetYears;

        static string ApplyInner(SessionState s, GameData data, Command c)
        {
            var role = RoleCheck(s, c.Player, c.Type);
            if (role != Ok) return role;
            if (s.Poll != null && ChangesDesign(c.Type)) return "err.poll_active";
            switch (c.Type)
            {
                case CommandType.Join:
                    if (s.Player(c.Player) != null) return Ok;
                    if (s.Players.Count >= 4) return "err.room_full";
                    s.Players.Add(new PlayerInfo { Id = c.Player, Look = (c.Look ?? new AvatarLook()).Sanitized(), Station = DefaultStation(s.Players.Count) });
                    return Ok;
                case CommandType.Leave:
                    var leaver = s.Player(c.Player);
                    s.Players.RemoveAll(p => p.Id == c.Player);
                    s.Held.Remove(c.Player);
                    s.HeldComp.Remove(c.Player);
                    // 嚴格分工但有一區沒人了：取消限制，不然剩下的人永遠做不完
                    if (s.StrictRoles && s.Phase != SessionPhase.Lobby && !BothStationsManned(s)) s.StrictRoles = false;
                    if (s.Poll != null && leaver != null) PollHandOver(s, leaver);
                    return Ok;
                case CommandType.SetStrict:
                    if (s.Phase != SessionPhase.Lobby) return "err.phase";
                    if (c.Player != HostPlayer) return "err.host_only";
                    s.StrictRoles = c.N == 1;
                    return Ok;
                case CommandType.SetLook:
                    var pl = s.Player(c.Player);
                    if (pl == null || c.Look == null) return "err.no_player";
                    pl.Look = c.Look.Sanitized();
                    return Ok;
                case CommandType.SetStation:
                    var ps = s.Player(c.Player);
                    if (ps == null) return "err.no_player";
                    // 嚴格分工時，開始之後位置就固定了
                    if (s.StrictRoles && s.Phase != SessionPhase.Lobby) return "err.phase";
                    if (ps.Ready) return "err.ready_locked";
                    ps.Station = c.N == 1 ? Station.Control : Station.Assembly;
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
                    if (s.StrictRoles && !BothStationsManned(s)) return "err.strict_need_both";
                    if (s.Players.Count == 0 || s.Players.Any(p => !p.Ready)) return "err.not_ready";
                    // 多人：簡報是每個人自己看（本機面板），不卡全隊；直接進設計階段
                    s.Phase = SessionPhase.Design;
                    return Ok;
                case CommandType.BeginDesign:
                    if (s.Phase == SessionPhase.Design) return Ok;
                    if (s.Phase != SessionPhase.Briefing) return "err.phase";
                    s.Phase = SessionPhase.Design;
                    // 歷史關卡：簡報完直接照當年的設計發射（設計本來就鎖住，不用測試、不用點名）
                    if (s.DesignLocked(data)) return Launch(s, data);
                    return Ok;

                case CommandType.Pick: return Pick(s, data, c.Player, c.A);
                case CommandType.CartAdd: return CartChange(s, data, c.A, true);
                case CommandType.CartRemove: return CartChange(s, data, c.A, false);
                case CommandType.PlaceOrder: return PlaceOrder(s, data, c.Player);
                case CommandType.ApproveOrder: return ApproveOrder(s, data, c.Player);
                case CommandType.RejectOrder: return ReturnOrder(s, data, true);
                case CommandType.WithdrawOrder: return ReturnOrder(s, data, false);
                case CommandType.Drop:
                    if (!s.Held.Remove(c.Player)) return "err.nothing_held";
                    return Ok;
                case CommandType.Install: return Install(s, data, c.Player);
                case CommandType.BenchPlace: return BenchPlace(s, data, c.Player);
                case CommandType.CompPick: return CompPick(s, data, c.Player, c.A);
                case CommandType.CompDrop:
                    if (!s.HeldComp.Remove(c.Player)) return "err.nothing_held";
                    return Ok;
                case CommandType.CompFit: return CompFit(s, data, c.Player);
                case CommandType.LoadDesign: return LoadDesign(s, data, c.A);
                case CommandType.Report:
                    if (s.Phase != SessionPhase.Design) return "err.phase";
                    s.Reported.Instruments = new List<string>(s.Bench.Instruments);
                    foreach (var kv in s.Bench.Parts)
                        if (kv.Key != PartCategory.GroundStation) s.Reported.Parts[kv.Key] = kv.Value;
                    foreach (var k in s.Reported.Parts.Keys.ToList())
                        if (k != PartCategory.GroundStation && !s.Bench.Parts.ContainsKey(k)) s.Reported.Parts.Remove(k);
                    s.Reports++;
                    return Ok;

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
                case CommandType.Call:
                    var dep = data.Department(c.A);
                    if (dep == null) return "err.unknown";
                    var lines = data.Lines(s.Scenario(data), dep);
                    var lineId = c.N >= 0 && c.N < lines.Count && GameData.LineOpen(lines[c.N], s.Difficulty(data)) ? lines[c.N].Id : null;
                    if (lineId == null) return "err.unknown";
                    if (!s.Calls.Any(x => x.Department == dep.Id && x.LineId == lineId))
                    {
                        // 新問題會花時程：點名中不能問（問過的可以再聽一次）
                        if (s.Poll != null) return "err.poll_active";
                        s.Calls.Add(new CallRecord { Department = dep.Id, LineId = lineId });
                    }
                    return Ok;
                case CommandType.Launch: return RequestLaunch(s, data, c.Player);
                case CommandType.PollCall: return PollCall(s, c.Player, c.A, c.N == 1);
                case CommandType.PollLaunch: return PollLaunch(s, data, c.Player);
                case CommandType.PollCancel: return PollCancel(s, c.Player, c.N == 1);
                case CommandType.PollProxy: return PollProxy(s, c.Player);
                case CommandType.FinishLaunch:
                    if (s.Phase != SessionPhase.Launch) return "err.phase";
                    if (s.LastLaunch.Reached) { StartOps(s, data); return Ok; }
                    s.Phase = SessionPhase.Design;
                    s.Held.Clear();
                    s.HeldComp.Clear();
                    // 失敗重來（歷史關卡另外處理：保留設計，接著改；沙盒沒有懲罰，也保留設計）
                    if (s.Scenario(data)?.Historical == null && s.Scenario(data)?.Sandbox != true) ResetAfterFailure(s, data);
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

        static Station DefaultStation(int index) => index % 2 == 0 ? Station.Assembly : Station.Control;

        static string Control(SessionState s, GameData data, Action<Design> set, Func<bool> valid)
        {
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) return "err.phase";
            if (s.DesignLocked(data)) return "err.locked";
            if (!valid()) return "err.not_allowed";
            set(s.Bench);
            set(s.Reported);
            return Ok;
        }

        static string Pick(SessionState s, GameData data, int player, string partId)
        {
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) return "err.phase";
            if (s.HeldBy(player) != null || s.HeldComp.ContainsKey(player)) return "err.hands_full";
            var part = data.Part(partId);
            if (part == null || part.Category == PartCategory.GroundStation) return "err.unknown";
            if (s.HolderOf(partId) != null) return "err.taken";
            if (s.Workbench != null && s.Workbench.PartId == partId)
            {
                // 工作台上的那一個：組好了才能拿走（組到一半不能拿，零件盤上的小零件會對不上）
                if (!s.Workbench.Done) return "err.bench_busy";
                s.Workbench = null;
            }
            if (s.Bench.AllPartIds().Contains(partId))
            {
                // 從船上拆下來
                if (s.DesignLocked(data)) return "err.locked";
                if (s.Poll != null) return "err.poll_active";
                if (part.Category == PartCategory.Instrument) s.Bench.Instruments.Remove(partId);
                else s.Bench.Parts.Remove(part.Category);
            }
            else if (!s.Owned.Contains(partId)) return "err.not_owned";
            s.Held[player] = partId;
            return Ok;
        }

        /// <summary>手上的零件裝上去會換下哪一個（null = 不會換）。</summary>
        public static string WouldReplace(SessionState s, GameData data, string partId)
        {
            var part = data.Part(partId);
            if (part == null || part.Category == PartCategory.Instrument) return null;
            return s.Bench.Parts.TryGetValue(part.Category, out var old) && old != partId ? old : null;
        }

        public static string CanInstall(SessionState s, GameData data, int player)
        {
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) return "err.phase";
            if (s.DesignLocked(data)) return "err.locked";
            var id = s.HeldBy(player);
            if (id == null) return "err.nothing_held";
            var part = data.Part(id);
            if (NeedsAssembly(s, data, id)) return "err.not_assembled";
            if (part.Category == PartCategory.Instrument && s.Bench.Instruments.Count >= (s.Scenario(data)?.MaxInstruments ?? 3))
                return "err.instruments_full";
            return Ok;
        }

        // ---------------------------------------------------------------- 組裝工作台（小零件 → 零件）
        //
        // 買來的零件是一箱小零件。玩家把箱子放上工作台（BenchPlace），零件盤上就擺出這一箱的小零件和幾個混進來的；
        // 看工作台上寫的「這一步需要做什麼事的小零件」，到零件盤拿（CompPick）、走回來裝上去（CompFit）。
        // 裝對了往下一步；裝錯了要拆下來重裝（返工，花時程），不屬於這個零件的還會報廢。
        // 全部裝好，成品留在工作台上，拿走（Pick）就可以裝上船。全隊共用一張工作台，誰拿、誰裝都可以。

        /// <summary>
        /// 這個零件還是一箱沒組的小零件：要先拿到組裝工作台組好，才能裝上船。
        /// 沒有配方的零件（封板、地面站）、沙盒裡的零件、還有這一局已經學會的構造（同類的組過一次）都不用組。
        /// </summary>
        public static bool NeedsAssembly(SessionState s, GameData data, string partId)
        {
            var part = data.Part(partId);
            if (part == null || s.Built.Contains(partId)) return false;
            if (s.Scenario(data)?.Sandbox == true) return false;
            var recipe = data.Recipe(part);
            return recipe != null && !s.Learned.Contains(recipe.Key);
        }

        static string BenchPhase(SessionState s, GameData data)
        {
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) return "err.phase";
            if (s.DesignLocked(data)) return "err.locked";
            return Ok;
        }

        /// <summary>手上的那一箱能不能放上工作台。回傳 Ok 或原因。</summary>
        public static string CanPlaceOnBench(SessionState s, GameData data, int player)
        {
            var why = BenchPhase(s, data);
            if (why != Ok) return why;
            var id = s.HeldBy(player);
            if (id == null) return "err.nothing_held";
            if (!NeedsAssembly(s, data, id)) return "err.already_built";
            if (s.Workbench != null) return "err.bench_busy";
            return Ok;
        }

        static string BenchPlace(SessionState s, GameData data, int player)
        {
            var why = CanPlaceOnBench(s, data, player);
            if (why != Ok) return why;
            s.Workbench = new BenchJob { PartId = s.HeldBy(player) };
            s.Held.Remove(player);
            return Ok;
        }

        /// <summary>工作台上正在組的零件的配方（沒有在組、或已經組好 = null）。</summary>
        public static AssemblyRecipe BenchRecipe(SessionState s, GameData data) =>
            s.Workbench == null || s.Workbench.Done ? null : data.Recipe(data.Part(s.Workbench.PartId));

        /// <summary>這一步要裝的小零件（沒有在組 = null）。</summary>
        public static AssemblyComponent BenchWants(SessionState s, GameData data)
        {
            var r = BenchRecipe(s, data);
            return r != null && s.Workbench.Step < r.Components.Count ? r.Components[s.Workbench.Step] : null;
        }

        /// <summary>
        /// 零件盤現在的樣子：一格一個小零件，照 <see cref="GameData.Tray"/> 的順序；
        /// 已經裝上去的、報廢的、被人拿在手上的那一格是 null。工作台沒有在組東西時是空清單。
        /// </summary>
        public static List<AssemblyComponent> TrayNow(SessionState s, GameData data)
        {
            var recipe = BenchRecipe(s, data);
            if (recipe == null) return new List<AssemblyComponent>();
            var job = s.Workbench;
            var gone = new HashSet<string>(recipe.Components.Take(job.Step).Select(c => c.Id));
            gone.UnionWith(job.Scrapped);
            gone.UnionWith(s.HeldComp.Values);
            return data.Tray(data.Part(job.PartId)).Select(c => gone.Contains(c.Id) ? null : c).ToList();
        }

        static string CompPick(SessionState s, GameData data, int player, string compId)
        {
            var why = BenchPhase(s, data);
            if (why != Ok) return why;
            if (BenchRecipe(s, data) == null) return "err.no_job";
            if (s.HeldBy(player) != null || s.HeldComp.ContainsKey(player)) return "err.hands_full";
            if (!TrayNow(s, data).Any(c => c != null && c.Id == compId)) return "err.comp_gone";
            s.HeldComp[player] = compId;
            return Ok;
        }

        /// <summary>
        /// 把手上的小零件裝上去。對的（這一步要的那一個）往下一步；錯的算一次返工，手上的小零件放回零件盤，
        /// 不屬於這個零件的直接報廢。對錯都回傳 Ok（狀態有變），畫面看 <see cref="BenchJob.LastOk"/>。
        /// </summary>
        static string CompFit(SessionState s, GameData data, int player)
        {
            var why = BenchPhase(s, data);
            if (why != Ok) return why;
            var recipe = BenchRecipe(s, data);
            if (recipe == null) return "err.no_job";
            if (!s.HeldComp.TryGetValue(player, out var id)) return "err.nothing_held";
            var job = s.Workbench;
            var want = recipe.Components[job.Step];
            s.HeldComp.Remove(player);
            job.Fits++;
            job.LastComp = id;
            job.LastBy = player;
            job.LastOk = id == want.Id;
            if (!job.LastOk)
            {
                job.Mistakes++;
                s.Rework++;
                if (recipe.Components.All(c => c.Id != id)) job.Scrapped.Add(id);
                return Ok;
            }
            job.Step++;
            if (job.Step < recipe.Components.Count) return Ok;
            // 組好了：成品留在工作台上；這種構造算學會了，同類的零件之後不用再組
            job.Done = true;
            if (!s.Built.Contains(job.PartId)) s.Built.Add(job.PartId);
            if (!s.Learned.Contains(recipe.Key)) s.Learned.Add(recipe.Key);
            if (job.Mistakes == 0 && !s.Flawless.Contains(recipe.Key)) s.Flawless.Add(recipe.Key);
            return Ok;
        }

        // ---------------------------------------------------------------- 沙盒：載入設計

        /// <summary>
        /// 把分享碼裡的設計放上整合台（只有沙盒可以：一般任務這樣做等於跳過採購）。
        /// 設計可以來自別的關卡：零件、軌道、火箭照用，儀器超過上限的部分和超過上限的年數裁掉。
        /// 用到的零件都算已經擁有、已經組好。
        /// </summary>
        static string LoadDesign(SessionState s, GameData data, string code)
        {
            var sc = s.Scenario(data);
            if (sc == null || !sc.Sandbox) return "err.sandbox_only";
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) return "err.phase";
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
            foreach (var id in d.AllPartIds())
            {
                if (data.Part(id)?.Category == PartCategory.GroundStation) continue;
                if (!s.Owned.Contains(id)) s.Owned.Add(id);
                if (!s.Built.Contains(id)) s.Built.Add(id);
            }
            s.Held.Clear();
            s.Cart.RemoveAll(s.Owned.Contains);
            return Ok;
        }

        static string Install(SessionState s, GameData data, int player)
        {
            var why = CanInstall(s, data, player);
            if (why != Ok) return why;
            var id = s.HeldBy(player);
            var part = data.Part(id);
            if (part.Category == PartCategory.Instrument) s.Bench.Instruments.Add(id);
            else s.Bench.Parts[part.Category] = id; // 換下的舊零件自動回到架上
            s.Held.Remove(player);
            return Ok;
        }

        static bool BothStationsManned(SessionState s) =>
            s.Players.Any(p => p.Station == Station.Assembly) && s.Players.Any(p => p.Station == Station.Control);

        /// <summary>現在能不能按發射（不含嚴格分工的檢查）。回傳 Ok 或原因。</summary>
        public static string CanLaunch(SessionState s)
        {
            if (s.Phase != SessionPhase.Design) return "err.phase";
            if (s.Poll != null) return "err.poll_active";
            // 系統不判斷設計好不好，結果交給發射模擬（採購機制 v2 拿掉了環境測試）
            return Ok;
        }

        // ---------------------------------------------------------------- 發射前的逐項點名（docs/go_no_go_poll.md）
        //
        // 在發射台按發射 = 發起點名（單人也一樣），發起的人是這一次的飛行總監。設計立刻鎖住，六項資源當時的狀態存下來當答案。
        // 每一項由負責的人看讀數、喊 GO 或 NO-GO（PollCall）；系統這時候不說對錯，發射後才對答案。
        // 多人：任何一項 NO-GO 馬上 HOLD；六項都 GO，飛行總監送出（PollLaunch）才發射。
        // 只有一個人：六項都自己喊，送出前可以改；送出時有 NO-GO 就 HOLD，都 GO 就發射。
        // 逾時、代喊的秒數由房主的畫面計時（跟營運回合的 EndTurn 一樣），時間到送 PollCancel、PollProxy。

        /// <summary>只有一個人在場：六項都自己喊，送出時才結算（多人是一喊 NO-GO 就 HOLD）。</summary>
        public static bool PollSolo(SessionState s) => s.Players.Count <= 1;

        /// <summary>
        /// 按下發射：開始點名。答案（六項的狀態）和讀數在這一刻存下來，之後設計鎖住。
        /// </summary>
        static string RequestLaunch(SessionState s, GameData data, int player)
        {
            var why = CanLaunch(s);
            if (why != Ok) return why;
            if (s.Player(player) == null) return "err.no_player";
            RefreshProcurement(s, data);
            var ev = Evaluator.EvaluateDesign(s.Bench, data, s.DifficultyId, false);
            var poll = new GoPoll { Id = ++s.Polls, CalledBy = player };
            foreach (var id in PollItems.Order)
            {
                var r = ev[id];
                var reading = new PollReading { Used = r.Used, Capacity = r.Capacity };
                if (id == ResourceId.Power) { reading.Extra = ev.Power.ActualDod; reading.ExtraLimit = ev.Power.AllowedDod; }
                else if (id == ResourceId.Data) reading.Extra = ev.Data.StorageRawGb;
                poll.Truth[PollItems.Key(id)] = r.Status;
                poll.Readings[PollItems.Key(id)] = reading;
            }
            AssignPollOwners(s, poll);
            s.Poll = poll;
            return Ok;
        }

        /// <summary>
        /// 誰負責哪一項：照席位分（<see cref="PollItems.StationOf"/>），同一席位好幾個人就輪流分；
        /// 某個席位沒有人，那幾項給另一席位。最後確保每個人至少一項（從項目最多的人那裡拿）。
        /// </summary>
        static void AssignPollOwners(SessionState s, GoPoll poll)
        {
            var everyone = s.Players.Select(p => p.Id).OrderBy(id => id).ToList();
            foreach (var station in new[] { Station.Assembly, Station.Control })
            {
                var crew = s.Players.Where(p => p.Station == station).Select(p => p.Id).OrderBy(id => id).ToList();
                if (crew.Count == 0) crew = everyone;
                var items = PollItems.All.Where(k => PollItems.StationOf(k) == station).ToList();
                for (int i = 0; i < items.Count; i++) poll.Owner[items[i]] = crew[i % crew.Count];
            }
            int Load(int id) => poll.Owner.Values.Count(o => o == id);
            foreach (var idle in everyone.Where(id => Load(id) == 0).ToList())
            {
                var busiest = everyone.OrderByDescending(Load).ThenBy(id => id).First();
                if (Load(busiest) <= 1) break;
                poll.Owner[PollItems.All.Last(k => poll.Owner[k] == busiest)] = idle;
            }
        }

        /// <summary>
        /// 這個人現在能不能喊這一項：負責人隨時可以（也可以改口）；其他人只有開放代喊之後、而且那一項還沒人喊才行，
        /// 嚴格分工時不能代喊。回傳 Ok 或原因。
        /// </summary>
        public static string CanCall(SessionState s, int player, string item)
        {
            var poll = s.Poll;
            if (poll == null || s.Phase != SessionPhase.Design) return "err.no_poll";
            if (s.Player(player) == null) return "err.no_player";
            if (!poll.Owner.TryGetValue(item ?? "", out var owner)) return "err.unknown";
            if (owner == player) return Ok;
            if (s.StrictNow() || !poll.ProxyOpen || poll.Called(item)) return "err.poll_not_yours";
            return Ok;
        }

        static string PollCall(SessionState s, int player, string item, bool go)
        {
            var why = CanCall(s, player, item);
            if (why != Ok) return why;
            var poll = s.Poll;
            poll.Calls[item] = go;
            poll.By[item] = player;
            // 多人：一有人喊 NO-GO 就 HOLD。只有一個人時等他送出（送出前可以改）
            if (!go && !PollSolo(s)) HoldPoll(s);
            return Ok;
        }

        /// <summary>飛行總監送出：六項都要喊完。有 NO-GO 就 HOLD，都 GO 就發射。</summary>
        static string PollLaunch(SessionState s, GameData data, int player)
        {
            var poll = s.Poll;
            if (poll == null || s.Phase != SessionPhase.Design) return "err.no_poll";
            if (player != poll.CalledBy) return "err.poll_director";
            if (!poll.Complete()) return "err.poll_incomplete";
            if (!poll.AllGo()) { HoldPoll(s); return Ok; }
            var calls = PollCalls(poll);
            EndPoll(s, PollEnd.Launched);
            var r = Launch(s, data);
            if (r == Ok) s.LastLaunch.Calls = calls;
            return r;
        }

        /// <summary>飛行總監取消，或房主的計時器逾時（timedOut）：回到設計，沒有代價，也不留紀錄。</summary>
        static string PollCancel(SessionState s, int player, bool timedOut)
        {
            var poll = s.Poll;
            if (poll == null) return "err.no_poll";
            if (player != poll.CalledBy && player != HostPlayer) return "err.poll_director";
            EndPoll(s, timedOut && player == HostPlayer ? PollEnd.TimedOut : PollEnd.Cancelled);
            return Ok;
        }

        /// <summary>房主的計時器：負責人太久沒喊，開放其他人代喊（嚴格分工時不開放）。</summary>
        static string PollProxy(SessionState s, int player)
        {
            if (s.Poll == null) return "err.no_poll";
            if (player != HostPlayer) return "err.host_only";
            if (s.StrictNow()) return "err.poll_not_yours";
            s.Poll.ProxyOpen = true;
            return Ok;
        }

        /// <summary>HOLD：記下這次喊過的每一項（誤報從這裡算），回到設計。</summary>
        static void HoldPoll(SessionState s)
        {
            var poll = s.Poll;
            poll.NoGo = PollItems.All.Where(k => poll.Calls.TryGetValue(k, out var go) && !go).ToList();
            poll.NoGoBy = poll.NoGo.Count > 0 && poll.By.TryGetValue(poll.NoGo[0], out var by) ? by : 0;
            s.Holds.Add(new PollRecord { Poll = poll.Id, Attempt = s.Launches.Count + 1, Calls = PollCalls(poll) });
            EndPoll(s, PollEnd.Hold);
        }

        static void EndPoll(SessionState s, PollEnd end)
        {
            var poll = s.Poll;
            poll.Done = true;
            poll.Go = end == PollEnd.Launched;
            poll.End = end;
            s.LastPoll = poll;
            s.Poll = null;
        }

        /// <summary>
        /// 點名中有人離線：他負責的項目改給同席位的其他人（那個席位沒人了就給剩下的人），已經喊的保留。
        /// 走的是飛行總監就換人當（中控室的人優先）。一個人都不剩：取消。
        /// </summary>
        static void PollHandOver(SessionState s, PlayerInfo leaver)
        {
            var poll = s.Poll;
            if (s.Players.Count == 0) { EndPoll(s, PollEnd.Cancelled); return; }
            var mates = s.Players.Where(p => p.Station == leaver.Station).Select(p => p.Id).ToList();
            if (mates.Count == 0) mates = s.Players.Select(p => p.Id).ToList();
            foreach (var item in PollItems.All)
            {
                if (!poll.Owner.TryGetValue(item, out var owner) || owner != leaver.Id) continue;
                poll.Owner[item] = mates.OrderBy(id => poll.Owner.Values.Count(o => o == id)).ThenBy(id => id).First();
            }
            if (poll.CalledBy == leaver.Id)
                poll.CalledBy = s.Players.OrderBy(p => p.Station == Station.Control ? 0 : 1).ThenBy(p => p.Id).First().Id;
        }

        /// <summary>這次點名喊過的每一項，連同當時的答案和讀數。</summary>
        static List<PollCall> PollCalls(GoPoll poll)
        {
            var list = new List<PollCall>();
            foreach (var item in PollItems.All)
            {
                if (!poll.Calls.TryGetValue(item, out var go)) continue;
                poll.By.TryGetValue(item, out var by);
                poll.Owner.TryGetValue(item, out var owner);
                poll.Truth.TryGetValue(item, out var truth);
                poll.Readings.TryGetValue(item, out var reading);
                list.Add(new PollCall { Item = item, Go = go, By = by, Owner = owner, Truth = truth, Reading = reading?.Clone() ?? new PollReading() });
            }
            return list;
        }

        /// <summary>放行錯誤：放行這次發射的點名裡，超標卻喊 GO 的項目。</summary>
        public static List<PollCall> BadReleases(LaunchRecord l) =>
            l.Calls.Where(c => c.Verdict == PollVerdict.BadRelease).ToList();

        /// <summary>冒險放行：餘量不到一成還喊 GO 的項目（不算錯）。</summary>
        public static List<PollCall> RiskyGos(LaunchRecord l) =>
            l.Calls.Where(c => c.Verdict == PollVerdict.RiskyGo).ToList();

        /// <summary>這次發射之前幾次 HOLD（同一次發射的準備期間）。</summary>
        public static List<PollRecord> HoldsFor(SessionState s, LaunchRecord l) =>
            s.Holds.Where(h => h.Attempt == l.Attempt).ToList();

        /// <summary>
        /// 誤報：這次發射之前的 HOLD 裡，有餘量卻喊 NO-GO 的項目（每喊一次算一次）。
        /// 同一次 HOLD 裡超標卻喊 GO 的不算：被 HOLD 擋下來、修好再點名，就沒有放行出去。
        /// </summary>
        public static List<PollCall> FalseAlarms(SessionState s, LaunchRecord l) =>
            HoldsFor(s, l).SelectMany(h => h.Calls).Where(c => c.Verdict == PollVerdict.FalseAlarm).ToList();

        static string Launch(SessionState s, GameData data)
        {
            if (s.Phase != SessionPhase.Design) return "err.phase";
            var design = s.Bench.Clone();
            int failed = FailedBeforeForScore(s, data);   // 歷史關卡的重演不算玩家的失敗
            // 種子由設計決定（同樣的設計結果一樣），再混入第幾次發射：
            // 失敗重來後照原樣組回同一個設計，才不會一直擲出同一個失敗
            int seed = design.StableHash();
            if (s.Launches.Count > 0) seed = unchecked(seed * 31 + s.Launches.Count);
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
            s.HeldComp.Clear();
            s.Responses.Clear();
            s.OpsTurn = 0;
            s.TurnResolved = false;
            s.Phase = SessionPhase.Launch;
            return Ok;
        }

        /// <summary>
        /// 計分用的「之前失敗了幾次」：歷史關卡第一次發射是重演歷史（玩家不能改設計），不算玩家的失敗。
        /// 採購的重建次數（<see cref="SessionState.FailedLaunches"/>）照算。
        /// </summary>
        public static int FailedBeforeForScore(SessionState s, GameData data)
        {
            int failed = s.FailedLaunches;
            if (s.Scenario(data)?.Historical != null && s.Launches.Count > 0 && !s.Launches[0].Reached) failed--;
            return Math.Max(0, failed);
        }

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
            return Simulator.SimulateMission(l.Design, data, l.Seed, new FixedDecisions(s.Responses), Context(data, s, l));
        }

        /// <summary>某一次發射的結果（失敗原因看板用）。</summary>
        public static MissionResult ResultOf(LaunchRecord l, SessionState s, GameData data) =>
            Simulator.SimulateMission(l.Design, data, l.Seed, new NoDecisions(), Context(data, s, l));

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
