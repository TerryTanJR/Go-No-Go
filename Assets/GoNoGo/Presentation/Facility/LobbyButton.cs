using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 大廳中央的準備按鈕（大地球前面，所有人共用一顆），走到旁邊按 E：準備／取消準備。全員準備好房主才能開始。
    /// 顏色：還沒準備 = <see cref="idleColor"/>、已準備 = 綠。
    /// </summary>
    public sealed class LobbyButton : Interactable
    {
        [Tooltip("按鈕帽（亮起來的部分）")]
        public Renderer cap;
        public Color idleColor = new Color(0.22f, 0.78f, 0.96f);

        static readonly Color ReadyColor = new Color(0.3f, 0.95f, 0.45f);

        MaterialPropertyBlock _mpb;
        int _lit = -1;
        bool _hover;

        public override float Range => 3.2f;

        /// <summary>本機玩家在名單裡的那一筆。每幀都會問，所以直接掃名單（SessionState.Player 用 LINQ，每次呼叫都會配置記憶體）。</summary>
        PlayerInfo Me
        {
            get
            {
                if (!GameSession.HasMission) return null;
                var players = GameSession.Mission.S.Players;
                int me = GameSession.LocalPlayer;
                for (int i = 0; i < players.Count; i++)
                    if (players[i].Id == me) return players[i];
                return null;
            }
        }
        bool InLobby => GameSession.HasMission && GameSession.Mission.S.Phase == SessionPhase.Lobby;

        public override HudTarget Describe(Interactor who)
        {
            if (!InLobby) return null;
            var me = Me;
            var h = new HudTarget
            {
                Title = Loc.T("ui.lobby.ready_button"),
                Color = "#" + ColorUtility.ToHtmlStringRGB(me != null && me.Ready ? ReadyColor : idleColor),
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
            var col = state == 3 ? ReadyColor : idleColor;
            float k = state == 0 ? 0.7f : state == 1 ? 1.6f : 3.2f;
            _mpb.SetColor("_EmissionColor", col * k);
            _mpb.SetColor("_BaseColor", col * (state == 0 ? 0.6f : 1f));
            cap.SetPropertyBlock(_mpb);
        }
    }
}
