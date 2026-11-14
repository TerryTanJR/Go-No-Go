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
    /// 月球任務：lunar_coast（地月轉移滑行）、loi（月球軌道進入點火）、capture（被月球捕獲）；
    /// 失敗：anomaly（爆炸）、fairing_fail、underspeed；入軌後失效：eclipse、signal_lost；最後 end。
    /// </para>
    /// </summary>
    public sealed class LaunchTimeline
    {
        public List<TimelineEvent> Events { get; } = new List<TimelineEvent>();
        public double End { get; private set; }
        /// <summary>月球任務的地月轉移（其他軌道是 null）。</summary>
        public LunarTransfer Lunar { get; private set; }
        /// <summary>縮時播放的區間：地月轉移要飛好幾天，演出時壓縮成幾十秒（任務時鐘照樣走真實時間）。</summary>
        public List<TimeWarp> Warps { get; } = new List<TimeWarp>();
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
            var tl = Build(prof, post, orbit?.Id ?? d.OrbitId ?? "", orbit?.AltitudeKm ?? 500, outcome, reasonCode, orbit?.Body == "moon");
            // 機身貼片的太陽能板沒有東西要展開：分離播報改成「開始發電」
            var solar = data.Part(d.Get(PartCategory.Solar));
            if (solar != null && solar.Mount != "wing")
                foreach (var e in tl.Events)
                    if (e.Id == "deploy" && e.Call == "ui.launch.call.deploy") e.Call = "ui.launch.call.deploy_fixed";
            return tl;
        }

        public static LaunchTimeline Build(FlightProfileDef prof, List<FlightEventDef> post, string orbitId, double orbitAltKm, LaunchOutcome outcome, string reasonCode, bool lunar = false)
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

                // 月球任務：地月轉移點火之後還要飛好幾天，抵達月球時再點火減速才會被捕獲
                if (lunar && outcome != LaunchOutcome.DeadOnOrbit)
                {
                    var tli = all.Where(e => IsCutoff(e.Id) && e.T <= deploy).OrderBy(e => e.T).LastOrDefault();
                    var relight = tli == null ? null : all.Where(e => IsIgnition(e.Id) && e.T < tli.T).OrderBy(e => e.T).LastOrDefault();
                    if (relight != null && relight.T > seco) relight.Call = "ui.launch.call.tli";
                    if (tli != null) tli.Call = "ui.launch.call.tli_done";
                    var tr = new LunarTransfer(tli?.T ?? seco, orbitAltKm);
                    tl.Lunar = tr;
                    double coast = deploy + 15;
                    all.Add(new TimelineEvent { Id = "lunar_coast", T = coast, Call = "ui.launch.call.lunar_coast" });
                    all.Add(new TimelineEvent { Id = "loi", T = tr.ArriveT, Call = "ui.launch.call.loi" });
                    all.Add(new TimelineEvent { Id = "capture", T = tr.CaptureT, Call = "ui.launch.call.capture" });
                    tl.End = tr.CaptureT + 20;
                    // 約 5 天的滑行用 22 秒播完、40 分鐘的減速點火用 8 秒；前後各留幾秒照真實時間播
                    tl.Warps.Add(new TimeWarp { From = coast + 3, To = tr.ArriveT - 6, Seconds = 22 });
                    tl.Warps.Add(new TimeWarp { From = tr.ArriveT + 4, To = tr.CaptureT - 4, Seconds = 8 });
                }
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

        public static bool IsCutoff(string id) => id == "meco" || id == "upper_cutoff" || id == "seco" || id == "seco2" || id == "underspeed" || id == "capture";
        public static bool IsIgnition(string id) => id == "ses" || id == "upper_ignite" || id == "restart" || id == "loi";

        public TimelineEvent First(string id) => Events.FirstOrDefault(e => e.Id == id);

        /// <summary>這個時間的縮時倍率（任務秒 / 播放秒）；不在縮時區間是 1。</summary>
        public double WarpRate(double t)
        {
            foreach (var w in Warps) if (t >= w.From && t < w.To) return w.Rate;
            return 1;
        }

        /// <summary>t 之後最近的縮時區間邊界（快轉時一步不能跨過去，否則跨過去的那段倍率會算錯）。</summary>
        public double NextWarpEdge(double t)
        {
            double best = double.MaxValue;
            foreach (var w in Warps)
            {
                if (w.From > t) best = Math.Min(best, w.From);
                if (w.To > t) best = Math.Min(best, w.To);
            }
            return best;
        }

        /// <summary>抵達月球之後，遙測的高度、速度改成相對月球。</summary>
        public bool MoonRelative(double t) => Lunar != null && t >= Lunar.ArriveT;

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
            if (Lunar != null)
            {
                LunarTelemetry(tSeco, a, v);
                return;
            }
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

        /// <summary>
        /// 月球任務入軌之後：停泊軌道滑行到地月轉移點火；關機後沿轉移橢圓飛（克卜勒方程式，離地球越遠飛得越慢），
        /// 抵達時高度、速度改成相對月球（近月點 100 km 的雙曲線速度），減速點火後是 100 km 圓軌道的速度。
        /// </summary>
        void LunarTelemetry(double tSeco, double a, double v)
        {
            var L = Lunar;
            foreach (var e in Events.Where(x => x.T > tSeco && x.T < L.TliT && IsIgnition(x.Id))) Key(e.T, a, v);
            // 取樣點在偏近點角上平均分布：離地球近的時候變化快，點自然比較密
            const int n = 48;
            for (int i = 0; i < n; i++)
            {
                double tau = L.TimeFromEccentricAnomaly(Math.PI * i / n);
                L.State(tau, out var r, out var s, out _);
                Key(L.TliT + tau, r - LunarTransfer.EarthRadiusKm, s);
            }
            L.State(L.ArriveT - 1 - L.TliT, out var r1, out var s1, out _);
            Key(L.ArriveT - 1, r1 - LunarTransfer.EarthRadiusKm, s1);
            Key(L.ArriveT, L.OrbitAltKm, L.ArrivalSpeedKms);
            Key(L.CaptureT, L.OrbitAltKm, L.OrbitSpeedKms);
            Key(End, L.OrbitAltKm, L.OrbitSpeedKms);
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
                case "lunar_coast": return "ui.launch.stage.lunar_coast";
                case "loi": return "ui.launch.stage.loi";
                case "capture": return "ui.launch.stage.lunar_orbit";
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

    /// <summary>縮時區間：任務時間 From → To 用 Seconds 秒播完。</summary>
    public sealed class TimeWarp
    {
        public double From { get; set; }
        public double To { get; set; }
        public double Seconds { get; set; }
        public double Rate => Seconds > 0 ? Math.Max(1, (To - From) / Seconds) : 1;
    }

    /// <summary>
    /// 地月轉移：地月轉移點火（TLI）關機時在近地點，沿一條遠地點在月球距離的橢圓飛半圈（霍曼轉移，最省能量，約 5 天；
    /// LRO 2009 年走比較快的路線，6 月 18 日發射、23 日抵達）。抵達時月球剛好轉到遠地點的位置，
    /// 再點火約 40 分鐘減速（LRO 的月球軌道進入點火 2009-06-23 09:47–10:26 UTC）被月球捕獲。
    /// 位置與速度都由克卜勒方程式算，演出的畫面和 HUD 的遙測用同一套數字。
    /// 來源（也列在 docs/DATA_SOURCES.md）：
    ///   LRO 發射與 TLI：https://spaceflightnow.com/atlas/av020/av020status.html
    ///   LRO 抵達、LOI 點火時間：https://www.lroc.asu.edu/news/146 、https://www.lroc.asu.edu/news/147
    ///   LRO 轉移軌道動畫：https://svs.gsfc.nasa.gov/10445
    ///   地月距離、恆星月：https://nssdc.gsfc.nasa.gov/planetary/factsheet/moonfact.html
    /// </summary>
    public sealed class LunarTransfer
    {
        // 地球：和 LaunchTimeline 用同一組；月球：constants.json 的 moon_mu_km3_s2、moon_radius_km（JPL DE440、NSSDCA）
        public const double EarthMu = 398600.4418, EarthRadiusKm = 6371.0;
        public const double MoonMu = 4902.800066, MoonRadiusKm = 1737.4;
        /// <summary>地月平均距離（NSSDCA Moon Fact Sheet 的半長軸）。</summary>
        public const double MoonDistanceKm = 384400;
        /// <summary>月球繞地球一圈（恆星月，NSSDCA）。</summary>
        public const double MoonPeriodDays = 27.3217;
        /// <summary>地月轉移點火關機時的高度（和時間表的關機高度一致）。</summary>
        public const double PerigeeAltKm = 260;
        /// <summary>月球軌道進入點火的長度：LRO 約 40 分鐘。</summary>
        public const double CaptureBurnS = 2400;

        public double TliT { get; }
        public double ArriveT { get; }
        public double CaptureT { get; }
        public double OrbitAltKm { get; }
        public double SemiMajorKm { get; }
        public double Eccentricity { get; }
        /// <summary>平均角速度（rad/s）。</summary>
        public double MeanMotion { get; }

        public LunarTransfer(double tliT, double orbitAltKm)
        {
            TliT = tliT;
            OrbitAltKm = orbitAltKm > 0 ? orbitAltKm : 100;
            double rp = EarthRadiusKm + PerigeeAltKm, ra = MoonDistanceKm;
            SemiMajorKm = (rp + ra) / 2;
            Eccentricity = (ra - rp) / (ra + rp);
            MeanMotion = Math.Sqrt(EarthMu / (SemiMajorKm * SemiMajorKm * SemiMajorKm));
            ArriveT = tliT + Math.PI / MeanMotion;
            CaptureT = ArriveT + CaptureBurnS;
        }

        public double TransferDays => (ArriveT - TliT) / 86400.0;

        /// <summary>從近地點飛到偏近點角 E 要多久（秒）。</summary>
        public double TimeFromEccentricAnomaly(double e) => (e - Eccentricity * Math.Sin(e)) / MeanMotion;

        /// <summary>過近地點 tau 秒後的位置：離地心距離、相對地球的速度、真近點角（0 = 近地點、π = 抵達月球）。</summary>
        public void State(double tau, out double rKm, out double speedKms, out double trueAnomaly)
        {
            AtEccentricAnomaly(EccentricAnomaly(tau), out rKm, out trueAnomaly);
            speedKms = Math.Sqrt(EarthMu * (2 / rKm - 1 / SemiMajorKm));
        }

        /// <summary>過近地點 tau 秒後的偏近點角（0..π）。</summary>
        public double EccentricAnomaly(double tau)
        {
            double m = Math.Max(0, Math.Min(Math.PI, MeanMotion * Math.Max(0, tau)));
            double e = Eccentricity;
            // 牛頓法從 π 開始：f(E) = E − e·sinE − M 在 [0, π] 遞增且下凸，從右邊出發一定單調收斂（e≈0.97 也一樣）
            double E = Math.PI;
            for (int i = 0; i < 60; i++)
            {
                double f = E - e * Math.Sin(E) - m, d = 1 - e * Math.Cos(E);
                double step = f / Math.Max(1e-9, d);
                E = Math.Max(0, E - step);
                if (Math.Abs(step) < 1e-12) break;
            }
            return E;
        }

        /// <summary>偏近點角 E 的位置：離地心距離與真近點角。</summary>
        public void AtEccentricAnomaly(double E, out double rKm, out double trueAnomaly)
        {
            double e = Eccentricity;
            rKm = SemiMajorKm * (1 - e * Math.Cos(E));
            trueAnomaly = 2 * Math.Atan2(Math.Sqrt(1 + e) * Math.Sin(E / 2), Math.Sqrt(1 - e) * Math.Cos(E / 2));
        }

        /// <summary>月球在轉移軌道平面上的角度（和真近點角同一個基準）：抵達那一刻剛好在 π。</summary>
        public double MoonAngle(double t) => Math.PI - 2 * Math.PI / (MoonPeriodDays * 86400) * (ArriveT - t);

        /// <summary>抵達時相對月球的剩餘速度（月球公轉速度減掉衛星在遠地點的速度）。</summary>
        public double ExcessSpeedKms
        {
            get
            {
                double vMoon = Math.Sqrt(EarthMu / MoonDistanceKm);
                double vApo = Math.Sqrt(EarthMu * (2 / MoonDistanceKm - 1 / SemiMajorKm));
                return vMoon - vApo;
            }
        }

        /// <summary>落到近月點（目標高度）時相對月球的速度：剩餘速度再加上月球重力加速。</summary>
        public double ArrivalSpeedKms => Math.Sqrt(ExcessSpeedKms * ExcessSpeedKms + 2 * MoonMu / (MoonRadiusKm + OrbitAltKm));

        /// <summary>目標高度的月球圓軌道速度。</summary>
        public double OrbitSpeedKms => Math.Sqrt(MoonMu / (MoonRadiusKm + OrbitAltKm));
    }
}
