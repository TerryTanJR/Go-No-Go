using System;
using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// 發射倒數時鐘：畫面上的數字要跟倒數錄音「念出那個數字」的瞬間同時跳。
    /// <para>
    /// 錄音（NASA IM-2 倒數網路）每數一下約 0.93 秒，不是剛好 1 秒；如果遊戲時間照實際秒數走，
    /// 念到「一」時畫面已經差了將近一秒。所以倒數期間的 T 由錄音位置換算：每個數字開口的時間點
    /// 對到 T = −n，中間線性內插；倒數前、零之後照 1:1 走。
    /// </para>
    /// <para>
    /// 顯示用「無條件進位」：T = −10.0 到 −9.0 之間顯示 10。用截斷的話，念「十」的那一刻畫面已經跳到 9。
    /// </para>
    /// </summary>
    public static class LaunchClock
    {
        /// <summary>畫面上的秒數：倒數（T &lt; 0）無條件進位，升空後無條件捨去。</summary>
        public static int DisplaySeconds(double t)
        {
            // 1e-4：避免 −10.00001 這種浮點誤差顯示成 11
            return t < 0 ? (int)Math.Ceiling(-t - 1e-4) : (int)Math.Floor(t);
        }

        /// <summary>T−HH:MM:SS／T+HH:MM:SS。</summary>
        public static string Format(double t)
        {
            int s = DisplaySeconds(t);
            bool minus = t < 0 && s > 0;
            return $"T{(minus ? "-" : "+")}{s / 3600:00}:{s / 60 % 60:00}:{s % 60:00}";
        }

        /// <summary>
        /// 錄音位置（秒）換成任務時間 T。<paramref name="calls"/>[i] 是念出 (n−i) 的時間點，n = calls.Count − 1，
        /// 最後一個是「零」。<paramref name="lead"/> 是從開口到聽得出是哪個字的延遲。
        /// </summary>
        public static double TFromAudio(double audio, IReadOnlyList<float> calls, double lead)
        {
            if (calls == null || calls.Count < 2) throw new ArgumentException("至少要兩個倒數點");
            int n = calls.Count - 1;
            double first = calls[0] + lead, last = calls[n] + lead;
            if (audio <= first) return -n - (first - audio);
            if (audio >= last) return audio - last;
            for (int i = 1; i <= n; i++)
            {
                double a0 = calls[i - 1] + lead, a1 = calls[i] + lead;
                if (audio <= a1) return -(n - i + 1) + (audio - a0) / (a1 - a0);
            }
            return 0;
        }

        /// <summary><see cref="TFromAudio"/> 的反函數：要從 T 開始播時，錄音該從哪裡開始。</summary>
        public static double AudioFromT(double t, IReadOnlyList<float> calls, double lead)
        {
            if (calls == null || calls.Count < 2) throw new ArgumentException("至少要兩個倒數點");
            int n = calls.Count - 1;
            if (t <= -n) return calls[0] + lead - (-n - t);
            if (t >= 0) return calls[n] + lead + t;
            double k = t + n;               // 0..n
            int i = Math.Min((int)Math.Floor(k), n - 1);
            double f = k - i;
            return calls[i] + lead + f * (calls[i + 1] - calls[i]);
        }
    }
}
