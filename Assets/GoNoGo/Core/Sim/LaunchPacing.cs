using System;
using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// 發射演出的播放倍率：整段在固定的真實秒數內播完，有動作的地方慢、沒事的地方快。
    /// <para>
    /// 每個時間點的倍率取兩者較小的：
    /// 「底速」（一節爬升最慢、上節燃燒其次、滑行最快，三者同乘一個係數 k）；
    /// 「事件限速」：分離、點火、拋整流罩、衛星分離前後有一段窗口用指定的慢速，離窗口越遠限速越高，
    /// 而且限速和距離成正比，所以倍率在真實時間裡是照固定比例（<see cref="Ramp"/>）變化的，不會突然暴衝或急煞。
    /// k 用二分法解出來，讓總長剛好等於預算；事件太多、預算不夠時，事件的慢速也一起放大。
    /// </para>
    /// <para>
    /// 失敗的飛行：k 照「成功的飛行」解（plan），出事前的倍率和成功時一樣，不會從倍率看出這次會失敗；
    /// 出事的地方再照失敗事件的窗口放慢，總長會比預算短。
    /// </para>
    /// </summary>
    public sealed class LaunchPacing
    {
        /// <summary>倍率在真實時間裡每秒最多變幾倍的自然對數（2.5 ≈ 每秒 12 倍；×2 到 ×500 約 2.2 秒）。</summary>
        public const double Ramp = 2.5;
        /// <summary>滑行比上節燃燒快幾倍；一節爬升（長焦追蹤鏡頭）比上節燃燒慢多少。</summary>
        const double CoastMul = 6, AscentMul = 0.2;
        const double Dt = 0.25;

        /// <summary>有動作的事件：窗口（事件前、後幾秒）裡的倍率。</summary>
        struct Window { public double From, To, Speed; }

        readonly LaunchTimeline _tl;
        readonly double _from, _end, _meco;
        readonly List<Window> _windows = new List<Window>();
        double _k = 1, _m = 1;
        double[] _left; // 每個取樣點到結束還要幾個真實秒（照計畫）

        public double End => _end;

        /// <param name="from">從這個任務時間開始加速（之前是原速）。</param>
        /// <param name="budget">從 from 播到結束可以用的真實秒數。</param>
        /// <param name="plan">用來解底速的時間軸（失敗的飛行傳成功的版本）；null = 用 tl 本身。</param>
        public LaunchPacing(LaunchTimeline tl, double from, double budget, LaunchTimeline plan = null)
        {
            _tl = tl;
            _from = from;
            _end = Math.Max(from, tl.End);
            _meco = tl.First("meco")?.T ?? tl.First("anomaly")?.T ?? _end;
            // 原速段結束的地方也當成一個窗口：倍率從 1 慢慢爬上去
            _windows.Add(new Window { From = from - 1, To = from, Speed = 1 });
            foreach (var e in tl.Events) AddWindow(e);
            if (plan != null && plan != tl)
            {
                var p = new LaunchPacing(plan, from, budget);
                _k = p._k;
                _m = p._m;
                BuildLeft();
            }
            else Solve(Math.Max(1, budget));
        }

        void AddWindow(TimelineEvent e)
        {
            double pre, post, speed;
            switch (e.Id)
            {
                // 分離、拋整流罩、衛星分離是主要看點；關機、再點火只稍微放慢（每個慢速窗口進出都要花時間減速、加速）
                case "booster_sep": case "stage_sep": case "upper_sep": pre = 2; post = 5; speed = 2; break;
                case "meco": case "ses": pre = 1; post = 3; speed = 3; break;
                case "upper_ignite": case "restart": pre = 1; post = 2; speed = 8; break;
                case "fairing": case "fairing_fail": pre = 1; post = 5; speed = 2; break;
                case "deploy": pre = 2; post = 20; speed = 1.5; break; // 一直慢到結束：衛星分離後慢慢飄開、展開太陽能板
                case "anomaly": pre = 3; post = 8; speed = 1; break;
                case "anomaly_warn": pre = 1; post = 3; speed = 1; break;
                case "underspeed_warn": pre = 1; post = 4; speed = 2; break;
                case "underspeed": pre = 1; post = 12; speed = 1.5; break;
                case "eclipse": case "signal_lost": pre = 1; post = 5; speed = 2; break;
                case "upper_cutoff": case "seco": case "seco2": pre = 1; post = 2; speed = 10; break;
                default: return;
            }
            _windows.Add(new Window { From = e.T - pre, To = e.T + post, Speed = speed });
        }

        double Base(double t)
        {
            if (t < _meco) return AscentMul;
            return _tl.Thrusting(t) ? 1 : CoastMul;
        }

        /// <summary>照計畫的倍率（還沒加上執行時的校正）。</summary>
        public double PlanSpeed(double t)
        {
            if (t < _from) return 1;
            double s = _k * Base(t);
            foreach (var w in _windows)
            {
                double d = t < w.From ? w.From - t : t > w.To ? t - w.To : 0;
                double cap = w.Speed * _m;
                // 限速和距離成正比：ds/dt_real = Ramp × s，倍率在真實時間裡等比例變化
                s = Math.Min(s, cap + Ramp * d);
            }
            return Math.Max(1, s);
        }

        double Cost()
        {
            double sum = 0;
            for (double t = _from; t < _end; t += Dt) sum += Math.Min(Dt, _end - t) / PlanSpeed(t);
            return sum;
        }

        void Solve(double budget)
        {
            // 先只調底速；k 再大都超過預算，表示事件太多，事件的慢速也一起放大
            _m = 1;
            _k = 1e5;
            if (Cost() > budget)
            {
                double lo = 0, hi = Math.Log(1e4);
                for (int i = 0; i < 40; i++) { double mid = (lo + hi) / 2; _m = Math.Exp(mid); if (Cost() > budget) lo = mid; else hi = mid; }
                _m = Math.Exp(hi);
            }
            else
            {
                double lo = Math.Log(1e-3), hi = Math.Log(1e5);
                for (int i = 0; i < 40; i++) { double mid = (lo + hi) / 2; _k = Math.Exp(mid); if (Cost() > budget) lo = mid; else hi = mid; }
                _k = Math.Exp(hi);
            }
            BuildLeft();
        }

        void BuildLeft()
        {
            int n = (int)Math.Ceiling((_end - _from) / Dt) + 1;
            _left = new double[n + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                double t = _from + i * Dt;
                _left[i] = _left[i + 1] + (t < _end ? Math.Min(Dt, _end - t) / PlanSpeed(t) : 0);
            }
        }

        /// <summary>照計畫從 t 播到結束還要幾個真實秒。</summary>
        public double PlannedLeft(double t)
        {
            if (t <= _from) return _left[0];
            double x = (t - _from) / Dt;
            int i = Math.Min(_left.Length - 2, (int)x);
            return _left[i] + (_left[i + 1] - _left[i]) * (x - i);
        }

        /// <summary>
        /// 執行時的倍率：照計畫，再依「計畫剩下的秒數 / 實際剩下的秒數」校正（掉幀、錄音慢了都會補回來）。
        /// </summary>
        public double Speed(double t, double realLeft)
        {
            double plan = PlanSpeed(t);
            if (t < _from) return plan;
            double fix = PlannedLeft(t) / Math.Max(0.5, realLeft);
            return Math.Max(1, plan * Math.Min(3, Math.Max(0.5, fix)));
        }

        /// <summary>照計畫播完的真實秒數（測試用）。</summary>
        public double PlannedTotal => _left[0];
    }
}
