using System.IO;
using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Planets;
using SpaceSim.Runtime;
using SpaceSim.Vehicles;
using UnityEngine;

namespace SpaceSim.Tests
{
    /// <summary>Save/load of the player profile through Unity's JsonUtility, in a temporary folder.</summary>
    public class PlayerSessionTests
    {
        string dir;
        GameObject go;

        [SetUp]
        public void SetUp()
        {
            dir = Path.Combine(Path.GetTempPath(), "SpaceSimSessionTest_" + System.Guid.NewGuid().ToString("N"));
            PlayerSession.SaveDirectoryOverride = dir;
        }

        [TearDown]
        public void TearDown()
        {
            if (go != null) Object.DestroyImmediate(go);
            PlayerSession.SaveDirectoryOverride = null;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        PlayerSession NewSession()
        {
            if (go != null) Object.DestroyImmediate(go);
            go = new GameObject("session");
            var s = go.AddComponent<PlayerSession>();
            s.Initialize(HomeSystemCatalog.HomeWorldId);
            return s;
        }

        [Test]
        public void Transactions_SurviveRestart()
        {
            var s = NewSession();
            Assert.AreEqual(PurchaseResult.Success, s.Purchase(ModuleCatalog.TankK140, out _));
            s.Award(ResearchMilestones.ReachSpace);
            var design = RocketBlueprint.CreateDefault();
            design.stages[1].tanks[0].propellantLoaded = 12345;
            design.payloadModules.Add(ModuleCatalog.NavComputer);
            s.SetBlueprint(design);
            s.Profile.samples.Add(new SampleRecord { id = "s1", name = "Базальт", planetId = "teonan", mass = 1.5, basePrice = 200 });
            s.SetUnits(SpaceSim.Units.DisplayUnitSystem.Kilometric);
            int credits = s.Profile.credits;

            var restarted = NewSession(); // reads the file written above
            var p = restarted.Profile;
            Assert.AreEqual(credits, p.credits);
            Assert.IsTrue(p.Owns(ModuleCatalog.TankK140));
            Assert.IsTrue(p.HasMilestone(ResearchMilestones.ReachSpace));
            Assert.AreEqual(12345.0, p.blueprint.stages[1].tanks[0].propellantLoaded);
            Assert.Contains(ModuleCatalog.NavComputer, p.blueprint.payloadModules);
            Assert.AreEqual("Базальт", p.samples[0].name);
            Assert.AreEqual(SpaceSim.Units.DisplayUnitSystem.Kilometric, p.settings.units);
            Assert.IsFalse(File.Exists(PlayerSession.SavePath + ".tmp"), "atomic save leaves no temp file");
        }

        [Test]
        public void CorruptSave_IsBackedUp_AndANewProfileStarts()
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(PlayerSession.SavePath, "{ this is not json");
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("повреждено"));
            var s = NewSession();
            Assert.AreEqual(PlayerProfile.StartingCredits, s.Profile.credits);
            Assert.AreEqual(1, Directory.GetFiles(dir, "profile.json.corrupt-*").Length, "the broken file is kept");
        }

        [Test]
        public void SaveFromNewerVersion_IsNeverOverwritten()
        {
            Directory.CreateDirectory(dir);
            var future = PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId);
            future.version = 99;
            future.credits = 777;
            string json = JsonUtility.ToJson(future);
            File.WriteAllText(PlayerSession.SavePath, json);
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("новой версией"));
            var s = NewSession();
            Assert.IsTrue(s.ReadOnly);
            Assert.AreEqual(PurchaseResult.ReadOnlyProfile, s.Purchase(ModuleCatalog.TankK140, out _), "no trading on a read-only profile");
            Assert.IsFalse(s.Profile.ownedModules.Contains(ModuleCatalog.TankK140));
            Assert.AreEqual(json, File.ReadAllText(PlayerSession.SavePath), "the file stays untouched");
        }
    }
}
