using System;
using System.Collections.Generic;

namespace GoNoGo.Core
{
    /// <summary>
    /// 假火點：雙頻道交叉驗證。左右兩張同一地區的熱影像（左 MWIR 中波紅外線、右 LWIR 長波熱紅外線），
    /// 8x6 格、有雜訊。真火在兩張圖的同一格都過各自的門檻線；假火（沙漠曬熱、太陽反光）只有 MWIR 過線，
    /// 另外有中等亮度的誘餌格，嚴重度 2 起還有「差一點」的格子（一邊很亮、另一邊剛好沒過線）。
    /// 玩家點左圖（或右圖）的格子標記（兩張圖同時畫圈），點格子會在中間顯示該格的 MWIR／LWIR 數字，
    /// 可以對照下方圖例的兩條門檻線；按「送出」結算。
    /// 結算：每個誤報 1 次失誤，漏掉一個真火 0.5 次失誤；失誤 ≤ 容許數（2 + Hull，上限 4）且至少找到一半真火才通關。
    /// 多人：每人各自判斷同一批影像，一半以上的人通關全隊才過。
    /// 設計影響：護盾（Ctx.Hull）每多一級多容許 1 次失誤。
    /// </summary>
    [MiniGame("fire_verify", EventKind.FalseFire, Rule = MiniRule.Ratio, PassScore = 1000, RatioNeeded = 0.5)]
    public sealed class FireVerify : MiniGame
    {
        const int Cols = 8, Rows = 6, N = Cols * Rows;
        const float CS = 38, LX = 40, RX = 456, GY = 40;
        const float MwirLine = 55f, LwirLine = 45f;
        const float BtnX = 400, BtnY = 412, BtnW = 180, BtnH = 64;
        // 圖例：每個頻道一條 300 px 寬的色階條（值 0~100 線性對應），位置全由這幾個常數算出來
        const float BarW = 300, BarH = 18, BarY = 331;
        const float RevealTime = 2.6f;

        readonly float[] _m = new float[N], _l = new float[N];
        readonly bool[] _real = new bool[N], _special = new bool[N], _marked = new bool[N];
        int _sel = -1, _allow, _nReal, _found, _falseCnt, _missed, _lastCell = -1;
        float _strikes, _reveal, _flash, _pulse;
        bool _submitted;

        // Autopilot
        List<int> _plan;
        int _apNext;
        float _apAt = 1.0f;
        bool _apSubmitted;

        public override int Score
        {
            get
            {
                if (State == MiniState.Won) return 1000;
                if (!_submitted) return 0;
                return (int)(900.0 * Math.Max(0, 1 - _strikes / (_allow + 2.0)));
            }
        }

        public override float MaxSeconds => 120;

        bool Passes => _strikes <= _allow + 1e-6 && _found >= (_nReal + 1) / 2;

