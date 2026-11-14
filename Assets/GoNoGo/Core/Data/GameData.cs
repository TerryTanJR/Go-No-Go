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
        public List<DepartmentDef> Departments { get; set; } = new List<DepartmentDef>();
        public Dictionary<string, LocalizedText> Strings { get; set; } = new Dictionary<string, LocalizedText>();

        Dictionary<string, PartDef> _parts;
        Dictionary<string, OrbitDef> _orbits;
        Dictionary<string, LauncherDef> _launchers;
        Dictionary<string, ScenarioDef> _scenarios;
        Dictionary<string, DifficultyDef> _difficulties;

        public void BuildIndex()
        {
            _parts = Parts.ToDictionary(p => p.Id);
            _orbits = Orbits.ToDictionary(o => o.Id);
            _launchers = Launchers.ToDictionary(l => l.Id);
            _scenarios = Scenarios.ToDictionary(s => s.Id);
            _difficulties = Difficulties.ToDictionary(d => d.Id);
        }

        public PartDef Part(string id) => id != null && _parts.TryGetValue(id, out var p) ? p : null;
        public OrbitDef Orbit(string id) => id != null && _orbits.TryGetValue(id, out var o) ? o : null;
        public LauncherDef Launcher(string id) => id != null && _launchers.TryGetValue(id, out var l) ? l : null;
        public ScenarioDef Scenario(string id) => id != null && _scenarios.TryGetValue(id, out var s) ? s : null;
        public DifficultyDef Difficulty(string id) => id != null && _difficulties.TryGetValue(id, out var d) ? d : null;
        public DepartmentDef Department(string id) => Departments.Find(x => x.Id == id);

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

        /// <summary>這個關卡、這個難度的預算倍率。</summary>
        public static double BudgetMultiplier(ScenarioDef s, DifficultyDef d)
        {
            if (s != null && d != null && s.BudgetMultiplierOverride.TryGetValue(d.Id, out var m) && m > 0) return m;
            return d?.BudgetMultiplier ?? 1.0;
        }

        /// <summary>這個關卡、這個難度會出現的營運事件。</summary>
        public static List<string> EventTypes(ScenarioDef s, DifficultyDef d) =>
            ByDifficulty(s?.EventTypes, d?.Id) ?? d?.EventTypes ?? new List<string>();

        /// <summary>
        /// 零件架上的零件（不含地面站）：關卡有指定就照指定，否則依難度開放的等級；
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

        /// <summary>這個處室在這個關卡的對話（關卡沒寫就用通用對話）。包含所有難度的題目，用 <see cref="LineOpen"/> 判斷問不問得到。</summary>
        public List<DialogueLine> Lines(ScenarioDef s, DepartmentDef dep) =>
            dep == null ? new List<DialogueLine>()
            : s != null && s.Dialogue.TryGetValue(dep.Id, out var l) && l.Count > 0 ? l : dep.Lines;

        public static bool LineOpen(DialogueLine line, DifficultyDef d) => line.MinConflicts <= (d?.RequirementConflicts ?? 0);

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
            "constants", "parts", "orbits", "launchers", "scenarios", "difficulty", "donki_events", "missions", "departments", "strings"
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
                Departments = Read<List<DepartmentDef>>("departments"),
                Strings = Read<Dictionary<string, LocalizedText>>("strings"),
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
                Check(l.Reliability > 0 && l.Reliability <= 1, $"{w}：可靠度必須在 (0,1]");
                Check(l.CapacityKg.Count > 0, $"{w}：沒有任何軌道運力");
                foreach (var k in l.CapacityKg.Keys) Check(d.Orbit(k) != null, $"{w}：運力表有未知軌道 {k}");
                if (!string.IsNullOrEmpty(l.OrbitLock))
                    Check(l.CapacityKg.Keys.All(k => k == l.OrbitLock), $"{w}：共乘只能有鎖定軌道的運力");
                Check(!string.IsNullOrEmpty(l.Source?.Title), $"{w}：缺來源");
            }

            foreach (var s in d.Scenarios)
            {
                string w = $"情境 {s.Id}";
                Check(s.BudgetMusd > 0 && s.TargetYears > 0 && s.DeadlineMonths > 0, $"{w}：預算/年數/期限必須 > 0");
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
                foreach (var kv in s.Dialogue)
                {
                    Check(Departments.Parse(kv.Key) != null, $"{w}：對話有未知處室 {kv.Key}");
                    foreach (var l in kv.Value)
                        Check(l.Question.En.Length * l.Question.ZhTW.Length * l.Answer.En.Length * l.Answer.ZhTW.Length > 0, $"{w} 對話 {kv.Key}/{l.Id}：缺中英文");
                }
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
                    Check(Departments.Parse(kv.Key) != null, $"{w}：未知處室 {kv.Key}");
                    foreach (var r in kv.Value) Check(!string.IsNullOrEmpty(r.Id), $"{w}：{kv.Key} 的需求缺 id");
                }
                foreach (var g in s.Requirements.Values.SelectMany(x => x).GroupBy(r => r.Id).Where(g => g.Count() > 1))
                    errors.Add($"{w}：需求 id 重複 {g.Key}");
            }

            Check(d.Difficulties.Count > 0, "缺難度設定");
            foreach (var df in d.Difficulties)
            {
                Check(df.Turns >= 1, $"難度 {df.Id}：回合數至少 1");
                Check(df.EventTypes.Count > 0, $"難度 {df.Id}：缺事件種類");
                foreach (var k in df.EventTypes) Check(OpsEvents.Parse(k) != null, $"難度 {df.Id}：未知事件 {k}");
                foreach (var k in df.DecoyCategories)
                    Check(k != PartCategory.GroundStation && PartCategory.All.Contains(k), $"難度 {df.Id}：誘餌類別 {k} 無效");
                Check(new[] { "checklist", "transcript", "none" }.Contains(df.NotesMode), $"難度 {df.Id}：未知筆記板模式 {df.NotesMode}");
                Check(new[] { "advice", "chain", "category" }.Contains(df.FailureDetail), $"難度 {df.Id}：未知失敗說明 {df.FailureDetail}");
                foreach (var kv in df.StartParts)
                {
                    var p = d.Part(kv.Value);
                    Check(p != null && p.Category == kv.Key && PartCategory.Single.Contains(kv.Key), $"難度 {df.Id}：開局零件 {kv.Key} = {kv.Value} 無效");
                    Check(p == null || p.Tier <= df.TiersPerCategory, $"難度 {df.Id}：開局零件 {kv.Value} 超過這個難度開放的等級");
                }
            }
            foreach (var key in Departments.All.Select(Departments.Key))
            {
                var dep = d.Department(key);
                Check(dep != null, $"departments.json 缺處室 {key}");
                if (dep == null) continue;
                Check(dep.Lines.Count > 0, $"處室 {key}：沒有對話");
                foreach (var l in dep.Lines)
                    Check(l.Question.En.Length * l.Question.ZhTW.Length * l.Answer.En.Length * l.Answer.ZhTW.Length > 0, $"處室 {key} 對話 {l.Id}：缺中英文");
            }
            Check(d.SpaceWeather.Count > 0, "缺太空天氣事件");
            Check(d.Constants.BatteryCycleLife.Count >= 2, "電池循環壽命表至少要兩個點");
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
