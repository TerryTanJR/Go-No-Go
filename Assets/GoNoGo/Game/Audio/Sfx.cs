using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 全遊戲的聲音：介面音效用程式合成（不需要音檔，也沒有授權問題），
    /// 背景環境音與發射音效用 NASA 的錄音（AudioBank）。
    /// 瀏覽器要等玩家第一次點擊後才允許出聲，Unity WebGL 會自動處理。
    /// </summary>
    public sealed class Sfx : MonoBehaviour
    {
        static Sfx _i;
        AudioSource _ui, _ambience;
        readonly Dictionary<string, AudioClip> _synth = new Dictionary<string, AudioClip>();
        float _hoverCooldown;
        float _ambienceTarget;

        /// <summary>主音量：所有聲音（包含發射場景的聲音）。</summary>
        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat("gonogo.volume", 0.8f);
            set { PlayerPrefs.SetFloat("gonogo.volume", Mathf.Clamp01(value)); AudioListener.volume = Mathf.Clamp01(value); }
        }

        static float? _ambienceVol, _effectsVol;

        /// <summary>環境音（控制室底噪、太空背景音）的倍率 0–1。</summary>
        public static float AmbienceVolume
        {
            get => _ambienceVol ??= Mathf.Clamp01(PlayerPrefs.GetFloat("gonogo.volume.ambience", 1f));
            set { _ambienceVol = Mathf.Clamp01(value); PlayerPrefs.SetFloat("gonogo.volume.ambience", _ambienceVol.Value); }
        }

        /// <summary>介面與動作音效（按鈕、拿取、安裝、電話…）的倍率 0–1。</summary>
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
        static void ResetStatics() { _i = null; _ambienceVol = null; _effectsVol = null; }

        void Init()
        {
            _ui = gameObject.AddComponent<AudioSource>();
            _ui.playOnAwake = false;
            _ambience = gameObject.AddComponent<AudioSource>();
            _ambience.loop = true;
            _ambience.playOnAwake = false;
            _ambience.volume = 0;
            AudioListener.volume = MasterVolume;
        }

        void Update()
        {
            _hoverCooldown -= Time.unscaledDeltaTime;
            float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime * 2.5f);
            float amb = AmbienceVolume;
            _ambience.volume = Mathf.Lerp(_ambience.volume, _ambienceTarget * amb, k);
            if (_ambienceTarget <= 0 && _ambience.volume < 0.005f && _ambience.isPlaying) _ambience.Stop();
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
        /// <summary>電話撥號鈴聲。</summary>
        public static void Ring() => I.Play("ring", 0.35f);
        public static void Door() => I.Play("door", 0.3f);

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

        AudioClip GetSynth(string id)
        {
            if (!_synth.TryGetValue(id, out var c)) { c = Synthesize(id); _synth[id] = c; }
            return c;
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
                case "ring": return Tone(id, 1.2f, t => (t % 0.6f < 0.4f ? (Sine(440, t) + Sine(480, t)) * 0.35f : 0f) * Env(t, 0.01f, 1.15f));
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
