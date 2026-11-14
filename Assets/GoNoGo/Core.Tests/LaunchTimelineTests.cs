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
