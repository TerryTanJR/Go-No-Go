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
        [Tooltip("面板 id：dashboard、procure、manual、bay_manual、goals、phone、mission、launch、failures、ops_desk、ops:power…")]
        public string panel = "";
        public string titleKey = "";
        public string bodyKey = "";
        [Tooltip("提示小字的字串鍵（預設：使用）")]
        public string actionKey = "ui.hint.use";
        [Tooltip("哪些階段可以用（空 = 都可以）：design、ops、briefing、lobby")]
        public string phases = "";
        public Renderer glow;

        MaterialPropertyBlock _mpb;

        /// <summary>環境測試台在採購機制 v2 拿掉了；場景裡舊的測試台直接隱藏（重建場景時 FacilityBuilder 不再產生它）。</summary>
        void Awake()
        {
            if (panel == "test") gameObject.SetActive(false);
        }

        /// <summary>
        /// 嚴格分工時這個控制台屬於哪一區（依面板 id）：採購終端、儀表板大螢幕、組裝手冊是組裝區的；
        /// 任務目標與預算、軌道與火箭、電話、筆記、發射、中控室手冊（型錄）是中控室的。失敗看板兩邊都能看。
        /// </summary>
        public static Station? StationOf(string panel) => panel switch
        {
            "report" or "bay_manual" or "procure" or "dashboard" or "assemble" => Station.Assembly,
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

        bool PhaseOk
        {
            get
            {
                if (string.IsNullOrEmpty(phases) || !GameSession.HasMission) return true;
                var p = GameSession.Mission.S.Phase.ToString().ToLowerInvariant();
                return phases.Contains(p);
            }
        }

        public bool AvailableNow => PhaseOk && Reveal.Lock(panel) == null;

        /// <summary>現在不能用的原因：營運才用的控制台說「營運時才用得到」，其他照 Reveal，都不是就是「現在不能用」。</summary>
        string LockKey => Reveal.Lock(panel) ?? (phases.Contains("ops") && !phases.Contains("design") ? "ui.hint.locked_ops" : "ui.hint.not_now");

        public override HudTarget Describe(Interactor who)
        {
            var t = new HudTarget { Title = Loc.T(titleKey), Body = string.IsNullOrEmpty(bodyKey) ? "" : Loc.T(bodyKey) };
            var block = StrictBlock;
            if (block != null) { t.Subtitle = Loc.T(block); t.Color = "#FFB547"; }
            else if (AvailableNow) t.Keys.Add(new KeyHint("E", Loc.T(actionKey)));
            else t.Subtitle = Loc.T(LockKey);
            return t;
        }

        // 現在用不到的控制台：螢幕和燈條調暗，進到用得到的階段再亮起來
        Renderer _screen;
        bool _off;
        float _checkAt;

        void Start()
        {
            // 只調單一材質的螢幕（多材質的模型連外框一起調會變色）；螢幕本身就是燈條時交給 SetHighlight
            var r = GetComponent<Renderer>();
            if (r != null && r != glow && r.sharedMaterials.Length == 1) _screen = r;
        }

        void Update()
        {
            if (Time.unscaledTime < _checkAt) return;
            _checkAt = Time.unscaledTime + 0.3f;
            bool off = GameSession.HasMission && !AvailableNow;
            if (off == _off) return;
            _off = off;
            if (_screen != null)
            {
                _mpb ??= new MaterialPropertyBlock();
                _screen.GetPropertyBlock(_mpb);
                if (off) _mpb.SetColor("_BaseColor", new Color(0.12f, 0.13f, 0.15f, 1f));
                else _mpb.Clear();
                _screen.SetPropertyBlock(_mpb);
            }
            SetHighlight(false);
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
            _mpb.SetColor("_EmissionColor", on ? baseC * 2.5f + new Color(0.1f, 0.35f, 0.45f) : _off ? baseC * 0.15f : baseC);
            glow.SetPropertyBlock(_mpb);
        }
    }
}
