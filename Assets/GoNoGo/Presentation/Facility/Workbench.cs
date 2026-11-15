using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 組裝工作台：買來的零件是一箱小零件，要在這裡親手組好才能裝上船。
    /// 拿著箱子按 E 放上來 → 到旁邊的零件盤（<see cref="CompTray"/>）拿小零件 → 走回來長按 V 裝上去；
    /// 全部裝好，成品留在台上，按 C 拿走。規則在 <see cref="SessionRules"/>（BenchPlace、CompPick、CompFit），
    /// 這裡只負責把狀態畫出來：台上的箱子、已經裝好的小零件、成品。全隊共用這一張工作台。
    /// </summary>
    public sealed class Workbench : Interactable
    {
        public static Workbench Instance { get; private set; }

        [Tooltip("組裝墊的中心（小零件和成品擺在這裡）")]
        public Transform jig;
        public Renderer glow;
        [Tooltip("裝對時的火花")]
        public ParticleSystem sparks;

        public override float Range => 2.8f;

        Transform _models;
        string _key;
        MaterialPropertyBlock _mpb;
        float _pulse;
        Renderer _nowPad;

        void Awake() => Instance = this;
        void OnDestroy() { if (Instance == this) Instance = null; }

        static MissionSession M => GameSession.Mission;

        /// <summary>依狀態更新台上的東西（FacilityDirector 在狀態改變時呼叫）。</summary>
        public void Sync(MissionSession m)
        {
            var job = m.S.Workbench;
            string key = job == null ? "" : job.PartId + "/" + job.Step + "/" + job.Done;
            if (key == _key) return;
            _key = key;
            if (_models != null) Destroy(_models.gameObject);
            _models = null;
            _nowPad = null;
            if (job == null || jig == null) return;
            var part = m.Data.Part(job.PartId);
            if (part == null) return;

            _models = new GameObject("BenchModels").transform;
            _models.SetParent(jig, false);
            var mats = SatMaterials.Load();
            if (job.Done)
            {
                // 成品：放在組裝墊中央
                var built = SatelliteBuilder.BuildPart(job.PartId, m.Data, _models, 0.5f);
                LayFlat(built);
                Seat(built, 0.02f);
            }
            else
            {
                // 打開的箱子在左後方；組裝墊上一格一個小零件的位置：裝好的放上模型，現在這一格亮著
                var kit = SatelliteBuilder.BuildKit(part, _models, 0.3f);
                kit.transform.localPosition = new Vector3(-0.36f, 0, 0.17f);
                Seat(kit, 0.01f);
                var recipe = m.Data.Recipe(part);
                int n = recipe != null ? recipe.Components.Count : 0;
                for (int i = 0; i < n; i++)
                {
                    float x = (i - (n - 1) / 2f) * 0.2f;
                    var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(pad.GetComponent<Collider>());
                    pad.name = "Pad" + i;
                    pad.transform.SetParent(_models, false);
                    pad.transform.localPosition = new Vector3(x, 0.006f, -0.08f);
                    pad.transform.localScale = new Vector3(0.17f, 0.008f, 0.17f);
                    var r = pad.GetComponent<Renderer>();
                    if (mats != null) r.sharedMaterial = i < job.Step ? mats.metal : mats.frameDark;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (i < job.Step)
                    {
                        var c = SatelliteBuilder.BuildComponent(recipe.Components[i].Id, _models, 0.15f);
                        c.transform.localPosition = new Vector3(x, 0.012f, -0.08f);
                    }
                    else if (i == job.Step)
                    {
                        if (mats != null) r.sharedMaterial = mats.accent;
                        _nowPad = r;
                    }
                }
            }
            InvalidateColliders();
        }

        /// <summary>
        /// 薄板狀的零件（電池、電腦板、太陽能板…）立著放時從正面看只剩一條線：讓最薄的那一軸朝上，平躺在組裝墊上。
        /// </summary>
        void LayFlat(GameObject model)
        {
            var b = SatelliteRig.ComputeBounds(model);
            var sz = jig.InverseTransformVector(b.size);
            sz = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
            float thin = Mathf.Min(sz.x, sz.y, sz.z), big = Mathf.Max(sz.x, sz.y, sz.z);
            if (thin >= big * 0.45f || thin == sz.y) return;
            var turn = thin == sz.x ? Quaternion.Euler(0, 0, 90) : Quaternion.Euler(90, 0, 0);
            model.transform.localRotation = turn * model.transform.localRotation;
            // 轉完之後中心會跑掉：移回組裝墊中央
            var nb = SatelliteRig.ComputeBounds(model);
            var off = jig.position - nb.center;
            model.transform.position += new Vector3(off.x, 0, off.z);
        }

        /// <summary>底部貼在組裝墊上。</summary>
        void Seat(GameObject model, float lift)
        {
            var b = SatelliteRig.ComputeBounds(model);
            model.transform.position += new Vector3(0, jig.position.y - b.min.y + lift, 0);
        }

        /// <summary>裝上一個小零件的效果（對：火花；錯：不放）。</summary>
        public void FitFx(bool ok)
        {
            if (ok && sparks != null && jig != null)
            {
                sparks.transform.position = jig.position + Vector3.up * 0.1f;
                sparks.Emit(24);
            }
        }

        void Update()
        {
            // 現在要裝的那一格：一明一暗地閃
            if (_nowPad == null) return;
            _pulse += Time.deltaTime * 4f;
            _mpb ??= new MaterialPropertyBlock();
            float k = 0.5f + 0.5f * Mathf.Sin(_pulse);
            _mpb.SetColor("_BaseColor", Color.Lerp(new Color(0.1f, 0.35f, 0.45f), new Color(0.3f, 0.9f, 1f), k));
            _mpb.SetColor("_EmissionColor", new Color(0.22f, 0.78f, 0.96f) * (0.4f + 1.6f * k));
            _nowPad.SetPropertyBlock(_mpb);
        }

        /// <summary>嚴格分工時只有組裝組能用（回傳提示的字串鍵；沒擋 = null）。</summary>
        static string StrictBlock
        {
            get
            {
                var s = M.S;
                if (!s.StrictNow()) return null;
                var me = s.Player(GameSession.LocalPlayer);
                return me == null || me.Station == Station.Assembly ? null : "ui.hint.strict_assembly";
            }
        }

        static bool PhaseOk => M.S.Phase == SessionPhase.Design || M.S.Phase == SessionPhase.Briefing;

        public override HudTarget Describe(Interactor who)
        {
            var t = new HudTarget { Title = Loc.T("ui.fac.bench") };
            if (!GameSession.HasMission) return t;
            var s = M.S;
            var data = M.Data;
            var block = StrictBlock;
            if (block != null) { t.Subtitle = Loc.T(block); t.Color = "#FFB547"; return t; }
            if (!PhaseOk) { t.Subtitle = Loc.T("ui.hint.not_now"); return t; }
            if (s.Scenario(data)?.Sandbox == true) { t.Body = Loc.T("ui.bench.sandbox"); return t; }

            var job = s.Workbench;
            string held = who.Holding, comp = who.HoldingComp;
            if (job == null)
            {
                if (held != null && SessionRules.NeedsAssembly(s, data, held))
                {
                    t.Body = Loc.T("ui.fac.bench_body");
                    t.Keys.Add(new KeyHint("E", Loc.T("ui.hint.bench_place")));
                    t.Keys.Add(new KeyHint("C", Loc.T("ui.hint.drop")));
                }
                else if (held != null) t.Subtitle = Loc.T("err.already_built");
                else t.Body = Loc.T("ui.bench.idle");
                return t;
            }
            var part = data.Part(job.PartId);
            if (job.Done)
            {
                t.Body = Loc.T("ui.bench.done", ("code", part?.Code ?? ""));
                if (held == null && comp == null) t.Keys.Add(new KeyHint("C", Loc.T("ui.hint.take_part")));
                else t.Subtitle = Loc.T("ui.hint.hands_full");
                return t;
            }
            var recipe = SessionRules.BenchRecipe(s, data);
            var want = SessionRules.BenchWants(s, data);
            if (recipe == null || want == null) return t;
            t.Title = Loc.T("ui.fac.bench") + "｜" + (part?.Code ?? "");
            t.Color = part?.Color ?? "";
            t.Subtitle = Loc.T("ui.bench.step", ("n", job.Step + 1), ("total", recipe.Components.Count));
            t.Body = Loc.T(want.Role);
            if (comp != null)
            {
                t.Keys.Add(new KeyHint("V", Loc.T("ui.hint.comp_fit")));
                t.Keys.Add(new KeyHint("C", Loc.T("ui.hint.drop")));
            }
            else if (held != null) t.Subtitle = Loc.T("err.bench_busy");
            else t.Body += "\n→ " + Loc.T("ui.bench.fetch");
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (!GameSession.HasMission || StrictBlock != null || !PhaseOk) return;
            var s = M.S;
            var job = s.Workbench;
            if (key == 'E')
            {
                if (who.Holding == null) return;
                string why = SessionRules.CanPlaceOnBench(s, M.Data, GameSession.LocalPlayer);
                if (why != SessionRules.Ok) { Sfx.NoGo(); Hud.Toast(Loc.T(why)); return; }
                GameSession.Send(CommandType.BenchPlace);
                Sfx.Drop();
                Hud.Signal("bench_place");
            }
            else if (key == 'C' && job != null && job.Done)
            {
                if (who.Holding != null || who.HoldingComp != null) { Hud.Toast(Loc.T("err.hands_full")); return; }
                GameSession.Send(CommandType.Pick, job.PartId);
                Sfx.Pick();
                Hud.Signal("pick:" + job.PartId);
            }
        }

        public override void SetHighlight(bool on)
        {
            if (glow == null) return;
            var mpb = new MaterialPropertyBlock();
            glow.GetPropertyBlock(mpb);
            var m = glow.sharedMaterial;
            var baseC = m != null && m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black;
            mpb.SetColor("_EmissionColor", on ? baseC * 2.5f + new Color(0.1f, 0.35f, 0.45f) : baseC);
            glow.SetPropertyBlock(mpb);
        }
    }
}
