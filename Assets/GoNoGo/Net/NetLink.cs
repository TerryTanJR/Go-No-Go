using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GoNoGo.Core;
using GoNoGo.Game;
using Newtonsoft.Json;
using Unity.Collections;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using SessionState = GoNoGo.Core.SessionState;

namespace GoNoGo.Net
{
    /// <summary>
    /// 多人模式的 <see cref="ISessionLink"/>：房主說了算。
    /// <list type="bullet">
    /// <item>客戶端把操作送給房主（gng.cmd），房主套用規則後把整包狀態廣播給大家（gng.state）；被拒絕的操作只回給送的人（gng.rej）。</item>
    /// <item>位置每秒 10 次：客戶端送自己的給房主，房主把所有人的位置一起廣播（gng.pose，不保證送達）。</item>
    /// <item>不用 NetworkObject 也不用 Netcode 的場景管理：每台電腦依階段自己換場景（<see cref="GameSession.SceneFor"/>）。</item>
    /// </list>
    /// 玩家編號 = Netcode clientId + 1（房主是 1）。
    /// </summary>
    public sealed class NetLink : ISessionLink
    {
        const string MsgState = "gng.state", MsgCmd = "gng.cmd", MsgRej = "gng.rej", MsgPose = "gng.pose";
        const float PoseInterval = 0.1f, ConnectTimeout = 15f;

        readonly NetworkManager _nm;
        readonly ISession _session;
        MissionSession _m;
        Action<string> _connected;
        float _poseTimer, _waited;
        bool _joinSent, _closed;

        public bool IsHost { get; }
        public int LocalPlayer => IsHost ? 1 : (int)_nm.LocalClientId + 1;
        public string RoomCode { get; }

        /// <summary>房主：狀態已經在本機建好。</summary>
        public static NetLink Host(NetworkManager nm, ISession session, string roomCode, MissionSession m) =>
            new NetLink(nm, session, roomCode, true, m, null);

        /// <summary>客戶端：連上後送 Join，收到第一包狀態時呼叫 connected("")；失敗呼叫 connected(錯誤字串鍵)。</summary>
        public static NetLink Client(NetworkManager nm, ISession session, string roomCode, Action<string> connected) =>
            new NetLink(nm, session, roomCode, false, null, connected);

        NetLink(NetworkManager nm, ISession session, string roomCode, bool host, MissionSession m, Action<string> connected)
        {
            _nm = nm;
            _session = session;
            RoomCode = roomCode ?? "";
            IsHost = host;
            _m = m;
            _connected = connected;

            var cm = _nm.CustomMessagingManager;
            if (host)
            {
                cm.RegisterNamedMessageHandler(MsgCmd, OnCommand);
                cm.RegisterNamedMessageHandler(MsgPose, OnClientPose);
            }
            else
            {
                cm.RegisterNamedMessageHandler(MsgState, OnState);
                cm.RegisterNamedMessageHandler(MsgRej, OnReject);
                cm.RegisterNamedMessageHandler(MsgPose, OnPoses);
            }
            _nm.OnClientDisconnectCallback += OnDisconnect;
            if (_session != null)
            {
                _session.RemovedFromSession += OnRemoved;
                _session.Deleted += OnRemoved;
            }
            PoseFeed.Remote.Clear();
        }

        // ---------------------------------------------------------------- 操作

        public void Send(Command c)
        {
            if (_closed) return;
            if (IsHost)
            {
                HostApply(c, NetworkManager.ServerClientId);
                return;
            }
            if (_m == null && c.Type != CommandType.Join) return;
            SendBytes(MsgCmd, NetworkManager.ServerClientId, Encode(JsonConvert.SerializeObject(c)), NetworkDelivery.ReliableFragmentedSequenced);
        }

        void HostApply(Command c, ulong sender)
        {
            if (_m == null) return;
            // 不信任客戶端填的玩家編號
            c.Player = (int)sender + 1;
            var before = Names(_m.S);
            var r = _m.Apply(c);
            if (r != SessionRules.Ok)
            {
                if (sender == NetworkManager.ServerClientId) _m.RaiseRejected(r);
                else SendBytes(MsgRej, sender, Encode(r), NetworkDelivery.ReliableSequenced);
                return;
            }
            AnnounceRoster(before, _m.S);
            Broadcast();
        }

