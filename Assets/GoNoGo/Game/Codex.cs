using System;
using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 知識卡圖鑑：每個零件一張卡（科普說明）。不用讀完才能玩——卡片是玩出來的：
    /// 在組裝工作台把零件組好、把零件裝上船、或在採購終端翻開說明，那張卡就收進圖鑑，之後隨時可以回來看。
    /// 存在 PlayerPrefs（跨局保留）。地面站沒有科普說明，不算卡片。
    /// </summary>
    public static class Codex
    {
        const string Key = "gonogo.codex";

        static HashSet<string> _have;
        static readonly List<string> _new = new List<string>();

        /// <summary>剛收進一張新卡（零件 id）。畫面用來跳通知。</summary>
        public static event Action<string> Unlocked;

        static HashSet<string> Have
        {
            get
            {
                if (_have != null) return _have;
                _have = new HashSet<string>();
                foreach (var id in PlayerPrefs.GetString(Key, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) _have.Add(id);
                return _have;
            }
        }

        /// <summary>圖鑑裡所有的卡（照零件資料的順序）。</summary>
        public static IEnumerable<PartDef> Cards(GameData data) =>
            data.Parts.Where(p => p.Category != PartCategory.GroundStation && !string.IsNullOrEmpty(p.Explainer?.En));

        public static bool Has(string partId) => partId != null && Have.Contains(partId);

        public static int Count(GameData data) => Cards(data).Count(p => Have.Contains(p.Id));
        public static int Total(GameData data) => Cards(data).Count();

        /// <summary>這一局新收的卡（開新的一局時歸零）。</summary>
        public static IReadOnlyList<string> NewThisMission => _new;
        public static void BeginMission() => _new.Clear();

        /// <summary>收進圖鑑。已經有了就什麼都不做；回傳是不是新收的。quiet：不跳通知（玩家自己翻開來看的時候）。</summary>
        public static bool Unlock(GameData data, string partId, bool quiet = false)
        {
            var p = data.Part(partId);
            if (p == null || p.Category == PartCategory.GroundStation || string.IsNullOrEmpty(p.Explainer?.En)) return false;
            if (!Have.Add(partId)) return false;
            _new.Add(partId);
            PlayerPrefs.SetString(Key, string.Join(",", Have.OrderBy(x => x, StringComparer.Ordinal)));
            PlayerPrefs.Save();
            if (!quiet) Unlocked?.Invoke(partId);
            return true;
        }

        /// <summary>清空圖鑑（設定頁的「重設」和測試用）。</summary>
        public static void Reset()
        {
            Have.Clear();
            _new.Clear();
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _have = null;
            _new.Clear();
            Unlocked = null;
        }
    }
}
