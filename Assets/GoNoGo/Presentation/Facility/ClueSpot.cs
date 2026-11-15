using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 放線索的一個位置：研究牆上的草稿紙、白板、黑板、書堆裡的一本書（中控室），牆上的設計圖、架子旁的便條（組裝區）。
    /// 遠看只是一張紙、一本書；靠近按 E 放大才看得出內容是不是有用的訊息。放大的內容只有自己看得到，
    /// 不能投到大屏幕，要靠口頭告訴隊友。哪一則線索擺在這裡每局不同（<see cref="Clues.Place"/>），
    /// 位置的 id 要和 <see cref="Clues.Spots"/> 對得上。
    /// </summary>
    public sealed class ClueSpot : Interactable
    {
        [Tooltip("Clues.Spots 裡的 id：wall_1、whiteboard、blackboard、book_1、print_1、shelfnote_1…")]
        public string spotId = "";
        public Renderer glow;
        [Tooltip("互動距離（公尺）")]
        public float range = 2.6f;

        MaterialPropertyBlock _mpb;

        public override float Range => range;

        string Medium => Clues.Spot(spotId)?.Medium ?? "paper";

        public override HudTarget Describe(Interactor who)
        {
            var t = new HudTarget { Title = Loc.T("ui.clue.medium." + Medium), Body = Loc.T("ui.clue.look") };
            t.Keys.Add(new KeyHint("E", Loc.T("ui.hint.zoom")));
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (key != 'E') return;
            Sfx.Click();
            Hud.Open("clue:" + spotId);
            // 新手訓練的「放大看一則線索」：這個位置真的有線索才算（空的只是一張沒用的紙）
            if (GameSession.HasMission && GameSession.Mission.CluesAt(spotId).Count > 0) Hud.Signal("clue");
        }

        public override void SetHighlight(bool on)
        {
            if (glow == null) return;
            _mpb ??= new MaterialPropertyBlock();
            glow.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissionColor", on ? new Color(0.22f, 0.5f, 0.6f) : Color.black);
            glow.SetPropertyBlock(_mpb);
        }
    }
}
