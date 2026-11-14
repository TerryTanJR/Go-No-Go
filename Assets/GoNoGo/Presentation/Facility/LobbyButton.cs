using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 大廳兩邊各一顆的實體按鈕，走到旁邊按 E：還不是這一組 → 加入這一組；已經在這組 → 準備；準備了 → 取消準備。
    /// 準備之後不能直接換到另一組（要先取消準備）。亮度：暗 = 不在這組、亮 = 在這組、綠 = 已準備。
    /// </summary>
    public sealed class LobbyButton : Interactable
    {
        public Station team;
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
        bool Mine => Me != null && Me.Station == team;

        public override HudTarget Describe(Interactor who)
        {
            if (!InLobby) return null;
            var me = Me;
            var h = new HudTarget
            {
                Title = Loc.T(team == Station.Assembly ? "ui.lobby.join.assembly" : "ui.lobby.join.control"),
                Color = "#" + ColorUtility.ToHtmlStringRGB(teamColor),
            };
            if (me == null) return h;
            if (Mine) h.Keys.Add(new KeyHint("E", Loc.T(me.Ready ? "ui.lobby.act.cancel" : "ui.lobby.act.ready")));
            else if (me.Ready) h.Subtitle = Loc.T("ui.lobby.ready_lock");
            else h.Keys.Add(new KeyHint("E", Loc.T("ui.lobby.join")));
            return h;
        }

        public override void OnKey(Interactor who, char key)
        {
            var me = Me;
            if (key != 'E' || !InLobby || me == null) return;
            if (Mine)
            {
                GameSession.Send(CommandType.SetReady, "", me.Ready ? 0 : 1);
                if (me.Ready) Sfx.Click(); else Sfx.Confirm();
                return;
            }
            if (me.Ready) { Sfx.NoGo(); Hud.Toast(Loc.T("ui.lobby.ready_lock")); return; }
            GameSession.Send(CommandType.SetStation, "", team == Station.Control ? 1 : 0);
            Sfx.Click();
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
            int state = Mine ? (me.Ready ? 3 : 2) : _hover ? 1 : 0;
            if (state == _lit) return;
            _lit = state;
            _mpb ??= new MaterialPropertyBlock();
            cap.GetPropertyBlock(_mpb);
            var col = state == 3 ? ReadyColor : teamColor;
            float k = state == 0 ? 0.4f : state == 1 ? 1.4f : state == 2 ? 2f : 3.2f;
            _mpb.SetColor("_EmissionColor", col * k);
            _mpb.SetColor("_BaseColor", col * (state == 0 ? 0.4f : 1f));
            cap.SetPropertyBlock(_mpb);
        }
    }
}
