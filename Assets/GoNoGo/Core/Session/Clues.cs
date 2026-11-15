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
        /// <summary>paper（釘在牆上的草稿紙）、whiteboard、blackboard、book（書堆裡的一本書）、blueprint（牆上的設計圖）。</summary>
        public string Medium { get; }
        public ClueSpotDef(string id, string zone, string medium) { Id = id; Zone = zone; Medium = medium; }
    }

    /// <summary>
    /// 線索：放在中控室（研究牆、白板、黑板、書堆）和組裝區（設計圖、架子旁的便條）。
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

        public static readonly ClueSpotDef[] Spots =
        {
            // 中控室：研究牆上釘的草稿紙
            new ClueSpotDef("wall_1", "control", "paper"),
            new ClueSpotDef("wall_2", "control", "paper"),
            new ClueSpotDef("wall_3", "control", "paper"),
            new ClueSpotDef("wall_4", "control", "paper"),
            new ClueSpotDef("wall_5", "control", "paper"),
            new ClueSpotDef("wall_6", "control", "paper"),
            // 中控室：白板、黑板
            new ClueSpotDef("whiteboard", "control", "whiteboard"),
            new ClueSpotDef("blackboard", "control", "blackboard"),
            // 中控室：書堆
            new ClueSpotDef("book_1", "control", "book"),
            new ClueSpotDef("book_2", "control", "book"),
            new ClueSpotDef("book_3", "control", "book"),
            new ClueSpotDef("book_4", "control", "book"),
            new ClueSpotDef("book_5", "control", "book"),
            // 組裝區：牆上的設計圖、架子旁的便條
            new ClueSpotDef("print_1", "bay", "blueprint"),
            new ClueSpotDef("print_2", "bay", "blueprint"),
            new ClueSpotDef("print_3", "bay", "blueprint"),
            new ClueSpotDef("print_4", "bay", "blueprint"),
            new ClueSpotDef("shelfnote_1", "bay", "paper"),
            new ClueSpotDef("shelfnote_2", "bay", "paper"),
            new ClueSpotDef("shelfnote_3", "bay", "paper"),
        };

        public static ClueSpotDef Spot(string id) => Array.Find(Spots, x => x.Id == id);

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
                        Fields = fs.Skip(i).Take(per).ToList(),
                        Kind = Kinds[n % Kinds.Length],
                        Variant = (n / Kinds.Length) % NoteVariants,
                    });
                }
            }
            return list;
        }

        /// <summary>
        /// 這一局每個位置放了哪些線索（位置 id → 線索，可能是空的）。照 <see cref="SessionState.ClueSeed"/> 打散：
        /// 每則線索放到「放得下這種呈現方式、目前線索最少」的位置，所以會平均分散在兩個房間。
        /// </summary>
        public static Dictionary<string, List<Clue>> Place(GameData data, SessionState s)
        {
            var map = Spots.ToDictionary(x => x.Id, x => new List<Clue>());
            var clues = For(data, s);
            if (clues.Count == 0) return map;
            uint seed = unchecked((uint)s.ClueSeed * 2654435761u + 0x9E3779B9u);
            uint Next()
            {
                seed = unchecked(seed * 1664525 + 1013904223);
                return seed >> 8;
            }
            void Shuffle<T>(IList<T> xs)
            {
                for (int i = xs.Count - 1; i > 0; i--)
                {
                    int j = (int)(Next() % (uint)(i + 1));
                    (xs[i], xs[j]) = (xs[j], xs[i]);
                }
            }
            // 新手訓練：線索只放在中控室（玩家還沒去過組裝區，第一件事就是找它）
            var spots = (s.Tutorial ? Spots.Where(x => x.Zone == "control") : Spots).ToList();
            Shuffle(spots);
            Shuffle(clues);
            foreach (var c in clues)
            {
                ClueSpotDef best = null;
                foreach (var sp in spots)
                {
                    if (!Accepts(sp.Medium, c.Kind)) continue;
                    if (best == null || map[sp.Id].Count < map[best.Id].Count) best = sp;
                }
                map[best.Id].Add(c);
            }
            return map;
        }

        /// <summary>這個位置這一局放的線索。</summary>
        public static List<Clue> At(GameData data, SessionState s, string spotId) =>
            Place(data, s).TryGetValue(spotId ?? "", out var list) ? list : new List<Clue>();
    }
}
