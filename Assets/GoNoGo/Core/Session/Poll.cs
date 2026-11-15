using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace GoNoGo.Core
{
    /// <summary>點名怎麼結束的。Open = 還在進行。</summary>
    public enum PollEnd { Open, Launched, Hold, Cancelled, TimedOut }

    /// <summary>
    /// 發射後對答案：喊的和當時的狀態對不對。
    /// BadRelease（放行錯誤）= 超標卻喊 GO，最嚴重；FalseAlarm（誤報）= 有餘量卻喊 NO-GO；
    /// RiskyGo（冒險放行）= 餘量不到一成還喊 GO，不算錯，只在報告標出來。
    /// </summary>
    public enum PollVerdict { Correct, RiskyGo, FalseAlarm, BadRelease }

    /// <summary>點名開始那一刻某一項的讀數（之後設計鎖住，不會變）。</summary>
    public sealed class PollReading
    {
        public double Used { get; set; }
        public double Capacity { get; set; }
        /// <summary>電力：實際放電深度；數據：最長空檔的資料量（不含安全係數）。其他項目是 0。</summary>
        public double Extra { get; set; }
        /// <summary>電力：這個軌道允許的放電深度。其他項目是 0。</summary>
        public double ExtraLimit { get; set; }

        public PollReading Clone() => (PollReading)MemberwiseClone();
    }

    /// <summary>一項的回答，連同當時的答案和讀數（報告、失敗看板對答案用）。</summary>
    public sealed class PollCall
    {
        /// <summary>項目（<see cref="PollItems.All"/>）。</summary>
        public string Item { get; set; } = "";
        public bool Go { get; set; }
        /// <summary>實際喊的人。</summary>
        public int By { get; set; }
        /// <summary>負責人；和 <see cref="By"/> 不同就是代喊。</summary>
        public int Owner { get; set; }
        public ResourceStatus Truth { get; set; }
        public PollReading Reading { get; set; } = new PollReading();

        [JsonIgnore] public bool Proxy => By != Owner;
        [JsonIgnore] public PollVerdict Verdict => PollItems.Verdict(Truth, Go);
    }

    /// <summary>一次 HOLD：那次點名喊過的每一項。誤報從這裡算（放行的那一次不會有 NO-GO）。</summary>
    public sealed class PollRecord
    {
        public int Poll { get; set; }
        /// <summary>這次點名是為了第幾次發射。</summary>
        public int Attempt { get; set; }
        public List<PollCall> Calls { get; set; } = new List<PollCall>();
    }

    /// <summary>點名的六個項目：挑戰要求的六項資源，一項一個判斷。</summary>
    public static class PollItems
    {
        /// <summary>點名的順序（官方的資源順序）。</summary>
        public static readonly ResourceId[] Order =
        {
            ResourceId.Mass, ResourceId.Power, ResourceId.Budget, ResourceId.Data, ResourceId.Comms, ResourceId.Time,
        };

        /// <summary>項目的字串鍵：小寫的資源名稱（和字串表的 res.* 一致）。</summary>
        public static string Key(ResourceId id) => id.ToString().ToLowerInvariant();

        public static readonly string[] All = Order.Select(Key).ToArray();

        public static ResourceId? Parse(string key)
        {
            foreach (var id in Order) if (Key(id) == key) return id;
            return null;
        }

        /// <summary>
        /// 這一項歸哪個席位：照「誰看得到那個數字」分。船的數字在組裝區的大螢幕，預算和時程在中控室。
        /// </summary>
        public static Station StationOf(string item) =>
            item == Key(ResourceId.Budget) || item == Key(ResourceId.Time) ? Station.Control : Station.Assembly;

        public static PollVerdict Verdict(ResourceStatus truth, bool go)
        {
            switch (truth)
            {
                case ResourceStatus.Over: return go ? PollVerdict.BadRelease : PollVerdict.Correct;
                case ResourceStatus.Ok: return go ? PollVerdict.Correct : PollVerdict.FalseAlarm;
                default: return go ? PollVerdict.RiskyGo : PollVerdict.Correct;
            }
        }

        /// <summary>發射結果對應到點名的哪一項（沒有對應 = null）。失敗看板用來寫「當時這一項喊了什麼」。</summary>
        public static string ItemFor(string reasonCode)
        {
            switch (reasonCode)
            {
                case "launch.unreachable":
                case "launch.overweight":
                case "launch.lower_orbit":
                    return Key(ResourceId.Mass);
                case "launch.dead_on_orbit":
                    return Key(ResourceId.Power);
                default:
                    return null;
            }
        }
    }
}
