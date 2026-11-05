using System;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Planets;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    public class HangarTests
    {
        const double HomeGravity = 8.27;

        static RocketDesignReport Analyze(RocketBlueprint b, double required = 0.0) =>
            RocketDesignAnalysis.Analyze(BlueprintCompiler.Compile(b).Configuration, HomeGravity, required);

        [Test]
        public void DefaultBlueprint_ReproducesMilestone1Rocket()
        {
            var compiled = BlueprintCompiler.Compile(RocketBlueprint.CreateDefault());
            var reference = new RocketConfiguration(); // the hard-coded M1 rocket
            Assert.AreEqual(reference.stages.Length, compiled.Configuration.stages.Length);
            for (int i = 0; i < reference.stages.Length; i++)
            {
                RocketStage a = reference.stages[i], b = compiled.Configuration.stages[i];
                Assert.AreEqual(a.dryMass, b.dryMass, 1e-9, "stage " + i + " dry");
                Assert.AreEqual(a.propellantMass, b.propellantMass, 1e-9);
                Assert.AreEqual(a.vacuumThrust, b.vacuumThrust, 1e-6);
                Assert.AreEqual(a.vacuumIsp, b.vacuumIsp);
                Assert.AreEqual(a.seaLevelIsp, b.seaLevelIsp);
                Assert.AreEqual(a.length, b.length, 1e-9);
            }
            Assert.AreEqual(reference.payloadMass, compiled.PayloadMass, 1e-9);
            Assert.IsEmpty(compiled.Issues);
            Assert.AreEqual(340000.0, compiled.UsableFuel);
            Assert.AreEqual(340000.0, compiled.MaxFuelCapacity);
        }

        [Test]
        public void MoreFuelOnFirstStage_MeansMoreDeltaVButHeavierLaunch()
        {
            var b = RocketBlueprint.CreateDefault();
            var before = Analyze(b);
            b.stages[0].tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK10, propellantLoaded = 10000 });
            var after = Analyze(b);
            Assert.Greater(after.TotalDeltaVVacuum, before.TotalDeltaVVacuum);
            Assert.Greater(after.LaunchMass, before.LaunchMass);
            Assert.Less(after.LiftoffTwr, before.LiftoffTwr, "heavier rocket, same engines");
            Assert.AreEqual(before.LaunchMass + 10800.0, after.LaunchMass, 1e-6, "tank dry + propellant");
        }

        [Test]
        public void MoreFuelOnUpperStage_CanReduceTotalDeltaV()
        {
            // Counter-intuitive but physical: stage 2 gains Δv, yet stage 1 must now lift 10.8 t more and loses more than that.
            var b = RocketBlueprint.CreateDefault();
            var before = Analyze(b);
            b.stages[1].tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK10, propellantLoaded = 10000 });
            var after = Analyze(b);
            Assert.Greater(after.Stages[1].DeltaVVacuum, before.Stages[1].DeltaVVacuum, "upper stage gains");
            Assert.Less(after.Stages[0].DeltaVVacuum, before.Stages[0].DeltaVVacuum, "first stage loses");
            Assert.Less(after.TotalDeltaVVacuum, before.TotalDeltaVVacuum, "net loss for this design");
        }

        [Test]
        public void PartialFuel_ChangesMassAndDeltaV()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[0].tanks[0].propellantLoaded = 50000; // half-fill one tank
            var c = BlueprintCompiler.Compile(b);
            Assert.AreEqual(290000.0, c.UsableFuel);
            Assert.AreEqual(340000.0, c.MaxFuelCapacity);
            Assert.Less(Analyze(b).TotalDeltaVVacuum, Analyze(RocketBlueprint.CreateDefault()).TotalDeltaVVacuum);
        }

        [Test]
        public void Overfilling_LoadsOnlyCapacity_AndWarns()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[1].tanks[0].propellantLoaded = 55000;
            var c = BlueprintCompiler.Compile(b);
            Assert.AreEqual(40000.0, c.Configuration.stages[1].propellantMass);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.FuelCapacityExceeded && i.StageIndex == 1));
        }

        [Test]
        public void WrongPropellant_IsDeadWeight()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[1].tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankH20, propellantLoaded = 20000 }); // hydrogen on a kerosene engine
            var c = BlueprintCompiler.Compile(b);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.IncompatiblePropellant));
            Assert.AreEqual(20000.0, c.UnusableFuel);
            Assert.AreEqual(40000.0, c.Configuration.stages[1].propellantMass, "only kerolox is burnable");
            Assert.AreEqual(4000.0 + 2000.0 + 20000.0, c.Configuration.stages[1].dryMass, 1e-9, "tank + its fuel ride as dry mass");
            Assert.Less(Analyze(b).TotalDeltaVVacuum, Analyze(RocketBlueprint.CreateDefault()).TotalDeltaVVacuum, "dead weight costs Δv");
        }

        [Test]
        public void OverweightRocket_CannotLiftOff_ButIsStillBuilt()
        {
            var b = RocketBlueprint.CreateDefault();
            for (int i = 0; i < 4; i++) b.stages[0].tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK100, propellantLoaded = 100000 });
            var report = Analyze(b);
            Assert.IsFalse(report.CanLiftOff);
            Assert.IsTrue(report.Warnings.Exists(w => w.Code == DesignWarningCode.CannotLiftOff));
            Assert.AreEqual(2, BlueprintCompiler.Compile(b).Configuration.stages.Length, "not refused — consequences are explained");
        }

        [Test]
        public void MissingEngine_AndTooManyEngines_AreReported()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[1].engineId = "";
            Assert.IsTrue(Analyze(b).Warnings.Exists(w => w.Code == DesignWarningCode.StageWithoutEngine));
            b.stages[0].engineCount = 20;
            var c = BlueprintCompiler.Compile(b);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.TooManyEngines));
            Assert.AreEqual(BlueprintCompiler.MaxEnginesPerStage * 1.30e6, c.Configuration.stages[0].vacuumThrust, 1e-3);
        }

        [Test]
        public void VacuumEngineOnFirstStage_IsExplained()
        {
            var b = RocketBlueprint.CreateDefault();
            b.stages[0].engineId = ModuleCatalog.EngineVega;
            var c = BlueprintCompiler.Compile(b);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.VacuumEngineOnFirstStage));
        }

        [Test]
        public void PayloadModules_AddMassAndCapabilities()
        {
            var b = RocketBlueprint.CreateDefault();
            var bare = BlueprintCompiler.Compile(b);
            Assert.IsFalse(bare.Capabilities.Has(ModuleCapability.OrbitPrediction));
            b.payloadModules.Add(ModuleCatalog.NavComputer);
            b.payloadModules.Add(ModuleCatalog.CollectorBasic);
            b.payloadModules.Add(ModuleCatalog.RadiationShield);
            var c = BlueprintCompiler.Compile(b);
            Assert.IsTrue(c.Capabilities.Has(ModuleCapability.OrbitPrediction));
            Assert.AreEqual(1, c.Capabilities.RadiationShielding);
            Assert.AreEqual(10.0, c.Capabilities.SampleCapacity);
            Assert.IsTrue(c.Capabilities.CanCollect(SampleKinds.Rock));
            Assert.IsFalse(c.Capabilities.CanCollect(SampleKinds.Liquid), "liquids need the advanced collector");
            Assert.AreEqual(1000.0 + 120 + 150 + 450, c.PayloadMass, 1e-9);
            Assert.Less(RocketDesignAnalysis.Analyze(c.Configuration, HomeGravity).TotalDeltaVVacuum,
                RocketDesignAnalysis.Analyze(bare.Configuration, HomeGravity).TotalDeltaVVacuum, "every kilogram costs Δv");
        }

        [Test]
        public void ProbeCore_IsMandatory_AndLockedModulesNeedPurchase()
        {
            var b = RocketBlueprint.CreateDefault();
            b.payloadModules.Clear();
            var c = BlueprintCompiler.Compile(b);
            Assert.IsTrue(c.Issues.Exists(i => i.Code == BlueprintIssueCode.MissingCore));
            Assert.AreEqual(1000.0, c.PayloadMass);

            b = RocketBlueprint.CreateDefault();
            b.stages[0].tanks.Add(new TankLoad { moduleId = ModuleCatalog.TankK140, propellantLoaded = 140000 });
            var locked = BlueprintCompiler.Compile(b, id => false);
            Assert.IsTrue(locked.Issues.Exists(i => i.Code == BlueprintIssueCode.LockedModule));
            Assert.AreEqual(300000.0, locked.Configuration.stages[0].propellantMass, "unbought tank not installed");
            var owned = BlueprintCompiler.Compile(b, id => true);
            Assert.AreEqual(440000.0, owned.Configuration.stages[0].propellantMass);
        }

        [Test]
        public void Blueprint_CloneIsDeep()
        {
            var a = RocketBlueprint.CreateDefault();
            var b = a.Clone();
            b.stages[0].tanks[0].propellantLoaded = 1;
            b.payloadModules.Add(ModuleCatalog.NavComputer);
            Assert.AreEqual(100000.0, a.stages[0].tanks[0].propellantLoaded);
            Assert.AreEqual(1, a.payloadModules.Count);
        }

        [Test]
        public void MissionRequirements_UseCentralOrbitalFunctions()
        {
            var home = HomeSystemCatalog.Bodies().Find(x => x.Id == HomeSystemCatalog.HomeWorldId).Physical;
            double mu = home.GravitationalParameter, R = home.meanRadius;
            double vRot = 2.0 * Math.PI / home.siderealRotationPeriod * R * Math.Cos(28.0 * Math.PI / 180.0);
            double hop = MissionRequirements.SuborbitalHop(mu, R, 100000.0);
            Assert.AreEqual(Math.Sqrt(2.0 * mu * (1.0 / R - 1.0 / (R + 100000.0))), hop, 1e-9);
            double orbit = MissionRequirements.LowOrbit(mu, R, 200000.0, vRot);
            var h = SpaceSim.Physics.TransferCalculator.Hohmann(mu, R, R + 200000.0);
            Assert.AreEqual(h.DepartureSpeed - vRot + h.DeltaV2, orbit, 1e-9);
            Assert.Greater(orbit, SpaceSim.Physics.OrbitalMechanics.CircularVelocity(mu, R + 200000.0) - vRot, "climbing costs extra energy");
            Assert.AreEqual(SpaceSim.Physics.OrbitalMechanics.EscapeVelocity(mu, R) - vRot, MissionRequirements.Escape(mu, R, vRot), 1e-9);
            // Default rocket: ideal Δv covers the ideal orbit with margin for real losses.
            Assert.Greater(Analyze(RocketBlueprint.CreateDefault()).TotalDeltaVVacuum, orbit + 2000.0);
        }
    }
}
