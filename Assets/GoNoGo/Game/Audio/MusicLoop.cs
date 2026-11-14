using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 背景音樂：程式合成一段 32 秒、可以無縫循環的太空氛圍音樂（不用音檔，也沒有授權問題）。
    /// 四個和弦各 8 秒（Am9 → Fmaj7 → Cadd9 → Em7），和弦之間交叉淡化；上面是慢慢的琶音、下面是低音，
    /// 最後加一點回聲。每個聲部的相位都從自己那段的開頭算，所以循環接縫聽不出來。
    /// 用 22050 Hz 單聲道：墊子音色不需要高頻，合成時間和記憶體都省一半。
    /// </summary>
    public static class MusicLoop
    {
        const int Rate = 22050;
        const float ChordSec = 8f;
        const float Fade = 1.6f;

        // 每個和弦：低音（根音）與墊子的音（Hz）
        static readonly float[] Roots = { 55.00f, 43.65f, 65.41f, 41.20f };
        static readonly float[][] Pads =
        {
            new[] { 164.81f, 220.00f, 246.94f, 261.63f, 329.63f },  // Am9
            new[] { 130.81f, 174.61f, 220.00f, 261.63f, 329.63f },  // Fmaj7
            new[] { 130.81f, 196.00f, 293.66f, 329.63f, 392.00f },  // Cadd9
            new[] { 164.81f, 196.00f, 246.94f, 293.66f, 392.00f },  // Em7
        };
        // 琶音每 0.5 秒一個音，挑和弦內音的順序
        static readonly int[] Arp = { 0, 2, 4, 3, 1, 3, 4, 2 };

        public static AudioClip Build()
        {
            int chords = Pads.Length;
            float loop = chords * ChordSec;
            int n = Mathf.RoundToInt(loop * Rate);
            var dry = new float[n];

            for (int c = 0; c < chords; c++)
            {
                float start = c * ChordSec;
                var pad = Pads[c];
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate;
                    // 這個和弦自己的時間：前面淡入的那段會繞到 loop 的尾巴
                    float x = Mathf.Repeat(t - start, loop);
                    if (x > loop - Fade) x -= loop;
                    float w = Window(x);
                    if (w <= 0) continue;

                    float s = 0;
                    foreach (var f in pad)
                    {
                        // 兩個稍微走音的振盪器：聽起來比較寬、比較像合成器墊子
                        s += Mathf.Sin(2 * Mathf.PI * f * x) + Mathf.Sin(2 * Mathf.PI * f * 1.004f * x) * 0.7f;
                        s += Mathf.Sin(2 * Mathf.PI * f * 2 * x) * 0.12f;
                    }
                    s *= 0.05f;
                    // 慢慢的音量起伏
                    s *= 0.85f + 0.15f * Mathf.Sin(2 * Mathf.PI * x / ChordSec);
                    // 低音
                    s += Mathf.Sin(2 * Mathf.PI * Roots[c] * x) * 0.16f;

                    // 琶音：和弦開始後 0.5 秒起，每 0.5 秒一個，最後 1 秒不再起新的音（讓尾音在和弦內收完）
                    if (x >= 0.5f)
                    {
                        int k = Mathf.FloorToInt(x / 0.5f);
                        float on = k * 0.5f;
                        if (on <= ChordSec - 1f)
                        {
                            float f = pad[Arp[k % Arp.Length]] * 2;
                            float u = x - on;
                            s += Mathf.Sin(2 * Mathf.PI * f * u) * Mathf.Exp(-u * 4f) * 0.07f;
                        }
                    }
                    dry[i] += s * w;
                }
            }

            // 回聲：繞著整段跑兩圈，讓接縫的地方也有前一圈留下的尾巴
            int d = Mathf.RoundToInt(0.375f * Rate);
            var wet = new float[n];
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < n; i++)
                    wet[i] = dry[i] + wet[(i - d + n) % n] * 0.38f;

            float peak = 0.0001f;
            for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(wet[i]));
            float gain = 0.8f / peak;
            for (int i = 0; i < n; i++) wet[i] *= gain;

            var clip = AudioClip.Create("music_space", n, 1, Rate, false);
            clip.SetData(wet, 0);
            return clip;
        }

        /// <summary>和弦的音量：前後各 Fade 秒用平滑曲線淡入淡出，中間全開。</summary>
        static float Window(float x)
        {
            if (x < -Fade || x > ChordSec + Fade) return 0;
            if (x < 0) return Smooth((x + Fade) / Fade);
            if (x > ChordSec) return Smooth(1 - (x - ChordSec) / Fade);
            return 1;
        }

        static float Smooth(float v) => v * v * (3 - 2 * v);
    }
}
