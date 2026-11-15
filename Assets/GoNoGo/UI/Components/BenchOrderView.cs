using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 組裝單：一座組裝台現在的狀況。左側的小零件組合還沒認出配方時，寫現在是哪一種狀態（還沒放齊、組合錯誤、
    /// 不是這次設計選的零件）；認出來之後，列出組裝順序的提示——只寫「這一步要做什麼事的小零件」，不寫名稱，
    /// 玩家要自己判斷左側哪一個在做這件事（這就是這一關要學的東西）。
    /// 兩個地方共用：畫面右上角（人走到組裝台旁邊時），和組裝台背板上的螢幕。
    /// </summary>
    public static class BenchOrderView
    {
        /// <param name="bench">第幾座組裝台。</param>
        /// <param name="screen">畫在組裝台的螢幕上（字比較大）。</param>
        public static void Build(VisualElement parent, int bench, bool screen)
        {
            if (!GameSession.HasMission) return;
            var s = GameSession.Mission.S;
            var d = GameSession.Data;
            var b = s.BenchAt(bench);
            var root = Ui.El("ord").Cls("ord--screen", screen).AddTo(parent);
            root.pickingMode = PickingMode.Ignore;
            var head = Ui.Row("ord-head").AddTo(root);
            Ui.Text(Loc.T("ui.asm.order") + " " + (bench + 1), "eyebrow").AddTo(head);
            Ui.Spacer().AddTo(head);

            if (s.IsSandbox(d))
            {
                Ui.Text(Loc.T("ui.bench.sandbox"), "ord-idle").AddTo(root);
                return;
            }
            var status = SessionRules.BenchStatusOf(s, d, bench);
            var job = b?.Job;
            if (job == null)
            {
                // 左側：還沒認出配方
                string cls = status == BenchStatus.Unknown || status == BenchStatus.NotInDesign ? "ord-status--bad"
                    : status == BenchStatus.Waiting ? "ord-status--wait" : "";
                Ui.Text(Loc.T("ui.bench.left_n", ("n", b?.Left.Count ?? 0)), "ord-count", "mono").AddTo(head);
                Ui.Text(Ui.NoOrphan(Loc.T("ui.bench.status." + status.ToString().ToLowerInvariant())), "ord-status", cls).AddTo(root);
                if (status == BenchStatus.Empty) Ui.Text(Loc.T("ui.asm.howto_left"), "ord-how").AddTo(root);
                return;
            }
            var part = d.Part(job.PartId);
            var recipe = part != null ? d.Recipe(part) : null;
            if (recipe == null) return;
            int total = recipe.Components.Count;
            Ui.Text(Loc.T("ui.asm.progress", ("n", System.Math.Min(job.Step, total)), ("total", total)), "ord-count", "mono").AddTo(head);
            var title = Ui.Row("ord-title").AddTo(root);
            Ui.Text(part.Code, "man-code", "mono").AddTo(title);
            Ui.Text(SelectView.PartName(part), "ord-name").AddTo(title);

            if (job.Done)
            {
                Ui.Text(Loc.T("ui.asm.take"), "ord-take").AddTo(root);
                return;
            }

            for (int i = 0; i < total; i++)
            {
                var c = recipe.Components[i];
                if (i < job.Step)
                {
                    // 裝好的：打勾 + 名稱（答案揭曉，順便記住它叫什麼）
                    var row = Ui.Row("ord-step", "ord-step--done").AddTo(root);
                    Ui.Text("✔", "ord-n").AddTo(row);
                    Ui.Text(Loc.T(c.Name), "ord-step-text").AddTo(row);
                }
                else if (i == job.Step)
                {
                    var now = Ui.El("ord-now").AddTo(root);
                    Ui.Text(Loc.T("ui.asm.now"), "eyebrow").AddTo(now);
                    Ui.Text(Ui.NoOrphan(Loc.T(c.Role)), "ord-role").AddTo(now);
                }
                else
                {
                    // 還沒輪到的：先寫出來（多人時隊友可以先拿在旁邊等）
                    var row = Ui.Row("ord-step", "ord-step--later").AddTo(root);
                    Ui.Text((i + 1).ToString(), "ord-n", "mono").AddTo(row);
                    Ui.Text(Loc.T(c.Role), "ord-step-text").AddTo(row);
                }
            }

            Ui.Text(Loc.T("ui.asm.howto"), "ord-how").AddTo(root);
        }
    }
}
