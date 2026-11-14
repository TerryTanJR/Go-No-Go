using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 設計室：左邊依角色分頁選零件（滑鼠移上去即時預覽），右邊六條資源條與警告（附電腦算好的解法），
    /// 中間是 3D 太空船（DesignRoom 場景）。按「Go/No-Go 點名」進任務控制中心。
    /// </summary>
    public sealed class DesignScreen : GameScreen
    {
        static int _trainingStep;

        VisualElement _left, _list, _warnings, _pills, _resPanel, _warnPanel;
        readonly Dictionary<ResourceId, ResourceBar> _bars = new Dictionary<ResourceId, ResourceBar>();
        readonly List<(VisualElement card, string category, string id)> _cards = new List<(VisualElement, string, string)>();
        Label _yearsValue, _reqText;
        VisualElement _reqCard;
        Button _pollButton;
        ScrollView _scroll;

        protected override void Build()
        {
            Sfx.SetAmbience(Sfx.Ambience.ControlRoom);
            _bars.Clear();
            _cards.Clear();

            _pills = TopBar(Loc.T(S.Scenario.Name));

            if (S.DesignLocked)
            {
                var banner = Ui.Row("locked-banner").AddTo(Root);
                Ui.Text(Loc.T("ui.design.locked_title"), "eyebrow", "tight").AddTo(banner);
                Ui.Text(Loc.T("ui.design.locked_desc"), "small").AddTo(banner);
            }
            else if (S.History == HistoryStage.Rewrite)
            {
                var banner = Ui.Row("locked-banner", "locked-banner--rewrite").AddTo(Root);
                Ui.Text(Loc.T("ui.design.rewrite_title"), "eyebrow", "accent").AddTo(banner);
                Ui.Text(Loc.T("ui.design.rewrite_desc"), "small").AddTo(banner);
            }

            BuildLeft();
            BuildRight();
            BuildBottom();
            if (S.TutorialMode) BuildTraining();

            On(() => S.DesignChanged += OnDesignChanged, () => S.DesignChanged -= OnDesignChanged);
            On(() => S.PreviewChanged += OnPreview, () => S.PreviewChanged -= OnPreview);
            On(() => S.ActiveRoleChanged += OnRoleChanged, () => S.ActiveRoleChanged -= OnRoleChanged);
            Refresh();
        }

        protected override void OnClose() => S.ClearPreview();

        // ---------------- 左：角色與零件 ----------------

        void BuildLeft()
        {
            _left = Ui.El("panel", "design-left").AddTo(Root);
            var tabs = Ui.Row("role-tabs").AddTo(_left);
            foreach (var r in Roles.All)
            {
                var role = r;
                var tab = Ui.Btn(Fmt.Role(role), () => S.SetActiveRole(role), "role-tab").AddTo(tabs);
                tab.EnableInClassList("role-tab--on", role == S.ActiveRole);
                tab.name = "tab-" + Roles.Key(role);
            }
            _reqCard = Ui.El("req-card").AddTo(_left);
            Ui.Text(Loc.T("ui.design.dept_requirement"), "eyebrow").AddTo(_reqCard);
            _reqText = Ui.Text("", "small").AddTo(_reqCard);

            _scroll = new ScrollView(ScrollViewMode.Vertical).AddTo(_left);
            _scroll.AddToClassList("design-scroll");
            _list = _scroll.contentContainer;
            BuildCategoryList();
        }

        void BuildCategoryList()
        {
            _list.Clear();
            _cards.Clear();
            _yearsValue = null;
            foreach (var cat in Roles.Categories(S.ActiveRole))
            {
                switch (cat)
                {
                    case "launcher": BuildLaunchers(); break;
                    case "years": BuildYears(); break;
                    case "orbit": BuildOrbits(); break;
                    default: BuildParts(cat); break;
                }
            }
            _scroll.scrollOffset = Vector2.zero;
        }

        void Section(string titleKey, string hint = null)
        {
            var row = Ui.Row().AddTo(_list);
            Ui.Text(Loc.T(titleKey), "section-title").AddTo(row);
            if (hint != null)
            {
                Ui.Spacer().AddTo(row);
                Ui.Text(hint, "small", "faint").AddTo(row);
            }
        }

        void BuildParts(string cat)
        {
            string hint = cat == PartCategory.Instrument
                ? Loc.T("ui.design.instrument_hint", ("max", (object)S.Scenario.MaxInstruments))
                : null;
            Section("ui.cat." + cat, hint);
            foreach (var p in S.PartsFor(cat))
            {
                var part = p;
                var card = Ui.El("part").AddTo(_list);
                var sw = Ui.El("part-swatch").AddTo(card);
                if (ColorUtility.TryParseHtmlString(part.Color, out var c)) sw.style.backgroundColor = c;
                var body = Ui.El("part-body").AddTo(card);
                var head = Ui.Row().AddTo(body);
                Ui.Text(Loc.Part(part.Id), "part-name").AddTo(head);
                Ui.Spacer().AddTo(head);
                Ui.Text(Loc.T("ui.tier." + part.Tier), "part-tier").AddTo(head);
                var stats = Ui.Row("part-stats").AddTo(body);
                foreach (var s in PartStats(part)) Ui.Text(s, "part-stat", "mono").AddTo(stats);
                Ui.El("part-check").AddTo(card);
                Wire(card, cat, part.Id, () => PartTip(part));
            }
        }

        static IEnumerable<string> PartStats(PartDef p)
        {
            if (p.MassKg > 0) yield return $"{Fmt.N(p.MassKg, p.MassKg < 10 ? 1 : 0)} kg";
            if (p.PowerW > 0) yield return $"{Fmt.N(p.PowerW)} W";
            if (p.CostMusd > 0) yield return Fmt.Money(p.CostMusd);
            switch (p.Category)
            {
                case PartCategory.Instrument:
                    yield return $"{Fmt.N(p.DataGbPerDay)} GB/{(Loc.IsZh ? "天" : "d")}";
                    yield return (Loc.IsZh ? "科學 " : "sci ") + Fmt.N(p.ScienceValue);
                    break;
                case PartCategory.Solar: yield return $"{Fmt.N(p.AreaM2, 1)} m²"; break;
                case PartCategory.Battery: yield return $"{Fmt.N(p.CapacityWh)} Wh"; break;
                case PartCategory.Antenna: yield return $"{Fmt.N(p.DownlinkMbps)} Mbps"; break;
                case PartCategory.GroundStation:
                    yield return (Loc.IsZh ? $"{p.Stations} 站" : $"{p.Stations} stn");
                    yield return Fmt.Money(p.OpsCostMusdPerYear) + (Loc.IsZh ? "/年" : "/yr");
                    break;
                case PartCategory.Computer:
                    yield return $"{Fmt.N(p.StorageGb)} GB";
                    yield return $"{Fmt.N(p.RadToleranceKrad)} krad";
                    break;
                case PartCategory.Propulsion: if (p.PropellantKg > 0) yield return (Loc.IsZh ? "推進劑 " : "prop ") + $"{Fmt.N(p.PropellantKg, 1)} kg"; break;
                case PartCategory.Shielding: yield return (Loc.IsZh ? "劑量 ×" : "dose ×") + Fmt.N(p.DoseFactor, 2); break;
            }
            if (p.Redundant) yield return Loc.IsZh ? "備援" : "redundant";
        }

        static string PartTip(PartDef p)
        {
            var s = Loc.T(p.Description);
            if (p.SunlitOnly) s += "\n\n" + Loc.T("ui.design.sunlit_only");
            if (!string.IsNullOrEmpty(p.Source?.Title)) s += "\n\n" + Loc.T("ui.design.source") + " " + p.Source.Title;
            return s;
        }

        void BuildOrbits()
        {
            Section("ui.cat.orbit");
            foreach (var o in S.Data.OrbitsFor(S.Scenario))
            {
                var orbit = o;
                var card = Ui.El("part").AddTo(_list);
                var sw = Ui.El("part-swatch").AddTo(card);
                if (ColorUtility.TryParseHtmlString(orbit.Color, out var c)) sw.style.backgroundColor = c;
                var body = Ui.El("part-body").AddTo(card);
                Ui.Text(Loc.Orbit(orbit.Id), "part-name").AddTo(body);
                var stats = Ui.Row("part-stats").AddTo(body);
                Ui.Text($"{Fmt.N(orbit.AltitudeKm)} km", "part-stat", "mono").AddTo(stats);
                if (orbit.DoseKradPerYear > 0) Ui.Text($"{Fmt.N(orbit.DoseKradPerYear, 1)} krad/{(Loc.IsZh ? "年" : "yr")}", "part-stat", "mono").AddTo(stats);
                if (orbit.DeltaVPerYearMs > 0) Ui.Text($"Δv {Fmt.N(orbit.DeltaVPerYearMs)} m/s/{(Loc.IsZh ? "年" : "yr")}", "part-stat", "mono").AddTo(stats);
                Ui.El("part-check").AddTo(card);
                Wire(card, "orbit", orbit.Id, () => Loc.T(orbit.Description));
            }
        }

        void BuildLaunchers()
        {
            Section("ui.cat.launcher");
            foreach (var l in S.Data.LaunchersFor(S.Scenario))
            {
                var lv = l;
                var card = Ui.El("part").AddTo(_list);
                Ui.El("part-swatch").AddTo(card);
                var body = Ui.El("part-body").AddTo(card);
                var head = Ui.Row().AddTo(body);
                Ui.Text(Loc.Launcher(lv.Id), "part-name").AddTo(head);
                if (lv.Historical)
                {
                    Ui.Spacer().AddTo(head);
                    Ui.Text(Loc.T("ui.design.historical"), "part-tier").AddTo(head);
                }
                var stats = Ui.Row("part-stats").AddTo(body);
                Ui.Text(Fmt.Money(lv.CostMusd), "part-stat", "mono").AddTo(stats);
                Ui.Text((Loc.IsZh ? "可靠度 " : "rel ") + Fmt.Pct(lv.Reliability), "part-stat", "mono").AddTo(stats);
                var orbitKey = OrbitCapacityKey();
                if (orbitKey != null && lv.CapacityKg.TryGetValue(orbitKey, out var cap))
                    Ui.Text($"{Fmt.N(cap)} kg → {Loc.Orbit(S.Design.OrbitId)}", "part-stat", "mono").AddTo(stats);
                else
                    Ui.Text(Loc.T("ui.design.cannot_reach"), "part-stat", "over").AddTo(stats);
                Ui.El("part-check").AddTo(card);
                Wire(card, "launcher", lv.Id, () => Loc.T(lv.Description) +
                    (string.IsNullOrEmpty(lv.FlightRecord) ? "" : "\n\n" + lv.FlightRecord));
            }
        }

        /// <summary>launchers.json 的運力鍵：leo / sso / geo / llo（由軌道 id 的前綴決定）。</summary>
        static string OrbitCapacityKey()
        {
            var id = S.Design.OrbitId ?? "";
            int u = id.IndexOf('_');
            return u > 0 ? id.Substring(0, u) : id;
        }

        void BuildYears()
        {
            Section("ui.cat.years");
            var row = Ui.Row("years-row").AddTo(_list);
            Ui.Btn("-", () => S.SetYears(S.Design.MissionYears - 1), "btn-small").AddTo(row);
            _yearsValue = Ui.Text("", "h2", "mono", "years-value").AddTo(row);
            Ui.Btn("+", () => S.SetYears(S.Design.MissionYears + 1), "btn-small").AddTo(row);
            Ui.Text(Loc.T("ui.design.years_hint"), "small", "faint", "years-hint").AddTo(row);
        }

        void Wire(VisualElement card, string category, string id, System.Func<string> tip)
        {
            _cards.Add((card, category, id));
            card.RegisterCallback<PointerEnterEvent>(_ =>
            {
                if (S.DesignLocked) return;
                Sfx.Hover();
                S.Hover(category, id);
            });
            card.RegisterCallback<PointerLeaveEvent>(_ => S.ClearPreview());
            card.RegisterCallback<ClickEvent>(_ =>
            {
                if (S.DesignLocked) { UIRoot.Toast(Loc.T("ui.design.locked_toast")); return; }
                if (category == PartCategory.Instrument && !S.Design.Instruments.Contains(id) && !S.CanAddInstrument)
                {
                    UIRoot.Toast(Loc.T("ui.design.instrument_full"));
                    Sfx.Alert();
                    return;
                }
                Sfx.Click();
                switch (category)
                {
                    case "orbit": S.SetOrbit(id); break;
                    case "launcher": S.SetLauncher(id); break;
                    default: S.SetPart(category, id); break;
                }
            });
            UIRoot.AttachTip(card, tip);
        }

        // ---------------- 右：資源與警告 ----------------

        void BuildRight()
        {
            var right = Ui.El("design-right").AddTo(Root);
            var res = Ui.Panel(right, Loc.T("ui.design.resources"), out _resPanel);
            foreach (ResourceId id in System.Enum.GetValues(typeof(ResourceId)))
            {
                var bar = new ResourceBar(id).AddTo(res);
                bar.EnableInClassList("res--hidden", !S.ResourceVisible(id));
                _bars[id] = bar;
            }
            var warnPanel = Ui.Panel(right, Loc.T("ui.design.warnings"), out _warnPanel, "warn-panel");
            var sc = new ScrollView(ScrollViewMode.Vertical).AddTo(warnPanel);
            sc.AddToClassList("warn-scroll");
            _warnings = sc.contentContainer;
        }

        void BuildBottom()
        {
            var bottom = Ui.Row("design-bottom").AddTo(Root);
            Ui.Btn(Loc.T("ui.design.briefing"), () => { S.SetPhase(Phase.Briefing); SceneFlow.Go(SceneFlow.MissionControl); }, "btn-ghost").AddTo(bottom);
            Ui.Spacer().AddTo(bottom);
            _pollButton = Ui.Btn(Loc.T("ui.design.call_poll"), CallPoll, "btn-primary", "btn-big").AddTo(bottom);
        }

        static void CallPoll()
        {
            Sfx.Confirm();
            S.SetPhase(Phase.RollCall);
            SceneFlow.Go(SceneFlow.MissionControl);
        }

        // ---------------- 更新 ----------------

        void OnDesignChanged(Evaluation ev) => Refresh();

        void OnRoleChanged(Role r)
        {
            foreach (var role in Roles.All)
                _left.Q<Button>("tab-" + Roles.Key(role))?.EnableInClassList("role-tab--on", role == r);
            BuildCategoryList();
            Refresh();
        }

        void Refresh()
        {
            // 火箭運力顯示依目前軌道而變：導演分頁要重建。必須在填入卡片狀態與年限之前做，
            // 否則重建出來的新元件（例如年限數字）會是空的。
            if (S.ActiveRole == Role.Director && _cards.Count > 0 && _lastOrbit != S.Design.OrbitId) BuildCategoryList();
            _lastOrbit = S.Design.OrbitId;

            var ev = S.Evaluation;
            foreach (var kv in _bars) kv.Value.Set(ev[kv.Key]);
            OnPreview();

            foreach (var (card, cat, id) in _cards)
            {
                bool on = cat switch
                {
                    "orbit" => S.Design.OrbitId == id,
                    "launcher" => S.Design.LauncherId == id,
                    PartCategory.Instrument => S.Design.Instruments.Contains(id),
                    _ => S.Design.Get(cat) == id,
                };
                card.EnableInClassList("part--on", on);
                card.SetEnabled(!S.DesignLocked || on);
            }
            if (_yearsValue != null) _yearsValue.text = Fmt.Years(S.Design.MissionYears);

            S.Requirements.TryGetValue(S.ActiveRole, out var req);
            _reqCard.style.display = req == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (req != null)
            {
                _reqText.text = Loc.T(req.Text);
                bool met = Requirements.IsMetPreLaunch(req, ev, S.Design, S.Data, S.Difficulty);
                _reqCard.EnableInClassList("req-card--met", met);
            }

            RefreshPills(ev);
            RefreshWarnings(ev);
        }

        string _lastOrbit;

        void RefreshPills(Evaluation ev)
        {
            _pills.Clear();
            double left = ev.Cost.BudgetMusd - ev.Cost.TotalMusd;
            StatPill(_pills, Loc.T("ui.pill.budget_left"), Fmt.Money(left), left < 0 ? "over" : left < ev.Cost.BudgetMusd * 0.1 ? "tight" : "go");
            double years = ev.Lifetime.Years;
            StatPill(_pills, Loc.T("ui.pill.lifetime"), years >= 50 ? "50+" : Fmt.Years(years), years < ev.Lifetime.TargetYears ? "tight" : "go");
            StatPill(_pills, Loc.T("ui.pill.science"), Fmt.N(ev.SciencePerYear), ev.SciencePerYear <= 0 ? "over" : null);
            StatPill(_pills, Loc.T("ui.pill.mass"), $"{Fmt.N(ev.Mass.WetKg)} kg");
        }

        void RefreshWarnings(Evaluation ev)
        {
            _warnings.Clear();
            string hint = S.Difficulty.HintLevel;
            var list = ev.Warnings.OrderByDescending(w => w.Severity).ToList();
            if (list.Count == 0)
            {
                var ok = Ui.El("warn", "warn--ok").AddTo(_warnings);
                Ui.Text(Loc.T("ui.design.all_clear"), "warn-text", "go").AddTo(ok);
                return;
            }
            if (hint == "lights")
            {
                var w = Ui.El("warn").AddTo(_warnings);
                Ui.Text(Loc.T("ui.design.lights_only", ("n", (object)list.Count)), "warn-text").AddTo(w);
                return;
            }
            foreach (var w in list)
            {
                var card = Ui.El("warn").AddTo(_warnings);
                card.EnableInClassList("warn--tight", w.Severity != ResourceStatus.Over);
                Ui.Text(WarningText(w), "warn-text").AddTo(card);
                if (hint != "fixes" || w.Fixes.Count == 0 || S.DesignLocked) continue;
                var row = Ui.El("fix-row").AddTo(card);
                foreach (var f in w.Fixes.Take(2))
                {
                    var fix = f;
                    var b = Ui.Btn(FixText(fix), () => { Sfx.Confirm(); S.ApplyFix(fix); }, "fix").AddTo(row);
                    b.RegisterCallback<PointerEnterEvent>(_ => PreviewFix(fix));
                    b.RegisterCallback<PointerLeaveEvent>(_ => S.ClearPreview());
                }
            }
        }

        void PreviewFix(Fix f)
        {
            switch (f.Kind)
            {
                case FixKind.SwapPart: S.Hover(f.Category, f.ToId); break;
                case FixKind.ChangeOrbit: S.Hover("orbit", f.ToId); break;
                case FixKind.ChangeLauncher: S.Hover("launcher", f.ToId); break;
                case FixKind.RemoveInstrument: S.Hover(PartCategory.Instrument, f.FromId); break;
            }
        }

        public static string WarningText(Warning w)
        {
            // 橘燈（餘裕不足、還沒超標）不能用超標的句子：「超出 / 還差」的數字會是負的
            if (w.Severity == ResourceStatus.Tight && w.Values.ContainsKey("margin_pct"))
            {
                var vals = new Dictionary<string, double>(w.Values) { ["need"] = Evaluator.TightMargin * 100 };
                return Loc.Format(Loc.T("warn.tight"), k => k == "res" ? Loc.T("res." + w.Resource.ToString().ToLowerInvariant()) : null, vals);
            }
            return Loc.Format(Loc.T(w.Code), k => k == "launcher" ? Loc.Launcher(S.Design.LauncherId) : null, w.Values);
        }

        public static string FixText(Fix f)
        {
            string key = f.Kind switch
            {
                FixKind.SwapPart => "fix.swap",
                FixKind.RemoveInstrument => "fix.remove",
                FixKind.ChangeOrbit => "fix.orbit",
                FixKind.ChangeLauncher => "fix.launcher",
                _ => "fix.years",
            };
            string Name(string id) => f.Kind == FixKind.ChangeOrbit ? Loc.Orbit(id) : f.Kind == FixKind.ChangeLauncher ? Loc.Launcher(id) : Loc.Part(id);
            var text = Loc.T(key, ("from", (object)Name(f.FromId)), ("to", Name(f.ToId)), ("years", f.Years));
            if (System.Math.Abs(f.CostDeltaMusd) >= 0.05)
                text += $"  ({(f.CostDeltaMusd > 0 ? "+" : "-")}{Fmt.Money(System.Math.Abs(f.CostDeltaMusd))})";
            return text;
        }

        void OnPreview()
        {
            var pv = S.PreviewEvaluation;
            foreach (var kv in _bars) kv.Value.Preview(pv?[kv.Key]);
        }

        // ---------------- 地面訓練 ----------------

        void BuildTraining()
        {
            const int steps = 5;
            if (_trainingStep >= steps) return;
            var card = Ui.El("panel", "training-coach").AddTo(Root);
            Ui.Text(Loc.T("ui.training.title") + $"  {_trainingStep + 1}/{steps}", "eyebrow").AddTo(card);
            Ui.Text(Loc.T("ui.training.step" + (_trainingStep + 1)), "body", "training-text").AddTo(card);
            var row = Ui.Row("training-actions").AddTo(card);
            Ui.Btn(Loc.T("ui.training.skip"), () => { _trainingStep = steps; card.RemoveFromHierarchy(); HighlightTraining(); }, "btn-ghost", "btn-small").AddTo(row);
            Ui.Spacer().AddTo(row);
            Ui.Btn(Loc.T(_trainingStep == steps - 1 ? "ui.training.done" : "ui.training.next"), () =>
            {
                _trainingStep++;
                card.RemoveFromHierarchy();
                BuildTraining();
            }, "btn-primary", "btn-small").AddTo(row);
            HighlightTraining();
        }

        void HighlightTraining()
        {
            if (_trainingStep >= 5) _trainingStep = 5;
            // 依步驟把相關區塊框起來
            _left.EnableInClassList("coach-focus", _trainingStep == 1 || _trainingStep == 2);
            _resPanel?.EnableInClassList("coach-focus", _trainingStep == 0);
            _warnPanel?.EnableInClassList("coach-focus", _trainingStep == 3);
            _pollButton?.EnableInClassList("coach-focus", _trainingStep == 4);
        }

        public static void ResetTraining() => _trainingStep = 0;
    }
}
