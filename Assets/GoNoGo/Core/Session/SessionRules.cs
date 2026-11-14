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
            Math.Max(1, (int)Math.Floor(scenario?.TargetYears ?? 8) + 2);

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
        }

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

        /// <summary>點名中不能改設計（要改就喊 NO-GO）。</summary>
        static bool ChangesDesign(CommandType t) =>
            t == CommandType.Install || t == CommandType.Report || t == CommandType.PlaceOrder || t == CommandType.ApproveOrder
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
                    s.Players.RemoveAll(p => p.Id == c.Player);
                    s.Held.Remove(c.Player);
                    // 嚴格分工但有一區沒人了：取消限制，不然剩下的人永遠做不完
                    if (s.StrictRoles && s.Phase != SessionPhase.Lobby && !BothStationsManned(s)) s.StrictRoles = false;
                    if (s.Poll != null)
                    {
                        s.Poll.Votes.Remove(c.Player);
                        CheckPoll(s, data);
                    }
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
                        s.Calls.Add(new CallRecord { Department = dep.Id, LineId = lineId });
                    return Ok;
                case CommandType.Launch: return RequestLaunch(s, data, c.Player);
                case CommandType.Vote: return Vote(s, data, c.Player, c.N == 1);
                case CommandType.FinishLaunch:
                    if (s.Phase != SessionPhase.Launch) return "err.phase";
                    if (s.LastLaunch.Reached) { StartOps(s, data); return Ok; }
                    s.Phase = SessionPhase.Design;
                    s.Held.Clear();
                    // 失敗重來（歷史關卡另外處理：保留設計，接著改）
                    if (s.Scenario(data)?.Historical == null) ResetAfterFailure(s, data);
                    return Ok;

                case CommandType.Judge:
                case CommandType.Act:
                    return Respond(s, data, c);
                case CommandType.EndTurn:
                    if (s.Phase != SessionPhase.Ops) return "err.phase";
                    if (s.TurnResolved) return Ok;
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
            if (s.HeldBy(player) != null) return "err.hands_full";
            var part = data.Part(partId);
            if (part == null || part.Category == PartCategory.GroundStation) return "err.unknown";
            if (s.HolderOf(partId) != null) return "err.taken";
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
            if (part.Category == PartCategory.Instrument && s.Bench.Instruments.Count >= (s.Scenario(data)?.MaxInstruments ?? 3))
                return "err.instruments_full";
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

        /// <summary>
        /// 按下發射。個人模式直接發射；多人時開始 Go/No-Go 點名，全員 GO 才發射。
        /// </summary>
        static string RequestLaunch(SessionState s, GameData data, int player)
        {
            var why = CanLaunch(s);
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

        /// <summary>學員難度直接告訴玩家是什麼事件（自動判斷正確）。</summary>
        static void Reveal(SessionState s, GameData data)
        {
            if (!s.Difficulty(data).EventReveal) return;
            var e = CurrentEvent(s, data);
            if (e != null) Response(s, e.Id).Judged = OpsEvents.Key(e.Kind);
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
                if (s.Difficulty(data).EventReveal) return Ok;
                if (OpsEvents.Parse(c.A) == null) return "err.unknown";
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
