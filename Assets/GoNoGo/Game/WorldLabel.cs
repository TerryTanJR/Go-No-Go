using System.Collections.Generic;
using UnityEngine;

namespace GoNoGo.Game
{
    // 必須自己一個檔案、檔名和類別同名：Unity 才能把它存進場景（放在 Hud.cs 裡會變成 Missing Script）
    /// <summary>
    /// 場景裡的文字標籤（名牌、零件編號、零件架名稱）。由 UI 層畫在畫面上，所以中英文、字型都和介面一致。
    /// </summary>
    public sealed class WorldLabel : MonoBehaviour
    {
        public static readonly List<WorldLabel> All = new List<WorldLabel>();

        [Tooltip("字串鍵（有的話優先，依語言顯示）")]
        public string key = "";
        [Tooltip("直接顯示的文字（沒有字串鍵時用）")]
        public string text = "";
        public Vector3 offset = Vector3.up;
        public float maxDistance = 8f;
        [Tooltip("tag（名牌）、sign（看板）、code（零件編號）")]
        public string style = "sign";
        public string color = "";

        public string Resolve() => string.IsNullOrEmpty(key) ? text : Loc.T(key);
        public Vector3 WorldPos => transform.position + offset;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }
}
