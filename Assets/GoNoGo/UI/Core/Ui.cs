using System;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>用程式組 UI 的小工具：一行建一個元素並掛上 USS class。樣式一律寫在 Theme.uss。</summary>
    public static class Ui
    {
        public static VisualElement El(params string[] classes)
        {
            var e = new VisualElement();
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) e.AddToClassList(c);
            return e;
        }

        public static VisualElement Row(params string[] classes)
        {
            var e = El(classes);
            e.AddToClassList("row");
            return e;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text ?? "");
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) l.AddToClassList(c);
            return l;
        }

        public static Button Btn(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text ?? "" };
            foreach (var c in classes) if (!string.IsNullOrEmpty(c)) b.AddToClassList(c);
            b.RegisterCallback<ClickEvent>(_ => Sfx.Click());
            b.RegisterCallback<PointerEnterEvent>(_ => { if (b.enabledInHierarchy) Sfx.Hover(); });
            return b;
        }

        public static VisualElement Spacer() => El("spacer");

        public static T AddTo<T>(this T e, VisualElement parent) where T : VisualElement
        {
            parent.Add(e);
            return e;
        }

        public static T Cls<T>(this T e, string cls, bool on = true) where T : VisualElement
        {
            e.EnableInClassList(cls, on);
            return e;
        }

        public static T Named<T>(this T e, string name) where T : VisualElement
        {
            e.name = name;
            return e;
        }

        /// <summary>面板（含標題列）。回傳內容區。</summary>
        public static VisualElement Panel(VisualElement parent, string title, out VisualElement panel, string extraClass = null)
        {
            panel = El("panel", extraClass).AddTo(parent);
            if (!string.IsNullOrEmpty(title))
            {
                var head = El("panel-title").AddTo(panel);
                Text(title, "eyebrow").AddTo(head);
            }
            return El("panel-body").AddTo(panel);
        }

        /// <summary>下一幀把 class 加上去，讓 USS transition 播得出來（剛加入的元素直接加 class 不會有動畫）。</summary>
        public static void AnimateIn(VisualElement e, string onClass, long delayMs = 16)
        {
            e.schedule.Execute(() => e.AddToClassList(onClass)).StartingIn(delayMs);
        }
    }
}
