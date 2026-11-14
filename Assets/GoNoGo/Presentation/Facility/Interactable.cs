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

        /// <summary>自己和子物件的碰撞體（零件是執行時才建的，第一次用到時才收集）。</summary>
        public Collider[] Colliders => _cols != null && _cols.Length > 0 ? _cols : _cols = GetComponentsInChildren<Collider>(true);

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
    }
}
