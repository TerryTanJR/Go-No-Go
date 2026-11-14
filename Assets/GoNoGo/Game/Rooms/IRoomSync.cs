using System;
using System.Collections.Generic;
using GoNoGo.Core;

namespace GoNoGo.Game.Rooms
{
    /// <summary>房間裡的一位玩家。</summary>
    [Serializable]
    public sealed class RoomMember
    {
        public string Uid = "";
        public string Name = "";
        /// <summary>認領的角色（director / engineer / scientist / comms），空字串 = 還沒選。</summary>
        public string Role = "";
        public bool Ready;
        public long LastSeenMs;
    }

    /// <summary>
    /// 房間的完整狀態（和網頁版共用同一個結構，存在 Firebase Realtime Database 的 /rooms/{code}）。
    /// </summary>
    [Serializable]
    public sealed class RoomSnapshot
    {
        public string Code = "";
        public string HostUid = "";
        public string ScenarioId = "";
        public string DifficultyId = "";
        /// <summary>title / briefing / design / rollcall / launch / operations / report。</summary>
        public string Phase = "title";
        public int Seed;
        /// <summary>設計（Design.ToJson()）；任何角色修改都整份覆寫，用 ETag 避免互相覆蓋。</summary>
        public string DesignJson = "";
        public Dictionary<string, RoomMember> Members = new Dictionary<string, RoomMember>();
        /// <summary>角色 → "go" / "nogo:{reason}"。</summary>
        public Dictionary<string, string> Votes = new Dictionary<string, string>();
        /// <summary>營運事件的選擇（事件 id → 選項），由指揮決定後寫入。</summary>
        public Dictionary<string, string> Choices = new Dictionary<string, string>();
        public long UpdatedMs;
    }

    /// <summary>
    /// 房間同步介面。單人版用 <see cref="LocalRoomSync"/>（全部在本機），
    /// 線上四人版用 <see cref="FirebaseRestRoomSync"/>（Firebase REST，未啟用）。
    /// 遊戲邏輯只透過這個介面同步，所以換成線上版不用改 GameState。
    /// </summary>
    public interface IRoomSync
    {
        bool IsOnline { get; }
        string RoomCode { get; }
        string LocalUid { get; }
        RoomSnapshot Snapshot { get; }

        /// <summary>房間狀態改變（包含自己的修改）。</summary>
        event Action<RoomSnapshot> Changed;
        /// <summary>連線錯誤（多語鍵 + 細節）。</summary>
        event Action<string, string> Error;

        void CreateRoom(string scenarioId, string difficultyId, Action<bool> done);
        void JoinRoom(string code, string displayName, Action<bool> done);
        void ClaimRole(Role role, Action<bool> done);
        void PushDesign(Design design);
        void PushVote(Role role, Vote vote, string reasonCode);
        void PushPhase(string phase);
        void PushChoice(string eventId, string choice);
        void Leave();
        /// <summary>每幀呼叫（線上版用來輪詢）。</summary>
        void Tick(float deltaTime);
    }
}
