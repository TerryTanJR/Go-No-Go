using System;
using System.Threading.Tasks;
using GoNoGo.Game;
using Unity.Services.Vivox;
using UnityEngine;

namespace GoNoGo.Net
{
    /// <summary>
    /// 房間語音（Vivox）：同一個房間碼的人在同一個頻道，有人講話全房都聽得到。
    /// 只用在房間碼（轉送）模式，要先登入 Unity 服務（<see cref="NetBootstrap"/> 的 SignIn）；區網直連沒有語音。
    /// 瀏覽器版只支援一般（非 3D）頻道、一次一個頻道、沒有顯示名稱和「誰在講話」的事件，所以這裡只用最基本的功能。
    /// 瀏覽器第一次會跳出麥克風權限；拒絕的話還是聽得到別人。
    /// </summary>
    public static class VoiceChat
    {
        static string _channel;
        // 每次 Join / Leave 加一：加入到一半就離開房間時，舊的流程看到號碼變了就自己退出
        static int _generation;
        // Vivox 是常駐的單例，InitializeAsync 重複呼叫會丟例外；不在 Reset 清掉，和它一樣跨過進出 Play Mode
        static bool _initialized;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void GoNoGoSetVoiceVolume(float volume);
        // Vivox 網頁版 library.jslib 裡的函式：1 = 開麥克風、0 = 關（把本機麥克風音軌的 enabled 設掉）
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void vx_setLocalCapture(int isTransmitting);
        // 網頁版的 IsInputDeviceMuted 永遠是 false（見 SetMicMuted），靜音狀態自己記
        static bool _webMicMuted;
#endif

        /// <summary>
        /// 語音聊天音量（0–1）：只調整聽到隊友說話的音量。語音不經過 Unity 的 AudioListener，所以不受主音量影響，
        /// 要用 Vivox 自己的輸出音量。Vivox 的音量是 -50（最小）到 50，0 是預設：這裡把 100% 對到 0、0% 對到靜音。
        /// 瀏覽器版 Vivox 的 SetOutputDeviceVolume 是空的（什麼都不做），改成直接調它播放隊友聲音的 audio 元素
        /// （Plugins/GoNoGoVoice.jslib）。
        /// </summary>
        public static void SetVolume(float volume)
        {
            volume = Mathf.Clamp01(volume);
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                GoNoGoSetVoiceVolume(volume);
#else
                if (!_initialized) return; // 還沒進過語音頻道：加入時（Join）會套用
                var vivox = VivoxService.Instance;
                if (volume <= 0.001f) { vivox.MuteOutputDevice(); return; }
                if (vivox.IsOutputDeviceMuted) vivox.UnmuteOutputDevice();
                vivox.SetOutputDeviceVolume(VivoxVolume(volume));
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Voice] volume: " + e.Message);
            }
        }

        /// <summary>0–1 → Vivox 的 -50…0（100% = 預設音量，不放大）。</summary>
        public static int VivoxVolume(float volume) => Mathf.RoundToInt(Mathf.Lerp(-50f, 0f, Mathf.Clamp01(volume)));

        /// <summary>
        /// 自己的麥克風靜音：別人聽不到你，你還是聽得到別人。換房間也保持。
        /// 瀏覽器版 Vivox 的 MuteInputDevice 最後呼叫的 AudioInputDevicesWeb.Mute 是空的（什麼都不做），
        /// 改成直接呼叫它 jslib 的 vx_setLocalCapture。
        /// </summary>
        public static void SetMicMuted(bool muted)
        {
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                _webMicMuted = muted;
                vx_setLocalCapture(muted ? 0 : 1);
                Multiplayer.MicMuted = muted;
#else
                if (muted) VivoxService.Instance.MuteInputDevice();
                else VivoxService.Instance.UnmuteInputDevice();
                Multiplayer.MicMuted = VivoxService.Instance.IsInputDeviceMuted;
#endif
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Voice] mute: " + e.Message);
            }
        }

        /// <summary>加入這個房間的語音頻道；失敗只記 log，不影響遊戲連線。</summary>
        public static async void Join(string roomCode)
        {
            if (string.IsNullOrEmpty(roomCode)) return;
            int gen = ++_generation;
            var channel = "gng" + roomCode.Trim().ToUpperInvariant();
            try
            {
                var vivox = VivoxService.Instance;
                if (!vivox.IsLoggedIn)
                {
                    if (!_initialized)
                    {
                        await vivox.InitializeAsync();
                        _initialized = true;
                    }
#if UNITY_WEBGL && !UNITY_EDITOR
                    await LoginWeb(vivox);
#else
                    await vivox.LoginAsync(new LoginOptions());
#endif
                }
                if (gen != _generation) return;
                if (_channel != null) await vivox.LeaveAllChannelsAsync();
                await vivox.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
                _channel = channel;
                Multiplayer.VoiceConnected = true;
#if UNITY_WEBGL && !UNITY_EDITOR
                // 新頻道沿用同一條麥克風音軌，再套一次確保換房間後還是靜音
                SetMicMuted(_webMicMuted);
#else
                Multiplayer.MicMuted = vivox.IsInputDeviceMuted;
#endif
                SetVolume(GameSettings.VoiceVolume);
                Debug.Log("[Voice] joined " + channel);
                if (gen != _generation) await LeaveNow();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Voice] join: " + e.Message);
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        /// <summary>
        /// 瀏覽器版登入。Vivox 16.13 網頁版的 LoginAsync 永遠不會結束：LoginSessionWeb.BeginLogin 回傳的 IAsyncResult
        /// 從來沒有被標成完成，Task.Factory.FromAsync 就一直等；就算過了，接著呼叫的 SetTransmissionModeAsync
        /// 在網頁版也只會丟 NotImplementedException。登入其實在 JS 端成功了（回呼會把狀態改成已登入），
        /// 所以不等 LoginAsync，改成等 IsLoggedIn 變成 true（最多 20 秒），再照常加入頻道。
        /// </summary>
        static async Task LoginWeb(IVivoxService vivox)
        {
            var login = vivox.LoginAsync(new LoginOptions());
            // 它之後若失敗（上面說的 NotImplementedException），讀掉例外，不要變成未處理的工作例外
            _ = login.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!vivox.IsLoggedIn)
            {
                if (login.IsFaulted) await login; // 登入真的失敗：把原本的例外丟出去
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Vivox login timed out");
                await Task.Yield();
            }
        }
#endif

        /// <summary>離開語音頻道（離開房間時呼叫）。</summary>
        public static void Leave()
        {
            _generation++;
            _ = LeaveNow();
        }

        static async Task LeaveNow()
        {
            if (_channel == null) return;
            _channel = null;
            Multiplayer.VoiceConnected = false;
            try
            {
                await VivoxService.Instance.LeaveAllChannelsAsync();
                Debug.Log("[Voice] left");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Voice] leave: " + e.Message);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _channel = null;
            _generation = 0;
            Multiplayer.VoiceConnected = false;
            Multiplayer.SetMicMuted = SetMicMuted;
            Multiplayer.SetVoiceVolume = SetVolume;
        }
    }
}
