using System;
using GoNoGo.Core;

namespace GoNoGo.Game
{
    /// <summary>
    /// 發射畫面的即時資料：3D 發射導演（Launch 組件）寫入，HUD（UI 組件）讀取。
    /// 兩邊互不引用，只透過這裡溝通。
    /// </summary>
    public static class LaunchFeed
    {
        /// <summary>相對於升空的秒數（負數 = 倒數中）。</summary>
        public static float T;
        public static float AltitudeKm;
        public static float VelocityKms;
        public static float Throttle;
        /// <summary>目前階段的字串鍵，例如 ui.launch.stage.maxq。</summary>
        public static string StageKey = "";
        /// <summary>整段動畫播完（或跳過），可以顯示結果。</summary>
        public static bool Finished;
        /// <summary>場景裡有發射導演在跑（沒有的話 HUD 自己倒數，方便單獨測 UI）。</summary>
        public static bool DirectorActive;

        /// <summary>播報（字串鍵）。</summary>
        public static event Action<string> Callout;
        public static event Action SkipRequested;

        public static void Reset()
        {
            T = -10f;
            AltitudeKm = 0;
            VelocityKms = 0;
            Throttle = 0;
            StageKey = "ui.launch.stage.countdown";
            Finished = false;
        }

        public static void Say(string key) => Callout?.Invoke(key);
        public static void RequestSkip() => SkipRequested?.Invoke();

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Callout = null;
            SkipRequested = null;
            DirectorActive = false;
            Reset();
        }
    }

    /// <summary>營運畫面的時鐘：營運 UI 推進，3D 地球（Presentation 組件）讀取。</summary>
    public static class OpsClock
    {
        public static double Day;
        public static double TotalDays;
        public static bool Paused;
        public static Sample Current;
        public static event Action<MissionEvent> EventTriggered;

        public static void Trigger(MissionEvent e) => EventTriggered?.Invoke(e);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Day = 0;
            TotalDays = 0;
            Paused = false;
            Current = null;
            EventTriggered = null;
        }
    }
}
