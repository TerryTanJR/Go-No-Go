using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>
    /// 超標時找出「兩個真的有效的解法」：把每一種單步修改都實際算一次，
    /// 只留下能解決問題的，再挑副作用最小的兩個。所以提示永遠不會錯。
    /// </summary>
    public static class FixFinder
    {
        public static void AttachFixes(Evaluation ev, Design design, GameData data, string difficultyId)
        {
            var targets = ev.Warnings.Where(w => w.Severity == ResourceStatus.Over &&
                                                 (w.Resource.HasValue || w.Code.StartsWith("warn.lifetime"))).ToList();
            if (targets.Count == 0) return;

            var candidates = Candidates(design, data, difficultyId)
                .Select(f => (fix: f, ev: Evaluator.EvaluateDesign(f.ApplyTo(design), data, difficultyId, false)))
                .Where(x => x.ev.DesignErrors.Count == 0)
                .ToList();
            int baseOver = ev.Resources.Values.Count(r => r.Status == ResourceStatus.Over);

            foreach (var w in targets)
            {
                var good = new List<(Fix fix, double score)>();
                foreach (var (fix, cev) in candidates)
                {
                    double newMargin;
                    if (w.Resource.HasValue)
                    {
                        var r = cev.Resources[w.Resource.Value];
                        if (r.Status == ResourceStatus.Over) continue;
                        newMargin = r.Margin;
                    }
                    else
                    {
                        if (cev.Lifetime.Years < fix.ApplyTo(design).MissionYears) continue;
                        newMargin = cev.Lifetime.Years;
                    }
                    int newOver = cev.Resources.Values.Count(r => r.Status == ResourceStatus.Over);
                    double costDelta = cev.Cost.TotalMusd - ev.Cost.TotalMusd;
                    double sciDelta = cev.SciencePerYear - ev.SciencePerYear;
                    var f = new Fix
                    {
                        Kind = fix.Kind, Category = fix.Category, FromId = fix.FromId, ToId = fix.ToId, Years = fix.Years,
                        NewMargin = newMargin, CostDeltaMusd = costDelta, ScienceDelta = sciDelta,
                    };
                    // 分數越低越好：先避免製造新的超標，再少犧牲科學、少花錢
                    double score = 100.0 * Math.Max(0, newOver - (baseOver - 1))
                                   + 3.0 * Math.Max(0, -sciDelta)
                                   + 0.5 * Math.Max(0, costDelta)
                                   - 5.0 * Math.Min(0.3, Math.Max(0, w.Resource.HasValue ? newMargin : 0))
                                   + (fix.Kind == FixKind.ChangeYears ? 15 : 0)      // 縮短任務是最後手段
                                   + (fix.Kind == FixKind.RemoveInstrument ? 8 : 0);
                    good.Add((f, score));
                }

                var picked = new List<Fix>();
                foreach (var g in good.OrderBy(g => g.score).ThenBy(g => g.fix.ToId, StringComparer.Ordinal))
                {
                    // 兩個解法要是不同方向（不同類別或不同種類）
                    if (picked.Any(p => p.Kind == g.fix.Kind && p.Category == g.fix.Category)) continue;
                    picked.Add(g.fix);
                    if (picked.Count == 2) break;
                }
                w.Fixes = picked;
            }
        }

        /// <summary>所有單步修改。</summary>
        public static IEnumerable<Fix> Candidates(Design design, GameData data, string difficultyId)
        {
            var scenario = data.Scenario(design.ScenarioId);
            var diff = data.Difficulty(difficultyId) ?? data.Difficulties.FirstOrDefault() ?? new DifficultyDef();
            var shelf = new HashSet<string>(data.ShelfFor(scenario, diff).Select(p => p.Id));

            foreach (var cat in PartCategory.Single)
            {
                var cur = design.Get(cat);
                var options = cat == PartCategory.GroundStation ? data.PartsIn(cat).Where(p => p.Tier <= diff.TiersPerCategory) : data.PartsIn(cat).Where(p => shelf.Contains(p.Id));
                foreach (var p in options.Where(p => p.Id != cur))
                    yield return new Fix { Kind = FixKind.SwapPart, Category = cat, FromId = cur, ToId = p.Id };
            }

            var instruments = data.PartsIn(PartCategory.Instrument).Where(p => shelf.Contains(p.Id)).ToList();
            foreach (var cur in design.Instruments)
            {
                if (design.Instruments.Count > 1)
                    yield return new Fix { Kind = FixKind.RemoveInstrument, Category = PartCategory.Instrument, FromId = cur };
                foreach (var p in instruments.Where(p => !design.Instruments.Contains(p.Id)))
                    yield return new Fix { Kind = FixKind.SwapPart, Category = PartCategory.Instrument, FromId = cur, ToId = p.Id };
            }

            foreach (var o in data.OrbitsFor(scenario).Where(o => o.Id != design.OrbitId
                         && SessionRules.OrbitLauncherCheck(data, o.Id, design.LauncherId) == SessionRules.Ok))
                yield return new Fix { Kind = FixKind.ChangeOrbit, Category = "orbit", FromId = design.OrbitId, ToId = o.Id };

            foreach (var l in data.LaunchersFor(scenario).Where(l => l.Id != design.LauncherId
                         && SessionRules.OrbitLauncherCheck(data, design.OrbitId, l.Id) == SessionRules.Ok))
                yield return new Fix { Kind = FixKind.ChangeLauncher, Category = "launcher", FromId = design.LauncherId, ToId = l.Id };

            if (design.MissionYears > 1)
                yield return new Fix { Kind = FixKind.ChangeYears, Category = "years", Years = design.MissionYears - 1 };
        }
    }
}
