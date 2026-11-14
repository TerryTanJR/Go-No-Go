using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // ======================================================================== 環境測試台（組裝區）

    /// <summary>
    /// 振動／熱真空測試台：把整合台上現在的太空船拿去測。發射前一定要測過目前的設計
    /// （之後動到零件、軌道、火箭就要重測）；結果好壞不擋發射，由團隊自己判斷。
    /// </summary>
    public sealed class TestPanel : FacPanel
    {
        public override string Title => Loc.T("ui.test.title");
        public override string WideClass => "modal--wide";

        protected override void Build(VisualElement body)
        {
            Ui.Text(Loc.T("ui.test.intro"), "body").AddTo(body);
            TestView.Build(Scroll(body), false);
            // 關閉在標題列右上（Esc），這裡只放主要按鈕
            var row = Ui.Row("modal-actions").AddTo(body);
            Ui.Spacer().AddTo(row);
            bool current = EnvTests.IsCurrent(M.S);
            var run = Ui.Btn(Loc.T(M.S.LastTest == null ? "ui.test.run" : current ? "ui.test.run_again" : "ui.test.rerun"), () =>
            {
                Sfx.Confirm();
                GameSession.Send(CommandType.RunTest);
                Hud.Signal("test");
            }, current ? "btn-ghost" : "btn-primary", "btn-big").AddTo(row);
            run.SetEnabled(M.S.Phase == SessionPhase.Design);
        }
    }

    /// <summary>測試報告（測試台面板、儀表板、平板共用）。學員、工程師難度有數字；任務主管只有通過與否。</summary>
    public static class TestView
    {
        static readonly string[] Tests = { EnvTests.Vibration, EnvTests.Thermal };

        public static void Build(VisualElement parent, bool compact)
        {
            var m = GameSession.Mission;
            var s = m.S;
            var t = s.LastTest;
            if (compact)
            {
                // 儀表板的一行：第幾次、兩項結果、是不是目前的設計
                var row = Ui.Row("dash-row").AddTo(parent);
                Ui.El("dash-lamp", "dash-lamp--none").AddTo(row); // 和儀表板其他列對齊
                Ui.Text(Loc.T("ui.test.short"), "dash-k").AddTo(row);
                Ui.Spacer().AddTo(row);
                if (t == null) { Ui.Text(Loc.T("ui.test.none_short"), "small", "over").AddTo(row); return; }
                foreach (var test in Tests)
                {
                    var lv = t.Worst(test);
                    Ui.Text(Loc.T("ui.test." + test + ".short") + " " + LevelText(lv), "mono", "small", LevelClass(lv), "test-chip").AddTo(row);
                }
                bool cur = EnvTests.IsCurrent(s);
                Ui.Text(Loc.T(cur ? "ui.test.current" : "ui.test.stale"), "small", cur ? "dim" : "tight", "dash-x").AddTo(row);
                return;
            }

            if (t == null)
            {
                Ui.Text(Loc.T("ui.test.none"), "body", "tight").AddTo(parent);
                return;
            }
            var head = Ui.Row("test-head").AddTo(parent);
            var by = s.Player(t.By)?.Look?.Name;
            Ui.Text(Loc.T("ui.test.record", ("n", t.N)) + (s.Multiplayer && !string.IsNullOrEmpty(by) ? " · " + by : ""), "eyebrow").AddTo(head);
            Ui.Spacer().AddTo(head);
            bool current = EnvTests.IsCurrent(s);
            Ui.Text(Loc.T(current ? "ui.test.current" : "ui.test.stale_long"), "small", current ? "go" : "tight").AddTo(head);

            bool numbers = m.Difficulty.FailureDetail != "category";
            foreach (var test in Tests)
            {
                var lv = t.Worst(test);
                var card = Ui.El("test-card", "test-card--" + LevelClass(lv)).AddTo(parent);
                var top = Ui.Row().AddTo(card);
                Ui.Text(Loc.T("ui.test." + test), "h3").AddTo(top);
                Ui.Spacer().AddTo(top);
                Ui.Text(LevelText(lv), "h3", "mono", LevelClass(lv)).AddTo(top);
                Ui.Text(Loc.T("ui.test." + test + ".what"), "small", "faint").AddTo(card);
                if (!numbers)
                {
                    if (lv != TestLevel.Pass) Ui.Text(Loc.T("ui.test.cat." + test), "body").AddTo(card);
                    continue;
                }
                foreach (var f in t.Findings.Where(x => x.Test == test))
                    Ui.Text(Sentence(f), "body").AddTo(card);
            }
            Ui.Text(Loc.T(numbers ? "ui.test.limits" : "ui.test.limits_category"), "small", "faint").AddTo(parent);
        }

        public static string LevelText(TestLevel l) => l == TestLevel.Pass ? "PASS" : l == TestLevel.Warn ? "WARN" : "FAIL";
        public static string LevelClass(TestLevel l) => l == TestLevel.Pass ? "go" : l == TestLevel.Warn ? "tight" : "over";

        /// <summary>發現的句子：A、B、C 依項目換成公斤、分鐘、瓦時、百分比。</summary>
        public static string Sentence(TestFinding f)
        {
            string a, b, c;
            switch (f.Code)
            {
                case "test.vib.overweight":
                case "test.vib.tight":
                case "test.vib.pass":
                    a = Fmt.N(f.A) + " kg"; b = Fmt.N(f.B) + " kg"; c = Fmt.Pct(f.C); break;
                case "test.tv.battery_dead":
                    a = Fmt.N(f.A, 1); b = Fmt.N(f.B, 1); c = Fmt.N(f.C) + " Wh"; break;
                case "test.tv.battery_wear":
                    a = Fmt.Pct(f.A); b = Fmt.Pct(f.B); c = Fmt.N(f.C, 1); break;
                case "test.tv.solar":
                    a = Fmt.N(f.A) + " W"; b = Fmt.N(f.B) + " W"; c = ""; break;
                case "test.tv.pass":
                    a = Fmt.N(f.A, 1); b = Fmt.N(f.B) + " Wh"; c = Fmt.N(f.C) + " Wh"; break;
                default:
                    a = Fmt.N(f.A); b = Fmt.N(f.B); c = Fmt.N(f.C); break;
            }
            return Loc.T(f.Code, ("a", a), ("b", b), ("c", c));
        }
    }

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

    /// <summary>
    /// 畫面上方的點名橫幅：多人按發射之後，每個人都要喊 GO（G 鍵）或 NO-GO（N 鍵）。
    /// 全員 GO 才發射；有人喊 NO-GO 就 HOLD，顯示是誰叫停，討論完再按一次發射重新點名。
    /// </summary>
    public sealed class PollBanner
    {
        readonly VisualElement _root;
        int _seenPoll = -1, _shownHold = -1;
        float _holdLeft;

        public PollBanner(VisualElement parent)
        {
            _root = Ui.El("poll").AddTo(parent);
            _root.style.display = DisplayStyle.None;
            var m = GameSession.Mission;
            // 進場景之前就結束的點名不要再顯示
            _seenPoll = m.S.Poll?.Id ?? -1;
            _shownHold = m.S.LastPoll?.Id ?? -1;
        }

        static MissionSession M => GameSession.Mission;

        public bool Active => M.S.Poll != null && M.S.Phase == SessionPhase.Design;
        public bool CanVote => Active && !M.S.Poll.Votes.ContainsKey(GameSession.LocalPlayer);

        public void Vote(bool go)
        {
            if (!CanVote) return;
            if (go) Sfx.Go(); else Sfx.NoGo();
            GameSession.Send(CommandType.Vote, "", go ? 1 : 0);
        }

        public void Refresh()
        {
            var s = M.S;
            if (s.Poll != null && s.Poll.Id != _seenPoll)
            {
                _seenPoll = s.Poll.Id;
                Sfx.Alert();
            }
            if (s.Poll == null && s.LastPoll != null && !s.LastPoll.Go && s.LastPoll.Id != _shownHold)
            {
                _shownHold = s.LastPoll.Id;
                _holdLeft = 12f;
                Sfx.NoGo();
            }
            Draw();
        }

        public void Tick(float dt)
        {
            if (_holdLeft <= 0) return;
            _holdLeft -= dt;
            if (_holdLeft <= 0) Draw();
        }

        void Draw()
        {
            _root.Clear();
            var s = M.S;
            if (Active)
            {
                var poll = s.Poll;
                _root.style.display = DisplayStyle.Flex;
                _root.EnableInClassList("poll--hold", false);
                var head = Ui.Row("poll-head").AddTo(_root);
                Ui.Text(Loc.T("ui.poll.title"), "eyebrow").AddTo(head);
                Ui.Spacer().AddTo(head);
                Ui.Text(Loc.T("ui.poll.called_by", ("name", Name(poll.CalledBy))), "small", "dim").AddTo(head);
                var list = Ui.Row("poll-list").AddTo(_root);
                foreach (var p in s.Players)
                {
                    var cell = Ui.El("poll-cell").AddTo(list);
                    Ui.Text(Name(p.Id) + (p.Id == GameSession.LocalPlayer ? " ★" : ""), "small").AddTo(cell);
                    Ui.Text(Loc.T(p.Station == Station.Assembly ? "ui.poll.station.assembly" : "ui.poll.station.control"), "small", "faint").AddTo(cell);
                    bool voted = poll.Votes.TryGetValue(p.Id, out var go);
                    Ui.Text(!voted ? "…" : go ? "GO" : "NO-GO", "h3", "mono", !voted ? "dim" : go ? "go" : "over").AddTo(cell);
                }
                if (CanVote)
                {
                    var row = Ui.Row("poll-actions").AddTo(_root);
                    Ui.Text(Loc.T("ui.poll.ask"), "small").AddTo(row);
                    Ui.Spacer().AddTo(row);
                    Ui.Btn("[N]  NO-GO", () => Vote(false), "btn-danger").AddTo(row);
                    Ui.Btn("[G]  GO", () => Vote(true), "btn-go").AddTo(row);
                }
                else Ui.Text(Loc.T("ui.poll.waiting"), "small", "dim").AddTo(_root);
                Ui.Text(Loc.T("ui.poll.frozen"), "small", "faint").AddTo(_root);
                return;
            }
            if (_holdLeft > 0 && s.LastPoll != null && !s.LastPoll.Go)
            {
                _root.style.display = DisplayStyle.Flex;
                _root.EnableInClassList("poll--hold", true);
                Ui.Text("HOLD", "h2", "over").AddTo(_root);
                Ui.Text(Loc.T("ui.poll.hold", ("name", Name(s.LastPoll.NoGoBy))), "body").AddTo(_root);
                return;
            }
            _root.style.display = DisplayStyle.None;
        }

        static string Name(int id)
        {
            var n = M.S.Player(id)?.Look?.Name;
            return string.IsNullOrEmpty(n) ? Loc.T("ui.poll.player", ("n", id)) : n;
        }
    }
}
