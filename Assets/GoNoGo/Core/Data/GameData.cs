using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace GoNoGo.Core
{
    /// <summary>所有遊戲資料（從 Data/*.json 載入）。核心計算只讀這裡。</summary>
    public sealed class GameData
    {
        public Constants Constants { get; set; } = new Constants();
        public List<PartDef> Parts { get; set; } = new List<PartDef>();
        public List<OrbitDef> Orbits { get; set; } = new List<OrbitDef>();
        public List<LauncherDef> Launchers { get; set; } = new List<LauncherDef>();
        public List<ScenarioDef> Scenarios { get; set; } = new List<ScenarioDef>();
        public List<DifficultyDef> Difficulties { get; set; } = new List<DifficultyDef>();
        public List<SpaceWeatherEvent> SpaceWeather { get; set; } = new List<SpaceWeatherEvent>();
        public List<MissionCard> Missions { get; set; } = new List<MissionCard>();
        /// <summary>通用的取捨說明（關卡沒有自己寫的時候用）。</summary>
        public List<TradeoffNote> Tradeoffs { get; set; } = new List<TradeoffNote>();
        public Dictionary<string, LocalizedText> Strings { get; set; } = new Dictionary<string, LocalizedText>();
        /// <summary>組裝台的配方（小零件 → 零件）。</summary>
        public AssemblyData Assemblies { get; set; } = new AssemblyData();
        /// <summary>沙盒的情境與難度（不在 <see cref="Scenarios"/>、<see cref="Difficulties"/> 裡，用 id 查得到）。</summary>
        public SandboxData Sandbox { get; set; }

        Dictionary<string, PartDef> _parts;
        Dictionary<string, OrbitDef> _orbits;
        Dictionary<string, LauncherDef> _launchers;
        Dictionary<string, ScenarioDef> _scenarios;
        Dictionary<string, DifficultyDef> _difficulties;
        Dictionary<string, AssemblyComponent> _components;

        public void BuildIndex()
        {
            _parts = Parts.ToDictionary(p => p.Id);
            _orbits = Orbits.ToDictionary(o => o.Id);
            _launchers = Launchers.ToDictionary(l => l.Id);
            _scenarios = Scenarios.ToDictionary(s => s.Id);
            _difficulties = Difficulties.ToDictionary(d => d.Id);
            _components = new Dictionary<string, AssemblyComponent>();
            foreach (var c in (Assemblies?.Recipes ?? new List<AssemblyRecipe>()).SelectMany(r => r.Components))
                if (!_components.ContainsKey(c.Id)) _components[c.Id] = c;
            if (Sandbox != null)
            {
                _scenarios[Sandbox.Scenario.Id] = Sandbox.Scenario;
                _difficulties[Sandbox.Difficulty.Id] = Sandbox.Difficulty;
            }
        }

        /// <summary>
        /// 同一份資料、換一批零件的複本（其他都共用）。大屏幕的儀表板用「玩家填的數值」跑評估時用這個，
        /// 原本的資料（真實數值）不會被動到。
        /// </summary>
        public GameData WithParts(List<PartDef> parts)
        {
            var d = (GameData)MemberwiseClone();
            d.Parts = parts;
            d._shelf = null;
            d.BuildIndex();
            return d;
        }

        /// <summary>所有情境，含沙盒（資料檢查用；選單只列 <see cref="Scenarios"/>）。</summary>
        public IEnumerable<ScenarioDef> AllScenarios => Sandbox != null ? Scenarios.Concat(new[] { Sandbox.Scenario }) : Scenarios;
        public IEnumerable<DifficultyDef> AllDifficulties => Sandbox != null ? Difficulties.Concat(new[] { Sandbox.Difficulty }) : Difficulties;

        /// <summary>
        /// 這個零件的組裝配方：先找指定這個零件的，再找這一類通用的。
        /// null = 不用組裝（地面站、封板這類沒有小零件的東西）。
        /// </summary>
        public AssemblyRecipe Recipe(PartDef part)
        {
            if (part == null || Assemblies == null || Assemblies.Prebuilt.Contains(part.Id)) return null;
            return Assemblies.Recipes.Find(r => r.PartIds.Contains(part.Id))
                   ?? Assemblies.Recipes.Find(r => r.PartIds.Count == 0 && r.Category == part.Category);
        }

        /// <summary>小零件：依 id 查名稱和功能；找不到是 null。</summary>
        public AssemblyComponent Component(string id) =>
            id != null && _components != null && _components.TryGetValue(id, out var c) ? c : null;

        /// <summary>單人的籃子一次裝得下幾個小零件。配方的小零件數不超過這個數，一趟拿得齊一個零件要用的。</summary>
        public const int BasketSize = 4;
        /// <summary>組裝台左側最多擺幾個小零件（比配方多兩格：放錯了還有地方調整）。</summary>
        public const int BenchSlots = 6;
        /// <summary>組裝台的數量（不分配，誰都能用）。</summary>
        public const int BenchCount = 2;

        List<AssemblyComponent> _shelf;

        /// <summary>
        /// 小零件架上的每一格（一格一種小零件，不限數量）：所有配方用到的小零件都在，包括這次設計用不到的。
        /// 順序照固定的方式打散（每一局、每台電腦都一樣）：同一個配方的小零件不會排在一起，架子本身不提示該拿什麼。
        /// </summary>
        public List<AssemblyComponent> ShelfComponents()
        {
            if (_shelf != null) return _shelf;
            var list = (Assemblies?.Recipes ?? new List<AssemblyRecipe>()).SelectMany(r => r.Components)
                .GroupBy(c => c.Id).Select(g => g.First()).OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
            // 自己寫的亂數（不用 System.Random：不同執行環境的實作可能不一樣）
            uint seed = 2166136261;
            for (int i = list.Count - 1; i > 0; i--)
            {
                seed = unchecked(seed * 1664525 + 1013904223);
                int j = (int)((seed >> 8) % (uint)(i + 1));
                (list[i], list[j]) = (list[j], list[i]);
            }
            return _shelf = list;
        }

        /// <summary>小零件組合剛好是這幾個的配方（不看順序）；沒有 = null。</summary>
        public AssemblyRecipe RecipeOf(ICollection<string> componentIds)
        {
            if (Assemblies == null || componentIds == null || componentIds.Count == 0) return null;
            return Assemblies.Recipes.Find(r => r.Components.Count == componentIds.Count && r.Components.All(c => componentIds.Contains(c.Id)));
        }

        /// <summary>這幾個小零件是不是某個配方的一部分（還沒放齊）。</summary>
        public bool PartOfRecipe(ICollection<string> componentIds)
        {
            if (Assemblies == null || componentIds == null) return false;
            return Assemblies.Recipes.Any(r => componentIds.All(id => r.Components.Any(c => c.Id == id)));
        }

        public PartDef Part(string id) => id != null && _parts.TryGetValue(id, out var p) ? p : null;
        public OrbitDef Orbit(string id) => id != null && _orbits.TryGetValue(id, out var o) ? o : null;
        public LauncherDef Launcher(string id) => id != null && _launchers.TryGetValue(id, out var l) ? l : null;
        public ScenarioDef Scenario(string id) => id != null && _scenarios.TryGetValue(id, out var s) ? s : null;
        public DifficultyDef Difficulty(string id) => id != null && _difficulties.TryGetValue(id, out var d) ? d : null;
        /// <summary>
        /// 沒指定難度時用的基準難度（工程師：預算倍率 1）。不照 difficulty.json 的順序取第一個：
        /// 那個順序是選單的排列（由易到難），調整選單不應該改到評估和模擬的基準。
        /// </summary>
        public DifficultyDef DefaultDifficulty => Difficulty("engineer") ?? Difficulties.FirstOrDefault();
        /// <summary>選單上可以選的難度（不含新手訓練專用的）。</summary>
        public IEnumerable<DifficultyDef> SelectableDifficulties => Difficulties.Where(d => !d.Hidden);

        /// <summary>零件編號 → 零件。</summary>
        public PartDef PartByCode(string code) => Parts.Find(p => p.Code == code);

        public IEnumerable<PartDef> PartsIn(string category) =>
            Parts.Where(p => p.Category == category).OrderBy(p => p.Tier).ThenBy(p => p.Id, StringComparer.Ordinal);

        /// <summary>這個情境可用的火箭（歷史關卡另外加入歷史火箭）。</summary>
        public IEnumerable<LauncherDef> LaunchersFor(ScenarioDef s)
        {
            var allowed = s?.AllowedLaunchers;
            return Launchers
                .Where(l => allowed != null && allowed.Count > 0 ? allowed.Contains(l.Id) : !l.Historical)
                // 到不了這個情境任何一條軌道的火箭（例如只去晨昏軌道的共乘）不列出
                .Where(l => s == null || s.AllowedOrbits.Count == 0 || s.AllowedOrbits.Any(l.CapacityKg.ContainsKey))
                .OrderBy(l => l.Tier).ThenBy(l => l.Id, StringComparer.Ordinal);
        }

        public IEnumerable<OrbitDef> OrbitsFor(ScenarioDef s) =>
            Orbits.Where(o => s == null || s.AllowedOrbits.Count == 0 || s.AllowedOrbits.Contains(o.Id));

        public string Text(string key, string lang) =>
            Strings.TryGetValue(key, out var t) ? t.Get(lang) : key;

        // ---------------------------------------------------------------- 關卡覆蓋難度的設定

        static T ByDifficulty<T>(Dictionary<string, T> map, string difficultyId) where T : class =>
            map == null ? null
            : difficultyId != null && map.TryGetValue(difficultyId, out var v) ? v
            : map.TryGetValue("*", out var all) ? all : null;

        /// <summary>這一關固定的難度（一關一個難度）。關卡沒寫就用基準難度。</summary>
        public DifficultyDef DifficultyFor(ScenarioDef s) => Difficulty(s?.Difficulty) ?? DefaultDifficulty;

        /// <summary>這一關的預算（M$）。難度倍率已經併進 budget_musd；歷史關卡再乘上當年的預算上限比例。</summary>
        public static double Budget(ScenarioDef s) => (s?.BudgetMusd ?? 0) * (s?.Historical?.BudgetCap ?? 1.0);

        /// <summary>
        /// 營運時會抽到的太陽風暴。一般關卡用整個目錄（過去的真實事件當例子）；
        /// 歷史關卡只用任務期間（當年起算、任務年數內）真的發生過的事件，不會在 2009 年的任務裡出現 2024 年的風暴。
        /// 任務期間一個都沒有時才退回整個目錄。
        /// </summary>
        public List<SpaceWeatherEvent> StormsDuring(ScenarioDef s, int years)
        {
            if (s?.Historical == null || s.Historical.Year <= 0) return SpaceWeather;
            int from = s.Historical.Year, to = s.Historical.Year + Math.Max(1, years);
            var list = SpaceWeather.Where(e => StormYear(e) >= from && StormYear(e) <= to).ToList();
            return list.Count > 0 ? list : SpaceWeather;
        }

        /// <summary>手冊列得出來的事件：歷史關卡只列到任務結束那一年為止（不列「未來」的事件）。</summary>
        public List<SpaceWeatherEvent> StormsKnownBy(ScenarioDef s, int years)
        {
            if (s?.Historical == null || s.Historical.Year <= 0) return SpaceWeather;
            int to = s.Historical.Year + Math.Max(1, years);
            var list = SpaceWeather.Where(e => StormYear(e) <= to).ToList();
            return list.Count > 0 ? list : SpaceWeather;
        }

        static int StormYear(SpaceWeatherEvent e) =>
            e?.Date != null && e.Date.Length >= 4 && int.TryParse(e.Date.Substring(0, 4), out var y) ? y : 0;

        /// <summary>這支火箭飛不飛得到這個軌道（運力表上有這個軌道、而且大於 0）。</summary>
        public static bool Reaches(LauncherDef l, string orbitId) =>
            l != null && orbitId != null && l.CapacityKg.TryGetValue(orbitId, out var cap) && cap > 0;

        /// <summary>這個關卡、這個難度會出現的營運事件。</summary>
        public static List<string> EventTypes(ScenarioDef s, DifficultyDef d) =>
            ByDifficulty(s?.EventTypes, d?.Id) ?? d?.EventTypes ?? new List<string>();

        /// <summary>
        /// 這一關大屏幕上列得出來的零件（不含地面站）：關卡有指定就照指定，否則依難度開放的等級；
        /// 再加上這個難度開放類別的誘餌零件。
        /// </summary>
        public IEnumerable<PartDef> ShelfFor(ScenarioDef s, DifficultyDef d)
        {
            bool InScenario(PartDef p) => p.Scenarios.Count == 0 || (s != null && p.Scenarios.Contains(s.Id));
            var list = ByDifficulty(s?.Shelf, d?.Id);
            int tiers = d?.TiersPerCategory ?? 3;
            var regular = list != null
                ? list.Select(Part).Where(p => p != null && p.Category != PartCategory.GroundStation)
                : Parts.Where(p => !p.Decoy && p.Category != PartCategory.GroundStation && p.Tier <= tiers && InScenario(p));
            var decoys = Parts.Where(p => p.Decoy && d != null && d.DecoyCategories.Contains(p.Category) && InScenario(p));
            return regular.Concat(decoys).Distinct();
        }

        /// <summary>這一關、這個難度列在大屏幕上的取捨說明（關卡有自己寫的就用關卡的，否則用通用的）。</summary>
        public List<TradeoffNote> TradeoffsFor(ScenarioDef s, DifficultyDef d)
        {
            var list = s != null && s.Tradeoffs.Count > 0 ? s.Tradeoffs : Tradeoffs;
            int conflicts = d?.RequirementConflicts ?? 0;
            return list.Where(t => t.MinConflicts <= conflicts).ToList();
        }

        /// <summary>開局設計：關卡可以依難度指定。</summary>
        public static Design DefaultDesign(ScenarioDef s, string difficultyId) =>
            ByDifficulty(s?.DefaultDesigns, difficultyId) ?? s?.DefaultDesign ?? new Design();
    }

    public static class GameDataLoader
    {
        public static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver { NamingStrategy = new SnakeCaseNamingStrategy() },
            MissingMemberHandling = MissingMemberHandling.Error,
            NullValueHandling = NullValueHandling.Ignore,
        };

        /// <summary>資料檔名（不含副檔名）。</summary>
        public static readonly string[] FileNames =
        {
            "constants", "parts", "orbits", "launchers", "scenarios", "difficulty", "donki_events", "missions", "tradeoffs", "strings",
            "assemblies", "sandbox"
        };

        /// <summary>
        /// 從「檔名 → JSON 文字」載入。遊戲從 Resources 讀，測試從磁碟讀。
        /// 欄位拼錯會直接丟例外（MissingMemberHandling.Error），避免數值悄悄變 0。
        /// </summary>
        public static GameData Load(IDictionary<string, string> files)
        {
            T Read<T>(string name)
            {
                if (!files.TryGetValue(name, out var json))
                    throw new ArgumentException($"缺少資料檔 {name}.json");
                try { return JsonConvert.DeserializeObject<T>(json, Settings); }
                catch (JsonException e) { throw new FormatException($"{name}.json 格式錯誤：{e.Message}", e); }
            }

            var d = new GameData
            {
                Constants = Read<Constants>("constants"),
                Parts = Read<List<PartDef>>("parts"),
                Orbits = Read<List<OrbitDef>>("orbits"),
                Launchers = Read<List<LauncherDef>>("launchers"),
                Scenarios = Read<List<ScenarioDef>>("scenarios"),
                Difficulties = Read<List<DifficultyDef>>("difficulty"),
                SpaceWeather = Read<List<SpaceWeatherEvent>>("donki_events"),
                Missions = Read<List<MissionCard>>("missions"),
                Tradeoffs = Read<List<TradeoffNote>>("tradeoffs"),
                Strings = Read<Dictionary<string, LocalizedText>>("strings"),
                Assemblies = Read<AssemblyData>("assemblies"),
                Sandbox = Read<SandboxData>("sandbox"),
            };
            d.BuildIndex();
            return d;
        }

        /// <summary>檢查資料完整性。回傳所有問題；空清單代表通過。</summary>
        public static List<string> Validate(GameData d)
        {
            var errors = new List<string>();
            void Check(bool ok, string msg) { if (!ok) errors.Add(msg); }

            foreach (var g in d.Parts.GroupBy(p => p.Id).Where(g => g.Count() > 1)) errors.Add($"零件 id 重複：{g.Key}");

            foreach (var p in d.Parts)
            {
                string w = $"零件 {p.Id}";
                Check(PartCategory.All.Contains(p.Category), $"{w}：未知類別 {p.Category}");
                Check(!string.IsNullOrEmpty(p.Name.En) && !string.IsNullOrEmpty(p.Name.ZhTW), $"{w}：缺中英文名稱");
                Check(p.CostMusd >= 0 && p.MassKg >= 0 && p.PowerW >= 0, $"{w}：數值不可為負");
                Check(p.ReliabilityPerYear > 0 && p.ReliabilityPerYear <= 1, $"{w}：可靠度必須在 (0,1]");
                Check(!string.IsNullOrEmpty(p.Source?.Title), $"{w}：缺來源");
                if (p.Category != PartCategory.GroundStation)
                {
                    Check(!string.IsNullOrEmpty(p.Code), $"{w}：缺零件編號");
                    Check(!string.IsNullOrEmpty(p.Function?.En) && !string.IsNullOrEmpty(p.Function?.ZhTW), $"{w}：缺白話功能");
                }
                switch (p.Category)
                {
                    case PartCategory.Instrument:
                        Check(p.MassKg > 0 && p.PowerW > 0 && p.DataGbPerDay > 0 && p.ScienceValue > 0, $"{w}：儀器缺質量/耗電/資料量/科學值");
                        Check(p.ObjectiveMatch.Count > 0, $"{w}：缺任務目標吻合度");
                        break;
                    case PartCategory.Solar:
                        Check(p.AreaM2 > 0 && p.MassKg > 0, $"{w}：太陽能板缺面積或質量");
                        Check(p.CellFactor > 0 && p.CellFactor <= 1, $"{w}：電池效率倍率必須在 (0,1]");
                        break;
                    case PartCategory.Battery: Check(p.CapacityWh > 0 && p.MassKg > 0, $"{w}：電池缺容量或質量"); break;
                    case PartCategory.Antenna:
                        Check(p.DownlinkMbps > 0 && p.TxPowerW > 0, $"{w}：天線缺速率或發射功率");
                        Check(p.LinkAvailability > 0 && p.LinkAvailability <= 1, $"{w}：連線可用比例必須在 (0,1]");
                        break;
                    case PartCategory.GroundStation: Check(p.Stations > 0, $"{w}：地面站數量必須 > 0"); break;
                    case PartCategory.Computer: Check(p.StorageGb > 0 && p.RadToleranceKrad > 0, $"{w}：電腦缺儲存或耐輻射"); break;
                    case PartCategory.Propulsion: Check(p.PropellantKg == 0 || p.IspS > 0, $"{w}：有推進劑就必須有比衝"); break;
                    case PartCategory.Shielding: Check(p.DoseFactor > 0 && p.DoseFactor <= 1, $"{w}：劑量倍率必須在 (0,1]"); break;
                }
            }

            foreach (var g in d.Parts.Where(p => !string.IsNullOrEmpty(p.Code)).GroupBy(p => p.Code).Where(g => g.Count() > 1))
                errors.Add($"零件編號重複：{g.Key}");

            foreach (var o in d.Orbits)
            {
                string w = $"軌道 {o.Id}";
                Check(o.AltitudeKm > 0, $"{w}：高度必須 > 0");
                Check(o.Body == "earth" || o.Body == "moon", $"{w}：未知天體 {o.Body}");
                Check(o.BatteryDod > 0 && o.BatteryDod <= 1, $"{w}：放電深度必須在 (0,1]");
                Check(o.ContactFraction > 0 || (o.PassesPerDayPerStation > 0 && o.PassMinutes > 0), $"{w}：缺通聯設定");
                Check(!string.IsNullOrEmpty(o.Source?.Title), $"{w}：缺來源");
            }

            foreach (var l in d.Launchers)
            {
                string w = $"火箭 {l.Id}";
                Check(l.CapacityKg.Count > 0, $"{w}：沒有任何軌道運力");
                foreach (var k in l.CapacityKg.Keys) Check(d.Orbit(k) != null, $"{w}：運力表有未知軌道 {k}");
                if (!string.IsNullOrEmpty(l.OrbitLock))
                    Check(l.CapacityKg.Keys.All(k => k == l.OrbitLock), $"{w}：共乘只能有鎖定軌道的運力");
                Check(!string.IsNullOrEmpty(l.Source?.Title), $"{w}：缺來源");
            }

            foreach (var g in d.AllScenarios.GroupBy(s => s.Id).Where(g => g.Count() > 1)) errors.Add($"情境 id 重複：{g.Key}");
            Check(d.Scenarios.All(s => !s.Sandbox), "沙盒情境要放在 sandbox.json，不要放進 scenarios.json");
            if (d.Sandbox != null)
            {
                Check(d.Sandbox.Scenario.Sandbox, "sandbox.json 的情境要標 sandbox: true");
                Check(d.Sandbox.Scenario.Difficulty == d.Sandbox.Difficulty.Id, "sandbox.json 的情境要用同一個檔案裡的難度");
                Check(d.Sandbox.Difficulty.Hidden, "沙盒難度不出現在選單（hidden: true）");
                Check(d.Sandbox.Scenario.Requirements.Count == 0 && d.Sandbox.Scenario.Historical == null, "沙盒沒有任務需求，也不是歷史關卡");
                Check(d.Sandbox.Scenario.BudgetMusd >= ScenarioDef.NoLimit, "沙盒的預算要設成不限（≥ NoLimit）");
                Check(d.Sandbox.Difficulty.BlankRatio == 0 && d.Sandbox.Difficulty.BlankRatioSolo == 0 && d.Sandbox.Difficulty.BlankCells.Count == 0, "沙盒不挖空數值");
            }

            foreach (var s in d.AllScenarios)
            {
                string w = $"情境 {s.Id}";
                Check(s.BudgetMusd > 0 && s.TargetYears > 0, $"{w}：預算/年數必須 > 0");
                Check(string.IsNullOrEmpty(s.Difficulty) || (d.Difficulty(s.Difficulty) != null && (s.Sandbox || !d.Difficulty(s.Difficulty).Hidden)), $"{w}：未知難度 {s.Difficulty}");
                foreach (var o in s.AllowedOrbits) Check(d.Orbit(o) != null, $"{w}：未知軌道 {o}");
                foreach (var l in s.AllowedLaunchers) Check(d.Launcher(l) != null, $"{w}：未知火箭 {l}");
                errors.AddRange(ValidateDesign(d, s.DefaultDesign, $"{w} 預設設計"));
                foreach (var kv in s.DefaultDesigns) errors.AddRange(ValidateDesign(d, kv.Value, $"{w} {kv.Key} 開局設計"));
                foreach (var kv in s.Shelf)
                    foreach (var id in kv.Value)
                    {
                        Check(d.Part(id) != null && d.Part(id).Category != PartCategory.GroundStation, $"{w}：零件架有未知零件 {id}");
                        Check(d.Part(id) == null || !d.Part(id).Decoy, $"{w}：零件架不要直接列誘餌零件 {id}（由難度的 decoy_categories 決定）");
                    }
                foreach (var kv in s.EventTypes)
                    foreach (var k in kv.Value) Check(OpsEvents.Parse(k) != null, $"{w}：{kv.Key} 未知事件 {k}");
                errors.AddRange(ValidateTradeoffs(s.Tradeoffs, w));
                Check(s.ScienceModel == "" || s.ScienceModel == "fire_alert", $"{w}：未知科學算法 {s.ScienceModel}");
                foreach (var r in s.Requirements.Values.SelectMany(x => x))
                {
                    if (r.Type == "orbit_required") Check(s.AllowedOrbits.Contains(r.OrbitId), $"{w}：需求 {r.Id} 的軌道 {r.OrbitId} 不在允許清單");
                    if (r.Type == "instrument_any") Check(r.PartIds.Count > 0 && r.PartIds.All(id => d.Part(id)?.Category == PartCategory.Instrument), $"{w}：需求 {r.Id} 的儀器清單無效");
                }
                if (s.Historical != null)
                {
                    errors.AddRange(ValidateDesign(d, s.Historical.PresetDesign, $"{w} 歷史設計"));
                    Check(s.Historical.Sources.Count > 0, $"{w}：歷史關卡缺來源");
                }
                foreach (var m in s.ComparisonMissions) Check(d.Missions.Any(x => x.Id == m), $"{w}：未知對照任務 {m}");
                foreach (var kv in s.Requirements)
                {
                    Check(Departments.Parse(kv.Key) != null, $"{w}：未知需求分類 {kv.Key}");
                    foreach (var r in kv.Value) Check(!string.IsNullOrEmpty(r.Id), $"{w}：{kv.Key} 的需求缺 id");
                }
                foreach (var g in s.Requirements.Values.SelectMany(x => x).GroupBy(r => r.Id).Where(g => g.Count() > 1))
                    errors.Add($"{w}：需求 id 重複 {g.Key}");
            }

            Check(d.Difficulties.Count > 0, "缺難度設定");
            foreach (var df in d.AllDifficulties)
            {
                Check(df.Turns >= 1, $"難度 {df.Id}：回合數至少 1");
                Check(df.EventTypes.Count > 0, $"難度 {df.Id}：缺事件種類");
                foreach (var k in df.EventTypes) Check(OpsEvents.Parse(k) != null, $"難度 {df.Id}：未知事件 {k}");
                foreach (var k in df.DecoyCategories)
                    Check(k != PartCategory.GroundStation && PartCategory.All.Contains(k), $"難度 {df.Id}：誘餌類別 {k} 無效");
                Check(df.BlankRatio >= 0 && df.BlankRatio <= 1 && df.BlankRatioSolo >= 0 && df.BlankRatioSolo <= df.BlankRatio + 1e-9,
                    $"難度 {df.Id}：挖空比例要在 0–1，單人的不能比多人多");
                foreach (var cell in df.BlankCells)
                {
                    var bits = cell.Split('/');
                    var bp = bits.Length == 2 ? d.Part(bits[0]) : null;
                    Check(bp != null && PartFields.All.Contains(bits[1]) && PartFields.Get(bp, bits[1]) > 0, $"難度 {df.Id}：挖空的格子 {cell} 無效");
                }
                foreach (var k in df.PrebuiltCategories)
                    Check(k != PartCategory.GroundStation && PartCategory.All.Contains(k), $"難度 {df.Id}：預先組好的類別 {k} 無效");
                Check(new[] { "advice", "chain", "category" }.Contains(df.FailureDetail), $"難度 {df.Id}：未知失敗說明 {df.FailureDetail}");
                foreach (var kv in df.StartParts)
                {
                    var p = d.Part(kv.Value);
                    Check(p != null && p.Category == kv.Key && PartCategory.Single.Contains(kv.Key), $"難度 {df.Id}：開局零件 {kv.Key} = {kv.Value} 無效");
                    Check(p == null || p.Tier <= df.TiersPerCategory, $"難度 {df.Id}：開局零件 {kv.Value} 超過這個難度開放的等級");
                }
            }
            Check(d.Tradeoffs.Count > 0, "tradeoffs.json 沒有取捨說明");
            errors.AddRange(ValidateTradeoffs(d.Tradeoffs, "tradeoffs.json"));
            // 組裝配方：除了地面站和標成不用組裝的，每個零件都要有配方；小零件要有中英文名稱和功能
            foreach (var r in d.Assemblies.Recipes)
            {
                string w = $"組裝配方 {r.Category}";
                Check(PartCategory.All.Contains(r.Category) && r.Category != PartCategory.GroundStation, $"{w}：類別無效");
                Check(r.Components.Count >= 3 && r.Components.Count <= GameData.BasketSize, $"{w}：小零件要 3–{GameData.BasketSize} 個（單人的籃子一趟要拿得齊）");
                Check(r.Components.Select(c => c.Id).Distinct().Count() == r.Components.Count, $"{w}：小零件 id 重複");
                Check(!string.IsNullOrEmpty(r.Source?.Title), $"{w}：缺來源");
                foreach (var id in r.PartIds) Check(d.Part(id)?.Category == r.Category, $"{w}：指定的零件 {id} 不是這個類別");
                foreach (var c in r.Components)
                    Check(c.Id.Length * c.Name.En.Length * c.Name.ZhTW.Length * c.Role.En.Length * c.Role.ZhTW.Length > 0, $"{w}：小零件 {c.Id} 缺 id 或中英文");
            }
            foreach (var g in d.Assemblies.Recipes.SelectMany(r => r.Components).GroupBy(c => c.Id))
                Check(g.Select(c => c.Name.En).Distinct().Count() == 1, $"小零件 {g.Key}：同一個 id 在不同配方裡名稱不一樣");
            foreach (var id in d.Assemblies.Prebuilt) Check(d.Part(id) != null, $"不用組裝的零件 {id} 不存在");
            foreach (var p in d.Parts.Where(p => p.Category != PartCategory.GroundStation && !d.Assemblies.Prebuilt.Contains(p.Id)))
                Check(d.Recipe(p) != null, $"零件 {p.Id}：沒有組裝配方");
            // 組裝台靠左側擺的小零件組合認配方：任兩個配方的組合不能一樣，也不能一個是另一個的一部分
            // （不然小的那個還沒放齊大的，就先被認出來了）
            var recipes = d.Assemblies.Recipes;
            for (int i = 0; i < recipes.Count; i++)
                for (int j = 0; j < recipes.Count; j++)
                {
                    if (i == j) continue;
                    var a = recipes[i].Components.Select(c => c.Id).ToList();
                    var b = new HashSet<string>(recipes[j].Components.Select(c => c.Id));
                    Check(!a.All(b.Contains), $"組裝配方 {recipes[i].Key} 的小零件組合和 {recipes[j].Key} 重複（或是它的一部分）");
                }
            // 酬載儀器一次可以選好幾台：每台都要有自己的配方，組裝台才知道組的是哪一台
            foreach (var g in d.Parts.Where(p => p.Category == PartCategory.Instrument).GroupBy(p => d.Recipe(p)).Where(g => g.Key != null))
            {
                Check(g.Count() == 1, $"酬載儀器 {string.Join("、", g.Select(p => p.Id))} 共用同一個配方");
                Check(g.Key.PartIds.Count == 1, $"酬載儀器 {g.First().Id} 的配方要指定這一台（part_ids 只寫它）");
            }
            foreach (var r in recipes.Where(r => r.PartIds.Count == 0))
                Check(recipes.Count(x => x.PartIds.Count == 0 && x.Category == r.Category) == 1, $"類別 {r.Category} 有兩個通用配方");
            Check(d.SpaceWeather.Count > 0, "缺太空天氣事件");
            Check(d.Constants.BatteryCycleLife.Count >= 2, "電池循環壽命表至少要兩個點");
            return errors;
        }

        static List<string> ValidateTradeoffs(List<TradeoffNote> notes, string where)
        {
            var errors = new List<string>();
            foreach (var t in notes)
            {
                if (string.IsNullOrEmpty(t.Id)) errors.Add($"{where}：取捨說明缺 id");
                if (Departments.Parse(t.Topic) == null) errors.Add($"{where}：取捨說明 {t.Id} 的分類 {t.Topic} 無效");
                if (t.Title.En.Length * t.Title.ZhTW.Length * t.Text.En.Length * t.Text.ZhTW.Length == 0) errors.Add($"{where}：取捨說明 {t.Id} 缺中英文");
            }
            foreach (var g in notes.GroupBy(t => t.Id).Where(g => g.Count() > 1)) errors.Add($"{where}：取捨說明 id 重複 {g.Key}");
            return errors;
        }

        public static List<string> ValidateDesign(GameData d, Design des, string where)
        {
            var errors = new List<string>();
            if (des == null) { errors.Add($"{where}：設計為空"); return errors; }
            foreach (var i in des.Instruments)
            {
                var p = d.Part(i);
                if (p == null || p.Category != PartCategory.Instrument) errors.Add($"{where}：{i} 不是儀器");
            }
            foreach (var c in PartCategory.Single)
            {
                var id = des.Get(c);
                var p = d.Part(id);
                if (p == null || p.Category != c) errors.Add($"{where}：{c} 選擇無效（{id}）");
            }
            if (d.Orbit(des.OrbitId) == null) errors.Add($"{where}：未知軌道 {des.OrbitId}");
            if (d.Launcher(des.LauncherId) == null) errors.Add($"{where}：未知火箭 {des.LauncherId}");
            if (des.MissionYears < 1) errors.Add($"{where}：任務年數必須 ≥ 1");
            return errors;
        }
    }
}
