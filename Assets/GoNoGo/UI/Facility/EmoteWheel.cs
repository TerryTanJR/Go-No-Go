using GoNoGo.Game;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 表情輪盤：按住 X，畫面中央跳出四格，滑鼠往哪個方向推就選哪一格，放開 X 就做那個動作
    /// （Overwatch、Fortnite 的做法；游標一直鎖著，用滑鼠的移動量選）。滑鼠沒推出去就放開 = 不做。
    /// </summary>
    public sealed class EmoteWheel
    {
        /// <summary>滑鼠累積移動多少（像素）才算選了一格。</summary>
        const float PickDistance = 30f;
        /// <summary>格子離中心多遠（px）。</summary>
        const float SlotRadius = 125f;

        VisualElement _root;
        readonly Label[] _slots = new Label[Emotes.Wheel.Length];
        Vector2 _aim;
        int _picked = -1;

        public void Attach(VisualElement parent)
        {
            _root = Ui.El("emote-wheel").AddTo(parent);
            _root.pickingMode = PickingMode.Ignore;
            Ui.El("emote-wheel-hub").AddTo(_root).pickingMode = PickingMode.Ignore;
            for (int i = 0; i < _slots.Length; i++)
            {
                // 從正上方開始順時針排（畫面座標 y 往下）
                float a = i * 2f * Mathf.PI / _slots.Length;
                var slot = Ui.Text(Loc.T(Emotes.NameKey(Emotes.Wheel[i])), "emote-slot").AddTo(_root);
                slot.pickingMode = PickingMode.Ignore;
                slot.style.translate = new Translate(Mathf.Sin(a) * SlotRadius, -Mathf.Cos(a) * SlotRadius);
                _slots[i] = slot;
            }
            Show(Hud.EmoteWheelOpen);
        }

        public void Tick(bool allowed)
        {
            var kb = Keyboard.current;
            if (kb == null || _root == null) return;
            if (!Hud.EmoteWheelOpen)
            {
                if (allowed && kb.xKey.wasPressedThisFrame) Open();
                return;
            }
            if (!allowed) { Close(); return; }
            var mouse = Mouse.current;
            if (mouse != null) _aim = Vector2.ClampMagnitude(_aim + mouse.delta.ReadValue(), PickDistance * 2f);
            Highlight(_aim.magnitude < PickDistance ? -1 : SlotAt(_aim));
            if (kb.xKey.wasReleasedThisFrame)
            {
                int picked = _picked;
                Close();
                if (picked >= 0) Hud.PlayEmote(Emotes.Wheel[picked]);
            }
        }

        /// <summary>滑鼠推的方向對到第幾格（滑鼠的 y 往上是正的）。</summary>
        int SlotAt(Vector2 aim)
        {
            float deg = Mathf.Atan2(aim.x, aim.y) * Mathf.Rad2Deg;
            float step = 360f / _slots.Length;
            return Mathf.RoundToInt(Mathf.Repeat(deg, 360f) / step) % _slots.Length;
        }

        void Open()
        {
            _aim = Vector2.zero;
            Highlight(-1);
            Hud.EmoteWheelOpen = true;
            Show(true);
        }

        void Close()
        {
            Hud.EmoteWheelOpen = false;
            Show(false);
        }

        void Highlight(int index)
        {
            if (index == _picked) return;
            _picked = index;
            for (int i = 0; i < _slots.Length; i++) _slots[i].EnableInClassList("emote-slot--on", i == index);
        }

        void Show(bool on) => _root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