        protected override void OnStart()
        {
            int sev = Ctx.Severity;
            _allow = Math.Min(4, 2 + Math.Max(0, Ctx.Hull));
            _nReal = 2 + sev;
            int nA = 1 + sev, nB = sev >= 2 ? 1 : 0, nC = sev >= 2 ? sev - 1 : 0, nDecoy = 2 + sev;
            for (int i = 0; i < N; i++)
            {
                _m[i] = 4 + Rand() * (10 + 3 * sev);
                _l[i] = 4 + Rand() * (10 + 3 * sev);
                if (Rand() < 0.05f * sev) { if (Rand() < 0.5f) _m[i] = Rand(28, 36); else _l[i] = Rand(28, 36); }
            }
            var order = new List<int>();
            for (int i = 0; i < N; i++) order.Add(i);
            for (int i = N - 1; i > 0; i--) { int j = Rng.Next(i + 1); int t = order[i]; order[i] = order[j]; order[j] = t; }
            int p = 0;
            var blobs = new List<int>();
            var mm = new float[N]; var ll = new float[N];
            Array.Copy(_m, mm, N); Array.Copy(_l, ll, N);
            void Put(float m, float l) { int c = order[p++]; mm[c] = m; ll[c] = l; _special[c] = true; blobs.Add(c); }
            float lo = sev >= 3 ? 62 : 70, llo = sev >= 3 ? 52 : 60;
            for (int i = 0; i < _nReal; i++) Put(Rand(lo, 95), Rand(llo, 90));
            for (int i = 0; i < nA; i++) Put(Rand(65, 95), Rand(12, 34));
            for (int i = 0; i < nB; i++) Put(Rand(12, 34), Rand(55, 85));
            for (int i = 0; i < nC; i++)
            {
                if (i % 2 == 0) Put(Rand(80, 95), Rand(36, 42));     // MWIR 很亮、LWIR 剛好沒過線
                else Put(Rand(46, 52), Rand(52, 64));                // LWIR 過線、MWIR 剛好沒過線
            }
            // 光暈：沒被指定的鄰格受到影響
            foreach (int c in blobs)
            {
                int cx = c % Cols, cy = c / Cols;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if ((dx == 0 && dy == 0) || x < 0 || y < 0 || x >= Cols || y >= Rows) continue;
                        int n = y * Cols + x;
                        if (_special[n]) continue;
                        mm[n] = Math.Max(mm[n], 0.3f * mm[c] * Rand(0.7f, 1f));
                        ll[n] = Math.Max(ll[n], 0.3f * ll[c] * Rand(0.7f, 1f));
                    }
            }
            // 誘餌：兩邊都中等亮，但都沒過線
            for (int i = 0; i < nDecoy && p < N; i++)
            {
                int c = order[p++];
                if (_special[c]) { i--; continue; }
                mm[c] = Rand(38, 52); ll[c] = Rand(32, 42); _special[c] = true;
            }
            for (int i = 0; i < N; i++)
            {
                _m[i] = (float)Math.Round(Clamp(mm[i], 0, 99));
                _l[i] = (float)Math.Round(Clamp(ll[i], 0, 99));
                _real[i] = _m[i] >= MwirLine && _l[i] >= LwirLine;
            }
            _nReal = 0;
            for (int i = 0; i < N; i++) if (_real[i]) _nReal++;
        }

        static bool InRect(float px, float py, float x, float y, float w, float h) =>
            Math.Abs(px - x) <= w / 2 && Math.Abs(py - y) <= h / 2;

        static int CellAt(float px, float py)
        {
            foreach (float x0 in new[] { LX, RX })
            {
                float fx = (px - x0) / CS, fy = (py - GY) / CS;
                if (fx >= 0 && fx < Cols && fy >= 0 && fy < Rows) return (int)fy * Cols + (int)fx;
            }
            return -1;
        }

        static float CellX(int c, float x0) => x0 + (c % Cols + 0.5f) * CS;
        static float CellY(int c) => GY + (c / Cols + 0.5f) * CS;

        protected override void Update(float dt, MiniInput input)
        {
            _flash = Math.Max(0, _flash - dt);
            _pulse = Math.Max(0, _pulse - dt);
            if (_submitted)
            {
                _reveal -= dt;
                if (_reveal <= 0) { if (Passes) Win(); else Lose(); }
                return;
            }
            if (!input.Pressed) return;
            int c = CellAt(input.PX, input.PY);
            if (c >= 0)
            {
                _marked[c] = !_marked[c];
                _sel = c; _lastCell = c; _pulse = 0.2f;
            }
            else if (InRect(input.PX, input.PY, BtnX, BtnY, BtnW + 30, BtnH + 30))
            {
                _submitted = true;
                _flash = 0.3f;
                _reveal = RevealTime;
                for (int i = 0; i < N; i++)
                {
                    if (_marked[i] && _real[i]) _found++;
                    else if (_marked[i]) _falseCnt++;
                    else if (_real[i]) _missed++;
                }
                _strikes = _falseCnt + 0.5f * _missed;
            }
        }

        static uint Lerp(uint a, uint b, float t)
        {
            uint Ch(int sh) { float x = ((a >> sh) & 255) * (1 - t) + ((b >> sh) & 255) * t; return (uint)Math.Round(x) & 255; }
            return (Ch(16) << 16) | (Ch(8) << 8) | Ch(0);
        }

