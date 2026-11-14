using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 控制台、電話、手冊、回報台…：按 E 打開對應的面板（或直接送一個操作）。
    /// </summary>
    public sealed class ConsoleTarget : Interactable
    {
        [Tooltip("面板 id：dashboard、manual、phone、mission、launch、failures、ops_desk、ops:power…；report = 直接回報設計")]
        public string panel = "";
        public string titleKey = "";
        public string bodyKey = "";
        [Tooltip("提示小字的字串鍵（預設：使用）")]
        public string actionKey = "ui.hint.use";
        [Tooltip("哪些階段可以用（空 = 都可以）：design、ops、briefing、lobby")]
        public string phases = "";
        public Renderer glow;

        MaterialPropertyBlock _mpb;

        public bool AvailableNow
        {
            get
            {
                if (string.IsNullOrEmpty(phases) || !GameSession.HasMission) return true;
                var p = GameSession.Mission.S.Phase.ToString().ToLowerInvariant();
                return phases.Contains(p);
            }
        }

        public override HudTarget Describe(Interactor who)
        {
            var t = new HudTarget { Title = Loc.T(titleKey), Body = string.IsNullOrEmpty(bodyKey) ? "" : Loc.T(bodyKey) };
            if (AvailableNow) t.Keys.Add(new KeyHint("E", Loc.T(actionKey)));
            else t.Subtitle = Loc.T("ui.hint.not_now");
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (key != 'E' || !AvailableNow) return;
            if (panel == "report")
            {
                GameSession.Send(CommandType.Report);
                Sfx.Confirm();
                Hud.Toast(Loc.T("ui.fac.reported"));
                Hud.Signal("report");
                return;
            }
            Sfx.Click();
            Hud.Open(panel);
            Hud.Signal("open:" + panel);
        }

        public override void SetHighlight(bool on)
        {
            if (glow == null) return;
            _mpb ??= new MaterialPropertyBlock();
            glow.GetPropertyBlock(_mpb);
            var m = glow.sharedMaterial;
            var baseC = m != null && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            _mpb.SetColor("_EmissionColor", on ? baseC * 2.5f + new Color(0.1f, 0.35f, 0.45f) : baseC);
            glow.SetPropertyBlock(_mpb);
        }
    }
}
