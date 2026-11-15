using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GoNoGo.Core;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 中英文切換。字串全部放在 strings.json（和核心共用一份），格式為 "{name}" 或 "{name:0.0}"。
    /// 參數值以 "part:" / "launcher:" / "orbit:" / "sw:" 開頭時，換成對應的名稱。
    /// 選 strings.json 而不是 Unity Localization 套件：同一份 JSON 網頁版也能讀，核心測試也能檢查缺字。
    /// 語言不記在本機：每次開遊戲都從英文開始（不看系統語言），玩家在選單切換後只維持到關掉遊戲。
    /// </summary>
    public static class Loc
    {
        /// <summary>每次開遊戲的語言。</summary>
        public const string DefaultLang = Core.Lang.En;
        static string _lang;

        public static event Action Changed;

        public static string Lang
        {
            get => _lang ??= DefaultLang;
            set
            {
                if (value == _lang) return;
                _lang = value;
                Changed?.Invoke();
            }
        }

        // 編輯器關掉 Domain Reload 時靜態欄位會留到下一次 Play：每次開始都回到預設語言
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _lang = null;
            _cache.Clear();
            _cacheData = null;
            _cacheLang = null;
        }

        public static bool IsZh => Lang == Core.Lang.ZhTW;

        public static void Toggle() => Lang = IsZh ? Core.Lang.En : Core.Lang.ZhTW;

        static GameData D => GameDataProvider.Data;

        // T(key) 的結果（已經換好重點字的顏色）：介面每幀都在問同樣的鍵，查過一次就記住，不必每次重新掃字串。
        // 字串資料載入後不會改，所以只有換語言、重新載入資料時才清掉。
        static readonly Dictionary<string, string> _cache = new Dictionary<string, string>();
        static GameData _cacheData;
        static string _cacheLang;
        /// <summary>快取的上限：正常只會有字串表那一千多個鍵，超過代表有人拿會變的字串當鍵，清掉重來。</summary>
        const int MaxCache = 8192;

        public static string T(string key)
        {
            var d = D;
            string lang = Lang;
            if (!ReferenceEquals(d, _cacheData) || lang != _cacheLang)
            {
                _cache.Clear();
                _cacheData = d;
                _cacheLang = lang;
            }
            if (!_cache.TryGetValue(key, out var s))
            {
                s = Mark(d.Text(key, lang));
                if (_cache.Count >= MaxCache) _cache.Clear();
                _cache[key] = s;
            }
            return s;
        }

        public static string T(LocalizedText t) => Mark(t?.Get(Lang) ?? "");

        /// <summary>重點字的顏色（和介面主色同一個色系，比內文亮）。</summary>
        public const string MarkColor = "#7FE3FF";

        /// <summary>
        /// 字串裡用 [[…]] 包起來的是重點字：換成上色的 rich text。長句子只掃重點字也看得懂，
        /// 不用把整句讀完（降低閱讀負擔，資訊一個字都沒少）。沒有 [[ 的字串原樣回傳。
        /// </summary>
        public static string Mark(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf("[[", StringComparison.Ordinal) < 0) return s ?? "";
            return s.Replace("[[", "<color=" + MarkColor + ">").Replace("]]", "</color>");
        }

        public static string T(string key, IDictionary<string, string> args) => Format(T(key), k => args != null && args.TryGetValue(k, out var v) ? v : null);

        public static string T(string key, params (string k, object v)[] args)
        {
            // 參數只有幾個：直接從後面找（同名的以最後一個為準），不必每次另外建一個字典
            return Format(T(key), name =>
            {
                for (int i = args.Length - 1; i >= 0; i--)
                    if (args[i].k == name) return args[i].v;
                return null;
            });
        }

        public static string Part(string id) => T(D.Part(id)?.Name) is var s && s.Length > 0 ? s : id;
        public static string Orbit(string id) => T(D.Orbit(id)?.Name) is var s && s.Length > 0 ? s : id;
        public static string Launcher(string id) => T(D.Launcher(id)?.Name) is var s && s.Length > 0 ? s : id;

        public static string SpaceWeather(string id)
        {
            var e = D.SpaceWeather.Find(x => x.Id == id);
            if (e == null) return id;
            string kind = e.Type == "FLR" ? (IsZh ? $"{e.ClassType} 級閃焰" : $"{e.ClassType} flare")
                : e.Type == "GST" ? (IsZh ? $"{e.ClassType} 地磁暴（Kp {Kp(e.KpIndex)}）" : $"{e.ClassType} geomagnetic storm (Kp {Kp(e.KpIndex)})")
                : (IsZh ? "日冕巨量噴發" : "coronal mass ejection");
            return $"{kind}, {e.Date}";
        }

        /// <summary>Kp 以三分之一為單位：8.67 寫成 NOAA 的「9-」，8.33 寫成「8+」。</summary>
        static string Kp(double kp)
        {
            int thirds = (int)System.Math.Round(kp * 3);
            int whole = (thirds + 1) / 3;
            int rest = thirds - whole * 3;
            return whole + (rest > 0 ? "+" : rest < 0 ? "-" : "");
        }

        /// <summary>把 "part:xxx" 這類參照換成名稱。</summary>
        public static string Resolve(string v)
        {
            if (string.IsNullOrEmpty(v)) return v ?? "";
            // Ordinal：前綴都是 ASCII，照字元比就好（預設的文化比對慢很多，而且每個參數都會經過這裡）
            if (v.StartsWith("part:", StringComparison.Ordinal)) return Part(v.Substring(5));
            if (v.StartsWith("launcher:", StringComparison.Ordinal)) return Launcher(v.Substring(9));
            if (v.StartsWith("orbit:", StringComparison.Ordinal)) return Orbit(v.Substring(6));
            if (v.StartsWith("sw:", StringComparison.Ordinal)) return SpaceWeather(v.Substring(3));
            if (v.StartsWith("site:", StringComparison.Ordinal)) return T("site." + v.Substring(5));
            return v;
        }

        public static string Cause(CauseStep s)
        {
            var args = new Dictionary<string, string>();
            foreach (var kv in s.Args) args[kv.Key] = Resolve(kv.Value);
            return T(s.Key, args);
        }

        public static string Format(string template, Func<string, object> lookup)
        {
            if (string.IsNullOrEmpty(template) || template.IndexOf('{') < 0) return template ?? "";
            var sb = new StringBuilder(template.Length + 16);
            int i = 0;
            while (i < template.Length)
            {
                char c = template[i];
                int close = c == '{' ? template.IndexOf('}', i + 1) : -1;
                if (close < 0) { sb.Append(c); i++; continue; }
                string inner = template.Substring(i + 1, close - i - 1);
                string name = inner, fmt = null;
                int colon = inner.IndexOf(':');
                if (colon >= 0) { name = inner.Substring(0, colon); fmt = inner.Substring(colon + 1); }
                object v = lookup(name);
                if (v == null) sb.Append('{').Append(inner).Append('}');
                else if (v is double d) sb.Append(d.ToString(fmt ?? "0.##", CultureInfo.InvariantCulture));
                else if (v is float f) sb.Append(f.ToString(fmt ?? "0.##", CultureInfo.InvariantCulture));
                else if (v is int n) sb.Append(n.ToString(fmt ?? "0", CultureInfo.InvariantCulture));
                else sb.Append(Resolve(v.ToString()));
                i = close + 1;
            }
            return sb.ToString();
        }

        /// <summary>數字格式：千分位、固定小數。</summary>
        public static string Num(double v, int decimals = 0) =>
            v.ToString("N" + decimals, CultureInfo.InvariantCulture);
    }
}