        // 熱度色階：綠 → 黃 → 橘 → 紅（越紅越嚴重）。門檻 45、55 附近的顏色差異夠大，可以和刻線比對
        static readonly float[] StopV = { 0, 25, 40, 55, 75, 100 };
        static readonly uint[] StopC = { 0x1C4D2B, 0x4FA03A, 0xE6D030, 0xF29A20, 0xE84E1E, 0xC8102E };

        static uint Ramp(float v)
        {
            v = Clamp(v, 0, 100);
            for (int i = 1; i < StopV.Length; i++)
                if (v <= StopV[i]) return Lerp(StopC[i - 1], StopC[i], (v - StopV[i - 1]) / (StopV[i] - StopV[i - 1]));
            return StopC[StopC.Length - 1];
        }

        public override void Draw(List<MiniShape> s)
        {
            Spr(s, "fire_verify/bg", MiniField.W / 2, MiniField.H / 2, MiniField.W, MiniField.H);
            Text(s, LX + 25, 14, 15, "MWIR", MiniColor.Cyan);
            Text(s, LX + 190, 14, 13, "@mini.fire_verify.lbl.mwir", MiniColor.Dim);
            Text(s, RX + 25, 14, 15, "LWIR", MiniColor.Cyan);
            Text(s, RX + 190, 14, 13, "@mini.fire_verify.lbl.lwir", MiniColor.Dim);
            for (int i = 0; i < N; i++)
            {
                Rect(s, CellX(i, LX), CellY(i), CS - 2, CS - 2, Ramp(_m[i]));
                Rect(s, CellX(i, RX), CellY(i), CS - 2, CS - 2, Ramp(_l[i]));
            }
            Spr(s, "fire_verify/grid_frame", LX + Cols * CS / 2, GY + Rows * CS / 2, 320, 244);
            Spr(s, "fire_verify/grid_frame", RX + Cols * CS / 2, GY + Rows * CS / 2, 320, 244);

            // 選取框、標記圈
            if (_sel >= 0 && !_submitted)
                foreach (float x0 in new[] { LX, RX })
                {
                    float x = CellX(_sel, x0), y = CellY(_sel), h = CS / 2 - 1;
                    Line(s, x - h, y - h, x + h, y - h, 2, MiniColor.White, 0.9f);
                    Line(s, x - h, y + h, x + h, y + h, 2, MiniColor.White, 0.9f);
                    Line(s, x - h, y - h, x - h, y + h, 2, MiniColor.White, 0.9f);
                    Line(s, x + h, y - h, x + h, y + h, 2, MiniColor.White, 0.9f);
                }
            for (int i = 0; i < N; i++)
            {
                bool mk = _marked[i];
                if (!_submitted && !mk) continue;
                if (_submitted && !mk && !_real[i]) continue;
                uint c = MiniColor.Amber;
                if (_submitted) c = mk ? (_real[i] ? MiniColor.Green : MiniColor.Red) : MiniColor.Amber;
                float sz = 34 + (i == _lastCell && !_submitted ? 10 * _pulse / 0.2f : 0);
                float a = _submitted && !mk ? 0.7f : 1f;
                Spr(s, "fire_verify/ring", CellX(i, LX), CellY(i), sz, sz, 0, a, c);
                Spr(s, "fire_verify/ring", CellX(i, RX), CellY(i), sz, sz, 0, a, c);
            }

            // 中間：選取格的數字
            if (_sel >= 0)
            {
                string name = (char)('A' + _sel / Cols) + ((_sel % Cols) + 1).ToString();
                Text(s, 400, 66, 20, name, MiniColor.Text);
                Text(s, 400, 106, 14, "MWIR", MiniColor.Dim);
                Text(s, 400, 132, 28, ((int)_m[_sel]).ToString(), MiniColor.Text);
                Text(s, 400, 176, 14, "LWIR", MiniColor.Dim);
                Text(s, 400, 202, 28, ((int)_l[_sel]).ToString(), MiniColor.Text);
            }
            else Text(s, 400, 154, 13, "@mini.fire_verify.lbl.tap", MiniColor.Dim);

            // 圖例：色階 + 兩條門檻線
            Legend(s, LX + 2, "MWIR", MwirLine);
            Legend(s, RX + 2, "LWIR", LwirLine);

            // 送出
            uint bc = _flash > 0 ? MiniColor.Green : MiniColor.Cyan;
            Spr(s, "fire_verify/btn_submit", BtnX, BtnY, BtnW, BtnH, 0, _submitted ? 0.5f : 1f, bc);
            Text(s, BtnX + 14, BtnY, 20, "@mini.fire_verify.lbl.submit", MiniColor.Bg);
        }

