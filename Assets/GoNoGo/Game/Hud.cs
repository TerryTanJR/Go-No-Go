using System;
using System.Collections.Generic;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>按鍵提示的一格：鍵帽文字 + 小字說明。</summary>
    public struct KeyHint
    {
        public string Key;
        public string Label;
        public KeyHint(string key, string label) { Key = key; Label = label; }
    }

    /// <summary>準星對到的東西：資訊框的內容與可以按的鍵。</summary>
    public sealed class HudTarget
    {
        public string Title = "";
        public string Subtitle = "";
        public string Body = "";
        public string Color = "";
        public List<KeyHint> Keys = new List<KeyHint>();
    }

    /// <summary>
    /// 3D 互動（Presentation）和介面（UI）之間的溝通：兩邊互不引用，只透過這裡。
    /// </summary>
    public static class Hud
    {
        /// <summary>準星對到的東西（null = 沒有）。</summary>
        public static HudTarget Target;
        /// <summary>長按 V 安裝的進度 0–1（0 = 沒在按）。</summary>
        public static float Progress;
        /// <summary>目前是不是第一人稱（預設第一人稱，Q 或選單切換）。</summary>
        public static bool FirstPerson = true;
        /// <summary>目前在哪一區（"assembly"、"control"、"lobby"），畫面左上角顯示。</summary>
        public static string Area = "";
        /// <summary>人站在哪一座組裝台旁邊（0 或 1；-1 = 不在旁邊）。畫面右上角顯示那一座的組裝單。</summary>
        public static int NearBench = -1;

        /// <summary>開著面板（控制台、線索、平板…）：角色不能動、滑鼠游標放開。</summary>
        public static bool PanelOpen;
        /// <summary>剛關掉面板的那一幀（同一個 E 不要又打開控制台）。</summary>
        public static int ClosedFrame = -1;
        /// <summary>遊戲選單（Tab / Esc）開著：角色不能動。多人時遊戲本身不會停。</summary>
        public static bool Paused;
        /// <summary>進場過場蓋著畫面（UI 層寫入）：場景已經載好也還不能動，等進度條跑完。</summary>
        public static bool Covered;
        public static bool InputBlocked => PanelOpen || Paused || Covered || SceneFlow.Loading;

        /// <summary>互動物件要求打開某個面板（面板 id、參數）。</summary>
        public static event Action<string, string> PanelRequested;
        public static void Open(string panel, string arg = "") => PanelRequested?.Invoke(panel, arg);

        /// <summary>畫面上方的通知。</summary>
        public static event Action<string> ToastRequested;
        public static void Toast(string text) => ToastRequested?.Invoke(text);

        /// <summary>新手訓練：完成某個動作（"look"、"pick"、"install"…）。</summary>
        public static event Action<string> TutorialSignal;
        public static void Signal(string what) => TutorialSignal?.Invoke(what);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Target = null;
            Progress = 0;
            FirstPerson = true;
            PanelOpen = false;
            Paused = false;
            Covered = false;
            ClosedFrame = -1;
            Area = "";
            NearBench = -1;
            PanelRequested = null;
            ToastRequested = null;
            TutorialSignal = null;
        }
    }

    /// <summary>多人：其他玩家的位置（GoNoGo.Net 寫入，Presentation 讀取）與本機玩家的位置（反過來）。</summary>
    public static class PoseFeed
    {
        public struct Pose
        {
            public Vector3 Position;
            public float Yaw;
            public float Speed;
            public bool Installing;
        }

        public static Pose Local;
        public static readonly Dictionary<int, Pose> Remote = new Dictionary<int, Pose>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Local = default;
            Remote.Clear();
        }
    }
}
