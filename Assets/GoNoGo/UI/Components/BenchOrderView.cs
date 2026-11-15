using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 組裝單：組裝工作台上現在組的是哪個零件、裝到第幾個小零件、下一個小零件要做什麼事。
    /// 只寫「要做什麼事」，不寫名稱——玩家要自己判斷零件盤上哪一個在做這件事（這就是這一關要學的東西）。
    /// 兩個地方共用：組裝區畫面右上角（人在組裝區、工作台上有東西時），和工作台洞洞板上的螢幕。
    /// </summary>
    public static class BenchOrderView
    {
        /// <summary>月數：最多兩位小數，不補零（0.25、0.5、1）。</summary>
        public static string Months(double v) => Fmt.Months(v);

        /// <param name="screen">畫在工作台的螢幕上（字比較大；沒有東西在組時也畫一句說明）。</param>
        public static void Build(VisualElement parent, bool screen)
        {
            if (!GameSession.HasMission) return;
            var s = GameSession.Mission.S;
            var d = GameSession.Data;
            var job = s.Workbench;
            var root = Ui.El("ord").Cls("ord--screen", screen).AddTo(parent);
            root.pickingMode = PickingMode.Ignore;

            if (job == null)
            {
                Ui.Text(Loc.T("ui.asm.order"), "eyebrow").AddTo(root);
                bool sandbox = s.Scenario(d)?.Sandbox == true;
                Ui.Text(Loc.T(sandbox ? "ui.bench.sandbox" : "ui.bench.idle"), "ord-idle").AddTo(root);
                return;
            }
            var part = d.Part(job.PartId);
            var recipe = part != null ? d.Recipe(part) : null;
            if (recipe == null) return;
            int total = recipe.Components.Count;

            var head = Ui.Row("ord-head").AddTo(root);
            Ui.Text(Loc.T("ui.asm.order"), "eyebrow").AddTo(head);
            Ui.Spacer().AddTo(head);
            Ui.Text(Loc.T("ui.asm.progress", ("n", System.Math.Min(job.Step, total)), ("total", total)), "ord-count", "mono").AddTo(head);
            var title = Ui.Row("ord-title").AddTo(root);
            Ui.Text(part.Code, "man-code", "mono").AddTo(title);
            Ui.Text(Loc.T(part.Name), "ord-name").AddTo(title);

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
                    // 還沒輪到的：先寫出來（多人時隊友可以先去拿、在旁邊等）
                    var row = Ui.Row("ord-step", "ord-step--later").AddTo(root);
                    Ui.Text((i + 1).ToString(), "ord-n", "mono").AddTo(row);
                    Ui.Text(Loc.T(c.Role), "ord-step-text").AddTo(row);
                }
            }

            Ui.Text(Loc.T("ui.asm.howto"), "ord-how").AddTo(root);
            double per = d.Constants.ReworkMonths;
            if (job.Mistakes > 0)
                Ui.Text(Loc.T("ui.asm.rework", ("n", job.Mistakes), ("m", Months(job.Mistakes * per))), "ord-rework", "ord-rework--bad").AddTo(root);
            else
                Ui.Text(Loc.T("ui.asm.rework_warn", ("m", Months(per))), "ord-rework").AddTo(root);
        }
    }
}
