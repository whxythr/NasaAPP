using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Planets;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    public class EconomyTests
    {
        static PlayerProfile NewProfile() => PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId);

        [Test]
        public void NewProfile_HasStartingCreditsAndFreeModulesOnly()
        {
            var p = NewProfile();
            Assert.AreEqual(PlayerProfile.StartingCredits, p.credits);
            Assert.IsTrue(p.Owns(ModuleCatalog.EngineSokol), "free modules are always available");
            Assert.IsFalse(p.Owns(ModuleCatalog.TankK140), "shop modules must be bought");
            Assert.Contains(HomeSystemCatalog.HomeWorldId, p.visitedBodies);
            Assert.AreEqual(2, p.blueprint.stages.Count);
        }

        [Test]
        public void Purchase_DeductsCredits_AndUnlocksModuleInCompiler()
        {
            var p = NewProfile();
            Assert.AreEqual(PurchaseResult.Success, Shop.TryPurchase(p, ModuleCatalog.TankK140, out _));
            Assert.AreEqual(PlayerProfile.StartingCredits - 6000, p.credits);
            Assert.IsTrue(p.Owns(ModuleCatalog.TankK140));
            Assert.AreEqual(PurchaseResult.AlreadyOwned, Shop.TryPurchase(p, ModuleCatalog.TankK140, out _));

            var bp = RocketBlueprint.CreateDefault();
            bp.stages[0].tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK140, propellantLoaded = 140000 });
            Assert.AreEqual(440000.0, BlueprintCompiler.Compile(bp, p.Owns).Configuration.stages[0].propellantMass, "bought tank really adds fuel");
            Assert.AreEqual(300000.0, BlueprintCompiler.Compile(bp, NewProfile().Owns).Configuration.stages[0].propellantMass, "not for others");
        }

        [Test]
        public void FailedPurchases_LeaveProfileUnchanged()
        {
            var p = NewProfile();
            Assert.AreEqual(PurchaseResult.RequirementsNotMet, Shop.TryPurchase(p, ModuleCatalog.RadiationShield, out string msg));
            StringAssert.Contains("Первый выход в космос", msg);
            Assert.AreEqual(PurchaseResult.NotForSale, Shop.TryPurchase(p, ModuleCatalog.EngineSokol, out _));

            ResearchMilestones.Award(p, ResearchMilestones.ReachSpace);
            ResearchMilestones.Award(p, ResearchMilestones.EscapeHome);
            p.credits = 100;
            Assert.AreEqual(PurchaseResult.InsufficientFunds, Shop.TryPurchase(p, ModuleCatalog.RadiationShieldHeavy, out _));
            Assert.AreEqual(100, p.credits);
            Assert.IsFalse(p.ownedModules.Contains(ModuleCatalog.RadiationShieldHeavy));
            Assert.AreEqual(ShopItemState.Unaffordable, Shop.StateOf(p, Shop.Find(ModuleCatalog.RadiationShieldHeavy)));
        }

        [Test]
        public void EveryShopItemIsAPurchasableModuleWithAnEffect()
        {
            foreach (ShopItem item in Shop.Items)
            {
                Assert.IsNotNull(item.Module);
                Assert.IsTrue(item.Module.requiresPurchase, item.Module.id);
                Assert.Greater(item.Price, 0);
                Assert.IsFalse(string.IsNullOrEmpty(item.Effect));
                if (item.RequiredMilestone != null) Assert.IsNotNull(ResearchMilestones.Find(item.RequiredMilestone));
            }
        }

        [Test]
        public void Milestones_RewardOnce()
        {
            var p = NewProfile();
            var m = ResearchMilestones.Award(p, ResearchMilestones.FirstOrbit);
            Assert.IsNotNull(m);
            Assert.AreEqual(PlayerProfile.StartingCredits + m.CreditReward, p.credits);
            Assert.AreEqual(m.ResearchReward, p.researchPoints);
            Assert.IsNull(ResearchMilestones.Award(p, ResearchMilestones.FirstOrbit), "only once");
            Assert.AreEqual(PlayerProfile.StartingCredits + m.CreditReward, p.credits);
            Assert.AreEqual(m.CreditReward, p.lifetimeEarnings);
        }

        [Test]
        public void Market_PricesByRarityAndAnalysis_AndSells()
        {
            var p = NewProfile();
            var common = new SampleRecord { id = "a", basePrice = 100, rarity = SampleRarity.Common, scientificValue = 2 };
            var rare = new SampleRecord { id = "b", basePrice = 100, rarity = SampleRarity.Rare, scientificValue = 5, analysed = true, analysisMultiplier = 1.5 };
            Assert.AreEqual(100, Market.SellPrice(common));
            Assert.AreEqual(1050, Market.SellPrice(rare), "100 × 7 (rare) × 1.5 (spectrometer)");
            p.samples.Add(common);
            p.samples.Add(rare);
            Assert.IsFalse(Market.Sell(p, "b", out _), "still in the rocket: not delivered yet");
            Assert.AreEqual(2, p.DeliverCargo());
            Assert.IsTrue(Market.Sell(p, "b", out int credits));
            Assert.AreEqual(1050, credits);
            Assert.AreEqual(PlayerProfile.StartingCredits + 1050, p.credits);
            Assert.AreEqual(7.5, p.researchPoints, 1e-9);
            Assert.AreEqual(1, p.samples.Count);
            Assert.IsFalse(Market.Sell(p, "missing", out _));
        }

        [Test]
        public void Migration_FixesOldProfiles_AndRefusesNewerOnes()
        {
            var old = new PlayerProfile { version = 0, ownedModules = null, samples = null, settings = null, blueprint = null };
            Assert.AreEqual(MigrationResult.Migrated, ProfileMigration.Migrate(old));
            Assert.AreEqual(PlayerProfile.CurrentVersion, old.version);
            Assert.IsNotNull(old.ownedModules);
            Assert.AreEqual(2, old.blueprint.stages.Count);

            var withGhost = NewProfile();
            withGhost.ownedModules.Add("module-from-the-future");
            Assert.AreEqual(MigrationResult.Migrated, ProfileMigration.Migrate(withGhost));
            Assert.IsEmpty(withGhost.ownedModules);

            Assert.AreEqual(MigrationResult.UpToDate, ProfileMigration.Migrate(NewProfile()));
            Assert.AreEqual(MigrationResult.TooNew, ProfileMigration.Migrate(new PlayerProfile { version = 99 }));
        }

        [Test]
        public void Hazards_ProtectionLevelsMatter()
        {
            var teonan = new EnvironmentHazards { radiationLevel = 2 };
            HazardExposure bare = HazardModel.Evaluate(teonan, 0.0, 0, 0);
            HazardExposure r1 = HazardModel.Evaluate(teonan, 0.0, 1, 0);
            HazardExposure r2 = HazardModel.Evaluate(teonan, 0.0, 2, 0);
            Assert.AreEqual(2, bare.RadiationExcess);
            Assert.AreEqual(0.06 / 60.0, bare.DegradationPerSecond, 1e-12);
            Assert.AreEqual(0.01 / 60.0, r1.DegradationPerSecond, 1e-12);
            Assert.IsTrue(r2.IsSafe);

            var mora = new EnvironmentHazards { radiationLevel = 1, acidLevel = 1 };
            Assert.AreEqual(0, HazardModel.Evaluate(mora, 0.5, 0, 0).Radiation, "dense air shields radiation");
            Assert.AreEqual(1, HazardModel.Evaluate(mora, 0.5, 0, 0).AcidExcess, "acid acts inside the air");
            Assert.IsTrue(HazardModel.Evaluate(mora, 0.5, 0, 1).IsSafe, "acid coating protects");
            Assert.AreEqual(0, HazardModel.Evaluate(mora, 0.0, 0, 0).Acid, "no acid in space");
        }

        [Test]
        public void EquipmentDegrades_AndInstrumentsGoOffline()
        {
            var c = new EquipmentCondition();
            var exposure = HazardModel.Evaluate(new EnvironmentHazards { radiationLevel = 2 }, 0.0, 0, 0);
            for (int i = 0; i < 400; i++) c.Apply(exposure, 1.0); // 400 s at 6 %/min = 40 %
            Assert.AreEqual(0.6, c.Integrity, 1e-9);
            Assert.IsTrue(c.InstrumentsOnline);
            for (int i = 0; i < 200; i++) c.Apply(exposure, 1.0);
            Assert.Less(c.Integrity, HazardModel.InstrumentThreshold);
            Assert.IsFalse(c.InstrumentsOnline);
            c.Reset();
            Assert.AreEqual(1.0, c.Integrity);
        }

        [Test]
        public void Catalog_HazardsMatchTheWorlds()
        {
            var bodies = HomeSystemCatalog.Bodies();
            Assert.AreEqual(0, bodies.Find(b => b.Id == HomeSystemCatalog.HomeWorldId).Hazards.radiationLevel, "home is safe");
            Assert.AreEqual(2, bodies.Find(b => b.Id == HomeSystemCatalog.TeonanId).Hazards.radiationLevel);
            Assert.AreEqual(1, bodies.Find(b => b.Id == HomeSystemCatalog.HermeousMoraId).Hazards.acidLevel);
        }
    }
}
