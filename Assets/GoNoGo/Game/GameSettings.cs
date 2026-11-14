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
            TextScaleChanged = null;
        }
    }
}
