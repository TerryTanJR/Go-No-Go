using System;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 玩家自己的設定（存在 PlayerPrefs，下次開遊戲還在）。音量在 <see cref="Sfx"/>。
    /// 遊戲中按 Tab 或 Esc 打開的選單可以調整。
    /// </summary>
    public static class GameSettings
    {
        const string MouseKey = "gonogo.mouse";
        const string RunToggleKey = "gonogo.run.toggle";
        const string VoiceKey = "gonogo.voice";
        // 字級基準放大過一次（舊的 115% 變成現在的 100%），換一個 key，舊 key 的值換算過來
        const string TextKey = "gonogo.textscale.v2";
        const string OldTextKey = "gonogo.textscale";
        const float OldTextBase = 115f;

        public const float MinMouse = 0.3f, MaxMouse = 2.5f;

        /// <summary>
        /// 可選的字體大小（百分比）。UI 根元素會掛上 <c>fs-{百分比}</c>，FontScale.uss 依此換掉所有 --fs-N 變數。
        /// 改這裡之後要執行 GoNoGo/Build/UI Assets 重新產生 FontScale.uss。
        /// </summary>
        public static readonly int[] TextScales = { 90, 100, 115, 130, 150 };
        public const int DefaultTextScale = 100;

        static float? _mouse;
        static int? _text;
        static bool? _runToggle;
        static float? _voice;

        /// <summary>字體大小改變時（UI 根元素換 class）。</summary>
        public static event Action TextScaleChanged;

        /// <summary>滑鼠靈敏度倍率（1 = 預設）。每幀都會讀，所以快取起來。</summary>
        public static float MouseSensitivity
        {
            get => _mouse ??= Mathf.Clamp(PlayerPrefs.GetFloat(MouseKey, 1f), MinMouse, MaxMouse);
            set
            {
                _mouse = Mathf.Clamp(value, MinMouse, MaxMouse);
                PlayerPrefs.SetFloat(MouseKey, _mouse.Value);
            }
        }

        /// <summary>
        /// 奔跑方式。false（預設）= 按住：按住 Shift 跑，放開就走。
        /// true = 切換：按一下 Shift 開始跑，再按一下改回走；停下來（沒有按方向鍵）時自動改回走。
        /// 存在本機，多人時各自設定。
        /// </summary>
        public static bool RunToggle
        {
            get => _runToggle ??= PlayerPrefs.GetInt(RunToggleKey, 0) == 1;
            set
            {
                _runToggle = value;
                PlayerPrefs.SetInt(RunToggleKey, value ? 1 : 0);
            }
        }

        /// <summary>
        /// 語音聊天音量（0–1，預設 1）：只調整聽到隊友說話的音量，不受主音量影響；自己麥克風的靜音另外管。
        /// 存在本機。語音在 GoNoGo.Net（Vivox），改了就透過 <see cref="Multiplayer.SetVoiceVolume"/> 套用。
        /// </summary>
        public static float VoiceVolume
        {
            get => _voice ??= Mathf.Clamp01(PlayerPrefs.GetFloat(VoiceKey, 1f));
            set
            {
                _voice = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(VoiceKey, _voice.Value);
                Multiplayer.SetVoiceVolume?.Invoke(_voice.Value);
            }
        }

        /// <summary>字體大小（百分比，只會是 <see cref="TextScales"/> 其中之一）。</summary>
        public static int TextScale
        {
            get => _text ??= Snap(PlayerPrefs.GetInt(TextKey, Migrated()));
            set
            {
                int v = Snap(value);
                if (v == TextScale) return;
                _text = v;
                PlayerPrefs.SetInt(TextKey, v);
                TextScaleChanged?.Invoke();
            }
        }

        /// <summary>舊版存的字級換成新基準的百分比（舊 115% = 新 100%）；沒存過就是預設。</summary>
        static int Migrated()
        {
            if (!PlayerPrefs.HasKey(OldTextKey)) return DefaultTextScale;
            return Mathf.RoundToInt(PlayerPrefs.GetInt(OldTextKey, (int)OldTextBase) * 100f / OldTextBase);
        }

        /// <summary>USS class 名稱，例如 fs-115。</summary>
        public static string TextScaleClass(int pct) => "fs-" + pct;

        static int Snap(int v)
        {
            int best = DefaultTextScale;
            foreach (var s in TextScales)
                if (Math.Abs(s - v) < Math.Abs(best - v)) best = s;
            return best;
        }

        /// <summary>寫進磁碟（瀏覽器是 IndexedDB）。關選單時呼叫。</summary>
        public static void Save() => PlayerPrefs.Save();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _mouse = null;
            _text = null;
            _runToggle = null;
            _voice = null;
            TextScaleChanged = null;
        }
    }
}
