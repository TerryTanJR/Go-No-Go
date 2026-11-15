// 網頁版的語音聊天音量。C# 端在 Net/VoiceChat.cs。
// Vivox 的網頁版把每位隊友的聲音放進頁面上的 <audio> 元素（.vxaudiolist 底下），它自己的音量 API 在瀏覽器是空的，
// 所以直接調這些元素的 volume；之後才加入的隊友（新的 <audio>）也要套用，用 MutationObserver 盯著。
mergeInto(LibraryManager.library, {
  GoNoGoSetVoiceVolume: function (volume) {
    var v = Math.max(0, Math.min(1, volume));
    window.gngVoiceVolume = v;
    var apply = function () {
      try {
        var list = document.querySelectorAll('.vxaudiolist audio');
        for (var i = 0; i < list.length; i++) list[i].volume = window.gngVoiceVolume;
      } catch (e) { }
    };
    apply();
    if (!window.gngVoiceObserver && typeof MutationObserver !== 'undefined') {
      try {
        window.gngVoiceObserver = new MutationObserver(apply);
        window.gngVoiceObserver.observe(document.body, { childList: true, subtree: true });
      } catch (e) { }
    }
  }
});
