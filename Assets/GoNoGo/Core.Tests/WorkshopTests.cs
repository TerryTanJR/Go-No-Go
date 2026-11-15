using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>
    /// 組裝：架子上拿小零件 → 組裝台左側認配方 → 右側照順序組 → 裝上衛星。還有沙盒和設計分享碼。
    /// </summary>
    public class WorkshopTests
    {
        GameData _data;

        [SetUp]
        public void SetUp() => _data = TestData.Load();

        SessionState Solo(string scenario = "earth_observation", string diff = "engineer")
        {
            var s = SessionRules.NewSession(_data, scenario, diff, false, false);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.BeginDesign, 1));
            return s.TickDefaults(_data);
        }

        SessionState Duo()
        {
            var s = SessionRules.NewSession(_data, "earth_observation", "engineer", false, true);
            Ok(s, Command.Of(CommandType.Join, 1));
            Ok(s, Command.Of(CommandType.Join, 2));
            Ok(s, Command.Of(CommandType.SetReady, 1, "", 1));
            Ok(s, Command.Of(CommandType.SetReady, 2, "", 1));
            Ok(s, Command.Of(CommandType.StartMission, 1));
            return s.TickDefaults(_data);
        }

        void Ok(SessionState s, Command c) => Flow.Do(s, _data, c);
        string Try(SessionState s, CommandType t, int player, string a = "", int n = 0) => SessionRules.Apply(s, _data, Command.Of(t, player, a, n));

        void Put(SessionState s, int bench, params string[] comps)
        {
            foreach (var c in comps)
            {
                Ok(s, Command.Of(CommandType.CompPick, 1, c));
                Ok(s, Command.Of(CommandType.BenchPut, 1, "", bench));
            }
        }

        static string[] Ids(AssemblyRecipe r) => r.Components.Select(c => c.Id).ToArray();

        // ---------------------------------------------------------------- 配方資料

        [Test]
        public void EveryPartHasARecipeExceptGroundStationsAndThePlate()
        {
            foreach (var p in _data.Parts)
            {
                var r = _data.Recipe(p);
                if (p.Category == PartCategory.GroundStation || p.Id == "prop_none") Assert.That(r, Is.Null, p.Id);
                else
                {
                    Assert.That(r, Is.Not.Null, p.Id);
                    Assert.That(r.Category, Is.EqualTo(p.Category), p.Id);
                    Assert.That(r.Components.Count, Is.InRange(3, GameData.BasketSize), p.Id + "：單人的籃子一趟要拿得齊");
                }
            }
            // 同一類的小、中、大型共用一個配方
            Assert.That(_data.Recipe(_data.Part("batt_small")), Is.SameAs(_data.Recipe(_data.Part("batt_large"))));
            Assert.That(_data.Recipe(_data.Part("solar_deployable")), Is.SameAs(_data.Recipe(_data.Part("solar_large"))));
            Assert.That(_data.Recipe(_data.Part("solar_body")), Is.Not.SameAs(_data.Recipe(_data.Part("solar_large"))), "貼片太陽能板不用展開機構");
        }

        [Test]
        public void NoTwoRecipesShareAComponentSet()
        {
            var recipes = _data.Assemblies.Recipes;
            for (int i = 0; i < recipes.Count; i++)
                for (int j = 0; j < recipes.Count; j++)
                {
                    if (i == j) continue;
                    var a = Ids(recipes[i]);
                    var b = new HashSet<string>(Ids(recipes[j]));
                    Assert.That(a.All(b.Contains), Is.False,
                        recipes[i].Key + " 的小零件組合和 " + recipes[j].Key + " 一樣（或是它的一部分），組裝台會認錯");
                }
            // 所以每一組小零件都只認得出一個配方
            foreach (var r in recipes)
                Assert.That(_data.RecipeOf(Ids(r)), Is.SameAs(r), r.Key);
        }

        [Test]
        public void EveryInstrumentHasItsOwnRecipe()
        {
            var instruments = _data.Parts.Where(p => p.Category == PartCategory.Instrument).ToList();
            Assert.That(instruments.Count, Is.EqualTo(10), "酬載儀器 10 個");
            var seen = new HashSet<AssemblyRecipe>();
            foreach (var p in instruments)
            {
                var r = _data.Recipe(p);
                Assert.That(r.PartIds, Is.EqualTo(new[] { p.Id }), p.Id + "：一次可以選好幾台，不能共用配方");
                Assert.That(seen.Add(r), p.Id);
            }
            // 共用光學鏡組、感測器這類通用小零件沒關係，只要每台至少有一個不一樣
            var optical = new[] { "inst_visible", "inst_multispectral", "inst_hyperspectral", "inst_co2_spectrometer", "inst_fire_mwir", "inst_lightning", "inst_laser_altimeter" };
            foreach (var id in optical) Assert.That(Ids(_data.Recipe(_data.Part(id))), Has.Member("optics").And.Member("detector"), id);
            Assert.That(Ids(_data.Recipe(_data.Part("inst_multispectral"))), Has.Member("filter_wheel"));
            Assert.That(Ids(_data.Recipe(_data.Part("inst_hyperspectral"))), Has.Member("disperser"));
            Assert.That(Ids(_data.Recipe(_data.Part("inst_laser_altimeter"))), Has.Member("pulse_laser"));
        }

        [Test]
        public void NewComponentsCarryASourceAndBothLanguages()
        {
            foreach (var id in new[] { "filter_wheel", "disperser", "grating", "band_filter", "event_processor", "pulse_laser", "timer" })
            {
                var c = _data.Component(id);
                Assert.That(c, Is.Not.Null, id);
                Assert.That(c.Name.En.Length * c.Name.ZhTW.Length * c.Role.En.Length * c.Role.ZhTW.Length, Is.GreaterThan(0), id);
                Assert.That(c.Source?.Title, Is.Not.Empty, id);
                Assert.That(c.Source.Url, Does.StartWith("https://"), id);
            }
        }

        [Test]
        public void TheShelvesHoldEveryComponentInAStableOrder()
        {
            var shelf = _data.ShelfComponents();
            var all = _data.Assemblies.Recipes.SelectMany(r => r.Components).Select(c => c.Id).Distinct().ToList();
            Assert.That(shelf.Select(c => c.Id), Is.EquivalentTo(all), "所有配方會用到的小零件都在架上，包括這次設計用不到的");
            Assert.That(shelf.Select(c => c.Id), Is.Unique);
            var again = GameDataLoader.Load(TestData.Files()).ShelfComponents();
            Assert.That(again.Select(c => c.Id), Is.EqualTo(shelf.Select(c => c.Id)), "每一局、每台電腦的排法都一樣");
            // 架子本身不提示該拿什麼：同一個配方的小零件不會整組排在一起
            var solar = Ids(_data.Recipe(_data.Part("solar_deployable")));
            var at = solar.Select(id => shelf.FindIndex(c => c.Id == id)).OrderBy(i => i).ToList();
            Assert.That(at.Last() - at.First(), Is.GreaterThan(solar.Length - 1));
        }

        // ---------------------------------------------------------------- 架子與搬運

        [Test]
        public void ASoloPlayerCarriesFourInABasketAndATeamCarriesOneEach()
        {
            var s = Solo();
            Assert.That(SessionRules.CarryCapacity(s), Is.EqualTo(4));
            foreach (var c in new[] { "optics", "detector", "readout", "baffle" }) Ok(s, Command.Of(CommandType.CompPick, 1, c));
            Assert.That(s.CompsOf(1).Count, Is.EqualTo(4));
            Assert.That(Try(s, CommandType.CompPick, 1, "cells"), Is.EqualTo("err.hands_full"), "籃子最多 4 個");
            Ok(s, Command.Of(CommandType.CompDrop, 1, "baffle"));
            Assert.That(Try(s, CommandType.CompPick, 1, "optics"), Is.EqualTo("err.comp_dup"), "同一種不用拿兩個");
            Ok(s, Command.Of(CommandType.CompPick, 1, "cells"));
            Ok(s, Command.Of(CommandType.CompDrop, 1));
            Assert.That(s.Comps, Is.Empty, "放回架子");
            Assert.That(Try(s, CommandType.CompPick, 1, "no_such"), Is.EqualTo("err.unknown"));

            var d = Duo();
            Assert.That(SessionRules.CarryCapacity(d), Is.EqualTo(1));
            Ok(d, Command.Of(CommandType.CompPick, 1, "optics"));
            Assert.That(Try(d, CommandType.CompPick, 1, "detector"), Is.EqualTo("err.hands_full"), "兩人以上一次拿一個");
            Ok(d, Command.Of(CommandType.CompPick, 2, "optics"));      // 架子不限數量，拿走會補
            Assert.That(d.CompsOf(2), Is.EqualTo(new[] { "optics" }));
            // 剩下一個人：有籃子了
            Ok(d, Command.Of(CommandType.Leave, 2));
            Assert.That(SessionRules.CarryCapacity(d), Is.EqualTo(4));
            Ok(d, Command.Of(CommandType.CompPick, 1, "detector"));
        }

        // ---------------------------------------------------------------- 組裝台左側：認配方

        [Test]
        public void PlayersChooseWhichSlotAComponentGoesIn()
        {
            var s = Solo();
            Flow.Confirm(s, _data);
            Ok(s, Command.Of(CommandType.CompPick, 1, "cells"));
            Ok(s, Command.Of(CommandType.BenchPut, 1, "4", 0));
            Assert.That(s.Benches[0].At(4), Is.EqualTo("cells"), "放在準星對著的那一格");
            Assert.That(s.Benches[0].At(0), Is.Null);

            Ok(s, Command.Of(CommandType.CompPick, 1, "bms"));
            Assert.That(Try(s, CommandType.BenchPut, 1, "4", 0), Is.EqualTo("err.slot_taken"), "有東西的格子不能再放");
            Ok(s, Command.Of(CommandType.BenchPut, 1, "1", 0));
            Ok(s, Command.Of(CommandType.BenchTake, 1, "bms", 0));
            Assert.That(s.Benches[0].At(4), Is.EqualTo("cells"), "拿走別格的東西，這一格不會移動");
            Assert.That(s.Benches[0].At(1), Is.Null);

            var copy = SessionState.FromJson(s.ToJson());
            Assert.That(copy.Benches[0].At(4), Is.EqualTo("cells"), "多人時整包傳過去，格子不變");
            Assert.That(copy.Benches[0].Slots.Count, Is.EqualTo(GameData.BenchSlots), "傳過去不會多出格子");
        }

        [Test]
        public void TheLeftSideRecognisesARecipeOnlyWhenTheDesignNeedsIt()
        {
            var s = Solo();
            Flow.Confirm(s, _data);
            Assert.That(SessionRules.BenchStatusOf(s, _data, 0), Is.EqualTo(BenchStatus.Empty));

            // 還沒放齊：不算錯
            Put(s, 0, "cells", "bms");
            Assert.That(SessionRules.BenchStatusOf(s, _data, 0), Is.EqualTo(BenchStatus.Partial));
            Assert.That(s.Benches[0].Errors, Is.Zero);
            Assert.That(s.Benches[0].Job, Is.Null);

            // 對不上任何配方：組合錯誤，小零件留在左側讓玩家調整
            Put(s, 0, "optics");
            Assert.That(SessionRules.BenchStatusOf(s, _data, 0), Is.EqualTo(BenchStatus.Unknown));
            Assert.That(s.Benches[0].Errors, Is.EqualTo(1));
            Assert.That(s.Benches[0].LastError, Is.EqualTo(BenchStatus.Unknown));
            Assert.That(s.Benches[0].LastBy, Is.EqualTo(1));
            Assert.That(s.Benches[0].Left, Is.EqualTo(new[] { "cells", "bms", "optics" }));
            Ok(s, Command.Of(CommandType.BenchTake, 1, "optics", 0));
            Ok(s, Command.Of(CommandType.CompDrop, 1));
            Assert.That(SessionRules.BenchStatusOf(s, _data, 0), Is.EqualTo(BenchStatus.Partial));
            Assert.That(s.Benches[0].Errors, Is.EqualTo(1), "拿走東西不算一次新的錯誤");

            // 放齊電池的配方：設計裡有還沒組好的電池 → 認出是哪一顆，右側開始組
            Put(s, 0, "heater", "housing");
            Assert.That(SessionRules.BenchStatusOf(s, _data, 0), Is.EqualTo(BenchStatus.Active));
            Assert.That(s.Benches[0].Job.PartId, Is.EqualTo("batt_small"), "每類只選一個：認出配方就知道是哪個零件");
            Assert.That(SessionRules.BenchWants(s, _data, 0).Id, Is.EqualTo("cells"));

            // 另一座台子放同一個配方：設計裡用它的零件已經在組了 → 這不是這次設計選的零件
            Put(s, 1, "cells", "bms", "heater", "housing");
            Assert.That(SessionRules.BenchStatusOf(s, _data, 1), Is.EqualTo(BenchStatus.NotInDesign));
            Assert.That(s.Benches[1].Errors, Is.EqualTo(1));
            Assert.That(s.Benches[1].LastError, Is.EqualTo(BenchStatus.NotInDesign));

            // 設計沒選的儀器（熱紅外線）的配方：也算錯誤
            foreach (var c in s.Benches[1].Left.ToList()) { Ok(s, Command.Of(CommandType.BenchTake, 1, c, 1)); Ok(s, Command.Of(CommandType.CompDrop, 1)); }
            Put(s, 1, "optics", "detector", "cryocooler", "readout");
            Assert.That(SessionRules.BenchStatusOf(s, _data, 1), Is.EqualTo(BenchStatus.NotInDesign));
            Assert.That(s.Benches[1].Errors, Is.EqualTo(2));
        }

        [Test]
        public void TheBenchWaitsForTheDesignToBeConfirmed()
        {
            var s = Solo();
            Put(s, 0, "cells", "bms", "heater", "housing");
            Assert.That(SessionRules.BenchStatusOf(s, _data, 0), Is.EqualTo(BenchStatus.Waiting), "設計還沒在設計主螢幕上確定");
            Assert.That(s.Benches[0].Job, Is.Null);
            Assert.That(s.Benches[0].LastError, Is.EqualTo(BenchStatus.Waiting));
            Ok(s, Command.Of(CommandType.Confirm, 1, "", 1));
            Assert.That(s.Benches[0].Job?.PartId, Is.EqualTo("batt_small"), "確定之後，左側已經擺好的組合直接認出來");
        }

        [Test]
        public void SizesOfOneCategoryShareARecipeAndTheBenchBuildsTheOneTicked()
        {
            var s = Solo();
            Flow.Select(s, _data, "batt_large");
            Flow.Confirm(s, _data);
            Put(s, 0, "cells", "bms", "heater", "housing");
            Assert.That(s.Benches[0].Job.PartId, Is.EqualTo("batt_large"));
        }

        // ---------------------------------------------------------------- 組裝台右側：照順序組

        [Test]
        public void FittingInOrderBuildsThePartAndMistakesCostNothing()
        {
            var s = Solo();
            Flow.Confirm(s, _data);
            var before = Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false);
            var recipe = _data.Recipe(_data.Part("batt_small"));
            Put(s, 0, Ids(recipe));
            var b = s.Benches[0];

            // 順序錯：小零件退回左側
            Ok(s, Command.Of(CommandType.BenchTake, 1, "heater", 0));
            Ok(s, Command.Of(CommandType.CompFit, 1, "", 0));
            Assert.That(b.Job.LastOk, Is.False);
            Assert.That(b.Job.Step, Is.Zero);
            Assert.That(b.Job.Mistakes, Is.EqualTo(1));
            Assert.That(b.Left, Has.Member("heater"), "順序錯的退回左側");
            Assert.That(s.Comps, Is.Empty);

            // 誘答小零件（不屬於這個零件）直接從架子拿來硬裝：報廢，再去架子拿就有
            Ok(s, Command.Of(CommandType.CompPick, 1, "nozzle"));
            Ok(s, Command.Of(CommandType.CompFit, 1, "", 0));
            Assert.That(b.Job.LastOk, Is.False);
            Assert.That(b.Job.Scrapped, Is.EqualTo(new[] { "nozzle" }));
            Assert.That(b.Left, Has.No.Member("nozzle"));
            Assert.That(b.Job.Mistakes, Is.EqualTo(2));
            Ok(s, Command.Of(CommandType.CompPick, 1, "nozzle"));
            Ok(s, Command.Of(CommandType.CompDrop, 1));

            // 右側正在組：一次拿一個去裝
            Ok(s, Command.Of(CommandType.BenchTake, 1, "cells", 0));
            Assert.That(Try(s, CommandType.BenchTake, 1, "bms", 0), Is.EqualTo("err.hands_full"));
            // 拿錯了（或改變主意）：放回左側，再拿一次；放回去不算裝錯
            int mistakes = b.Job.Mistakes;
            Ok(s, Command.Of(CommandType.BenchPut, 1, "", 0));
            Assert.That(b.Left, Has.Member("cells"));
            Assert.That(s.CompsOf(1), Is.Empty);
            Assert.That(b.Job.Mistakes, Is.EqualTo(mistakes));
            Assert.That(b.Job.Step, Is.EqualTo(0));
            // 不屬於這個零件的小零件還是不收
            Ok(s, Command.Of(CommandType.CompPick, 1, "nozzle"));
            Assert.That(Try(s, CommandType.BenchPut, 1, "", 0), Is.EqualTo("err.bench_busy"));
            Ok(s, Command.Of(CommandType.CompDrop, 1));
            Ok(s, Command.Of(CommandType.BenchTake, 1, "cells", 0));
            Ok(s, Command.Of(CommandType.CompFit, 1, "", 0));
            Assert.That(b.Job.LastOk);
            Assert.That(b.Job.Step, Is.EqualTo(1));
            Assert.That(b.Job.LastBy, Is.EqualTo(1));
            Assert.That(SessionRules.BenchWants(s, _data, 0).Id, Is.EqualTo("bms"));
            foreach (var c in Ids(recipe).Skip(1))
            {
                Ok(s, Command.Of(CommandType.BenchTake, 1, c, 0));
                Ok(s, Command.Of(CommandType.CompFit, 1, "", 0));
            }
            Assert.That(b.Job.Done);
            Assert.That(b.Left, Is.Empty);
            Assert.That(s.Built, Has.Member("batt_small"));
            Assert.That(s.Learned, Has.Member(recipe.Key));
            Assert.That(s.Flawless, Has.No.Member(recipe.Key), "裝錯過：不算零失誤");
            Assert.That(SessionRules.BenchStatusOf(s, _data, 0), Is.EqualTo(BenchStatus.Done));
            Assert.That(Try(s, CommandType.CompFit, 1, "", 0), Is.EqualTo("err.nothing_held").Or.EqualTo("err.no_job"));

            // 成品沒拿走之前，這座台子不能放新的小零件
            Ok(s, Command.Of(CommandType.CompPick, 1, "optics"));
            Assert.That(Try(s, CommandType.BenchPut, 1, "", 0), Is.EqualTo("err.bench_take_first"));
            Assert.That(Try(s, CommandType.Pick, 1, "batt_small"), Is.EqualTo("err.hands_full"));
            Ok(s, Command.Of(CommandType.CompDrop, 1));
            Assert.That(SessionRules.BenchHolding(s, "batt_small"), Is.EqualTo(0));
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_small"));
            Assert.That(b.Job, Is.Null);
            Assert.That(Try(s, CommandType.CompPick, 1, "optics"), Is.EqualTo("err.hands_full"), "手上不是零件就是小零件");
            Ok(s, Command.Of(CommandType.Install, 1));
            Assert.That(s.Installed, Has.Member("batt_small"));
            Assert.That(s.Held, Is.Empty);

            // 裝錯沒有懲罰：不扣分，設計的評估完全一樣；只留紀錄（所有人看得到誰裝錯）
            Assert.That(s.Mistakes, Is.EqualTo(2));
            var after = Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false);
            Assert.That(after.Cost.TotalMusd, Is.EqualTo(before.Cost.TotalMusd));
            foreach (var id in before.Resources.Keys) Assert.That(after[id].Margin, Is.EqualTo(before[id].Margin), id.ToString());
        }

        [Test]
        public void AFlawlessBuildIsRemembered()
        {
            var s = Solo();
            Flow.Confirm(s, _data);
            Flow.Build(s, _data, "solar_body");
            var key = _data.Recipe(_data.Part("solar_body")).Key;
            Assert.That(s.Learned, Has.Member(key));
            Assert.That(s.Flawless, Has.Member(key), "一次都沒裝錯：知識卡升級成精通");
        }

        [Test]
        public void TwoBenchesBuildTwoPartsAtOnce()
        {
            var s = Duo();
            Flow.Confirm(s, _data);
            var batt = Ids(_data.Recipe(_data.Part("batt_small")));
            var cam = Ids(_data.Recipe(_data.Part("inst_visible")));
            // 兩個人各用一座，小零件一次拿一個、來回放
            for (int i = 0; i < 4; i++)
            {
                Ok(s, Command.Of(CommandType.CompPick, 1, batt[i]));
                Ok(s, Command.Of(CommandType.CompPick, 2, cam[i]));
                Ok(s, Command.Of(CommandType.BenchPut, 1, "", 0));
                Ok(s, Command.Of(CommandType.BenchPut, 2, "", 1));
            }
            Assert.That(s.Benches[0].Job.PartId, Is.EqualTo("batt_small"));
            Assert.That(s.Benches[1].Job.PartId, Is.EqualTo("inst_visible"));
            // 誰都能用：2 號幫忙裝 1 號那一座
            Ok(s, Command.Of(CommandType.BenchTake, 2, batt[0], 0));
            Ok(s, Command.Of(CommandType.CompFit, 2, "", 0));
            Assert.That(s.Benches[0].Job.Step, Is.EqualTo(1));
            Assert.That(s.Benches[0].Job.LastBy, Is.EqualTo(2));
            for (int i = 1; i < 4; i++) { Ok(s, Command.Of(CommandType.BenchTake, 1, batt[i], 0)); Ok(s, Command.Of(CommandType.CompFit, 1, "", 0)); }
            for (int i = 0; i < 4; i++) { Ok(s, Command.Of(CommandType.BenchTake, 2, cam[i], 1)); Ok(s, Command.Of(CommandType.CompFit, 2, "", 1)); }
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_small"));
            Ok(s, Command.Of(CommandType.Pick, 2, "inst_visible"));
            Assert.That(Try(s, CommandType.Pick, 2, "batt_small"), Is.EqualTo("err.hands_full"));
            Ok(s, Command.Of(CommandType.Install, 1));
            Ok(s, Command.Of(CommandType.Install, 2));
            Assert.That(s.Installed, Has.Member("batt_small").And.Member("inst_visible"));
        }

        [Test]
        public void SwappingAPartMidBuildCancelsItsBenchAndDropsItFromHands()
        {
            var s = Solo();
            Flow.Confirm(s, _data);
            var recipe = Ids(_data.Recipe(_data.Part("ant_xband")));
            Put(s, 0, recipe);
            Ok(s, Command.Of(CommandType.BenchTake, 1, recipe[0], 0));
            Ok(s, Command.Of(CommandType.CompFit, 1, "", 0));
            Assert.That(s.Benches[0].Job.Step, Is.EqualTo(1));
            // 天線換成別的類型（雷射光通訊是另一個配方）：這座台子上的取消，裝上去的小零件拆回左側
            Assert.That(SessionRules.Catalog(_data, s).Select(p => p.Id), Has.Member("ant_optical"));
            Flow.Select(s, _data, "ant_optical");
            Assert.That(s.Benches[0].Job, Is.Null);
            Assert.That(s.Benches[0].Left, Is.EquivalentTo(recipe));
            Flow.Confirm(s, _data);
            Assert.That(SessionRules.BenchStatusOf(s, _data, 0), Is.EqualTo(BenchStatus.NotInDesign));

            // 組好拿在手上的零件被換掉：放下（成品留著），改選回來就直接在衛星上
            Flow.Assemble(s, _data, "batt_small", 1, 1);
            Ok(s, Command.Of(CommandType.Pick, 1, "batt_small"));
            Flow.Select(s, _data, "batt_medium");
            Assert.That(s.Held, Is.Empty);
            Assert.That(s.Installed, Has.No.Member("batt_small"));
            Flow.Select(s, _data, "batt_small");
            Assert.That(s.Installed, Has.Member("batt_small"));
        }

        [Test]
        public void ThePlateNeedsNoAssembly()
        {
            var s = Solo();
            Assert.That(SessionRules.HasAssembly(s, _data, "prop_none"), Is.False);
            Assert.That(s.Installed, Has.Member("prop_none"), "不裝推進的封板：選了就在衛星上");
            Flow.Select(s, _data, "prop_green");
            Assert.That(s.Installed, Has.No.Member("prop_none"));
            Assert.That(SessionRules.NeedsAssembly(s, _data, "prop_green"));
            Flow.Select(s, _data, "prop_none");
            Assert.That(s.Installed, Has.Member("prop_none"));
        }

        [Test]
        public void AWholeSatelliteIsAboutEightPartsAndThirtyComponents()
        {
            var s = Solo();
            var parts = SessionRules.SelectedParts(s, _data);
            Assert.That(parts.Count, Is.EqualTo(8));
            int comps = parts.Sum(id => _data.Recipe(_data.Part(id))?.Components.Count ?? 0);
            Assert.That(comps, Is.InRange(24, 34));
            Flow.Fly(s, _data);
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
        }

        // ---------------------------------------------------------------- 沙盒

        [Test]
        public void SandboxIsNotAMenuScenarioButCanBeLookedUp()
        {
            Assert.That(_data.Scenarios.Select(s => s.Id), Has.No.Member("sandbox"));
            Assert.That(_data.Difficulties.Select(d => d.Id), Has.No.Member("sandbox"));
            Assert.That(_data.SelectableDifficulties.Select(d => d.Id), Has.No.Member("sandbox"));
            var sc = _data.Scenario("sandbox");
            Assert.That(sc, Is.Not.Null);
            Assert.That(sc.Sandbox && sc.Requirements.Count == 0);
            Assert.That(_data.DifficultyFor(sc).Id, Is.EqualTo("sandbox"));
            Assert.That(sc.AllowedOrbits, Is.EquivalentTo(_data.Orbits.Select(o => o.Id)), "所有軌道都開放");
            Assert.That(_data.LaunchersFor(sc).Select(l => l.Id), Is.EquivalentTo(_data.Launchers.Select(l => l.Id)), "所有火箭都開放");
            foreach (var key in new[] { "ui.brief.speech.sandbox", "objective.open", "ui.objective.open" })
                Assert.That(_data.Strings.ContainsKey(key), key);
        }

        [Test]
        public void SandboxHasNoPresetLimits()
        {
            var s = Solo("sandbox", null);
            Assert.That(s.DifficultyId, Is.EqualTo("sandbox"));
            var sc = s.Scenario(_data);

            // 所有零件（含誘餌、關卡專用的）都選得到，數值不挖空
            var catalog = SessionRules.Catalog(_data, s).Select(p => p.Id).ToList();
            Assert.That(catalog, Is.EquivalentTo(_data.Parts.Where(p => p.Category != PartCategory.GroundStation).Select(p => p.Id)));
            Assert.That(Blanks.Cells(_data, s), Is.Empty);
            Assert.That(Clues.For(_data, s), Is.Empty);

            // 不用組裝（選了就在衛星上）；儀器可以選到 6 台；年數可以拉到 15
            Assert.That(sc.MaxInstruments, Is.EqualTo(6));
            foreach (var id in new[] { "inst_sar", "inst_hyperspectral", "inst_thermal", "inst_multispectral", "inst_co2_spectrometer" })
            {
                Assert.That(SessionRules.NeedsAssembly(s, _data, id), Is.False);
                Ok(s, Command.Of(CommandType.SelectPart, 1, id, 1));
                Assert.That(s.Installed, Has.Member(id));
            }
            Assert.That(s.Bench.Instruments.Count, Is.EqualTo(6));
            Assert.That(Try(s, CommandType.CompPick, 1, "optics"), Is.EqualTo("err.sandbox_no_assembly"));
            Ok(s, Command.Of(CommandType.SetYears, 1, "", 99));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(SessionRules.SandboxMaxYears));

            // 每條軌道、每支火箭都選得到
            foreach (var o in _data.Orbits)
            {
                if (SessionRules.OrbitLauncherCheck(_data, o.Id, s.Bench.LauncherId) != SessionRules.Ok) continue;
                Ok(s, Command.Of(CommandType.SetOrbit, 1, o.Id));
            }
            Ok(s, Command.Of(CommandType.SetOrbit, 1, "sso_700"));
            foreach (var l in _data.Launchers.Where(l => string.IsNullOrEmpty(l.OrbitLock)))
                Ok(s, Command.Of(CommandType.SetLauncher, 1, l.Id));

            // 預算永遠不會是紅燈、也沒有任務需求；不用確定設計就能發射
            var ev = Evaluator.EvaluateDesign(s.Bench, _data, s.DifficultyId, false);
            Assert.That(ev[ResourceId.Budget].Status, Is.EqualTo(ResourceStatus.Ok));
            Assert.That(Departments.Active(sc, s.Difficulty(_data)), Is.Empty);
            Assert.That(SessionRules.LaunchBlockers(s, _data), Is.Empty);
        }

        [Test]
        public void SandboxScoresEveryInstrumentAndOrbitOnItsBestJob()
        {
            var sc = _data.Scenario("sandbox");
            foreach (var p in _data.Parts.Where(p => p.Category == PartCategory.Instrument))
                Assert.That(Evaluator.Match(p, sc.Objective), Is.EqualTo(p.ObjectiveMatch.Values.Max()).Within(1e-12), p.Id);
            foreach (var o in _data.Orbits)
                Assert.That(Evaluator.OrbitMatch(o, sc.Objective), Is.EqualTo(o.ObjectiveMatch.Values.DefaultIfEmpty(0).Max()).Within(1e-12), o.Id);
            // 一般關卡照舊：只看自己的目標
            var cam = _data.Part("inst_visible");
            Assert.That(Evaluator.Match(cam, "earth_observation"), Is.EqualTo(cam.ObjectiveMatch["earth_observation"]));
            Assert.That(Evaluator.Match(cam, "no_such_objective"), Is.EqualTo(0));
        }

        [Test]
        public void SandboxKeepsTheDesignAfterAFailedLaunchAndScoresNoRequirements()
        {
            var s = Solo("sandbox", null);
            Ok(s, Command.Of(CommandType.SelectPart, 1, "batt_large", 1));
            Ok(s, Command.Of(CommandType.Launch, 1));
            s.LastLaunch.Reached = false;
            Ok(s, Command.Of(CommandType.FinishLaunch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Design));
            Assert.That(s.Bench.Get(PartCategory.Battery), Is.EqualTo("batt_large"), "沙盒失敗不重來");
            Assert.That(s.Installed, Has.Member("batt_large"));
            Assert.That(s.Restarts, Is.EqualTo(0));

            // 沒有任務需求：需求分不會因此變成 0
            var d = s.Bench.Clone();
            var ctx = SessionRules.Context(_data, s, 0);
            for (int seed = 1; seed <= 40; seed++)
            {
                var r = Simulator.SimulateMission(d, _data, seed, new PerfectDecisions(), ctx);
                if (!r.ReachedOrbit) continue;
                Assert.That(r.Score.Requirements, Is.Empty);
                Assert.That(r.Score.Needs, Is.EqualTo(100));
                return;
            }
            Assert.Fail("40 個種子都沒入軌");
        }

        [Test]
        public void LoadDesignOnlyWorksInTheSandbox()
        {
            var eo = TestData.Balanced();
            string code = DesignCode.Encode(eo, _data, "balanced");

            var normal = Solo();
            Assert.That(Try(normal, CommandType.LoadDesign, 1, code), Is.EqualTo("err.sandbox_only"));

            var s = Solo("sandbox", null);
            Assert.That(Try(s, CommandType.LoadDesign, 1, "hello"), Is.EqualTo("err.code_format"));
            Ok(s, Command.Of(CommandType.LoadDesign, 1, code));
            Assert.That(s.Bench.ScenarioId, Is.EqualTo("sandbox"));
            Assert.That(s.Bench.Instruments, Is.EqualTo(eo.Instruments));
            foreach (var c in PartCategory.Single) Assert.That(s.Bench.Get(c), Is.EqualTo(eo.Get(c)), c);
            Assert.That(s.Bench.OrbitId, Is.EqualTo(eo.OrbitId));
            Assert.That(s.Bench.LauncherId, Is.EqualTo(eo.LauncherId));
            Assert.That(s.Bench.MissionYears, Is.EqualTo(eo.MissionYears));
            var parts = eo.AllPartIds().Where(id => _data.Part(id).Category != PartCategory.GroundStation).ToList();
            Assert.That(s.Installed, Is.EquivalentTo(parts), "載入的設計整艘在衛星上");
            Assert.That(s.Built, Is.SupersetOf(parts));
            Assert.That(GameDataLoader.ValidateDesign(_data, s.Bench, "loaded"), Is.Empty);
            // 載入之後照樣可以發射
            Ok(s, Command.Of(CommandType.Launch, 1));
            Assert.That(s.Phase, Is.EqualTo(SessionPhase.Launch));
        }

        // ---------------------------------------------------------------- 設計分享碼

        [Test]
        public void ShareCodeRoundTripsEveryKindOfDesign()
        {
            var designs = new List<Design> { TestData.CheapSmallsat(), TestData.Balanced(), TestData.HighScience() };
            designs.AddRange(_data.AllScenarios.Select(s => { var d = s.DefaultDesign.Clone(); d.ScenarioId = s.Id; return d; }));
            // 少一類零件的設計、儀器裝到 6 台
            var bare = TestData.Balanced();
            bare.ScenarioId = "sandbox";
            bare.Parts.Remove(PartCategory.Propulsion);
            bare.Instruments = new List<string> { "inst_sar", "inst_thermal", "inst_visible", "inst_magnetometer", "inst_fire_mwir", "inst_lightning" };
            bare.MissionYears = 15;
            designs.Add(bare);

            foreach (var d in designs)
            {
                string code = DesignCode.Encode(d, _data, "我的設計 #1");
                Assert.That(code, Does.StartWith(DesignCode.Prefix));
                Assert.That(code, Does.Match("^[A-Za-z0-9_-]+$"), "只用貼到哪裡都不會壞的字元");
                Assert.That(code.Length, Is.LessThan(220), "一行貼得完");
                Assert.That(DesignCode.TryDecode("  " + code + "\n", _data, out var back, out var name, out var error), error);
                Assert.That(name, Is.EqualTo("我的設計 #1"));
                Assert.That(back.ScenarioId, Is.EqualTo(d.ScenarioId));
                Assert.That(back.Instruments, Is.EqualTo(d.Instruments));
                foreach (var c in PartCategory.Single) Assert.That(back.Get(c), Is.EqualTo(d.Get(c)), c);
                Assert.That(back.OrbitId, Is.EqualTo(d.OrbitId));
                Assert.That(back.LauncherId, Is.EqualTo(d.LauncherId));
                Assert.That(back.MissionYears, Is.EqualTo(d.MissionYears));
                Assert.That(back.StableHash(), Is.EqualTo(d.StableHash()), "同一個碼 = 同一個設計 = 同樣的模擬結果");
            }
        }

        [Test]
        public void ShareCodeRejectsGarbageAndTypos()
        {
            string code = DesignCode.Encode(TestData.Balanced(), _data, "x");
            foreach (var bad in new[] { null, "", "GNG1-", "GNG2-" + code.Substring(5), "not a code", "GNG1-!!!!", code.Substring(0, code.Length - 6) })
                Assert.That(DesignCode.TryDecode(bad, _data, out var d, out _, out var error), Is.False, bad ?? "null");

            // 改掉中間任何一個字元：不是解不開，就是檢查碼對不上；絕對不會解出另一個設計
            int same = 0;
            for (int i = DesignCode.Prefix.Length; i < code.Length; i++)
            {
                char swap = code[i] == 'A' ? 'B' : 'A';
                string typo = code.Substring(0, i) + swap + code.Substring(i + 1);
                if (DesignCode.TryDecode(typo, _data, out var d, out _, out var error))
                {
                    same++;
                    Assert.That(d.StableHash(), Is.EqualTo(TestData.Balanced().StableHash()), "位置 " + i);
                }
                else Assert.That(new[] { "err.code_format", "err.code_checksum", "err.code_unknown" }, Has.Member(error));
            }
            // base64 最後一個字元的低位元不帶資料，改了也解得出同一份：只允許這一種
            Assert.That(same, Is.LessThanOrEqualTo(1));
            foreach (var key in new[] { "err.code_format", "err.code_checksum", "err.code_unknown", "err.sandbox_only" })
                Assert.That(_data.Strings.ContainsKey(key), key);
        }

        [Test]
        public void ShareCodeNamesAreCleaned()
        {
            Assert.That(DesignCode.CleanName("  a|b\nc  "), Is.EqualTo("a b c"));
            Assert.That(DesignCode.CleanName(new string('x', 80)).Length, Is.EqualTo(DesignCode.MaxName));
            string code = DesignCode.Encode(TestData.Balanced(), _data, "a|b");
            Assert.That(DesignCode.TryDecode(code, _data, out _, out var name, out _), Is.True);
            Assert.That(name, Is.EqualTo("a b"));
        }
    }
}
