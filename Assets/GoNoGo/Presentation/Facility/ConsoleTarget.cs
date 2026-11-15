using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 設計主螢幕、發射鈕、營運席、看板、書…：按 E 打開對應的面板。不分組，所有人都可以用。
    /// </summary>
    public sealed class ConsoleTarget : Interactable
    {
        [Tooltip("面板 id：select（設計主螢幕）、bay（組裝區儀表板）、launch、failures、ops_desk、ops:power、manual:rules…")]
        public string panel = "";
        public string titleKey = "";
        public string bodyKey = "";
        [Tooltip("提示小字的字串鍵（預設：使用）")]
        public string actionKey = "ui.hint.use";
        [Tooltip("哪些階段可以用（空 = 都可以）：design、ops、briefing、lobby")]
        public string phases = "";
        public Renderer glow;
        [Tooltip("互動距離（公尺）；0 = 預設。設計主螢幕很大，站遠一點也要對得到")]
        public float range;

        MaterialPropertyBlock _mpb;

        public override float Range => range > 0 ? range : base.Range;

        bool PhaseOk
        {
            get
            {
                if (string.IsNullOrEmpty(phases) || !GameSession.HasMission) return true;
                return phases.Contains(PhaseName(GameSession.Mission.S.Phase));
            }
        }

        static SessionPhase _namedPhase;
        static string _phaseName;

        /// <summary>階段名稱的小寫（"design"、"ops"…）。每座控制台每 0.3 秒就問一次，階段沒變就用上次產生的字串。</summary>
        static string PhaseName(SessionPhase p)
        {
            if (_phaseName == null || p != _namedPhase)
            {
                _namedPhase = p;
                _phaseName = p.ToString().ToLowerInvariant();
            }
            return _phaseName;
        }

        public bool AvailableNow => PhaseOk && Reveal.Lock(panel) == null;

        /// <summary>現在不能用的原因：營運才用的控制台說「營運時才用得到」，其他照 Reveal，都不是就是「現在不能用」。</summary>
        string LockKey => Reveal.Lock(panel) ?? (phases.Contains("ops") && !phases.Contains("design") ? "ui.hint.locked_ops" : "ui.hint.not_now");

        public override HudTarget Describe(Interactor who)
        {
            var t = new HudTarget { Title = Loc.T(titleKey), Body = string.IsNullOrEmpty(bodyKey) ? "" : Loc.T(bodyKey) };
            if (AvailableNow) t.Keys.Add(new KeyHint("E", Loc.T(actionKey)));
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
