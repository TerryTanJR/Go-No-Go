using GoNoGo.Core;
using GoNoGo.Game;
using UnityEngine;

namespace GoNoGo.Presentation
{
    /// <summary>
    /// 組裝台右側（組裝墊）：左側的小零件組合被認出是設計裡某個零件的配方之後，在這裡照提示的順序一個一個裝上去。
    /// 提示只寫「這一步需要做什麼事的小零件」，名稱要玩家自己判斷。拿著小零件長按 V 裝上去；
    /// 全部裝好，成品留在這裡，按 C 拿走，再拿去整合台裝上衛星。
    /// 規則在 <see cref="SessionRules"/>（BenchPut、BenchTake、CompFit、Pick）。
    /// </summary>
    public sealed class BenchJig : Interactable
    {
        public int bench;
        public Renderer glow;

        public override float Range => 2.8f;

        static MissionSession M => GameSession.Mission;

        /// <summary>左側組合的狀態 → 一句說明的字串鍵。</summary>
        public static string StatusKey(BenchStatus status)
        {
            switch (status)
            {
                case BenchStatus.Partial: return "ui.bench.partial";
                case BenchStatus.Unknown: return "ui.bench.unknown";
                case BenchStatus.NotInDesign: return "ui.bench.not_in_design";
                case BenchStatus.Waiting: return "ui.bench.waiting";
                case BenchStatus.Active: return "ui.bench.active";
                case BenchStatus.Done: return "ui.bench.done_short";
                default: return "ui.bench.idle";
            }
        }

        /// <summary>把手上的小零件放到這座組裝台的左側（左側的格子和右側按 E 都會到這裡）。</summary>
        /// <summary>把手上的小零件放到左側：<paramref name="slot"/> 指定第幾格（-1 = 依序放進空格）。</summary>
        public static void Put(int bench, int slot = -1)
        {
            if (!GameSession.HasMission) return;
            string why = SessionRules.CanPut(M.S, M.Data, GameSession.LocalPlayer, bench);
            if (why != SessionRules.Ok) { Sfx.NoGo(); Hud.Toast(Loc.T(why)); return; }
            GameSession.Send(CommandType.BenchPut, slot >= 0 ? slot.ToString() : "", bench);
            Sfx.Drop();
            Hud.Signal("bench_put");
        }

        public override HudTarget Describe(Interactor who)
        {
            var t = new HudTarget { Title = Loc.T("ui.fac.bench") + " " + (bench + 1) };
            if (!GameSession.HasMission) return t;
            var s = M.S;
            var data = M.Data;
            if (s.IsSandbox(data)) { t.Body = Loc.T("ui.bench.sandbox"); return t; }
            if (s.Phase != SessionPhase.Design && s.Phase != SessionPhase.Briefing) { t.Subtitle = Loc.T("ui.hint.not_now"); return t; }

            var b = s.BenchAt(bench);
            var status = SessionRules.BenchStatusOf(s, data, bench);
            var job = b?.Job;
            if (job != null && job.Done)
            {
                var part = data.Part(job.PartId);
                t.Body = Loc.T("ui.bench.done", ("code", part?.Code ?? ""));
                if (who.Holding == null && who.Comps.Count == 0) t.Keys.Add(new KeyHint("C", Loc.T("ui.hint.take_part")));
                else t.Subtitle = Loc.T("ui.hint.hands_full");
                return t;
            }
            if (job != null)
            {
                var part = data.Part(job.PartId);
                var recipe = SessionRules.BenchRecipe(s, data, bench);
                var want = SessionRules.BenchWants(s, data, bench);
                if (recipe == null || want == null) return t;
                t.Title += "｜" + (part?.Code ?? "");
                t.Color = part?.Color ?? "";
                t.Subtitle = Loc.T("ui.bench.step", ("n", job.Step + 1), ("total", recipe.Components.Count));
                t.Body = Loc.T(want.Role);
                if (who.Comps.Count > 0) t.Keys.Add(new KeyHint("V", Loc.T("ui.hint.comp_fit")));
                else t.Body += "\n→ " + Loc.T("ui.bench.fetch");
                return t;
            }
            // 還沒認出配方：說明左側現在的狀態；手上有小零件就可以直接按 E 放到左側
            t.Body = Loc.T(StatusKey(status));
            if (who.Comps.Count > 0)
            {
                string why = SessionRules.CanPut(s, data, GameSession.LocalPlayer, bench);
                if (why == SessionRules.Ok) t.Keys.Add(new KeyHint("E", Loc.T("ui.hint.bench_put")));
                else t.Subtitle = Loc.T(why);
            }
            return t;
        }

        public override void OnKey(Interactor who, char key)
        {
            if (!GameSession.HasMission) return;
            var s = M.S;
            var job = s.BenchAt(bench)?.Job;
            if (key == 'E')
            {
                if (who.Comps.Count == 0 || job != null) return;
                Put(bench);
            }
            else if (key == 'C' && job != null && job.Done)
            {
                if (who.Holding != null || who.Comps.Count > 0) { Sfx.NoGo(); Hud.Toast(Loc.T("err.hands_full")); return; }
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
