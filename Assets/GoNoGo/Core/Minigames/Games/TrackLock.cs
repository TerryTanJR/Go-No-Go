using System;
using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// 科學機會：追蹤目標。衛星下方的地面往下捲，地面上有一個移動的觀測目標（野火／火山／洪水／颶風，形狀依 RefId 不同）。
    /// 滑鼠移動準星；把準星保持在目標的「鎖定圈」內累積鎖定時間，圈外進度會慢慢流失。
    /// 目標會變速、轉向，偶爾被雲遮住（雲層期間鎖定暫停，也不計入總時間）。固定 20 秒，鎖定比例 ≥ 60% 才算拍到。
    /// 多人：每個人各自追同一顆目標的軌跡，算總分（所有人鎖定比例的平均 ≥ 60%），反映全隊的總拍攝時間。
    /// 設計影響：Steady（姿態控制精度）越高，鎖定圈越大、準星抖動越小；Severity 越高目標越快、轉向越花俏。
    /// </summary>
    [MiniGame("track_lock", EventKind.ScienceOpportunity, Rule = MiniRule.Sum, PassScore = 600)]
    public sealed class TrackLock : MiniGame
    {
        const float Duration = 20f, Follow = 14f, Drain = 0.05f;

        struct Cloud { public float Start, End; }

        readonly List<Cloud> _clouds = new List<Cloud>();
        float _tx, _ty, _heading, _turn, _turnT, _phase, _speed, _vx, _vy;
        float _mx = MiniField.W / 2, _my = MiniField.H / 2, _rx = MiniField.W / 2, _ry = MiniField.H / 2;
        float _lock, _clear, _radius, _cloudAmt;
        bool _inside, _cloudy;

        public override int Score => _clear <= 0.001f ? 0 : (int)Math.Max(0, Math.Min(1000, 1000.0 * _lock / _clear));
        public override float MaxSeconds => 30;

        protected override void OnStart()
        {
            int sev = Ctx.Severity;
            _radius = 42 + 26 * (float)Ctx.Steady - 3 * (sev - 1);
            _tx = MiniField.W / 2 + Rand(-60, 60);
            _ty = MiniField.H / 2 + Rand(-40, 40);
            _heading = Rand(0, 6.2832f);
            _phase = Rand(0, 6.2832f);
            _turnT = 1;
            // 雲層：兩到三團，錯開
            int n = 2 + (sev >= 3 ? 1 : 0);
            float t = Rand(4, 6);
            for (int i = 0; i < n; i++)
            {
                float len = Rand(1.3f, 2.2f);
                _clouds.Add(new Cloud { Start = t, End = t + len });
                t += len + Rand(4.5f, 6.5f);
            }
        }

        float Jitter(float t, int axis)
        {
            float amp = 2 + 12 * (1 - (float)Ctx.Steady);
            return axis == 0
                ? amp * (0.6f * (float)Math.Sin(t * 7.3 + 1) + 0.4f * (float)Math.Sin(t * 13.1))
                : amp * (0.6f * (float)Math.Sin(t * 6.1 + 2) + 0.4f * (float)Math.Sin(t * 11.7 + 0.5));
        }

        float AimX => _rx + Jitter(Time, 0);
        float AimY => _ry + Jitter(Time, 1);

        protected override void Update(float dt, MiniInput input)
        {
            int sev = Ctx.Severity;

            // 準星：只用滑鼠，直接跟著指標（平滑一點點），不用點擊
            _mx = Clamp(input.PX, 0, MiniField.W);
            _my = Clamp(input.PY, 0, MiniField.H);
            float k = 1 - (float)Math.Exp(-Follow * dt);
            _rx += (_mx - _rx) * k;
            _ry += (_my - _ry) * k;

            // 目標：變速、轉向，靠近邊緣時繞回來
            _phase += (0.9f + 0.35f * sev) * dt;
            _speed = (80 + 30 * sev) * (1 + 0.4f * (float)Math.Sin(_phase));
            _turnT -= dt;
            if (_turnT <= 0)
            {
                _turn = Rand(-1, 1) * (0.7f + 0.5f * sev);
                _turnT = Rand(0.8f, 2f);
                if (Rand() < 0.25f * (sev - 1)) _heading += (Rand() < 0.5f ? -1 : 1) * 1.0f;
            }
            const float mx = 90, my = 70;
            if (_tx < mx || _tx > MiniField.W - mx || _ty < my || _ty > MiniField.H - my)
            {
                float want = (float)Math.Atan2(MiniField.H / 2 - _ty, MiniField.W / 2 - _tx);
                float d = want - _heading;
                while (d > Math.PI) d -= 6.2832f;
                while (d < -Math.PI) d += 6.2832f;
                _heading += Clamp(d, -3 * dt, 3 * dt);
            }
            else _heading += _turn * dt;
            _vx = (float)Math.Cos(_heading) * _speed;
            _vy = (float)Math.Sin(_heading) * _speed;
            _tx = Clamp(_tx + _vx * dt, 30, MiniField.W - 30);
            _ty = Clamp(_ty + _vy * dt, 30, MiniField.H - 30);

            // 雲
            _cloudy = false;
            foreach (var c in _clouds) if (Time >= c.Start && Time < c.End) _cloudy = true;
            _cloudAmt = Clamp(_cloudAmt + (_cloudy ? 1 : -1) * 4 * dt, 0, 1);

            // 鎖定
            float dx = AimX - _tx, dy = AimY - _ty;
            _inside = dx * dx + dy * dy <= _radius * _radius;
            if (!_cloudy)
            {
                _clear += dt;
                if (_inside) _lock += dt;
                else if (dx * dx + dy * dy > _radius * _radius * 3.2f) _lock = Math.Max(0, _lock - Drain * dt); // 外圈（約 1.8 倍半徑）以內不扣
            }

            if (Time >= Duration)
            {
                if (Score >= Def.PassScore) Win(); else Lose();
            }
        }

        static float Hash(int i) { float v = (float)Math.Sin(i * 12.9898 + 4.1) * 43758.5453f; return v - (float)Math.Floor(v); }

        public override void Draw(List<MiniShape> s)
        {
            Rect(s, MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H, MiniColor.Bg);
            // 地面：上下可接合的貼圖，往下捲
            float scroll = Time * 50;
            float gy = scroll % MiniField.H;
            Spr(s, "track_lock/ground", MiniField.W / 2, MiniField.H / 2 + gy, MiniField.W, MiniField.H);
            Spr(s, "track_lock/ground", MiniField.W / 2, MiniField.H / 2 + gy - MiniField.H, MiniField.W, MiniField.H);
            for (int i = 0; i < 12; i++) Line(s, i * 70, 0, i * 70, MiniField.H, 1, MiniColor.Grid, 0.5f);
            for (int i = 0; i < 9; i++)
            {
                float y = (i * 56 + scroll) % MiniField.H;
                Line(s, 0, y, MiniField.W, y, 1, MiniColor.Grid, 0.5f);
            }

            DrawTarget(s);

            // 鎖定圈
            uint rc = _cloudy ? MiniColor.Dim : _inside ? MiniColor.Green : MiniColor.Amber;
            Ring(s, _tx, _ty, _radius, 2.5f, rc, 0.9f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 0.7854f;
                Line(s, _tx + (float)Math.Cos(a) * _radius, _ty + (float)Math.Sin(a) * _radius,
                    _tx + (float)Math.Cos(a) * (_radius + 6), _ty + (float)Math.Sin(a) * (_radius + 6), 2, rc, 0.8f);
            }

            // 雲：蓋在目標上
            if (_cloudAmt > 0)
                for (int i = 0; i < 6; i++)
                {
                    float ox = (Hash(i + 200) - 0.5f) * 120 + (float)Math.Sin(Time * 0.8 + i) * 8;
                    float oy = (Hash(i + 230) - 0.5f) * 56;
                    float w = 110 + 50 * Hash(i + 260);
                    Spr(s, "track_lock/cloud", _tx + ox, _ty + oy, w, w / 2, 0, 0.92f * _cloudAmt);
                }

            // 準星（含姿態抖動）
            float ax = AimX, ay = AimY;
            uint pc = _inside && !_cloudy ? MiniColor.Green : MiniColor.Cyan;
            Ring(s, ax, ay, 11, 2, pc);
            Line(s, ax - 20, ay, ax - 6, ay, 2, pc);
            Line(s, ax + 6, ay, ax + 20, ay, 2, pc);
            Line(s, ax, ay - 20, ax, ay - 6, 2, pc);
            Line(s, ax, ay + 6, ax, ay + 20, 2, pc);
            if (_inside && !_cloudy) Circle(s, ax, ay, 3, MiniColor.Green);

            // 開頭提示：脈動的圈
            if (Time < 5)
            {
                float pu = (Time * 1.2f) % 1f;
                Ring(s, _tx, _ty, _radius + 8 + 26 * pu, 3, MiniColor.Cyan, (1 - pu) * Clamp(5 - Time, 0, 1));
            }

            // 說明文字（字串鍵，由 UI 翻譯）
            Rect(s, MiniField.W / 2, 24, 520, 30, MiniColor.Bg, 0.7f);
            string cap = _cloudy ? "@mini.track_lock.cap.cloud" : _inside ? "@mini.track_lock.cap.locking" : "@mini.track_lock.cap.aim";
            Text(s, MiniField.W / 2, 24, 18, cap, _cloudy ? MiniColor.Dim : _inside ? MiniColor.Green : MiniColor.Amber);
            Text(s, MiniField.W / 2, MiniField.H - 33, 13, "@mini.track_lock.cap.progress", MiniColor.Text);

            // 進度條
            float frac = Clamp(Score / 1000f, 0, 1);
            Rect(s, MiniField.W / 2, MiniField.H - 12, 400, 8, MiniColor.Panel, 0.9f);
            Rect(s, MiniField.W / 2 - 200 + 200 * frac, MiniField.H - 12, 400 * frac, 8, frac * 1000 >= Def.PassScore ? MiniColor.Green : MiniColor.Cyan);
            Rect(s, MiniField.W / 2 - 200 + 400 * Def.PassScore / 1000f, MiniField.H - 12, 2, 14, MiniColor.Amber);
            Rect(s, MiniField.W / 2 - 200 + 400 * Clamp(Time / Duration, 0, 1), MiniField.H - 24, 2, 5, MiniColor.Dim);
        }

        void DrawTarget(List<MiniShape> s)
        {
            float x = _tx, y = _ty, t = Time;
            switch (Ctx.RefId)
            {
                case "wildfire":
                {
                    float fl = 1 + 0.08f * (float)Math.Sin(t * 9);
                    Spr(s, "track_lock/target_fire", x, y - 4, 64 * fl, 64 * fl);
                    break;
                }
                case "volcano":
                {
                    float fl = 1 + 0.05f * (float)Math.Sin(t * 6);
                    Spr(s, "track_lock/target_volcano", x, y - 4, 66, 66 * fl);
                    break;
                }
                case "flood":
                {
                    float fl = 1 + 0.04f * (float)Math.Sin(t * 3);
                    Spr(s, "track_lock/target_flood", x, y, 68 * fl, 68 * fl);
                    break;
                }
                case "hurricane":
                    Spr(s, "track_lock/target_hurricane", x, y, 70, 70, t * 2.2f);
                    break;
                default:
                    Ring(s, x, y, 12, 3, MiniColor.Amber);
                    Circle(s, x, y, 5, MiniColor.Red);
                    break;
            }
        }

        public override IEnumerable<MiniStat> Stats
        {
            get
            {
                yield return new MiniStat("mini.track_lock.stat.lock", Score / 10 + "%  ( ≥ " + Def.PassScore / 10 + "% )");
                yield return new MiniStat("mini.track_lock.stat.time", Math.Max(0, (int)Math.Ceiling(Duration - Time)) + " s");
            }
        }

        // ---- 自動駕駛：用滑鼠座標追目標；技術差的人常常跟丟、而且瞄不準
        Random _apRng;
        float _wanderUntil, _wanderX = MiniField.W / 2, _wanderY = MiniField.H / 2;
        bool _wandering;

        public override MiniInput Autopilot(float skill)
        {
            if (_apRng == null) _apRng = new Random(4242);
            if (Time >= _wanderUntil)
            {
                _wanderUntil = Time + 0.4f + (float)_apRng.NextDouble() * 0.5f;
                _wanderX = 40 + (float)_apRng.NextDouble() * (MiniField.W - 80);
                _wanderY = 40 + (float)_apRng.NextDouble() * (MiniField.H - 80);
                _wandering = _apRng.NextDouble() < (1 - skill) * 0.85;
            }
            var inp = new MiniInput();
            if (_wandering) { inp.PX = _wanderX; inp.PY = _wanderY; return inp; }
            float err = (1 - skill) * 40;
            inp.PX = Clamp(_tx + _vx / Follow + err * (float)Math.Sin(Time * 2.3), 0, MiniField.W);
            inp.PY = Clamp(_ty + _vy / Follow + err * (float)Math.Cos(Time * 1.9), 0, MiniField.H);
            return inp;
        }
    }
}
