using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 營運：把模擬結果當成影片播放（天數往前走），遇到事件就暫停讓玩家決定。
    /// 玩家的選擇送回核心重算整個任務；事件之前的日子不受影響，所以已經播過的圖表不會跳動。
    /// </summary>
    public sealed class OperationsScreen : GameScreen
    {
        const double BaseDaysPerSecond = 30;
        static readonly int[] Speeds = { 1, 4, 16 };

        int _speedIndex;
        bool _paused;
        double _day;
        bool _ended;
        readonly HashSet<string> _handled = new HashSet<string>();
        MissionEvent _pending;

        Label _dayLabel, _yearLabel, _modeLabel, _sciLabel, _dataLabel, _dutyLabel;
        LineChart _sci, _data, _batt, _store;
        VisualElement _log, _modal, _endPanel, _speedRow;
        Button _pauseBtn;

        MissionResult R => S.Result;
        double TotalDays => R != null && R.Samples.Count > 0 ? R.Samples[R.Samples.Count - 1].Day : 0;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.Space);
            var right = TopBar(Loc.T(S.Scenario.Name));

            // 左上：任務時鐘
            var clock = Ui.El("panel", "ops-clock").AddTo(Root);
            Ui.Text(Loc.T("ui.ops.eyebrow"), "eyebrow").AddTo(clock);
            _dayLabel = Ui.Text("", "h1", "mono", "ops-day").AddTo(clock);
            _yearLabel = Ui.Text("", "small", "dim").AddTo(clock);
            _modeLabel = Ui.Text("", "ops-mode").AddTo(clock);
            _speedRow = Ui.Row("ops-speed").AddTo(clock);
            _pauseBtn = Ui.Btn("", TogglePause, "btn-small").AddTo(_speedRow);
            for (int i = 0; i < Speeds.Length; i++)
            {
                int k = i;
                Ui.Btn(Speeds[i] + "×", () => { _speedIndex = k; RefreshSpeed(); }, "chip").AddTo(_speedRow).name = "speed" + i;
            }
            RefreshSpeed();

            // 右：儀表板
            var dash = Ui.El("panel", "ops-dash").AddTo(Root);
            Ui.Text(Loc.T("ui.ops.telemetry"), "eyebrow").AddTo(dash);
            var nums = Ui.Row("ops-nums").AddTo(dash);
            _sciLabel = Num(nums, "ui.ops.science");
            _dataLabel = Num(nums, "ui.ops.data");
            _dutyLabel = Num(nums, "ui.ops.duty");
            _sci = Chart(dash, "ui.ops.chart.science", new Color(0.24f, 0.86f, 0.52f));
            _data = Chart(dash, "ui.ops.chart.data", new Color(0.22f, 0.78f, 0.96f));
            _batt = Chart(dash, "ui.ops.chart.battery", new Color(1f, 0.71f, 0.28f));
            _store = Chart(dash, "ui.ops.chart.storage", new Color(0.62f, 0.55f, 1f));
            _batt.SetWarnLine(0.2f);
            _store.SetWarnLine(0.98f);

            // 左下：事件紀錄
            var logPanel = Ui.El("panel", "ops-log").AddTo(Root);
            Ui.Text(Loc.T("ui.ops.log"), "eyebrow").AddTo(logPanel);
            var sc = new ScrollView(ScrollViewMode.Vertical).AddTo(logPanel);
            _log = sc.contentContainer;
            foreach (var o in R?.Events ?? new List<EventOutcome>())
                if (_handled.Contains(o.Event.Id)) AddLog(o, false);

            var bottom = Ui.Row("ops-bottom").AddTo(Root);
            Ui.Spacer().AddTo(bottom);
            Ui.Btn(Loc.T("ui.ops.skip"), SkipToEnd, "btn-ghost").AddTo(bottom);

            _modal = Ui.El("event-modal", "modal-hidden").AddTo(Root);
            _endPanel = Ui.El("event-modal", "modal-hidden").AddTo(Root);

            RebuildCharts();
            if (_pending != null) ShowEvent(_pending);
            if (_ended) ShowEnd();
            UpdateClock();
        }

        static Label Num(VisualElement parent, string key)
        {
            var b = Ui.El("ops-num").AddTo(parent);
            Ui.Text(Loc.T(key), "small", "dim").AddTo(b);
            return Ui.Text("—", "h3", "mono").AddTo(b);
        }

        static LineChart Chart(VisualElement parent, string key, Color c)
        {
            var card = Ui.El("chart-card").AddTo(parent);
            var head = Ui.Row("chart-head").AddTo(card);
            Ui.Text(Loc.T(key), "small", "dim").AddTo(head);
            var chart = new LineChart().AddTo(card);
            chart.SetColor(c);
            return chart;
        }

        void RebuildCharts()
        {
            if (R == null) return;
            double total = Mathf.Max(1, (float)TotalDays);
            double sciMax = Mathf.Max(1e-3f, (float)R.Samples.Max(s => s.Science)) * 1.05;
            double dataMax = Mathf.Max(1e-3f, (float)R.Samples.Max(s => s.DataReturnedGb)) * 1.05;
            _sci.SetData(R.Samples.Select(s => new Vector2((float)(s.Day / total), (float)(s.Science / sciMax))).ToList());
            _data.SetData(R.Samples.Select(s => new Vector2((float)(s.Day / total), (float)(s.DataReturnedGb / dataMax))).ToList());
            _batt.SetData(R.Samples.Select(s => new Vector2((float)(s.Day / total), (float)s.BatterySoc)).ToList());
            _store.SetData(R.Samples.Select(s => new Vector2((float)(s.Day / total), (float)s.StorageFill)).ToList());
            var marks = R.Events.Select(o => (float)(o.Event.Day / total)).ToList();
            _sci.SetMarkers(marks);
            _data.SetMarkers(marks);
            _batt.SetMarkers(marks);
            _store.SetMarkers(marks);
            OpsClock.TotalDays = total;
        }

        // ---------------- 播放 ----------------

        public override void Tick(float dt)
        {
            if (R == null) return;
            if (!_paused && _pending == null && !_ended)
            {
                double next = _day + dt * BaseDaysPerSecond * Speeds[_speedIndex];
                // 下一個還沒處理的事件
                var e = S.Plan?.Events.FirstOrDefault(x => !_handled.Contains(x.Id) && x.Day < TotalDays);
                if (e != null && next >= e.Day)
                {
                    _day = e.Day;
                    _pending = e;
                    ShowEvent(e);
                }
                else _day = next;
                if (_day >= TotalDays)
                {
                    _day = TotalDays;
                    _ended = true;
                    ShowEnd();
                }
            }
            UpdateClock();
        }

        void UpdateClock()
        {
            if (R == null) return;
            double total = Mathf.Max(1, (float)TotalDays);
            float x = (float)(_day / total);
            _sci.SetCursor(x); _data.SetCursor(x); _batt.SetCursor(x); _store.SetCursor(x);
            var s = SampleAt(_day);
            OpsClock.Day = _day;
            OpsClock.Paused = _paused || _pending != null || _ended;
            OpsClock.Current = s;
            _dayLabel.text = (Loc.IsZh ? "第 " : "DAY ") + Fmt.N(_day) + (Loc.IsZh ? " 天" : "");
            _yearLabel.text = Loc.T("ui.ops.year_of", ("y", (object)Fmt.N(_day / 365.25, 1)), ("t", Fmt.N(R.TargetYears, 0)));
            if (s != null)
            {
                _sciLabel.text = Fmt.N(Scoring.ScienceScore(s.Science, S.Scenario));
                _dataLabel.text = s.DataReturnedGb >= 1000 ? Fmt.N(s.DataReturnedGb / 1000, 1) + " TB" : Fmt.N(s.DataReturnedGb) + " GB";
                _dutyLabel.text = Fmt.Pct(s.Duty);
                // 事件視窗開著時還沒做決定：顯示事件前一天的狀態，不要先顯示預設選擇的結果（例如還沒選就顯示「安全模式」）
                var m = _pending != null ? SampleAt(System.Math.Max(0, _pending.Day - 1)) ?? s : s;
                bool safe = m.SafeMode;
                _modeLabel.text = Loc.T(safe ? "ui.ops.mode.safe" : m.Duty < 0.999 ? "ui.ops.mode.limited" : "ui.ops.mode.nominal");
                _modeLabel.EnableInClassList("tight", safe || m.Duty < 0.999);
                _modeLabel.EnableInClassList("go", !safe && m.Duty >= 0.999);
            }
        }

        Sample SampleAt(double day)
        {
            var list = R.Samples;
            if (list.Count == 0) return null;
            Sample best = list[0];
            foreach (var s in list)
            {
                if (s.Day > day) break;
                best = s;
            }
            return best;
        }

        void TogglePause()
        {
            _paused = !_paused;
            RefreshSpeed();
        }

        void RefreshSpeed()
        {
            _pauseBtn.text = Loc.T(_paused ? "ui.ops.play" : "ui.ops.pause");
            for (int i = 0; i < Speeds.Length; i++)
                _speedRow.Q<Button>("speed" + i)?.EnableInClassList("chip--on", i == _speedIndex && !_paused);
        }

        // ---------------- 事件 ----------------

        void ShowEvent(MissionEvent e)
        {
            Sfx.Alert();
            OpsClock.Trigger(e);
            _modal.Clear();
            _modal.RemoveFromClassList("modal-hidden");
            var card = Ui.El("panel", "event-card", "fade-in").AddTo(_modal);
            Ui.AnimateIn(card, "fade-in--on");
            string kind = e.Kind.ToString().ToLowerInvariant();
            Ui.Text(Loc.T("ui.ops.day_n", ("d", (object)e.Day)), "eyebrow").AddTo(card);
            Ui.Text(Loc.T("event.title." + kind), "h2", "event-title").AddTo(card);
            Ui.Text(Prompt(e), "body", "event-prompt").AddTo(card);
            if (e.Kind == EventKind.SolarStorm)
            {
                var sw = S.Data.SpaceWeather.FirstOrDefault(x => x.Id == e.RefId);
                if (sw != null && !string.IsNullOrEmpty(Loc.T(sw.Summary)))
                    Ui.Text(Loc.T(sw.Summary), "small", "dim", "event-fact").AddTo(card);
                Ui.Text(Loc.T("ui.ops.donki_source"), "small", "faint").AddTo(card);
            }
            var row = Ui.Row("event-choices").AddTo(card);
            Ui.Spacer().AddTo(row);
            foreach (var c in e.Choices)
            {
                string choice = c;
                bool primary = c == e.DefaultChoice;
                Ui.Btn(Loc.T("choice." + c), () => Decide(e, choice), primary ? "btn-primary" : "btn").AddTo(row);
            }
        }

        static string Prompt(MissionEvent e)
        {
            switch (e.Kind)
            {
                case EventKind.SolarStorm:
                    return Loc.T("event.prompt.solarstorm", ("event", (object)Loc.SpaceWeather(e.RefId)), ("days", 3 + 3 * e.Severity));
                case EventKind.PartFailure:
                    return Loc.T("event.prompt.partfailure", ("part", (object)Loc.Part(e.RefId)));
                default:
                    return Loc.T("event.prompt.scienceopportunity", ("opportunity", (object)Loc.T("opp." + e.RefId)));
            }
        }

        void Decide(MissionEvent e, string choice)
        {
            _handled.Add(e.Id);
            _pending = null;
            _modal.AddToClassList("modal-hidden");
            S.Decide(e.Id, choice);
            RebuildCharts();
            var o = R.Events.FirstOrDefault(x => x.Event.Id == e.Id);
            if (o != null) AddLog(o, true);
            if (_day >= TotalDays && !_ended)
            {
                // 這個決定讓任務提早結束（例如電池提早耗盡）
                _day = TotalDays;
                _ended = true;
                ShowEnd();
            }
        }

        void AddLog(EventOutcome o, bool animate)
        {
            var row = Ui.El("log-row", "fade-in").AddTo(_log);
            Ui.Text(Loc.T("ui.ops.day_n", ("d", (object)o.Event.Day)) + " · " + Loc.T("event.title." + o.Event.Kind.ToString().ToLowerInvariant()), "small", "accent").AddTo(row);
            string text = Loc.T(o.ResultCode, ("days", (object)o.LostDays), ("part", Loc.Part(o.AffectedPartId)));
            Ui.Text(text, "small").AddTo(row);
            if (animate) Ui.AnimateIn(row, "fade-in--on"); else row.AddToClassList("fade-in--on");
        }

        void SkipToEnd()
        {
            // 沒處理的事件用預設選項（核心已經這樣算了），直接跳到結尾
            foreach (var e in S.Plan?.Events ?? new List<MissionEvent>())
                if (!_handled.Contains(e.Id) && e.Day < TotalDays)
                {
                    _handled.Add(e.Id);
                    var o = R.Events.FirstOrDefault(x => x.Event.Id == e.Id);
                    if (o != null) AddLog(o, false);
                }
            _pending = null;
            _modal.AddToClassList("modal-hidden");
            _day = TotalDays;
            _ended = true;
            ShowEnd();
        }

        void ShowEnd()
        {
            _endPanel.Clear();
            _endPanel.RemoveFromClassList("modal-hidden");
            var card = Ui.El("panel", "event-card", "end-card", "fade-in").AddTo(_endPanel);
            Ui.AnimateIn(card, "fade-in--on");
            bool full = R.EndReason == EndReason.MissionComplete;
            Ui.Text(Loc.T("ui.ops.end.eyebrow"), "eyebrow").AddTo(card);
            Ui.Text(Loc.T("ui.ops.end." + R.EndReason.ToString().ToLowerInvariant()), "h2", full ? "go" : "tight").AddTo(card);
            Ui.Text(Loc.T("ui.ops.end.summary", ("years", (object)Fmt.N(R.ActualYears, 1)), ("data", Fmt.N(R.DataReturnedGb / 1000, 1)), ("pct", Fmt.Pct(R.ReturnFraction))), "body").AddTo(card);
            var row = Ui.Row("event-choices").AddTo(card);
            Ui.Spacer().AddTo(row);
            Ui.Btn(Loc.T("ui.ops.to_report"), () =>
            {
                Sfx.Confirm();
                S.SetPhase(Phase.Report);
                SceneFlow.Go(SceneFlow.Report, 0.8f);
            }, "btn-primary", "btn-big").AddTo(row);
            if (full) Sfx.Go(); else Sfx.Alert();
        }
    }
}
