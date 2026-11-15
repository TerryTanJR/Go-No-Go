using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>
    /// 數值挖空、玩家填值、大屏幕的儀表板（用玩家的數字）、線索（模板產生、每局位置隨機）。
    /// </summary>
    public class BlankTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        SessionState New(string scenario, string diff, bool multi = false, int seed = 0)
        {
            var s = SessionRules.NewSession(_data, scenario, diff, false, multi, seed);
            if (multi) s.Solo = false;
            s.Phase = SessionPhase.Design;
            s.Players.Add(new PlayerInfo { Id = 1 });
            if (multi) s.Players.Add(new PlayerInfo { Id = 2 });
            return s.PresetDefaults(_data);
        }

        void Ok(SessionState s, Command c) => Flow.Do(s, _data, c);
        string Try(SessionState s, CommandType t, int player, string a = "", int n = 0) => SessionRules.Apply(s, _data, Command.Of(t, player, a, n));
        void Fill(SessionState s, string part, string field, double v, int player = 1) =>
            Ok(s, Command.Of(CommandType.Fill, player, Blanks.FillArg(part, field, v)));

        // ---------------------------------------------------------------- 哪些格子挖空

        [Test]
        public void OnlyTheSixFieldsOfListedPartsAreBlankedAndOnlyWhereThereIsAValue()
        {
            var s = New("earth_observation", "director", true);
            var cells = Blanks.Cells(_data, s);
            Assert.That(cells, Is.Not.Empty);
            var catalog = SessionRules.Catalog(_data, s).Select(p => p.Id).ToList();
            foreach (var (part, field) in cells)
            {
                Assert.That(catalog, Has.Member(part), "只挖這一關能選的零件");
                Assert.That(PartFields.All, Has.Member(field));
                Assert.That(PartFields.Get(_data.Part(part), field), Is.GreaterThan(0), part + "/" + field + "：沒有數值的欄位不挖");
            }
            // 任務主管（多人）：六種欄位全部挖空
            int all = catalog.Sum(id => PartFields.All.Count(f => PartFields.Get(_data.Part(id), f) > 0));
            Assert.That(cells.Count, Is.EqualTo(all));
            Assert.That(cells.Select(c => c.field).Distinct(), Is.EquivalentTo(PartFields.All));
            // 價格不挖空（也不在六個欄位裡）；地面站不在大屏幕的零件清單裡
            Assert.That(PartFields.All, Has.No.Member("cost_musd"));
            Assert.That(Blanks.IsBlank(_data, s, "gs_3", PartFields.MassKg), Is.False);
            Assert.That(cells.Count, Is.LessThanOrEqualTo(catalog.Count * 6), "每個零件最多 6 個欄位");
        }

        [Test]
        public void HarderDifficultiesBlankMoreAndSoloBlanksLikeACadet()
        {
            int Count(string diff, bool multi) => Blanks.Cells(_data, New("earth_observation", diff, multi)).Count;
            var cadet = Blanks.Cells(_data, New("lunar_mapping", "cadet", true));
            var eng = Blanks.Cells(_data, New("lunar_mapping", "engineer", true));
            var dir = Blanks.Cells(_data, New("lunar_mapping", "director", true));
            Assert.That(cadet.Count, Is.GreaterThan(0));
            Assert.That(eng.Count, Is.GreaterThan(cadet.Count), "學員少，高難度多");
            Assert.That(dir.Count, Is.GreaterThan(eng.Count));
            // 難度越高只會多挖，不會換一批：同一個零件在學員挖空的格子，工程師也一定挖空
            var engSet = new HashSet<(string, string)>(eng);
            var dirSet = new HashSet<(string, string)>(dir);
            foreach (var c in cadet.Where(c => SessionRules.Catalog(_data, New("lunar_mapping", "engineer", true)).Any(p => p.Id == c.part)))
                Assert.That(engSet.Contains(c), c.part + "/" + c.field);
            foreach (var c in eng) Assert.That(dirSet.Contains(c), c.part + "/" + c.field);

            // 單人：比照學員減少（其他照所選難度）
            foreach (var diff in new[] { "engineer", "director" })
            {
                Assert.That(Count(diff, false), Is.LessThan(Count(diff, true)), diff);
                Assert.That(Blanks.Ratio(_data, New("earth_observation", diff)), Is.EqualTo(_data.Difficulty("cadet").BlankRatio), diff);
            }
            Assert.That(Blanks.Ratio(_data, New("earth_observation", "director", true)), Is.EqualTo(1.0));
        }

        [Test]
        public void HistoricalAndSandboxMissionsAreNeverBlanked()
        {
            foreach (var id in new[] { "hist_oco", "sandbox" })
            {
                var s = New(id, null);
                Assert.That(Blanks.Enabled(_data, s), Is.False, id);
                Assert.That(Blanks.Cells(_data, s), Is.Empty, id);
                Assert.That(Clues.For(_data, s), Is.Empty, id);
                Assert.That(Blanks.PlayerData(_data, s), Is.SameAs(_data), id);
                Assert.That(Blanks.Missing(_data, s, s.Bench, PartFields.All), Is.Zero, id);
            }
            // 歷史關卡失敗之後改設計，一樣看得到全部數值
            var h = New("hist_oco", null);
            Assert.That(Try(h, CommandType.Fill, 1, Blanks.FillArg("batt_large", PartFields.CapacityWh, 5)), Is.EqualTo("err.not_blank"));
        }

        // ---------------------------------------------------------------- 填值

        [Test]
        public void PlayersTypeTheNumbersInAndTheLastOneWins()
        {
            var s = New("earth_observation", "director", true);
            var cell = Blanks.Cells(_data, s).First(c => c.part == "batt_small" && c.field == PartFields.CapacityWh);
            Assert.That(Blanks.Shown(_data, s, cell.part, cell.field), Is.Null, "開局是空的");
            Assert.That(Blanks.Known(_data, s, cell.part, cell.field), Is.False);

            Fill(s, cell.part, cell.field, 80, 1);
            Assert.That(Blanks.Shown(_data, s, cell.part, cell.field), Is.EqualTo(80));
            Assert.That(Blanks.Filled(s, cell.part, cell.field).By, Is.EqualTo(1));
            // 兩個人填同一格：後填的蓋掉先填的，格子旁邊顯示是誰填的
            Fill(s, cell.part, cell.field, 800, 2);
            Assert.That(Blanks.Filled(s, cell.part, cell.field).V, Is.EqualTo(800), "填錯不擋，也不提示");
            Assert.That(Blanks.Filled(s, cell.part, cell.field).By, Is.EqualTo(2));
            // 清掉
            Ok(s, Command.Of(CommandType.Fill, 2, Blanks.FillArg(cell.part, cell.field, null)));
            Assert.That(Blanks.Filled(s, cell.part, cell.field), Is.Null);

            Assert.That(Try(s, CommandType.Fill, 1, Blanks.FillArg(cell.part, cell.field, -1)), Is.EqualTo("err.fill_range"));
            Assert.That(Try(s, CommandType.Fill, 1, Blanks.FillArg(cell.part, cell.field, 1e9)), Is.EqualTo("err.fill_range"));
            Assert.That(Try(s, CommandType.Fill, 1, "batt_small|capacity_wh|abc"), Is.EqualTo("err.fill_format"));
            Assert.That(Try(s, CommandType.Fill, 1, "junk"), Is.EqualTo("err.fill_format"));
            Assert.That(Try(s, CommandType.Fill, 1, Blanks.FillArg("batt_small", "cost_musd", 1)), Is.EqualTo("err.not_blank"), "價格不挖空");
            Assert.That(Try(s, CommandType.Fill, 1, Blanks.FillArg("prop_none", PartFields.MassKg, 1)), Is.EqualTo("err.not_blank"), "沒有數值的欄位不是空格");
            Assert.That(Try(s, CommandType.Fill, 1, Blanks.FillArg("inst_fire_mwir", PartFields.MassKg, 1)), Is.EqualTo("err.not_blank"), "這一關選不到的零件");
            // 設計確定之後、點名中都還可以填（填數值不算改設計）
            Ok(s, Command.Of(CommandType.Confirm, 1, "", 1));
            Fill(s, cell.part, cell.field, 80);
            // 小數、整數都能來回
            Assert.That(Blanks.TryParseFill(Blanks.FillArg("a", "b", 12.75), out var p, out var f, out var v) && p == "a" && f == "b" && v == 12.75);
        }

        [Test]
        public void TheRealDataIsNeverTouched()
        {
            var s = New("earth_observation", "director", true);
            double real = _data.Part("batt_small").CapacityWh;
            Fill(s, "batt_small", PartFields.CapacityWh, 9999);
            var shadow = Blanks.PlayerData(_data, s);
            Assert.That(shadow, Is.Not.SameAs(_data));
            Assert.That(shadow.Part("batt_small").CapacityWh, Is.EqualTo(9999));
            Assert.That(shadow.Part("batt_small").MassKg, Is.Zero, "沒填的欄位不算進去");
            Assert.That(_data.Part("batt_small").CapacityWh, Is.EqualTo(real), "真實數值不能被玩家輸入影響");
            Assert.That(shadow.Part("batt_small").CostMusd, Is.EqualTo(_data.Part("batt_small").CostMusd), "價格不挖空");
            Assert.That(shadow.Part("gs_1"), Is.SameAs(_data.Part("gs_1")));
            Assert.That(shadow.Scenario("earth_observation"), Is.SameAs(_data.Scenario("earth_observation")));
            Assert.That(shadow.Recipe(shadow.Part("batt_small")), Is.SameAs(_data.Recipe(_data.Part("batt_small"))));
        }

        // ---------------------------------------------------------------- 儀表板用玩家的數字

        [Test]
        public void TheDashboardOnlyCountsWhatHasBeenFilledIn()
        {
            var s = New("earth_observation", "director", true);
            var d = s.Bench;
            Evaluation Dash() => Evaluator.EvaluateDesign(d, Blanks.PlayerData(_data, s), s.DifficultyId, false);
            var real = Evaluator.EvaluateDesign(d, _data, s.DifficultyId, false);

            // 什麼都沒填：質量只剩結構以外是 0，旁邊要標「還有 N 個數值沒填」
            int massBlanks = Blanks.Missing(_data, s, d, Blanks.FieldsFor("mass"));
            Assert.That(massBlanks, Is.EqualTo(d.AllPartIds().Count(id => _data.Part(id).MassKg > 0 && _data.Part(id).Category != PartCategory.GroundStation)));
            Assert.That(Dash().Mass.PartsKg, Is.Zero);
            Assert.That(Blanks.Summable("mass") && Blanks.Summable("power") && Blanks.Summable("budget"));
            Assert.That(Blanks.Summable("battery") || Blanks.Summable("comms"), Is.False);

            // 例：某零件挖了好幾個空、只填了 1 個，勾選它時加總項只增加那 1 個欄位的數值
            Fill(s, "inst_visible", PartFields.MassKg, _data.Part("inst_visible").MassKg);
            Assert.That(Dash().Mass.PartsKg, Is.EqualTo(_data.Part("inst_visible").MassKg).Within(1e-9));
            Assert.That(Blanks.Missing(_data, s, d, Blanks.FieldsFor("mass")), Is.EqualTo(massBlanks - 1));
            Assert.That(Blanks.MissingOf(_data, s, "inst_visible"), Is.EqualTo(3), "相機還有功率、解析度、資料量沒填");
            var co = Dash().Cost;
            Assert.That(co.TotalMusd, Is.EqualTo(real.Cost.TotalMusd).Within(1e-9), "成本用的是價格，不受挖空影響");
            Assert.That(Blanks.Missing(_data, s, d, Blanks.FieldsFor("budget")), Is.Zero);

            // 全部照真實數值填完：儀表板和真實評估一模一樣
            foreach (var (part, field) in Blanks.Cells(_data, s)) Fill(s, part, field, PartFields.Get(_data.Part(part), field));
            Assert.That(Blanks.Missing(_data, s, d, PartFields.All), Is.Zero);
            var full = Dash();
            foreach (var id in real.Resources.Keys)
            {
                Assert.That(full[id].Used, Is.EqualTo(real[id].Used).Within(1e-9), id.ToString());
                Assert.That(full[id].Margin, Is.EqualTo(real[id].Margin).Within(1e-9), id.ToString());
            }
            Assert.That(full.Lifetime.Years, Is.EqualTo(real.Lifetime.Years).Within(1e-9));
            Assert.That(full.Data.ReturnFraction, Is.EqualTo(real.Data.ReturnFraction).Within(1e-9));
        }

        [Test]
        public void ARequirementWithAnEmptyInputIsNotJudged()
        {
            var s = New("earth_observation", "director", true);
            var reqs = Departments.Active(s.Scenario(_data), s.Difficulty(_data)).Select(a => a.Requirement).ToList();
            RequirementDef R(string type) => reqs.First(r => r.Type == type);
            // 不用挖空數值的需求：隨時判得出來
            Assert.That(Blanks.CanJudge(_data, s, s.Bench, R("budget_reserve")), "預算只看價格");
            Assert.That(Blanks.CanJudge(_data, s, s.Bench, R("instrument_required")));
            // 用到挖空數值的：有一格沒填就是「資料不足」
            foreach (var type in new[] { "lifetime_target", "mass_power_margin", "science_threshold", "data_return" })
                Assert.That(Blanks.CanJudge(_data, s, s.Bench, R(type)), Is.False, type);

            // 資料傳回率只看資料量和下傳速率：把這兩種填完就判得出來，其他的還是不行
            foreach (var (part, field) in Blanks.Cells(_data, s).Where(c => c.field == PartFields.DataGbPerDay || c.field == PartFields.DownlinkMbps))
                Fill(s, part, field, PartFields.Get(_data.Part(part), field));
            Assert.That(Blanks.CanJudge(_data, s, s.Bench, R("data_return")));
            Assert.That(Blanks.CanJudge(_data, s, s.Bench, R("mass_power_margin")), Is.False);
            // 只看「選了的零件」：沒選的零件沒填不影響
            Assert.That(Blanks.Missing(_data, s, s.Bench, PartFields.DataGbPerDay, PartFields.DownlinkMbps), Is.Zero);
            Assert.That(Blanks.Cells(_data, s).Any(c => !s.Bench.AllPartIds().Contains(c.part)));

            // 每一種需求都有對應（沒列到的種類 = 不用挖空的數值）
            foreach (var sc in _data.Scenarios)
                foreach (var r in sc.Requirements.Values.SelectMany(x => x))
                    Assert.That(Blanks.FieldsForRequirement(r.Type), Is.Not.Null, r.Type);
            foreach (var item in new[] { "mass", "power", "battery", "lifetime", "data", "comms", "return", "science", "latency", "resolution" })
                Assert.That(Blanks.FieldsFor(item), Is.Not.Empty, item);
            Assert.That(Blanks.FieldsFor("budget"), Is.Empty);
        }

        /// <summary>
        /// Spec 的例子：電池的真實容量不夠，玩家把它填大了。儀表板顯示電力通過，發射後失敗。
        /// 發射、失敗原因和分數都用零件的真實數值。
        /// </summary>
        [Test]
        public void AWrongNumberPassesOnTheDashboardButTheLaunchUsesTheRealOne()
        {
            var s = New("earth_observation", "director", true);
            // 最耗電的儀器 + 最小的電池：真實數值撐不過地影
            var hungry = SessionRules.Catalog(_data, s).Where(p => p.Category == PartCategory.Instrument).OrderByDescending(p => p.PowerW).First();
            Ok(s, Command.Of(CommandType.SelectPart, 1, "inst_visible", 0));
            Ok(s, Command.Of(CommandType.SelectPart, 1, hungry.Id, 1));
            var real = Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false);
            Assume.That(real.Power.ActualDod, Is.GreaterThan(1.0));

            // 其他都照實填，只有電池容量填成一個很大的數字
            foreach (var (part, field) in Blanks.Cells(_data, s)) Fill(s, part, field, PartFields.Get(_data.Part(part), field));
            Fill(s, "batt_small", PartFields.CapacityWh, 5000);
            var dash = Evaluator.EvaluateDesign(s.Bench, Blanks.PlayerData(_data, s), s.DifficultyId, false);
            Assert.That(dash.Power.ActualDod, Is.LessThan(1.0), "儀表板照玩家填的數字算：電池看起來夠");
            Assert.That(dash.Power.BatteryWh, Is.EqualTo(5000));

            Flow.BuildAll(s, _data);
            Ok(s, Command.Of(CommandType.Launch, 1));
            Ok(s, Command.Of(CommandType.Vote, 1, "", 1));
            Ok(s, Command.Of(CommandType.Vote, 2, "", 1));
            Assert.That(s.LastLaunch.Reached, Is.False, "發射用真實數值");
            Assert.That(s.LastLaunch.Outcome, Is.EqualTo(LaunchOutcome.DeadOnOrbit));
            var r = SessionRules.ResultOf(s.LastLaunch, s, _data);
            Assert.That(r.Causes[0].Code, Is.EqualTo("dead_on_orbit"));
            // 失敗原因寫得出真實數值：電池真正的容量
            var steps = r.Causes[0].Steps;
            Assert.That(steps.Any(st => st.Args.Values.Any(a => a == _data.Part("batt_small").CapacityWh.ToString("0"))),
                "因果鏈要帶電池的真實容量：" + string.Join(" / ", steps.Select(st => st.Key + ":" + string.Join(",", st.Args.Values))));
        }

        [Test]
        public void NamesDoNotGiveAwayABlankedNumber()
        {
            var s = New("earth_observation", "director", true);
            var batt = _data.Part("batt_small");
            Assert.That(batt.Name.ZhTW, Does.Contain("80 Wh"));
            Assert.That(Blanks.MaskedName(_data, s, batt, Lang.ZhTW), Is.EqualTo("小型鋰電池"), "容量還沒填：名字上不能直接寫答案");
            Assert.That(Blanks.MaskedName(_data, s, batt, Lang.En), Does.Not.Contain("Wh"));
            var ant = _data.Part("ant_xband");
            Assert.That(Blanks.MaskedName(_data, s, ant, Lang.ZhTW), Does.Not.Contain("Mbps"));
            Fill(s, "batt_small", PartFields.CapacityWh, 80);
            Assert.That(Blanks.MaskedName(_data, s, batt, Lang.ZhTW), Is.EqualTo(batt.Name.ZhTW), "填了之後照常顯示");
            // 括號裡不是挖空數值的（面積、推進劑）不動；沒挖空的局不動
            var solar = _data.Part("solar_deployable");
            Assert.That(Blanks.MaskedName(_data, s, solar, Lang.ZhTW), Is.EqualTo(solar.Name.ZhTW));
            var sandbox = New("sandbox", null);
            Assert.That(Blanks.MaskedName(_data, sandbox, batt, Lang.ZhTW), Is.EqualTo(batt.Name.ZhTW));
            Assert.That(Blanks.StripParen("Small Li-ion Battery (80 Wh)"), Is.EqualTo("Small Li-ion Battery"));
            Assert.That(Blanks.StripParen("No parens"), Is.EqualTo("No parens"));
        }

        // ---------------------------------------------------------------- 線索

        [Test]
        public void EveryBlankedCellIsInExactlyOneClue()
        {
            foreach (var sc in _data.Scenarios.Where(x => x.Historical == null))
                foreach (var diff in new[] { "cadet", "engineer", "director" })
                    foreach (bool multi in new[] { false, true })
                    {
                        var s = New(sc.Id, diff, multi);
                        var cells = Blanks.Cells(_data, s);
                        var clues = Clues.For(_data, s);
                        var covered = clues.SelectMany(c => c.Fields.Select(f => (c.PartId, f))).ToList();
                        string where = sc.Id + "/" + diff + (multi ? "/multi" : "/solo");
                        Assert.That(covered, Is.EquivalentTo(cells), where);
                        Assert.That(covered, Is.Unique, where);
                        var catalog = SessionRules.Catalog(_data, s).Select(p => p.Id).ToList();
                        foreach (var c in clues)
                        {
                            Assert.That(catalog, Has.Member(c.PartId), where + "：只產生當關能選的零件的線索");
                            Assert.That(c.Fields.Count, Is.InRange(1, Clues.MaxFields), where);
                            Assert.That(c.Variant, Is.InRange(0, Clues.NoteVariants - 1));
                        }
                        Assert.That(clues.Select(c => c.Id), Is.Unique);
                        // 呈現方式多樣化，不分難度：表格、敘述、圖表、設計圖都要有
                        Assert.That(clues.Select(c => c.Kind).Distinct().Count(), Is.EqualTo(4), where);
                    }
        }

        [Test]
        public void ClueContentIsFixedButPlacementChangesEveryGame()
        {
            var a = New("earth_observation", "engineer", true, 11);
            var b = New("earth_observation", "engineer", true, 12345);
            string Sig(List<Clue> list) => string.Join(";", list.OrderBy(c => c.Id).Select(c => c.Id + ":" + c.PartId + ":" + string.Join(",", c.Fields) + ":" + c.Kind));
            Assert.That(Sig(Clues.For(_data, a)), Is.EqualTo(Sig(Clues.For(_data, b))), "內容每局固定");

            var pa = Clues.Place(_data, a);
            var pb = Clues.Place(_data, b);
            string Where(Dictionary<string, List<Clue>> m) => string.Join(";", m.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + string.Join(",", kv.Value.Select(c => c.Id).OrderBy(x => x))));
            Assert.That(Where(pa), Is.Not.EqualTo(Where(pb)), "擺放位置每局隨機");
            Assert.That(Where(Clues.Place(_data, New("earth_observation", "engineer", true, 11))), Is.EqualTo(Where(pa)), "同一個種子擺法一樣（多人時每台電腦算出來都一樣）");

            foreach (var map in new[] { pa, pb })
            {
                Assert.That(map.Keys, Is.EquivalentTo(Clues.Spots.Select(x => x.Id)));
                Assert.That(map.Values.SelectMany(x => x).Select(c => c.Id), Is.EquivalentTo(Clues.For(_data, a).Select(c => c.Id)), "每則線索都擺在某個地方，而且只擺一次");
                foreach (var kv in map)
                    foreach (var c in kv.Value)
                        Assert.That(Clues.Accepts(Clues.Spot(kv.Key).Medium, c.Kind), kv.Key + " 放不下 " + c.Kind);
                // 兩個房間都有線索，而且平均分散（不會全部擠在一本書裡）
                Assert.That(map.Where(kv => kv.Value.Count > 0).Select(kv => Clues.Spot(kv.Key).Zone).Distinct(), Is.EquivalentTo(new[] { "control", "bay" }));
                int total = map.Values.Sum(x => x.Count);
                Assert.That(map.Values.Max(x => x.Count), Is.LessThanOrEqualTo(total / 4 + 2));
                // 散到各個角落：有線索的區塊數至少是「線索數」和「區塊總數的一半」中比較小的那個
                int areas = map.Where(kv => kv.Value.Count > 0).Select(kv => Clues.Spot(kv.Key).Area).Distinct().Count();
                int allAreas = Clues.Spots.Select(x => x.Area).Distinct().Count();
                Assert.That(areas, Is.GreaterThanOrEqualTo(System.Math.Min(total, allAreas / 2)), "線索擠在少數幾個區塊");
            }
            Assert.That(Clues.At(_data, a, "no_such_spot"), Is.Empty);
        }

        /// <summary>新手訓練第三步就要放大線索：那則線索一定擺在中控室（玩家一開始站的房間），不管種子是多少。</summary>
        [Test]
        public void TutorialCluesStayInTheControlRoom()
        {
            foreach (int seed in new[] { 0, 1, 7, 42, 999, 123456 })
            {
                var s = SessionRules.NewSession(_data, "earth_observation", "tutorial", true, false, seed);
                var placed = Clues.Place(_data, s).Where(kv => kv.Value.Count > 0).ToList();
                Assert.That(placed, Is.Not.Empty, "新手訓練有一格挖空，就有一則線索");
                Assert.That(placed.Select(kv => Clues.Spot(kv.Key).Zone).Distinct(), Is.EquivalentTo(new[] { "control" }), "seed " + seed);
                Assert.That(placed.All(kv => Clues.Spot(kv.Key).Obvious), "新手訓練的線索要放在顯眼的地方（seed " + seed + "）");
                Assert.That(placed.SelectMany(kv => kv.Value).Select(c => c.Id), Is.EquivalentTo(Clues.For(_data, s).Select(c => c.Id)));
            }
        }

        [Test]
        public void ClueSpotsCoverBothRoomsAndEveryKindOfClue()
        {
            Assert.That(Clues.Spots.Select(x => x.Id), Is.Unique);
            Assert.That(Clues.Spots.Select(x => x.Zone).Distinct(), Is.EquivalentTo(new[] { "control", "bay" }));
            // 中控室：研究牆（草稿紙）、白板、黑板、書堆；組裝區：設計圖、架子旁
            var control = Clues.Spots.Where(x => x.Zone == "control").Select(x => x.Medium).Distinct().ToList();
            Assert.That(control, Is.EquivalentTo(new[] { "paper", "whiteboard", "blackboard", "book", "blueprint" }));
            Assert.That(Clues.Spots.Where(x => x.Zone == "bay").Select(x => x.Medium), Has.Member("blueprint"));
            foreach (ClueKind kind in System.Enum.GetValues(typeof(ClueKind)))
                Assert.That(Clues.Spots.Count(x => Clues.Accepts(x.Medium, kind)), Is.GreaterThanOrEqualTo(2), kind.ToString());
            foreach (var sp in Clues.Spots)
                Assert.That(System.Enum.GetValues(typeof(ClueKind)).Cast<ClueKind>().Any(k => Clues.Accepts(sp.Medium, k)), sp.Id);
        }
    }
}
