using System;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class EvaluatorTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        [Test]
        public void MassIsPartsPlusStructurePlusPropellant()
        {
            var d = TestData.Balanced();
            d.Parts[PartCategory.Propulsion] = "prop_green";
            var ev = Evaluator.EvaluateDesign(d, _data);
            double parts = d.AllPartIds().Sum(id => _data.Part(id).MassKg);
            double dry = parts / (1 - _data.Constants.StructureFraction);
            Assert.That(ev.Mass.PartsKg, Is.EqualTo(parts).Within(1e-9));
            Assert.That(ev.Mass.DryKg, Is.EqualTo(dry).Within(1e-9));
            Assert.That(ev.Mass.WetKg, Is.EqualTo(dry + 9.7).Within(1e-9));
            Assert.That(ev[ResourceId.Mass].Capacity, Is.EqualTo(150));
            Assert.That(ev[ResourceId.Mass].Margin, Is.EqualTo((150 - ev.Mass.WetKg) / 150).Within(1e-9));
        }

        [Test]
        public void SolarArraySizingFollowsSmad()
        {
            var d = TestData.Balanced();
            var ev = Evaluator.EvaluateDesign(d, _data);
            var c = _data.Constants;
            double te = ev.Orbit.EclipseMin, td = ev.Orbit.SunlitMin;
            // 日照與影子負載：光學儀器只在日照段耗電
            double tx = 25.0 * ev.Orbit.ContactMinPerDay / 1440.0 + 1.0;
            double always = c.BusBasePowerW + 25 /*熱紅外*/ + 4 /*電腦*/ + 30 /*反應輪*/ + tx;
            double pd = always + 5.8 /*多光譜*/;
            double pe = always;
            double required = (pe * te / 0.6 + pd * td / 0.8) / td;
            Assert.That(ev.Power.RequiredArrayW, Is.EqualTo(required).Within(1e-6));
            double eol = 1.2 * c.SolarArrayWPerM2 * Math.Pow(1 - c.SolarDegradationPerYear, 3);
            Assert.That(ev.Power.ArrayEolW, Is.EqualTo(eol).Within(1e-6));
            double needWh = pe * te / 60.0 / (0.3 * 0.9);
            Assert.That(ev.Power.BatteryRequiredWh, Is.EqualTo(needWh).Within(1e-6));
        }

        [Test]
        public void OcoDownlinkMatchesFormula()
        {
            // 150 Mbit/s × (11 次 × 8 分鐘) × 60 / 8 / 1000 × 0.8 = 79.2 GB/天
            var ev = Evaluator.EvaluateDesign(_data.Scenario("hist_oco").DefaultDesign, _data);
            Assert.That(ev.Orbit.ContactMinPerDay, Is.EqualTo(88));
            Assert.That(ev.Data.DownlinkGbPerDay, Is.EqualTo(79.2).Within(1e-9));
        }

        [Test]
        public void StorageNeedFollowsLongestGap()
        {
            var d = TestData.CheapSmallsat();
            d.Parts[PartCategory.GroundStation] = "gs_1";
            var ev = Evaluator.EvaluateDesign(d, _data);
            double gap = 2.0 * 24.0 / 11.0;              // 11 次過境，最長空檔是平均的兩倍
            Assert.That(ev.Data.StorageNeededGb, Is.EqualTo(33 * gap / 24 * 1.5).Within(1e-9));
            Assert.That(ev.Data.StorageRawGb, Is.EqualTo(33 * gap / 24).Within(1e-9));
            // 6.0 GB 存得下 8 GB，但低於含安全係數的 9.0 GB → 黃燈，不掉資料
            Assert.That(ev[ResourceId.Data].Status, Is.EqualTo(ResourceStatus.Tight), "8 GB 存得下 4.4 小時的影像，但沒有安全餘裕");
            Assert.That(ev.Warnings.Select(w => w.Code), Does.Contain("warn.storage_tight"));
            Assert.That(ev.Data.ReturnFraction, Is.EqualTo(1.0).Within(1e-9));

            // 換成資料量更大的高光譜（80 GB/天 → 空檔 14.5 GB）就真的存不下：紅燈、會掉資料
            d.Instruments[0] = "inst_hyperspectral";
            ev = Evaluator.EvaluateDesign(d, _data);
            Assert.That(ev[ResourceId.Data].Status, Is.EqualTo(ResourceStatus.Over), "8 GB 存不下 14.5 GB");
            Assert.That(ev.Warnings.Select(w => w.Code), Does.Contain("warn.storage"));
            double linkFrac = Math.Min(1, ev.Data.DownlinkGbPerDay / 80); // S 頻段 + 一座站也下傳不完
            Assert.That(ev.Data.ReturnFraction, Is.EqualTo(linkFrac * 8 / (80 * gap / 24)).Within(1e-9));
        }

        [Test]
        public void BudgetIsDevelopmentLaunchAndOperations()
        {
            var d = TestData.HighScience();
            var ev = Evaluator.EvaluateDesign(d, _data);
            var c = _data.Constants;
            double dev = c.BusBaseCostMusd + d.AllPartIds().Sum(id => _data.Part(id).CostMusd);
            double ops = (c.OpsBaseCostMusdPerYear + 2.1) * 3;
            Assert.That(ev.Cost.TotalMusd, Is.EqualTo(dev + 69.85 + ops).Within(1e-9));
            Assert.That(ev[ResourceId.Budget].Capacity, Is.EqualTo(210), "地球觀測：預算跟著關卡走（原本 140 × 學員 1.5）");
        }

        [Test]
        public void ScheduleIsSlowestItemPlusIntegration()
        {
            var ev = Evaluator.EvaluateDesign(TestData.HighScience(), _data);
            Assert.That(ev.ScheduleMonths, Is.EqualTo(28 + 8), "高光譜儀要 28 個月");
        }

        [Test]
        public void LowOrbitWithoutPropulsionHasShortLife()
        {
            var d = TestData.Balanced();
            d.OrbitId = "leo_400";
            var ev = Evaluator.EvaluateDesign(d, _data);
            Assert.That(ev.Lifetime.Limit, Is.EqualTo(LifetimeLimit.Propellant));
            Assert.That(ev.Lifetime.Years, Is.EqualTo(1.5).Within(1e-9));
            Assert.That(ev.Warnings.Select(w => w.Code), Does.Contain("warn.lifetime.propellant"));

            d.Parts[PartCategory.Propulsion] = "prop_green";
            var ev2 = Evaluator.EvaluateDesign(d, _data);
            Assert.That(ev2.Lifetime.Years, Is.GreaterThan(3), "帶推進劑就能撐過 3 年");
        }

        [Test]
        public void GeoRadiationKillsCommercialComputer()
        {
            var d = TestData.Balanced();
            d.OrbitId = "geo";
            d.LauncherId = "falcon9";
            var ev = Evaluator.EvaluateDesign(d, _data);
            Assert.That(ev.Lifetime.RadiationYears, Is.EqualTo(30.0 / 30.0).Within(1e-9));
            d.Parts[PartCategory.Computer] = "obc_radhard";
            d.Parts[PartCategory.Shielding] = "shield_vault";
            var ev2 = Evaluator.EvaluateDesign(d, _data);
            Assert.That(ev2.Lifetime.RadiationYears, Is.EqualTo(100.0 / 15.0).Within(1e-9));
        }

        [Test]
        public void UnreachableOrbitIsFlagged()
        {
            var d = TestData.Balanced();
            d.OrbitId = "geo"; // Electron 到不了地球同步軌道
            var ev = Evaluator.EvaluateDesign(d, _data);
            Assert.That(ev.Mass.OrbitReachable, Is.False);
            Assert.That(ev[ResourceId.Mass].Status, Is.EqualTo(ResourceStatus.Over));
            Assert.That(ev.Warnings.Select(w => w.Code), Does.Contain("warn.orbit_unreachable"));
            Assert.That(ev.LaunchFeasible, Is.False);
        }

        [Test]
        public void SunlitOnlyInstrumentsDoNotLoadTheBattery()
        {
            var a = TestData.Balanced();
            var b = a.Clone();
            b.Instruments = new System.Collections.Generic.List<string> { "inst_thermal" };
            var evA = Evaluator.EvaluateDesign(a, _data);
            var evB = Evaluator.EvaluateDesign(b, _data);
            Assert.That(evA.Power.EclipseEnergyWh, Is.EqualTo(evB.Power.EclipseEnergyWh).Within(1e-9), "多光譜只在白天運作");
        }

        [Test]
        public void ScienceUsesObjectiveAndOrbitMatch()
        {
            var d = TestData.Balanced();
            var ev = Evaluator.EvaluateDesign(d, _data);
            Assert.That(ev.SciencePerYear, Is.EqualTo(40 * 1.0 + 35 * 0.9).Within(1e-9));
            d.OrbitId = "leo_400";
            d.LauncherId = "electron";
            Assert.That(Evaluator.EvaluateDesign(d, _data).SciencePerYear, Is.EqualTo((40 + 31.5) * 0.85).Within(1e-9));
        }

        [Test]
        public void EvaluationIsPureAndRepeatable()
        {
            var d = TestData.HighScience();
            var a = Newtonsoft.Json.JsonConvert.SerializeObject(Evaluator.EvaluateDesign(d, _data));
            var b = Newtonsoft.Json.JsonConvert.SerializeObject(Evaluator.EvaluateDesign(d.Clone(), _data));
            Assert.That(b, Is.EqualTo(a));
        }

        [Test]
        public void CycleLifeInterpolatesInLogSpace()
        {
            var c = _data.Constants;
            Assert.That(Evaluator.CycleLife(c, 0.05), Is.EqualTo(100000));
            Assert.That(Evaluator.CycleLife(c, 0.3), Is.EqualTo(30000).Within(1e-6));
            Assert.That(Evaluator.CycleLife(c, 0.25), Is.EqualTo(Math.Sqrt(60000.0 * 30000.0)).Within(1e-6));
            Assert.That(Evaluator.CycleLife(c, 2.0), Is.EqualTo(800));
        }
    }
}