        void Broadcast()
        {
            var ids = Others();
            if (ids.Count == 0) return;
            var bytes = Encode(_m.S.ToJson());
            using var w = Writer(bytes.Length);
            w.WriteValueSafe(bytes);
            _nm.CustomMessagingManager.SendNamedMessage(MsgState, ids, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        List<ulong> Others() => _nm.ConnectedClientsIds.Where(id => id != NetworkManager.ServerClientId).ToList();

        // ---------------------------------------------------------------- 房主收

        void OnCommand(ulong sender, FastBufferReader r)
        {
            Command c;
            try
            {
                r.ReadValueSafe(out byte[] bytes);
                c = JsonConvert.DeserializeObject<Command>(Decode(bytes));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] bad command from " + sender + ": " + e.Message);
                return;
            }
            if (c != null) HostApply(c, sender);
        }

        void OnClientPose(ulong sender, FastBufferReader r)
        {
            if (!ReadPose(r, out var p)) return;
            PoseFeed.Remote[(int)sender + 1] = p;
        }

        // ---------------------------------------------------------------- 客戶端收

        void OnState(ulong sender, FastBufferReader r)
        {
            SessionState s;
            try
            {
                r.ReadValueSafe(out byte[] bytes);
                s = SessionState.FromJson(Decode(bytes));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] bad state: " + e.Message);
                return;
            }
            if (s == null || _closed) return;

            if (_m == null)
            {
                // 第一包狀態：開局、換到對應的場景
                _m = new MissionSession(GameSession.Data, s);
                GameSession.Begin(_m, this);
                SceneFlow.Go(GameSession.SceneFor(s.Phase));
                var cb = _connected;
                _connected = null;
                cb?.Invoke("");
                return;
            }
            var before = Names(_m.S);
            _m.Replace(s);
            AnnounceRoster(before, s);
        }

        void OnReject(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out byte[] bytes);
            var code = Decode(bytes);
            if (_m == null)
            {
                // 還沒進房就被拒（多半是房間滿了）
                Fail(code == "err.room_full" ? "ui.net.err.full" : "ui.net.err.connect");
                return;
            }
            _m.RaiseRejected(code);
        }

        void OnPoses(ulong sender, FastBufferReader r)
        {
            r.ReadValueSafe(out int n);
            var seen = new HashSet<int>();
            for (int i = 0; i < n; i++)
            {
                r.ReadValueSafe(out int id);
                if (!ReadPose(r, out var p)) return;
                if (id == LocalPlayer) continue;
                PoseFeed.Remote[id] = p;
                seen.Add(id);
            }
            foreach (var id in PoseFeed.Remote.Keys.Where(k => !seen.Contains(k)).ToList()) PoseFeed.Remote.Remove(id);
        }

        // ---------------------------------------------------------------- 每幀

        public void Tick(float dt)
        {
            if (_closed || _nm == null) return;

            if (!IsHost && !_joinSent && _nm.IsConnectedClient)
            {
                _joinSent = true;
                Send(new Command { Type = CommandType.Join, Look = Profile.Look.Clone() });
            }
            if (!IsHost && _m == null)
            {
                _waited += dt;
                if (_waited > ConnectTimeout) Fail("ui.net.err.timeout");
                return;
            }

            _poseTimer -= dt;
            if (_poseTimer > 0) return;
            _poseTimer = PoseInterval;
            if (IsHost) BroadcastPoses();
            else SendPose();
        }

        void SendPose()
        {
            using var w = Writer(128);
            WritePose(w, PoseFeed.Local);
            _nm.CustomMessagingManager.SendNamedMessage(MsgPose, NetworkManager.ServerClientId, w, NetworkDelivery.UnreliableSequenced);
        }

        void BroadcastPoses()
        {
            var ids = Others();
            if (ids.Count == 0) return;
            var all = new List<KeyValuePair<int, PoseFeed.Pose>> { new KeyValuePair<int, PoseFeed.Pose>(1, PoseFeed.Local) };
            all.AddRange(PoseFeed.Remote);
            using var w = Writer(64 + all.Count * 128);
            w.WriteValueSafe(all.Count);
            foreach (var kv in all)
            {
                w.WriteValueSafe(kv.Key);
                WritePose(w, kv.Value);
            }
            _nm.CustomMessagingManager.SendNamedMessage(MsgPose, ids, w, NetworkDelivery.UnreliableSequenced);
        }

