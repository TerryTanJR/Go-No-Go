// Comments in English on purpose: Unity re-encodes .jspre files with the system code page, so Chinese text ends up garbled in the build.
// Vivox for Web (com.unity.services.vivox, Plugins/WebGL/library.jslib) calls back into C# through Module["dynCall_vi"] and Module["dynCall_vd"].
// Unity 6's Emscripten no longer puts dynCall_* on Module, so every callback threw
// "TypeError: Module[("dynCall_" + paramTypes)] is not a function" as soon as a co-op room joined voice (the mic level meter, getRMS, fires every 100 ms).
// Pre-js code runs in the same scope as the Vivox library, so Module, getWasmTableEntry and wasmTable are visible here.
// If a later Vivox or Unity version provides these functions again, they are left alone.
(function () {
  ['vi', 'vd'].forEach(function (sig) {
    var name = 'dynCall_' + sig;
    if (typeof Module[name] === 'function') return;
    Module[name] = function (fn, arg) {
      var f = typeof getWasmTableEntry === 'function' ? getWasmTableEntry(fn) : wasmTable.get(fn);
      return f(arg);
    };
  });
})();
