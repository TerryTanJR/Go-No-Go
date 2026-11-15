using System;
using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine.UIElements;

namespace GoNoGo.UI
{
    /// <summary>
    /// 一個場景的介面。UIRoot 依場景名稱建立對應的畫面；切語言時整個重建（狀態都在 MissionSession，重建不會遺失）。
    /// 用 <see cref="On"/> 訂閱的事件會在畫面關閉時自動取消，避免場景切換後還收到事件。
    /// </summary>
    public abstract class GameScreen
    {
        protected VisualElement Root { get; private set; }
        /// <summary>這一局（直接開場景測試時自動建立）。</summary>
        protected static MissionSession M => GameSession.Mission;
        protected static GameData D => GameSession.Data;
        readonly List<Action> _unsubscribe = new List<Action>();

        public void Attach(VisualElement root)
        {
            Root = root;
            Build();
        }

        public void Rebuild()
        {
            DisposeSubscriptions();
            Root.Clear();
            Build();
        }

        protected abstract void Build();

        public virtual void Tick(float dt) { }

        /// <summary>按 Esc 時先問畫面（例如關掉開著的面板）。回傳 true = 已處理，不要打開遊戲選單。</summary>
        public virtual bool HandleEscape() => false;

        public void Close()
        {
            DisposeSubscriptions();
            OnClose();
            Root?.Clear();
        }

        protected virtual void OnClose() { }

        /// <summary>訂閱事件，畫面關閉或重建時自動取消。</summary>
        protected void On(Action subscribe, Action unsubscribe)
        {
            subscribe();
            _unsubscribe.Add(unsubscribe);
        }

        void DisposeSubscriptions()
        {
            foreach (var u in _unsubscribe)
            {
                try { u(); } catch { /* 物件可能已被銷毀 */ }
            }
            _unsubscribe.Clear();
        }

        /// <summary>頂部列：品牌、任務名。</summary>
        protected void TopBar(string missionLabel)
        {
            var bar = Ui.El("topbar").AddTo(Root);
            Ui.Text("GO / NO-GO", "brand").AddTo(bar);
            if (!string.IsNullOrEmpty(missionLabel)) Ui.Text(missionLabel, "mission").AddTo(bar);
        }
    }
}
