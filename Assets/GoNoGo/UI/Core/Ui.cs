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

        /// <summary>
        /// 會換行的長文字用：每一段的結尾不會只剩一個字（或一個英文單字）自己一行。
        /// 中文把最後三個字用 U+2060（不換行的零寬字元）黏在一起；英文把最後一個空白換成不斷行空格。
        /// </summary>
        public static string NoOrphan(string text)
        {
            if (string.IsNullOrEmpty(text)) return text ?? "";
            var paras = text.Split('\n');
            for (int i = 0; i < paras.Length; i++) paras[i] = NoOrphanLine(paras[i]);
            return string.Join("\n", paras);
        }

        /// <summary>不算 rich text 標籤（&lt;color=…&gt;）的字數：打字機效果要照看得到的字算。</summary>
        public static int VisibleLength(string rich)
        {
            if (string.IsNullOrEmpty(rich)) return 0;
            int n = 0;
            bool tag = false;
            foreach (char c in rich)
            {
                if (c == '<') tag = true;
                else if (c == '>' && tag) tag = false;
                else if (!tag) n++;
            }
            return n;
        }

        /// <summary>
        /// 前 visible 個看得到的字（標籤不算、不會被切一半）。切在上色的字中間時補上結尾標籤，後面的字才不會全部變色。
        /// </summary>
        public static string RichPrefix(string rich, int visible)
        {
            if (string.IsNullOrEmpty(rich) || visible <= 0) return "";
            var sb = new System.Text.StringBuilder(rich.Length);
            int n = 0, open = 0;
            for (int i = 0; i < rich.Length; i++)
            {
                char c = rich[i];
                if (c == '<')
                {
                    int close = rich.IndexOf('>', i);
                    if (close > i)
                    {
                        string tag = rich.Substring(i, close - i + 1);
                        if (tag.StartsWith("</")) open = System.Math.Max(0, open - 1);
                        else open++;
                        sb.Append(tag);
                        i = close;
                        continue;
                    }
                }
                if (n >= visible) break;
                sb.Append(c);
                n++;
            }
            for (int k = 0; k < open; k++) sb.Append("</color>");
            return sb.ToString();
        }

        static string NoOrphanLine(string s)
        {
            s = s.TrimEnd();
            if (s.Length < 4) return s;
            // 結尾幾個字裡有 rich text 標籤：不黏（零寬字元插進標籤裡，標籤就壞了）
            if (s.IndexOf('>', s.Length - 4) >= 0 || s.IndexOf('<', s.Length - 4) >= 0) return s;
            int sp = s.LastIndexOf(' ');
            // 英文：最後一個單字和前一個連在一起（太長的不黏，免得整行被推下去）
            if (sp > 0 && s.Length - sp <= 16 && !IsCjk(s[s.Length - 1]) && !IsCjk(s[s.Length - 2]))
                return s.Substring(0, sp) + ' ' + s.Substring(sp + 1);
            if (!IsCjk(s[s.Length - 1]) && !IsCjk(s[s.Length - 2])) return s;
            var sb = new System.Text.StringBuilder(s.Length + 2);
            sb.Append(s, 0, s.Length - 3);
            for (int k = s.Length - 3; k < s.Length; k++)
            {
                if (k > s.Length - 3) sb.Append('⁠');
                sb.Append(s[k]);
            }
            return sb.ToString();
        }

        static bool IsCjk(char c) => c >= 0x2E80 && c <= 0x9FFF || c >= 0xF900 && c <= 0xFAFF || c >= 0xFF00 && c <= 0xFFEF;

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
