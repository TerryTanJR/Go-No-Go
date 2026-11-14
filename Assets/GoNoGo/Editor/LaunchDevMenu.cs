using GoNoGo.Core;
using GoNoGo.Game;
using GoNoGo.Launch;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 開發用的發射動畫入口（只在 Editor 的 Play Mode 裡有作用，正式遊戲沒有）：
    /// 直接開一局預設任務（展開式太陽能板）並發射、在自動倍率上再快轉、跳到結尾看結果。
    /// 可以指定火箭和發射結果（只改這次發射的演出資料，不影響核心的判定）。
    /// </summary>
    public static class LaunchDevMenu
    {
        const string Root = "GoNoGo/Dev/";

        /// <summary>指定的火箭（空字串 = 照任務預設）。</summary>
        public static string Launcher = "";
        /// <summary>指定的發射結果（null = 照核心算出來的）。</summary>
        public static Outcome? Result;

        public enum Outcome { Nominal, LowerOrbit, Anomaly, Underspeed, FairingFailure, DeadOnOrbit }

        static readonly string[] Launchers = { "electron", "falcon9", "falcon_heavy", "taurus_xl", "delta_ii", "falcon9_rideshare" };

        [MenuItem(Root + "Test Launch", priority = 100)]
        static void TestLaunch()
        {
            // 已經在發射階段（例如看完一次想重播）就直接重載發射場景
            if (GameSession.HasMission && GameSession.Mission.S.Phase == SessionPhase.Launch && GameSession.Mission.S.LastLaunch != null)
            {
                ApplyOverrides(GameSession.Mission.S.LastLaunch);
                SceneManager.LoadScene(SceneFlow.Launch);
                return;
            }
            GameSession.StartSolo("earth_observation", "engineer", false, false);
            GameSession.Send(CommandType.BeginDesign);
            var s = GameSession.Mission.S;
            // 用展開式太陽能板：發射動畫最後要看得到翼板展開
            s.Bench.Parts[PartCategory.Solar] = "solar_deployable";
            GameSession.Send(CommandType.RunTest);
            GameSession.Send(CommandType.Launch);
            ApplyOverrides(s.LastLaunch);
            // 換到設計階段時已經開始淡出換場景，那時要求換到發射場景會被忽略：等它換完再換一次
            EditorApplication.update += GoWhenReady;
        }

        static void ApplyOverrides(LaunchRecord r)
        {
            if (r == null) return;
            if (Launcher != "")
            {
                r.Design.LauncherId = Launcher;
                // 共乘只飛晨昏軌道
                if (Launcher == "falcon9_rideshare") r.Design.OrbitId = "sso_dawn_dusk";
            }
            if (Result == null) return;
            (r.Outcome, r.ReasonCode) = Result.Value switch
            {
                Outcome.LowerOrbit => (LaunchOutcome.LowerOrbit, "launch.lower_orbit"),
                Outcome.Anomaly => (LaunchOutcome.Failure, "launch.anomaly"),
                Outcome.Underspeed => (LaunchOutcome.Failure, "launch.overweight"),
                Outcome.FairingFailure => (LaunchOutcome.FairingFailure, "launch.fairing"),
                Outcome.DeadOnOrbit => (LaunchOutcome.DeadOnOrbit, "launch.dead_on_orbit"),
                _ => (LaunchOutcome.Nominal, "launch.nominal"),
            };
            r.Reached = r.Outcome == LaunchOutcome.Nominal || r.Outcome == LaunchOutcome.LowerOrbit;
        }

        static void GoWhenReady()
        {
            if (!EditorApplication.isPlaying) { EditorApplication.update -= GoWhenReady; return; }
            if (SceneFlow.Loading) return;
            EditorApplication.update -= GoWhenReady;
            if (SceneManager.GetActiveScene().name != SceneFlow.Launch) SceneFlow.Go(SceneFlow.Launch);
        }

        [MenuItem(Root + "Launch Speed ×1", priority = 111)] static void Speed1() => LaunchDirector.DevSpeed = 1f;
        [MenuItem(Root + "Launch Speed ×4", priority = 112)] static void Speed4() => LaunchDirector.DevSpeed = 4f;
        [MenuItem(Root + "Launch Speed ×10", priority = 113)] static void Speed10() => LaunchDirector.DevSpeed = 10f;

        [MenuItem(Root + "Skip Launch to End", priority = 120)]
        static void SkipToEnd()
        {
            var d = Object.FindAnyObjectByType<LaunchDirector>();
            if (d != null) d.Skip();
        }

        // ---- 指定火箭、發射結果 ----
        [MenuItem(Root + "Launcher/Mission Default", priority = 200)] static void LDefault() => Launcher = "";
        [MenuItem(Root + "Launcher/Electron", priority = 201)] static void L0() => Launcher = Launchers[0];
        [MenuItem(Root + "Launcher/Falcon 9", priority = 202)] static void L1() => Launcher = Launchers[1];
        [MenuItem(Root + "Launcher/Falcon Heavy", priority = 203)] static void L2() => Launcher = Launchers[2];
        [MenuItem(Root + "Launcher/Taurus XL", priority = 204)] static void L3() => Launcher = Launchers[3];
        [MenuItem(Root + "Launcher/Delta II", priority = 205)] static void L4() => Launcher = Launchers[4];
        [MenuItem(Root + "Launcher/Falcon 9 Rideshare", priority = 206)] static void L5() => Launcher = Launchers[5];

        [MenuItem(Root + "Outcome/As Simulated", priority = 300)] static void ODefault() => Result = null;
        [MenuItem(Root + "Outcome/Nominal", priority = 301)] static void O0() => Result = Outcome.Nominal;
        [MenuItem(Root + "Outcome/Lower Orbit", priority = 302)] static void O1() => Result = Outcome.LowerOrbit;
        [MenuItem(Root + "Outcome/Anomaly", priority = 303)] static void O2() => Result = Outcome.Anomaly;
        [MenuItem(Root + "Outcome/Underspeed", priority = 304)] static void O3() => Result = Outcome.Underspeed;
        [MenuItem(Root + "Outcome/Fairing Failure", priority = 305)] static void O4() => Result = Outcome.FairingFailure;
        [MenuItem(Root + "Outcome/Dead On Orbit", priority = 306)] static void O5() => Result = Outcome.DeadOnOrbit;

        [MenuItem(Root + "Test Launch", true)]
        [MenuItem(Root + "Skip Launch to End", true)]
        static bool PlayingOnly() => EditorApplication.isPlaying;

        [MenuItem(Root + "Launch Speed ×1", true)]
        [MenuItem(Root + "Launch Speed ×4", true)]
        [MenuItem(Root + "Launch Speed ×10", true)]
        static bool SpeedValidate()
        {
            Menu.SetChecked(Root + "Launch Speed ×1", LaunchDirector.DevSpeed <= 1f);
            Menu.SetChecked(Root + "Launch Speed ×4", Mathf.Approximately(LaunchDirector.DevSpeed, 4f));
            Menu.SetChecked(Root + "Launch Speed ×10", Mathf.Approximately(LaunchDirector.DevSpeed, 10f));
            return true;
        }
    }
}
