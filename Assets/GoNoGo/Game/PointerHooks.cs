using System;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 3D 鏡頭控制要知道滑鼠是不是在 UI 上（在 UI 上拖曳不該轉鏡頭）。
    /// UI 組件設定 OverUI，Presentation 組件讀取，兩邊不用互相引用。
    /// </summary>
    public static class PointerHooks
    {
        /// <summary>參數：螢幕座標（左下為原點，與 Input System 相同）。</summary>
        public static Func<Vector2, bool> OverUI;

        public static bool IsOverUI(Vector2 screenPos) => OverUI != null && OverUI(screenPos);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => OverUI = null;
    }
}
