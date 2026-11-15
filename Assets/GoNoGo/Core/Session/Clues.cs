using System;
using System.Collections.Generic;
using System.Linq;

namespace GoNoGo.Core
{
    /// <summary>線索的呈現方式（不分難度，四種都會出現）。</summary>
    public enum ClueKind
    {
        /// <summary>規格表。</summary>
        Table,
        /// <summary>一段敘述：測試紀錄、工程師筆記裡提到的數字。</summary>
        Note,
        /// <summary>圖表：從長條圖讀出數值。</summary>
        Chart,
        /// <summary>設計圖上標註的數字。</summary>
        Drawing,
    }

    /// <summary>一則線索：揭露一個零件的幾個欄位。內容由模板從 parts.json 產生，不是一則一則手寫的。</summary>
    public sealed class Clue
    {
        public string Id { get; set; } = "";
        public string PartId { get; set; } = "";
        public List<string> Fields { get; set; } = new List<string>();
        public ClueKind Kind { get; set; }
        /// <summary>同一種呈現方式的第幾個模板（敘述有好幾種寫法）。</summary>
        public int Variant { get; set; }
    }

    /// <summary>場景裡放線索的一個位置。場景建置（FacilityBuilder）照這張表擺東西，id 要對得上。</summary>
    public sealed class ClueSpotDef
    {
        public string Id { get; }
        /// <summary>control（中控室）或 bay（組裝區）。</summary>
        public string Zone { get; }
        /// <summary>paper（草稿紙、便條）、whiteboard、blackboard、book（一本書）、blueprint（設計圖）。</summary>
        public string Medium { get; }
        /// <summary>房間裡的哪一塊（研究牆、書桌、休息區、組裝台旁…）。擺線索時先填滿不同的區塊，才不會擠在同一面牆上。</summary>
        public string Area { get; }
        /// <summary>顯眼、好找的位置（牆上的板子、書桌）：新手訓練的線索只放這些。</summary>
        public bool Obvious { get; }
        public ClueSpotDef(string id, string zone, string medium, string area, bool obvious = false)
        { Id = id; Zone = zone; Medium = medium; Area = area; Obvious = obvious; }
    }

    /// <summary>
    /// 線索：散落在中控室（研究牆、白板、黑板、書堆、機櫃、休息區、置物櫃、隔牆、地上）
    /// 和組裝區（設計圖、架子旁的便條、組裝台旁的夾板、整合台邊、工作台、儀器櫃、門邊、地上）。
    /// 內容每局固定（同一關、同一個難度、同樣的人數模式都一樣），擺放位置每局隨機（<see cref="SessionState.ClueSeed"/>）。
    /// 每一格挖空的數值都剛好出現在一則線索裡；只產生這一關能選的零件的線索。
    /// 玩家靠近按互動鍵放大才看得到內容，而且只有自己看得到，要靠口頭告訴隊友。
    /// </summary>
    public static class Clues
    {
        /// <summary>一則線索最多寫幾個欄位（一個零件挖空超過這個數，就拆成兩則）。</summary>
        public const int MaxFields = 3;
        /// <summary>敘述模板的數量（strings.json 的 ui.clue.note.0 …）。</summary>
        public const int NoteVariants = 4;
        /// <summary>
        /// 太空知識的數量（strings.json 的 ui.clue.trivia.0 …）：一部分沒放線索的位置放一則（見 <see cref="Trivia"/>）。
        /// 不能少於位置數，同一局每個位置才拿得到不同的一則。
        /// </summary>
        public const int TriviaCount = 64;

