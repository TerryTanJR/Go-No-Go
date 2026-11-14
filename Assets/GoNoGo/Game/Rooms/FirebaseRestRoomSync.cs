using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using GoNoGo.Core;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace GoNoGo.Game.Rooms
{
    /// <summary>
    /// Firebase 專案設定。填好 ApiKey 與 DatabaseUrl 才會啟用線上模式。
    /// 單人版留空，所以 <see cref="IsConfigured"/> 是 false，遊戲一律用 LocalRoomSync。
    /// </summary>
    [Serializable]
    public sealed class FirebaseConfig
    {
        /// <summary>Firebase 專案的 Web API Key（公開值，靠 Database Rules 保護）。</summary>
        public string ApiKey = "";
        /// <summary>例如 https://gonogo-xxxx-default-rtdb.asia-southeast1.firebasedatabase.app</summary>
        public string DatabaseUrl = "";
        /// <summary>輪詢間隔（秒）。WebGL 的 UnityWebRequest 不能讀串流（SSE），所以用輪詢 + ETag。</summary>
        public float PollSeconds = 1.0f;

        public bool IsConfigured => !string.IsNullOrEmpty(ApiKey) && !string.IsNullOrEmpty(DatabaseUrl);
    }

    /// <summary>
    /// 所有 REST 端點集中在這裡（網頁版用同樣的路徑）。
    /// </summary>
    public static class FirebaseEndpoints
    {
        /// <summary>匿名登入：POST，body {"returnSecureToken":true} → idToken、refreshToken、localId、expiresIn。</summary>
        public static string SignUpAnonymous(string apiKey) =>
            "https://identitytoolkit.googleapis.com/v1/accounts:signUp?key=" + apiKey;

        /// <summary>換新 idToken（一小時過期）：POST，form grant_type=refresh_token&amp;refresh_token=…</summary>
        public static string RefreshToken(string apiKey) =>
            "https://securetoken.googleapis.com/v1/token?key=" + apiKey;

        /// <summary>整個房間：GET（輪詢，帶 X-Firebase-ETag: true）、PUT（建立）。</summary>
        public static string Room(string db, string code, string idToken) => $"{db.TrimEnd('/')}/rooms/{code}.json?auth={idToken}";

        /// <summary>單一成員：PATCH（加入、心跳）、DELETE（離開）。</summary>
        public static string Member(string db, string code, string uid, string idToken) => $"{db.TrimEnd('/')}/rooms/{code}/members/{uid}.json?auth={idToken}";

        /// <summary>認領角色：PUT 帶 if-match ETag，兩人同時搶同一個角色時後到的會得到 412。</summary>
        public static string RoleClaim(string db, string code, string role, string idToken) => $"{db.TrimEnd('/')}/rooms/{code}/roles/{role}.json?auth={idToken}";

        /// <summary>設計：PUT 整份（帶 if-match，衝突時重新讀取再套用自己的修改）。</summary>
        public static string Design(string db, string code, string idToken) => $"{db.TrimEnd('/')}/rooms/{code}/design_json.json?auth={idToken}";

        /// <summary>投票：PUT "go" 或 "nogo:{reason}"。</summary>
        public static string Vote(string db, string code, string role, string idToken) => $"{db.TrimEnd('/')}/rooms/{code}/votes/{role}.json?auth={idToken}";

        /// <summary>階段：PUT（只有房主寫）。</summary>
        public static string Phase(string db, string code, string idToken) => $"{db.TrimEnd('/')}/rooms/{code}/phase.json?auth={idToken}";

        /// <summary>營運事件選擇：PUT（由指揮寫）。</summary>
        public static string Choice(string db, string code, string eventId, string idToken) => $"{db.TrimEnd('/')}/rooms/{code}/choices/{eventId}.json?auth={idToken}";

        /// <summary>
        /// 建議的 Database Rules（貼到 Firebase 主控台）：
        /// 只有登入者能讀寫自己所在的房間；成員只能改自己的節點；房間 24 小時後由排程清除。
        /// </summary>
        public const string SuggestedRules = @"{
  ""rules"": {
    ""rooms"": {
      ""$code"": {
        "".read"": ""auth != null"",
        "".write"": ""auth != null && (!data.exists() || data.child('members').child(auth.uid).exists())"",
        ""members"": { ""$uid"": { "".write"": ""auth != null && auth.uid === $uid"" } }
      }
    }
  }
}";
    }

    /// <summary>
    /// 線上四人版的房間同步（Firebase Realtime Database REST + Identity Toolkit 匿名登入）。
    /// <para>狀態：<b>未啟用、未在真實 Firebase 專案測試</b>。單人版不會建立這個類別。</para>
    /// <para>啟用步驟：建立 Firebase 專案 → 開啟匿名登入 → 建 Realtime Database 並貼上
    /// <see cref="FirebaseEndpoints.SuggestedRules"/> → 在 GameSession 換成這個類別並填入 <see cref="FirebaseConfig"/>。</para>
    /// </summary>
    public sealed class FirebaseRestRoomSync : IRoomSync
    {
        readonly FirebaseConfig _cfg;
        readonly MonoBehaviour _runner;
        string _idToken = "", _refreshToken = "", _etag = "";
        float _pollTimer, _tokenExpiresIn;
        bool _polling;

        public FirebaseRestRoomSync(FirebaseConfig cfg, MonoBehaviour coroutineRunner)
        {
            _cfg = cfg;
            _runner = coroutineRunner;
        }

        public bool IsOnline => _cfg.IsConfigured && !string.IsNullOrEmpty(_idToken);
        public string RoomCode => Snapshot.Code;
        public string LocalUid { get; private set; } = "";
        public RoomSnapshot Snapshot { get; private set; } = new RoomSnapshot();

        public event Action<RoomSnapshot> Changed;
        public event Action<string, string> Error;

        static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            ContractResolver = new Newtonsoft.Json.Serialization.DefaultContractResolver
            {
                NamingStrategy = new Newtonsoft.Json.Serialization.SnakeCaseNamingStrategy { ProcessDictionaryKeys = false },
            },
            NullValueHandling = NullValueHandling.Ignore,
        };

        // ---------- 房間 ----------

        public void CreateRoom(string scenarioId, string difficultyId, Action<bool> done)
        {
            EnsureSignedIn(ok =>
            {
                if (!ok) { done?.Invoke(false); return; }
                Snapshot = new RoomSnapshot
                {
                    Code = NewCode(),
                    HostUid = LocalUid,
                    ScenarioId = scenarioId,
                    DifficultyId = difficultyId,
                    Seed = UnityEngine.Random.Range(1, int.MaxValue),
                };
                Snapshot.Members[LocalUid] = new RoomMember { Uid = LocalUid, Name = "Host", LastSeenMs = Now() };
                Send("PUT", FirebaseEndpoints.Room(_cfg.DatabaseUrl, Snapshot.Code, _idToken), Serialize(Snapshot), null, r =>
                {
                    _polling = r;
                    done?.Invoke(r);
                });
            });
        }

        public void JoinRoom(string code, string displayName, Action<bool> done)
        {
            EnsureSignedIn(ok =>
            {
                if (!ok) { done?.Invoke(false); return; }
                Snapshot.Code = code.ToUpperInvariant();
                var me = new RoomMember { Uid = LocalUid, Name = displayName, LastSeenMs = Now() };
                Send("PATCH", FirebaseEndpoints.Member(_cfg.DatabaseUrl, Snapshot.Code, LocalUid, _idToken), Serialize(me), null, r =>
                {
                    _polling = r;
                    done?.Invoke(r);
                });
            });
        }

        public void ClaimRole(Role role, Action<bool> done)
        {
            // 先讀角色節點的 ETag，再用 if-match 寫入：被別人搶先時伺服器回 412
            var url = FirebaseEndpoints.RoleClaim(_cfg.DatabaseUrl, Snapshot.Code, Roles.Key(role), _idToken);
            Get(url, (ok, body, etag) =>
            {
                if (!ok) { done?.Invoke(false); return; }
                if (body != "null" && body != JsonConvert.SerializeObject(LocalUid)) { done?.Invoke(false); return; }
                Send("PUT", url, JsonConvert.SerializeObject(LocalUid), etag, done);
            });
        }

        public void PushDesign(Design design) =>
            Send("PUT", FirebaseEndpoints.Design(_cfg.DatabaseUrl, Snapshot.Code, _idToken), JsonConvert.SerializeObject(design.ToJson()), null, null);

        public void PushVote(Role role, Vote vote, string reasonCode) =>
            Send("PUT", FirebaseEndpoints.Vote(_cfg.DatabaseUrl, Snapshot.Code, Roles.Key(role), _idToken),
                JsonConvert.SerializeObject(vote == Vote.Go ? "go" : "nogo:" + reasonCode), null, null);

        public void PushPhase(string phase) =>
            Send("PUT", FirebaseEndpoints.Phase(_cfg.DatabaseUrl, Snapshot.Code, _idToken), JsonConvert.SerializeObject(phase), null, null);

        public void PushChoice(string eventId, string choice) =>
            Send("PUT", FirebaseEndpoints.Choice(_cfg.DatabaseUrl, Snapshot.Code, eventId, _idToken), JsonConvert.SerializeObject(choice), null, null);

        public void Leave()
        {
            _polling = false;
            if (IsOnline && !string.IsNullOrEmpty(Snapshot.Code))
                Send("DELETE", FirebaseEndpoints.Member(_cfg.DatabaseUrl, Snapshot.Code, LocalUid, _idToken), null, null, null);
        }

        public void Tick(float dt)
        {
            if (!_polling || !IsOnline) return;
            _tokenExpiresIn -= dt;
            if (_tokenExpiresIn < 120) { _tokenExpiresIn = 3600; Refresh(); }
            _pollTimer -= dt;
            if (_pollTimer > 0) return;
            _pollTimer = _cfg.PollSeconds;
            Get(FirebaseEndpoints.Room(_cfg.DatabaseUrl, Snapshot.Code, _idToken), (ok, body, etag) =>
            {
                if (!ok || etag == _etag || string.IsNullOrEmpty(body) || body == "null") return;
                _etag = etag;
                try
                {
                    Snapshot = JsonConvert.DeserializeObject<RoomSnapshot>(body, Json) ?? Snapshot;
                    Changed?.Invoke(Snapshot);
                }
                catch (JsonException e) { Error?.Invoke("room.error.parse", e.Message); }
            });
        }

        // ---------- 登入 ----------

        void EnsureSignedIn(Action<bool> done)
        {
            if (!_cfg.IsConfigured) { Error?.Invoke("room.error.not_configured", ""); done(false); return; }
            if (!string.IsNullOrEmpty(_idToken)) { done(true); return; }
            Send("POST", FirebaseEndpoints.SignUpAnonymous(_cfg.ApiKey), "{\"returnSecureToken\":true}", null, ok => { if (!ok) done(false); },
                body =>
                {
                    var r = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
                    _idToken = r["idToken"];
                    _refreshToken = r["refreshToken"];
                    LocalUid = r["localId"];
                    _tokenExpiresIn = float.TryParse(r["expiresIn"], out var s) ? s : 3600;
                    done(true);
                });
        }

        void Refresh()
        {
            var form = "grant_type=refresh_token&refresh_token=" + UnityWebRequest.EscapeURL(_refreshToken);
            _runner.StartCoroutine(Request("POST", FirebaseEndpoints.RefreshToken(_cfg.ApiKey), form, null, "application/x-www-form-urlencoded",
                (ok, body, _) =>
                {
                    if (!ok) { Error?.Invoke("room.error.auth", body); return; }
                    var r = JsonConvert.DeserializeObject<Dictionary<string, string>>(body);
                    _idToken = r["id_token"];
                    _refreshToken = r["refresh_token"];
                }));
        }

        // ---------- HTTP ----------

        void Send(string method, string url, string body, string ifMatch, Action<bool> done, Action<string> onBody = null) =>
            _runner.StartCoroutine(Request(method, url, body, ifMatch, "application/json", (ok, text, _) =>
            {
                if (!ok) Error?.Invoke("room.error.network", text);
                else onBody?.Invoke(text);
                done?.Invoke(ok);
            }));

        void Get(string url, Action<bool, string, string> done) =>
            _runner.StartCoroutine(Request("GET", url, null, null, null, done, wantEtag: true));

        static IEnumerator Request(string method, string url, string body, string ifMatch, string contentType,
            Action<bool, string, string> done, bool wantEtag = false)
        {
            using (var req = new UnityWebRequest(url, method))
            {
                req.downloadHandler = new DownloadHandlerBuffer();
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                    req.SetRequestHeader("Content-Type", contentType ?? "application/json");
                }
                if (wantEtag || ifMatch != null) req.SetRequestHeader("X-Firebase-ETag", "true");
                if (ifMatch != null) req.SetRequestHeader("if-match", ifMatch);
                req.timeout = 10;
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                done?.Invoke(ok, req.downloadHandler?.text ?? req.error, req.GetResponseHeader("ETag") ?? "");
            }
        }

        static string Serialize(object o) => JsonConvert.SerializeObject(o, Json);

        static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        static string NewCode()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // 去掉容易看錯的 I、O、0、1
            var sb = new StringBuilder();
            for (int i = 0; i < 5; i++) sb.Append(letters[UnityEngine.Random.Range(0, letters.Length)]);
            return sb.ToString();
        }
    }
}
