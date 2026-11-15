using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 零件盤上的一格：工作台上放了一箱小零件之後，這一格擺出其中一個小零件（或混進來、不屬於這個零件的）。
    /// 只標名稱，不寫它是做什麼的：玩家要自己判斷「工作台要的那件事是誰在做」。按 C 拿起來，拿到工作台長按 V 裝上去。
    /// 擺什麼由 <see cref="SessionRules.TrayNow"/> 決定（第幾格 = <see cref="index"/>）。
    /// </summary>
    public sealed class CompTray : Interactable
    {
        public static readonly List<CompTray> Trays = new List<CompTray>();

        public int index;
        [Tooltip("小零件擺在這裡（盤底的中心）")]
        public Transform slot;
        public Renderer glow;

        public override float Range => 2.2f;

        AssemblyComponent _comp;
        GameObject _model;
        SatPart _glowPart;
        WorldLabel _label;
        string _lang;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            Trays.Add(this);
            _label = gameObject.AddComponent<WorldLabel>();
            _label.style = "comp";
            _label.maxDistance = 9f;
            _label.offset = new Vector3(0, 0.5f, 0);
            _label.enabled = false;
        }

        void OnDestroy() => Trays.Remove(this);

        static MissionSession M => GameSession.Mission;

        /// <summary>這一格現在擺的小零件（null = 空的）。</summary>
        public AssemblyComponent Component => _comp;

        public void Sync(MissionSession m)
        {
            var tray = SessionRules.TrayNow(m.S, m.Data);
            var c = index < tray.Count ? tray[index] : null;
            if (c?.Id == _comp?.Id) return;
            if (_model != null) Destroy(_model);
            _model = null;
            _glowPart = null;
            _comp = c;
            _label.enabled = c != null;
            if (c == null) return;
            _model = SatelliteBuilder.BuildComponent(c.Id, slot != null ? slot : transform, 0.26f);
            _glowPart = _model.GetComponent<SatPart>();
            _label.text = Loc.T(c.Name);
            _lang = Loc.Lang;
        }

        void Update()
        {
            // 換語言：標籤跟著換
            if (_comp != null && _lang != Loc.Lang)
            {
                _lang = Loc.Lang;
                _label.text = Loc.T(_comp.Name);
            }
        }

        public override HudTarget Describe(Interactor who)
        {
            if (!GameSession.HasMission) return null;
            var s = M.S;
            // 手上拿著小零件：對著哪一格都是「放回去」
            if (who.HoldingComp != null)
            {
                var hc = M.Data.Component(who.HoldingComp);
                var back = new HudTarget { Title = hc != null ? Loc.T(hc.Name) : Loc.T("ui.fac.rack") };
                back.Keys.Add(new KeyHint("C", Loc.T("ui.hint.drop")));
                return back;
            }
            if (_comp == null)
            {
                bool working = SessionRules.BenchRecipe(s, M.Data) != null;
                bool sandbox = s.Scenario(M.Data)?.Sandbox == true;
                return new HudTarget { Title = Loc.T("ui.fac.rack"), Subtitle = Loc.T(sandbox ? "ui.bench.sandbox" : working ? "ui.tray.slot_empty" : "ui.tray.empty") };
            }
            var t = new HudTarget { Title = Loc.T(_comp.Name) };
            if (s.StrictNow() && s.Player(GameSession.LocalPlayer)?.Station != Station.Assembly)
            {
                t.Subtitle = Loc.T("ui.hint.strict_assembly");
                t.Color = "#FFB547";
            }
            else if (who.Holding != null) t.Subtitle = Loc.T("ui.hint.hands_full");
            else t.Keys.Add(new KeyHint("C", Loc.T("ui.hint.pick")));
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (key != 'C' || _comp == null || !GameSession.HasMission) return;
            if (who.Holding != null || who.HoldingComp != null) { Hud.Toast(Loc.T("err.hands_full")); return; }
            GameSession.Send(CommandType.CompPick, _comp.Id);
            Sfx.Pick();
            Hud.Signal("comp_pick");
        }

        public override void SetHighlight(bool on)
        {
            if (_glowPart != null) _glowPart.SetGlow(on ? 0.8f : 0f);
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
