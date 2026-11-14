using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace GoNoGo.Core.Tests
{
    /// <summary>從磁碟讀遊戲資料（Unity 編輯器與 dotnet test 都能用）。</summary>
    public static class TestData
    {
        const string Rel = "Assets/GoNoGo/Data/Resources/GameData";
        static GameData _cached;

        public static string Dir
        {
            get
            {
                foreach (var start in new[] { Directory.GetCurrentDirectory(), TestContext.CurrentContext.TestDirectory, AppDomain.CurrentDomain.BaseDirectory })
                {
                    var d = new DirectoryInfo(start);
                    while (d != null)
                    {
                        var p = Path.Combine(d.FullName, Rel);
                        if (Directory.Exists(p)) return p;
                        d = d.Parent;
                    }
                }
                throw new DirectoryNotFoundException("找不到 " + Rel);
            }
        }

        public static Dictionary<string, string> Files()
        {
            var files = new Dictionary<string, string>();
            foreach (var name in GameDataLoader.FileNames)
                files[name] = File.ReadAllText(Path.Combine(Dir, name + ".json"));
            return files;
        }

        /// <summary>共用一份（資料是唯讀的）。</summary>
        public static GameData Load() => _cached ??= GameDataLoader.Load(Files());

        public static Design Eo(string[] instruments, string solar, string battery, string antenna, string gs, string computer,
            string adcs, string propulsion, string shielding, string orbit, string launcher, int years = 3) => new Design
        {
            ScenarioId = "earth_observation",
            Instruments = new List<string>(instruments),
            Parts = new Dictionary<string, string>
            {
                [PartCategory.Solar] = solar,
                [PartCategory.Battery] = battery,
                [PartCategory.Antenna] = antenna,
                [PartCategory.GroundStation] = gs,
                [PartCategory.Computer] = computer,
                [PartCategory.Adcs] = adcs,
                [PartCategory.Propulsion] = propulsion,
                [PartCategory.Shielding] = shielding,
            },
            OrbitId = orbit,
            LauncherId = launcher,
            MissionYears = years,
        };

        /// <summary>便宜小衛星：可見光相機、小火箭。</summary>
        public static Design CheapSmallsat() => Eo(new[] { "inst_visible" },
            "solar_body", "batt_small", "ant_xband", "gs_3", "obc_cubesat", "adcs_compact", "prop_none", "shield_std",
            "sso_700", "electron");

        /// <summary>中型均衡型：多光譜 + 熱紅外，仍然用小火箭。</summary>
        public static Design Balanced() => Eo(new[] { "inst_multispectral", "inst_thermal" },
            "solar_deployable", "batt_medium", "ant_xband", "gs_3", "obc_mass_memory", "adcs_wheels", "prop_none", "shield_std",
            "sso_700", "electron");

        /// <summary>昂貴高科學型：高光譜 + 多光譜 + 熱紅外，中型火箭。</summary>
        public static Design HighScience() => Eo(new[] { "inst_hyperspectral", "inst_multispectral", "inst_thermal" },
            "solar_large", "batt_medium", "ant_xband", "gs_3", "obc_mass_memory", "adcs_precision", "prop_none", "shield_std",
            "sso_700", "falcon9");
    }

    /// <summary>照指定函式回應每個事件。</summary>
    public sealed class Scripted : IDecisionSource
    {
        readonly System.Func<MissionEvent, EventResponse> _f;
        public Scripted(System.Func<MissionEvent, EventResponse> f) { _f = f; }
        public EventResponse Respond(MissionEvent e) => _f(e);

        /// <summary>判斷正確，但只做處置手冊的前幾步。</summary>
        public static Scripted Partial(int steps) => new Scripted(e => new EventResponse
        {
            Judged = OpsEvents.Key(e.Kind),
            Actions = Procedures.Required(e.Kind).Take(steps).ToList(),
        });

        /// <summary>每個事件都判斷成同一種，並照那一種的手冊操作。</summary>
        public static Scripted AlwaysTreatAs(EventKind k) => new Scripted(e => new EventResponse
        {
            Judged = OpsEvents.Key(k),
            Actions = Procedures.Required(k).ToList(),
        });
    }
}
