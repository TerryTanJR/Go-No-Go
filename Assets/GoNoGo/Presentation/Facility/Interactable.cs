using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>準星對得到的東西。碰撞體在這個物件或子物件上。</summary>
    public abstract class Interactable : MonoBehaviour
    {
        /// <summary>資訊框內容；null 表示現在不能互動。</summary>
        public abstract HudTarget Describe(Interactor who);
        /// <summary>按下 C 或 E。</summary>
        public virtual void OnKey(Interactor who, char key) { }
        public virtual void SetHighlight(bool on) { }
        public virtual float Range => 2.5f;
    }
}
