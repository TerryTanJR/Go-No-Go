using System.Collections.Generic;
using GoNoGo.Core;
using UnityEngine;

namespace GoNoGo.Game
{
    /// <summary>從 Resources/GameData 載入遊戲資料（整個遊戲共用一份）。</summary>
    public static class GameDataProvider
    {
        static GameData _data;

        public static GameData Data => _data ??= Load();

        /// <summary>
        /// 每次進 Play 都重新讀 JSON。專案關掉了 Domain Reload，不清掉的話會一直用上次編譯時讀到的舊資料
        /// （改了 strings.json、parts.json 卻看不到變化）。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _data = null;

        static GameData Load()
        {
            var files = new Dictionary<string, string>();
            foreach (var name in GameDataLoader.FileNames)
            {
                var asset = Resources.Load<TextAsset>("GameData/" + name);
                if (asset == null)
                {
                    Debug.LogError($"[GoNoGo] 缺少資料檔 Resources/GameData/{name}.json");
                    continue;
                }
                files[name] = asset.text;
            }
            var data = GameDataLoader.Load(files);
            var errors = GameDataLoader.Validate(data);
            foreach (var e in errors) Debug.LogError("[GoNoGo] 資料檢查：" + e);
            return data;
        }
    }
}
