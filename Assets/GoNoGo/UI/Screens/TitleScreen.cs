using System;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>標題畫面：選任務、難度、語言、地面訓練，然後開始。背景是 Boot 場景的地球。</summary>
    public sealed class TitleScreen : GameScreen
    {
        static string _scenarioId;
        static string _difficultyId;
        static bool _training;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            var d = S.Data;
            _scenarioId ??= d.Scenarios[0].Id;
            _difficultyId ??= d.Difficulties[0].Id;

            var top = Ui.El("topbar", "topbar--clear").AddTo(Root);
            Ui.Spacer().AddTo(top);
            Ui.Btn(Loc.IsZh ? "EN" : "中文", Loc.Toggle, "btn-small", "btn-ghost").AddTo(top);

            var col = Ui.El("title-col", "fade-in").AddTo(Root);
            Ui.AnimateIn(col, "fade-in--on", 80);
            Ui.Text(Loc.T("ui.title.eyebrow"), "eyebrow").AddTo(col);
            Ui.Text("GO / NO-GO", "h1", "title-logo").AddTo(col);
            Ui.Text(Loc.T("ui.title.tagline"), "body", "title-tagline").AddTo(col);

            Ui.Text(Loc.T("ui.title.choose_mission"), "section-title").AddTo(col);
            var cards = Ui.Row("scn-row").AddTo(col);
            foreach (var s in d.Scenarios)
            {
                var sc = s;
                var card = Ui.El("scn-card").AddTo(cards);
                card.EnableInClassList("scn-card--on", sc.Id == _scenarioId);
                if (sc.Historical != null) Ui.Text(Loc.T("ui.title.history_badge") + " · " + sc.Historical.Year, "scn-badge").AddTo(card);
                else Ui.Text(Loc.T("ui.objective." + sc.Objective), "scn-badge", "scn-badge--plain").AddTo(card);
                Ui.Text(Loc.T(sc.Name), "h3", "scn-name").AddTo(card);
                Ui.Text(Summary(Loc.T(sc.Briefing)), "small", "dim", "scn-desc").AddTo(card);
                var stats = Ui.Row("scn-stats").AddTo(card);
                Ui.Text(Fmt.Money(sc.BudgetMusd * (sc.Historical != null ? sc.Historical.BudgetCap : 1)), "mono", "small").AddTo(stats);
                Ui.Text("·", "faint", "small", "scn-dot").AddTo(stats);
                Ui.Text(Fmt.Years(sc.TargetYears), "mono", "small").AddTo(stats);
                card.RegisterCallback<ClickEvent>(_ => { _scenarioId = sc.Id; Sfx.Click(); Rebuild(); });
                card.RegisterCallback<PointerEnterEvent>(_ => Sfx.Hover());
            }

            Ui.Text(Loc.T("ui.title.difficulty"), "section-title").AddTo(col);
            var diffs = Ui.Row().AddTo(col);
            foreach (var df in d.Difficulties)
            {
                var id = df.Id;
                Ui.Btn(Loc.T(df.Name), () => { _difficultyId = id; Rebuild(); }, "chip")
                    .Cls("chip--on", id == _difficultyId).AddTo(diffs);
            }
            Ui.Text(Loc.T("ui.difficulty." + _difficultyId), "small", "dim", "diff-desc").AddTo(col);

            var trainRow = Ui.Row("train-row").AddTo(col);
            Ui.Btn(Loc.T("ui.title.training"), () => { _training = !_training; Rebuild(); }, "chip")
                .Cls("chip--on", _training).AddTo(trainRow);
            Ui.Text(Loc.T("ui.title.training_desc"), "small", "faint").AddTo(trainRow);

            var startRow = Ui.Row("start-row").AddTo(col);
            Ui.Btn(Loc.T("ui.title.start"), Start, "btn-primary", "btn-big").AddTo(startRow);

            var foot = Ui.El("title-foot").AddTo(Root);
            Ui.Text(Loc.T("ui.title.credits"), "small", "faint").AddTo(foot);
        }

        static string Summary(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            int cut = text.IndexOfAny(new[] { '.', '。' });
            string s = cut > 0 ? text.Substring(0, cut + 1) : text;
            return s.Length > 140 ? s.Substring(0, 137) + "…" : s;
        }

        void Start()
        {
            Sfx.Confirm();
            S.TutorialMode = _training;
            DesignScreen.ResetTraining();
            S.StartScenario(_scenarioId, _difficultyId, Environment.TickCount & 0x7fffffff);
            SceneFlow.Go(SceneFlow.MissionControl);
        }
    }
}
