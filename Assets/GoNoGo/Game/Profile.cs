using System;
using GoNoGo.Core;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>本機玩家的 avatar 設定（存在 PlayerPrefs）。</summary>
    public static class Profile
    {
        const string Key = "gonogo.avatar";

        static AvatarLook _look;

        public static bool HasAvatar => PlayerPrefs.HasKey(Key);

        public static AvatarLook Look
        {
            get
            {
                if (_look != null) return _look;
                _look = new AvatarLook { Name = "Astro", Suit = 0, Helmet = 0, Visor = 2, Accessory = 1 };
                var s = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(s))
                {
                    try { _look = JsonUtility.FromJson<Saved>(s).ToLook(); }
                    catch (Exception) { /* 壞掉就用預設 */ }
                }
                return _look;
            }
        }

        public static event Action Changed;

        /// <summary>編輯 avatar 時的即時預覽（null = 沒在編輯）。主選單的 3D avatar 顯示這個。</summary>
        public static AvatarLook Preview { get; private set; }
        public static event Action PreviewChanged;

        public static void SetPreview(AvatarLook look)
        {
            Preview = look?.Sanitized();
            PreviewChanged?.Invoke();
        }

        /// <summary>主選單要顯示的外型。</summary>
        public static AvatarLook Shown => Preview ?? Look;

        public static void Save(AvatarLook look)
        {
            _look = look.Sanitized();
            if (string.IsNullOrEmpty(_look.Name)) _look.Name = "Astro";
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Saved.From(_look)));
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        [Serializable]
        sealed class Saved
        {
            public string name;
            public int suit, helmet, visor, accessory, accent, lower;
            public static Saved From(AvatarLook l) => new Saved { name = l.Name, suit = l.Suit, helmet = l.Helmet, visor = l.Visor, accessory = l.Accessory, accent = l.Accent, lower = l.Lower };
            public AvatarLook ToLook() => new AvatarLook { Name = name, Suit = suit, Helmet = helmet, Visor = visor, Accessory = accessory, Accent = accent, Lower = lower }.Sanitized();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _look = null;
            Changed = null;
            Preview = null;
            PreviewChanged = null;
        }
    }
}