        static void WritePose(FastBufferWriter w, PoseFeed.Pose p)
        {
            w.WriteValueSafe(p.Position);
            w.WriteValueSafe(p.Yaw);
            w.WriteValueSafe(p.Speed);
            w.WriteValueSafe(p.Installing);
            w.WriteValueSafe((byte)p.Emote);
            w.WriteValueSafe(p.EmoteSeq);
        }

        static bool ReadPose(FastBufferReader r, out PoseFeed.Pose p)
        {
            p = default;
            try
            {
                r.ReadValueSafe(out Vector3 pos);
                r.ReadValueSafe(out float yaw);
                r.ReadValueSafe(out float speed);
                r.ReadValueSafe(out bool installing);
                r.ReadValueSafe(out byte emote);
                r.ReadValueSafe(out byte emoteSeq);
                p = new PoseFeed.Pose { Position = pos, Yaw = yaw, Speed = speed, Installing = installing, Emote = (Emote)emote, EmoteSeq = emoteSeq };
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ---------------------------------------------------------------- 有人來、有人走

        void OnDisconnect(ulong clientId)
        {
            if (_closed) return;
            if (IsHost)
            {
                if (clientId == NetworkManager.ServerClientId) return;
                PoseFeed.Remote.Remove((int)clientId + 1);
                HostApply(new Command { Type = CommandType.Leave }, clientId);
                return;
            }
            // 客戶端：自己斷線（房主離開、網路斷了）
            if (clientId != _nm.LocalClientId && clientId != NetworkManager.ServerClientId) return;
            if (_m == null) { Fail("ui.net.err.connect"); return; }
            HostGone();
        }

        void OnRemoved()
        {
            if (_closed || IsHost) return;
            if (_m == null) Fail("ui.net.err.connect");
            else HostGone();
        }

        void HostGone()
        {
            Hud.Toast(Loc.T("ui.net.host_left"));
            GameSession.EndMission();
        }

        void Fail(string err)
        {
            var cb = _connected;
            _connected = null;
            Shutdown();
            cb?.Invoke(err);
        }

        static Dictionary<int, string> Names(SessionState s) => s.Players.ToDictionary(p => p.Id, p => p.Look?.Name ?? "?");

        void AnnounceRoster(Dictionary<int, string> before, SessionState now)
        {
            foreach (var p in now.Players)
                if (!before.ContainsKey(p.Id) && p.Id != LocalPlayer)
                    Hud.Toast(Loc.T("ui.net.joined", ("name", p.Look?.Name ?? "?")));
            foreach (var kv in before)
                if (now.Players.All(p => p.Id != kv.Key))
                    Hud.Toast(Loc.T("ui.net.left", ("name", kv.Value)));
        }

        // ---------------------------------------------------------------- 結束

        public void Shutdown()
        {
            if (_closed) return;
            _closed = true;
            PoseFeed.Remote.Clear();
            if (_nm != null)
            {
                _nm.OnClientDisconnectCallback -= OnDisconnect;
                var cm = _nm.CustomMessagingManager;
                if (cm != null)
                {
                    foreach (var name in new[] { MsgState, MsgCmd, MsgRej, MsgPose })
                        cm.UnregisterNamedMessageHandler(name);
                }
            }
            if (_session != null)
            {
                _session.RemovedFromSession -= OnRemoved;
                _session.Deleted -= OnRemoved;
                VoiceChat.Leave();
            }
            _ = Close(_nm, _session);
        }

        static async Task Close(NetworkManager nm, ISession session)
        {
            if (session != null)
            {
                try { await session.LeaveAsync(); }
                catch (Exception e) { Debug.LogWarning("[Net] leave: " + e.Message); }
            }
            if (nm != null && nm.IsListening) nm.Shutdown();
        }

        // ---------------------------------------------------------------- 小工具

        static byte[] Encode(string s) => Encoding.UTF8.GetBytes(s ?? "");
        static string Decode(byte[] b) => b == null ? "" : Encoding.UTF8.GetString(b);

        static FastBufferWriter Writer(int size) => new FastBufferWriter(size + 16, Allocator.Temp, Math.Max(size + 16, 1 << 20));

        void SendBytes(string msg, ulong to, byte[] bytes, NetworkDelivery delivery)
        {
            using var w = Writer(bytes.Length);
            w.WriteValueSafe(bytes);
            _nm.CustomMessagingManager.SendNamedMessage(msg, to, w, delivery);
        }
    }
}
