using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>發射時間表上的一個事件（真實時間，T+ 秒）。</summary>
    public sealed class TimelineEvent
    {
        public string Id { get; set; } = "";
        public double T { get; set; }
        /// <summary>播報的字串鍵。</summary>
        public string Call { get; set; } = "";
        /// <summary>第幾節（播報「第 {n} 節點火」用；Taurus 從第 0 節算起）。</summary>
        public int N { get; set; }
        public override string ToString() => $"{Id}@{T:0}";
    }

    /// <summary>
    /// 一次發射的完整時間表與遙測：依火箭（launchers.json 的 flight）、目標軌道、發射結果組出來。
    /// 時間是真實的任務時間（例如 Falcon 9 的一節關機在 T+2:33），演出可以用 1、5、10、50 倍播放。
    /// <para>
    /// 事件 id：mach1、maxq、booster_cutoff、booster_sep（助推器）、meco、stage_sep（第一節）、ses（上節點火）、
    /// fairing、upper_cutoff、upper_sep、upper_ignite（多節火箭或踢昇級的下一節）、battery_swap（Electron）、
    /// seco（第一次關機）、coast、restart、seco2（再點火）、deploy（衛星分離）；
    /// 失敗：anomaly（爆炸）、fairing_fail、underspeed；入軌後失效：eclipse、signal_lost；最後 end。
    /// </para>
    /// </summary>
    public sealed class LaunchTimeline
    {
        public List<TimelineEvent> Events { get; } = new List<TimelineEvent>();
        public double End { get; private set; }
        /// <summary>演出換到太空場景的時間：天空已經夠黑、第二節已經點火、還沒拋整流罩。</summary>
        public double SwapT { get; private set; }
        public int Stages { get; private set; } = 2;
        public bool HasBoosters { get; private set; }
        public string OrbitId { get; private set; } = "";

        readonly List<double> _kt = new List<double>();
        readonly List<double> _ka = new List<double>();
        readonly List<double> _kv = new List<double>();
        readonly List<double> _ma = new List<double>();
        readonly List<double> _mv = new List<double>();
        readonly List<(double t, bool on)> _thrust = new List<(double, bool)>();

        const double Mu = 398600.4418, Re = 6371.0;
        public static double CircularVelocity(double altKm) => Math.Sqrt(Mu / (Re + altKm));

        /// <summary>沒有 flight 資料的火箭用的通用時間表（Falcon 9 的量級）。</summary>
        static readonly FlightProfileDef Generic = new FlightProfileDef
        {
            Ascent = new List<FlightEventDef>
            {
                new FlightEventDef { Id = "maxq", T = 72 }, new FlightEventDef { Id = "meco", T = 150 },
                new FlightEventDef { Id = "stage_sep", T = 153 }, new FlightEventDef { Id = "ses", T = 160 },
                new FlightEventDef { Id = "fairing", T = 200 }, new FlightEventDef { Id = "seco", T = 500 },
            },
            Orbits = new Dictionary<string, List<FlightEventDef>> { ["default"] = new List<FlightEventDef> { new FlightEventDef { Id = "deploy", T = 720 } } },
        };

        public static LaunchTimeline Build(GameData data, Design d, LaunchOutcome outcome, string reasonCode)
        {
            var launcher = data.Launcher(d.LauncherId);
            var orbit = data.Orbit(d.OrbitId);
            var prof = launcher?.Flight != null && launcher.Flight.Ascent.Count > 0 ? launcher.Flight : Generic;
            List<FlightEventDef> post = null;
            if (prof.Orbits != null && !prof.Orbits.TryGetValue(d.OrbitId ?? "", out post)) prof.Orbits.TryGetValue("default", out post);
            post ??= new List<FlightEventDef>();
            if (!post.Any(e => e.Id == "deploy"))
            {
                double last = prof.Ascent.Max(e => e.T);
                post = post.Concat(new[] { new FlightEventDef { Id = "deploy", T = last + 240 } }).ToList();
            }
            return Build(prof, post, orbit?.Id ?? d.OrbitId ?? "", orbit?.AltitudeKm ?? 500, outcome, reasonCode);
        }

        public static LaunchTimeline Build(FlightProfileDef prof, List<FlightEventDef> post, string orbitId, double orbitAltKm, LaunchOutcome outcome, string reasonCode)
        {
            var tl = new LaunchTimeline { Stages = Math.Max(2, prof.Stages), OrbitId = orbitId ?? "" };
            var asc = prof.Ascent.OrderBy(e => e.T).Select(Ev).ToList();
            var orb = post.OrderBy(e => e.T).Select(Ev).ToList();
            tl.HasBoosters = asc.Any(e => e.Id == "booster_sep");
            double seco = asc.LastOrDefault(e => e.Id == "seco")?.T ?? asc.Max(e => e.T);
            bool reaches = outcome == LaunchOutcome.Nominal || outcome == LaunchOutcome.LowerOrbit || outcome == LaunchOutcome.DeadOnOrbit;
            bool anomaly = outcome == LaunchOutcome.Failure && reasonCode == "launch.anomaly";

            var all = new List<TimelineEvent>();
            if (anomaly)
            {
                // 飛行中異常：在最大動壓附近爆炸（真實的失敗時間各不相同，這裡選最常見的高負載點）
                double at = (asc.FirstOrDefault(e => e.Id == "maxq")?.T ?? 60) + 4;
                all.AddRange(asc.Where(e => e.T < at));
                all.Add(new TimelineEvent { Id = "anomaly", T = at });
                tl.End = at + 8;
            }
            else if (!reaches)
            {
                // 速度不足（太重、到不了、整流罩沒分離）：每一節照常燒完，最後一節關機時速度還不夠，掉回大氣層
                foreach (var e in asc)
                {
                    if (e.Id == "seco") { all.Add(new TimelineEvent { Id = "underspeed", T = e.T }); continue; }
                    if (e.Id == "fairing" && outcome == LaunchOutcome.FairingFailure) { all.Add(new TimelineEvent { Id = "fairing_fail", T = e.T }); continue; }
                    all.Add(e);
                }
                tl.End = seco + 14;
            }
            else
            {
                all.AddRange(asc);
                all.AddRange(orb.Where(e => e.T > seco));
                double deploy = all.Last(e => e.Id == "deploy").T;
                if (outcome == LaunchOutcome.DeadOnOrbit)
                {
                    all.Add(new TimelineEvent { Id = "eclipse", T = deploy + 25 });
                    all.Add(new TimelineEvent { Id = "signal_lost", T = deploy + 40 });
                    tl.End = deploy + 50;
                }
                else tl.End = deploy + 20;
            }
            all = all.OrderBy(e => e.T).ToList();

            // 關機後到下一次點火超過一分鐘：加一個「滑行」播報
            var coasts = new List<TimelineEvent>();
            for (int i = 0; i < all.Count; i++)
            {
                if (!IsCutoff(all[i].Id)) continue;
                var next = all.Skip(i + 1).FirstOrDefault(e => IsIgnition(e.Id) || e.Id == "deploy");
                if (next != null && next.T - all[i].T > 60)
                    coasts.Add(new TimelineEvent { Id = "coast", T = all[i].T + 4, Call = next.Id == "deploy" ? "ui.launch.call.coast_deploy" : "" });
            }
            all.AddRange(coasts);
            all.Add(new TimelineEvent { Id = "end", T = tl.End });
            all = all.OrderBy(e => e.T).ToList();

            // 播報字串：最後一次關機是「入軌」；之前的 SECO 是「進入停泊軌道」
            var lastCut = all.LastOrDefault(e => IsCutoff(e.Id) && e.Id != "underspeed");
            foreach (var e in all)
            {
                if (!string.IsNullOrEmpty(e.Call)) continue;
                if (reaches && ReferenceEquals(e, lastCut)) e.Call = outcome == LaunchOutcome.LowerOrbit ? "ui.launch.call.lower" : "ui.launch.call.orbit";
                else if (e.Id == "deploy") e.Call = orbitId == "geo" ? "ui.launch.call.deploy_gto" : orbitId == "llo_100" ? "ui.launch.call.deploy_lunar" : "ui.launch.call.deploy";
                else if (e.Id == "end") e.Call = "";
                else e.Call = "ui.launch.call." + e.Id;
            }
            // 節數：一節關機、分離是第 1 節（Taurus 是第 0 節），之後每次上節點火加一
            int cur = all.Any(e => e.Call.Contains("stage0")) ? 0 : 1;
            foreach (var e in all)
            {
                if (e.Id == "ses" || e.Id == "upper_ignite") cur++;
                if (e.Id == "meco" || e.Id == "stage_sep" || e.Id == "ses" || e.Id.StartsWith("upper_") || e.Id == "seco") e.N = cur;
            }
            tl.Events.AddRange(all);
            tl.BuildTelemetry(orbitAltKm, outcome, reaches);
            tl.BuildTangents(tl._ka, tl._ma);
            tl.BuildTangents(tl._kv, tl._mv);
            tl.BuildThrust();

            // 換到太空場景：高度到 80 km（天空幾乎全黑）、第二節點火至少 6 秒、拋整流罩前 10 秒
            double ses = tl.First("ses")?.T ?? 0;
            double fairing = tl.First("fairing")?.T ?? tl.First("fairing_fail")?.T ?? seco;
            double at80 = tl.TimeAtAltitude(80);
            tl.SwapT = Math.Min(fairing - 10, Math.Max(ses + 6, at80));
            if (anomaly) tl.SwapT = double.MaxValue;
            return tl;
        }

        static TimelineEvent Ev(FlightEventDef e) => new TimelineEvent { Id = e.Id, T = e.T, Call = e.Call ?? "" };

        public static bool IsCutoff(string id) => id == "meco" || id == "upper_cutoff" || id == "seco" || id == "seco2" || id == "underspeed";
        public static bool IsIgnition(string id) => id == "ses" || id == "upper_ignite" || id == "restart";

        public TimelineEvent First(string id) => Events.FirstOrDefault(e => e.Id == id);

        /// <summary>t 之後的下一個事件（HUD 顯示「下一步」；滑行、結束不算）。</summary>
        public TimelineEvent Next(double t) => Events.FirstOrDefault(e => e.T > t && e.Id != "coast" && e.Id != "end");

        // ---------------------------------------------------------------- 遙測

        void Key(double t, double alt, double v)
        {
            // 時間相同的關鍵點只留最後一個
            if (_kt.Count > 0 && Math.Abs(_kt[_kt.Count - 1] - t) < 1e-6) { _ka[_ka.Count - 1] = alt; _kv[_kv.Count - 1] = v; return; }
            if (_kt.Count > 0 && t < _kt[_kt.Count - 1]) return;
            _kt.Add(t); _ka.Add(alt); _kv.Add(v);
        }

        /// <summary>
        /// 高度與速度的關鍵點：最大動壓約 12 km、0.45 km/s；一節關機的高度與速度依燒的時間（Falcon 9 約 70 km、2.3 km/s，
        /// Delta II 燒得久，約 115 km、4 km/s）；拋整流罩時已在 110 km 以上；SECO 時到停泊軌道（約 200 km）或目標軌道。
        /// 關機到下一次點火之間速度、高度維持（滑行）。
        /// </summary>
        void BuildTelemetry(double orbitAltKm, LaunchOutcome outcome, bool reaches)
        {
            double finalAlt = Math.Min(orbitAltKm, 2000);
            if (outcome == LaunchOutcome.LowerOrbit) finalAlt *= 0.8;
            bool parking = Events.Any(e => e.Id == "restart") || Events.Any(e => e.Id == "upper_ignite" && e.T > (First("seco")?.T ?? 1e9));
            bool deepSpace = OrbitId == "geo" || OrbitId == "llo_100";

            var meco = First("meco");
            double tMeco = meco?.T ?? 150;
            double altMeco = Clamp(0.45 * tMeco, 35, 115), vMeco = 0.015 * tMeco;
            var seco = First("seco") ?? First("underspeed");
            double tSeco = seco?.T ?? tMeco + 300;
            double altSeco = !reaches ? 150 : parking ? 200 : Math.Min(finalAlt, 700);
            double vSeco = !reaches ? 6.4 : CircularVelocity(altSeco);
            double tFair = (First("fairing") ?? First("fairing_fail"))?.T ?? tMeco + 40;
            var ses = First("ses");
            double altSes = ses != null ? altMeco + Math.Max(0, ses.T - tMeco) * 0.9 : altMeco;
            double altFair = Math.Max(altSes + 8, 110);

            Key(0, 0, 0);
            var maxq = First("maxq");
            if (maxq != null && maxq.T < tMeco)
            {
                // 最大動壓之前大致是等加速度：速度和時間成正比、高度和時間平方成正比
                foreach (var f in new[] { 0.15, 0.35, 0.6 }) Key(maxq.T * f, 12 * f * f, 0.45 * f);
                Key(maxq.T, 12, 0.45);
            }
            Key(tMeco, altMeco, vMeco);
            if (ses != null) Key(ses.T, altSes, vMeco - 0.02 * (ses.T - tMeco) / 10);

            // 爬升段中途的上節關機（多節火箭）：速度依燒完的節數平均分配，關機到下一節點火之間維持
            var burns = Events.Where(e => e.T > (ses?.T ?? tMeco) && e.T <= tSeco && (e.Id == "upper_cutoff" || e.Id == "upper_ignite" || e.Id == "fairing" || e.Id == "fairing_fail")).ToList();
            int nCut = burns.Count(e => e.Id == "upper_cutoff") + 1;
            int k = 0;
            double lastA = altMeco, lastV = vMeco;
            double v0 = ses != null ? vMeco : vMeco;
            foreach (var e in burns)
            {
                double f = (double)(k + (e.Id == "upper_cutoff" ? 1 : 0)) / nCut;
                if (e.Id == "fairing" || e.Id == "fairing_fail")
                {
                    if (nCut == 1) Key(e.T, altFair, Lerp(v0, vSeco, (e.T - (ses?.T ?? tMeco)) / Math.Max(1, tSeco - (ses?.T ?? tMeco)) * 0.6));
                    continue;
                }
                if (e.Id == "upper_cutoff") { k++; lastA = Math.Max(altFair, Lerp(altMeco, altSeco, f)); lastV = Lerp(v0, vSeco, f); Key(e.T, lastA, lastV); }
                else Key(e.T, lastA, lastV); // 下一節點火：滑行結束
            }
            Key(tSeco, altSeco, vSeco);

            if (!reaches)
            {
                // 速度不足：慢慢掉回去
                Key(End, 95, 6.6);
                return;
            }
            // 入軌之後：再點火（太陽同步、地球同步轉移、地月轉移）或踢昇級
            double lastT = tSeco, a = altSeco, v = vSeco;
            foreach (var e in Events.Where(x => x.T > tSeco))
            {
                if (IsIgnition(e.Id)) Key(e.T, a, v);
                else if (e.Id == "seco2" || e.Id == "upper_cutoff")
                {
                    if (OrbitId == "geo") { a = 250; v = 10.2; }
                    else if (OrbitId == "llo_100") { a = 260; v = 10.9; }
                    else { a = finalAlt; v = CircularVelocity(finalAlt); }
                    Key(e.T, a, v);
                    lastT = e.T;
                }
                else if (e.Id == "deploy")
                {
                    // 地球同步轉移、地月轉移：關機後一路往上爬（速度跟著變慢）
                    if (deepSpace) { double dt = e.T - lastT; a += dt * 2.2; v -= dt * 0.0012; }
                    Key(e.T, a, v);
                }
            }
            Key(End, a + (deepSpace ? (End - lastT) * 2.2 : 0), v);
        }

        void BuildThrust()
        {
            _thrust.Add((0, true));
            foreach (var e in Events)
            {
                if (IsIgnition(e.Id)) _thrust.Add((e.T, true));
                else if (IsCutoff(e.Id) || e.Id == "anomaly") _thrust.Add((e.T, false));
            }
        }

        /// <summary>這個時間引擎有沒有在燒（HUD 的推力、尾焰）。</summary>
        public bool Thrusting(double t)
        {
            if (t < 0) return false;
            bool on = false;
            foreach (var (tt, o) in _thrust) { if (tt > t) break; on = o; }
            return on;
        }

        public double Altitude(double t) => Interp(_ka, _ma, t);
        public double Velocity(double t) => Interp(_kv, _mv, t);

        /// <summary>
        /// 單調三次插值（Fritsch–Butland）：曲線平滑、不會在關鍵點停頓（逐段 smoothstep 每到一個點速度就歸零），
        /// 也不會超過兩端的值（高度不會先衝過頭再掉回來）。
        /// </summary>
        double Interp(List<double> ys, List<double> ms, double t)
        {
            int n = _kt.Count;
            if (n == 0) return 0;
            if (t <= _kt[0]) return ys[0];
            if (t >= _kt[n - 1]) return ys[n - 1];
            int lo = 0, hi = n - 1;
            while (hi - lo > 1) { int mid = (lo + hi) / 2; if (_kt[mid] < t) lo = mid; else hi = mid; }
            double h = Math.Max(1e-6, _kt[hi] - _kt[lo]);
            double f = (t - _kt[lo]) / h, f2 = f * f, f3 = f2 * f;
            return (2 * f3 - 3 * f2 + 1) * ys[lo] + (f3 - 2 * f2 + f) * h * ms[lo] + (-2 * f3 + 3 * f2) * ys[hi] + (f3 - f2) * h * ms[hi];
        }

        void BuildTangents(List<double> ys, List<double> ms)
        {
            ms.Clear();
            int n = _kt.Count;
            var d = new double[Math.Max(0, n - 1)];
            for (int i = 0; i < n - 1; i++) d[i] = (ys[i + 1] - ys[i]) / Math.Max(1e-6, _kt[i + 1] - _kt[i]);
            for (int i = 0; i < n; i++)
            {
                if (n < 2) { ms.Add(0); continue; }
                if (i == 0) { ms.Add(d[0]); continue; }
                if (i == n - 1) { ms.Add(d[n - 2]); continue; }
                double a = d[i - 1], b = d[i];
                ms.Add(a * b <= 0 ? 0 : 2 * a * b / (a + b));
            }
        }

        /// <summary>高度第一次到達 km 的時間（二分搜尋；到不了就回傳 SECO）。</summary>
        public double TimeAtAltitude(double km)
        {
            double lo = 0, hi = End;
            if (Altitude(hi) < km && _ka.Max() < km) return First("seco")?.T ?? End;
            for (int i = 0; i < 40; i++)
            {
                double mid = (lo + hi) / 2;
                if (Altitude(mid) >= km) hi = mid; else lo = mid;
            }
            return hi;
        }

        /// <summary>HUD 上的階段名稱。</summary>
        public string StageKey(double t)
        {
            if (t < -3) return "ui.launch.stage.countdown";
            if (t < 0) return "ui.launch.stage.ignition";
            TimelineEvent last = null;
            foreach (var e in Events) { if (e.T > t) break; if (e.Id != "end") last = e; }
            if (last == null) return "ui.launch.stage.ascent";
            double since = t - last.T;
            switch (last.Id)
            {
                case "maxq": return since < 12 ? "ui.launch.stage.maxq" : "ui.launch.stage.ascent";
                case "mach1": case "booster_cutoff": case "booster_sep": return "ui.launch.stage.ascent";
                case "meco": case "stage_sep": case "upper_sep": return since < 12 ? "ui.launch.stage.staging" : Cruise(t);
                case "fairing": return since < 10 ? "ui.launch.stage.fairing" : Thrusting(t) ? "ui.launch.stage.upper_burn" : "ui.launch.stage.coast";
                case "ses": case "upper_ignite": case "restart": return "ui.launch.stage.upper_burn";
                case "battery_swap": return Thrusting(t) ? "ui.launch.stage.upper_burn" : "ui.launch.stage.coast";
                case "seco": case "upper_cutoff": case "seco2": case "coast": return Cruise(t);
                case "deploy": return "ui.launch.stage.deploy";
                case "anomaly": case "underspeed": case "fairing_fail": return "ui.launch.stage.anomaly";
                case "eclipse": return "ui.launch.stage.eclipse";
                case "signal_lost": return "ui.launch.stage.signal_lost";
            }
            return "ui.launch.stage.ascent";
        }

        /// <summary>沒有新事件時：在燒就是上節燃燒，後面還有點火就是滑行，否則已入軌。</summary>
        string Cruise(double t) => Thrusting(t) ? "ui.launch.stage.upper_burn" : Events.Any(e => e.T > t && IsIgnition(e.Id)) ? "ui.launch.stage.coast" : "ui.launch.stage.orbit";

        static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        static double Lerp(double a, double b, double f) => a + (b - a) * Clamp(f, 0, 1);
    }
}
