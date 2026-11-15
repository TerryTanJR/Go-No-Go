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

        public static bool InChannel => _channel != null;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void GoNoGoSetVoiceVolume(float volume);
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

        /// <summary>自己的麥克風靜音：別人聽不到你，你還是聽得到別人。換房間也保持。</summary>
        public static void SetMicMuted(bool muted)
        {
            try
            {
                if (muted) VivoxService.Instance.MuteInputDevice();
                else VivoxService.Instance.UnmuteInputDevice();
                Multiplayer.MicMuted = VivoxService.Instance.IsInputDeviceMuted;
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
                    await vivox.LoginAsync(new LoginOptions());
                }
                if (gen != _generation) return;
                if (_channel != null) await vivox.LeaveAllChannelsAsync();
                await vivox.JoinGroupChannelAsync(channel, ChatCapability.AudioOnly);
                _channel = channel;
                Multiplayer.VoiceConnected = true;
                Multiplayer.MicMuted = vivox.IsInputDeviceMuted;
                SetVolume(GameSettings.VoiceVolume);
                Debug.Log("[Voice] joined " + channel);
                if (gen != _generation) await LeaveNow();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Voice] join: " + e.Message);
            }
        }

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
        static void Reset()
        {
            _channel = null;
            _generation = 0;
            Multiplayer.VoiceConnected = false;
            Multiplayer.SetMicMuted = SetMicMuted;
            Multiplayer.SetVoiceVolume = SetVolume;
        }
    }
}
