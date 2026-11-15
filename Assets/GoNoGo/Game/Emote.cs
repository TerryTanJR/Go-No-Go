namespace GoNoGo.Game
{
    /// <summary>角色的表情動作（按住 X 的輪盤選）。數值會跟著位置一起送給其他玩家，不要改已經有的號碼。</summary>
    public enum Emote : byte
    {
        None = 0,
        Wave = 1,
        Cheer = 2,
        Laugh = 3,
        Nod = 4,
    }

    public static class Emotes
    {
        /// <summary>輪盤上的順序：從正上方開始順時針。</summary>
        public static readonly Emote[] Wheel = { Emote.Wave, Emote.Cheer, Emote.Laugh, Emote.Nod };

        /// <summary>一個動作做多久（秒）。</summary>
        public const float Seconds = 2.2f;

        /// <summary>輪盤上的名稱（strings.json 的 ui.emote.wave …）。</summary>
        public static string NameKey(Emote e) => "ui.emote." + e.ToString().ToLowerInvariant();

        /// <summary>做動作時頭上冒出來的那句話。</summary>
        public static string SayKey(Emote e) => NameKey(e) + ".say";
    }
}
