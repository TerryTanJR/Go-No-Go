using System;
using System.Collections.Generic;
using System.Linq;
using GoNoGo.Core;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>
    /// 知識卡圖鑑：每個零件一張卡（科普說明）。不用讀完才能玩——卡片是玩出來的：
    /// 在組裝工作台把零件組好、或把零件裝上船，那張卡就收進圖鑑，之後隨時可以回來看。
    /// 組裝時一次都沒裝錯（看懂了每個小零件在做什麼），那張卡升級成「精通」：亂試是拿不到的。
    /// 存在 PlayerPrefs（跨局保留）。地面站沒有科普說明，不算卡片。
    /// </summary>
    public static class Codex
    {
        const string Key = "gonogo.codex";
        const string MasterKey = "gonogo.codex.master";

        static HashSet<string> _have, _master;
        static readonly List<string> _new = new List<string>();
        static readonly List<string> _newMaster = new List<string>();

        /// <summary>剛收進一張新卡（零件 id）。畫面用來跳通知。</summary>
        public static event Action<string> Unlocked;

        static HashSet<string> Load(string key)
        {
            var set = new HashSet<string>();
            foreach (var id in PlayerPrefs.GetString(key, "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) set.Add(id);
            return set;
        }

        static void Store(string key, HashSet<string> set)
        {
            PlayerPrefs.SetString(key, string.Join(",", set.OrderBy(x => x, StringComparer.Ordinal)));
            PlayerPrefs.Save();
        }

        static HashSet<string> Have => _have ??= Load(Key);
        static HashSet<string> Masters => _master ??= Load(MasterKey);

        /// <summary>圖鑑裡所有的卡（照零件資料的順序）。</summary>
        public static IEnumerable<PartDef> Cards(GameData data) =>
            data.Parts.Where(p => p.Category != PartCategory.GroundStation && !string.IsNullOrEmpty(p.Explainer?.En));

        public static bool Has(string partId) => partId != null && Have.Contains(partId);
        /// <summary>這張卡是「精通」：組裝這種構造時一次都沒裝錯。</summary>
        public static bool IsMaster(string partId) => partId != null && Masters.Contains(partId);

        public static int Count(GameData data) => Cards(data).Count(p => Have.Contains(p.Id));
        public static int Total(GameData data) => Cards(data).Count();

        /// <summary>這一局新收的卡、新升級成精通的卡（開新的一局時歸零）。</summary>
        public static IReadOnlyList<string> NewThisMission => _new;
        public static IReadOnlyList<string> MasteredThisMission => _newMaster;
        public static void BeginMission() { _new.Clear(); _newMaster.Clear(); }

        static bool IsCard(GameData data, string partId)
        {
            var p = data.Part(partId);
            return p != null && p.Category != PartCategory.GroundStation && !string.IsNullOrEmpty(p.Explainer?.En);
        }

        /// <summary>收進圖鑑。已經有了就什麼都不做；回傳是不是新收的。quiet：不跳通知（呼叫的人自己會顯示）。</summary>
        public static bool Unlock(GameData data, string partId, bool quiet = false)
        {
            if (!IsCard(data, partId)) return false;
            if (!Have.Add(partId)) return false;
            _new.Add(partId);
            Store(Key, Have);
            if (!quiet) Unlocked?.Invoke(partId);
            return true;
        }

        /// <summary>升級成精通（還沒收的話順便收進圖鑑）。已經是精通就什麼都不做；回傳是不是這次才升級的。</summary>
        public static bool Master(GameData data, string partId)
        {
            if (!IsCard(data, partId)) return false;
            Unlock(data, partId, true);
            if (!Masters.Add(partId)) return false;
            _newMaster.Add(partId);
            Store(MasterKey, Masters);
            return true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _have = null;
            _master = null;
            _new.Clear();
            _newMaster.Clear();
            Unlocked = null;
        }
    }
}