        void Legend(List<MiniShape> s, float barL, string code, float line)
        {
            float step = BarW / 100f;
            for (int k = 0; k < 100; k++)
                Rect(s, barL + (k + 0.5f) * step, BarY, step, BarH, Ramp(k + 0.5f));
            float tx = barL + line * step, end = barL + BarW;
            Text(s, barL + 22, 301, 13, code, MiniColor.Text);
            // 「過線」括號：從門檻刻線到色階條右端
            Line(s, tx, 312, end, 312, 2, MiniColor.Amber);
            Line(s, tx, 312, tx, 318, 2, MiniColor.Amber);
            Line(s, end, 312, end, 318, 2, MiniColor.Amber);
            Text(s, (tx + end) / 2, 301, 13, "@mini.fire_verify.lbl.over", MiniColor.Amber);
            // 門檻刻線（畫在色階條上）
            Line(s, tx, BarY - BarH / 2 - 3, tx, BarY + BarH / 2 + 3, 3, MiniColor.White);
            Text(s, tx, 353, 13, ((int)line).ToString(), MiniColor.Amber);
            Text(s, barL, 353, 11, "0", MiniColor.Dim);
            Text(s, end, 353, 11, "100", MiniColor.Dim);
        }

        public override MiniCaption Caption
        {
            get
            {
                if (_submitted) return Passes ? new MiniCaption("mini.fire_verify.cap.pass", 1) : new MiniCaption("mini.fire_verify.cap.fail", 3);
                int marked = 0;
                foreach (bool b in _marked) if (b) marked++;
                return marked == 0 ? new MiniCaption("mini.fire_verify.cap.rule", 2) : new MiniCaption("mini.fire_verify.cap.submit", 0);
            }
        }

        public override IEnumerable<MiniStat> Stats
        {
            get
            {
                int marked = 0;
                foreach (bool b in _marked) if (b) marked++;
                yield return new MiniStat("mini.fire_verify.stat.marked", marked.ToString());
                yield return new MiniStat("mini.fire_verify.stat.strikes", (_submitted ? _strikes.ToString("0.#") : "-") + " / " + _allow);
            }
        }

        public override MiniInput Autopilot(float skill)
        {
            var inp = new MiniInput();
            if (State != MiniState.Running || _submitted || _apSubmitted) return inp;
            if (_plan == null)
            {
                _plan = new List<int>();
                float flip = 0.5f * (1 - Clamp(skill, 0, 1));
                for (int i = 0; i < N; i++)
                {
                    bool cand = Math.Max(_m[i], _l[i]) >= 35;
                    bool mark = _m[i] >= MwirLine && _l[i] >= LwirLine; // 讀數字、對照兩條線
                    if (cand && Rand() < flip) mark = !mark;
                    if (mark) _plan.Add(i);
                }
            }
            if (Time < _apAt) return inp;
            _apAt = Time + 0.25f;
            if (_apNext < _plan.Count)
            {
                int c = _plan[_apNext++];
                inp.PX = CellX(c, LX); inp.PY = CellY(c); inp.Pressed = true;
            }
            else
            {
                inp.PX = BtnX; inp.PY = BtnY; inp.Pressed = true;
                _apSubmitted = true;
            }
            return inp;
        }
    }
}
