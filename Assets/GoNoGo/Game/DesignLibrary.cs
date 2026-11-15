using System;
using System.Collections.Generic;
using GoNoGo.Core;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>設計圖庫裡的一筆：名稱、分享碼（設計本身就存在碼裡）、來自哪一關，以及飛過的話當時的成績。</summary>
    [Serializable]
    public sealed class SavedDesign
    {
        public string id = "";
        public string name = "";
        /// <summary>設計分享碼（<see cref="DesignCode"/>）。貼給別人就是這一串。</summary>
        public string code = "";
        public string scenarioId = "";
        public string difficultyId = "";
        /// <summary>飛過（存的時候有任務結果）：評等與總分。沒飛過是空字串和 0。</summary>
        public string grade = "";
        public double score;
        public double years;
        /// <summary>存檔時間（UTC ticks），只用來排序和顯示日期。</summary>
        public long savedTicks;
        /// <summary>別人分享、用分享碼匯入的。</summary>
        public bool imported;

        public bool Flown => !string.IsNullOrEmpty(grade);
    }

    /// <summary>
    /// 設計圖庫：把設計存在這台電腦（PlayerPrefs；網頁版存在瀏覽器裡），之後可以比較、分享、在沙盒打開。
    /// 不用伺服器：分享就是複製一行分享碼，對方貼上就匯入。
    /// </summary>
    public static class DesignLibrary
    {
        const string Key = "gonogo.designs";
        public const int Max = 40;

        [Serializable]
        sealed class Store { public List<SavedDesign> items = new List<SavedDesign>(); }

        static Store _store;

        static Store S
        {
            get
            {
                if (_store != null) return _store;
                _store = new Store();
                var json = PlayerPrefs.GetString(Key, "");
                if (!string.IsNullOrEmpty(json))
                {
                    try { _store = JsonUtility.FromJson<Store>(json) ?? new Store(); }
                    catch (Exception) { _store = new Store(); /* 壞掉就當成空的 */ }
                }
                _store.items ??= new List<SavedDesign>();
                return _store;
            }
        }

        /// <summary>最新的在最前面。</summary>
        public static IReadOnlyList<SavedDesign> All => S.items;

        public static SavedDesign Find(string id) => S.items.Find(x => x.id == id);

        /// <summary>
        /// 存一份設計。同一個設計（同樣的零件、軌道、火箭、年數、關卡）已經存過就更新那一筆（保留比較好的成績），不會重複。
        /// </summary>
        public static SavedDesign Save(GameData data, string name, Design design, string difficultyId, MissionResult result = null, bool imported = false)
        {
            name = DesignCode.CleanName(name);
            var d = design.Clone();
            int hash = d.StableHash();
            var existing = S.items.Find(x => DesignCode.TryDecode(x.code, data, out var od, out _, out _) && od.StableHash() == hash);
            // 沒取名字：已經存過的沿用原本的名字，新的才給預設名稱
            if (name.Length == 0) name = existing != null ? existing.name : DefaultName(data, design);
            string code = DesignCode.Encode(d, data, name);
            var item = existing ?? new SavedDesign { id = Guid.NewGuid().ToString("N").Substring(0, 12), imported = imported };
            item.name = name;
            item.code = code;
            item.scenarioId = d.ScenarioId ?? "";
            item.difficultyId = difficultyId ?? "";
            item.savedTicks = DateTime.UtcNow.Ticks;
            if (result != null && (!item.Flown || result.Score.Total >= item.score))
            {
                item.grade = result.Score.Grade;
                item.score = result.Score.Total;
                item.years = result.ActualYears;
            }
            S.items.Remove(item);
            S.items.Insert(0, item);
            while (S.items.Count > Max) S.items.RemoveAt(S.items.Count - 1);
            Persist();
            return item;
        }

        /// <summary>用分享碼匯入。失敗時 error 是字串鍵。</summary>
        public static bool Import(GameData data, string code, out SavedDesign saved, out string error)
        {
            saved = null;
            if (!DesignCode.TryDecode(code, data, out var d, out var name, out error)) return false;
            saved = Save(data, name, d, data.DifficultyFor(data.Scenario(d.ScenarioId))?.Id, null, true);
            return true;
        }

        public static void Delete(string id)
        {
            if (S.items.RemoveAll(x => x.id == id) > 0) Persist();
        }

        public static Design Decode(GameData data, SavedDesign s) =>
            s != null && DesignCode.TryDecode(s.code, data, out var d, out _, out _) ? d : null;

        /// <summary>沒取名字時的預設名稱：關卡名 + 第幾份。</summary>
        public static string DefaultName(GameData data, Design d)
        {
            string scn = data.Scenario(d.ScenarioId)?.Name.Get(Loc.Lang) ?? "Design";
            // 關卡名可能很長（歷史關卡）：留前面幾個字
            if (scn.Length > 14) scn = scn.Substring(0, 14);
            // 編號往上找到沒人用的（刪過中間的設計之後，「數量 + 1」會和現有的名字撞名）
            for (int n = S.items.Count + 1; ; n++)
            {
                string name = DesignCode.CleanName(scn + " #" + n);
                if (!S.items.Exists(x => x.name == name)) return name;
            }
        }

        static void Persist()
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(S));
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _store = null;
    }
}
