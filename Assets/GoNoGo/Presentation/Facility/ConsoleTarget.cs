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
        [Tooltip("面板 id：dashboard、procure、manual、bay_manual、test、goals、phone、mission、launch、failures、ops_desk、ops:power…")]
        public string panel = "";
        public string titleKey = "";
        public string bodyKey = "";
        [Tooltip("提示小字的字串鍵（預設：使用）")]
        public string actionKey = "ui.hint.use";
        [Tooltip("哪些階段可以用（空 = 都可以）：design、ops、briefing、lobby")]
        public string phases = "";
        public Renderer glow;

        MaterialPropertyBlock _mpb;

        /// <summary>
        /// 嚴格分工時這個控制台屬於哪一區（依面板 id）：採購終端、儀表板大螢幕、測試台、組裝手冊是組裝區的；
        /// 任務目標與預算、軌道與火箭、電話、筆記、發射、中控室手冊（型錄）是中控室的。失敗看板兩邊都能看。
        /// </summary>
        public static Station? StationOf(string panel) => panel switch
        {
            "report" or "test" or "bay_manual" or "procure" or "dashboard" => Station.Assembly,
            "goals" or "mission" or "phone" or "notes" or "launch" or "manual" => Station.Control,
            // 後排的手冊各章（manual:payload…）也是中控室的
            _ when panel != null && panel.StartsWith("manual:") => Station.Control,
            _ => null,
        };

        /// <summary>嚴格分工擋住本機玩家（回傳原因的字串鍵，沒擋 = null）。</summary>
        public string StrictBlock
        {
            get
            {
                if (!GameSession.HasMission) return null;
                var s = GameSession.Mission.S;
                if (!s.StrictNow()) return null;
                var need = StationOf(panel);
                var me = s.Player(GameSession.LocalPlayer);
                if (need == null || me == null || me.Station == need) return null;
                return need == Station.Assembly ? "ui.hint.strict_assembly" : "ui.hint.strict_control";
            }
        }

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
            var block = StrictBlock;
            if (block != null) { t.Subtitle = Loc.T(block); t.Color = "#FFB547"; }
            else if (AvailableNow) t.Keys.Add(new KeyHint("E", Loc.T(actionKey)));
            else t.Subtitle = Loc.T("ui.hint.not_now");
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (key != 'E' || !AvailableNow) return;
            if (StrictBlock != null) { Sfx.NoGo(); Hud.Toast(Loc.T(StrictBlock)); return; }
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
