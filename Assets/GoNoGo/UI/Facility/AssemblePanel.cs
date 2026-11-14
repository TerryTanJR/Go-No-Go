using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 組裝工作台：買來的零件是一箱小零件，在這裡組成零件才能裝上船。
    /// 玩法是「看功能、挑零件」：左邊是組裝順序，每一步只寫這個小零件要做什麼事；右邊的盤子裡是打散的小零件
    /// （混了兩個不屬於這個零件的），點出對的那一個。點錯不會失敗，會告訴你那個小零件其實是做什麼的。
    /// 組一次，就知道一顆電池、一台儀器裡面有什麼——不用讀說明文字。
    /// 組好之後這個零件的知識卡收進圖鑑（<see cref="Codex"/>）。
    /// </summary>
    public sealed class AssemblePanel : FacPanel
    {
        public override string Title => Loc.T("ui.asm.title");
        public override string WideClass => "modal--manual";

        string _part;                       // 正在組的零件
        int _step;                          // 組到第幾個小零件
        int _mistakes;
        bool _done;
        bool _newCard;
        string _feedback = "";
        bool _feedbackBad;
        List<AssemblyComponent> _tray;      // 盤子裡的小零件（打散、含混進來的）
        readonly HashSet<string> _used = new HashSet<string>();
        VisualElement _preview;

        protected override void Build(VisualElement body)
        {
            var s = M.S;
            string held = s.HeldBy(GameSession.LocalPlayer);
            if (_done) { Finished(body); return; }

            if (held == null) { Empty(body, "ui.asm.empty", true); return; }
            if (!SessionRules.NeedsAssembly(s, D, held))
            {
                Empty(body, s.Scenario(D)?.Sandbox == true ? "ui.asm.sandbox" : "ui.asm.already", false);
                return;
            }
            string why = SessionRules.CanAssemble(s, D, GameSession.LocalPlayer);
            if (why != SessionRules.Ok) { Empty(body, why, false); return; }

            var def = D.Part(held);
            var recipe = D.Recipe(def);
            if (_part != held)
            {
                // 換了一箱：重新開始
                _part = held;
                _step = 0;
                _mistakes = 0;
                _feedback = "";
                _used.Clear();
                _tray = Shuffle(recipe.Components.Concat(D.Distractors(def, 2)).ToList(), held);
            }

            // ---- 上方：這一箱是什麼
            var head = Ui.Row("asm-head").AddTo(body);
            Ui.Text(def.Code, "man-code", "mono").AddTo(head);
            Ui.Text(Loc.T(def.Name), "h2", "asm-name").AddTo(head);
            Ui.Spacer().AddTo(head);
            Ui.Text(Loc.T("ui.asm.progress", ("n", _step), ("total", recipe.Components.Count)), "h3", "accent").AddTo(head);
            Ui.Text(Loc.T("ui.asm.how"), "body", "asm-how").AddTo(body);

            var wrap = Ui.Row("asm-wrap").AddTo(body);

            // ---- 左：3D 預覽（組得越多越清楚）
            _preview = Ui.El("proc-preview", "asm-preview").AddTo(wrap);
            var tex = PartPreview.Show?.Invoke(held);
            if (tex != null) _preview.style.backgroundImage = new StyleBackground(Background.FromRenderTexture((RenderTexture)tex));
            _preview.style.opacity = 0.3f + 0.7f * _step / Mathf.Max(1, recipe.Components.Count);

            // ---- 中：組裝順序（每一步寫的是「這個小零件要做什麼」）
            var steps = Ui.El("asm-steps").AddTo(wrap);
            Ui.Text(Loc.T("ui.asm.steps"), "eyebrow").AddTo(steps);
            for (int i = 0; i < recipe.Components.Count; i++)
            {
                var c = recipe.Components[i];
                bool filled = i < _step, now = i == _step;
                var row = Ui.Row("asm-step").Cls("asm-step--done", filled).Cls("asm-step--now", now).AddTo(steps);
                Ui.Text(filled ? "✔" : (i + 1).ToString(), "asm-step-n", "mono").AddTo(row);
                var col = Ui.El("asm-step-col").AddTo(row);
                Ui.Text(filled ? Loc.T(c.Name) : now ? Loc.T("ui.asm.need") : "· · ·", "asm-step-name").AddTo(col);
                Ui.Text(Loc.T(c.Role), "asm-step-role").AddTo(col);
            }
            var fb = Ui.Text(_feedback, "asm-feedback").Cls("asm-feedback--bad", _feedbackBad).AddTo(steps);
            fb.style.display = string.IsNullOrEmpty(_feedback) ? DisplayStyle.None : DisplayStyle.Flex;

            // ---- 右：零件盤
            var tray = Ui.El("asm-tray").AddTo(wrap);
            Ui.Text(Loc.T("ui.asm.tray"), "eyebrow").AddTo(tray);
            var grid = Ui.Row("asm-grid").AddTo(tray);
            foreach (var c in _tray)
            {
                var comp = c;
                bool used = _used.Contains(c.Id);
                var b = Ui.Btn(Loc.T(c.Name), () => Pick(comp, recipe), "asm-piece").Cls("asm-piece--used", used).AddTo(grid);
                b.SetEnabled(!used);
            }
        }

        void Pick(AssemblyComponent c, AssemblyRecipe recipe)
        {
            if (_done || _step >= recipe.Components.Count) return;
            var want = recipe.Components[_step];
            if (c.Id == want.Id)
            {
                _used.Add(c.Id);
                _step++;
                _feedbackBad = false;
                _feedback = Loc.T("ui.asm.right", ("name", Loc.T(c.Name)));
                Sfx.Install();
                if (_step >= recipe.Components.Count) { Complete(); return; }
            }
            else
            {
                _mistakes++;
                _feedbackBad = true;
                // 點錯也學得到東西：告訴玩家這個小零件其實是做什麼的
                _feedback = Loc.T("ui.asm.wrong", ("name", Loc.T(c.Name)), ("role", Loc.T(c.Role)));
                Sfx.NoGo();
            }
            Refresh();
        }

        void Complete()
        {
            _done = true;
            _newCard = Codex.Unlock(D, _part, true);
            Sfx.Confirm();
            GameSession.Send(CommandType.Assemble, _part);
            Hud.Signal("assemble");
            // 送出操作後狀態改變會重畫（個人模式是同一幀）；多人要等房主回來，先畫一次
            Refresh();
        }

        // ---------------- 完成畫面：知識卡

        void Finished(VisualElement body)
        {
            var def = D.Part(_part);
            if (def == null) { Empty(body, "ui.asm.empty", true); return; }
            var wrap = Ui.Row("asm-wrap", "asm-wrap--done").AddTo(body);
            _preview = Ui.El("proc-preview", "asm-preview").AddTo(wrap);
            var tex = PartPreview.Show?.Invoke(_part);
            if (tex != null) _preview.style.backgroundImage = new StyleBackground(Background.FromRenderTexture((RenderTexture)tex));

            var col = Ui.El("asm-done").AddTo(wrap);
            Ui.Text(Loc.T(_mistakes == 0 ? "ui.asm.done_perfect" : "ui.asm.done"), "h2", "go").AddTo(col);
            var top = Ui.Row("asm-head").AddTo(col);
            Ui.Text(def.Code, "man-code", "mono").AddTo(top);
            Ui.Text(Loc.T(def.Name), "h3", "asm-name").AddTo(top);
            if (_mistakes > 0) Ui.Text(Loc.T("ui.asm.mistakes", ("n", _mistakes)), "small", "dim").AddTo(col);

            // 知識卡：組好才看得到的獎勵（想看再看，不看也不影響）
            var card = Ui.El("codex-card", "codex-card--big").AddTo(col);
            Ui.Text(Loc.T(_newCard ? "ui.codex.new" : "ui.codex.card"), "eyebrow").AddTo(card);
            Ui.Text(Loc.T(def.Function), "codex-fact").AddTo(card);
            Ui.Text(Ui.NoOrphan(Loc.T(def.Explainer)), "body", "codex-text").AddTo(card);
            Ui.Text(Loc.T("ui.codex.count", ("n", Codex.Count(D)), ("total", Codex.Total(D))), "small", "faint").AddTo(card);

            var row = Ui.Row("modal-actions").AddTo(col);
            Ui.Spacer().AddTo(row);
            Ui.Btn(Loc.T("ui.asm.go_install"), () => Close?.Invoke(), "btn-go", "btn-big").AddTo(row);
        }

        // ---------------- 手上沒有箱子（或不用組）

        void Empty(VisualElement body, string key, bool listKits)
        {
            var col = Ui.El("asm-empty").AddTo(body);
            Ui.Text(Loc.T(key), "h3").AddTo(col);
            if (listKits)
            {
                var s = M.S;
                var waiting = s.Owned.Where(id => SessionRules.NeedsAssembly(s, D, id)).ToList();
                if (waiting.Count > 0)
                {
                    Ui.Text(Loc.T("ui.asm.waiting"), "small", "dim", "asm-waiting").AddTo(col);
                    var chips = Ui.Row("asm-chips").AddTo(col);
                    foreach (var id in waiting) Ui.Text(D.Part(id)?.Code ?? id, "man-code", "mono", "asm-chip").AddTo(chips);
                }
                else Ui.Text(Loc.T("ui.asm.none"), "small", "dim", "asm-waiting").AddTo(col);
            }
            var row = Ui.Row("modal-actions").AddTo(col);
            Ui.Spacer().AddTo(row);
            Ui.Btn(Loc.T("ui.fac.close"), () => Close?.Invoke(), "btn-primary").AddTo(row);
        }

        /// <summary>打散順序：用零件 id 當種子（同一個零件每次的排法一樣，不會靠記位置）。</summary>
        static List<AssemblyComponent> Shuffle(List<AssemblyComponent> list, string seed)
        {
            int h = 17;
            foreach (char ch in seed) h = unchecked(h * 31 + ch);
            var rnd = new System.Random(h);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        public override void Tick(float dt) => _preview?.MarkDirtyRepaint();

        public override void Detach()
        {
            PartPreview.Hide?.Invoke();
            _preview = null;
            base.Detach();
        }
    }
}
