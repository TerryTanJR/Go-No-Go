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
    // 只講故事和目標；限制條件（預算、年限、儀器數量……）和任務需求都直接列在中控室的設計主螢幕上。
    // 一般關卡最後一句是提醒：零件的數值有一部分是空的，要在兩個房間找線索，自己填回設計主螢幕（歷史關卡、沙盒另有結尾）。
    // 台詞在 strings.json 的 ui.brief.speech.<情境 id>（一行一句）。

    public sealed class BriefingPanel : FacPanel
    {
        public override string Title => Loc.T("ui.brief.title");
        // 單人的開場簡報一定要看完（按「開始任務」才進設計）；之後（多人、或回頭複習）隨時可以關
        public override bool Closable => M.S.Phase != SessionPhase.Briefing;
        public override string WideClass => "modal--brief";

        /// <summary>這個本機玩家這一局看過簡報了（之後再打開就直接顯示全文，不重新打字）。</summary>
        static object _seenFor;

        List<string> _lines;
        List<string> _voices;  // 和 _lines 一一對應的語音檔名（沒有錄音的句子是 null）
        int _voiceLine = -1;   // 已經開始播語音的那一句（重建畫面時不要重播）
        bool _init;
        int _shown;            // 已經講完的句數
        float _typed;          // 目前這句打到第幾個字
        float _wave;
        bool _done;

        Label _current;
        VisualElement _log, _bars, _dots, _foot, _live;
        Button _next, _skip;

        /// <summary>
        /// 版面只跟關卡、難度、是不是還在開場簡報、單人或多人有關。簡報開著的時候別人做了別的事（狀態變了）不用整個重建：
        /// 講到哪一句、打到第幾個字都記在欄位裡，重建出來的也是同一個畫面。
        /// </summary>
        string _built;

        public override void Refresh()
        {
            var s = M.S;
            string sig = s.ScenarioId + "/" + s.DifficultyId + "/" + (s.Phase == SessionPhase.Briefing) + "/" + s.Multiplayer;
            if (sig == _built && Body != null && Body.childCount > 0) return;
            _built = sig;
            base.Refresh();
        }

        /// <summary>切語言：台詞換成新語言的，講到第幾句留著（這一句從頭打）。</summary>
        protected override void OnReattach()
        {
            _lines = Speech();
            _typed = 0;
            if (_shown >= _lines.Count) { _shown = _lines.Count; _done = true; }
            base.OnReattach();
        }

        protected override void Build(VisualElement body)
        {
            _lines ??= Speech();
            var sc = M.Scenario;
            if (!_init)
            {
                _init = true;
                bool review = ReferenceEquals(_seenFor, M) && M.S.Phase != SessionPhase.Briefing;
                _seenFor = M;
                if (review) { _shown = _lines.Count; _done = true; }
            }

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
            // 歷史關卡：簡報完直接照當年的設計發射，第一步不是找數值
            if (!M.IsHistorical && !M.Scenario.Sandbox) Ui.Text("？", "brief-todo-icon").AddTo(todo);
            var tt = Ui.El("brief-todo-text").AddTo(todo);
            // 不分組：所有人都從中控室開始，分工自己決定。多人多一句「分頭找，用講的」
            bool multi = M.S.Multiplayer && !M.IsHistorical;
            Ui.Text(Loc.T("ui.brief.todo_title"), "eyebrow").AddTo(tt);
            Ui.Text(Ui.NoOrphan(Loc.T(M.IsHistorical ? "ui.brief.todo_history" : M.Scenario.Sandbox ? "ui.brief.todo_sandbox"
                : multi ? "ui.brief.todo_team" : "ui.brief.todo_solo")), "body").AddTo(tt);
            if (M.IsHistorical)
            {
                var dos = Ui.El("brief-dossier").AddTo(_foot);
                Ui.Text(Loc.T("ui.brief.dossier"), "eyebrow").AddTo(dos);
                Ui.Text(Ui.NoOrphan(Loc.T(sc.Historical.Background)), "body").AddTo(dos);
            }
            // 之後想再看：隨時按 B（很多人以為簡報只出現一次，看過就沒了）
            Ui.Text(Loc.T("ui.brief.again"), "brief-again").AddTo(_foot);
            var row = Ui.Row("modal-actions").AddTo(_foot);
            // 回頭複習時：可以從頭再播一次
            if (M.S.Phase != SessionPhase.Briefing)
                Ui.Btn(Loc.T("ui.brief.replay"), () => { _shown = 0; _typed = 0; _done = false; _voiceLine = -1; Sfx.Click(); Redraw(); PlayVoice(); }, "btn-ghost").AddTo(row);
            Ui.Spacer().AddTo(row);
            if (M.S.Phase == SessionPhase.Briefing)
                Ui.Btn(Loc.T(M.IsHistorical ? "ui.brief.launch_history" : "ui.brief.start"), () =>
                {
                    Sfx.Confirm();
                    GameSession.Send(CommandType.BeginDesign);
                }, "btn-primary", "btn-big").AddTo(row);
            else
            {
                // 多人 / 複習：只關掉自己的簡報，不影響別人
                Ui.Btn(Loc.T("ui.brief.back_to_work"), () => { Sfx.Confirm(); Close?.Invoke(); }, "btn-primary", "btn-big").AddTo(row);
            }

            Redraw();
            PlayVoice();
        }

        /// <summary>過場畫面還蓋著（載入場景、連線）：簡報先不開口、不打字，等過場收起來。</summary>
        static bool Waiting => Hud.Covered || SceneFlow.Loading;

        public override void Detach() { Sfx.StopVoice(); base.Detach(); }

        /// <summary>目前這一句有錄音就播（每一句只播一次，按「下一句」才換下一句；切語言用同一份錄音）。</summary>
        void PlayVoice()
        {
            if (_done || _voices == null || _shown >= _voices.Count || _voiceLine == _shown || Waiting) return;
            _voiceLine = _shown;
            if (_voices[_shown] != null) Sfx.Voice(_voices[_shown]); else Sfx.StopVoice();
        }

        /// <summary>這個情境的台詞（沒有專屬台詞就用通用的），最後接上「數值要自己找」。</summary>
        List<string> Speech()
        {
            var strings = D.Strings;
            string key = "ui.brief.speech." + M.S.ScenarioId;
            if (!strings.ContainsKey(key)) key = "ui.brief.speech.default";
            var list = new List<string>();
            _voices = new List<string>();
            foreach (var l in Loc.T(key).Split('\n'))
                if (!string.IsNullOrWhiteSpace(l)) { list.Add(Ui.NoOrphan(l.Trim())); _voices.Add(null); }
            // 錄音：開場白（所有一般關卡共用）、地球觀測的兩句任務說明、單人的結尾提醒。中英文用同一份錄音
            bool story = !M.IsHistorical && !M.Scenario.Sandbox;
            if (story && _voices.Count > 0) _voices[0] = "brief_open";
            if (M.S.ScenarioId == "earth_observation" && _voices.Count >= 3) { _voices[1] = "brief_eo_1"; _voices[2] = "brief_eo_2"; }
            // 結尾那句：單人、多人（分頭找線索）不同；歷史關卡是倒數開始
            string close = "ui.brief.close_solo";
            if (M.IsHistorical) close = "ui.brief.close_history";
            else if (M.Scenario.Sandbox) close = "ui.brief.close_sandbox";
            else if (M.S.Multiplayer) close = "ui.brief.close_team";
            list.Add(Ui.NoOrphan(Loc.T(close)));
            _voices.Add(close == "ui.brief.close_solo" ? "brief_close_solo" : null);
            return list;
        }

        void Advance()
        {
            if (_done) return;
            string line = _lines[_shown];
            int len = Ui.VisibleLength(line);
            if (_typed < len) { _typed = len; Redraw(); return; }   // 先把這句打完
            _shown++;
            _typed = 0;
            Sfx.Tick();
            if (_shown >= _lines.Count) { _done = true; Sfx.StopVoice(); Sfx.Confirm(); }
            Redraw();
            PlayVoice();
        }

        void SkipAll()
        {
            _shown = _lines.Count;
            _done = true;
            Sfx.StopVoice();
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
            if (!_done) _current.text = Ui.RichPrefix(_lines[_shown], (int)_typed);
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
            if (_current == null || _done || Waiting) return;
            PlayVoice();
            string line = _lines[_shown];
            bool talking = _typed < Ui.VisibleLength(line);
            if (talking)
            {
                int before = (int)_typed;
                // 打字速度：比閱讀速度快一點（等字出來的時間也是閱讀負擔）
                _typed += dt * (Loc.IsZh ? 34f : 70f);
                if ((int)_typed != before) _current.text = Ui.RichPrefix(line, (int)_typed);
            }
            // 聲波：講話時跳動，停下來就平
            _wave += dt;
            int bars = _bars.childCount;
            for (int k = 0; k < bars; k++)
            {
                float h = talking ? 0.25f + 0.75f * (float)Math.Abs(Math.Sin(_wave * (7 + k % 5) + k * 1.7) * Math.Sin(_wave * 3.1 + k)) : 0.08f;
                _bars[k].style.height = Length.Percent(h * 100);
            }
        }
    }
}
