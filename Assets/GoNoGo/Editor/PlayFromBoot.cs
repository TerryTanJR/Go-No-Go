using UnityEditor;
using UnityEditor.SceneManagement;

namespace GoNoGo.EditorTools
{
    /// <summary>
    /// 在 Editor 按 Play 一律從 Boot（標題頁）開始，不管目前開著哪個場景；Play 結束後回到原本開著的場景。
    /// 想直接測目前的場景（例如 Facility 會自己建一局）就把選單 GoNoGo/Play From Title 的勾拿掉，這個選擇存在本機。
    /// </summary>
    [InitializeOnLoad]
    public static class PlayFromBoot
    {
        const string BootScene = "Assets/GoNoGo/Scenes/Boot.unity";
        const string Menu = "GoNoGo/Play From Title";
        const string Pref = "GoNoGo.PlayFromBoot";

        static bool Enabled
        {
            get => EditorPrefs.GetBool(Pref, true);
            set => EditorPrefs.SetBool(Pref, value);
        }

        static PlayFromBoot()
        {
            // 載入時直接設；資產資料庫還沒準備好時（剛開專案）再補一次
            Apply();
            EditorApplication.delayCall += Apply;
            EditorApplication.playModeStateChanged += s => { if (s == PlayModeStateChange.ExitingEditMode) Apply(); };
        }

        static void Apply()
        {
            var boot = Enabled ? AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScene) : null;
            if (EditorSceneManager.playModeStartScene != boot) EditorSceneManager.playModeStartScene = boot;
            UnityEditor.Menu.SetChecked(Menu, Enabled);
        }

        [MenuItem(Menu, priority = 0)]
        static void Toggle()
        {
            Enabled = !Enabled;
            Apply();
        }

        [MenuItem(Menu, true)]
        static bool Menu_Validate()
        {
            UnityEditor.Menu.SetChecked(Menu, Enabled);
            return !EditorApplication.isPlaying;
        }
    }
}
