using System;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // ======================================================================== 線索（放大看）
    //
    // 線索散在兩個房間（紙條、白板、黑板、書、設計圖；貼在牆上、放在桌面和架子旁、掉在地上）：靠近按 E 放大，才看得出內容。
    // 放大的內容只有自己看得到，不能投到設計主螢幕，要靠口頭告訴隊友，再由人把數字填回設計主螢幕。
    // 一則線索揭露一個零件的幾個欄位。內容用模板從 parts.json 自動產生（不是一則一則手寫），
    // 呈現方式有四種：規格表、一段敘述、長條圖、設計圖上標註的數字。每局位置不同（Clues.Place）。

    public sealed class CluePanel : FacPanel
    {
        readonly string _spot;
        readonly string _medium;
        int _page;

        public CluePanel(string spotId)
        {
            _spot = spotId ?? "";
            _medium = Clues.Spot(_spot)?.Medium ?? "paper";
        }

        public override string Title => Loc.T("ui.clue.medium." + _medium);
        public override string WideClass => "modal--clue";

        /// <summary>
        /// 畫面上的內容只跟這一局線索的擺法（<see cref="MissionSession.CluesAt"/> 用的那幾項）和翻到第幾張有關：
        /// 別人做了別的事（狀態變了）不用整張重畫。
        /// </summary>
        string _drawn;

        public override void Refresh()
        {
            var s = M.S;
            string sig = s.ScenarioId + "/" + s.DifficultyId + "/" + s.Solo + "/" + s.ClueSeed + "/" + _page;
            if (sig == _drawn && Body != null && Body.childCount > 0) return;
            _drawn = sig;
            base.Refresh();
        }

        protected override void Build(VisualElement body)
        {
            var clues = M.CluesAt(_spot);
            var sheet = Ui.El("clue", "clue--" + _medium).AddTo(body);
            if (clues.Count == 0)
            {
                // 這裡這一局沒有線索：寫的是一則太空知識，放大看了才知道沒有要找的數值
                Ui.Text(Loc.T("ui.clue.trivia.head"), "clue-trivia-head").AddTo(sheet);
                Ui.Text(Ui.NoOrphan(Loc.T("ui.clue.trivia." + M.TriviaAt(_spot))), "clue-trivia").AddTo(sheet);
                Ui.Text(Loc.T("ui.clue.nothing"), "small", "faint", "clue-foot").AddTo(body);
                return;
            }
            _page = Math.Max(0, Math.Min(clues.Count - 1, _page));
            ClueView.Build(sheet, clues[_page]);

            var foot = Ui.Row("clue-nav").AddTo(body);
            if (clues.Count > 1)
            {
                Ui.Btn("‹  " + Loc.T("ui.manual.prev"), () => Flip(-1), "book-turn").AddTo(foot).SetEnabled(_page > 0);
                Ui.Spacer().AddTo(foot);
                Ui.Text(Loc.T("ui.manual.page", ("n", _page + 1), ("total", clues.Count)), "book-pageno", "mono").AddTo(foot);
                Ui.Spacer().AddTo(foot);
                Ui.Btn(Loc.T("ui.manual.next") + "  ›", () => Flip(1), "book-turn").AddTo(foot).SetEnabled(_page < clues.Count - 1);
            }
        }

        void Flip(int dir)
        {
            int n = M.CluesAt(_spot).Count;
            int to = Math.Max(0, Math.Min(n - 1, _page + dir));
            if (to == _page) return;
            _page = to;
            Sfx.Page();
            Refresh();
        }

        public override void Tick(float dt)
        {
            var kb = Keyboard.current;
            if (kb == null || Body == null || UIRoot.IsTyping) return;
            if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) Flip(1);
            else if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) Flip(-1);
        }
    }

    /// <summary>一則線索的畫面（四種模板）。數字一律是 parts.json 的真實數值。</summary>
    public static class ClueView
    {
        static GameData D => GameSession.Data;

        /// <summary>零件的稱呼：編號 + 名稱（名稱後面括號裡的容量、速率拿掉：那是另一格要找的數值）。</summary>
        static string PartLabel(PartDef p) => p.Code + " " + Blanks.StripParen(p.Name.Get(Loc.Lang));

        static string Value(PartDef p, string field) => SelectView.Num(PartFields.Get(p, field));

        public static void Build(VisualElement sheet, Clue clue)
        {
            var p = D.Part(clue.PartId);
            if (p == null) return;
            switch (clue.Kind)
            {
                case ClueKind.Table: Table(sheet, p, clue); break;
                case ClueKind.Note: Note(sheet, p, clue); break;
                case ClueKind.Chart: Chart(sheet, p, clue); break;
                default: Drawing(sheet, p, clue); break;
            }
        }

        /// <summary>規格表：一列一個欄位。</summary>
        static void Table(VisualElement sheet, PartDef p, Clue clue)
        {
            Ui.Text(Loc.T("ui.clue.table.title"), "clue-kicker").AddTo(sheet);
            Ui.Text(PartLabel(p), "clue-title").AddTo(sheet);
            Ui.Text(Loc.T("ui.cat." + p.Category), "clue-sub").AddTo(sheet);
            var table = Ui.El("clue-table").AddTo(sheet);
            foreach (var f in clue.Fields)
            {
                var row = Ui.Row("clue-tr").AddTo(table);
                Ui.Text(Loc.T("ui.field." + f), "clue-td", "clue-td--k").AddTo(row);
                Ui.Text(Value(p, f), "clue-td", "clue-td--v", "mono").AddTo(row);
                Ui.Text(Fmt.Unit(PartFields.Unit(f)), "clue-td", "clue-td--u").AddTo(row);
            }
            Ui.Text(Loc.T("ui.clue.table.foot"), "clue-small").AddTo(sheet);
        }

        /// <summary>一段敘述：測試紀錄、工程師筆記裡提到的數字。每個欄位一句，接在一起。</summary>
        static void Note(VisualElement sheet, PartDef p, Clue clue)
        {
            var phrases = clue.Fields.Select(f => Loc.T("ui.clue.phrase." + f, ("v", Value(p, f)))).ToList();
            string joined = string.Join(Loc.IsZh ? "；" : "; ", phrases);
            Ui.Text(Loc.T("ui.clue.note.head." + clue.Variant), "clue-kicker").AddTo(sheet);
            Ui.Text(Ui.NoOrphan(Loc.T("ui.clue.note." + clue.Variant, ("part", PartLabel(p)), ("facts", joined))), "clue-note").AddTo(sheet);
            Ui.Text(Loc.T("ui.clue.note.sign." + clue.Variant), "clue-small", "clue-sign").AddTo(sheet);
        }

        /// <summary>
        /// 圖表：一個欄位一條長條，每條有自己的刻度（0 到一個整數上限）。數值要從圖上讀：
        /// 長條的末端往下對到刻度，那個刻度寫的就是數值。
        /// </summary>
        static void Chart(VisualElement sheet, PartDef p, Clue clue)
        {
            Ui.Text(Loc.T("ui.clue.chart.title"), "clue-kicker").AddTo(sheet);
            Ui.Text(PartLabel(p), "clue-title").AddTo(sheet);
            foreach (var f in clue.Fields)
            {
                double v = PartFields.Get(p, f);
                double max = NiceMax(v);
                float pct = (float)(v / max * 100);
                var box = Ui.El("clue-bar").AddTo(sheet);
                Ui.Text(Loc.T("ui.field." + f) + "（" + Fmt.Unit(PartFields.Unit(f)) + "）", "clue-bar-k").AddTo(box);
                var track = Ui.El("clue-bar-track").AddTo(box);
                Ui.El("clue-bar-fill").AddTo(track).style.width = Length.Percent(pct);
                // 四條格線（每 1/4 一條），讓人看得出長條大概到哪裡
                for (int i = 1; i < 4; i++) Ui.El("clue-bar-grid").AddTo(track).style.left = Length.Percent(i * 25);
                var axis = Ui.El("clue-bar-axis").AddTo(box);
                Ui.Text("0", "clue-bar-tick", "mono").AddTo(axis).style.left = 0;
                var end = Ui.Text(SelectView.Num(max), "clue-bar-tick", "clue-bar-tick--end", "mono").AddTo(axis);
                end.style.right = 0;
                // 長條末端的刻度：這就是數值
                var mark = Ui.El("clue-bar-mark").AddTo(axis);
                mark.style.left = Length.Percent(pct);
                Ui.Text(SelectView.Num(v), "clue-bar-value", "mono").AddTo(mark);
            }
            Ui.Text(Loc.T("ui.clue.chart.foot"), "clue-small").AddTo(sheet);
        }

        /// <summary>比數值大的「整數」上限（1、2、2.5、5 × 10ⁿ），而且數值不會剛好貼在最右邊。</summary>
        public static double NiceMax(double v)
        {
            if (v <= 0) return 1;
            double pow = Math.Pow(10, Math.Floor(Math.Log10(v)));
            foreach (double k in new[] { 1.0, 2.0, 2.5, 5.0, 10.0, 20.0 })
                if (k * pow >= v * 1.15) return k * pow;
            return 20 * pow;
        }

        /// <summary>設計圖：中間是零件的外框，四周用尺寸線標出數字。</summary>
        static void Drawing(VisualElement sheet, PartDef p, Clue clue)
        {
            Ui.Text(Loc.T("ui.clue.drawing.title"), "clue-kicker").AddTo(sheet);
            Ui.Text(PartLabel(p), "clue-title").AddTo(sheet);
            var plan = Ui.El("clue-plan").AddTo(sheet);
            var shape = Ui.El("clue-shape", "clue-shape--" + p.Category).AddTo(plan);
            Ui.Text(p.Code, "clue-shape-code", "mono").AddTo(shape);
            // 最多三個欄位：上、右、下各一條標註
            string[] sides = { "top", "right", "bottom" };
            for (int i = 0; i < clue.Fields.Count && i < sides.Length; i++)
            {
                string f = clue.Fields[i];
                var call = Ui.El("clue-call", "clue-call--" + sides[i]).AddTo(plan);
                Ui.El("clue-call-line").AddTo(call);
                Ui.Text(Loc.T("ui.field." + f), "clue-call-k").AddTo(call);
                Ui.Text(Value(p, f) + " " + Fmt.Unit(PartFields.Unit(f)), "clue-call-v", "mono").AddTo(call);
            }
            Ui.Text(Loc.T("ui.clue.drawing.foot", ("cat", Loc.T("ui.cat." + p.Category))), "clue-small").AddTo(sheet);
        }
    }
}
