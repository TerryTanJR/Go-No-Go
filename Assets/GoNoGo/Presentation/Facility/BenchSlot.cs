using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 組裝台左側的一格：小零件先放到這一側，系統比對這幾格的組合認配方。
    /// 手上有小零件：按 E 放上來（籃子裡的一次全部放）。格子上有小零件：按 C 拿起來
    /// （組合錯了拿走調整；配方認出來之後，照提示的順序一個一個拿到右側裝）。
    /// 擺什麼由 <see cref="BenchState.Left"/> 決定（第幾座 = <see cref="bench"/>、第幾格 = <see cref="index"/>）。
    /// </summary>
    public sealed class BenchSlot : Interactable
    {
        public int bench;
        public int index;
        [Tooltip("小零件擺在這裡（格子的中心）")]
        public Transform slot;
        public Renderer glow;

        public override float Range => 2.6f;

        string _compId;
        GameObject _model;
        SatPart _glowPart;
        WorldLabel _label;
        string _lang;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _label = gameObject.AddComponent<WorldLabel>();
            _label.style = "comp";
            _label.maxDistance = 6f;
            _label.offset = new Vector3(0, 0.34f, 0);
            _label.enabled = false;
        }

        static MissionSession M => GameSession.Mission;

        /// <summary>依狀態更新這一格擺的小零件（<see cref="Workbench.Sync"/> 呼叫）。</summary>
        public void Sync(MissionSession m)
        {
            var left = m.S.BenchAt(bench)?.Left;
            string id = left != null && index < left.Count ? left[index] : null;
            if (id == _compId) return;
            if (_model != null) Destroy(_model);
            _model = null;
            _glowPart = null;
            _compId = id;
            var comp = m.Data.Component(id);
            _label.enabled = comp != null;
            if (comp == null) { InvalidateColliders(); return; }
            _model = SatelliteBuilder.BuildComponent(id, slot != null ? slot : transform, 0.2f);
            _glowPart = _model.GetComponent<SatPart>();
            _label.text = Loc.T(comp.Name);
            _lang = Loc.Lang;
            InvalidateColliders();
        }

        void Update()
        {
            if (_compId != null && _lang != Loc.Lang && GameSession.HasMission)
            {
                _lang = Loc.Lang;
                var comp = M.Data.Component(_compId);
                if (comp != null) _label.text = Loc.T(comp.Name);
            }
        }

        public override HudTarget Describe(Interactor who)
        {
            if (!GameSession.HasMission) return null;
            var s = M.S;
            var data = M.Data;
            var comp = data.Component(_compId);
            var t = new HudTarget { Title = comp != null ? Loc.T(comp.Name) : Loc.T("ui.fac.bench_left") };
            if (s.IsSandbox(data)) { t.Subtitle = Loc.T("ui.bench.sandbox"); return t; }
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) { t.Subtitle = Loc.T("ui.hint.not_now"); return t; }
            var status = SessionRules.BenchStatusOf(s, data, bench);
            if (comp == null) t.Body = Loc.T(BenchJig.StatusKey(status));
            else if (status != BenchStatus.Active) t.Subtitle = Loc.T(BenchJig.StatusKey(status));
            // 手上有小零件：放到左側
            if (who.Comps.Count > 0)
            {
                string why = SessionRules.CanPut(s, data, GameSession.LocalPlayer, bench);
                if (why == SessionRules.Ok) t.Keys.Add(new KeyHint("E", Loc.T("ui.hint.bench_put")));
                else if (status != BenchStatus.Active) t.Subtitle = Loc.T(why);
            }
            if (comp != null)
            {
                bool canTake = who.Holding == null && (status == BenchStatus.Active ? who.Comps.Count == 0
                    : SessionRules.CanCarry(s, GameSession.LocalPlayer, _compId) == SessionRules.Ok);
                if (canTake) t.Keys.Add(new KeyHint("C", Loc.T(status == BenchStatus.Active ? "ui.hint.bench_take_fit" : "ui.hint.pick")));
            }
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (!GameSession.HasMission) return;
            var s = M.S;
            if (key == 'E')
            {
                if (who.Comps.Count == 0) return;
                BenchJig.Put(bench);
            }
            else if (key == 'C' && _compId != null)
            {
                if (who.Holding != null) { Sfx.NoGo(); Hud.Toast(Loc.T("err.hands_full")); return; }
                if (s.BenchAt(bench)?.Job != null && who.Comps.Count > 0) { Sfx.NoGo(); Hud.Toast(Loc.T("err.hands_full")); return; }
                string why = SessionRules.CanCarry(s, GameSession.LocalPlayer, _compId);
                if (why != SessionRules.Ok) { Sfx.NoGo(); Hud.Toast(Loc.T(why)); return; }
                GameSession.Send(CommandType.BenchTake, _compId, bench);
                Sfx.Pick();
                Hud.Signal("bench_take");
            }
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
