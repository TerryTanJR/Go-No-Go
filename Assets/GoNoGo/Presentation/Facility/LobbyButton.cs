using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 大廳的實體按鈕，走到旁邊按 E：準備／取消準備。全員準備好房主才能開始。
    /// 不分組：所有人開局都在中控室，分工自己決定，所以兩邊的按鈕作用一樣，按哪一顆都可以。
    /// 亮度：暗 = 還沒準備、綠 = 已準備。
    /// </summary>
    public sealed class LobbyButton : Interactable
    {
        [Tooltip("按鈕帽（亮起來的部分）")]
        public Renderer cap;
        public Color teamColor = new Color(0.22f, 0.78f, 0.96f);

        static readonly Color ReadyColor = new Color(0.3f, 0.95f, 0.45f);

        MaterialPropertyBlock _mpb;
        int _lit = -1;
        bool _hover;

        public override float Range => 3.2f;

        PlayerInfo Me => GameSession.HasMission ? GameSession.Mission.S.Player(GameSession.LocalPlayer) : null;
        bool InLobby => GameSession.HasMission && GameSession.Mission.S.Phase == SessionPhase.Lobby;

        public override HudTarget Describe(Interactor who)
        {
            if (!InLobby) return null;
            var me = Me;
            var h = new HudTarget
            {
                Title = Loc.T("ui.lobby.ready_button"),
                Color = "#" + ColorUtility.ToHtmlStringRGB(me != null && me.Ready ? ReadyColor : teamColor),
            };
            if (me == null) return h;
            h.Keys.Add(new KeyHint("E", Loc.T(me.Ready ? "ui.lobby.act.cancel" : "ui.lobby.act.ready")));
            return h;
        }

        public override void OnKey(Interactor who, char key)
        {
            var me = Me;
            if (key != 'E' || !InLobby || me == null) return;
            GameSession.Send(CommandType.SetReady, "", me.Ready ? 0 : 1);
            if (me.Ready) Sfx.Click(); else Sfx.Confirm();
        }

        public override void SetHighlight(bool on)
        {
            _hover = on;
            _lit = -1;
        }

        void Update()
        {
            if (cap == null) return;
            var me = Me;
            int state = me != null && me.Ready ? 3 : _hover ? 1 : 0;
            if (state == _lit) return;
            _lit = state;
            _mpb ??= new MaterialPropertyBlock();
            cap.GetPropertyBlock(_mpb);
            var col = state == 3 ? ReadyColor : teamColor;
            float k = state == 0 ? 0.7f : state == 1 ? 1.6f : 3.2f;
            _mpb.SetColor("_EmissionColor", col * k);
            _mpb.SetColor("_BaseColor", col * (state == 0 ? 0.6f : 1f));
            cap.SetPropertyBlock(_mpb);
        }
    }
}
