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
    /// </summary>
    public static class Loc
    {
        const string PrefKey = "gonogo.lang";
        static string _lang;

        public static event Action Changed;

        public static string Lang
        {
            get
            {
                if (_lang == null)
                {
                    _lang = PlayerPrefs.GetString(PrefKey, "");
                    if (_lang != Core.Lang.En && _lang != Core.Lang.ZhTW)
                        _lang = Application.systemLanguage == SystemLanguage.ChineseTraditional ? Core.Lang.ZhTW : Core.Lang.En;
                }
                return _lang;
            }
            set
            {
                if (value == _lang) return;
                _lang = value;
                PlayerPrefs.SetString(PrefKey, value);
                Changed?.Invoke();
            }
        }

        public static bool IsZh => Lang == Core.Lang.ZhTW;

        public static void Toggle() => Lang = IsZh ? Core.Lang.En : Core.Lang.ZhTW;

        static GameData D => GameDataProvider.Data;

        public static string T(string key) => Mark(D.Text(key, Lang));

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

        /// <summary>拿掉 [[…]] 記號（不支援 rich text 的地方用，例如寫進記錄檔）。</summary>
        public static string Plain(string s) =>
            string.IsNullOrEmpty(s) ? s ?? "" : s.Replace("[[", "").Replace("]]", "");

        public static string T(string key, IDictionary<string, string> args) => Format(T(key), k => args != null && args.TryGetValue(k, out var v) ? v : null);

        public static string T(string key, IDictionary<string, double> values) =>
            Format(T(key), k => null, values);

        public static string T(string key, params (string k, object v)[] args)
        {
            var map = new Dictionary<string, object>();
            foreach (var (k, v) in args) map[k] = v;
            return Format(T(key), k => map.TryGetValue(k, out var v) ? v : null);
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
            if (v.StartsWith("part:")) return Part(v.Substring(5));
            if (v.StartsWith("launcher:")) return Launcher(v.Substring(9));
            if (v.StartsWith("orbit:")) return Orbit(v.Substring(6));
            if (v.StartsWith("sw:")) return SpaceWeather(v.Substring(3));
            if (v.StartsWith("site:")) return T("site." + v.Substring(5));
            return v;
        }

        public static string Cause(CauseStep s)
        {
            var args = new Dictionary<string, string>();
            foreach (var kv in s.Args) args[kv.Key] = Resolve(kv.Value);
            return T(s.Key, args);
        }

        public static string Format(string template, Func<string, object> lookup, IDictionary<string, double> values = null)
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
                if (v == null && values != null && values.TryGetValue(name, out var dv)) v = dv;
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