        public static readonly ClueSpotDef[] Spots =
        {
            // 中控室：研究牆上釘的草稿紙
            new ClueSpotDef("wall_1", "control", "paper", "research", true),
            new ClueSpotDef("wall_2", "control", "paper", "research", true),
            new ClueSpotDef("wall_3", "control", "paper", "research", true),
            new ClueSpotDef("wall_4", "control", "paper", "research", true),
            new ClueSpotDef("wall_5", "control", "paper", "research", true),
            new ClueSpotDef("wall_6", "control", "paper", "research", true),
            // 中控室：白板、黑板
            new ClueSpotDef("whiteboard", "control", "whiteboard", "whiteboard", true),
            new ClueSpotDef("blackboard", "control", "blackboard", "blackboard", true),
            // 中控室：書堆
            new ClueSpotDef("book_1", "control", "book", "books", true),
            new ClueSpotDef("book_2", "control", "book", "books", true),
            new ClueSpotDef("book_3", "control", "book", "books", true),
            new ClueSpotDef("book_4", "control", "book", "books", true),
            new ClueSpotDef("book_5", "control", "book", "books", true),
            // 中控室其他角落：機櫃和資料櫃上貼的紙、休息區茶几上的書和沙發上的紙、置物櫃門上的便條、
            // 隔牆上的設計圖和公告、前方兩側牆上的設計圖和便條、掉在地上的紙
            new ClueSpotDef("racknote", "control", "paper", "racks"),
            new ClueSpotDef("racknote_2", "control", "paper", "racks"),
            new ClueSpotDef("archivenote", "control", "paper", "archive"),
            new ClueSpotDef("archivenote_2", "control", "paper", "archive"),
            new ClueSpotDef("loungebook", "control", "book", "lounge"),
            new ClueSpotDef("sofanote", "control", "paper", "lounge"),
            new ClueSpotDef("lockernote", "control", "paper", "lockers"),
            new ClueSpotDef("lockernote_2", "control", "paper", "lockers"),
            new ClueSpotDef("divider_1", "control", "blueprint", "divider_n"),
            new ClueSpotDef("divider_2", "control", "paper", "divider_s"),
            new ClueSpotDef("frontprint", "control", "blueprint", "front_n"),
            new ClueSpotDef("frontnote", "control", "paper", "front_s"),
            new ClueSpotDef("floor_c1", "control", "paper", "floor_c1"),
            new ClueSpotDef("floor_c2", "control", "paper", "floor_c2"),
            // 組裝區：牆上的設計圖、架子旁的便條
            new ClueSpotDef("print_1", "bay", "blueprint", "westwall"),
            new ClueSpotDef("print_2", "bay", "blueprint", "westwall"),
            new ClueSpotDef("print_3", "bay", "blueprint", "shelf_w"),
            new ClueSpotDef("print_4", "bay", "blueprint", "shelf_e"),
            new ClueSpotDef("shelfnote_1", "bay", "paper", "shelves"),
            new ClueSpotDef("shelfnote_2", "bay", "paper", "shelves"),
            new ClueSpotDef("shelfnote_3", "bay", "paper", "shelves"),
            new ClueSpotDef("shelfnote_4", "bay", "paper", "shelves"),
            // 組裝區其他角落：兩座組裝台旁的夾板、整合台邊的夾板、南牆的設計圖、工作台上的書和紙、儀器櫃門上的紙、
            // 門邊貼的便條、掉在地上的紙
            new ClueSpotDef("benchnote_1", "bay", "paper", "bench_w"),
            new ClueSpotDef("benchnote_2", "bay", "paper", "bench_e"),
            new ClueSpotDef("standnote", "bay", "paper", "stand"),
            new ClueSpotDef("print_5", "bay", "blueprint", "southwall_w"),
            new ClueSpotDef("print_6", "bay", "blueprint", "southwall_e"),
            new ClueSpotDef("workbook", "bay", "book", "workbench"),
            new ClueSpotDef("benchpaper", "bay", "paper", "workbench"),
            new ClueSpotDef("cabinetnote", "bay", "paper", "cabinets"),
            new ClueSpotDef("cabinetnote_2", "bay", "paper", "cabinets"),
            new ClueSpotDef("doornote", "bay", "paper", "door"),
            new ClueSpotDef("floor_b1", "bay", "paper", "floor_b1"),
            new ClueSpotDef("floor_b2", "bay", "paper", "floor_b2"),
        };

        static Dictionary<string, ClueSpotDef> _spotById;

        public static ClueSpotDef Spot(string id)
        {
            if (id == null) return null;
            if (_spotById == null)
            {
                // 表是固定的，查表只建一次；id 重複時留第一個（和照順序找一樣）
                var map = new Dictionary<string, ClueSpotDef>(Spots.Length);
                foreach (var x in Spots)
                    if (!map.ContainsKey(x.Id)) map[x.Id] = x;
                _spotById = map;
            }
            return _spotById.TryGetValue(id, out var spot) ? spot : null;
        }

        /// <summary>這種位置放不放得下這種呈現方式（設計圖只畫圖和表，書不會是設計圖…）。</summary>
        public static bool Accepts(string medium, ClueKind kind)
        {
            switch (medium)
            {
                case "paper": return kind == ClueKind.Table || kind == ClueKind.Note || kind == ClueKind.Chart;
                case "whiteboard": return kind == ClueKind.Chart || kind == ClueKind.Note || kind == ClueKind.Table;
                case "blackboard": return kind == ClueKind.Drawing || kind == ClueKind.Chart || kind == ClueKind.Note;
                case "book": return kind == ClueKind.Table || kind == ClueKind.Note || kind == ClueKind.Chart;
                case "blueprint": return kind == ClueKind.Drawing || kind == ClueKind.Table;
                default: return false;
            }
        }

        static readonly ClueKind[] Kinds = { ClueKind.Table, ClueKind.Note, ClueKind.Chart, ClueKind.Drawing };

