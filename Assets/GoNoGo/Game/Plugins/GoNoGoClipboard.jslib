// 網頁版的系統剪貼簿（設計分享碼的複製與貼上）。C# 端在 Game/ClipboardUtil.cs。
mergeInto(LibraryManager.library, {
  GoNoGoCopyText: function (ptr) {
    var text = UTF8ToString(ptr);
    var fallback = function () {
      try {
        var area = document.createElement('textarea');
        area.value = text;
        area.style.position = 'fixed';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.focus();
        area.select();
        document.execCommand('copy');
        document.body.removeChild(area);
      } catch (e) { }
    };
    try {
      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(text).catch(fallback);
        return;
      }
    } catch (e) { }
    fallback();
  },

  GoNoGoReadText: function (objPtr, methodPtr) {
    var obj = UTF8ToString(objPtr);
    var method = UTF8ToString(methodPtr);
    var send = function (text) {
      try { SendMessage(obj, method, text || ''); } catch (e) { }
    };
    try {
      if (navigator.clipboard && navigator.clipboard.readText) {
        navigator.clipboard.readText().then(send).catch(function () { send(''); });
        return;
      }
    } catch (e) { }
    send('');
  }
});
