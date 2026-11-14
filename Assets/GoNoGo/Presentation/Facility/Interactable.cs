using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>準星對得到的東西。碰撞體在這個物件或子物件上。</summary>
    public abstract class Interactable : MonoBehaviour
    {
        /// <summary>場景裡啟用中的互動物件（第三人稱找身邊的目標用）。</summary>
        public static readonly List<Interactable> All = new List<Interactable>();

        Collider[] _cols;

        /// <summary>資訊框內容；null 表示現在不能互動。</summary>
        public abstract HudTarget Describe(Interactor who);
        /// <summary>按下 C 或 E。</summary>
        public virtual void OnKey(Interactor who, char key) { }
        public virtual void SetHighlight(bool on) { }
        public virtual float Range => 2.5f;

        /// <summary>
        /// 自己和子物件的碰撞體（零件是執行時才建的，第一次用到時才收集）。
        /// 子物件會重建（整合台上的船每次裝、拆零件都整艘重組）：舊的碰撞體被銷毀後這份清單就過期了，
        /// 發現有被銷毀的、或有人呼叫 <see cref="InvalidateColliders"/>，下次用到時重新收集。
        /// </summary>
        public Collider[] Colliders
        {
            get
            {
                if (_cols == null || Stale(_cols)) _cols = GetComponentsInChildren<Collider>(true);
                return _cols;
            }
        }

        /// <summary>子物件換過了（重建模型、開關零件）：下次用到 <see cref="Colliders"/> 時重新收集。</summary>
        public void InvalidateColliders() => _cols = null;

        static bool Stale(Collider[] cols)
        {
            if (cols.Length == 0) return true;
            foreach (var c in cols) if (c == null) return true;
            return false;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }
}