        /// <summary>
        /// 這一局的全部線索（內容固定）。每個有挖空的零件一則；挖空超過 <see cref="MaxFields"/> 格的拆成兩則。
        /// 呈現方式照順序輪流，所以四種都會出現。
        /// </summary>
        public static List<Clue> For(GameData data, SessionState s)
        {
            var list = new List<Clue>();
            var byPart = new Dictionary<string, List<string>>();
            var order = new List<string>();
            foreach (var (part, field) in Blanks.Cells(data, s))
            {
                if (!byPart.TryGetValue(part, out var fs)) { byPart[part] = fs = new List<string>(); order.Add(part); }
                fs.Add(field);
            }
            foreach (var part in order)
            {
                var fs = byPart[part];
                // 平均拆開（4 格 → 2 + 2，不是 3 + 1）
                int chunks = (fs.Count + MaxFields - 1) / MaxFields;
                int per = (fs.Count + chunks - 1) / chunks;
                for (int i = 0; i < fs.Count; i += per)
                {
                    int n = list.Count;
                    list.Add(new Clue
                    {
                        Id = "c" + n,
                        PartId = part,
                        Fields = fs.GetRange(i, Math.Min(per, fs.Count - i)),
                        Kind = Kinds[n % Kinds.Length],
                        Variant = (n / Kinds.Length) % NoteVariants,
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// 這一局每個位置放了哪些線索（位置 id → 線索，可能是空的）。照 <see cref="SessionState.ClueSeed"/> 打散：
        /// 每則線索放到「放得下這種呈現方式」的位置裡最空的那一個——先比位置上有幾則，再比那一塊區域有幾則，
        /// 再比那個房間有幾則。所以線索會散到兩個房間的各個角落，不會擠在同一面牆上。
        /// </summary>
        public static Dictionary<string, List<Clue>> Place(GameData data, SessionState s)
        {
            var map = Spots.ToDictionary(x => x.Id, x => new List<Clue>());
            var clues = For(data, s);
            if (clues.Count == 0) return map;
            var rng = new SeededShuffle(s.ClueSeed, 0x9E3779B9u);
            // 新手訓練：線索只放在中控室顯眼的地方（玩家還沒去過組裝區，第一件事就是找它）
            var spots = (s.Tutorial ? Spots.Where(x => x.Zone == "control" && x.Obvious) : Spots).ToList();
            rng.Shuffle(spots);
            rng.Shuffle(clues);
            var perArea = new Dictionary<string, int>();
            var perZone = new Dictionary<string, int>();
            int Count(Dictionary<string, int> d, string k) => d.TryGetValue(k, out var n) ? n : 0;
            foreach (var c in clues)
            {
                ClueSpotDef best = null;
                (int, int, int) bestKey = default;
                foreach (var sp in spots)
                {
                    if (!Accepts(sp.Medium, c.Kind)) continue;
                    var key = (map[sp.Id].Count, Count(perArea, sp.Area), Count(perZone, sp.Zone));
                    if (best == null || key.CompareTo(bestKey) < 0) { best = sp; bestKey = key; }
                }
                map[best.Id].Add(c);
                perArea[best.Area] = Count(perArea, best.Area) + 1;
                perZone[best.Zone] = Count(perZone, best.Zone) + 1;
            }
            return map;
        }

        /// <summary>
        /// 這一局哪些沒放線索的位置放太空知識、放哪一則（位置 id → 第幾則）。放知識的位置和放線索的位置一樣多，
        /// 找線索時不會被一大堆知識淹沒；其他空位置這一局什麼都不放。同一局不重複；照 <see cref="SessionState.ClueSeed"/> 打散，
        /// 每局不一樣，同一個種子每台電腦算出來都一樣。<paramref name="placed"/> 是 <see cref="Place"/> 的結果。
        /// </summary>
        public static Dictionary<string, int> Trivia(Dictionary<string, List<Clue>> placed, int clueSeed)
        {
            var empty = Spots.Where(sp => !placed.TryGetValue(sp.Id, out var here) || here.Count == 0).Select(sp => sp.Id).ToList();
            int withClues = Spots.Length - empty.Count;
            var order = Enumerable.Range(0, TriviaCount).ToList();
            var rng = new SeededShuffle(clueSeed, 0x7F4A7C15u);
            rng.Shuffle(empty);
            rng.Shuffle(order);
            var map = new Dictionary<string, int>();
            for (int i = 0; i < Math.Min(withClues, empty.Count); i++) map[empty[i]] = order[i];
            return map;
        }

        /// <summary>照種子打散清單（線性同餘）。C# 的 Random 在不同 .NET 版本可能不一樣，多人時每台電腦要算出同一個結果，所以自己寫。</summary>
        sealed class SeededShuffle
        {
            uint _state;

            public SeededShuffle(int seed, uint salt) => _state = unchecked((uint)seed * 2654435761u + salt);

            uint Next()
            {
                _state = unchecked(_state * 1664525 + 1013904223);
                return _state >> 8;
            }

            public void Shuffle<T>(IList<T> xs)
            {
                for (int i = xs.Count - 1; i > 0; i--)
                {
                    int j = (int)(Next() % (uint)(i + 1));
                    (xs[i], xs[j]) = (xs[j], xs[i]);
                }
            }
        }

        /// <summary>這個位置這一局放的線索。</summary>
        public static List<Clue> At(GameData data, SessionState s, string spotId) =>
            Place(data, s).TryGetValue(spotId ?? "", out var list) ? list : new List<Clue>();
    }
}
