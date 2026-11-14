using System;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>新手訓練的提示框（第 6 階段補完步驟內容）。</summary>
    public sealed class TutorialCoach : IDisposable
    {
        readonly VisualElement _box;

        public TutorialCoach(VisualElement parent)
        {
            _box = Ui.El("coach").AddTo(parent);
            _box.pickingMode = PickingMode.Ignore;
        }

        public void Tick(float dt) { }

        public void Dispose() => _box.RemoveFromHierarchy();
    }
}
