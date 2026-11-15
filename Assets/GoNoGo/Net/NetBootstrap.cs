using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using GoNoGo.Core;
using GoNoGo.Game;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace GoNoGo.Net
{
    /// <summary>
    /// 把連線功能接到 <see cref="Multiplayer"/>（主選單只認得那裡）。
    /// <list type="bullet">
    /// <item>房間碼：Unity 的多人連線服務（房主建立工作階段、經由轉送伺服器連線，瀏覽器也能玩）。專案要先連結 Unity Cloud。</item>
    /// <item>區網直連：不需要雲端，測試用；房主要是桌面版或編輯器（瀏覽器不能開伺服器）。</item>
    /// </list>
    /// </summary>
    public static class NetBootstrap
    {
        public const ushort DirectPort = 7777;
        static bool _busy;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            _busy = false;
            Multiplayer.Host = (scenario, difficulty, done) => Run(done, () => HostRelay(scenario, difficulty));
            Multiplayer.Join = (code, done) => Run(done, () => JoinRelay(code));
            Multiplayer.HostDirect = (scenario, difficulty, done) => Run(done, () => HostDirect(scenario, difficulty));
            Multiplayer.JoinDirect = (ip, done) => Run(done, () => JoinDirect(ip));
        }

        /// <summary>
        /// 測試用：命令列 <c>-gng-join 127.0.0.1</c> 開起來就用 IP 加入，<c>-gng-code ABC123</c> 用房間碼加入（可加 <c>-gng-name 名字</c>），
        /// 加 <c>-gng-control</c> 會在進房後要求去中控室（測試客戶端送操作給房主）；<c>-gng-host</c> 直接用 IP 當房主。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CommandLineJoin()
        {
            var args = new System.Collections.Generic.List<string>(Environment.GetCommandLineArgs());
            // 瀏覽器版沒有命令列：網址加 ?gng-join=192.168.1.20&gng-name=名字 效果一樣
            var url = Application.absoluteURL ?? "";
            int q = url.IndexOf('?');
            if (q >= 0)
                foreach (var pair in url.Substring(q + 1).Split('&'))
                {
                    var kv = pair.Split('=');
                    args.Add("-" + Uri.UnescapeDataString(kv[0]));
                    if (kv.Length > 1) args.Add(Uri.UnescapeDataString(kv[1]));
                }
            string Arg(string name)
            {
                int i = args.IndexOf(name);
                return i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
            }
            if (args.Contains("-gng-host"))
            {
                Multiplayer.HostDirect("earth_observation", "engineer", err => Debug.Log("[Net] command-line host: " + (string.IsNullOrEmpty(err) ? "listening" : err)));
                return;
            }
            var code = Arg("-gng-code");
            var ip = Arg("-gng-join");
            if (ip == null && code == null) return;
            var name = Arg("-gng-name");
            if (!string.IsNullOrEmpty(name))
            {
                var look = Profile.Look.Clone();
                look.Name = name;
                Profile.Save(look);
            }
            Action<string, Action<string>> join = code != null ? Multiplayer.Join : Multiplayer.JoinDirect;
            join(code ?? ip, err =>
            {
                Debug.Log("[Net] command-line join: " + (string.IsNullOrEmpty(err) ? "connected as player " + GameSession.LocalPlayer : err));
            });
        }

        sealed class Delay : MonoBehaviour
        {
            float _t;
            Action _a;

            public static void After(float seconds, Action a)
            {
                var go = new GameObject("[NetDelay]");
                DontDestroyOnLoad(go);
                var d = go.AddComponent<Delay>();
                d._t = seconds;
                d._a = a;
            }

            void Update()
            {
                _t -= Time.unscaledDeltaTime;
                if (_t > 0) return;
                Destroy(gameObject);
                _a?.Invoke();
            }
        }

        /// <summary>跑一個連線流程；回呼一定在主執行緒、一定只呼叫一次。</summary>
        static async void Run(Action<string> done, Func<Task<string>> flow)
        {
            if (_busy) return;
            _busy = true;
            string err;
            try
            {
                err = await flow();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] " + e);
                err = "ui.net.err.connect";
            }
            _busy = false;
            // 成功的客戶端由 NetLink 收到第一包狀態時回呼；這裡只回報失敗或房主成功
            if (err != null) done?.Invoke(err);
        }

        // ---------------------------------------------------------------- 房間碼

        static async Task<string> SignIn()
        {
            if (string.IsNullOrEmpty(Application.cloudProjectId)) return "ui.net.err.cloud";
            try
            {
                if (UnityServices.State != ServicesInitializationState.Initialized)
                {
                    var options = new InitializationOptions();
#if UNITY_EDITOR
                    // 同一台電腦開好幾個編輯器測試時，每個要用不同的匿名身分
                    options.SetProfile("p" + UnityEngine.Random.Range(0, 99999));
#endif
                    await UnityServices.InitializeAsync(options);
                }
                if (!AuthenticationService.Instance.IsSignedIn)
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                return null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] sign-in: " + e.Message);
                return "ui.net.err.cloud";
            }
        }

        /// <summary>
        /// 轉送協定明確指定，和下面 <see cref="Manager"/> 的 UseWebSockets 一致：瀏覽器和編輯器 WSS、桌面版 DTLS（轉送伺服器兩種都接受）。
        /// 編輯器一律 WSS：編輯器裡的多人套件可能是照上次的 WebGL 平台編的，只接受 WSS（會把 DTLS 自動換成 WSS，
        /// 跟 UseWebSockets=false 對不上，房主就建不起來）；WSS 在所有平台都合法。
        /// </summary>
        static NetworkOptions RelayNet() => new NetworkOptions
        {
#if UNITY_WEBGL || UNITY_EDITOR
            RelayProtocol = RelayProtocol.WSS,
#else
            RelayProtocol = RelayProtocol.DTLS,
#endif
        };

        static async Task<string> HostRelay(string scenario, string difficulty)
        {
            var err = await SignIn();
            if (err != null) return err;
            var nm = Manager();
            var options = new SessionOptions { MaxPlayers = 4, IsPrivate = true }.WithRelayNetwork().WithNetworkOptions(RelayNet());
            IHostSession session;
            try
            {
                session = await MultiplayerService.Instance.CreateSessionAsync(options);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Net] create: " + e.Message);
                return "ui.net.err.create";
            }
            StartAsHost(nm, session, session.Code, scenario, difficulty);
            VoiceChat.Join(session.Code);
            return "";
        }

        static async Task<string> JoinRelay(string code)
        {
            var err = await SignIn();
            if (err != null) return err;
            var nm = Manager();
            ISession session;
            try
            {
                session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant(),
                    new JoinSessionOptions().WithNetworkOptions(RelayNet()));
            }
            catch (SessionException e)
            {
                Debug.LogWarning("[Net] join: " + e.Error + " " + e.Message);
                return e.Error == SessionError.SessionNotFound || e.Message.Contains("404") ? "ui.net.err.code" : "ui.net.err.connect";
            }
            err = await WaitForState(NetLinkClient(nm, session, session.Code));
            if (string.IsNullOrEmpty(err)) VoiceChat.Join(session.Code);
            return err;
        }

        // ---------------------------------------------------------------- 區網直連

        static Task<string> HostDirect(string scenario, string difficulty)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return Task.FromResult("ui.net.err.web_host");
