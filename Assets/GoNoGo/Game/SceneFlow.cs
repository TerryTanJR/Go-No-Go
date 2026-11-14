using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoNoGo.Game
{
    /// <summary>場景名稱與切換。切換時先淡出、載入、再淡入（淡入淡出由 UI 層的 ScreenFader 畫）。</summary>
    public static class SceneFlow
    {
        public const string Boot = "Boot";
        public const string MainMenu = "MainMenu";
        /// <summary>大廳、組裝區、中控室都在這個場景裡（多人時大家同一個場景，同步最單純）。</summary>
        public const string Facility = "Facility";
        public const string Launch = "Launch";
        public const string Report = "Report";

        public static readonly string[] All = { Boot, MainMenu, Facility, Launch, Report };

        /// <summary>UI 層註冊：淡出到黑（參數：秒數、完成時回呼）。</summary>
        public static Action<float, Action> FadeOut;
        /// <summary>UI 層註冊：從黑淡入。</summary>
        public static Action<float> FadeIn;

        public static bool Loading { get; private set; }
        /// <summary>載入新場景的進度 0–1（過場畫面的進度條用）。</summary>
        public static float Progress { get; private set; }

        public static void Go(string scene, float fade = 0.45f)
        {
            if (Loading) return;
            Loading = true;
            Progress = 0;
            GameSession.Instance.StartCoroutine(Load(scene, fade));
        }

        static IEnumerator Load(string scene, float fade)
        {
            bool faded = FadeOut == null;
            FadeOut?.Invoke(fade, () => faded = true);
            float t = 0;
            while (!faded && t < fade + 1f) { t += Time.unscaledDeltaTime; yield return null; }
            var op = SceneManager.LoadSceneAsync(scene);
            while (op != null && !op.isDone)
            {
                Progress = Mathf.Clamp01(op.progress / 0.9f); // Unity 的 progress 載完資料停在 0.9，啟用場景後才到 1
                yield return null;
            }
            Progress = 1;
            Loading = false;
            FadeIn?.Invoke(fade);
        }
    }
}
