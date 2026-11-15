using System;
using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// 電量異常：電力繞線。太陽能板輸出掉了，要把電力重新接到電池。
    /// 畫面是一格格的電線方塊（直線、彎頭，另有幾塊破損的不能用）。左邊是太陽能板、右邊是電池；
    /// 滑鼠點方塊就順時針轉 90°，轉到電線從太陽能板一路連到電池、而且沒有任何漏電的開口（接到邊界、破損方塊或接不上的鄰格）就過關（通電的方塊會發亮、漏電處閃紅點、電池會充電）。
    /// 旋轉次數有上限（最少需要的次數 + 約 60% 餘裕），用完還沒接通就失敗。沒有計時壓力。
    /// 關卡由種子決定：先挖一條一定走得通的路（只經過沒破損的方塊），其餘隨機填滿，再全部亂轉（保證開局還沒接通）。
    /// 多人：每人各自一張圖，通關人數過半才算全隊過（Ratio，一半）。
    /// 設計影響：Ctx.Severity 越高格子越大（4×3、5×4、6×4）；Ctx.Margin（電池餘裕）高 → 多一點旋轉次數（最多 +3）；
    /// Ctx.Hull（護盾）每一級 +2 次。
    /// </summary>
    [MiniGame("power_load", EventKind.PowerAnomaly, Rule = MiniRule.Ratio, PassScore = 1000, RatioNeeded = 0.5)]
    public sealed class PowerLoad : MiniGame
    {
        // 方向位元：上 1、右 2、下 4、左 8；順時針轉 90° ＝ 上→右→下→左→上
        const int N = 1, E = 2, S = 4, W = 8;
        const int Straight = 0, Corner = 1, Dead = 2;
        static readonly int[] BaseMask = { N | S, N | E, 0 };
        static readonly string[] WireSprite = { "wire_straight", "wire_corner", null };

        static int RotMask(int m, int k)
        {
            for (int i = 0; i < (k & 3); i++) m = ((m << 1) | (m >> 3)) & 15;
            return m;
        }

        int _cols, _rows, _sr, _er, _budget, _left, _used, _reach;
        float _tile, _gx0, _gy0;
        int[] _type, _rot;
        int[] _req;                       // 開局挖出來的那條路：每格需要的開口（0 = 不在路上）
        readonly List<int> _path = new List<int>();
        float[] _anim, _flash, _shake;
        bool[] _live;
        float _solvedT = -1, _deadMsg;

        public override float MaxSeconds => 120;

        public override int Score
        {
            get
            {
                if (State == MiniState.Won) return 1000;
                float v = 700f * _reach / _cols + 200f * _left / Math.Max(1, _budget);
                return (int)Math.Min(900f, v);
            }
        }

        int Idx(int c, int r) => r * _cols + c;
        int Mask(int i) => RotMask(BaseMask[_type[i]], _rot[i]);

        protected override void OnStart()
        {
            int sev = Ctx.Severity;
            _cols = sev == 1 ? 4 : sev == 2 ? 5 : 6;
            _rows = sev == 1 ? 3 : 4;
            _tile = Math.Min(100f, Math.Min(520f / _cols, 320f / _rows));
            _gx0 = 400 - _cols * _tile / 2;
            _gy0 = 252 - _rows * _tile / 2;
            int n = _cols * _rows;
            _type = new int[n]; _rot = new int[n]; _req = new int[n];
            _anim = new float[n]; _flash = new float[n]; _shake = new float[n]; _live = new bool[n];

            _sr = Rng.Next(_rows);
            _er = Rng.Next(_rows);
            // 挖路：每一欄走過去，欄與欄之間上下移動到下一欄的入口列
            var cells = new List<int>();
            int row = _sr;
            for (int c = 0; c < _cols; c++)
            {
                int target = c == _cols - 1 ? _er : (Rand() < 0.45f ? row : Rng.Next(_rows));
                cells.Add(Idx(c, row));
                while (row != target) { row += target > row ? 1 : -1; cells.Add(Idx(c, row)); }
            }
            _path.AddRange(cells);
            for (int k = 0; k < cells.Count; k++)
            {
                int cur = cells[k];
                int need = 0;
                need |= k == 0 ? W : DirTo(cur, cells[k - 1]);
                need |= k == cells.Count - 1 ? E : DirTo(cur, cells[k + 1]);
                _req[cur] = need;
                bool opposite = need == (N | S) || need == (E | W);
                _type[cur] = opposite ? Straight : Corner;
            }
            // 其他格：隨機填，一部分是破損的
            float deadP = sev == 1 ? 0.2f : 0.28f;
            for (int i = 0; i < n; i++)
            {
                if (_req[i] != 0) continue;
                float r = Rand();
                _type[i] = r < deadP ? Dead : r < deadP + (1 - deadP) / 2 ? Straight : Corner;
            }
            // 全部亂轉，直到「沒接通」；轉的方向都不是剛好解答
            for (int attempt = 0; attempt < 200; attempt++)
            {
                for (int i = 0; i < n; i++) _rot[i] = Rng.Next(4);
                ComputeLive();
                if (!Solved()) break;
            }
            if (Solved()) { _rot[_path[0]]++; ComputeLive(); } // 幾乎不會發生；保險

            int sum = 0;
            foreach (int p in _path) sum += Need(p);
            _budget = (int)Math.Ceiling(sum * 1.6) + 2 + (int)Math.Round(Ctx.Margin * 3) + 2 * Ctx.Hull;
            _left = _budget;
        }

        int DirTo(int from, int to)
        {
            int dc = to % _cols - from % _cols, dr = to / _cols - from / _cols;
            return dc == 1 ? E : dc == -1 ? W : dr == 1 ? S : N;
        }

        /// <summary>這一格要再轉幾下才含有「路」需要的開口（0 = 已經好了或不在路上）。</summary>
        int Need(int i)
        {
            if (_req[i] == 0) return 0;
            for (int k = 0; k < 4; k++)
                if ((RotMask(Mask(i), k) & _req[i]) == _req[i]) return k;
            return 0;
        }

        void ComputeLive()
        {
            Array.Clear(_live, 0, _live.Length);
            int start = Idx(0, _sr);
            var stack = new Stack<int>();
            if ((Mask(start) & W) != 0) { _live[start] = true; stack.Push(start); }
            while (stack.Count > 0)
            {
                int cur = stack.Pop();
                int m = Mask(cur), c = cur % _cols, r = cur / _cols;
                for (int d = 0; d < 4; d++)
                {
                    int bit = 1 << d;
                    if ((m & bit) == 0) continue;
                    int nc = c + (d == 1 ? 1 : d == 3 ? -1 : 0), nr = r + (d == 2 ? 1 : d == 0 ? -1 : 0);
                    if (nc < 0 || nc >= _cols || nr < 0 || nr >= _rows) continue;
                    int nb = Idx(nc, nr);
                    int opp = ((bit << 2) | (bit >> 2)) & 15;
                    if (!_live[nb] && (Mask(nb) & opp) != 0) { _live[nb] = true; stack.Push(nb); }
                }
            }
            _reach = 0;
            for (int i = 0; i < _live.Length; i++) if (_live[i]) _reach = Math.Max(_reach, i % _cols + 1);
        }

        /// <summary>通電方塊 i 的 d 方向開口是不是漏電（接到邊界、破損或接不上的鄰格；太陽能板與電池的接口不算）。</summary>
        bool Leak(int i, int d)
        {
            int bit = 1 << d;
            if ((Mask(i) & bit) == 0) return false;
            int c = i % _cols, r = i / _cols;
            int nc = c + (d == 1 ? 1 : d == 3 ? -1 : 0), nr = r + (d == 2 ? 1 : d == 0 ? -1 : 0);
            if (nc < 0 || nc >= _cols || nr < 0 || nr >= _rows)
                return !((nc < 0 && r == _sr) || (nc >= _cols && r == _er));
            int opp = ((bit << 2) | (bit >> 2)) & 15;
            return (Mask(Idx(nc, nr)) & opp) == 0;
        }

        bool AnyLeak()
        {
            for (int i = 0; i < _live.Length; i++)
                if (_live[i]) for (int d = 0; d < 4; d++) if (Leak(i, d)) return true;
            return false;
        }

        // 過關：電通到電池，而且沒有漏電（只有直線與彎頭，通電的部分一定是一條乾淨的路）
        bool Solved()
        {
            int sink = Idx(_cols - 1, _er);
            return _live[sink] && (Mask(sink) & E) != 0 && !AnyLeak();
        }

        int HitTile(float px, float py)
        {
            float m = 4; // 判定比畫出來的大一圈
            int c = (int)Math.Floor((px - _gx0) / _tile), r = (int)Math.Floor((py - _gy0) / _tile);
            if (px < _gx0 - m || py < _gy0 - m || px > _gx0 + _cols * _tile + m || py > _gy0 + _rows * _tile + m) return -1;
            c = Math.Max(0, Math.Min(_cols - 1, c)); r = Math.Max(0, Math.Min(_rows - 1, r));
            return Idx(c, r);
        }

        protected override void Update(float dt, MiniInput input)
        {
            for (int i = 0; i < _anim.Length; i++)
            {
                _anim[i] = Math.Max(0, _anim[i] - dt * 7f);
                _flash[i] = Math.Max(0, _flash[i] - dt * 3f);
                _shake[i] = Math.Max(0, _shake[i] - dt * 3f);
            }
            _deadMsg = Math.Max(0, _deadMsg - dt);

            if (_solvedT >= 0)
            {
                if (Time - _solvedT >= 1.1f) Win();
                return;
            }

            if (input.Pressed)
            {
                int i = HitTile(input.PX, input.PY);
                if (i >= 0)
                {
                    if (_type[i] == Dead) { _shake[i] = 1f; _deadMsg = 2.5f; }
                    else if (_left > 0)
                    {
                        _rot[i] = (_rot[i] + 1) & 3;
                        _anim[i] = 1f;
                        _flash[i] = 0.6f;
                        _left--; _used++;
                        ComputeLive();
                        if (Solved()) { _solvedT = Time; CueGood(); return; }
                        if (_left <= 0) Lose();
                    }
                }
            }
        }

        // ------------------------------------------------------------ 畫面
        const string K = "@mini.power_load.";

        public override MiniCaption Caption
        {
            get
            {
                if (State == MiniState.Lost) return new MiniCaption("mini.power_load.cap.fail", 3);
                if (_solvedT >= 0) return new MiniCaption("mini.power_load.cap.done", 1);
                if (_deadMsg > 0) return new MiniCaption("mini.power_load.cap.dead", 2);
                if (_left <= Math.Max(2, _budget / 4)) return new MiniCaption("mini.power_load.cap.low", 2);
                if (_reach > 0 && AnyLeak()) return new MiniCaption("mini.power_load.cap.leak", 2);
                if (_used == 0) return new MiniCaption("mini.power_load.cap.start", 0);
                if (_reach > 0) return new MiniCaption("mini.power_load.cap.live", 0);
                return new MiniCaption("mini.power_load.cap.start", 0);
            }
        }

        static uint Mix(uint a, uint b, float t)
        {
            t = Clamp(t, 0, 1);
            uint Ch(int sh) => (uint)Math.Round(((a >> sh) & 255) * (1 - t) + ((b >> sh) & 255) * t);
            return (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
        }

        float CX(int c) => _gx0 + (c + 0.5f) * _tile;
        float CY(int r) => _gy0 + (r + 0.5f) * _tile;

        public override void Draw(List<MiniShape> s)
        {
            Spr(s, "power_load/bg", MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H);
            bool solved = _solvedT >= 0;
            float sprite = 74;

            // 左上：剩餘旋轉次數
            bool low = _left <= Math.Max(2, _budget / 4);
            Text(s, 96, 22, 14, K + "lbl.moves", MiniColor.Dim);
            Text(s, 168, 22, 22, _left + " / " + _budget, low ? MiniColor.Amber : MiniColor.Text);

            // 太陽能板與電池
            float py = CY(_sr), by = CY(_er);
            float px = _gx0 - sprite / 2 + 2, bx = _gx0 + _cols * _tile + sprite / 2 - 2;
            bool srcOn = (Mask(Idx(0, _sr)) & W) != 0;
            Spr(s, "power_load/panel", px, py, sprite, sprite);
            Text(s, px, py + sprite / 2 + 10, 12, K + "lbl.source", MiniColor.Cyan);
            Spr(s, "power_load/battery_empty", bx, by, sprite, sprite);
            if (solved)
            {
                float ch = Clamp((Time - _solvedT) / 0.9f, 0, 1);
                Spr(s, "power_load/battery_full", bx, by, sprite, sprite, 0, ch);
                Ring(s, bx, by, 34 + 14 * ((Time * 2) % 1f), 3, MiniColor.Green, 1 - ((Time * 2) % 1f));
            }
            Text(s, bx, by + sprite / 2 + 10, 12, K + "lbl.sink", solved ? MiniColor.Green : MiniColor.Dim);
            if (srcOn) Rect(s, _gx0 - 3, py, 8, 8, MiniColor.Amber, 0.9f);

            // 提示：開局還沒點過，圈出第一塊要轉的
            if (_used == 0 && !solved)
            {
                foreach (int p in _path)
                    if (Need(p) > 0)
                    {
                        float a = (float)Math.Abs(Math.Sin(Time * 4));
                        Rect(s, CX(p % _cols), CY(p / _cols), _tile + 4 + 8 * a, _tile + 4 + 8 * a, MiniColor.Amber, 0.3f + 0.5f * a);
                        break;
                    }
            }

            // 方塊
            for (int i = 0; i < _type.Length; i++)
            {
                int c = i % _cols, r = i / _cols;
                float x = CX(c), y = CY(r);
                if (_shake[i] > 0) x += (float)Math.Sin(Time * 60) * 4 * _shake[i];
                float size = _tile - 3;
                if (_type[i] == Dead)
                {
                    Spr(s, "power_load/tile_dead", x, y, size, size);
                    if (_flash[i] > 0 || _shake[i] > 0) Rect(s, x, y, size, size, MiniColor.Red, 0.35f * Math.Max(_flash[i], _shake[i]));
                    continue;
                }
                Spr(s, "power_load/tile_plate", x, y, size, size);
                bool on = _live[i];
                if (on) Rect(s, x, y, size - 4, size - 4, MiniColor.Green, solved ? 0.28f : 0.14f);
                uint tint = on ? Mix(MiniColor.Green, 0xE6F25C, 0.5f + 0.5f * (float)Math.Sin(Time * 5 - c * 0.9f)) : 0x6A7B8E;
                float rot = (_rot[i] & 3) * 1.5707963f - _anim[i] * 1.5707963f;
                float sz = _tile + 1;
                Spr(s, "power_load/" + WireSprite[_type[i]], x, y, sz, sz, rot, 1, tint);
                if (_flash[i] > 0) Rect(s, x, y, size, size, MiniColor.White, 0.35f * _flash[i]);

                // 漏電的開口：紅點提示
                if (on && !solved)
                    for (int d = 0; d < 4; d++)
                    {
                        if (!Leak(i, d)) continue;
                        float ex = x + (d == 1 ? 1 : d == 3 ? -1 : 0) * (_tile / 2 - 5), ey = y + (d == 2 ? 1 : d == 0 ? -1 : 0) * (_tile / 2 - 5);
                        float pl = 0.6f + 0.4f * (float)Math.Abs(Math.Sin(Time * 6));
                        Circle(s, ex, ey, 6, MiniColor.Red, pl);
                        Ring(s, ex, ey, 9, 2, MiniColor.Red, pl * 0.7f);
                    }
            }

            // 下方小提示
            Text(s, 400, 436, 13, K + "hint.line", MiniColor.Dim, 0.9f);
        }

        public override IEnumerable<MiniStat> Stats
        {
            get
            {
                yield return new MiniStat("mini.power_load.stat.moves", _left + " / " + _budget);
                int live = 0;
                foreach (bool b in _live) if (b) live++;
                yield return new MiniStat("mini.power_load.stat.live", live + "");
            }
        }

        // ------------------------------------------------------------ 自動駕駛
        float _nextAct;
        float _mx = MiniField.W / 2, _my = MiniField.H / 2;

        // 點擊：滑鼠停在那一格上（之後每一幀都維持這個位置，框架把 Pressed 延到下一個固定步長時座標才不會跑掉）
        MiniInput Click(int tile)
        {
            _mx = CX(tile % _cols); _my = CY(tile / _cols);
            return new MiniInput { PX = _mx, PY = _my, Pressed = true, Down = true };
        }

        public override MiniInput Autopilot(float skill)
        {
            var idle = new MiniInput { PX = _mx, PY = _my };
            if (Time < _nextAct || _solvedT >= 0) return idle;
            _nextAct = Time + 0.4f; // Time 只在固定步長前進，同一個遊戲時間不會重複點
            if (Rand() < (1f - skill) * 0.85f)
            {
                // 手滑：亂點一格（可能是破損的、也可能把轉好的轉壞）
                return Click(Rng.Next(_type.Length));
            }
            foreach (int p in _path)
                if (Need(p) > 0) return Click(p);
            return idle;
        }
    }
}
