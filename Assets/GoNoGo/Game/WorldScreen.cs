using System.Collections.Generic;
using UnityEngine;

namespace GoNoGo.Game
{
    // 必須自己一個檔案、檔名和類別同名（同上）
    /// <summary>
    /// 場景裡的螢幕：UI 層把對應的面板（依 id）畫到 RenderTexture，再貼到這個 Renderer 上。
    /// </summary>
    public sealed class WorldScreen : MonoBehaviour
    {
        public static readonly List<WorldScreen> All = new List<WorldScreen>();

        [Tooltip("bigscreen（設計主螢幕）、wall_reqs、wall_asm、bay、failures、bench:0、bench:1、lobby")]
        public string id = "";
        public Vector2Int resolution = new Vector2Int(1024, 640);
        public Renderer target;
        [Tooltip("營運、報告階段改貼這個材質（設計主螢幕：地球縮景的畫面）；沒有就一直畫介面")]
        public Material opsMaterial;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }
}
