using System.Collections.Generic;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 零件架：放哪些類別、幾層、多寬。零件本身由 <see cref="FacilityDirector"/> 依難度在執行時擺上去
    /// （學員難度每類只有 2 種）。架子的局部座標：+Z 朝外（玩家站的那一側），層板沿 X 排開。
    /// </summary>
    public sealed class PartRack : MonoBehaviour
    {
        [Tooltip("逗號分隔的零件類別，例如 solar,battery")]
        public string categories = "";
        public float width = 4f;
        public float depth = 0.7f;
        public float[] shelfHeights = { 0.32f, 0.86f, 1.4f };
        public float itemSize = 0.42f;

        public IEnumerable<string> Categories
        {
            get
            {
                foreach (var c in categories.Split(','))
                    if (!string.IsNullOrWhiteSpace(c)) yield return c.Trim();
            }
        }

        /// <summary>第 i 個零件（共 n 個）在架上的局部位置：先填下層，再往上。</summary>
        public Vector3 Slot(int i, int n)
        {
            int shelves = shelfHeights.Length;
            int perShelf = Mathf.Max(1, Mathf.CeilToInt(n / (float)shelves));
            int shelf = i / perShelf;
            int col = i % perShelf;
            int inThis = Mathf.Min(perShelf, n - shelf * perShelf);
            float step = width / Mathf.Max(1, inThis);
            float x = -width / 2 + step * (col + 0.5f);
            return new Vector3(x, shelfHeights[Mathf.Min(shelf, shelves - 1)], 0.05f);
        }
    }
}