#else
            var nm = Manager(true);
            Transport(nm).SetConnectionData("127.0.0.1", DirectPort, "0.0.0.0");
            if (!nm.StartHost()) return Task.FromResult("ui.net.err.port");
            StartAsHost(nm, null, LocalIp(), scenario, difficulty);
            return Task.FromResult("");
#endif
        }

        static Task<string> JoinDirect(string ip)
        {
            ip = string.IsNullOrWhiteSpace(ip) ? "127.0.0.1" : ip.Trim();
            if (!IPAddress.TryParse(ip, out _)) return Task.FromResult("ui.net.err.ip");
            var nm = Manager(true);
            Transport(nm).SetConnectionData(ip, DirectPort);
            if (!nm.StartClient()) return Task.FromResult("ui.net.err.connect");
            return WaitForState(NetLinkClient(nm, null, ip));
        }

        // ---------------------------------------------------------------- 共用

        static void StartAsHost(NetworkManager nm, ISession session, string code, string scenario, string difficulty)
        {
            var data = GameSession.Data;
            var s = SessionRules.NewSession(data, scenario, difficulty, false, true, GameSession.NewClueSeed());
            var m = new MissionSession(data, s);
            GameSession.Begin(m, NetLink.Host(nm, session, code, m));
            GameSession.Send(new Command { Type = CommandType.Join, Look = Profile.Look.Clone() });
            SceneFlow.Go(SceneFlow.Facility);
        }

        static TaskCompletionSource<string> _clientResult;

        static NetLink NetLinkClient(NetworkManager nm, ISession session, string code)
        {
            _clientResult = new TaskCompletionSource<string>();
            var tcs = _clientResult;
            var link = NetLink.Client(nm, session, code, err => tcs.TrySetResult(err));
            // 收到第一包狀態之前，GameSession 還沒有這個 link，要自己推它的 Tick
            Pump.Run(link, tcs.Task);
            return link;
        }

        static async Task<string> WaitForState(NetLink link)
        {
            var err = await _clientResult.Task;
            // 成功時 NetLink 已經開局、換場景；主選單的回呼也要清掉「連線中」
            return err;
        }

        /// <param name="direct">同一個網路：一律走 WebSocket，瀏覽器才能加入桌面版或編輯器開的房間。
        /// 房間碼（轉送）：瀏覽器和編輯器用 WSS、桌面版用 DTLS，轉送伺服器兩種都接受（見 <see cref="RelayNet"/>）。</param>
        static NetworkManager Manager(bool direct = false)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null)
            {
                var go = new GameObject("[Network]");
                UnityEngine.Object.DontDestroyOnLoad(go);
                var transport = go.AddComponent<UnityTransport>();
                // 有人當機或關掉視窗：10 秒就判定離開（預設 30 秒，大家會以為他還在）
                transport.DisconnectTimeoutMS = 10000;
                transport.ConnectTimeoutMS = 1000;
                transport.MaxConnectAttempts = 8;
                nm = go.AddComponent<NetworkManager>();
                nm.NetworkConfig = new NetworkConfig
                {
                    NetworkTransport = transport,
                    EnableSceneManagement = false,
                    ConnectionApproval = false,
                    TickRate = 30,
                };
                nm.SetSingleton();
            }
            if (nm.IsListening) nm.Shutdown();
