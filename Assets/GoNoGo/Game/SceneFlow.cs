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
        public const string DesignRoom = "DesignRoom";
        public const string MissionControl = "MissionControl";
        public const string Launch = "Launch";
        public const string Operations = "Operations";
        public const string Report = "Report";

        public static readonly string[] All = { Boot, DesignRoom, MissionControl, Launch, Operations, Report };

        /// <summary>UI 層註冊：淡出到黑（參數：秒數、完成時回呼）。</summary>
        public static Action<float, Action> FadeOut;
        /// <summary>UI 層註冊：從黑淡入。</summary>
        public static Action<float> FadeIn;

        public static bool Loading { get; private set; }

        public static void Go(string scene, float fade = 0.45f)
        {
            if (Loading) return;
            Loading = true;
            GameSession.Instance.StartCoroutine(Load(scene, fade));
        }

        static IEnumerator Load(string scene, float fade)
        {
            bool faded = FadeOut == null;
            FadeOut?.Invoke(fade, () => faded = true);
            float t = 0;
            while (!faded && t < fade + 1f) { t += Time.unscaledDeltaTime; yield return null; }
            var op = SceneManager.LoadSceneAsync(scene);
            while (op != null && !op.isDone) yield return null;
            Loading = false;
            FadeIn?.Invoke(fade);
        }
    }
}
