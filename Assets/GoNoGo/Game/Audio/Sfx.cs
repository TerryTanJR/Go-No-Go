using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 全遊戲的聲音：介面音效和背景音樂用程式合成（不需要音檔，也沒有授權問題），
    /// 背景環境音與發射音效用 NASA 的錄音（AudioBank）。
    /// 瀏覽器要等玩家第一次點擊後才允許出聲，Unity WebGL 會自動處理。
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        static Sfx _i;
        AudioSource _ui, _ambience, _music;
        readonly Dictionary<string, AudioClip> _synth = new Dictionary<string, AudioClip>();
        float _hoverCooldown;
        float _ambienceTarget, _musicTarget;
        AudioClip _musicClip;

        /// <summary>主音量：所有聲音（包含發射場景的聲音）。</summary>
        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat("gonogo.volume", 0.8f);
            set { PlayerPrefs.SetFloat("gonogo.volume", Mathf.Clamp01(value)); AudioListener.volume = Mathf.Clamp01(value); }
        }

        static float? _ambienceVol, _effectsVol, _musicVol;

        /// <summary>背景音樂的倍率 0–1。</summary>
        public static float MusicVolume
        {
            get => _musicVol ??= Mathf.Clamp01(PlayerPrefs.GetFloat("gonogo.volume.music", 0.7f));
            set { _musicVol = Mathf.Clamp01(value); PlayerPrefs.SetFloat("gonogo.volume.music", _musicVol.Value); }
        }

        /// <summary>環境音（控制室底噪、太空背景音）的倍率 0–1。</summary>
        public static float AmbienceVolume
        {
            get => _ambienceVol ??= Mathf.Clamp01(PlayerPrefs.GetFloat("gonogo.volume.ambience", 1f));
            set { _ambienceVol = Mathf.Clamp01(value); PlayerPrefs.SetFloat("gonogo.volume.ambience", _ambienceVol.Value); }
        }

        /// <summary>介面與動作音效（按鈕、拿取、安裝…）的倍率 0–1。</summary>
        public static float EffectsVolume
        {
            get => _effectsVol ??= Mathf.Clamp01(PlayerPrefs.GetFloat("gonogo.volume.effects", 1f));
            set { _effectsVol = Mathf.Clamp01(value); PlayerPrefs.SetFloat("gonogo.volume.effects", _effectsVol.Value); }
        }

        static Sfx I
        {
            get
            {
                if (_i == null)
                {
                    var go = new GameObject("[Sfx]");
                    DontDestroyOnLoad(go);
                    _i = go.AddComponent<Sfx>();
                    _i.Init();
                }
                return _i;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { _i = null; _ambienceVol = null; _effectsVol = null; _musicVol = null; }

        void Init()
        {
            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ambience = gameObject.AddComponent<AudioSource>();
            _ambience.loop = true;
            _ambience.playOnAwake = false;
            _ambience.volume = 0;
            _music = gameObject.AddComponent<AudioSource>();
            _music.loop = true;
            _music.playOnAwake = false;
            _music.volume = 0;
            AudioListener.volume = MasterVolume;
        }

        void Update()
        {
            _hoverCooldown -= Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2.5f);
            float amb = AmbienceVolume;
            _ambience.volume = Mathf.Lerp(_ambience.volume, _ambienceTarget * amb, k);
            if (_ambienceTarget <= 0 && _ambience.volume < 0.005f && _ambience.isPlaying) _ambience.Stop();
            _music.volume = Mathf.Lerp(_music.volume, _musicTarget * MusicVolume, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 1.2f));
            if (_musicTarget <= 0 && _music.volume < 0.003f && _music.isPlaying) _music.Pause();
        }

        // ---------------- 介面音效 ----------------

        public static void Click() => I.Play("click", 0.35f);
        public static void Hover()
        {
            var s = I;
            if (s._hoverCooldown > 0) return;
            s._hoverCooldown = 0.05f;
            s.Play("hover", 0.12f);
        }
        public static void Confirm() => I.Play("confirm", 0.4f);
        public static void Alert() => I.Play("alert", 0.45f);
        public static void Go() => I.Play("go", 0.45f);
        public static void NoGo() => I.Play("nogo", 0.4f);
        public static void Stamp() => I.Play("stamp", 0.8f);
        /// <summary>火箭爆炸：一聲爆裂接很低的轟鳴，拖一段越來越悶的隆隆餘音（約 5 秒）。</summary>
        public static void Explosion() => I.Play("explosion", 1f);
        public static void Tick() => I.Play("tick", 0.18f);
        public static void Whoosh() => I.Play("whoosh", 0.35f);
        /// <summary>拿起零件。</summary>
        public static void Pick() => I.Play("pick", 0.4f);
        /// <summary>放下零件。</summary>
        public static void Drop() => I.Play("drop", 0.4f);
        /// <summary>零件裝上太空船：機械卡榫聲 + 上升的電子音。</summary>
        public static void Install() => I.Play("install", 0.55f);
        /// <summary>長按安裝時的充能聲（每 0.1 秒一下，音高跟著進度升高）。</summary>
        public static void Charge(float progress) => I.Play("charge" + Mathf.Clamp(Mathf.RoundToInt(progress * 10), 0, 10), 0.12f);
        public static void Door() => I.Play("door", 0.3f);
        /// <summary>翻書頁：一陣紙張摩擦的沙沙聲。</summary>
        public static void Page() => I.Play("page", 0.4f);

        // ---------------- 營運、處置小遊戲 ----------------

        /// <summary>營運：新事件的警報（三聲一高一低的警示音）。</summary>
        public static void Alarm() => I.Play("alarm", 0.42f);
        /// <summary>營運：這回合處置成功（上行的四個音）。</summary>
        public static void Success() => I.Play("success", 0.5f);
        /// <summary>營運：這回合處置失敗（下行的三個音）。</summary>
        public static void Fail() => I.Play("fail", 0.5f);
        /// <summary>回合倒數最後幾秒：每秒一聲。</summary>
        public static void Countdown() => I.Play("count", 0.3f);
        /// <summary>小遊戲：按下去的回饋（很短的一聲）。</summary>
        public static void MiniTap() => I.Play("tap", 0.22f);
        /// <summary>小遊戲：做對一件事（命中、送出、接對）；進度越高音越高。</summary>
        public static void MiniGain(float progress) => I.Play("gain" + Mathf.Clamp(Mathf.RoundToInt(progress * 10), 0, 10), 0.32f);
        /// <summary>小遊戲：被打中、失誤。</summary>
        public static void MiniBad() => I.Play("bad", 0.42f);
        /// <summary>小遊戲：開火。</summary>
        public static void MiniShot() => I.Play("shot", 0.16f);
        /// <summary>小遊戲：通關。</summary>
        public static void MiniWin() => I.Play("win", 0.55f);
        /// <summary>小遊戲：沒過。</summary>
        public static void MiniLose() => I.Play("lose", 0.5f);

        // ---------------- 背景音樂 ----------------

        /// <summary>Menu：標題、主選單、報告；Facility：設施裡小聲一點，不蓋過音效；None：發射（有自己的聲音）。</summary>
        public enum Music { None, Menu, Facility }

        public static void SetMusic(Music m)
        {
            var s = I;
            s._musicTarget = m switch { Music.Menu => 0.45f, Music.Facility => 0.2f, _ => 0f };
            if (s._musicTarget <= 0) return;
            if (s._musicClip == null) s._musicClip = MusicLoop.Build();
            if (s._music.clip != s._musicClip) s._music.clip = s._musicClip;
            if (!s._music.isPlaying) s._music.Play();
        }

        void Play(string id, float vol)
        {
            if (!_synth.TryGetValue(id, out var clip))
            {
                clip = Synthesize(id);
                _synth[id] = clip;
            }
            _ui.PlayOneShot(clip, vol * EffectsVolume);
        }

        // ---------------- 環境音 ----------------

        /// <summary>ControlRoom 和 None 一樣是安靜的（保留給呼叫端標明場景）；Space 是營運畫面的電波聲。</summary>
        public enum Ambience { None, ControlRoom, Space }

        /// <summary>
        /// 切換背景環境音（淡入淡出）。控制室（標題、主選單、設施、報告）不放底噪，安靜就好；
        /// 營運畫面的太空放 Juno 的電波聲。
        /// </summary>
        public static void SetAmbience(Ambience a)
        {
            var s = I;
            switch (a)
            {
                case Ambience.Space:
                    var bank = AudioBank.Load();
                    if (bank != null && bank.spaceAmbience != null) s.StartLoop(s._ambience, bank.spaceAmbience, 0.22f, ref s._ambienceTarget);
                    break;
                default:
                    s._ambienceTarget = 0;
                    break;
            }
        }

        void StartLoop(AudioSource src, AudioClip clip, float vol, ref float target)
        {
            if (clip == null) return;
            if (src.clip != clip) { src.clip = clip; src.volume = 0; }
            if (!src.isPlaying) src.Play();
            target = vol;
        }

        // ---------------- 合成 ----------------

        const int Rate = 44100;

        static AudioClip Synthesize(string id)
        {
            if (id.StartsWith("charge") && int.TryParse(id.Substring(6), out int step))
            {
                float f = 500f + step * 70f;
                return Tone(id, 0.05f, t => Sine(f, t) * Env(t, 0.002f, 0.045f));
            }
            if (id.StartsWith("gain") && int.TryParse(id.Substring(4), out int g))
            {
                // 進度 0 → 10：音高升一個八度；兩個音疊在一起（主音 + 五度），聽起來是「叮」
                float f = 620f * Mathf.Pow(2f, g / 10f);
                return Tone(id, 0.16f, t => (Sine(f, t) * 0.6f + Sine(f * 1.5f, t) * 0.25f + Sine(f * 2f, t) * 0.12f) * Env(t, 0.003f, 0.14f));
            }
            switch (id)
            {
                case "pick": return Tone(id, 0.12f, t => (Sine(520 + 900 * t, t) * 0.6f + Sine(1040, t) * 0.2f) * Env(t, 0.003f, 0.1f));
                case "drop": return Tone(id, 0.16f, t => (Sine(420 - 600 * t, t) * 0.6f + Sine(90, t) * 0.5f) * Env(t, 0.003f, 0.14f));
                case "install":
                {
                    var rnd = new System.Random(11);
                    return Tone(id, 0.7f, t =>
                    {
                        float n = (float)(rnd.NextDouble() * 2 - 1);
                        float clunk = (n * 0.6f + Sine(95, t)) * Env(t, 0.001f, 0.08f);
                        float rise = t > 0.08f ? (Sine(660 * (1 + (t - 0.08f) * 1.4f), t) * 0.35f + Sine(1320 * (1 + (t - 0.08f) * 1.4f), t) * 0.15f) * Env(t - 0.08f, 0.01f, 0.55f) : 0f;
                        return clunk + rise;
                    });
                }
                case "door":
                {
                    var rnd = new System.Random(5);
                    float lp = 0;
                    return Tone(id, 0.5f, t =>
                    {
                        float n = (float)(rnd.NextDouble() * 2 - 1);
                        lp += (n - lp) * 0.05f;
                        return lp * 1.6f * Env(t, 0.05f, 0.45f) + Sine(60, t) * 0.2f * Env(t, 0.02f, 0.4f);
                    });
                }
                case "page":
                {
                    // 紙張：白噪音經過一個慢慢打開又關上的低通，兩段（翻起、落下）
                    var rnd = new System.Random(17);
                    float lp = 0, hp = 0;
                    return Tone(id, 0.32f, t =>
                    {
                        float n = (float)(rnd.NextDouble() * 2 - 1);
                        float open = 0.08f + 0.5f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.32f));
                        lp += (n - lp) * open;
                        hp = lp - hp * 0.6f;
                        float env = Env(t, 0.02f, 0.16f) + 0.6f * Env(Mathf.Max(0, t - 0.16f), 0.01f, 0.12f) * (t > 0.16f ? 1 : 0);
                        return hp * 0.9f * env;
                    });
                }
                case "alarm":
                    // 三聲：每聲前半高、後半低（方波太刺耳，用正弦加一點方波）
                    return Tone(id, 0.96f, t =>
                    {
                        float u = t % 0.32f;
                        float hz = u < 0.14f ? 880f : 660f;
                        return (Sine(hz, t) * 0.5f + Square(hz, t) * 0.12f) * (u < 0.28f ? Env(u % 0.14f, 0.004f, 0.16f) : 0f);
                    });
                case "success":
                    return Tone(id, 0.75f, t =>
                    {
                        float[] n = { 523.25f, 659.25f, 783.99f, 1046.5f };
                        int i = Mathf.Min(3, (int)(t / 0.11f));
                        float u = t - i * 0.11f;
                        return (Sine(n[i], t) * 0.55f + Sine(n[i] * 2f, t) * 0.18f) * Env(u, 0.004f, i == 3 ? 0.4f : 0.16f);
                    });
                case "fail":
                    return Tone(id, 0.8f, t =>
                    {
                        float[] n = { 392f, 311.13f, 233.08f };
                        int i = Mathf.Min(2, (int)(t / 0.2f));
                        float u = t - i * 0.2f;
                        return (Sine(n[i], t) * 0.5f + Saw(n[i] / 2f, t) * 0.22f) * Env(u, 0.006f, i == 2 ? 0.38f : 0.22f);
                    });
                case "count": return Tone(id, 0.09f, t => Sine(1040, t) * Env(t, 0.002f, 0.08f) * 0.7f);
                case "tap": return Tone(id, 0.05f, t => (Sine(980 - 2400 * t, t) * 0.7f + Sine(1960, t) * 0.15f) * Env(t, 0.001f, 0.045f));
                case "shot": return Tone(id, 0.11f, t => (Sine(1500 - 9000 * t, t) * 0.6f + Saw(700 - 3000 * t, t) * 0.2f) * Env(t, 0.001f, 0.1f));
                case "bad":
                {
                    var rnd = new System.Random(31);
                    return Tone(id, 0.28f, t =>
                    {
                        float n = (float)(rnd.NextDouble() * 2 - 1);
                        return (Saw(170 - 220 * t, t) * 0.45f + Sine(85, t) * 0.5f + n * 0.25f * Env(t, 0.001f, 0.06f)) * Env(t, 0.003f, 0.25f);
                    });
                }
                case "win":
                    return Tone(id, 1.05f, t =>
                    {
                        float[] n = { 523.25f, 659.25f, 783.99f, 1046.5f, 1318.5f };
                        int i = Mathf.Min(4, (int)(t / 0.1f));
                        float u = t - i * 0.1f;
                        float chord = i == 4 ? Sine(1046.5f, t) * 0.25f + Sine(783.99f, t) * 0.2f : 0f;
                        return (Sine(n[i], t) * 0.5f + Sine(n[i] * 2f, t) * 0.15f + chord) * Env(u, 0.004f, i == 4 ? 0.6f : 0.14f);
                    });
                case "lose":
                    return Tone(id, 0.95f, t =>
                    {
                        float[] n = { 440f, 369.99f, 311.13f, 220f };
                        int i = Mathf.Min(3, (int)(t / 0.17f));
                        float u = t - i * 0.17f;
                        return (Sine(n[i], t) * 0.5f + Saw(n[i] / 2f, t) * 0.2f) * Env(u, 0.006f, i == 3 ? 0.42f : 0.2f);
                    });
                case "click": return Tone(id, 0.045f, t => Sine(2400, t) * Env(t, 0.002f, 0.04f) * 0.6f + Sine(1200, t) * Env(t, 0.001f, 0.02f) * 0.4f);
                case "hover": return Tone(id, 0.03f, t => Sine(1800, t) * Env(t, 0.003f, 0.025f));
                case "tick": return Tone(id, 0.02f, t => Sine(3200, t) * Env(t, 0.001f, 0.015f));
                case "confirm": return Tone(id, 0.22f, t => (t < 0.09f ? Sine(880, t) : Sine(1320, t)) * Env(t % 0.09f, 0.004f, 0.12f) * 0.7f);
                case "alert": return Tone(id, 0.5f, t => Square(t < 0.25f ? 760 : 570, t) * 0.25f * Env(t % 0.25f, 0.005f, 0.22f));
                case "go": return Tone(id, 0.6f, t => (Sine(660, t) + Sine(990, t) * 0.6f + Sine(1320, t) * 0.3f) * Env(t, 0.005f, 0.55f) * 0.45f);
                case "nogo": return Tone(id, 0.45f, t => (Saw(150, t) * 0.4f + Sine(110, t) * 0.6f) * Env(t, 0.01f, 0.4f) * 0.6f);
                case "stamp":
                {
                    var rnd = new System.Random(7);
                    float lp = 0;
                    return Tone(id, 0.5f, t =>
                    {
                        float n = (float)(rnd.NextDouble() * 2 - 1);
                        lp += (n - lp) * 0.08f;
                        return (lp * 2.2f + Sine(70, t) * 0.8f) * Env(t, 0.002f, 0.35f);
                    });
                }
                case "explosion":
                {
                    // 1. 開頭 60 ms 的寬頻爆裂；2. 低通噪音的主體，濾波慢慢關上（聲音越來越悶）；
                    // 3. 從 55 Hz 往下掉到 28 Hz 的重低音。只有一聲，不加二次爆裂（聽起來太多段）。最後用 tanh 壓住，不會破音
                    var rnd = new System.Random(23);
                    float lp1 = 0, lp2 = 0, phase = 0;
                    return Tone(id, 5.5f, t =>
                    {
                        float n = (float)(rnd.NextDouble() * 2 - 1);
                        float crack = n * Env(t, 0.001f, 0.06f) * 1.2f;
                        float cut = Mathf.Lerp(0.25f, 0.015f, Mathf.Clamp01(t / 2.5f));
                        lp1 += (n - lp1) * cut;
                        lp2 += (lp1 - lp2) * cut;
                        float body = lp2 * 3.2f * Env(t, 0.01f, 3.8f);
                        phase += 2f * Mathf.PI * Mathf.Lerp(55f, 28f, Mathf.Clamp01(t / 1.5f)) / Rate;
                        float sub = Mathf.Sin(phase) * 0.9f * Env(t, 0.005f, 2.6f);
                        return (float)System.Math.Tanh((crack + body + sub) * 1.4f) * 0.95f;
                    });
                }
                case "whoosh":
                {
                    var rnd = new System.Random(11);
                    float lp = 0;
                    return Tone(id, 0.6f, t =>
                    {
                        float n = (float)(rnd.NextDouble() * 2 - 1);
                        float cutoff = 0.02f + 0.18f * Mathf.Sin(Mathf.PI * t / 0.6f);
                        lp += (n - lp) * cutoff;
                        return lp * 1.6f * Mathf.Sin(Mathf.PI * t / 0.6f);
                    });
                }
                default: return Tone(id, 0.05f, t => 0);
            }
        }

        static AudioClip Tone(string id, float seconds, Func<float, float> f)
        {
            int n = Mathf.CeilToInt(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create(id, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Sine(float hz, float t) => Mathf.Sin(2 * Mathf.PI * hz * t);
        static float Square(float hz, float t) => Mathf.Sign(Mathf.Sin(2 * Mathf.PI * hz * t));
        static float Saw(float hz, float t) => 2f * (t * hz - Mathf.Floor(t * hz + 0.5f));
        /// <summary>簡單包絡：attack 秒線性上升，之後指數衰減（decay 秒降到約 1%）。</summary>
        static float Env(float t, float attack, float decay) =>
            t < attack ? t / attack : Mathf.Exp(-(t - attack) * 4.6f / decay);
    }
}
