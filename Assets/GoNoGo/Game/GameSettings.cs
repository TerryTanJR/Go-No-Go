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

        public const float MinMouse = 0.3f, MaxMouse = 2.5f;

        static float? _mouse;

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

        /// <summary>寫進磁碟（瀏覽器是 IndexedDB）。關選單時呼叫。</summary>
        public static void Save() => PlayerPrefs.Save();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _mouse = null;
    }
}
