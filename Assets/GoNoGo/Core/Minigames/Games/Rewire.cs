using System;
using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// 零件故障：對應手冊三步「隔離故障零件 → 切換備援／重新開機 → 重新排程」，做成三個連續的小關卡。
    /// 1. 找出故障：6~8 條零件遙測波形，其中一條故障（掉到底、雜訊暴衝或卡死），選出來按「隔離」。
    /// 2. 切換備援：把插頭接到顏色與形狀相同的插孔，接錯短路扣機會。
    /// 3. 重新排程：游標來回移動，在綠色區域按下停住，要連續成功數次。
    /// 多人：每個人各自挑戰，通關人數達一半以上就算全隊過（Ratio）；分工的零件故障只要有人處置好，團隊就能處置。
    /// 設計影響：護盾（Hull）加機會數；備援零件（Redundant）只要接 1 條線；姿態穩定度（Steady）加大綠色區域。
    /// </summary>
    [MiniGame("rewire", EventKind.PartFailure, Rule = MiniRule.Ratio, PassScore = 1000, RatioNeeded = 0.5)]
    public sealed class Rewire : MiniGame
    {
        const float BarL = 100, BarR = 700, BarY = 225, FaultAt = 0.8f, Window = 1.5f;
        static readonly uint[] PairColor = { MiniColor.Cyan, MiniColor.Amber, MiniColor.Violet };
        static readonly float[] SlotY = { 150, 250, 350 };
        const float PlugX = 220, SockX = 580;
        // 每一格都要畫的固定文字（先備好，不用每格重新組字串）
        static readonly string[] StepNo = { "1", "2", "3" };
        static readonly string[] StepLabel = { "@mini.rewire.lbl.step1", "@mini.rewire.lbl.step2", "@mini.rewire.lbl.step3" };
        static readonly string[] ChannelName = { "CH1", "CH2", "CH3", "CH4", "CH5", "CH6", "CH7", "CH8" };

        int _stage = 1, _lives, _maxLives;
        float _pause, _hurt, _pulse;

        // 階段 1
        int _n, _fault, _ftype, _cursor = -1;
        float[] _freq, _phase, _amp, _flash;
        bool[] _elim;
        int _wrongCount;

        // 階段 2
        int[] _plugOrder = new int[3], _sockOrder = new int[3];
        bool[] _connected = new bool[3];
        int _need, _done, _only = -1, _selPlug = -1;
        float _shortFlash;
        int _shortSock = -1;

        // 階段 3
        float _cx = BarL, _cdir = 1, _zc, _zh, _speed, _freeze;
        int _streak, _nStreak, _attempt;
        bool _lastOk;

        public override int Score
        {
            get
            {
                if (State == MiniState.Won) return 1000;
                if (_stage == 1) return 0;
                if (_stage == 2) return 300 + (int)(300.0 * _done / Math.Max(1, _need));
                return 600 + (int)(300.0 * _streak / Math.Max(1, _nStreak));
            }
        }

        public override float MaxSeconds => 150;

        float _clickX, _clickY, _clickT;

        public override MiniCaption Caption
        {
            get
            {
                if (State != MiniState.Running) return default;
                if (_hurt > 0) return new MiniCaption("mini.rewire.cap.wrong", 3);
                if (_stage == 1) return new MiniCaption(_cursor >= 0 ? "mini.rewire.cap.1b" : "mini.rewire.cap.1");
                if (_stage == 2) return new MiniCaption(_selPlug >= 0 ? "mini.rewire.cap.2b" : "mini.rewire.cap.2");
                return new MiniCaption("mini.rewire.cap.3");
            }
        }

        protected override void OnStart()
        {
            _maxLives = _lives = 3 + Ctx.Hull;
            // 階段 1
            float rn = Rand();
            _n = (Ctx.Severity >= 2 && rn < 0.5f) ? 8 : 6;
            _freq = new float[_n]; _phase = new float[_n]; _amp = new float[_n]; _flash = new float[_n]; _elim = new bool[_n];
            for (int i = 0; i < _n; i++) { _freq[i] = Rand(0.8f, 1.6f); _phase[i] = Rand(0, 6.28f); _amp[i] = Rand(0.45f, 0.8f); }
            _fault = Math.Min(_n - 1, (int)(Rand() * _n));
            _ftype = Math.Min(2, (int)(Rand() * 3));
            // 階段 2：把兩邊的順序打亂
            Shuffle(_plugOrder);
            Shuffle(_sockOrder);
            _only = Math.Min(2, (int)(Rand() * 3));
            _need = Ctx.Redundant ? 1 : 3;
            // 階段 3
            _nStreak = 2 + Ctx.Severity / 2;
            _zh = (110 + 70 * (float)Ctx.Steady - 10 * Ctx.Severity) / 2;
            _speed = 240 + 40 * Ctx.Severity;
        }

        void Shuffle(int[] a)
        {
            for (int i = 0; i < a.Length; i++) a[i] = i;
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = Math.Min(i, (int)(Rand() * (i + 1)));
                int t = a[i]; a[i] = a[j]; a[j] = t;
            }
        }

        static float Hash(int a, int b)
        {
            unchecked
            {
                uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663);
                h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
                return (h & 0xFFFF) / 65535f;
            }
        }

        float Wave(int ch, float tau)
        {
            if (ch == _fault && tau >= FaultAt)
            {
                switch (_ftype)
                {
                    case 0: return -0.9f;
                    case 1: return Hash(ch, (int)Math.Floor(tau * 40)) * 2 - 1;
                    default: return _amp[ch] * (float)Math.Sin(6.2832 * _freq[ch] * FaultAt + _phase[ch]);
                }
            }
            return _amp[ch] * (float)Math.Sin(6.2832 * _freq[ch] * tau + _phase[ch]);
        }

        // ---------------------------------------------------------------- 更新

        protected override void Update(float dt, MiniInput input)
        {
            _hurt = Math.Max(0, _hurt - dt);
            _shortFlash = Math.Max(0, _shortFlash - dt);
            _pulse += dt;
            _clickT = Math.Max(0, _clickT - dt);
            if (input.Pressed) { _clickX = input.PX; _clickY = input.PY; _clickT = 0.35f; }
            for (int i = 0; i < _n; i++) _flash[i] = Math.Max(0, _flash[i] - dt);
            if (_pause > 0) { _pause -= dt; return; }
            if (_stage == 1) UpdateFind(input);
            else if (_stage == 2) UpdateWire(input);
            else UpdateSchedule(dt, input);
        }

        void Hurt()
        {
            _lives--;
            CueBad();
            _hurt = 0.4f;
            if (_lives <= 0) Lose();
        }

        // ---- 階段 1
        static float CellW(int n) => 740f / (n / 2);
        float CellCx(int i) => 30 + CellW(_n) * (i % (_n / 2) + 0.5f);
        static float CellCy(int i, int n) => 133 + (i / (n / 2)) * 158;

        int CellAt(float px, float py)
        {
            for (int i = 0; i < _n; i++)
                if (Math.Abs(px - CellCx(i)) < CellW(_n) / 2 && Math.Abs(py - CellCy(i, _n)) < 79) return i;
            return -1;
        }

        void UpdateFind(MiniInput input)
        {
            bool isolate = false;
            if (input.Pressed)
            {
                int c = CellAt(input.PX, input.PY);
                if (c >= 0 && !_elim[c]) _cursor = c;
                else if (_cursor >= 0 && Math.Abs(input.PX - 400) < 100 && Math.Abs(input.PY - 405) < 28) isolate = true;
            }
            if (!isolate || _cursor < 0) return;
            if (_cursor == _fault)
            {
                _stage = 2;
                _pause = 0.7f;
            }
            else
            {
                _elim[_cursor] = true;
                _flash[_cursor] = 0.5f;
                _wrongCount++;
                Hurt();
                _cursor = -1;
            }
        }

        // ---- 階段 2
        bool PairActive(int pair) => !Ctx.Redundant || pair == _only;

        int NearSlot(float px, float py, float x)
        {
            for (int s = 0; s < 3; s++)
                if (Math.Abs(px - x) < 40 && Math.Abs(py - SlotY[s]) < 40) return s;
            return -1;
        }

        void UpdateWire(MiniInput input)
        {
            int plug = -1, sock = -1;
            if (input.Pressed)
            {
                int p = NearSlot(input.PX, input.PY, PlugX);
                if (p >= 0) plug = p;
                else
                {
                    int s = NearSlot(input.PX, input.PY, SockX);
                    if (s >= 0 && _selPlug >= 0) sock = s;
                }
            }
            if (plug >= 0)
            {
                int pair = _plugOrder[plug];
                if (PairActive(pair) && !_connected[pair]) _selPlug = plug;
            }
            else if (sock >= 0 && _selPlug >= 0)
            {
                int sp = _sockOrder[sock];
                if (_connected[sp]) return;
                int pp = _plugOrder[_selPlug];
                if (sp == pp)
                {
                    _connected[pp] = true;
                    _done++;
                    CueGood();
                    _selPlug = -1;
                    if (_done >= _need) { _stage = 3; _pause = 0.7f; NewZone(); }
                }
                else
                {
                    _shortFlash = 0.5f;
                    _shortSock = sock;
                    _selPlug = -1;
                    Hurt();
                }
            }
        }

        // ---- 階段 3
        void NewZone()
        {
            _zc = Rand(BarL + _zh + 10, BarR - _zh - 10);
            _attempt++;
        }

        void UpdateSchedule(float dt, MiniInput input)
        {
            if (_freeze > 0)
            {
                _freeze -= dt;
                if (_freeze <= 0) NewZone();
                return;
            }
            _cx += _cdir * _speed * dt;
            if (_cx > BarR) { _cx = BarR; _cdir = -1; }
            else if (_cx < BarL) { _cx = BarL; _cdir = 1; }
            if (!input.Pressed) return;
            _lastOk = Math.Abs(_cx - _zc) <= _zh;
            _freeze = 0.55f;
            if (_lastOk)
            {
                _streak++;
                CueGood();
                if (_streak >= _nStreak) Win();
            }
            else
            {
                _streak = 0;
                Hurt();
            }
        }

        // ---------------------------------------------------------------- 畫面

        public override void Draw(List<MiniShape> s)
        {
            Spr(s, "rewire/bg", MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H);
            // 上方：三個階段
            for (int i = 0; i < 3; i++)
            {
                bool done = _stage > i + 1 || State == MiniState.Won;
                uint c = done ? MiniColor.Green : _stage == i + 1 ? MiniColor.Cyan : MiniColor.Dim;
                float x = 300 + i * 100;
                if (i > 0) Line(s, x - 70, 22, x - 30, 22, 2, MiniColor.Dim, 0.6f);
                Ring(s, x, 22, 14, 2.5f, c);
                Text(s, x, 22, 11, StepNo[i], c);
                Text(s, x, 46, 11, StepLabel[i], c);
            }
            // 機會
            for (int i = 0; i < _maxLives; i++)
                Spr(s, "rewire/light", 30 + i * 18, 22, 13, 13, 0, 1, i < _lives ? MiniColor.Green : MiniColor.Grid);

            if (_stage == 1) DrawFind(s);
            else if (_stage == 2) DrawWire(s);
            else DrawSchedule(s);

            if (_clickT > 0)
            {
                float k = 1 - _clickT / 0.35f;
                Ring(s, _clickX, _clickY, 8 + 26 * k, 3, MiniColor.White, 1 - k);
            }
            if (_hurt > 0) Rect(s, MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H, MiniColor.Red, 0.25f * (_hurt / 0.4f));
            if (_pause > 0 && State == MiniState.Running)
                Text(s, 400, 225, 60, "OK", MiniColor.Green, Math.Min(1f, _pause / 0.4f));
        }

        void DrawFind(List<MiniShape> s)
        {
            float cw = CellW(_n);
            for (int i = 0; i < _n; i++)
            {
                float cx = CellCx(i), cy = CellCy(i, _n);
                bool sel = i == _cursor && !_elim[i];
                Rect(s, cx, cy, cw - 6, 150, sel ? MiniColor.Cyan : MiniColor.Grid);
                Rect(s, cx, cy, cw - 10, 146, MiniColor.Panel);
                if (_flash[i] > 0) Rect(s, cx, cy, cw - 10, 146, MiniColor.Red, 0.5f * (_flash[i] / 0.5f));
                Spr(s, "rewire/corner", cx - cw / 2 + 14, cy - 61, 22, 22, 0, 1, sel ? MiniColor.Cyan : MiniColor.Dim);
                Spr(s, "rewire/corner", cx + cw / 2 - 14, cy + 61, 22, 22, 3.1416f, 1, sel ? MiniColor.Cyan : MiniColor.Dim);
                // 另外兩角：rot 為正 = 順時針（畫布 y 軸向下），左上角順轉 90° 是右上角
                Spr(s, "rewire/corner", cx + cw / 2 - 14, cy - 61, 22, 22, 1.5708f, 1, sel ? MiniColor.Cyan : MiniColor.Dim);
                Spr(s, "rewire/corner", cx - cw / 2 + 14, cy + 61, 22, 22, -1.5708f, 1, sel ? MiniColor.Cyan : MiniColor.Dim);
                Text(s, cx - cw / 2 + 38, cy - 62, 12, ChannelName[i], _elim[i] ? MiniColor.Dim : MiniColor.Text);
                Line(s, cx - cw / 2 + 12, cy + 8, cx + cw / 2 - 12, cy + 8, 1, MiniColor.Grid);
                uint lc = _elim[i] ? MiniColor.Dim : MiniColor.Green;
                float x0 = cx - cw / 2 + 12, x1 = cx + cw / 2 - 12;
                const int seg = 24;
                float px = x0, py = 0;
                for (int k = 0; k <= seg; k++)
                {
                    float x = x0 + (x1 - x0) * k / seg;
                    float tau = Time - (x1 - x) / (((x1 - x0)) / Window);
                    float y = cy + 8 - Wave(i, tau) * 48;
                    if (k > 0) Line(s, px, py, x, y, 2, lc, _elim[i] ? 0.5f : 1f);
                    px = x; py = y;
                }
                if (_elim[i]) Text(s, cx, cy + 8, 40, "X", MiniColor.Red, 0.8f);
            }
            float bp = 1 + 0.04f * (float)Math.Sin(_pulse * 6);
            Spr(s, "rewire/button", 400, 405, 176 * bp, 44 * bp, 0, 1, MiniColor.Amber);
            Text(s, 400, 405, 18, "@mini.rewire.lbl.isolate", MiniColor.Bg);
            if (_cursor >= 0)
                Ring(s, 400, 405, 100 + 4 * (float)Math.Sin(_pulse * 8), 3, MiniColor.White, 0.85f);
        }

        static void DrawPlug(List<MiniShape> s, int pair, float x, float y, uint c, float alpha)
        {
            Spr(s, pair == 0 ? "rewire/plug_circle" : pair == 1 ? "rewire/plug_square" : "rewire/plug_tri", x, y, 40, 40, 0, alpha, c);
        }

        void DrawWire(List<MiniShape> s)
        {
            for (int sl = 0; sl < 3; sl++)
            {
                int pp = _plugOrder[sl], sp = _sockOrder[sl];
                bool pa = PairActive(pp), sa = PairActive(sp);
                // 插頭
                Spr(s, "rewire/socket", PlugX, SlotY[sl], 64, 64);
                DrawPlug(s, pp, PlugX, SlotY[sl], PairColor[pp], pa ? 1f : 0.2f);
                if (_selPlug == sl) Ring(s, PlugX, SlotY[sl], 34 + 2 * (float)Math.Sin(_pulse * 8), 3, MiniColor.White);
                // 插孔
                Spr(s, "rewire/socket", SockX, SlotY[sl], 64, 64);
                DrawPlug(s, sp, SockX, SlotY[sl], PairColor[sp], sa ? 0.45f : 0.12f);
                Ring(s, SockX, SlotY[sl], 33, 2, sa ? PairColor[sp] : MiniColor.Grid);
                if (_selPlug >= 0 && sa && !_connected[sp]) Ring(s, SockX, SlotY[sl], 38 + 3 * (float)Math.Sin(_pulse * 7), 3, MiniColor.White, 0.6f);
                if (_shortFlash > 0 && _shortSock == sl) Rect(s, SockX, SlotY[sl], 64, 64, MiniColor.Red, 0.7f * (_shortFlash / 0.5f));
            }
            // 已接好的線
            for (int sl = 0; sl < 3; sl++)
            {
                int pp = _plugOrder[sl];
                if (!_connected[pp]) continue;
                int ss = Array.IndexOf(_sockOrder, pp);
                Line(s, PlugX + 32, SlotY[sl], SockX - 32, SlotY[ss], 6, PairColor[pp]);
            }
            Text(s, 400, 405, 14, _done + " / " + _need, MiniColor.Text);
            if (_selPlug < 0)
                for (int sl = 0; sl < 3; sl++)
                    if (PairActive(_plugOrder[sl]) && !_connected[_plugOrder[sl]])
                    { Ring(s, PlugX, SlotY[sl], 38 + 3 * (float)Math.Sin(_pulse * 7), 3, MiniColor.White, 0.7f); break; }
        }

        void DrawSchedule(List<MiniShape> s)
        {
            Spr(s, "rewire/bar_frame", 400, BarY, 620, 38.75f);
            Rect(s, _zc, BarY, _zh * 2, 36, MiniColor.Green, 0.85f);
            uint cc = _freeze > 0 ? (_lastOk ? MiniColor.Green : MiniColor.Red) : MiniColor.Amber;
            Rect(s, _cx, BarY, 6, 70, cc);
            Spr(s, "rewire/marker", _cx, BarY - 46, 20, 20, 3.1416f, 1, cc);
            for (int i = 0; i < _nStreak; i++)
                Spr(s, "rewire/light", 400 + (i - (_nStreak - 1) / 2f) * 30, 330, 20, 20, 0, 1, i < _streak ? MiniColor.Green : MiniColor.Grid);
            Text(s, 400, 370, 14, _streak + " / " + _nStreak, MiniColor.Text);
        }

        public override IEnumerable<MiniStat> Stats
        {
            get
            {
                yield return new MiniStat("mini.rewire.stat.stage", Math.Min(3, _stage) + " / 3");
                yield return new MiniStat("mini.rewire.stat.chances", _lives + " / " + _maxLives);
                string prog = _stage == 1 ? "0 / 1" : _stage == 2 ? _done + " / " + _need : _streak + " / " + _nStreak;
                yield return new MiniStat("mini.rewire.stat.progress", prog);
            }
        }

        // ---------------------------------------------------------------- 自動駕駛

        int _botSig = -1, _botSock = -1, _botAttempt = -1;
        float _botT, _botOff;

        public override MiniInput Autopilot(float skill)
        {
            var inp = new MiniInput();
            if (State != MiniState.Running || _pause > 0) return inp;
            int sig = _stage * 100000 + _lives * 10000 + _wrongCount * 1000 + (_selPlug + 1) * 100 + _done * 10 + _streak + _attempt * 7919;
            if (sig != _botSig) { _botSig = sig; _botT = Time + 0.35f; if (_selPlug < 0) _botSock = -1; }
            if (Time < _botT) return inp;

            if (_stage == 1)
            {
                if (Time < FaultAt + Window + 0.2f) return inp;
                if (_botSock < 0)
                {
                    int pick = _fault;
                    if (Rand() > skill)
                    {
                        var left = new List<int>();
                        for (int i = 0; i < _n; i++) if (!_elim[i]) left.Add(i);
                        pick = left[Math.Min(left.Count - 1, (int)(Rand() * left.Count))];
                    }
                    _botSock = pick;
                }
                if (_cursor != _botSock)
                {
                    inp.PX = CellCx(_botSock); inp.PY = CellCy(_botSock, _n); inp.Pressed = true;
                }
                else { inp.PX = 400; inp.PY = 405; inp.Pressed = true; }
                return inp;
            }
            if (_stage == 2)
            {
                if (_selPlug < 0)
                {
                    for (int sl = 0; sl < 3; sl++)
                    {
                        int pp = _plugOrder[sl];
                        if (PairActive(pp) && !_connected[pp]) { inp.PX = PlugX; inp.PY = SlotY[sl]; inp.Pressed = true; break; }
                    }
                    return inp;
                }
                if (_botSock < 0)
                {
                    int want = Array.IndexOf(_sockOrder, _plugOrder[_selPlug]);
                    int pick = want;
                    if (Rand() > skill) pick = Math.Min(2, (int)(Rand() * 3));
                    _botSock = pick;
                }
                inp.PX = SockX; inp.PY = SlotY[_botSock]; inp.Pressed = true;
                return inp;
            }
            // 階段 3
            if (_freeze > 0) return inp;
            if (_botAttempt != _attempt)
            {
                _botAttempt = _attempt;
                _botOff = (Rand() * 2 - 1) * (1 - skill) * 200f;
            }
            float target = Clamp(_zc + _botOff, BarL, BarR);
            if (Math.Abs(_cx - target) <= _speed * FixedDt * 2.5f) { inp.PX = 400; inp.PY = BarY; inp.Pressed = true; }
            return inp;
        }
    }
}
