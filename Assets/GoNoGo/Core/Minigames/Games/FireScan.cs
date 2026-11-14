using System;
using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// 火點資料優先：資料卡沿三條輸送帶由右往左送向左邊的下傳站。
    /// 紅色火焰卡是火點資料，點它就加速送進站內（得分）；灰色的雲、影像、雜訊（嚴重度 3 還有夕陽）不要點，點了算失誤。
    /// 火焰卡沒點就到站，算漏掉。輸送帶會慢慢變快，灰卡也越來越多。全程只用滑鼠，判定範圍比卡片大一圈。
    /// 過關：送出至少 70% 的火焰卡，而且失誤少於 3 + 護盾加成。多人：每個人各送一段，算總分（平均 ≥ 70%）。
    /// 設計影響：電力餘裕（Margin）越高，輸送帶越慢；護盾加成（Hull）多容許幾次失誤；指向穩定度（Steady）加大判定範圍。
    /// </summary>
    [MiniGame("fire_scan", EventKind.BigFire, Rule = MiniRule.Sum, PassScore = 700)]
    public sealed class FireScan : MiniGame
    {
        // 卡片種類：0 火焰、1 雲、2 影像、3 雜訊、4 夕陽
        sealed class Card
        {
            public int Kind, Lane;
            public float S0, X, Y, T, Phase;
            public int State; // 0 在帶上、1 送進站、2 點錯、3 漏掉、4 普通灰卡到站
        }

        struct Spawn { public float At; public int Kind, Lane; }
        sealed class Fx { public float X, Y, Life; public int Kind; } // 1 送出環、2 叉叉、3 點擊漣漪

        const float DishX = 88, DishY = 235, EndX = 168, StartX = 850, G = 0.012f, Gap = 112;
        static readonly float[] LaneY = { 130, 235, 340 };

        readonly List<Card> _cards = new List<Card>();
        readonly List<Spawn> _spawns = new List<Spawn>();
        readonly List<Fx> _fx = new List<Fx>();
        int _next, _flames, _sent, _strikes, _limit, _missed;
        float _v0, _half, _lastStrike = -99, _lastMiss = -99, _dishGood, _dishBad;
        float _cx = MiniField.W / 2, _cy = MiniField.H / 2;
        float _apCd;

        public override int Score => _flames == 0 ? 0 : (int)(1000.0 * _sent / _flames);
        public override float MaxSeconds => 70;

        float Belt(float t) => _v0 * (t + G * t * t / 2);

        protected override void OnStart()
        {
            int sev = Ctx.Severity;
            _flames = 10 + 3 * sev;
            _limit = 3 + Ctx.Hull;
            _v0 = 100 + 12 * sev - 20 * (float)Ctx.Margin;
            _half = 38 + 16 + 10 * (float)Ctx.Steady;

            var raw = new List<Spawn>();
            float step = 29f / (_flames - 1);
            for (int i = 0; i < _flames; i++)
                raw.Add(new Spawn { At = 1f + i * step + (i == 0 ? 0 : Rand(-0.3f, 0.3f)), Kind = 0 });
            int decoys = (int)(_flames * 1.1f) + 3 * sev;
            for (int i = 0; i < decoys; i++)
            {
                int k = 1 + (int)(Rand() * 3) % 3;
                if (sev >= 3 && Rand() < 0.3f) k = 4;
                raw.Add(new Spawn { At = Rand(1f, 30f), Kind = k });
            }
            raw.Sort((a, b) => a.At.CompareTo(b.At));
            var last = new float[3] { -1000, -1000, -1000 };
            foreach (var sp0 in raw)
            {
                var sp = sp0;
                int lane = -1;
                for (int guard = 0; guard < 400 && lane < 0; guard++)
                {
                    int start = (int)(Rand() * 3) % 3;
                    for (int j = 0; j < 3; j++)
                    {
                        int l = (start + j) % 3;
                        if (Belt(sp.At) - last[l] >= Gap) { lane = l; break; }
                    }
                    if (lane < 0) sp.At += 0.1f;
                }
                if (lane < 0) lane = 0;
                last[lane] = Belt(sp.At);
                sp.Lane = lane;
                _spawns.Add(sp);
            }
            _spawns.Sort((a, b) => a.At.CompareTo(b.At));
        }

        protected override void Update(float dt, MiniInput input)
        {
            _cx = Clamp(input.PX, 0, MiniField.W); _cy = Clamp(input.PY, 0, MiniField.H);
            if (input.Pressed) Click(_cx, _cy);
            _dishGood = Math.Max(0, _dishGood - dt);
            _dishBad = Math.Max(0, _dishBad - dt);

            while (_next < _spawns.Count && _spawns[_next].At <= Time)
            {
                var sp = _spawns[_next++];
                _cards.Add(new Card { Kind = sp.Kind, Lane = sp.Lane, S0 = Belt(Time), X = StartX, Y = LaneY[sp.Lane], Phase = Rand(0, 6.28f) });
            }

            float b = Belt(Time);
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var c = _cards[i];
                if (c.State == 0)
                {
                    c.X = StartX - (b - c.S0);
                    if (c.X <= EndX)
                    {
                        if (c.Kind == 0) { c.State = 3; _missed++; _lastMiss = Time; _dishBad = 0.5f; }
                        else c.State = 4;
                        c.T = 0;
                    }
                }
                else
                {
                    c.T += dt;
                    float life = c.State == 1 ? 0.35f : 0.45f;
                    if (c.T >= life)
                    {
                        if (c.State == 1) _dishGood = 0.45f;
                        _cards.RemoveAt(i);
                    }
                }
            }
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                _fx[i].Life -= dt;
                if (_fx[i].Life <= 0) _fx.RemoveAt(i);
            }

            if (_strikes >= _limit) { Lose(); return; }
            if (_next >= _spawns.Count && _cards.Count == 0)
            {
                if (Score >= Def.PassScore) Win(); else Lose();
            }
        }

        void Click(float x, float y)
        {
            _fx.Add(new Fx { X = x, Y = y, Life = 0.25f, Kind = 3 });
            Card best = null; float bd = float.MaxValue;
            foreach (var c in _cards)
            {
                if (c.State != 0 || c.X > MiniField.W + 20) continue;
                float dx = Math.Abs(c.X - x), dy = Math.Abs(c.Y - y);
                if (dx > _half || dy > _half) continue;
                float d = dx * dx + dy * dy;
                // 範圍重疊時，火焰卡優先
                if (best == null || (c.Kind == 0 && best.Kind != 0) || ((c.Kind == 0) == (best.Kind == 0) && d < bd)) { best = c; bd = d; }
            }
            if (best == null) return;
            if (best.Kind == 0)
            {
                best.State = 1; best.T = 0; _sent++;
                _fx.Add(new Fx { X = best.X, Y = best.Y, Life = 0.4f, Kind = 1 });
            }
            else
            {
                best.State = 2; best.T = 0; _strikes++; _lastStrike = Time;
                _fx.Add(new Fx { X = best.X, Y = best.Y, Life = 0.6f, Kind = 2 });
            }
        }

        public override MiniCaption Caption
        {
            get
            {
                if (_limit - _strikes == 1 && _strikes > 0) return new MiniCaption("mini.fire_scan.cap.last", 3);
                if (Time - _lastStrike < 2f) return new MiniCaption("mini.fire_scan.cap.noise", 2);
                if (Time - _lastMiss < 2f) return new MiniCaption("mini.fire_scan.cap.missed", 2);
                return new MiniCaption("mini.fire_scan.cap.main", 0);
            }
        }

        static string Sprite(int kind) =>
            kind == 0 ? "fire_scan/card_flame" : kind == 1 ? "fire_scan/card_cloud" : kind == 2 ? "fire_scan/card_image" : kind == 3 ? "fire_scan/card_noise" : "fire_scan/card_sunset";

        public override void Draw(List<MiniShape> s)
        {
            Spr(s, "fire_scan/bg", MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H);
            float off = Belt(Time) % 100f;
            for (int l = 0; l < 3; l++)
            {
                Spr(s, "fire_scan/belt", 400 - off + 80, LaneY[l], 800, 100);
                Spr(s, "fire_scan/belt", 1200 - off + 80, LaneY[l], 800, 100);
            }
            // 下傳站
            float pulse = 1 + 0.12f * (_dishGood / 0.45f);
            if (_dishGood > 0) Circle(s, DishX, DishY, 80, MiniColor.Green, 0.35f * (_dishGood / 0.45f));
            if (_dishBad > 0) Circle(s, DishX, DishY, 80, MiniColor.Red, 0.45f * (_dishBad / 0.5f));
            Spr(s, "fire_scan/dish", DishX, DishY, 150 * pulse, 150 * pulse, 0, 1, _dishBad > 0 ? 0xFF9090u : 0xFFFFFFu);
            Text(s, DishX, DishY - 92, 14, "@mini.fire_scan.lbl.station", MiniColor.Cyan);

            foreach (var c in _cards)
            {
                float x = c.X, y = c.Y, sz = 76, a = 1;
                uint tint = 0xFFFFFF;
                if (c.State == 1)
                {
                    float p = c.T / 0.35f; p *= p;
                    x = c.X + (DishX - c.X) * p; y = c.Y + (DishY - c.Y) * p; sz = 76 * (1 - 0.6f * p);
                }
                else if (c.State == 2) { tint = 0xFF8080; a = 1 - c.T / 0.6f; x += (float)Math.Sin(c.T * 60) * 3; }
                else if (c.State == 3) { tint = 0xFF6060; a = 1 - c.T / 0.45f; }
                else if (c.State == 4) { a = 1 - c.T / 0.45f; }
                else if (c.Kind == 0) sz = 76 + 3 * (float)Math.Sin(Time * 9 + c.Phase);
                if (c.State == 0 && c.Kind == 0) Circle(s, x, y, 46, MiniColor.Red, 0.18f);
                Spr(s, Sprite(c.Kind), x, y, sz, sz, 0, Math.Max(0, a), tint);
            }

            foreach (var f in _fx)
            {
                if (f.Kind == 1) { float d = 70 + 90 * (1 - f.Life / 0.4f); Spr(s, "fire_scan/boost_ring", f.X, f.Y, d, d, 0, f.Life / 0.4f); }
                else if (f.Kind == 2) Spr(s, "fire_scan/strike_x", f.X, f.Y, 60, 60, 0, Math.Min(1, f.Life * 3));
                else Ring(s, f.X, f.Y, 10 + 40 * (1 - f.Life / 0.25f), 2, MiniColor.Cyan, f.Life / 0.25f * 0.8f);
            }

            Spr(s, "fire_scan/cursor", _cx, _cy, 44, 44, 0, 0.95f, MiniColor.Cyan);
        }

        public override IEnumerable<MiniStat> Stats
        {
            get
            {
                yield return new MiniStat("mini.fire_scan.stat.sent", _sent + " / " + _flames);
                yield return new MiniStat("mini.fire_scan.stat.strikes", _strikes + " / " + _limit);
            }
        }

        // ---------------------------------------------------------------- 自動駕駛
        public override MiniInput Autopilot(float skill)
        {
            var inp = new MiniInput { PX = _cx, PY = _cy };
            _apCd -= FixedDt;
            if (_apCd > 0) return inp;
            float react = 0.1f + (1 - skill) * 0.9f;
            Card flame = null, grey = null;
            foreach (var c in _cards)
            {
                if (c.State != 0 || c.X > MiniField.W - 30) continue;
                if (c.Kind == 0)
                {
                    float seen = (StartX - c.X) / Math.Max(1f, _v0);
                    if (seen >= react && (flame == null || c.X < flame.X)) flame = c;
                }
                else if (grey == null || c.X < grey.X) grey = c;
            }
            _apCd = 0.15f + (1 - skill) * 0.3f;
            if (flame == null) { _apCd = 0.03f; return inp; }
            if (grey != null && Rand() < (1 - skill) * 0.5f)
            {
                float e = (1 - skill) * 14;
                inp.PX = grey.X + Rand(-e, e); inp.PY = grey.Y + Rand(-e, e); inp.Pressed = true;
                return inp;
            }
            if (Rand() < (1 - skill) * 0.3f) return inp; // 分心，這次沒點
            float err = (1 - skill) * 18;
            inp.PX = flame.X + Rand(-err, err); inp.PY = flame.Y + Rand(-err, err); inp.Pressed = true;
            return inp;
        }
    }
}
