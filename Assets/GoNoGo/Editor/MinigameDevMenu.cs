using GoNoGo.Game;
using GoNoGo.Core;
using UnityEditor;
using UnityEngine;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 營運小遊戲的除錯入口：選單 GoNoGo/Dev/Minigames/Open Picker。
    /// 不在 Play Mode 就先進 Play Mode，等標題頁載好後自動開一局單人任務、進中控室並打開小遊戲選單（也可以在 Facility 裡隨時按 F9）。
    /// </summary>
    [InitializeOnLoad]
    public static class MinigameDevMenu
    {
        const string Root = "GoNoGo/Dev/Minigames/";
        const string Pending = "GoNoGo.MinigamePickerPending";
        static double _deadline;

        static MinigameDevMenu()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s == PlayModeStateChange.EnteredPlayMode && UnityEditor.SessionState.GetBool(Pending, false))
                {
                    UnityEditor.SessionState.SetBool(Pending, false);
                    _deadline = EditorApplication.timeSinceStartup + 30;
                    EditorApplication.update += GoWhenReady;
                }
            };
        }

        [MenuItem(Root + "Open Picker", priority = 100)]
        static void OpenPicker()
        {
            if (EditorApplication.isPlaying) { Go(); return; }
            UnityEditor.SessionState.SetBool(Pending, true);
            EditorApplication.EnterPlaymode();
        }

        static void GoWhenReady()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup > _deadline) { EditorApplication.update -= GoWhenReady; return; }
            if (SceneFlow.Loading || GameSession.Data == null) return;
            EditorApplication.update -= GoWhenReady;
            Go();
        }

        static void Go()
        {
            MinigameDev.PickerOnStart = true;
            if (GameSession.HasMission && UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == SceneFlow.Facility)
            {
                // 已經在中控室：直接叫面板（面板請求事件）
                MinigameDev.PickerOnStart = false;
                Hud.Open("minigame_debug");
                return;
            }
            GameSession.StartSolo("earth_observation", "engineer", false, true);
        }
    }
}
