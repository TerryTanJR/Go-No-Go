using System;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class LaunchTimelineTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        LaunchTimeline Tl(string launcher, string orbit, LaunchOutcome o = LaunchOutcome.Nominal, string reason = "launch.nominal")
        {
            var d = TestData.Balanced();
            d.LauncherId = launcher;
            d.OrbitId = orbit;
            return LaunchTimeline.Build(_data, d, o, reason);
        }

        [Test]
        public void EveryLauncherHasASourcedFlightProfile()
        {
            foreach (var l in _data.Launchers)
            {
                Assert.That(l.Flight.Ascent, Is.Not.Empty, l.Id);
                Assert.That(l.Flight.Sources, Is.Not.Empty, l.Id);
                Assert.That(l.Flight.Ascent.Select(e => e.Id), Does.Contain("meco").And.Contain("ses").And.Contain("fairing").And.Contain("seco"), l.Id);
            }
        }

        [Test]
        public void NominalFlightsReachTheTargetOrbitInRealTime()
        {
            foreach (var l in _data.Launchers)
                foreach (var orbit in l.CapacityKg.Keys)
                {
                    var tl = Tl(l.Id, orbit);
                    string w = l.Id + " → " + orbit;
                    Assert.That(tl.Events.Select(e => e.T), Is.Ordered, w);
                    Assert.That(tl.Events.Last().Id, Is.EqualTo("end"), w);
                    var deploy = tl.First("deploy");
                    Assert.That(deploy, Is.Not.Null, w);
                    Assert.That(deploy.T, Is.GreaterThan(400), w + "：真實的發射到衛星分離至少要好幾分鐘");
                    double ses = tl.First("ses").T, fairing = tl.First("fairing").T;
                    Assert.That(tl.SwapT, Is.GreaterThanOrEqualTo(ses).And.LessThan(fairing), w);
                    Assert.That(tl.Altitude(tl.SwapT), Is.GreaterThan(45), w + "：換到太空場景時天空要夠暗");
                    // 高度一路往上（到入軌為止）
                    double prev = -1;
                    for (double t = 0; t <= tl.First("seco").T; t += 5)
                    {
                        double a = tl.Altitude(t);
                        Assert.That(a, Is.GreaterThanOrEqualTo(prev - 1e-6), $"{w} t={t}");
                        prev = a;
                    }
                    Assert.That(tl.Velocity(deploy.T), Is.GreaterThan(7.0), w);
                    Assert.That(tl.Thrusting(1), Is.True, w);
                    Assert.That(tl.Thrusting(deploy.T + 1), Is.False, w);
                }
        }

        [Test]
        public void RealEventTimesComeFromTheData()
        {
            var f9 = Tl("falcon9", "leo_400");
            Assert.That(f9.First("meco").T, Is.EqualTo(140));
            Assert.That(f9.First("deploy").T, Is.EqualTo(720).Within(1), "直接入軌：約 12 分鐘");
            Assert.That(f9.Thrusting(f9.First("meco").T + 1), Is.False, "一節關機到二節點火之間沒有推力");
            Assert.That(f9.Thrusting(f9.First("ses").T + 1), Is.True);

            // OCO-2：Delta II 7320 在 T+56:15 分離，中間滑行 40 分鐘
            var delta = Tl("delta_ii", "sso_700");
            Assert.That(delta.First("deploy").T, Is.EqualTo(3375));
            Assert.That(delta.First("booster_sep").T, Is.EqualTo(99));
            Assert.That(delta.Events.Any(e => e.Id == "coast"), Is.True);
            Assert.That(delta.StageKey(2000), Is.EqualTo("ui.launch.stage.coast"));

            // Taurus XL：四節，三次節間分離（第一節 + 兩次上節）
            var taurus = Tl("taurus_xl", "sso_700");
            Assert.That(taurus.Stages, Is.EqualTo(4));
            Assert.That(taurus.Events.Count(e => e.Id == "upper_sep"), Is.EqualTo(2));
            Assert.That(taurus.Events.Count(e => e.Id == "upper_ignite") + 1, Is.EqualTo(3), "上面三節各點火一次");
            var lastSep = taurus.Events.Last(e => e.Id == "upper_sep");
            Assert.That(taurus.StageKey(lastSep.T + 60), Is.EqualTo("ui.launch.stage.coast"), "節間分離後長時間滑行要顯示「滑行」");

            // Electron：二節關機後由踢昇級做最後一次點火
            var e1 = Tl("electron", "sso_700");
            Assert.That(e1.First("upper_ignite").Call, Is.EqualTo("ui.launch.call.kick_ignite"));
            Assert.That(e1.Thrusting(1900), Is.True);
        }

        [Test]
        public void TelemetryLooksLikeARealAscent()
        {
            var f9 = Tl("falcon9", "leo_400");
            TestContext.WriteLine($"swap={f9.SwapT:0} alt30={f9.Altitude(30):0.0} alt60={f9.Altitude(60):0.0} v60={f9.Velocity(60):0.00} meco={f9.Altitude(140):0} v={f9.Velocity(140):0.00}");
            Assert.That(f9.Altitude(10), Is.InRange(0.05, 0.6), "T+10 還在幾百公尺內");
            Assert.That(f9.Altitude(60), Is.InRange(5.0, 12.0));
            Assert.That(f9.Velocity(140), Is.InRange(2.0, 2.6), "一節關機約 2.3 km/s");
            Assert.That(f9.SwapT, Is.InRange(150.0, 190.0)); // 二節點火後、拋整流罩前（Transporter-10：T+2:31、T+3:02）
            // 上升段速度不會倒退（滑行時持平）
            double prev = 0;
            for (double t = 0; t < 497; t += 2) { var v = f9.Velocity(t); Assert.That(v, Is.GreaterThanOrEqualTo(prev - 0.03), "t=" + t); prev = v; }
        }

        /// <summary>關機後的滑行：高度沿轉移橢圓慢慢爬上去、速度跟著變慢，不是再點火那幾秒才跳上去。</summary>
        [Test]
        public void CoastsClimbAlongTheTransferOrbit()
        {
            // Transporter：SECO-1 進 200 × 700 km 的橢圓，滑行約 42 分鐘到遠地點，再點火 4 秒圓化
            var sso = Tl("falcon9", "sso_700");
            double seco = sso.First("seco").T, restart = sso.First("restart").T;
            Assert.That(sso.Altitude((seco + restart) / 2), Is.InRange(350.0, 550.0), "滑行到一半時在半路上");
            Assert.That(sso.Altitude(restart), Is.EqualTo(700).Within(1));
            Assert.That(sso.Velocity(seco), Is.EqualTo(7.93).Within(0.01), "近地點速度");
            Assert.That(sso.Velocity(restart), Is.EqualTo(7.37).Within(0.01), "遠地點速度");
            Assert.That(sso.Velocity(sso.First("seco2").T), Is.EqualTo(LaunchTimeline.CircularVelocity(700)).Within(0.01));

            // 地球同步轉移：關機後 5 分鐘約 540 km（克卜勒方程式），不是線性往上衝
            var gto = Tl("falcon9", "geo");
            double dep = gto.First("deploy").T;
            Assert.That(gto.Altitude(dep), Is.InRange(450.0, 650.0));
            Assert.That(gto.Velocity(dep), Is.InRange(9.8, 10.1));

            // Taurus：倒數第二節燒完時速度還不夠入軌，不會已經在目標高度；滑行到目標高度才點最後一節
            var taurus = Tl("taurus_xl", "sso_700");
            Assert.That(taurus.Altitude(taurus.Events.Last(e => e.Id == "upper_cutoff").T), Is.LessThan(250));
            Assert.That(taurus.Altitude(taurus.Events.Last(e => e.Id == "upper_ignite").T), Is.EqualTo(700).Within(1));
        }

        [Test]
        public void DeployCallMatchesTheSolarArray()
        {
            var d = TestData.Balanced();
            d.OrbitId = "sso_700";
            d.Parts[PartCategory.Solar] = "solar_deployable";
            Assert.That(LaunchTimeline.Build(_data, d, LaunchOutcome.Nominal, "launch.nominal").First("deploy").Call, Is.EqualTo("ui.launch.call.deploy"));
            d.Parts[PartCategory.Solar] = "solar_body";
            Assert.That(LaunchTimeline.Build(_data, d, LaunchOutcome.Nominal, "launch.nominal").First("deploy").Call, Is.EqualTo("ui.launch.call.deploy_fixed"), "機身貼片沒有東西要展開");
            d.OrbitId = "geo";
            d.LauncherId = "falcon9";
            Assert.That(LaunchTimeline.Build(_data, d, LaunchOutcome.Nominal, "launch.nominal").First("deploy").Call, Is.EqualTo("ui.launch.call.deploy_gto"));
        }

        /// <summary>月球任務：衛星分離後還有地月轉移（約 5 天，縮時播放）、月球軌道進入點火（約 40 分鐘）、被捕獲。</summary>
        [TestCase("falcon9")]
        [TestCase("falcon_heavy")]
        public void LunarFlightCoastsToTheMoonAndIsCaptured(string launcher)
        {
            var tl = Tl(launcher, "llo_100");
            var L = tl.Lunar;
            Assert.That(L, Is.Not.Null);
            Assert.That(tl.Events.Select(e => e.T), Is.Ordered);
            Assert.That(tl.Events.Select(e => e.Id), Is.SupersetOf(new[] { "deploy", "lunar_coast", "loi", "capture", "end" }));
            double deploy = tl.First("deploy").T, loi = tl.First("loi").T, cap = tl.First("capture").T;
            Assert.That(tl.First("lunar_coast").T, Is.GreaterThan(deploy));
            Assert.That(L.TransferDays, Is.InRange(4.5, 5.5), "最省能量的地月轉移約 5 天（LRO 走快一點的路線 4.5 天）");
            Assert.That(cap - loi, Is.EqualTo(2400), "LRO 的月球軌道進入點火約 40 分鐘");
            Assert.That(tl.End, Is.EqualTo(cap + 20));

            // 關機時在近地點，速度和停泊軌道之後的再點火銜接；之後一路變遠、變慢
            Assert.That(tl.Velocity(L.TliT), Is.InRange(10.8, 11.0), "地月轉移需要約 10.9 km/s");
            Assert.That(tl.Velocity(deploy), Is.GreaterThan(7.0));
            double prevA = 0, prevV = 99;
            for (double t = L.TliT; t < loi - 1; t += 1800)
            {
                Assert.That(tl.Altitude(t), Is.GreaterThanOrEqualTo(prevA - 1e-6), "t=" + t);
                Assert.That(tl.Velocity(t), Is.LessThanOrEqualTo(prevV + 1e-6), "t=" + t);
                prevA = tl.Altitude(t); prevV = tl.Velocity(t);
            }
            Assert.That(tl.Altitude(loi - 2), Is.GreaterThan(370000), "抵達前離地球約 38 萬公里");
            Assert.That(tl.Velocity(loi - 2), Is.LessThan(0.3), "遠地點只剩約 0.19 km/s");
            Assert.That(tl.MoonRelative(loi - 2), Is.False);
            Assert.That(tl.MoonRelative(loi), Is.True);
            Assert.That(tl.Altitude(loi + 60), Is.EqualTo(100).Within(1e-6), "抵達後改看離月面的高度");
            Assert.That(tl.Velocity(loi), Is.InRange(2.3, 2.6), "近月點的雙曲線速度");
            Assert.That(tl.Velocity(cap + 5), Is.EqualTo(1.633).Within(0.002), "月面 100 km 圓軌道");
            Assert.That(tl.Thrusting(loi + 60), Is.True, "減速點火有推力");
            Assert.That(tl.Thrusting(cap + 1), Is.False);
            Assert.That(tl.StageKey(L.TliT + 86400), Is.EqualTo("ui.launch.stage.lunar_coast"));
            Assert.That(tl.StageKey(loi + 60), Is.EqualTo("ui.launch.stage.loi"));
            Assert.That(tl.StageKey(cap + 5), Is.EqualTo("ui.launch.stage.lunar_orbit"));

            // 月球在抵達那一刻轉到衛星的位置（真近點角 π）
            L.State(loi - L.TliT, out var r, out _, out var nu);
            Assert.That(r, Is.EqualTo(LunarTransfer.MoonDistanceKm).Within(1));
            Assert.That(nu, Is.EqualTo(L.MoonAngle(loi)).Within(1e-6));
            Assert.That(L.MoonAngle(L.TliT), Is.InRange(Math.PI - 1.3, Math.PI - 0.9), "5 天前月球在後面約 65°");

            // 縮時：滑行和點火各壓縮成幾十秒內，區間不重疊、都在事件之間
            Assert.That(tl.Warps.Count, Is.EqualTo(2));
            Assert.That(tl.Warps.Sum(w => w.Seconds) + (tl.End - tl.Warps.Sum(w => w.To - w.From)), Is.LessThan(tl.First("deploy").T + 120), "縮時之後整段演出只比一般任務多兩分鐘左右");
            Assert.That(tl.WarpRate(L.TliT + 86400), Is.GreaterThan(10000));
            Assert.That(tl.WarpRate(deploy), Is.EqualTo(1));
            Assert.That(tl.NextWarpEdge(deploy), Is.EqualTo(tl.Warps[0].From));
            Assert.That(tl.First("restart").Call, Is.EqualTo("ui.launch.call.tli"));
            Assert.That(tl.First("seco2").Call, Is.EqualTo("ui.launch.call.tli_done"));
            foreach (var e in tl.Events.Where(e => e.Call.Length > 0))
                Assert.That(_data.Strings.ContainsKey(e.Call), e.Call);
            foreach (var id in new[] { "lunar_coast", "loi", "capture" })
                Assert.That(_data.Strings.ContainsKey("ui.launch.ev." + id) && _data.Strings.ContainsKey(tl.StageKey(tl.First(id).T + 1)), id);
        }

        [Test]
        public void LunarConstantsMatchTheGameData()
        {
            Assert.That(LunarTransfer.MoonMu, Is.EqualTo(_data.Constants.MoonMuKm3S2));
            Assert.That(LunarTransfer.MoonRadiusKm, Is.EqualTo(_data.Constants.MoonRadiusKm));
            Assert.That(Tl("falcon9", "leo_400").Lunar, Is.Null);
            Assert.That(Tl("falcon9", "llo_100", LaunchOutcome.DeadOnOrbit, "launch.dead_on_orbit").Lunar, Is.Null, "入軌後就失效的不會飛到月球");
        }

        [Test]
        public void FailuresEndEarly()
        {
            var boom = Tl("falcon9", "leo_400", LaunchOutcome.Failure, "launch.anomaly");
            Assert.That(boom.First("anomaly"), Is.Not.Null);
            Assert.That(boom.First("meco"), Is.Null, "爆炸之後沒有一節關機");
            Assert.That(boom.End, Is.LessThan(100));

            var slow = Tl("electron", "geo", LaunchOutcome.Failure, "launch.unreachable");
            Assert.That(slow.First("underspeed"), Is.Not.Null);
            Assert.That(slow.First("deploy"), Is.Null);
            Assert.That(slow.Velocity(slow.First("underspeed").T), Is.LessThan(7.0));

            var oco = Tl("taurus_xl", "sso_700", LaunchOutcome.FairingFailure, "launch.fairing");
            Assert.That(oco.First("fairing_fail").T, Is.EqualTo(177));
            Assert.That(oco.First("fairing"), Is.Null);
            Assert.That(oco.SwapT, Is.LessThan(177));

            var dead = Tl("falcon9", "sso_700", LaunchOutcome.DeadOnOrbit, "launch.dead_on_orbit");
            Assert.That(dead.First("signal_lost").T, Is.GreaterThan(dead.First("deploy").T));
        }
    }
}
