using System;
using GoNoGo.Core;

namespace GoNoGo.Game.Rooms
{
    /// <summary>
    /// 單人版的「房間」：全部在本機記憶體，一個人同時擔任四個角色。
    /// 和線上版走同一個介面，所以畫面與流程不用分兩套。
    /// </summary>
    public sealed class LocalRoomSync : IRoomSync
    {
        public bool IsOnline => false;
        public string RoomCode => Snapshot.Code;
        public string LocalUid => "local";
        public RoomSnapshot Snapshot { get; } = new RoomSnapshot { Code = "SOLO", HostUid = "local" };

        public event Action<RoomSnapshot> Changed;
        public event Action<string, string> Error { add { } remove { } }

        public void CreateRoom(string scenarioId, string difficultyId, Action<bool> done)
        {
            Snapshot.ScenarioId = scenarioId ?? "";
            Snapshot.DifficultyId = difficultyId ?? "";
            Snapshot.Members[LocalUid] = new RoomMember { Uid = LocalUid, Name = "Solo", Role = "all", Ready = true };
            Touch();
            done?.Invoke(true);
        }

        public void JoinRoom(string code, string displayName, Action<bool> done) => done?.Invoke(false);

        public void ClaimRole(Role role, Action<bool> done) => done?.Invoke(true);

        public void PushDesign(Design design)
        {
            Snapshot.DesignJson = design.ToJson();
            Touch();
        }

        public void PushVote(Role role, Vote vote, string reasonCode)
        {
            Snapshot.Votes[Roles.Key(role)] = vote == Vote.Go ? "go" : "nogo:" + reasonCode;
            Touch();
        }

        public void PushPhase(string phase)
        {
            Snapshot.Phase = phase;
            if (phase == "design") Snapshot.Votes.Clear();
            Touch();
        }

        public void PushChoice(string eventId, string choice)
        {
            Snapshot.Choices[eventId] = choice;
            Touch();
        }

        public void Leave() { }

        public void Tick(float deltaTime) { }

        void Touch()
        {
            Snapshot.UpdatedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Changed?.Invoke(Snapshot);
        }
    }
}
