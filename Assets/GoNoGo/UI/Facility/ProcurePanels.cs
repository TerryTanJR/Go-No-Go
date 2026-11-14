using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // ======================================================================== 採購終端（組裝區）

    /// <summary>
    /// 組裝區的採購終端：左邊零件清單、中間轉動的 3D 模型、右邊科普說明。
    /// 這裡沒有任何數字（價格、規格都在中控室的型錄），所以「買哪個」「買得起嗎」都要問中控室。
    /// 加進購物車後按「下單」，零件就上架；採購次數有限，補單要加急件費、時程延後。
    /// </summary>
    public sealed class ProcurePanel : FacPanel
    {
        public override string Title => Loc.T("ui.proc.title");
        public override string WideClass => "modal--manual";

        static string _cat = PartCategory.Instrument;
        static string _sel;
        VisualElement _preview;

        static readonly string[] Cats =
        {
            PartCategory.Instrument, PartCategory.Solar, PartCategory.Battery, PartCategory.Antenna,
            PartCategory.Computer, PartCategory.Adcs, PartCategory.Propulsion, PartCategory.Shielding,
        };

        protected override void Build(VisualElement body)
        {
            var s = M.S;
            var catalog = SessionRules.ShelfParts(D, s);
            var cats = Cats.Where(c => catalog.Any(p => p.Category == c)).ToList();
            if (!cats.Contains(_cat)) _cat = cats.FirstOrDefault() ?? PartCategory.Instrument;
            var parts = catalog.Where(p => p.Category == _cat).OrderBy(p => p.Tier).ThenBy(p => p.Code).ToList();
            if (_sel == null || parts.All(p => p.Id != _sel)) _sel = parts.FirstOrDefault()?.Id;

            // 上方：剩幾次採購、下一次是原價還是補單
            int left = SessionRules.OrdersLeft(s, D);
            var head = Ui.Row("proc-head").AddTo(body);
            Ui.Text(Loc.T("ui.proc.left", ("n", left)), "h3", left > 0 ? "accent" : "over").AddTo(head);
            Ui.Text(Loc.T(left == 0 ? "ui.proc.none_left" : SessionRules.RebuildPending(s) ? "ui.proc.next_rebuild"
                : s.Orders.Count == 0 ? "ui.proc.next_first" : "ui.proc.next_rush"), "small", "dim", "proc-head-note").AddTo(head);
            Ui.Spacer().AddTo(head);
            Ui.Text(Loc.T("ui.proc.no_prices"), "small", "faint").AddTo(head);

            Tabs(body, cats.Select(c => "ui.cat." + c).ToArray(), cats.IndexOf(_cat), i => { _cat = cats[i]; _sel = null; Refresh(); });

            var wrap = Ui.Row("proc-wrap").AddTo(body);

            // 左：這一類的零件（誘餌加進來後一類可能有十幾個，要能捲動，不然會超出去蓋到下面的購物車列）
            var list = Scroll(wrap, "proc-list");
            var installed = new HashSet<string>(s.Bench.AllPartIds());
            foreach (var p in parts)
            {
                var id = p.Id;
                var b = Ui.Btn("", () => { _sel = id; Sfx.Click(); Refresh(); }, "proc-item").Cls("proc-item--on", id == _sel).AddTo(list);
                b.Clear();
                var top = Ui.Row("proc-item-top").AddTo(b);
                Ui.Text(p.Code, "man-code", "mono").AddTo(top);
                Ui.Text(Loc.T(p.Name), "proc-item-name").AddTo(top);
                Ui.Text(Loc.T(StateKey(s, id, installed)), "small", StateClass(s, id, installed), "proc-item-state").AddTo(b);
            }

            // 中：3D 模型
            _preview = Ui.El("proc-preview").AddTo(wrap);
            var tex = _sel != null ? PartPreview.Show?.Invoke(_sel) : null;
            if (tex != null) _preview.style.backgroundImage = new StyleBackground(Background.FromRenderTexture((RenderTexture)tex));
            else Ui.Text(Loc.T("ui.proc.no_preview"), "small", "faint").AddTo(_preview);

            // 右：科普說明（可捲動）與加入購物車（固定在下面）。
            // 說明很長時以前會把按鈕擠出這一欄，掉到購物車列底下，點擊全被購物車列吃掉。
            var info = Ui.El("proc-info").AddTo(wrap);
            var sel = D.Part(_sel);
            if (sel != null)
            {
                var text = Scroll(info, "proc-info-text");
                var top = Ui.Row("proc-info-top").AddTo(text);
                Ui.Text(sel.Code, "man-code", "mono").AddTo(top);
                Ui.Text(Loc.T(sel.Name), "h2", "proc-info-name").AddTo(top);
                Ui.Text(Loc.T("ui.cat." + sel.Category), "eyebrow", "proc-info-cat").AddTo(text);
                Ui.Text(Loc.T(sel.Function), "body", "proc-func").AddTo(text);
                Ui.Text(Loc.T(sel.Explainer), "body", "proc-explainer").AddTo(text);
                if (sel.SunlitOnly) Ui.Text(Loc.T("ui.manual.sunlit"), "man-badge", "man-badge--warn").AddTo(text);

                var act = Ui.Row("proc-info-actions").AddTo(info);
                bool owned = s.Owned.Contains(sel.Id), inCart = s.Cart.Contains(sel.Id);
                if (owned) Ui.Text(Loc.T(installed.Contains(sel.Id) ? "ui.proc.state.installed" : "ui.proc.owned_hint"), "body", "go").AddTo(act);
                else
                {
                    var btn = Ui.Btn(Loc.T(inCart ? "ui.proc.remove" : "ui.proc.add"), () =>
                    {
                        Sfx.Click();
                        GameSession.Send(inCart ? CommandType.CartRemove : CommandType.CartAdd, sel.Id);
                    }, inCart ? "btn-ghost" : "btn-primary").AddTo(act);
                    btn.SetEnabled(s.Phase == SessionPhase.Design || s.Phase == SessionPhase.Briefing);
                }
            }

            // 下：購物車、下單、採購紀錄
            var cart = Ui.Row("proc-cart").AddTo(body);
            Ui.Text(Loc.T("ui.proc.cart"), "eyebrow").AddTo(cart);
            if (s.Cart.Count == 0) Ui.Text(Loc.T("ui.proc.cart_empty"), "small", "dim", "proc-cart-empty").AddTo(cart);
            foreach (var id in s.Cart)
            {
                var pid = id;
                Ui.Btn((D.Part(id)?.Code ?? id) + "  ✕", () => GameSession.Send(CommandType.CartRemove, pid), "chip", "chip--on", "mono").AddTo(cart);
            }
            Ui.Spacer().AddTo(cart);
            var why = s.Cart.Count == 0 ? "err.cart_empty" : left == 0 ? "err.no_orders" : null;
            if (why != null && s.Cart.Count > 0) Ui.Text(Loc.T(why), "small", "over").AddTo(cart);
            var order = Ui.Btn(Loc.T("ui.proc.order"), () =>
            {
                Sfx.Confirm();
                GameSession.Send(CommandType.PlaceOrder);
                Hud.Toast(Loc.T("ui.proc.ordered"));
                Hud.Signal("order");
            }, "btn-go", "btn-big").AddTo(cart);
            order.SetEnabled(why == null && (s.Phase == SessionPhase.Design || s.Phase == SessionPhase.Briefing));

            if (s.Orders.Count > 0)
            {
                var hist = Ui.Row("proc-hist").AddTo(body);
                foreach (var o in s.Orders)
                    Ui.Text(Loc.T(o.Rebuild ? "ui.proc.hist_rebuild" : o.N > 1 ? "ui.proc.hist_rush" : "ui.proc.hist", ("n", o.N))
                            + "  " + string.Join(" ", o.Parts.Select(id => D.Part(id)?.Code ?? id)), "small", "dim", "mono", "proc-hist-item").AddTo(hist);
            }
        }

        static string StateKey(SessionState s, string id, HashSet<string> installed) =>
            installed.Contains(id) ? "ui.proc.state.installed"
            : s.Owned.Contains(id) ? "ui.proc.state.shelf"
            : s.Cart.Contains(id) ? "ui.proc.state.cart"
            : "ui.proc.state.catalog";

        static string StateClass(SessionState s, string id, HashSet<string> installed) =>
            installed.Contains(id) ? "go" : s.Owned.Contains(id) ? "accent" : s.Cart.Contains(id) ? "tight" : "faint";

        public override void Tick(float dt) => _preview?.MarkDirtyRepaint();

        public override void Detach()
        {
            PartPreview.Hide?.Invoke();
            _preview = null;
            base.Detach();
        }
    }

    // ======================================================================== 任務目標與預算（中控室）

    public sealed class GoalsPanel : FacPanel
    {
        public override string Title => Loc.T("ui.goals.title");
        public override string WideClass => "modal--dash";
        protected override void Build(VisualElement body) => GoalsView.Build(Scroll(body), false);
    }

    /// <summary>
    /// 中控室的任務目標與預算：進度、問到的需求（目標值），以及錢和時程。
    /// 船上的物理數字（電力、質量、延遲…）在組裝區的大螢幕上，這裡看不到，要問組裝區。
    /// </summary>
    public static class GoalsView
    {
        public static void Build(VisualElement body, bool compact)
        {
            var m = GameSession.Mission;
            var data = GameSession.Data;
            var ev = m.Dashboard;
            var s = m.S;
            bool colors = m.Difficulty.DashboardColors;

            VisualElement right = body;
            if (compact)
            {
                var cols = Ui.Row("dash-wall").AddTo(body);
                MissionProgressView.Build(Ui.El("dash-wall-left").AddTo(cols), true);
                right = Ui.El("dash-wall-right").AddTo(cols);
            }
            else MissionProgressView.Build(body, false);

            // 錢：開銷對預算
            var head = Ui.Row("dash-head").AddTo(right);
            Ui.Text(Loc.T("ui.goals.money"), "eyebrow", "dash-sec").AddTo(head);
            Ui.Spacer().AddTo(head);
            if (!compact) Ui.Text(Loc.Orbit(s.Reported.OrbitId) + " · " + Loc.Launcher(s.Reported.LauncherId) + " · " + Fmt.Years(s.Reported.MissionYears), "small", "dim").AddTo(head);
            var grid = Ui.El("dash-grid").AddTo(right);
            var b = ev[ResourceId.Budget];
            Row(grid, Loc.T("res.budget"), Fmt.Resource(b), Fmt.Pct(b.Margin), colors ? Status(b.Status) : null);
            var co = ev.Cost;
            double parts = co.DevelopmentMusd - s.Reported.ProcurementMusd;
            Row(grid, Loc.T("ui.goals.dev"), Fmt.Money(parts), "", null);
            Row(grid, Loc.T("ui.goals.launch"), Fmt.Money(co.LaunchMusd), Loc.Launcher(s.Reported.LauncherId), null);
            Row(grid, Loc.T("ui.goals.ops"), Fmt.Money(co.OpsTotalMusd), Fmt.Money(co.OpsPerYearMusd) + "/yr", null);
            Row(grid, Loc.T("ui.goals.waste"), Fmt.Money(s.Reported.ProcurementMusd), Loc.T("ui.goals.waste_x"), s.Reported.ProcurementMusd > 0.005 ? "tight" : null);
            var t = ev[ResourceId.Time];
            Row(grid, Loc.T("res.time"), Fmt.Resource(t), Fmt.Pct(t.Margin), colors ? Status(t.Status) : null);
            Row(grid, Loc.T("ui.goals.orders"), Loc.T("ui.proc.left", ("n", SessionRules.OrdersLeft(s, data))), "", null);

            // 採購紀錄：每一單買了什麼、花多少（價格只在中控室看得到）
            if (s.Orders.Count > 0)
            {
                Ui.Text(Loc.T("ui.goals.history"), "eyebrow", "dash-sec").AddTo(right);
                var hist = Ui.El("dash-grid").AddTo(right);
                foreach (var o in s.Orders)
                {
                    double cost = o.Parts.Sum(id => (data.Part(id)?.CostMusd ?? 0) * o.PriceFactor);
                    string label = Loc.T(o.Rebuild ? "ui.proc.hist_rebuild" : o.N > 1 ? "ui.proc.hist_rush" : "ui.proc.hist", ("n", o.N));
                    Row(hist, label, Fmt.Money(cost), string.Join(" ", o.Parts.Select(id => data.Part(id)?.Code ?? id)), null);
                }
            }
            if (s.Cart.Count > 0)
            {
                double cart = s.Cart.Sum(id => data.Part(id)?.CostMusd ?? 0) * SessionRules.NextPriceFactor(s, data);
                Row(right, Loc.T("ui.goals.cart"), Fmt.Money(cart), string.Join(" ", s.Cart.Select(id => data.Part(id)?.Code ?? id)), "tight");
            }
            if (!compact) Ui.Text(Loc.T("ui.goals.ask_bay"), "small", "faint").AddTo(right);
        }

        static string Status(ResourceStatus st) => st == ResourceStatus.Over ? "over" : st == ResourceStatus.Tight ? "tight" : "go";

        static void Row(VisualElement parent, string name, string value, string extra, string cls)
        {
            var r = Ui.Row("dash-row").AddTo(parent);
            if (cls != null) Ui.El("dash-lamp", "dash-lamp--" + cls).AddTo(r);
            Ui.Text(name, "dash-k").AddTo(r);
            Ui.Spacer().AddTo(r);
            Ui.Text(value, "mono", "dash-v", cls).AddTo(r);
            Ui.Text(extra, "small", "dim", "dash-x", "mono").AddTo(r);
        }
    }
}
