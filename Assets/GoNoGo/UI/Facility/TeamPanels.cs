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