#if UNITY_WEBGL || UNITY_EDITOR
            Transport(nm).UseWebSockets = true;
#else
            Transport(nm).UseWebSockets = direct;
#endif
            return nm;
        }

        static UnityTransport Transport(NetworkManager nm) => (UnityTransport)nm.NetworkConfig.NetworkTransport;

        /// <summary>這台電腦在區網上的 IP：優先選有預設閘道的網卡（略過 VMware、Hyper-V 等虛擬網卡）。</summary>
        static string LocalIp()
        {
            try
            {
                string fallback = null;
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = nic.GetIPProperties();
                    var v4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
                    if (v4 == null) continue;
                    bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    if (gateway) return v4.ToString();
                    fallback ??= v4.ToString();
                }
                return fallback ?? "127.0.0.1";
            }
            catch (Exception)
            {
                return "127.0.0.1";
            }
        }

        /// <summary>連線中（還沒開局）時推 NetLink 的 Tick。</summary>
        sealed class Pump : MonoBehaviour
        {
            NetLink _link;
            Task _until;

            public static void Run(NetLink link, Task until)
            {
                var go = new GameObject("[NetPump]");
                DontDestroyOnLoad(go);
                var p = go.AddComponent<Pump>();
                p._link = link;
                p._until = until;
            }

            void Update()
            {
                if (_until.IsCompleted) { Destroy(gameObject); return; }
                _link.Tick(Time.unscaledDeltaTime);
            }
        }
    }
}
