// Comments in English on purpose: Unity re-encodes .jspre files with the system code page, so Chinese text ends up garbled in the build.
// Browsers refuse pointer lock for about a second after the player leaves it with Esc, and without a click.
// Unity 6 calls requestPointerLock() without handling the returned promise, so the refusal became an unhandled rejection and
// Unity's error handler showed "SecurityError: Pointer lock cannot be acquired immediately after the user has exited the lock"
// (pressing Esc, then Resume in the game menu right away). A refused lock is harmless: the cursor just stays free and
// clicking the view locks it again (PlayerController), so the refusal is caught and logged here.
(function () {
  var proto = typeof Element !== 'undefined' ? Element.prototype : null;
  if (!proto || !proto.requestPointerLock || proto.requestPointerLock.gngGuarded) return;
  var original = proto.requestPointerLock;
  var guarded = function () {
    var result = original.apply(this, arguments);
    if (result && typeof result.catch === 'function')
      return result.catch(function (e) { console.warn('[GoNoGo] pointer lock refused: ' + (e && e.message)); });
    return result;
  };
  guarded.gngGuarded = true;
  proto.requestPointerLock = guarded;
})();
