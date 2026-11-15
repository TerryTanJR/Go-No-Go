using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// NASA 音效的清單（Resources/Audio/AudioBank）。由編輯器腳本 AudioBankBuilder 自動填好；
    /// 放在 Resources 是為了讓任何場景都拿得到，而且只有被引用的音檔才會進建置。
    /// </summary>
    [CreateAssetMenu(menuName = "GoNoGo/Audio Bank")]
    public sealed class AudioBank : ScriptableObject
    {
        [Tooltip("IM-2 倒數網路，終端倒數在 44–54 秒")] public AudioClip countdownNet;
        [Tooltip("倒數網路裡念「十、九……一、零」的開口時間（秒）。從音檔的振幅包絡實測，每數一下約 0.93 秒")]
        public float[] countdownCalls = { 44.44f, 45.30f, 46.22f, 47.18f, 48.10f, 49.04f, 49.96f, 50.98f, 51.86f, 52.80f, 53.56f };
        [Tooltip("IM-2 發射台麥克風，點火大約在 8–10 秒")] public AudioClip padRoar;
        public float padIgnitionAt = 8f;
        [Tooltip("IM-2 海灘遠方隆隆聲")] public AudioClip distantRumble;
        [Tooltip("Juno 木衛三飛掠的電波聲（營運畫面背景）")] public AudioClip spaceAmbience;

        static AudioBank _cached;
        public static AudioBank Load()
        {
            if (_cached == null) _cached = Resources.Load<AudioBank>("Audio/AudioBank");
            return _cached;
        }
    }
}
