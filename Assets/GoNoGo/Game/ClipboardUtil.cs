using System;
using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace GoNoGo.Game
{
    /// <summary>
    /// 系統剪貼簿（分享設計用）。桌面版和 Editor 用 Unity 的 systemCopyBuffer；
    /// 網頁版的 systemCopyBuffer 只是 Unity 自己的緩衝區，碰不到瀏覽器的剪貼簿，要另外呼叫瀏覽器的 API
    /// （Plugins/GoNoGoClipboard.jslib）。讀取在瀏覽器是非同步的，結果送回 <see cref="GameSession"/> 的 OnClipboardText。
    /// </summary>
    public static class ClipboardUtil
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void GoNoGoCopyText(string text);
        [DllImport("__Internal")] static extern void GoNoGoReadText(string gameObject, string method);
#endif

        static Action<string> _pending;

        public static void Copy(string text)
        {
            text ??= "";
            GUIUtility.systemCopyBuffer = text;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { GoNoGoCopyText(text); } catch (Exception) { /* 瀏覽器不給用：至少 Unity 的緩衝區有了 */ }
#endif
        }

        /// <summary>讀剪貼簿的文字。桌面版立刻回呼；網頁版等瀏覽器回來（使用者拒絕的話回空字串）。</summary>
        public static void Paste(Action<string> done)
        {
            if (done == null) return;
#if UNITY_WEBGL && !UNITY_EDITOR
            _pending = done;
            try { GoNoGoReadText(GameSession.Instance.gameObject.name, "OnClipboardText"); }
            catch (Exception) { _pending = null; done(GUIUtility.systemCopyBuffer ?? ""); }
#else
            done(GUIUtility.systemCopyBuffer ?? "");
#endif
        }

        /// <summary>網頁版：瀏覽器把剪貼簿內容送回來了。</summary>
        public static void Deliver(string text)
        {
            var cb = _pending;
            _pending = null;
            cb?.Invoke(text ?? "");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _pending = null;
    }
}
