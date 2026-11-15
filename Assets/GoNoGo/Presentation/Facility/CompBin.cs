using System.Collections.Generic;
using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 小零件架上的一格：一格一種小零件，不限數量，拿走會補。所有配方會用到的小零件都在架上，
    /// 包括這次設計用不到的；架子只標名稱，不提示該拿什麼（要看中控室大屏幕或組裝區儀表板）。
    /// 按 C 拿；單人有籃子（一次最多 4 個），兩人以上一次拿一個。手上已經有這一種：按 C 放回去。
    /// 擺哪一種由 <see cref="GameData.ShelfComponents"/> 決定（第幾格 = <see cref="index"/>）。
    /// </summary>
    public sealed class CompBin : Interactable
    {
        public static readonly List<CompBin> Bins = new List<CompBin>();

        public int index;
        [Tooltip("小零件擺在這裡（盒底的中心）")]
        public Transform slot;
        public Renderer glow;

        public override float Range => 2.4f;

        AssemblyComponent _comp;
        GameObject _model;
        SatPart _glowPart;
        WorldLabel _label;
        string _lang;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            Bins.Add(this);
            _label = gameObject.AddComponent<WorldLabel>();
            _label.style = "comp";
            _label.maxDistance = 7f;
            _label.offset = new Vector3(0, 0.36f, 0);
            _label.enabled = false;
        }

        void OnDestroy() => Bins.Remove(this);

        static MissionSession M => GameSession.Mission;

        /// <summary>這一格擺的小零件（null = 架子比小零件多出來的空格）。</summary>
        public AssemblyComponent Component => _comp;

        void Start()
        {
            var shelf = GameSession.Data.ShelfComponents();
            _comp = index >= 0 && index < shelf.Count ? shelf[index] : null;
            if (_comp == null) return;
            _model = SatelliteBuilder.BuildComponent(_comp.Id, slot != null ? slot : transform, 0.24f);
            _glowPart = _model.GetComponent<SatPart>();
            _label.text = Loc.T(_comp.Name);
            _label.enabled = true;
            _lang = Loc.Lang;
            InvalidateColliders();
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
            if (!GameSession.HasMission || _comp == null) return null;
            var s = M.S;
            var t = new HudTarget { Title = Loc.T(_comp.Name) };
            if (s.IsSandbox(M.Data)) { t.Subtitle = Loc.T("ui.bench.sandbox"); return t; }
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) { t.Subtitle = Loc.T("ui.hint.not_now"); return t; }
            if (who.Comps.Contains(_comp.Id)) { t.Keys.Add(new KeyHint("C", Loc.T("ui.hint.drop"))); return t; }
            string why = SessionRules.CanCarry(s, GameSession.LocalPlayer, _comp.Id);
            if (why == SessionRules.Ok) t.Keys.Add(new KeyHint("C", Loc.T(s.Alone() ? "ui.hint.pick_basket" : "ui.hint.pick")));
            else t.Subtitle = Loc.T(who.Holding != null ? "ui.hint.carry_part" : s.Alone() ? "ui.hint.basket_full" : "ui.hint.hands_full");
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (key != 'C' || _comp == null || !GameSession.HasMission) return;
            if (who.Comps.Contains(_comp.Id))
            {
                GameSession.Send(CommandType.CompDrop, _comp.Id);
                Sfx.Drop();
                return;
            }
            string why = SessionRules.CanCarry(M.S, GameSession.LocalPlayer, _comp.Id);
            if (why != SessionRules.Ok) { Sfx.NoGo(); Hud.Toast(Loc.T(why)); return; }
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
