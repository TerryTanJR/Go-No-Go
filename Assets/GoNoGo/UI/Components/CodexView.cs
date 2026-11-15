using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 知識卡圖鑑的畫面：左邊照類別排的卡片格子（還沒收到的只有編號和問號），右邊是選到那張卡的內容。
    /// 主選單和設施裡（按 K）共用。卡片怎麼收：見 <see cref="Codex"/>。
    /// </summary>
    public static class CodexView
    {
        static string _sel;

        static readonly string[] Cats =
        {
            PartCategory.Instrument, PartCategory.Solar, PartCategory.Battery, PartCategory.Antenna,
            PartCategory.Computer, PartCategory.Adcs, PartCategory.Propulsion, PartCategory.Shielding,
        };

        /// <summary>編號裡的數字（P-10 → 10）：照數字排，P-2 才會排在 P-10 前面。</summary>
        static int CodeNumber(string code)
        {
            int dash = code != null ? code.LastIndexOf('-') : -1;
            return dash >= 0 && int.TryParse(code.Substring(dash + 1), out var n) ? n : 0;
        }

        public static void Build(VisualElement parent, System.Action rebuild)
        {
            var data = GameSession.Data;
            var cards = Codex.Cards(data).ToList();
            // 收到幾張、精通幾張：這份清單走一次就數完，不用再各掃一次零件表
            int have = 0, mastered = 0, total = cards.Count;
            foreach (var p in cards)
            {
                if (Codex.Has(p.Id)) have++;
                if (Codex.IsMaster(p.Id)) mastered++;
            }

            var head = Ui.Row("codex-head").AddTo(parent);
            Ui.Text(Loc.T("ui.codex.count", ("n", have), ("total", total)), "h3", have >= total ? "go" : "accent").AddTo(head);
            Ui.Text(Loc.T("ui.codex.mastered", ("n", mastered)), "codex-master-count").AddTo(head);
            var track = Ui.El("codex-track").AddTo(head);
            var fill = Ui.El("codex-fill").AddTo(track);
            fill.style.width = Length.Percent(total > 0 ? 100f * have / total : 0);
            Ui.Text(Ui.NoOrphan(Loc.T("ui.codex.how")), "small", "dim", "codex-how").AddTo(parent);

            var wrap = Ui.Row("codex-wrap").AddTo(parent);
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.AddToClassList("codex-list");
            list.AddToClassList(FacPanel.KeepScrollClass);
            wrap.Add(list);
            if (_sel == null || cards.All(p => p.Id != _sel))
                _sel = cards.FirstOrDefault(p => Codex.Has(p.Id))?.Id ?? cards.FirstOrDefault()?.Id;

            foreach (var cat in Cats)
            {
                var inCat = cards.Where(p => p.Category == cat).OrderBy(p => CodeNumber(p.Code)).ThenBy(p => p.Code, System.StringComparer.Ordinal).ToList();
                if (inCat.Count == 0) continue;
                Ui.Text(Loc.T("ui.cat." + cat) + "  " + inCat.Count(p => Codex.Has(p.Id)) + "/" + inCat.Count, "small", "dim", "codex-cat").AddTo(list);
                var grid = Ui.Row("codex-grid").AddTo(list);
                foreach (var p in inCat)
                {
                    var part = p;
                    bool has = Codex.Has(p.Id), master = Codex.IsMaster(p.Id);
                    var b = Ui.Btn("", () => { _sel = part.Id; rebuild?.Invoke(); }, "codex-tile")
                        .Cls("codex-tile--locked", !has).Cls("codex-tile--on", p.Id == _sel).AddTo(grid);
                    b.Clear();
                    Ui.Text((master ? "★ " : "") + p.Code, "codex-tile-code", "mono").Cls("codex-tile-code--master", master).AddTo(b);
                    Ui.Text(has ? Ui.NoOrphan(Loc.T(p.Name)) : "?", "codex-tile-name").AddTo(b);
                    if (has && ColorUtility.TryParseHtmlString(p.Color, out var c)) b.style.borderTopColor = c;
                }
            }

            // 右邊的卡片內容可以捲動（字體放大、或說明比較長時不會擠在一起）
            var detailScroll = new ScrollView(ScrollViewMode.Vertical);
            detailScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            detailScroll.AddToClassList("codex-card");
            detailScroll.AddToClassList("codex-detail");
            wrap.Add(detailScroll);
            var detail = detailScroll.contentContainer;
            var sel = data.Part(_sel);
            if (sel == null) return;
            var top = Ui.Row("asm-head").AddTo(detail);
            Ui.Text(sel.Code, "man-code", "mono").AddTo(top);
            if (Codex.Has(sel.Id))
            {
                // 這個零件由哪些小零件組成（組裝台的配方）；null = 不用組裝
                var recipe = data.Recipe(sel);
                Ui.Text(Ui.NoOrphan(Loc.T(sel.Name)), "h3", "asm-name").AddTo(top);
                Ui.Text(Loc.T("ui.cat." + sel.Category), "eyebrow", "codex-detail-cat").AddTo(detail);
                // 精通：組裝這種構造時一次都沒裝錯。還沒精通、又要組裝的零件，寫出怎麼拿到
                if (Codex.IsMaster(sel.Id)) Ui.Text(Loc.T("ui.codex.master_tag"), "codex-master").AddTo(detail);
                else if (recipe != null) Ui.Text(Ui.NoOrphan(Loc.T("ui.codex.master_how")), "small", "dim", "codex-master-how").AddTo(detail);
                Ui.Text(Ui.NoOrphan(Loc.T(sel.Function)), "codex-fact").AddTo(detail);
                Ui.Text(Ui.NoOrphan(Loc.T(sel.Explainer)), "body", "codex-text").AddTo(detail);
                if (recipe != null)
                {
                    Ui.Text(Loc.T("ui.codex.inside"), "eyebrow", "codex-detail-cat").AddTo(detail);
                    foreach (var c in recipe.Components)
                    {
                        var row = Ui.Row("codex-comp").AddTo(detail);
                        Ui.Text(Ui.NoOrphan(Loc.T(c.Name)), "codex-comp-name").AddTo(row);
                        Ui.Text(Ui.NoOrphan(Loc.T(c.Role)), "codex-comp-role").AddTo(row);
                    }
                }
            }
            else
            {
                Ui.Text("? ? ?", "h3", "asm-name", "faint").AddTo(top);
                Ui.Text(Loc.T("ui.cat." + sel.Category), "eyebrow", "codex-detail-cat").AddTo(detail);
                Ui.Text(Ui.NoOrphan(Loc.T("ui.codex.locked")), "body", "codex-text").AddTo(detail);
            }
        }
    }

    /// <summary>設施裡按 K 打開的圖鑑。</summary>
    public sealed class CodexPanel : FacPanel
    {
        public override string Title => Loc.T("ui.codex.title");
        public override string WideClass => "modal--manual";
        protected override string ScrollKey => "codex";
        protected override void Build(VisualElement body) => CodexView.Build(body, Refresh);
    }
}
