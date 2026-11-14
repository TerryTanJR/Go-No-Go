using System;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    public class OrbitMathTests
    {
        const double Re = 6378.137, MuE = 398600.4418, Rm = 1737.4, MuM = 4902.800066;

        [Test]
        public void IssAltitudePeriodIsAbout92_6Minutes()
        {
            Assert.That(OrbitMath.PeriodMinutes(400, Re, MuE), Is.EqualTo(92.56).Within(0.05));
        }

        [Test]
        public void GeostationaryPeriodIsOneSiderealDay()
        {
            Assert.That(OrbitMath.PeriodMinutes(35786, Re, MuE), Is.EqualTo(1436.1).Within(0.2));
        }

        [Test]
        public void OcoOrbitPeriodMatchesPressKit99Minutes()
        {
            // OCO 新聞資料袋：「orbiting Earth once every 99 minutes」
            Assert.That(OrbitMath.PeriodMinutes(705, Re, MuE), Is.EqualTo(98.8).Within(0.3));
        }

        [Test]
        public void LunarOrbitPeriodAndEclipse()
        {
            double t = OrbitMath.PeriodMinutes(100, Rm, MuM);
            Assert.That(t, Is.EqualTo(117.8).Within(0.2));
            Assert.That(t * OrbitMath.EclipseFraction(100, Rm, 0), Is.EqualTo(46.5).Within(0.3));
        }

        [Test]
        public void EclipseAtBetaZeroMatchesAsinFormula()
        {
            // arXiv 2007.03675 Eq. 38–39：Te = T · asin(R/(R+h)) / π
            foreach (var h in new[] { 400.0, 700.0, 35786.0 })
            {
                double expected = Math.Asin(Re / (Re + h)) / Math.PI;
                Assert.That(OrbitMath.EclipseFraction(h, Re, 0), Is.EqualTo(expected).Within(1e-9), $"h={h}");
            }
            double iss = OrbitMath.PeriodMinutes(400, Re, MuE) * OrbitMath.EclipseFraction(400, Re, 0);
            Assert.That(iss, Is.EqualTo(36.1).Within(0.2));
        }

        [Test]
        public void EclipseShrinksWithBetaAndVanishesAboveBetaStar()
        {
            double f0 = OrbitMath.EclipseFraction(700, Re, 0);
            double f25 = OrbitMath.EclipseFraction(700, Re, 25);
            double f60 = OrbitMath.EclipseFraction(700, Re, 60);
            Assert.That(f25, Is.LessThan(f0));
            Assert.That(f60, Is.LessThan(f25));
            double betaStar = Math.Asin(Re / (Re + 700)) * 180 / Math.PI;
            Assert.That(OrbitMath.EclipseFraction(700, Re, betaStar + 0.1), Is.EqualTo(0));
            Assert.That(OrbitMath.EclipseFraction(700, Re, -25), Is.EqualTo(f25), "β 的正負號不影響");
        }

        [Test]
        public void RocketEquation()
        {
            double dv = OrbitMath.DeltaV(220, 9.80665, 150, 10.7);
            Assert.That(dv, Is.EqualTo(220 * 9.80665 * Math.Log(150 / 139.3)).Within(1e-9));
            Assert.That(dv, Is.EqualTo(159.9).Within(0.5));
            Assert.That(OrbitMath.DeltaV(220, 9.80665, 150, 0), Is.EqualTo(0));
            Assert.That(OrbitMath.DeltaV(0, 9.80665, 150, 10), Is.EqualTo(0));
            Assert.That(OrbitMath.DeltaV(220, 9.80665, 10, 10), Is.EqualTo(0), "全部是推進劑時不合理，回傳 0");
        }
    }
}
