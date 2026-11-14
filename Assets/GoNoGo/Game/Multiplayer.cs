using System;

namespace GoNoGo.Game
{
    /// <summary>
    /// 多人連線的入口。GoNoGo.Net 組件在載入時註冊實作；主選單（UI 組件）只呼叫這裡，不直接引用網路套件。
    /// </summary>
    public static class Multiplayer
    {
        /// <summary>建立房間：情境、難度、完成或失敗時的回呼（錯誤訊息字串鍵，空字串 = 成功）。</summary>
        public static Action<string, string, Action<string>> Host;
        /// <summary>用房間碼加入。</summary>
        public static Action<string, Action<string>> Join;
        /// <summary>區網直連（測試用）：房主 IP。</summary>
        public static Action<string, Action<string>> JoinDirect;
        /// <summary>區網直連建房。</summary>
        public static Action<string, string, Action<string>> HostDirect;

        public static bool Available => Host != null;
    }
}
