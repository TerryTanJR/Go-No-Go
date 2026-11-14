using GoNoGo.Core;

namespace GoNoGo.Game
{
    /// <summary>
    /// 操作送去哪裡：個人模式直接在本機套用；多人模式送給房主（GoNoGo.Net 實作）。
    /// 畫面和 3D 互動只透過這個介面改狀態，所以個人模式做好的東西接上多人不用改。
    /// </summary>
    public interface ISessionLink
    {
        bool IsHost { get; }
        bool IsOnline { get; }
        int LocalPlayer { get; }
        /// <summary>房間碼（個人模式為空）。</summary>
        string RoomCode { get; }
        void Send(Command c);
        void Tick(float dt);
        void Shutdown();
    }

    public sealed class LocalLink : ISessionLink
    {
        readonly MissionSession _m;

        public LocalLink(MissionSession m, int player = 1)
        {
            _m = m;
            LocalPlayer = player;
        }

        public bool IsHost => true;
        public bool IsOnline => false;
        public int LocalPlayer { get; }
        public string RoomCode => "";

        public void Send(Command c)
        {
            var r = _m.Apply(c);
            if (r != SessionRules.Ok) _m.RaiseRejected(r);
        }

        public void Tick(float dt) { }
        public void Shutdown() { }
    }
}
