using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GoNoGo.Core
{
    // ------------------------------------------------------------------------------------------------
    // 營運小遊戲的共用框架。
    //
    // 流程：玩家在飛行指揮台判斷事件 → 依「判斷的事件」打對應的小遊戲 → 通過才算處置完成（拿到手冊上全部步驟），
    // 沒過 = 沒有處置（照原本的模擬扣分）。判斷錯了就會打到別的遊戲，處置到的步驟也對不上。
    //
    // 遊戲邏輯全部放在 Core（沒有 Unity 依賴）：用固定時間步長、System.Random(seed)，同種子 + 同輸入 = 同結果，
    // 所以可以不用人玩就跑測試（每個遊戲都附一個 Autopilot）。畫面由 UI 層讀 Draw() 畫出來。
    // ------------------------------------------------------------------------------------------------

    /// <summary>虛擬畫布大小。所有遊戲都畫在 800 × 450 裡（原點在左上角），UI 負責等比例縮放。</summary>
    public static class MiniField
    {
        public const float W = 800, H = 450;
    }

    /// <summary>遊戲裡用的顏色（0xRRGGBB），和設施 UI 的青／琥珀／綠色系一致。</summary>
    public static class MiniColor
    {
        public const uint Bg = 0x0A1018, Panel = 0x14202E, Grid = 0x1E2E40, Dim = 0x5C6F82, Text = 0xDCE6F0,
            Cyan = 0x35D0E8, Amber = 0xFFB020, Green = 0x4ADE80, Red = 0xFF5A5F, Violet = 0xB48CFF, White = 0xFFFFFF;
    }

    public enum MiniState { Running, Won, Lost }

    public enum MiniShapeKind
    {
        /// <summary>置中矩形：X, Y = 中心，A = 寬，B = 高，Rot = 旋轉（弧度）。</summary>
        Rect,
        /// <summary>實心圓：X, Y = 中心，A = 半徑。</summary>
        Circle,
        /// <summary>圓環：X, Y = 中心，A = 半徑，W = 線寬。</summary>
        Ring,
        /// <summary>線段：(X, Y) 到 (A, B)，W = 線寬。</summary>
        Line,
        /// <summary>三角形（尖端朝上，Rot = 旋轉弧度）：X, Y = 中心，A = 大小（外接圓半徑）。</summary>
        Tri,
        /// <summary>文字：X, Y = 中心，A = 字級（畫布像素），Str = 內容。Str 以 "@" 開頭 = 字串鍵，UI 會翻譯（例如 "@mini.power_load.lbl.battery"）。</summary>
        Text,
        /// <summary>
        /// 貼圖（Blender 畫的 2D 圖）：X, Y = 中心，A = 寬，B = 高，Rot = 旋轉（弧度，圖是尖端朝上畫的），
        /// Str = 圖的名字（"遊戲/名字"，對應 Resources/Minigames/遊戲/名字.png），Color = 色調（0xFFFFFF = 原色），Alpha = 透明度。
        /// </summary>
        Sprite,
    }

    public struct MiniShape
    {
        public MiniShapeKind Kind;
        public float X, Y, A, B, W, Rot;
        public uint Color;
        public float Alpha;
        public string Str;
    }

    /// <summary>
    /// 畫布上方的一行即時說明（現在該做什麼、發生什麼事）。Key 是字串鍵（UI 翻譯），Tone：0 一般、1 好（綠）、2 提醒（琥珀）、3 危險（紅）。
    /// Key 為 null = 不顯示。
    /// </summary>
    public struct MiniCaption
    {
        public string Key;
        public int Tone;
        public MiniCaption(string key, int tone = 0) { Key = key; Tone = tone; }
    }

    /// <summary>一個畫面上方的數值（例如分數、血量）。Key 是字串鍵，Value 已格式化。</summary>
    public struct MiniStat
    {
        public string Key, Value;
        public MiniStat(string key, string value) { Key = key; Value = value; }
    }

    /// <summary>
    /// 一個影格的輸入（座標是虛擬畫布座標）。Pressed、Action 和 Number 只在按下去的那一格有值。
    /// MoveX / MoveY 是 WASD 或方向鍵（-1 ~ 1），Fire 是按住空白鍵，Action 是按下空白鍵或 Enter。
    /// Number 是這一格按下的數字鍵 1 ~ 9（沒按是 0）。
    /// </summary>
    public struct MiniInput
    {
        public float MoveX, MoveY;
        public int Number;
        public bool Fire, Action;
        public float PX, PY;
        public bool Down, Pressed;
    }

    public enum MiniRule
    {
        /// <summary>按比例：每個人各自通關，通關人數要達到 RatioNeeded 才算過。</summary>
        Ratio,
        /// <summary>算總分：所有人的分數平均要達到 PassScore（沒打的算 0）。</summary>
        Sum,
    }

    /// <summary>標在遊戲類別上：這個遊戲處理哪種事件、多人怎麼判過關、單人過關分數。</summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class MiniGameAttribute : Attribute
    {
        public string Id { get; }
        public EventKind Kind { get; }
        public MiniRule Rule { get; set; } = MiniRule.Ratio;
        /// <summary>分數（0 ~ 1000）達到這個值就算通關。</summary>
        public int PassScore { get; set; } = 1000;
        /// <summary>Ratio 規則：通關人數比例（0.5 = 一半以上）。</summary>
        public double RatioNeeded { get; set; } = 0.5;
        public MiniGameAttribute(string id, EventKind kind) { Id = id; Kind = kind; }
    }

    public sealed class MinigameDef
    {
        public string Id { get; set; } = "";
        public EventKind Kind { get; set; }
        public MiniRule Rule { get; set; }
        public int PassScore { get; set; }
        public double RatioNeeded { get; set; }
        public Type Type { get; set; }
    }

    /// <summary>
    /// 開局時由設計與事件算出來的參數：設計得好的衛星，小遊戲比較好打（讓設計階段的選擇影響營運）。
    /// </summary>
    public sealed class MinigameContext
    {
        /// <summary>事件嚴重程度 1 ~ 3。</summary>
        public int Severity { get; set; } = 1;
        public int Players { get; set; } = 1;
        /// <summary>護盾加成（0 ~ 2）：多幾格血量。</summary>
        public int Hull { get; set; }
        /// <summary>指向穩定度（0 ~ 1，越高越容易鎖定）：來自姿態控制精度。</summary>
        public double Steady { get; set; } = 0.5;
        /// <summary>電力餘裕（0 ~ 1，越高越撐得住）：來自電池。</summary>
        public double Margin { get; set; } = 0.5;
        /// <summary>故障零件有備援（接線比較少）。</summary>
        public bool Redundant { get; set; }
        /// <summary>事件的參考 id：故障零件、科學機會種類、火點地點。</summary>
        public string RefId { get; set; } = "";

        public static MinigameContext From(Design design, GameData data, MissionEvent e, int players)
        {
            var c = new MinigameContext { Severity = Math.Max(1, Math.Min(3, e?.Severity ?? 1)), Players = Math.Max(1, players), RefId = e?.RefId ?? "" };
            if (design == null || data == null) return c;
            var shield = data.Part(design.Get(PartCategory.Shielding));
            if (shield != null) c.Hull = Math.Max(0, Math.Min(2, shield.Tier - 1));
            var adcs = data.Part(design.Get(PartCategory.Adcs));
            if (adcs != null && adcs.PointingArcsec > 0)
                c.Steady = Math.Max(0, Math.Min(1, 1.0 - Math.Log10(Math.Max(1, adcs.PointingArcsec)) / 2.5));
            var batt = data.Part(design.Get(PartCategory.Battery));
            if (batt != null) c.Margin = Math.Max(0, Math.Min(1, batt.Tier / 3.0));
            if (e != null && e.Kind == EventKind.PartFailure) c.Redundant = data.Part(e.RefId)?.Redundant ?? false;
            return c;
        }
    }

    /// <summary>小遊戲基底：固定時間步長、結算分數、畫面輸出。</summary>
    public abstract class MiniGame
    {
        public const float FixedDt = 1f / 60f;

        public MinigameDef Def { get; internal set; }
        public MinigameContext Ctx { get; internal set; }
        protected Random Rng { get; private set; }
        public float Time { get; private set; }
        public MiniState State { get; private set; } = MiniState.Running;
        float _acc;

        public string Id => Def.Id;

        /// <summary>目前的分數（0 ~ 1000）。</summary>
        public abstract int Score { get; }

        /// <summary>結算分數：通關一定 ≥ PassScore，沒過一定 &lt; PassScore（讓「分數」和「通關」不會互相矛盾）。</summary>
        public int FinalScore
        {
            get
            {
                int s = Math.Max(0, Math.Min(1000, Score));
                if (State == MiniState.Won) return Math.Max(s, Def.PassScore);
                return Math.Min(s, Math.Max(0, Def.PassScore - 1));
            }
        }

        public bool Passed => State == MiniState.Won;

        /// <summary>測試用的時間上限（秒）。超過就視為壞掉。</summary>
        public virtual float MaxSeconds => 120;

        internal void Setup(MinigameDef def, MinigameContext ctx, int seed)
        {
            Def = def;
            Ctx = ctx ?? new MinigameContext();
            Rng = new Random(seed);
            OnStart();
        }

        protected abstract void OnStart();
        protected abstract void Update(float dt, MiniInput input);

        /// <summary>畫面（每個影格呼叫，把形狀加進 list）。</summary>
        public abstract void Draw(List<MiniShape> shapes);

        /// <summary>畫布上方的即時說明（預設沒有）。要顯示的文字放在字串檔，這裡只給 key，不要在遊戲裡寫死中文。</summary>
        public virtual MiniCaption Caption => default;

        /// <summary>畫面上方顯示的數值。</summary>
        public abstract IEnumerable<MiniStat> Stats { get; }

        /// <summary>
        /// 自動駕駛（測試和除錯示範用）。skill 1 = 一定打得過；0 = 亂打。
        /// 預設什麼都不做（所以預設的測試「不玩一定輸」就能成立）。
        /// </summary>
        public virtual MiniInput Autopilot(float skill) => default;

        /// <summary>推進時間（任何 dt 都行，內部切成固定步長）。</summary>
        // 「剛按下」的訊號要先存起來，等到真的有跑一步的時候才給遊戲。
        // 螢幕刷新率高於 60Hz 時，有些影格一步都不會跑，沒有這個暫存，那一格的點擊就會被吞掉（有時有反應、有時沒反應）。
        bool _pendingPressed, _pendingAction;
        int _pendingNumber;
        float _pendingX, _pendingY; // 按下去那一刻的滑鼠位置（點擊要落在你按的地方，不是下一步時滑鼠移到的地方）

        public void Step(float dt, MiniInput input)
        {
            if (State != MiniState.Running) return;
            if (input.Pressed) { _pendingX = input.PX; _pendingY = input.PY; }
            _pendingPressed |= input.Pressed;
            _pendingAction |= input.Action;
            if (input.Number != 0) _pendingNumber = input.Number;
            _acc += Math.Min(dt, 0.1f);
            while (_acc >= FixedDt && State == MiniState.Running)
            {
                _acc -= FixedDt;
                Time += FixedDt;
                if (_pendingPressed) { input.PX = _pendingX; input.PY = _pendingY; }
                input.Pressed = _pendingPressed;
                input.Action = _pendingAction;
                input.Number = _pendingNumber;
                _pendingPressed = _pendingAction = false;
                _pendingNumber = 0;
                Update(FixedDt, input);
            }
        }

        /// <summary>玩家中途放棄：算失敗。</summary>
        public void Forfeit() => Lose();

        protected void Win() { if (State == MiniState.Running) State = MiniState.Won; }
        protected void Lose() { if (State == MiniState.Running) State = MiniState.Lost; }

        // ---- 繪圖小工具
        protected static void Rect(List<MiniShape> s, float x, float y, float w, float h, uint color, float alpha = 1, float rot = 0) =>
            s.Add(new MiniShape { Kind = MiniShapeKind.Rect, X = x, Y = y, A = w, B = h, Color = color, Alpha = alpha, Rot = rot });
        protected static void Circle(List<MiniShape> s, float x, float y, float r, uint color, float alpha = 1) =>
            s.Add(new MiniShape { Kind = MiniShapeKind.Circle, X = x, Y = y, A = r, Color = color, Alpha = alpha });
        protected static void Ring(List<MiniShape> s, float x, float y, float r, float width, uint color, float alpha = 1) =>
            s.Add(new MiniShape { Kind = MiniShapeKind.Ring, X = x, Y = y, A = r, W = width, Color = color, Alpha = alpha });
        protected static void Line(List<MiniShape> s, float x1, float y1, float x2, float y2, float width, uint color, float alpha = 1) =>
            s.Add(new MiniShape { Kind = MiniShapeKind.Line, X = x1, Y = y1, A = x2, B = y2, W = width, Color = color, Alpha = alpha });
        protected static void Tri(List<MiniShape> s, float x, float y, float size, float rot, uint color, float alpha = 1) =>
            s.Add(new MiniShape { Kind = MiniShapeKind.Tri, X = x, Y = y, A = size, Rot = rot, Color = color, Alpha = alpha });
        /// <summary>畫一張貼圖。key = "遊戲/名字"；tint 預設白色 = 不改色。</summary>
        protected static void Spr(List<MiniShape> s, string key, float x, float y, float w, float h, float rot = 0, float alpha = 1, uint tint = 0xFFFFFF) =>
            s.Add(new MiniShape { Kind = MiniShapeKind.Sprite, X = x, Y = y, A = w, B = h, Rot = rot, Str = key, Color = tint, Alpha = alpha });
        protected static void Text(List<MiniShape> s, float x, float y, float size, string str, uint color, float alpha = 1) =>
            s.Add(new MiniShape { Kind = MiniShapeKind.Text, X = x, Y = y, A = size, Str = str, Color = color, Alpha = alpha });

        protected static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        protected float Rand() => (float)Rng.NextDouble();
        protected float Rand(float lo, float hi) => lo + (hi - lo) * (float)Rng.NextDouble();
    }

    /// <summary>所有小遊戲的登記處：用反射找出標了 <see cref="MiniGameAttribute"/> 的類別，新增遊戲不用改這裡。</summary>
    public static class Minigames
    {
        static List<MinigameDef> _all;

        public static IReadOnlyList<MinigameDef> All
        {
            get
            {
                if (_all != null) return _all;
                var list = new List<MinigameDef>();
                foreach (var t in typeof(MiniGame).Assembly.GetTypes())
                {
                    if (t.IsAbstract || !typeof(MiniGame).IsAssignableFrom(t)) continue;
                    var a = t.GetCustomAttribute<MiniGameAttribute>();
                    if (a == null) continue;
                    list.Add(new MinigameDef { Id = a.Id, Kind = a.Kind, Rule = a.Rule, PassScore = a.PassScore, RatioNeeded = a.RatioNeeded, Type = t });
                }
                _all = list.OrderBy(d => (int)d.Kind).ThenBy(d => d.Id, StringComparer.Ordinal).ToList();
                return _all;
            }
        }

        public static MinigameDef Def(string id) => All.FirstOrDefault(d => d.Id == id);

        /// <summary>某種事件對應的小遊戲（沒有就是 null）。</summary>
        public static MinigameDef For(EventKind kind) => All.FirstOrDefault(d => d.Kind == kind);

        /// <summary>用事件的 key（solar_storm…）找；key 不認得、或這種事件沒有小遊戲就是 null。</summary>
        public static MinigameDef For(string eventKey)
        {
            var k = OpsEvents.Parse(eventKey);
            return k.HasValue ? For(k.Value) : null;
        }

        public static MiniGame Create(string id, MinigameContext ctx, int seed)
        {
            var def = Def(id);
            if (def == null) return null;
            var g = (MiniGame)Activator.CreateInstance(def.Type);
            g.Setup(def, ctx, seed);
            return g;
        }
    }

    /// <summary>營運小遊戲的總開關（false = 回到舊的「在控制台按指令」流程，除錯用）。</summary>
    public static class OpsMinigames
    {
        public static bool Enabled = true;
    }

    /// <summary>多人時怎麼算過關。</summary>
    public static class MinigameRules
    {
        /// <summary>單一玩家的分數是否通關。</summary>
        public static bool PlayerPassed(MinigameDef def, int score) => def != null && score >= def.PassScore;

        /// <summary>
        /// 全隊有沒有過。scores = 已經交出成績的玩家分數；players = 總人數（沒交的算 0 分、沒通關）。
        /// </summary>
        public static bool TeamPassed(MinigameDef def, IEnumerable<int> scores, int players)
        {
            if (def == null) return false;
            players = Math.Max(1, players);
            var list = (scores ?? Enumerable.Empty<int>()).ToList();
            if (def.Rule == MiniRule.Sum) return list.Sum() / (double)players >= def.PassScore;
            int passed = list.Count(s => s >= def.PassScore);
            int need = Math.Max(1, (int)Math.Ceiling(def.RatioNeeded * players - 1e-9));
            return passed >= need;
        }
    }
}
