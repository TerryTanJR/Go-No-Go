using System;
using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// 太陽風暴：駕駛衛星閃躲高能質子。WASD / 方向鍵移動。
    /// 風暴強度會一路升高（強時粒子也從側邊斜著打來），撐過整場風暴就通關。
    /// 場上會掉護盾能量球：撿到會充一次護盾（擋一次傷害），同時衛星進入「安全模式」一小段時間——
    /// 儀器關掉、身體縮小比較難被打中，代價是推進力變弱、動作比較慢。
    /// 多人：每個人各自閃，通關人數達一半才算全隊過（Ratio）。
    /// 設計影響：護盾越好（Hull）血量越多；電力餘裕（Margin）越高，安全模式撐得越久。嚴重度越高，粒子越密越快。
    /// </summary>
    [MiniGame("solar_dodge", EventKind.SolarStorm, Rule = MiniRule.Ratio, PassScore = 1000, RatioNeeded = 0.5)]
    public sealed class SolarDodge : MiniGame
    {
        sealed class Proton { public float X, Y, Vx, Vy; }
        sealed class Orb { public float X, Y; }
        sealed class Spark { public float X, Y, Vx, Vy, Life; public uint Color; }

        const float StormSeconds = 26f, Speed = 300f, SafeSpeedFactor = 0.75f;
        const float ShipR = 11f, SafeShipR = 6f, ProtonR = 6f, OrbR = 12f, InvulnTime = 1.2f;

        readonly List<Proton> _protons = new List<Proton>();
        readonly List<Orb> _orbs = new List<Orb>();
        readonly List<Spark> _sparks = new List<Spark>();
        int _lives, _maxLives;
        float _px = MiniField.W / 2, _py = MiniField.H - 70;
        float _invuln, _safe, _safeMax, _spawnAcc, _orbCd, _flash;
        bool _shield;
        float _wanderUntil, _wanderX;
        bool _wandering;

        public override int Score => State == MiniState.Won ? 1000 : (int)Math.Min(900, 900f * Time / StormSeconds);
        public override float MaxSeconds => StormSeconds + 5;

        /// <summary>風暴強度 0 ~ 1：隨時間升高，再加一點起伏（一陣一陣）。</summary>
        float Intensity => Clamp(0.3f + 0.7f * Time / StormSeconds + 0.12f * (float)Math.Sin(Time * 1.3f), 0.15f, 1f);

        protected override void OnStart()
        {
            _maxLives = _lives = 3 + Ctx.Hull;
            _safeMax = 3f + 2f * (float)Ctx.Margin;
            _orbCd = Rand(3.5f, 5f);
        }

        float ShipRadius => _safe > 0 ? SafeShipR : ShipR;

        protected override void Update(float dt, MiniInput input)
        {
            int sev = Ctx.Severity;
            float inten = Intensity;

            // 玩家：只用鍵盤（WASD / 方向鍵）
            float sp = Speed * (_safe > 0 ? SafeSpeedFactor : 1f);
            float mx = input.MoveX, my = input.MoveY;
            _px = Clamp(_px + mx * sp * dt, 16, MiniField.W - 16);
            _py = Clamp(_py + my * sp * dt, MiniField.H * 0.4f, MiniField.H - 22);
            _invuln = Math.Max(0, _invuln - dt);
            _safe = Math.Max(0, _safe - dt);
            _flash = Math.Max(0, _flash - dt);

            // 生成質子
            float rate = (4.5f + 1.5f * sev) * inten;
            _spawnAcc += rate * dt;
            while (_spawnAcc >= 1f)
            {
                _spawnAcc -= 1f;
                float fall = 150f + 25f * sev + 110f * inten;
                bool side = inten > 0.6f && Rand() < 0.28f;
                var p = new Proton();
                if (side)
                {
                    bool left = Rand() < 0.5f;
                    p.X = left ? -8 : MiniField.W + 8;
                    p.Y = Rand(10, MiniField.H * 0.5f);
                    p.Vx = (left ? 1 : -1) * Rand(90, 150) * (0.8f + 0.1f * sev);
                    p.Vy = fall * Rand(0.6f, 0.9f);
                }
                else
                {
                    bool aimed = Rand() < 0.3f;
                    p.X = aimed ? Clamp(_px + Rand(-45, 45), 10, MiniField.W - 10) : Rand(10, MiniField.W - 10);
                    p.Y = -10;
                    p.Vx = Rand(-25, 25);
                    p.Vy = fall * Rand(0.85f, 1.15f);
                }
                _protons.Add(p);
            }

            // 護盾球
            _orbCd -= dt;
            if (_orbCd <= 0)
            {
                _orbCd = Rand(5f, 8f);
                _orbs.Add(new Orb { X = Rand(40, MiniField.W - 40), Y = -12 });
            }

            // 移動與碰撞
            float r = ShipRadius;
            for (int i = _protons.Count - 1; i >= 0; i--)
            {
                var p = _protons[i];
                p.X += p.Vx * dt; p.Y += p.Vy * dt;
                bool dead = p.Y > MiniField.H + 12 || p.X < -20 || p.X > MiniField.W + 20;
                if (!dead && _invuln <= 0)
                {
                    float dx = p.X - _px, dy = p.Y - _py;
                    if (dx * dx + dy * dy < (r + ProtonR) * (r + ProtonR)) { Hit(); dead = true; }
                }
                if (dead) _protons.RemoveAt(i);
            }
            for (int i = _orbs.Count - 1; i >= 0; i--)
            {
                var o = _orbs[i];
                o.Y += 70f * dt;
                float dx = o.X - _px, dy = o.Y - _py;
                bool dead = o.Y > MiniField.H + 20;
                if (!dead && dx * dx + dy * dy < (OrbR + ShipR) * (OrbR + ShipR))
                {
                    _shield = true;
                    _safe = _safeMax;
                    Burst(o.X, o.Y, 10, MiniColor.Green);
                    dead = true;
                }
                if (dead) _orbs.RemoveAt(i);
            }
            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var s = _sparks[i];
                s.Life -= dt; s.X += s.Vx * dt; s.Y += s.Vy * dt;
                if (s.Life <= 0) _sparks.RemoveAt(i);
            }

            if (_lives <= 0) { Lose(); return; }
            if (Time >= StormSeconds) Win();
        }

        void Hit()
        {
            if (_shield)
            {
                _shield = false;
                _invuln = 0.5f;
                Burst(_px, _py, 12, MiniColor.Cyan);
                return;
            }
            _lives--;
            CueBad();
            _invuln = InvulnTime;
            _flash = 0.35f;
            Burst(_px, _py, 16, MiniColor.Red);
        }

        void Burst(float x, float y, int n, uint color)
        {
            for (int i = 0; i < n; i++)
            {
                float a = Rand(0, 6.2832f), v = Rand(40, 180);
                _sparks.Add(new Spark { X = x, Y = y, Vx = (float)Math.Cos(a) * v, Vy = (float)Math.Sin(a) * v, Life = Rand(0.25f, 0.6f), Color = color });
            }
        }

        public override void Draw(List<MiniShape> s)
        {
            float inten = Intensity;
            Spr(s, "solar_dodge/bg", MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H);
            // 風暴的紅光：越強越紅
            Rect(s, MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H, MiniColor.Red, 0.04f + 0.1f * inten);
            for (int i = 0; i < 40; i++)
            {
                float x = (i * 137.5f) % MiniField.W;
                float y = (i * 61.3f + Time * (25 + (i % 3) * 20)) % MiniField.H;
                Circle(s, x, y, 1 + (i % 3) * 0.5f, MiniColor.Dim, 0.5f);
            }
            // 太陽風暴進度條（畫面最上方）
            float frac = Clamp(Time / StormSeconds, 0, 1);
            Rect(s, MiniField.W / 2, 8, MiniField.W - 40, 6, MiniColor.Grid);
            Rect(s, 20 + (MiniField.W - 40) * frac / 2, 8, (MiniField.W - 40) * frac, 6, MiniColor.Amber);
            Text(s, 24, 22, 10, "SUN", MiniColor.Amber, 0.8f);

            foreach (var sp in _sparks) Circle(s, sp.X, sp.Y, 2.2f, sp.Color, Math.Max(0, sp.Life / 0.6f));
            foreach (var o in _orbs)
                Spr(s, "solar_dodge/orb", o.X, o.Y, OrbR * 2.4f, OrbR * 2.4f, 0, 1);
            foreach (var p in _protons)
            {
                // 圖尖端朝上（亮點在上、拖尾在下）：轉到飛行方向，並讓亮點對準判定中心
                float len = (float)Math.Sqrt(p.Vx * p.Vx + p.Vy * p.Vy);
                float hx = p.Vx / len, hy = p.Vy / len;
                float rot = (float)Math.Atan2(p.Vx, -p.Vy);
                Spr(s, "solar_dodge/proton", p.X - hx * 6.75f, p.Y - hy * 6.75f, 15, 30, rot);
            }
            if (_lives > 0 && !(_invuln > 0 && ((int)(Time * 14) % 2 == 0)))
            {
                if (_safe > 0) Spr(s, "solar_dodge/sat_safe", _px, _py, 26, 26, 0, 0.85f);
                else Spr(s, "solar_dodge/sat", _px, _py, 46, 46);
            }
            if (_shield && _lives > 0) Spr(s, "solar_dodge/shield", _px, _py, 58, 58);
            if (_safe > 0 && _lives > 0) Ring(s, _px, _py, 22 + 3 * (float)Math.Sin(Time * 8), 1.5f, MiniColor.Dim, 0.8f);
            if (_flash > 0) Rect(s, MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H, MiniColor.Red, _flash * 0.6f);
        }

        public override IEnumerable<MiniStat> Stats
        {
            get
            {
                yield return new MiniStat("mini.solar_dodge.stat.hull", _lives + " / " + _maxLives);
                yield return new MiniStat("mini.solar_dodge.stat.storm", (int)(100 * Clamp(Time / StormSeconds, 0, 1)) + "%");
                yield return new MiniStat("mini.solar_dodge.stat.shield", _shield ? "ON" : _safe > 0 ? "SAFE " + _safe.ToString("0.0") + "s" : "OFF");
            }
        }

        /// <summary>自動駕駛：只送 MoveX / MoveY（和真人按鍵一樣）。</summary>
        public override MiniInput Autopilot(float skill)
        {
            var inp = new MiniInput();
            inp.MoveY = Clamp((MiniField.H - 70 - _py) / 20f, -1, 1);
            if (Time >= _wanderUntil)
            {
                _wanderUntil = Time + Rand(0.4f, 0.9f);
                _wanderX = Rand(30, MiniField.W - 30);
                _wandering = Rand() > skill;
            }
            if (_wandering)
            {
                inp.MoveX = Clamp((_wanderX - _px) / 14f, -1, 1);
                return inp;
            }
            float sp = Speed * (_safe > 0 ? SafeSpeedFactor : 1f);
            float best = _px, bestCost = float.MaxValue;
            float lo = Math.Max(20, _px - 220), hi = Math.Min(MiniField.W - 20, _px + 220);
            for (float cx = lo; cx <= hi; cx += 6)
            {
                float cost = 0.002f * Math.Abs(cx - _px);
                foreach (var p in _protons)
                {
                    float t = (_py - p.Y) / p.Vy;
                    if (t < -0.15f || t > 1.1f) continue;
                    float ax = p.X + p.Vx * t;
                    float dist = cx - _px;
                    float sx = _px + Math.Sign(dist) * Math.Min(Math.Abs(dist), sp * Math.Max(0, t));
                    float gap = Math.Abs(ax - sx);
                    if (gap < 26) cost += 3f + (1.2f - t) + (26 - gap) * 0.05f;
                    else if (gap < 40) cost += 0.4f;
                }
                if (skill > 0.5f && !_shield)
                    foreach (var o in _orbs)
                    {
                        float t = (_py - o.Y) / 70f;
                        if (t < 0 || t > 3f) continue;
                        if (Math.Abs(o.X - cx) < 16) cost -= 1.2f;
                    }
                if (cost < bestCost) { bestCost = cost; best = cx; }
            }
            inp.MoveX = Clamp((best - _px) / 8f, -1, 1);
            return inp;
        }
    }
}
