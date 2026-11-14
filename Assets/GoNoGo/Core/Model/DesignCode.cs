using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GoNoGo.Core
{
    /// <summary>
    /// 設計分享碼：把一份設計（零件、軌道、火箭、年數）壓成一行文字，貼給別人就能在自己的電腦上重現。
    /// 不用伺服器。格式：GNG1- 加上 base64url（欄位用 | 隔開，零件寫編號，最後是檢查碼）。
    /// 只存選擇的 id，數值一律從 GameData 查，所以同一個碼在每台電腦上算出來的結果都一樣。
    /// </summary>
    public static class DesignCode
    {
        public const string Prefix = "GNG1-";
        public const int MaxName = 24;

        /// <summary>分享碼裡各單選類別的順序（固定，不能改；要加欄位就換前綴版本）。</summary>
        static readonly string[] Order =
        {
            PartCategory.Solar, PartCategory.Battery, PartCategory.Antenna, PartCategory.GroundStation,
            PartCategory.Computer, PartCategory.Adcs, PartCategory.Propulsion, PartCategory.Shielding,
        };

        public static string Encode(Design d, GameData data, string name = "")
        {
            var f = new List<string>
            {
                d.ScenarioId ?? "",
                string.Join(",", (d.Instruments ?? new List<string>()).Select(id => Short(data, id))),
            };
            foreach (var c in Order) f.Add(Short(data, d.Get(c)));
            f.Add(d.OrbitId ?? "");
            f.Add(d.LauncherId ?? "");
            f.Add(d.MissionYears.ToString(System.Globalization.CultureInfo.InvariantCulture));
            f.Add(CleanName(name));
            string body = string.Join("|", f);
            body += "|" + Checksum(body);
            return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(body)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        /// <summary>
        /// 解開分享碼。失敗時 error 是字串鍵：err.code_format（不是分享碼、或被截斷）、err.code_checksum（抄錯字）、
        /// err.code_unknown（裡面有這個版本沒有的零件、軌道或火箭）。
        /// </summary>
        public static bool TryDecode(string code, GameData data, out Design design, out string name, out string error)
        {
            design = null;
            name = "";
            error = "err.code_format";
            code = (code ?? "").Trim();
            if (!code.StartsWith(Prefix, StringComparison.Ordinal)) return false;
            string body;
            try
            {
                string b64 = code.Substring(Prefix.Length).Replace('-', '+').Replace('_', '/');
                // 貼上時混進來的空白、換行都不算
                b64 = new string(b64.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
                while (b64.Length % 4 != 0) b64 += "=";
                body = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            }
            catch (FormatException) { return false; }
            catch (ArgumentException) { return false; }

            int cut = body.LastIndexOf('|');
            if (cut < 0) return false;
            string payload = body.Substring(0, cut);
            if (Checksum(payload) != body.Substring(cut + 1)) { error = "err.code_checksum"; return false; }
            var f = payload.Split('|');
            if (f.Length != Order.Length + 6) return false;

            error = "err.code_unknown";
            var d = new Design { ScenarioId = f[0] };
            if (data.Scenario(d.ScenarioId) == null) return false;
            foreach (var token in f[1].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = Find(data, token);
                if (p == null || p.Category != PartCategory.Instrument) return false;
                if (!d.Instruments.Contains(p.Id)) d.Instruments.Add(p.Id);
            }
            for (int i = 0; i < Order.Length; i++)
            {
                string token = f[2 + i];
                if (token.Length == 0) continue; // 這一類沒裝
                var p = Find(data, token);
                if (p == null || p.Category != Order[i]) return false;
                d.Parts[Order[i]] = p.Id;
            }
            d.OrbitId = f[2 + Order.Length];
            d.LauncherId = f[3 + Order.Length];
            if (data.Orbit(d.OrbitId) == null || data.Launcher(d.LauncherId) == null) return false;
            if (!int.TryParse(f[4 + Order.Length], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int years)
                || years < 1 || years > 99) return false;
            d.MissionYears = years;
            name = f[5 + Order.Length];
            design = d;
            error = "";
            return true;
        }

        /// <summary>名稱裡不能有分隔符號，太長的截掉。</summary>
        public static string CleanName(string name)
        {
            name = (name ?? "").Replace('|', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
            return name.Length > MaxName ? name.Substring(0, MaxName) : name;
        }

        /// <summary>零件用編號（P-3）比 id 短；沒有編號的（地面站）用 id。</summary>
        static string Short(GameData data, string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            var p = data.Part(id);
            return p != null && !string.IsNullOrEmpty(p.Code) ? p.Code : id;
        }

        static PartDef Find(GameData data, string token) => data.PartByCode(token) ?? data.Part(token);

        /// <summary>四位十六進位的檢查碼（FNV-1a 折成 16 位元）：抄錯一個字就對不上。</summary>
        static string Checksum(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (byte b in Encoding.UTF8.GetBytes(s))
                {
                    h ^= b;
                    h *= 16777619;
                }
                return ((h >> 16) ^ (h & 0xffff)).ToString("x4", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
