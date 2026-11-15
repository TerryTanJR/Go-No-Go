using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 放線索的一個位置：研究牆上的草稿紙、白板、黑板、書堆裡的一本書（中控室），牆上的設計圖、架子旁的便條（組裝區）。
    /// 遠看只是一張紙、一本書；靠近按 E 放大才看得出內容是不是有用的訊息。放大的內容只有自己看得到，
    /// 不能投到設計主螢幕，要靠口頭告訴隊友。哪一則線索擺在這裡每局不同（<see cref="Clues.Place"/>），
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
        /// <summary>這一局這裡有東西可看（線索或太空知識）。</summary>
        bool _used = true;

        public override float Range => range;

        string Medium => Clues.Spot(spotId)?.Medium ?? "paper";

        /// <summary>
        /// 拿不走的東西：白板、黑板、書，和研究牆上用紅線連著的草稿紙。這一局沒用到時留著當擺設（只是不能放大）；
        /// 其他便條、設計圖整張收起來。
        /// </summary>
        bool Fixture
        {
            get
            {
                var spot = Clues.Spot(spotId);
                return spot == null || spot.Medium != "paper" && spot.Medium != "blueprint" || spot.Area == "research";
            }
        }

        /// <summary>照這一局的擺法決定這裡有沒有東西（<see cref="MissionSession.CluesAt"/>、<see cref="MissionSession.TriviaAt"/>）。</summary>
        public void Sync(MissionSession m)
        {
            _used = m.CluesAt(spotId).Count > 0 || m.TriviaAt(spotId) >= 0;
            if (!Fixture) gameObject.SetActive(_used);
        }

        public override HudTarget Describe(Interactor who)
        {
            if (!_used) return null;
            var t = new HudTarget { Title = Loc.T("ui.clue.medium." + Medium), Body = Loc.T("ui.clue.look") };
            t.Keys.Add(new KeyHint("E", Loc.T("ui.hint.zoom")));
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (key != 'E' || !_used) return;
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
