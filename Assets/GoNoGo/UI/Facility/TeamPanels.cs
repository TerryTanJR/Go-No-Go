using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // ======================================================================== 組裝區的手冊（嚴格分工）

    /// <summary>
    /// 嚴格分工時組裝區只有「組裝手冊」：零件代號、名稱、用途和組裝規則，沒有規格數字。
    /// 數字在中控室的手冊和儀表板上，要靠中控室唸給組裝區聽（不嚴格分工時是完整手冊）。
    /// </summary>
    public sealed class BayManualPanel : FacPanel
    {
        public override string Title => Loc.T("ui.baymanual.title");
        public override string WideClass => "modal--manual";

        protected override void Build(VisualElement body)
        {
            Ui.Text(Loc.T("ui.baymanual.intro"), "body", "tight", "man-intro").AddTo(body);
            var sv = Scroll(body, "man-scroll");
            var r = Ui.El("man-rule").AddTo(sv);
            Ui.Text(Loc.T("ui.manual.rule.instruments.t"), "h3").AddTo(r);
            Ui.Text(Loc.T("ui.baymanual.rules"), "body").AddTo(r);
            // 同一套零件卡，只是沒有數字
            foreach (var ch in ManualView.Chapters.Where(ManualView.IsParts))
                foreach (var cat in ManualView.CategoriesOf(ch)) ManualView.PartsSection(sv, cat, false);
        }
    }

    // ======================================================================== Go/No-Go 點名

    /// <summary>點名畫面共用的文字：項目名稱、讀數、玩家名字、對答案的句子（橫幅、發射台面板、報告、失敗看板都用）。</summary>
    public static class PollText
    {
        static MissionSession M => GameSession.Mission;

        public static string Item(string item) => Loc.T("res." + item);

        public static string Items(IEnumerable<string> items) => string.Join(Loc.IsZh ? "、" : ", ", items.Select(Item));

        public static string Name(int id)
        {
            var n = M.S.Player(id)?.Look?.Name;
            return string.IsNullOrEmpty(n) ? Loc.T("ui.poll.player", ("n", id)) : n;
        }

        static string UnitOf(ResourceId id) => id switch
        {
            ResourceId.Mass => "kg",
            ResourceId.Power => "W",
            ResourceId.Budget => "M$",
            ResourceId.Data => "GB",
            ResourceId.Comms => "GB/day",
            _ => "mo",
        };

        static ResourceReport Report(string item, PollReading r, ResourceStatus status)
        {
            var id = PollItems.Parse(item) ?? ResourceId.Mass;
            return new ResourceReport { Id = id, Used = r.Used, Capacity = r.Capacity, Unit = UnitOf(id), Status = status };
        }

        /// <summary>
        /// 點名開始那一刻的讀數，格式和儀表板一樣（用量 / 上限）。
        /// 電力多寫放電深度和這個軌道的上限、資料多寫不含安全餘量的需求：這兩項的狀態不只看「用量 / 上限」，
        /// 少了這些數字就沒辦法自己判斷。
        /// </summary>
        public static string Reading(string item, PollReading r)
        {
            string v = Fmt.Resource(Report(item, r, ResourceStatus.Ok));
            if (item == PollItems.Key(ResourceId.Power))
                return Loc.T("ui.poll.read.power", ("v", v), ("dod", Fmt.Pct(r.Extra)), ("max", Fmt.Pct(r.ExtraLimit)));
            if (item == PollItems.Key(ResourceId.Data))
                return Loc.T("ui.poll.read.data", ("v", v), ("raw", Fmt.N(r.Extra, 1)));
            return v;
        }

        /// <summary>
        /// 讀數那一列：這兩個數字是什麼（小字）和數字本身。學員難度加上燈號和差多少（教學用）；
        /// 其他難度只有數字，要自己比。
        /// </summary>
        public static void ReadingRow(VisualElement parent, string item, GoPoll poll)
        {
            if (!poll.Readings.TryGetValue(item, out var r)) return;
            poll.Truth.TryGetValue(item, out var truth);
            bool lamps = M.Difficulty.DashboardColors;
            var row = Ui.Row("poll-read").AddTo(parent);
            if (lamps) Ui.El("dash-lamp", "dash-lamp--" + DashboardView.Status(truth)).AddTo(row);
            Ui.Text(Loc.T("ui.poll.what." + item), "small", "dim", "poll-what").AddTo(row);
            Ui.Text(Reading(item, r), "mono", "poll-value").AddTo(row);
            // 電力的燈號可能是電池造成的，「差多少瓦」會誤導，只給燈號
            if (lamps && truth != ResourceStatus.Ok && item != PollItems.Key(ResourceId.Power))
                Ui.Text(DashboardView.Gap(Report(item, r, truth)), "small", "mono", DashboardView.Status(truth), "poll-gap").AddTo(row);
        }

        /// <summary>對答案的一句話：「電力：小明 喊 GO，當時 120 / 100 W…」。team = 寫出是誰喊的。</summary>
        public static string Line(PollCall c, bool team)
        {
            string who = team ? Name(c.By) + " " : "";
            string line = Loc.T(c.Go ? "ui.poll.rec.go" : "ui.poll.rec.nogo", ("item", Item(c.Item)), ("who", who), ("v", Reading(c.Item, c.Reading)));
            return c.Proxy ? line + Loc.T("ui.poll.rec.proxy", ("owner", Name(c.Owner))) : line;
        }

        /// <summary>點名的按鈕不拿鍵盤焦點：Enter、G、N 是點名的快捷鍵，不能又觸發剛才點過的按鈕。</summary>
        public static Button Key(VisualElement parent, string text, System.Action onClick, params string[] classes)
        {
            var b = Ui.Btn(text, onClick, classes).AddTo(parent);
            b.focusable = false;
            return b;
        }
    }

    /// <summary>
    /// 畫面上方的點名列（多人）：六項、各自的負責人、目前的回答（…／GO／NO-GO）。
    /// 只有負責人看得到自己那幾項的讀數，並且能喊（G / N）；其他人只看到結果，要知道數字就得用講的。
    /// 任何一項 NO-GO 馬上 HOLD，顯示是誰在哪一項叫停；六項都 GO，飛行總監按 Enter 放行。
    /// 房主的這個橫幅負責計時：負責人太久沒喊就開放代喊，時間到還沒喊完就取消（規則本身沒有時鐘）。
    /// 只有一個人時點名在發射台的面板裡走（<see cref="LaunchPanel"/>），那時候這裡不顯示。
    /// </summary>
    public sealed class PollBanner
    {
        const float HoldSeconds = 12f, NoteSeconds = 6f;

        readonly VisualElement _root;
        int _seenPoll = -1, _shownEnd = -1;
        float _noteLeft;
        /// <summary>這次點名開始多久了（從本機看到它算起）。</summary>
        float _elapsed;
        bool _proxySent, _cancelSent;
        Label _clock;

        /// <summary>發射台的點名面板開著、而且只有一個人：點名在面板裡走，橫幅不顯示也不出聲。</summary>
        public bool Suppressed { get; set; }

        public PollBanner(VisualElement parent)
        {
            _root = Ui.El("poll").AddTo(parent);
            _root.style.display = DisplayStyle.None;
            var m = GameSession.Mission;
            // 進場景之前就結束的點名不要再顯示
            _seenPoll = m.S.Poll?.Id ?? -1;
            _shownEnd = m.S.LastPoll?.Id ?? -1;
        }

        static MissionSession M => GameSession.Mission;
        static int Me => GameSession.LocalPlayer;

        public bool Active => M.S.Poll != null && M.S.Phase == SessionPhase.Design && !Suppressed;

        /// <summary>
        /// 我現在要喊的項目：先是自己負責、還沒喊的（照點名順序）；自己的都喊完了，開放代喊後是別人還沒喊的。
        /// 沒有 = null。
        /// </summary>
        public string Current
        {
            get
            {
                if (!Active) return null;
                var s = M.S;
                var mine = s.Poll.Pending(Me);
                if (mine.Count > 0) return mine[0];
                return PollItems.All.FirstOrDefault(k => !s.Poll.Called(k) && SessionRules.CanCall(s, Me, k) == SessionRules.Ok);
            }
        }

        public bool CanCall => Current != null;
        /// <summary>我是飛行總監，六項都喊完了：可以送出。</summary>
        public bool CanSubmit => Active && M.S.Poll.CalledBy == Me && M.S.Poll.Complete();

        public void Call(bool go)
        {
            var item = Current;
            if (item == null) return;
            if (go) Sfx.Go(); else Sfx.NoGo();
            GameSession.Send(CommandType.PollCall, item, go ? 1 : 0);
        }

        public void Submit()
        {
            if (!CanSubmit) return;
            if (M.S.Poll.AllGo()) { Sfx.Go(); Hud.Signal("launch"); }
            GameSession.Send(CommandType.PollLaunch);
        }

        void Cancel()
        {
            if (Active && M.S.Poll.CalledBy == Me) GameSession.Send(CommandType.PollCancel);
        }

        public void Refresh()
        {
            var s = M.S;
            if (s.Poll != null && s.Poll.Id != _seenPoll)
            {
                _seenPoll = s.Poll.Id;
                _elapsed = 0;
                _proxySent = _cancelSent = false;
                _noteLeft = 0;
                if (!Suppressed) Sfx.Alert();
            }
            if (s.Poll == null && s.LastPoll != null && s.LastPoll.Id != _shownEnd)
            {
                _shownEnd = s.LastPoll.Id;
                var end = s.LastPoll.End;
                // 發射了不用說；自己取消的不用再說一次；單人的結果在面板裡
                bool silent = Suppressed || end == PollEnd.Launched || (end == PollEnd.Cancelled && s.LastPoll.CalledBy == Me);
                _noteLeft = silent ? 0 : end == PollEnd.Hold ? HoldSeconds : NoteSeconds;
                if (!silent) Sfx.NoGo();
            }
            Draw();
        }

        public void Tick(float dt)
        {
            var s = M.S;
            if (s.Poll != null && s.Phase == SessionPhase.Design && !SessionRules.PollSolo(s))
            {
                _elapsed += dt;
                var c = GameSession.Data.Constants;
                bool waiting = !s.Poll.Complete();
                if (GameSession.IsHost)
                {
                    // 負責人太久沒喊：開放代喊（嚴格分工時不開放）
                    if (!_proxySent && waiting && !s.Poll.ProxyOpen && !s.StrictNow() && _elapsed >= c.PollProxySeconds)
                    {
                        _proxySent = true;
                        GameSession.Send(CommandType.PollProxy);
                    }
                    // 時間到還沒喊完：取消，回到設計
                    else if (!_cancelSent && waiting && _elapsed >= c.PollTimeoutSeconds)
                    {
                        _cancelSent = true;
                        GameSession.Send(CommandType.PollCancel, "", 1);
                    }
                }
                if (_clock != null)
                {
                    float left = (float)c.PollTimeoutSeconds - _elapsed;
                    _clock.text = waiting ? Fmt.Clock(left) : "";
                    _clock.EnableInClassList("over", left < 15);
                }
            }
            if (_noteLeft <= 0) return;
            _noteLeft -= dt;
            if (_noteLeft <= 0) Draw();
        }

        void Draw()
        {
            _root.Clear();
            _clock = null;
            var s = M.S;
            if (Active) { DrawPoll(s); return; }
            if (_noteLeft > 0 && s.LastPoll != null && !Suppressed) { DrawNote(s.LastPoll); return; }
            _root.style.display = DisplayStyle.None;
        }

        void DrawPoll(SessionState s)
        {
            var poll = s.Poll;
            bool solo = SessionRules.PollSolo(s);
            string current = Current;
            _root.style.display = DisplayStyle.Flex;
            _root.EnableInClassList("poll--hold", false);

            var head = Ui.Row("poll-head").AddTo(_root);
            Ui.Text(Loc.T("ui.poll.title"), "eyebrow").AddTo(head);
            Ui.Text(Loc.T("ui.poll.director", ("name", PollText.Name(poll.CalledBy))), "small", "dim", "poll-director").AddTo(head);
            Ui.Spacer().AddTo(head);
            if (!solo) _clock = Ui.Text("", "mono", "poll-clock").AddTo(head);

            // 六項：項目、負責人、回答
            var list = Ui.Row("poll-list").AddTo(_root);
            foreach (var item in PollItems.All)
            {
                poll.Owner.TryGetValue(item, out var owner);
                bool called = poll.Calls.TryGetValue(item, out var go);
                var cell = Ui.El("poll-cell").Cls("poll-cell--mine", owner == Me).Cls("poll-cell--now", item == current).AddTo(list);
                Ui.Text(PollText.Item(item), "poll-item").AddTo(cell);
                Ui.Text(PollText.Name(owner) + (owner == Me ? " ★" : ""), "small", "dim").AddTo(cell);
                Ui.Text(!called ? "…" : go ? "GO" : "NO-GO", "h3", "mono", !called ? "dim" : go ? "go" : "over").AddTo(cell);
            }

            if (current != null)
            {
                // 輪到我喊的那一項：讀數只有這裡看得到
                poll.Owner.TryGetValue(current, out var owner);
                var now = Ui.El("poll-now").AddTo(_root);
                var top = Ui.Row("poll-now-top").AddTo(now);
                Ui.Text(owner == Me ? Loc.T("ui.poll.your_call", ("item", PollText.Item(current)))
                    : Loc.T("ui.poll.your_proxy", ("item", PollText.Item(current)), ("name", PollText.Name(owner))), "h3").AddTo(top);
                Ui.Spacer().AddTo(top);
                PollText.Key(top, "[N]  NO-GO", () => Call(false), "btn-danger");
                PollText.Key(top, "[G]  GO", () => Call(true), "btn-go");
                PollText.ReadingRow(now, current, poll);
                Ui.Text(Loc.T("ui.poll.go." + current), "small", "dim").AddTo(now);
            }
            else if (CanSubmit)
            {
                var row = Ui.Row("poll-actions").AddTo(_root);
                Ui.Text(Loc.T(poll.AllGo() ? "ui.poll.all_go" : "ui.poll.has_nogo"), "small").AddTo(row);
                Ui.Spacer().AddTo(row);
                PollText.Key(row, "[Enter]  " + Loc.T(poll.AllGo() ? "ui.poll.launch" : "ui.poll.submit"), Submit, "btn-go");
            }
            else
                Ui.Text(poll.Complete() ? Loc.T("ui.poll.wait_director", ("name", PollText.Name(poll.CalledBy))) : Loc.T("ui.poll.waiting"), "small", "dim").AddTo(_root);

            var foot = Ui.Row("poll-foot").AddTo(_root);
            Ui.Text(Loc.T(solo ? "ui.poll.frozen_solo" : "ui.poll.frozen"), "small", "dim").AddTo(foot);
            Ui.Spacer().AddTo(foot);
            if (poll.CalledBy == Me) PollText.Key(foot, Loc.T("ui.poll.cancel"), Cancel, "btn-small", "btn-ghost");
        }

        /// <summary>點名剛結束、沒有發射：HOLD（誰在哪一項叫停、去哪裡修）、逾時、飛行總監取消。</summary>
        void DrawNote(GoPoll last)
        {
            _root.style.display = DisplayStyle.Flex;
            _root.EnableInClassList("poll--hold", true);
            switch (last.End)
            {
                case PollEnd.Hold:
                    Ui.Text("HOLD", "h2", "over").AddTo(_root);
                    Ui.Text(Loc.T("ui.poll.hold", ("name", PollText.Name(last.NoGoBy)), ("item", PollText.Items(last.NoGo))), "body").AddTo(_root);
                    foreach (var item in last.NoGo) Ui.Text(Loc.T("ui.poll.fix." + item), "small", "dim").AddTo(_root);
                    break;
                case PollEnd.TimedOut:
                    Ui.Text(Loc.T("ui.poll.timeout.t"), "h3", "tight").AddTo(_root);
                    Ui.Text(Loc.T("ui.poll.timeout", ("s", Fmt.N(GameSession.Data.Constants.PollTimeoutSeconds))), "body").AddTo(_root);
                    break;
                default:
                    Ui.Text(Loc.T("ui.poll.cancelled", ("name", PollText.Name(last.CalledBy))), "body").AddTo(_root);
                    break;
            }
        }
    }
}
