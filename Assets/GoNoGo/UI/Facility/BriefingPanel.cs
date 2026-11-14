using System;
using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    // ======================================================================== 任務簡報
    //
    // 控制中心主任透過加密視訊交代任務：一句一句打字出來，左邊是視訊畫面（剪影、聲波）。
    // 只講故事和目標，不講限制條件（預算、年限、發射期限、儀器數量……）——那些要玩家自己打電話問各處室。
    // 最後一句一定是提醒：細節去問。台詞在 strings.json 的 ui.brief.speech.<情境 id>（一行一句）。

    public sealed class BriefingPanel : FacPanel
    {
        public override string Title => Loc.T("ui.brief.title");
        public override bool Closable => false;
        public override string WideClass => "modal--brief";

        List<string> _lines;
        int _shown;            // 已經講完的句數
        float _typed;          // 目前這句打到第幾個字
        float _wave;
        bool _done;

        Label _current;
        VisualElement _log, _bars, _dots, _foot, _live;
        Button _next, _skip;

        protected override void Build(VisualElement body)
        {
            _lines ??= Speech();
            var sc = M.Scenario;

            var wrap = Ui.Row("brief-wrap").AddTo(body);

            // ---- 左：視訊畫面
            var feed = Ui.El("brief-feed").AddTo(wrap);
            var bar = Ui.Row("brief-feed-bar").AddTo(feed);
            _live = Ui.El("brief-live").AddTo(bar);
            Ui.Text(Loc.T("ui.brief.live"), "brief-live-text", "mono").AddTo(bar);
            Ui.Spacer().AddTo(bar);
            Ui.Text(Loc.T("ui.brief.channel"), "brief-channel", "mono").AddTo(bar);

            var screen = Ui.El("brief-screen").AddTo(feed);
            for (int i = 0; i < 14; i++) Ui.El("brief-scan").AddTo(screen);
            // 主任的肖像（Resources/UI/Portraits/director.png，去背）；找不到圖時用 UI 元素組成的剪影
            var portrait = Resources.Load<Texture2D>("UI/Portraits/director");
            if (portrait != null)
            {
                var pic = Ui.El("brief-portrait").AddTo(screen);
                pic.style.backgroundImage = new StyleBackground(portrait);
            }
            else
            {
                var who = Ui.El("brief-figure").AddTo(screen);
                Ui.El("brief-head").AddTo(who);
                var body2 = Ui.El("brief-body").AddTo(who);
                Ui.El("brief-collar").AddTo(body2);
                Ui.El("brief-badge").AddTo(body2);
                Ui.El("brief-headset").AddTo(who);
            }
            Ui.Text("◉ REC", "brief-rec", "mono").AddTo(screen);

            var id = Ui.El("brief-id").AddTo(feed);
            Ui.Text(Loc.T("ui.brief.speaker.name"), "brief-name").AddTo(id);
            Ui.Text(Loc.T("ui.brief.speaker.title"), "small", "dim").AddTo(id);
            _bars = Ui.Row("brief-wave").AddTo(feed);
            for (int i = 0; i < 24; i++) Ui.El("brief-wave-bar").AddTo(_bars);

            // ---- 右：台詞
            var talk = Ui.El("brief-talk").AddTo(wrap);
            var head = Ui.Row("brief-talk-head").AddTo(talk);
            var ht = Ui.El().AddTo(head);
            Ui.Text(M.IsHistorical ? Loc.T("ui.brief.history", ("year", sc.Historical.Year)) : Loc.T("ui.brief.op"), "eyebrow").AddTo(ht);
            Ui.Text(Loc.T(sc.Name), "h2", "brief-mission").AddTo(ht);
            Ui.Spacer().AddTo(head);
            if (!M.IsHistorical) Ui.Text(Loc.T("ui.brief.difficulty") + " · " + Loc.T(M.Difficulty.Name), "brief-tag").AddTo(head);

            _log = Ui.El("brief-log").AddTo(talk);
            var box = Ui.El("brief-line").AddTo(talk);
            box.RegisterCallback<ClickEvent>(_ => Advance());
            Ui.Text(Loc.T("ui.brief.speaker.short"), "brief-who").AddTo(box);
            _current = Ui.Text("", "brief-say").AddTo(box);

            var ctl = Ui.Row("brief-ctl").AddTo(talk);
            _dots = Ui.Row("brief-dots").AddTo(ctl);
            Ui.Spacer().AddTo(ctl);
            _skip = Ui.Btn(Loc.T("ui.brief.skip"), SkipAll, "btn-ghost").AddTo(ctl);
            _next = Ui.Btn(Loc.T("ui.brief.next"), Advance, "btn-primary").AddTo(ctl);

            // 講完才出現：接下來做什麼 + 開始
            _foot = Ui.El("brief-foot").AddTo(talk);
            var todo = Ui.Row("brief-todo").AddTo(_foot);
            // 歷史關卡：簡報完直接照當年的設計發射，第一步不是打電話
            if (!M.IsHistorical) Ui.Text("☎", "brief-todo-icon").AddTo(todo);
            var tt = Ui.El("brief-todo-text").AddTo(todo);
            Ui.Text(Loc.T("ui.brief.todo_title"), "eyebrow").AddTo(tt);
            Ui.Text(Loc.T(M.IsHistorical ? "ui.brief.todo_history" : M.S.Multiplayer ? "ui.brief.todo_multi" : "ui.brief.todo_solo"), "body").AddTo(tt);
            if (M.IsHistorical)
            {
                var dos = Ui.El("brief-dossier").AddTo(_foot);
                Ui.Text(Loc.T("ui.brief.dossier"), "eyebrow").AddTo(dos);
                Ui.Text(Loc.T(sc.Historical.Background), "body").AddTo(dos);
            }
            var row = Ui.Row("modal-actions").AddTo(_foot);
            Ui.Spacer().AddTo(row);
            Ui.Btn(Loc.T(M.IsHistorical ? "ui.brief.launch_history" : "ui.brief.start"), () =>
            {
                Sfx.Confirm();
                GameSession.Send(CommandType.BeginDesign);
            }, "btn-primary", "btn-big").AddTo(row);

            Redraw();
        }

        /// <summary>這個情境的台詞（沒有專屬台詞就用通用的），最後接上「細節去問」。</summary>
        List<string> Speech()
        {
            var strings = D.Strings;
            string key = "ui.brief.speech." + M.S.ScenarioId;
            if (!strings.ContainsKey(key)) key = "ui.brief.speech.default";
            var list = new List<string>();
            foreach (var l in Loc.T(key).Split('\n'))
                if (!string.IsNullOrWhiteSpace(l)) list.Add(l.Trim());
            list.Add(Loc.T(M.IsHistorical ? "ui.brief.close_history" : M.S.Multiplayer ? "ui.brief.close_multi" : "ui.brief.close_solo"));
            return list;
        }

        void Advance()
        {
            if (_done) return;
            string line = _lines[_shown];
            if (_typed < line.Length) { _typed = line.Length; Redraw(); return; }   // 先把這句打完
            _shown++;
            _typed = 0;
            Sfx.Tick();
            if (_shown >= _lines.Count) { _done = true; Sfx.Confirm(); }
            Redraw();
        }

        void SkipAll()
        {
            _shown = _lines.Count;
            _done = true;
            Redraw();
        }

        void Redraw()
        {
            if (_log == null) return;
            _log.Clear();
            int upto = Math.Min(_shown, _lines.Count);
            for (int i = 0; i < upto; i++)
                Ui.Text(_lines[i], "brief-past").AddTo(_log);
            _current.parent.style.display = _done ? DisplayStyle.None : DisplayStyle.Flex;
            if (!_done) _current.text = _lines[_shown].Substring(0, (int)Math.Min(_lines[_shown].Length, _typed));
            _dots.Clear();
            for (int i = 0; i < _lines.Count; i++)
                Ui.El("brief-dot").Cls("brief-dot--done", i < _shown).Cls("brief-dot--now", i == _shown).AddTo(_dots);
            _next.style.display = _done ? DisplayStyle.None : DisplayStyle.Flex;
            _skip.style.display = _done ? DisplayStyle.None : DisplayStyle.Flex;
            _next.text = !_done && _shown == _lines.Count - 1 ? Loc.T("ui.brief.got_it") : Loc.T("ui.brief.next");
            _foot.style.display = _done ? DisplayStyle.Flex : DisplayStyle.None;
            if (_done) _log.AddToClassList("brief-log--all"); else _log.RemoveFromClassList("brief-log--all");
            _live.EnableInClassList("brief-live--off", _done);
        }

        public override void Tick(float dt)
        {
            if (_current == null || _done) return;
            string line = _lines[_shown];
            bool talking = _typed < line.Length;
            if (talking)
            {
                int before = (int)_typed;
                _typed += dt * (Loc.IsZh ? 22f : 48f);
                if ((int)_typed != before) _current.text = line.Substring(0, (int)Math.Min(line.Length, _typed));
            }
            // 聲波：講話時跳動，停下來就平
            _wave += dt;
            int k = 0;
            foreach (var b in _bars.Children())
            {
                float h = talking ? 0.25f + 0.75f * (float)Math.Abs(Math.Sin(_wave * (7 + k % 5) + k * 1.7) * Math.Sin(_wave * 3.1 + k)) : 0.08f;
                b.style.height = Length.Percent(h * 100);
                k++;
            }
        }
    }
}
