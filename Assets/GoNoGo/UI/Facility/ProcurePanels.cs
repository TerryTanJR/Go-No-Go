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
    /// 採購機制 v2：只用錢當代價，買幾次都行，上限是「錢要留一支飛得到目前軌道的最便宜火箭」。
    /// 個人模式顯示價錢、還能買多少、營運撐得了幾年，按「下單」立刻扣錢上架。
    /// 多人模式不顯示任何金額（錢在中控室），按「送審」把採購單交給中控室批准或退回。
    /// </summary>
    public sealed class ProcurePanel : FacPanel
    {
        public override string Title => Loc.T("ui.proc.title");
        public override string WideClass => "modal--manual";

        static string _cat = PartCategory.Instrument;
        static string _sel;
        /// <summary>知識卡（科普說明）翻開了沒。預設收著：買零件只要看一句功能，想知道原理再翻開。</summary>
        static bool _cardOpen;
        VisualElement _preview;

        static readonly string[] Cats =
        {
            PartCategory.Instrument, PartCategory.Solar, PartCategory.Battery, PartCategory.Antenna,
            PartCategory.Computer, PartCategory.Adcs, PartCategory.Propulsion, PartCategory.Shielding,
        };

        protected override void Build(VisualElement body)
        {
            var s = M.S;
            bool money = !s.Multiplayer;          // 多人：組裝區看得到船、看不到錢
            bool review = SessionRules.NeedsApproval(s);
            bool open = s.Phase == SessionPhase.Design || s.Phase == SessionPhase.Briefing;
            var catalog = SessionRules.ShelfParts(D, s);
            var cats = Cats.Where(c => catalog.Any(p => p.Category == c)).ToList();
            if (!cats.Contains(_cat)) _cat = cats.FirstOrDefault() ?? PartCategory.Instrument;
            var parts = catalog.Where(p => p.Category == _cat).OrderBy(p => p.Tier).ThenBy(p => p.Code).ToList();
            if (_sel == null || parts.All(p => p.Id != _sel)) _sel = parts.FirstOrDefault()?.Id;

            // 上方：個人模式是錢包；多人是採購單的狀態
            var head = Ui.Row("proc-head").AddTo(body);
            if (money)
            {
                double room = SessionRules.Headroom(s, D);
                Ui.Text(Loc.T("ui.proc.room", ("money", Fmt.PartMoney(room))), "h3", room > 0 ? "accent" : "over").AddTo(head);
                Ui.Text(Loc.T("ui.proc.room_x", ("budget", Fmt.Money(SessionRules.Budget(s, D))), ("spent", Fmt.PartMoney(SessionRules.DevelopmentMusd(s, D))),
                    ("rocket", Loc.Launcher(SessionRules.ReserveLauncher(s, D)?.Id ?? ""))), "small", "dim", "proc-head-note").AddTo(head);
            }
            else if (s.Request != null)
            {
                Ui.Text(Loc.T("ui.proc.pending", ("n", s.Request.N)), "h3", "tight").AddTo(head);
                Ui.Text(string.Join(" ", s.Request.Parts.Select(id => D.Part(id)?.Code ?? id)), "small", "mono", "dim", "proc-head-note").AddTo(head);
                Ui.Spacer().AddTo(head);
                var w = Ui.Btn(Loc.T("ui.proc.withdraw"), () => { Sfx.Click(); GameSession.Send(CommandType.WithdrawOrder); }, "btn-ghost").AddTo(head);
                w.SetEnabled(open);
            }
            else if (s.Rejected != null)
            {
                Ui.Text(Loc.T("ui.proc.rejected", ("n", s.Rejected.N)), "h3", "over").AddTo(head);
                Ui.Text(Loc.T("ui.proc.rejected_x"), "small", "dim", "proc-head-note").AddTo(head);
            }
            else Ui.Text(Loc.T(review ? "ui.proc.no_prices_review" : "ui.proc.no_prices"), "small", "faint").AddTo(head);

            Tabs(body, cats.Select(c => "ui.cat." + c).ToArray(), cats.IndexOf(_cat), i => { _cat = cats[i]; _sel = null; Refresh(); });

            var wrap = Ui.Row("proc-wrap").AddTo(body);

            // 左：這一類的零件（誘餌加進來後一類可能有十幾個，要能捲動，不然會超出去蓋到下面的購物車列）
            var list = Scroll(wrap, "proc-list");
            var installed = new HashSet<string>(s.Bench.AllPartIds());
            foreach (var p in parts)
            {
                var id = p.Id;
                var b = Ui.Btn("", () => { _sel = id; _cardOpen = false; Sfx.Click(); Refresh(); }, "proc-item").Cls("proc-item--on", id == _sel).AddTo(list);
                b.Clear();
                var top = Ui.Row("proc-item-top").AddTo(b);
                Ui.Text(p.Code, "man-code", "mono").AddTo(top);
                Ui.Text(Loc.T(p.Name), "proc-item-name").AddTo(top);
                var st = Ui.Row("proc-item-state").AddTo(b);
                Ui.Text(Loc.T(StateKey(s, id, installed)), "small", StateClass(s, id, installed)).AddTo(st);
                if (money)
                {
                    Ui.Spacer().AddTo(st);
                    Ui.Text(Fmt.PartMoney(p.CostMusd), "small", "mono", s.Owned.Contains(id) ? "faint" : "dim").AddTo(st);
                }
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
                if (money) Ui.Text(Loc.T("ui.proc.price", ("money", Fmt.PartMoney(sel.CostMusd))), "body", "mono", "accent").AddTo(text);
                Ui.Text(Loc.T(sel.Function), "body", "proc-func").AddTo(text);
                if (sel.SunlitOnly) Ui.Text(Loc.T("ui.manual.sunlit"), "man-badge", "man-badge--warn").AddTo(text);
                // 知識卡：原理和真實任務的故事。預設收著（不讀也能玩）。這裡隨時可以翻開來讀，
                // 但卡片要自己動手組裝或安裝才會收進圖鑑（點一下就收到的話，亂點也收得齊）
                bool has = Codex.Has(sel.Id);
                var card = Ui.El("codex-card").Cls("codex-card--open", _cardOpen).AddTo(text);
                Ui.Btn((_cardOpen ? "▾  " : "▸  ") + (Codex.IsMaster(sel.Id) ? "★ " : "") + Loc.T(has ? "ui.codex.card" : "ui.codex.card_locked"), () =>
                {
                    _cardOpen = !_cardOpen;
                    Refresh();
                }, "codex-flip").AddTo(card);
                // 翻開的內容放在中間的大預覽圖上（右邊這一欄很窄，放這裡要捲好幾次才看得完）
                if (_cardOpen)
                {
                    var over = Ui.El("codex-overlay").AddTo(_preview);
                    Ui.Text(Loc.T("ui.codex.card"), "eyebrow").AddTo(over);
                    Ui.Text(Ui.NoOrphan(Loc.T(sel.Explainer)), "body", "codex-text").AddTo(over);
                }
                // 買回來是一箱小零件，要在組裝工作台親手組。只說有幾個，不列名稱：
                // 列出來就等於先告訴玩家哪幾個是真的、照什麼順序裝，工作台上的判斷就沒有意義了
                var recipe = D.Recipe(sel);
                if (recipe != null && M.Scenario?.Sandbox != true)
                {
                    // 同一種構造這一局已經親手組過：到貨就是成品，不用再組
                    bool learned = s.Learned.Contains(recipe.Key) && !s.Built.Contains(sel.Id);
                    Ui.Text(learned ? Loc.T("ui.proc.kit_learned") : Loc.T("ui.proc.kit", ("n", recipe.Components.Count)), "small", "dim", "proc-kit-title").AddTo(text);
                }

                var act = Ui.Row("proc-info-actions").AddTo(info);
                bool owned = s.Owned.Contains(sel.Id), inCart = s.Cart.Contains(sel.Id), inReq = s.Request?.Parts.Contains(sel.Id) == true;
                if (owned) Ui.Text(Loc.T(installed.Contains(sel.Id) ? "ui.proc.state.installed" : "ui.proc.owned_hint"), "body", "go").AddTo(act);
                else if (inReq) Ui.Text(Loc.T("ui.proc.state.request"), "body", "tight").AddTo(act);
                else
                {
                    var btn = Ui.Btn(Loc.T(inCart ? "ui.proc.remove" : "ui.proc.add"), () =>
                    {
                        Sfx.Click();
                        GameSession.Send(inCart ? CommandType.CartRemove : CommandType.CartAdd, sel.Id);
                    }, inCart ? "btn-ghost" : "btn-primary").AddTo(act);
                    btn.SetEnabled(open);
                }
            }

            // 下：購物車、下單（多人是送審）
            var cart = Ui.Row("proc-cart").AddTo(body);
            Ui.Text(Loc.T("ui.proc.cart"), "eyebrow").AddTo(cart);
            if (s.Cart.Count == 0) Ui.Text(Loc.T("ui.proc.cart_empty"), "small", "dim", "proc-cart-empty").AddTo(cart);
            foreach (var id in s.Cart)
            {
                var pid = id;
                var label = (D.Part(id)?.Code ?? id) + (money ? " " + Fmt.PartMoney(D.Part(id)?.CostMusd ?? 0) : "") + "  ✕";
                Ui.Btn(label, () => GameSession.Send(CommandType.CartRemove, pid), "chip", "chip--on", "mono").AddTo(cart);
            }
            Ui.Spacer().AddTo(cart);
            // 多人送審不檢查上限（錯誤訊息會讓組裝區知道錢的狀況），由中控室審查時決定
            string why = s.Cart.Count == 0 ? "err.cart_empty"
                : review ? (s.Request != null ? "err.request_pending" : null)
                : SessionRules.CanOrder(s, D, s.Cart) != SessionRules.Ok ? "err.order_over_cap" : null;
            if (money && s.Cart.Count > 0)
            {
                double cost = SessionRules.PartsMusd(s, D, s.Cart);
                double after = SessionRules.Headroom(s, D) - cost;
                double years = SessionRules.FundedYears(s, D, cost);
                Ui.Text(Loc.T("ui.proc.cart_sum", ("cost", Fmt.PartMoney(cost)), ("left", Fmt.PartMoney(after)),
                    ("years", years >= 99 ? "∞" : Fmt.N(years, 1)), ("target", s.Bench.MissionYears)), "small", after < 0 ? "over" : "dim").AddTo(cart);
            }
            if (why != null && s.Cart.Count > 0) Ui.Text(Loc.T(why), "small", "over").AddTo(cart);
            var order = Ui.Btn(Loc.T(review ? "ui.proc.submit" : "ui.proc.order"), () =>
            {
                Sfx.Confirm();
                GameSession.Send(CommandType.PlaceOrder);
                Hud.Toast(Loc.T(review ? "ui.proc.submitted" : "ui.proc.ordered"));
                Hud.Signal("order");
            }, "btn-go", "btn-big").AddTo(cart);
            order.SetEnabled(why == null && open);

            if (s.Orders.Count > 0)
            {
                var hist = Ui.Row("proc-hist").AddTo(body);
                foreach (var o in s.Orders)
                    Ui.Text(Loc.T("ui.proc.hist", ("n", o.N)) + "  " + string.Join(" ", o.Parts.Select(id => D.Part(id)?.Code ?? id))
                            + (money ? "  " + Fmt.PartMoney(o.Parts.Sum(id => D.Part(id)?.CostMusd ?? 0)) : ""), "small", "dim", "mono", "proc-hist-item").AddTo(hist);
            }
        }

        static string StateKey(SessionState s, string id, HashSet<string> installed) =>
            installed.Contains(id) ? "ui.proc.state.installed"
            : s.Owned.Contains(id) ? "ui.proc.state.shelf"
            : s.Request?.Parts.Contains(id) == true ? "ui.proc.state.request"
            : s.Cart.Contains(id) ? "ui.proc.state.cart"
            : "ui.proc.state.catalog";

        static string StateClass(SessionState s, string id, HashSet<string> installed) =>
            installed.Contains(id) ? "go" : s.Owned.Contains(id) ? "accent"
            : s.Request?.Parts.Contains(id) == true || s.Cart.Contains(id) ? "tight" : "faint";

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
    /// 列的寫法和儀表板一樣（DashboardView.Row）：學員難度正常的列變淡、有問題的寫差多少，經費也不拆細項；
    /// 「營運撐得了幾年」只在不夠時才列出來。
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
            bool basic = m.Difficulty.DashboardBasic;

            // 多人：組裝區送來的採購單放在最上面，中控室一打開就看得到批准／退回，不用往下捲
            VisualElement right = body;
            if (compact)
            {
                var cols = Ui.Row("dash-wall").AddTo(body);
                MissionProgressView.Build(Ui.El("dash-wall-left").AddTo(cols), true);
                right = Ui.El("dash-wall-right").AddTo(cols);
                if (s.Request != null) Review(right, s, data, true);
            }
            else
            {
                if (s.Request != null) Review(body, s, data, false);
                MissionProgressView.Build(body, false);
            }

            // 錢：開銷對預算。採購機制 v2：開發費 = 基本開發費 + 擁有的所有零件（開局零件和買過的），裝不裝都一樣
            var head = Ui.Row("dash-head").AddTo(right);
            Ui.Text(Loc.T("ui.goals.money"), "eyebrow", "dash-sec").AddTo(head);
            var grid = Ui.El("dash-grid").AddTo(right);
            var b = ev[ResourceId.Budget];
            Row(grid, Loc.T("res.budget"), Fmt.Resource(b), colors ? DashboardView.Gap(b) : Fmt.Pct(b.Margin), colors ? Status(b.Status) : null);
            var co = ev.Cost;
            if (!basic)
            {
                Row(grid, Loc.T("ui.goals.dev"), Fmt.PartMoney(co.DevelopmentMusd), Loc.T("ui.goals.dev_x"), null);
                Row(grid, Loc.T("ui.goals.launch"), Fmt.Money(co.LaunchMusd), Loc.Launcher(s.Reported.LauncherId), null);
                Row(grid, Loc.T("ui.goals.ops"), Fmt.Money(co.OpsTotalMusd), Fmt.Money(co.OpsPerYearMusd) + "/yr", null);
            }
            // 零件還能買多少：預算 − 已花 − 預留的火箭（飛得到目前軌道的最便宜一支）
            double room = SessionRules.Headroom(s, data);
            Row(grid, Loc.T("ui.goals.room"), Fmt.PartMoney(room), Loc.T("ui.goals.room_x", ("rocket", Loc.Launcher(SessionRules.ReserveLauncher(s, data)?.Id ?? ""))),
                room > 0.005 ? null : "over");
            // 營運經費撐得了幾年（用目前選的火箭和地面站）：不夠的話任務會提前結束；夠的時候不列
            double funded = SessionRules.FundedYears(s, data);
            int years = s.Reported.MissionYears;
            if (funded < years)
                Row(grid, Loc.T("ui.goals.funded"), Fmt.Years(funded), Loc.T("ui.goals.funded_x", ("years", years)), funded <= 0 ? "over" : "tight");
            var t = ev[ResourceId.Time];
            Row(grid, Loc.T("res.time"), Fmt.Resource(t), colors ? DashboardView.Gap(t) : Fmt.Pct(t.Margin), colors ? Status(t.Status) : null);

            // 採購紀錄：每一單買了什麼、花多少（價格只在中控室看得到）
            if (s.Orders.Count > 0)
            {
                Ui.Text(Loc.T("ui.goals.history"), "eyebrow", "dash-sec").AddTo(right);
                var hist = Ui.El("dash-grid").AddTo(right);
                foreach (var o in s.Orders)
                {
                    double cost = o.Parts.Sum(id => data.Part(id)?.CostMusd ?? 0);
                    Row(hist, Loc.T("ui.proc.hist", ("n", o.N)), Fmt.PartMoney(cost), string.Join(" ", o.Parts.Select(id => data.Part(id)?.Code ?? id)), null);
                }
            }
            if (s.Cart.Count > 0 && s.Request == null)
            {
                double cart = SessionRules.PartsMusd(s, data, s.Cart);
                Row(right, Loc.T("ui.goals.cart"), Fmt.PartMoney(cart), string.Join(" ", s.Cart.Select(id => data.Part(id)?.Code ?? id)), "tight");
            }
            if (!compact) Ui.Text(Loc.T("ui.goals.ask_bay"), "small", "faint").AddTo(right);
        }

        /// <summary>
        /// 審查採購單：每一項的價格、合計、下單後還能花多少、營運撐得了幾年。超過上限只能退回。
        /// 牆上的大螢幕（compact）只顯示，按鈕在「任務目標與預算」的控制台。
        /// </summary>
        static void Review(VisualElement parent, SessionState s, GameData data, bool compact)
        {
            var req = s.Request;
            double cost = SessionRules.PartsMusd(s, data, req.Parts);
            double after = SessionRules.Headroom(s, data) - cost;
            bool ok = SessionRules.CanOrder(s, data, req.Parts) == SessionRules.Ok;
            var by = s.Player(req.By)?.Look?.Name;
            Ui.Text(Loc.T("ui.review.title", ("n", req.N)) + (string.IsNullOrEmpty(by) ? "" : " · " + by), "eyebrow", "dash-sec", "tight").AddTo(parent);
            var grid = Ui.El("dash-grid").AddTo(parent);
            if (!compact)
                foreach (var id in req.Parts)
                {
                    var p = data.Part(id);
                    Row(grid, p?.Code ?? id, Fmt.PartMoney(p?.CostMusd ?? 0), p != null ? Loc.T(p.Name) : "", null);
                }
            Row(grid, Loc.T("ui.review.total"), Fmt.PartMoney(cost), compact ? string.Join(" ", req.Parts.Select(id => data.Part(id)?.Code ?? id)) : "", null);
            Row(grid, Loc.T("ui.review.after"), Fmt.PartMoney(after),
                Loc.T("ui.goals.room_x", ("rocket", Loc.Launcher(SessionRules.ReserveLauncher(s, data)?.Id ?? ""))), ok ? null : "over");
            double years = SessionRules.FundedYears(s, data, cost);
            int target = s.Reported.MissionYears;
            if (years < target)
                Row(grid, Loc.T("ui.review.funded"), Fmt.Years(years), Loc.T("ui.goals.funded_x", ("years", target)), years <= 0 ? "over" : "tight");
            if (compact)
            {
                Ui.Text(Loc.T("ui.review.go_console"), "small", "tight").AddTo(parent);
                return;
            }
            if (!ok) Ui.Text(Loc.T("ui.review.over"), "small", "over").AddTo(parent);
            bool open = s.Phase == SessionPhase.Design || s.Phase == SessionPhase.Briefing;
            var row = Ui.Row("modal-actions").AddTo(parent);
            var rj = Ui.Btn(Loc.T("ui.review.reject"), () => { Sfx.Click(); GameSession.Send(CommandType.RejectOrder); }, "btn-ghost", "btn-big").AddTo(row);
            rj.SetEnabled(open);
            Ui.Spacer().AddTo(row);
            var ap = Ui.Btn(Loc.T("ui.review.approve"), () => { Sfx.Confirm(); GameSession.Send(CommandType.ApproveOrder); }, "btn-go", "btn-big").AddTo(row);
            ap.SetEnabled(ok && open);
        }

        static string Status(ResourceStatus st) => DashboardView.Status(st);

        static void Row(VisualElement parent, string name, string value, string extra, string cls) => DashboardView.Row(parent, name, value, extra, cls);
    }
}
