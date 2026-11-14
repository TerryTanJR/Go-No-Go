using System;
using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// 外星人彩蛋：雷霆戰機式的射擊小遊戲。WASD / 方向鍵移動，自動連射，三波敵人，最後一波有母艦。
    /// 多人：每個人各駕一架，算總分（平均擊落比例達標才算過）。
    /// 設計影響：護盾越好，血量越多。
    /// </summary>
    [MiniGame("alien", EventKind.Alien, Rule = MiniRule.Sum, PassScore = 600)]
    public sealed class AlienShooter : MiniGame
    {
        sealed class Foe
        {
            public float X, Y, BaseX, T, FireCd;
            public int Hp, Kind; // 0 小飛碟、1 砲艇、2 母艦
            public float R;
            public int Weight;
            public float Dir = 1;
        }

        sealed class Shot { public float X, Y, Vx, Vy; }
        sealed class Spark { public float X, Y, Vx, Vy, Life, Max; public uint Color; }
        struct Spawn { public float At; public int Kind; public float X; }

        const float Speed = 330f, FireRate = 8f, PlayerShotSpeed = 620f;

        readonly List<Foe> _foes = new List<Foe>();
        readonly List<Shot> _mine = new List<Shot>(), _theirs = new List<Shot>();
        readonly List<Spark> _sparks = new List<Spark>();
        readonly List<Spawn> _spawns = new List<Spawn>();
        int _next, _lives, _maxLives, _totalWeight, _killedWeight;
        float _px = MiniField.W / 2, _py = MiniField.H - 60, _fireCd, _invuln;
        float _shotSpeed;
        bool _clearShots;

        public override int Score => _totalWeight == 0 ? 0 : (int)(1000.0 * _killedWeight / _totalWeight);
        public override float MaxSeconds => 90;

        protected override void OnStart()
        {
            int sev = Ctx.Severity;
            _maxLives = _lives = 3 + Ctx.Hull;
            _shotSpeed = 170 + 20 * sev;
            float t = 1f;
            // 第一波：小飛碟
            for (int i = 0; i < 5 + sev; i++) { _spawns.Add(new Spawn { At = t, Kind = 0, X = Rand(80, 720) }); t += 0.7f; }
            // 第二波：砲艇夾小飛碟
            t += 3f;
            for (int i = 0; i < 4 + sev; i++)
            {
                _spawns.Add(new Spawn { At = t, Kind = 1, X = Rand(100, 700) }); t += 0.9f;
                if (i % 2 == 0) { _spawns.Add(new Spawn { At = t, Kind = 0, X = Rand(80, 720) }); t += 0.5f; }
            }
            // 第三波：母艦（加幾隻護衛）
            t += 4f;
            _spawns.Add(new Spawn { At = t, Kind = 2, X = MiniField.W / 2 });
            for (int i = 0; i < 2 + sev; i++) { t += 1.6f; _spawns.Add(new Spawn { At = t, Kind = 0, X = Rand(80, 720) }); }
            foreach (var s in _spawns) _totalWeight += WeightOf(s.Kind);
        }

        static int WeightOf(int kind) => kind == 2 ? 10 : kind == 1 ? 2 : 1;

        protected override void Update(float dt, MiniInput input)
        {
            // 玩家
            _px = Clamp(_px + input.MoveX * Speed * dt, 20, MiniField.W - 20);
            _py = Clamp(_py + input.MoveY * Speed * dt, MiniField.H * 0.45f, MiniField.H - 24);
            _invuln = Math.Max(0, _invuln - dt);
            _fireCd -= dt;
            if (_fireCd <= 0)
            {
                _fireCd += 1f / FireRate;
                _mine.Add(new Shot { X = _px - 7, Y = _py - 14, Vy = -PlayerShotSpeed });
                _mine.Add(new Shot { X = _px + 7, Y = _py - 14, Vy = -PlayerShotSpeed });
            }

            // 生成敵人
            while (_next < _spawns.Count && _spawns[_next].At <= Time)
            {
                var s = _spawns[_next++];
                var f = new Foe { Kind = s.Kind, BaseX = s.X, X = s.X, Y = -20, T = Rand(0, 6), Weight = WeightOf(s.Kind) };
                f.Hp = s.Kind == 0 ? 1 : s.Kind == 1 ? 2 : 22 + 8 * Ctx.Severity;
                f.R = s.Kind == 2 ? 36 : s.Kind == 1 ? 17 : 13;
                f.FireCd = Rand(0.6f, 1.6f);
                _foes.Add(f);
            }

            // 敵人移動與射擊
            for (int i = _foes.Count - 1; i >= 0; i--)
            {
                var f = _foes[i];
                f.T += dt;
                switch (f.Kind)
                {
                    case 0:
                        f.Y += 95 * dt;
                        f.X = f.BaseX + (float)Math.Sin(f.T * 2.2) * 70;
                        break;
                    case 1:
                        f.Y += 50 * dt;
                        f.X = f.BaseX + (float)Math.Sin(f.T * 1.4) * 45;
                        break;
                    default:
                        if (f.Y < 80) f.Y += 60 * dt;
                        else
                        {
                            f.BaseX += f.Dir * 85 * dt;
                            if (f.BaseX > 640) f.Dir = -1; else if (f.BaseX < 160) f.Dir = 1;
                            f.X = f.BaseX;
                        }
                        break;
                }
                f.X = Clamp(f.X, 15, MiniField.W - 15);
                if (f.Kind > 0 && f.Y > 10)
                {
                    f.FireCd -= dt;
                    if (f.FireCd <= 0)
                    {
                        if (f.Kind == 1)
                        {
                            f.FireCd = 1.7f;
                            Aim(f, 0);
                        }
                        else
                        {
                            f.FireCd = Math.Max(0.9f, 1.5f - 0.15f * Ctx.Severity);
                            Aim(f, -0.35f); Aim(f, 0); Aim(f, 0.35f);
                        }
                    }
                }
                if (f.Y > MiniField.H + 30) _foes.RemoveAt(i); // 飛過去了：沒打到，不扣血
            }

            // 玩家子彈
            for (int i = _mine.Count - 1; i >= 0; i--)
            {
                var b = _mine[i];
                b.Y += b.Vy * dt;
                bool dead = b.Y < -10;
                if (!dead)
                    foreach (var f in _foes)
                    {
                        if (Math.Abs(b.X - f.X) < f.R && Math.Abs(b.Y - f.Y) < f.R)
                        {
                            f.Hp--;
                            dead = true;
                            Burst(b.X, b.Y, 2, MiniColor.Cyan);
                            break;
                        }
                    }
                if (dead) _mine.RemoveAt(i);
            }
            for (int i = _foes.Count - 1; i >= 0; i--)
                if (_foes[i].Hp <= 0)
                {
                    var f = _foes[i];
                    _killedWeight += f.Weight;
                    Burst(f.X, f.Y, f.Kind == 2 ? 40 : 10, f.Kind == 2 ? MiniColor.Red : MiniColor.Amber);
                    _foes.RemoveAt(i);
                }

            // 敵人子彈、碰撞
            for (int i = _theirs.Count - 1; i >= 0; i--)
            {
                var b = _theirs[i];
                b.X += b.Vx * dt; b.Y += b.Vy * dt;
                bool dead = b.Y > MiniField.H + 10 || b.X < -10 || b.X > MiniField.W + 10;
                if (!dead && _invuln <= 0 && Math.Abs(b.X - _px) < 9 && Math.Abs(b.Y - _py) < 10) { Hit(); dead = true; }
                if (dead) _theirs.RemoveAt(i);
            }
            if (_invuln <= 0)
                foreach (var f in _foes)
                    if (Math.Abs(f.X - _px) < f.R + 8 && Math.Abs(f.Y - _py) < f.R + 8) { Hit(); break; }

            if (_clearShots) { _theirs.Clear(); _clearShots = false; }

            for (int i = _sparks.Count - 1; i >= 0; i--)
            {
                var p = _sparks[i];
                p.Life -= dt; p.X += p.Vx * dt; p.Y += p.Vy * dt;
                if (p.Life <= 0) _sparks.RemoveAt(i);
            }

            if (_lives <= 0) { Lose(); return; }
            if (_next >= _spawns.Count && _foes.Count == 0)
            {
                if (Score >= Def.PassScore) Win(); else Lose();
            }
        }

        void Aim(Foe f, float spread)
        {
            float dx = _px - f.X, dy = _py - f.Y;
            float len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) len = 1;
            float ang = (float)Math.Atan2(dx, dy) + spread;
            _theirs.Add(new Shot { X = f.X, Y = f.Y + f.R, Vx = (float)Math.Sin(ang) * _shotSpeed, Vy = (float)Math.Cos(ang) * _shotSpeed });
        }

        void Hit()
        {
            _lives--;
            _invuln = 1.6f;
            Burst(_px, _py, 14, MiniColor.Red);
            _clearShots = true; // 被打中時清掉畫面上的敵彈（迴圈跑完再清，才不會在迴圈裡改到 list）
        }

        void Burst(float x, float y, int n, uint color)
        {
            for (int i = 0; i < n; i++)
            {
                float a = Rand(0, 6.2832f), v = Rand(40, 200);
                _sparks.Add(new Spark { X = x, Y = y, Vx = (float)Math.Cos(a) * v, Vy = (float)Math.Sin(a) * v, Life = Rand(0.25f, 0.6f), Max = 0.6f, Color = color });
            }
        }

        public override void Draw(List<MiniShape> s)
        {
            Spr(s, "alien/bg", MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H);
            // 星空（往下捲）
            for (int i = 0; i < 46; i++)
            {
                float speed = 30 + (i % 3) * 25;
                float x = (i * 137.5f) % MiniField.W;
                float y = (i * 61.3f + Time * speed) % MiniField.H;
                Circle(s, x, y, 1 + (i % 3) * 0.5f, MiniColor.Dim, 0.5f + 0.15f * (i % 3));
            }
            foreach (var p in _sparks) Circle(s, p.X, p.Y, 2.2f, p.Color, Math.Max(0, p.Life / p.Max));
            foreach (var f in _foes)
            {
                // 飛碟貼圖是 2 : 1（寬 : 高），判定範圍比圖小一點
                float w = f.R * (f.Kind == 2 ? 3.4f : 3.0f);
                Spr(s, f.Kind == 2 ? "alien/ufo_boss" : f.Kind == 1 ? "alien/ufo_gunner" : "alien/ufo_small", f.X, f.Y, w, w / 2);
                if (f.Kind == 2)
                {
                    float frac = f.Hp / (float)(22 + 8 * Ctx.Severity);
                    Rect(s, f.X, f.Y - f.R - 8, 60, 4, MiniColor.Grid);
                    Rect(s, f.X - 30 + 30 * frac, f.Y - f.R - 8, 60 * frac, 4, MiniColor.Red);
                }
            }
            foreach (var b in _mine) Spr(s, "alien/shot_player", b.X, b.Y, 7, 14);
            foreach (var b in _theirs) Spr(s, "alien/shot_enemy", b.X, b.Y, 13, 13);
            if (_lives > 0 && !(_invuln > 0 && ((int)(Time * 12) % 2 == 0)))
            {
                Spr(s, "alien/ship", _px, _py, 46, 46);
            }
        }

        public override IEnumerable<MiniStat> Stats
        {
            get
            {
                yield return new MiniStat("mini.alien.stat.hull", _lives + " / " + _maxLives);
                yield return new MiniStat("mini.alien.stat.destroyed", Score / 10 + "%  ( ≥ " + Def.PassScore / 10 + "% )");
            }
        }

        public override MiniInput Autopilot(float skill)
        {
            var inp = new MiniInput();
            inp.MoveY = Clamp((MiniField.H - 50 - _py) / 20f, -1, 1);
            // 技術差的人常常亂飛：每隔一小段時間，有 (1 - skill) 的機率改成飄向隨便一個位置
            if (Time >= _wanderUntil)
            {
                _wanderUntil = Time + Rand(0.4f, 0.9f);
                _wanderX = Rand(40, MiniField.W - 40);
                _wandering = Rand() > skill;
            }
            if (_wandering)
            {
                inp.MoveX = Clamp((_wanderX - _px) / 14f, -1, 1);
                return inp;
            }
            float dodge = 0;
            bool ignore = false;
            if (!ignore)
                foreach (var b in _theirs)
                {
                    if (b.Vy <= 0) continue;
                    float tt = (_py - b.Y) / b.Vy;
                    if (tt < -0.05f || tt > 0.8f) continue;
                    float bx = b.X + b.Vx * tt;
                    float d = _px - bx;
                    if (Math.Abs(d) < 30)
                    {
                        float room = d >= 0 ? MiniField.W - 20 - _px : _px - 20;
                        dodge += (d >= 0 ? 1 : -1) * (room > 60 ? 1 : -1);
                    }
                }
            foreach (var f in _foes)
                if (!ignore && f.Y < _py && _py - f.Y < 90 && Math.Abs(f.X - _px) < f.R + 24)
                    dodge += (_px >= f.X ? 1 : -1);
            if (Math.Abs(dodge) > 0.01f) inp.MoveX = Clamp(dodge, -1, 1);
            else
            {
                // 追最低的敵人（母艦出現後優先打母艦）
                Foe target = null;
                foreach (var f in _foes)
                    if (target == null || f.Kind == 2 || (target.Kind != 2 && f.Y > target.Y)) target = f;
                float tx = target != null ? target.X : MiniField.W / 2;
                inp.MoveX = Clamp((tx - _px) / 14f, -1, 1);
            }
            return inp;
        }

        float _wanderUntil, _wanderX;
        bool _wandering;
    }
}
