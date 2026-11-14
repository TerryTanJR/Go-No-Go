using System;

namespace GoNoGo.Core
{
    /// <summary>
    /// 圓軌道的幾何計算。公式公開在 README 的「公式」一節。
    /// </summary>
    public static class OrbitMath
    {
        /// <summary>軌道週期（分鐘）：T = 2π √(a³/μ)。</summary>
        public static double PeriodMinutes(double altitudeKm, double bodyRadiusKm, double muKm3S2)
        {
            double a = bodyRadiusKm + altitudeKm;
            return 2.0 * Math.PI * Math.Sqrt(a * a * a / muKm3S2) / 60.0;
        }

        /// <summary>
        /// 每圈在本體影子裡的比例（圓柱形影子模型）。
        /// β 是太陽方向和軌道面的夾角；|β| 夠大時整圈都照得到太陽（回傳 0）。
        /// f = (1/π) · acos( √(h² + 2Rh) / ((R + h) · cos β) )
        /// </summary>
        public static double EclipseFraction(double altitudeKm, double bodyRadiusKm, double betaDeg)
        {
            double r = bodyRadiusKm + altitudeKm;
            double beta = Math.Abs(betaDeg) * Math.PI / 180.0;
            double betaStar = Math.Asin(bodyRadiusKm / r); // 超過這個角度就不會進影子
            if (beta >= betaStar) return 0.0;
            double h = altitudeKm;
            double x = Math.Sqrt(h * h + 2.0 * bodyRadiusKm * h) / (r * Math.Cos(beta));
            if (x >= 1.0) return 0.0;
            return Math.Acos(x) / Math.PI;
        }

        /// <summary>火箭方程式：Δv = Isp · g0 · ln(m0 / m1)（m/s）。</summary>
        public static double DeltaV(double ispS, double g0, double wetKg, double propellantKg)
        {
            if (ispS <= 0 || propellantKg <= 0 || wetKg <= propellantKg) return 0.0;
            return ispS * g0 * Math.Log(wetKg / (wetKg - propellantKg));
        }
    }
}
