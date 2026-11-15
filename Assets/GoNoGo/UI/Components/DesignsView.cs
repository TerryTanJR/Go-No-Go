using System;
using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 設計圖庫：儲存、分享、比較任務設計（官方挑戰列的加分功能）。
    /// 儲存：把現在的設計存在這台電腦。分享：複製一行分享碼，對方貼上就匯入，不用伺服器。
    /// 比較：勾 2–3 份（任務中還可以加上「目前的設計」），六項資源、壽命、科學、成績並排，每一列最好的那格標出來。
    /// 存過的設計可以在沙盒打開接著改。主選單和設施裡（按 L）共用。
    /// </summary>
    public static class DesignsView
    {
        public const int MaxCompare = 3;
        const string Current = "@current";

        static readonly List<string> _picked = new List<string>();
        static string _name = "";
        static string _code = "";
        static string _msg = "";
        static bool _msgBad;
        static string _msgLang = "";
        static string _confirmDelete;

        /// <summary>
        /// 重新打開圖庫（進主選單的頁面、在設施裡按 L）：上一次留下的訊息、貼到一半的分享碼、勾選的比較都清掉。
        /// 重畫（按了某個按鈕）不會呼叫這裡，所以操作到一半的東西不會不見。
        /// </summary>
        public static void Open()
        {
            _picked.Clear();
            _name = "";
            _code = "";
            _msg = "";
            _confirmDelete = null;
        }

        static GameData D => GameSession.Data;
        static bool InMission => GameSession.HasMission;
        static MissionSession M => GameSession.Mission;

        /// <summary>任務中「目前的設計」：設計階段是整合台上的；發射之後是飛上去的那一份。</summary>
        static Design CurrentDesign()
        {
            if (!InMission) return null;
            var s = M.S;
            return s.Phase == SessionPhase.Design || s.Phase == SessionPhase.Briefing || s.LastLaunch == null ? s.Bench : s.LastLaunch.Design;
        }

        public static void Build(VisualElement parent, Action rebuild)
        {
            var all = DesignLibrary.All;
            _picked.RemoveAll(id => id != Current && DesignLibrary.Find(id) == null);
            if (!InMission) _picked.Remove(Current);

            // ---- 上方：儲存目前的設計、匯入分享碼
            var top = Ui.Row("dsg-top").AddTo(parent);
            if (InMission)
            {
                var save = Ui.El("dsg-box").AddTo(top);
                Ui.Text(Loc.T("ui.designs.save_title"), "eyebrow").AddTo(save);
                var row = Ui.Row("dsg-row").AddTo(save);
                var name = new TextField { value = _name, maxLength = DesignCode.MaxName };
                name.AddToClassList("code-field");
                name.AddToClassList("dsg-field");
                name.textEdition.placeholder = DesignLibrary.DefaultName(D, CurrentDesign());
                name.RegisterValueChangedCallback(e => _name = e.newValue);
                row.Add(name);
                Ui.Btn(Loc.T("ui.designs.save"), () =>
                {
                    var saved = SaveCurrent();
                    _name = "";
                    Say(Loc.T("ui.designs.saved", ("name", saved.name)), false);
                    Sfx.Confirm();
                    rebuild?.Invoke();
                }, "btn-primary").AddTo(row);
            }
            var imp = Ui.El("dsg-box").AddTo(top);
            Ui.Text(Loc.T("ui.designs.import_title"), "eyebrow").AddTo(imp);
            var irow = Ui.Row("dsg-row").AddTo(imp);
            var code = new TextField { value = _code };
            code.AddToClassList("code-field");
            code.AddToClassList("dsg-field");
            code.textEdition.placeholder = "GNG1-…";
            code.RegisterValueChangedCallback(e => _code = e.newValue);
            irow.Add(code);
            Ui.Btn(Loc.T("ui.designs.paste"), () => ClipboardUtil.Paste(t =>
            {
                // 拿不到剪貼簿（瀏覽器拒絕權限、不支援，或剪貼簿是空的）：保留已經打好的內容，請玩家自己貼
                t = (t ?? "").Trim();
                if (t.Length > 0) { _code = t; _msg = ""; }
                else Say(Loc.T("ui.designs.paste_failed"), true);
                rebuild?.Invoke();
            }), "btn-ghost").AddTo(irow);
            Ui.Btn(Loc.T("ui.designs.import"), () =>
            {
                if (DesignLibrary.Import(D, _code, out var saved, out var error))
                {
                    _code = "";
                    Say(Loc.T("ui.designs.imported", ("name", saved.name)), false);
                    Sfx.Confirm();
                }
                else
                {
                    Say(Loc.T(error), true);
                    Sfx.NoGo();
                }
                rebuild?.Invoke();
            }).AddTo(irow);

            // 訊息是當時那個語言的文字：換了語言就不顯示（不然英文畫面上會留著一句中文）
            bool showMsg = !string.IsNullOrEmpty(_msg) && _msgLang == Loc.Lang;
            var msg = Ui.Text(showMsg ? _msg : "", "small", _msgBad ? "over" : "go", "dsg-msg").AddTo(parent);
            msg.style.display = showMsg ? DisplayStyle.Flex : DisplayStyle.None;

            var wrap = Ui.Row("dsg-wrap").AddTo(parent);

            // ---- 左：存過的設計
            var listCol = Ui.El("dsg-list-col").AddTo(wrap);
            Ui.Text(Loc.T("ui.designs.list", ("n", all.Count), ("max", DesignLibrary.Max)), "eyebrow").AddTo(listCol);
            var list = new ScrollView(ScrollViewMode.Vertical);
            list.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            list.AddToClassList("dsg-list");
            listCol.Add(list);
            if (InMission) Item(list, null, rebuild);
            if (all.Count == 0) Ui.Text(Loc.T("ui.designs.empty"), "body", "dsg-empty").AddTo(list);
            foreach (var s in all.ToList()) Item(list, s, rebuild);

            // ---- 右：比較
            var cmp = Ui.El("dsg-cmp-col").AddTo(wrap);
            Ui.Text(Loc.T("ui.designs.compare"), "eyebrow").AddTo(cmp);
            Compare(cmp);
        }

        static SavedDesign SaveCurrent()
        {
            var s = M.S;
            // 評價階段才有完整的任務結果（營運到一半的成績還會變）
            var result = s.Phase == SessionPhase.Report && s.LastLaunch != null && s.LastLaunch.Reached ? M.Result : null;
            return DesignLibrary.Save(D, _name, CurrentDesign(), s.DifficultyId, result);
        }

        /// <summary>報告頁的「儲存設計」：直接存，帶著這一局的成績。</summary>
        public static SavedDesign SaveFlown(string name = "")
        {
            _name = name ?? "";
            var saved = SaveCurrent();
            _name = "";
            return saved;
        }

        static void Say(string text, bool bad)
        {
            _msg = text;
            _msgBad = bad;
            _msgLang = Loc.Lang;
        }

        // ---------------- 清單的一列

        static void Item(VisualElement list, SavedDesign s, Action rebuild)
        {
            string id = s?.id ?? Current;
            var design = s != null ? DesignLibrary.Decode(D, s) : CurrentDesign();
            bool picked = _picked.Contains(id);
            var card = Ui.El("dsg-item").Cls("dsg-item--on", picked).Cls("dsg-item--current", s == null).AddTo(list);
            var head = Ui.Row("dsg-item-head").AddTo(card);
            Ui.Text(s == null ? Loc.T("ui.designs.current") : s.name, "dsg-item-name").AddTo(head);
            Ui.Spacer().AddTo(head);
            if (s != null && s.Flown) Ui.Text(s.grade + "  " + Fmt.N(s.score, 1), "dsg-grade", "mono").Cls("dsg-grade--fail", s.grade == "FAIL").AddTo(head);
            else if (s != null && s.imported) Ui.Text(Loc.T("ui.designs.tag_imported"), "small", "accent").AddTo(head);

            if (design == null)
            {
                Ui.Text(Loc.T("err.code_unknown"), "small", "over").AddTo(card);
            }
            else
            {
                var sc = D.Scenario(design.ScenarioId);
                string where = sc != null ? Loc.T(sc.Name) : design.ScenarioId;
                Ui.Text(where + " · " + Loc.Orbit(design.OrbitId) + " · " + Loc.Launcher(design.LauncherId) + " · " + Fmt.Years(design.MissionYears), "small", "dim").AddTo(card);
                Ui.Text(string.Join(" ", design.AllPartIds().Select(p => D.Part(p)).Where(p => p != null && !string.IsNullOrEmpty(p.Code)).Select(p => p.Code)), "small", "faint", "dsg-codes").AddTo(card);
            }

            var acts = Ui.Row("dsg-acts").AddTo(card);
            var cb = Ui.Btn((picked ? "☑  " : "☐  ") + Loc.T("ui.designs.pick"), () =>
            {
                if (!_picked.Remove(id))
                {
                    if (_picked.Count >= MaxCompare) _picked.RemoveAt(0);
                    _picked.Add(id);
                }
                rebuild?.Invoke();
            }, "chip").Cls("chip--on", picked).AddTo(acts);
            cb.SetEnabled(design != null);
            if (s == null) return;

            Ui.Btn(Loc.T("ui.designs.copy"), () =>
            {
                ClipboardUtil.Copy(s.code);
                Say(Loc.T("ui.designs.copied", ("name", s.name)), false);
                rebuild?.Invoke();
            }, "chip").AddTo(acts);

            // 在沙盒打開：主選單直接開一局沙盒；已經在沙盒裡（設計階段）就放上整合台。一般任務不行（等於跳過採購）
            bool sandboxNow = InMission && M.Scenario != null && M.Scenario.Sandbox && M.S.Phase == SessionPhase.Design;
            if (design != null && (!InMission || sandboxNow))
                Ui.Btn(Loc.T(sandboxNow ? "ui.designs.load" : "ui.designs.open_sandbox"), () =>
                {
                    Sfx.Confirm();
                    if (sandboxNow)
                    {
                        GameSession.Send(CommandType.LoadDesign, s.code);
                        Say(Loc.T("ui.designs.loaded", ("name", s.name)), false);
                        rebuild?.Invoke();
                    }
                    else
                    {
                        UIRoot.BeginLoading(false);
                        GameSession.StartSandbox(s.code);
                    }
                }, "chip", "chip--go").AddTo(acts);

            Ui.Spacer().AddTo(acts);
            if (_confirmDelete == s.id)
            {
                Ui.Btn(Loc.T("ui.designs.delete_yes"), () => { DesignLibrary.Delete(s.id); _confirmDelete = null; rebuild?.Invoke(); }, "chip", "chip--danger").AddTo(acts);
                Ui.Btn(Loc.T("ui.gmenu.cancel"), () => { _confirmDelete = null; rebuild?.Invoke(); }, "chip").AddTo(acts);
            }
            else Ui.Btn("✕", () => { _confirmDelete = s.id; rebuild?.Invoke(); }, "chip", "dsg-del").AddTo(acts);
        }

        // ---------------- 比較表

        sealed class Col
        {
            public string Name;
            public Design Design;
            public Evaluation Ev;
            public ScenarioDef Scenario;
            public SavedDesign Saved;
        }

        static void Compare(VisualElement parent)
        {
            var cols = new List<Col>();
            foreach (var id in _picked)
            {
                var saved = id == Current ? null : DesignLibrary.Find(id);
                var design = id == Current ? CurrentDesign() : DesignLibrary.Decode(D, saved);
                if (design == null) continue;
                var sc = D.Scenario(design.ScenarioId);
                string diff = saved != null && !string.IsNullOrEmpty(saved.difficultyId) ? saved.difficultyId
                    : id == Current ? M.S.DifficultyId : D.DifficultyFor(sc)?.Id;
                cols.Add(new Col
                {
                    Name = id == Current ? Loc.T("ui.designs.current") : saved.name,
                    Design = design,
                    Ev = Evaluator.EvaluateDesign(design, D, diff, false),
                    Scenario = sc,
                    Saved = saved,
                });
            }
            if (cols.Count < 2)
            {
                Ui.Text(Loc.T(InMission ? "ui.designs.compare_hint_mission" : "ui.designs.compare_hint"), "body", "dsg-empty").AddTo(parent);
                if (cols.Count == 0) return;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.AddToClassList("dsg-cmp-scroll");
            parent.Add(scroll);
            var table = Ui.El("cmp-table").AddTo(scroll);

            // 標題列
            var hr = Ui.Row("cmp-row", "cmp-row--head").AddTo(table);
            Ui.Text("", "cmp-k").AddTo(hr);
            foreach (var c in cols)
            {
                var cell = Ui.El("cmp-cell").AddTo(hr);
                Ui.Text(c.Name, "cmp-name").AddTo(cell);
                Ui.Text(c.Scenario != null ? Loc.T(c.Scenario.Name) : "", "small", "dim").AddTo(cell);
            }

            foreach (ResourceId id in new[] { ResourceId.Mass, ResourceId.Power, ResourceId.Budget, ResourceId.Data, ResourceId.Comms })
            {
                var rid = id;
                // 正常的寫餘裕幾成；吃緊、超出的和儀表板一樣寫「還差多少／只剩多少」
                // （資料那一列的上限含 50% 安全係數，只寫百分比會和「已用 / 上限」對不起來）
                Row(table, Loc.T("res." + id.ToString().ToLowerInvariant()), cols,
                    c => Fmt.Resource(c.Ev[rid]),
                    c => c.Ev[rid].Status == ResourceStatus.Ok
                        ? Loc.T("ui.designs.margin", ("pct", Fmt.Pct(c.Ev[rid].Margin)))
                        : SelectView.Gap(c.Ev[rid]),
                    c => c.Ev[rid].Margin,
                    c => c.Ev[rid].Status == ResourceStatus.Over ? "over" : c.Ev[rid].Status == ResourceStatus.Tight ? "tight" : null);
            }
            Row(table, Loc.T("ui.dash.lifetime"), cols,
                c => c.Ev.Lifetime.Years >= Evaluator.Unlimited ? "∞" : Fmt.Years(c.Ev.Lifetime.Years),
                c => Loc.T("ui.dash.limit." + c.Ev.Lifetime.Limit.ToString().ToLowerInvariant()),
                c => c.Ev.Lifetime.Years,
                c => c.Ev.Lifetime.Years < c.Design.MissionYears ? "over" : null);
            Row(table, Loc.T("ui.designs.science"), cols,
                c => Fmt.N(ExpectedScience(c), 0),
                c => Loc.T("ui.dash.instruments", ("n", c.Design.Instruments.Count)),
                c => ExpectedScience(c),
                c => null);
            Row(table, Loc.T("ui.designs.result"), cols,
                c => c.Saved != null && c.Saved.Flown ? c.Saved.grade + "  " + Fmt.N(c.Saved.score, 1) : "—",
                c => c.Saved != null && c.Saved.Flown ? Fmt.Years(c.Saved.years) : Loc.T("ui.designs.not_flown"),
                c => c.Saved != null && c.Saved.Flown ? c.Saved.score : double.NaN,
                c => c.Saved != null && c.Saved.Flown && c.Saved.grade == "FAIL" ? "over" : null);
            Ui.Text(Loc.T("ui.designs.best_note"), "small", "faint", "dsg-note").AddTo(scroll);
        }

        /// <summary>發射前的科學預估（0–100）：和儀表板、處室需求用同一個算法。</summary>
        static double ExpectedScience(Col c)
        {
            if (c.Scenario != null && c.Scenario.FireAlert) return Scoring.ExpectedFireScience(c.Ev, c.Design);
            double expected = c.Ev.SciencePerYear * Math.Min(c.Design.MissionYears, c.Ev.Lifetime.Years) * c.Ev.Data.ReturnFraction;
            return Scoring.ScienceScore(expected, c.Scenario, c.Design.MissionYears);
        }

        static void Row(VisualElement table, string label, List<Col> cols, Func<Col, string> value, Func<Col, string> sub,
            Func<Col, double> rank, Func<Col, string> cls)
        {
            var row = Ui.Row("cmp-row").AddTo(table);
            Ui.Text(label, "cmp-k").AddTo(row);
            // 這一列最好的那一格（數字最大）；全部一樣、或只有一份時不標
            var ranks = cols.Select(rank).ToList();
            var valid = ranks.Where(v => !double.IsNaN(v)).ToList();
            double best = valid.Count > 0 ? valid.Max() : double.NaN;
            bool mark = cols.Count > 1 && valid.Count > 1 && valid.Max() - valid.Min() > 1e-9;
            for (int i = 0; i < cols.Count; i++)
            {
                var cell = Ui.El("cmp-cell").AddTo(row);
                string k = cls(cols[i]);
                // 吃緊（黃）和超出（紅）的格子不給星號：「最好」至少要是沒問題的
                bool isBest = mark && k == null && !double.IsNaN(ranks[i]) && Math.Abs(ranks[i] - best) <= 1e-9;
                cell.EnableInClassList("cmp-cell--best", isBest);
                if (k != null) cell.AddToClassList("cmp-cell--" + k);
                Ui.Text((isBest ? "★ " : "") + value(cols[i]), "cmp-v", "mono").AddTo(cell);
                Ui.Text(sub(cols[i]), "small", "dim").AddTo(cell);
            }
        }
    }

    /// <summary>設施裡按 L 打開的設計圖庫。</summary>
    public sealed class DesignsPanel : FacPanel
    {
        public override string Title => Loc.T("ui.designs.title");
        public override string WideClass => "modal--manual";
        // 在輸入框打字時 E 是字母，不是「關閉面板」
        public override bool CloseWithE => false;
        public DesignsPanel() => DesignsView.Open();
        protected override void Build(VisualElement body) => DesignsView.Build(body, Refresh);
    }
}
