using System;
using System.Collections.Generic;
using NUnit.Framework;
using SpaceSim.Economy;
using SpaceSim.Exploration;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Vehicles;

namespace SpaceSim.Tests
{
    /// <summary>Milestone 6: landing physics and verdicts, walking on planets, sample sites, EVA rules, cargo delivery.</summary>
    public class ExplorationTests
    {
        static CelestialBodyPreset Preset(string id) => HomeSystemCatalog.Bodies().Find(b => b.Id == id);

        sealed class Waves : ISurfaceHeightProvider
        {
            public double SurfaceHeight(Vector3d d) => 50.0 * Math.Sin(d.x * 3000.0) * Math.Cos(d.y * 2000.0);
            public double MinSurfaceHeight => -50.0;
            public double MaxSurfaceHeight => 50.0;
        }

        // ---- Landing --------------------------------------------------------------------------------------------

        [Test]
        public void LandingFormulas_AnalyticCases()
        {
            Assert.AreEqual(125.0, LandingPhysics.StoppingDistance(50.0, 20.0, 10.0), 1e-12, "v²/(2(a−g))");
            Assert.IsTrue(double.IsPositiveInfinity(LandingPhysics.StoppingDistance(50.0, 9.0, 10.0)), "thrust below weight cannot stop");
            Assert.AreEqual(Math.Sqrt(20.0), LandingPhysics.TimeToImpact(100.0, 0.0, 10.0), 1e-12);
            Assert.AreEqual(Math.Sqrt(2000.0), LandingPhysics.ImpactSpeed(100.0, 0.0, 10.0), 1e-12);
            Assert.AreEqual(0.0, LandingPhysics.TerrainSlope(ReferenceSphereSurface.Instance, 6.4e6, Vector3d.UnitX), 1e-9, "flat sphere");
        }

        [Test]
        public void LandingVerdict_SoftHardCrash_WithReasons()
        {
            Assert.AreEqual(LandingResult.Soft, LandingPhysics.Assess(-3.0, 1.0, 5.0, 4.0).Result);
            LandingOutcome hard = LandingPhysics.Assess(-9.0, 1.0, 5.0, 4.0);
            Assert.AreEqual(LandingResult.Hard, hard.Result);
            StringAssert.Contains("9", hard.Reason);
            Assert.AreEqual(LandingResult.Crash, LandingPhysics.Assess(-20.0, 0.0, 0.0, 0.0).Result);
            Assert.AreEqual(LandingResult.Crash, LandingPhysics.Assess(-2.0, 12.0, 0.0, 0.0).Result, "sideways");
            Assert.AreEqual(LandingResult.Crash, LandingPhysics.Assess(-2.0, 0.0, 60.0, 0.0).Result, "on its side");
            Assert.AreEqual(LandingResult.Hard, LandingPhysics.Assess(-2.0, 0.0, 0.0, 20.0).Result, "steep slope");
        }

        [Test]
        public void SimulatedFall_HitsAtTheFormulaSpeed()
        {
            // Airless Teonan, a probe released 200 m up at rest relative to the ground: impact speed = √(2gh) (drag-free).
            CelestialBodyPreset t = Preset(HomeSystemCatalog.TeonanId); // slow rotation (tidally locked, 5.9 d): Coriolis negligible
            var planet = new PlanetModel(t.DisplayName, t.Physical, ReferenceSphereSurface.Instance, Vector3d.Zero);
            var sim = TestPlanets.Simulation(planet);
            sim.ContactRadius = 0.0;
            sim.SetLocalState(0.3, 0.2, 200.0, 0.0, 0.0, 0.0, true, 0.0);
            for (int i = 0; i < 2000 && !sim.IsLanded; i++) sim.Step(0.02);
            Assert.IsTrue(sim.IsLanded);
            double g = planet.Gravity.SurfaceGravity;
            Assert.AreEqual(LandingPhysics.ImpactSpeed(200.0, 0.0, g), -sim.LastImpact.VerticalSpeed, 0.1, "simulation = Level A free fall");
            Assert.AreEqual(LandingResult.Crash, LandingPhysics.Assess(sim.LastImpact.VerticalSpeed, 0.0, 0.0, 0.0).Result,
                "√(2·2.31·200) ≈ 30 m/s — far beyond the gear's 15 m/s");
        }

        // ---- Walking ----------------------------------------------------------------------------------------------

        static SurfaceWalker Walker(string bodyId, ISurfaceHeightProvider surface = null, Func<Vector3d, bool> water = null)
        {
            CelestialBodyPreset b = Preset(bodyId);
            return new SurfaceWalker(b.Physical.GravitationalParameter, b.Physical.meanRadius, surface ?? ReferenceSphereSurface.Instance,
                new Vector3d(1, 0, 0), water);
        }

        [Test]
        public void Walking_ScalesWithGravity_FroudeNumber()
        {
            SurfaceWalker home = Walker(HomeSystemCatalog.HomeWorldId), teonan = Walker(HomeSystemCatalog.TeonanId);
            home.PlaceAt(home.Anchor);
            teonan.PlaceAt(teonan.Anchor);
            Assert.AreEqual(Math.Sqrt(0.25 * home.Gravity * 0.9), home.WalkSpeed, 1e-12);
            Assert.Greater(home.WalkSpeed, teonan.WalkSpeed, "weaker gravity — slower stride (and weaker grip)");
            for (int i = 0; i < 1000; i++) home.Step(0.01, 1.0, 0.0, 0.0, false, false);
            Assert.AreEqual(home.WalkSpeed * 10.0, home.DistanceWalked, 0.2, "10 s of walking ≈ v·t (short acceleration phase)");
            Assert.IsTrue(home.OnGround);
            Assert.AreEqual(home.Radius, home.Position.Magnitude, 1e-6, "feet stay on the reference sphere");
        }

        [Test]
        public void Jump_ReachesV2Over2g_OnEachWorld()
        {
            foreach (string id in new[] { HomeSystemCatalog.HomeWorldId, HomeSystemCatalog.TeonanId, HomeSystemCatalog.MoraMoonId })
            {
                SurfaceWalker w = Walker(id);
                w.PlaceAt(w.Anchor);
                w.Step(0.001, 0, 0, 0, false, true);
                int steps = 0;
                while (!w.OnGround && steps++ < 2000000) w.Step(0.001, 0, 0, 0, false, false);
                Assert.IsTrue(w.OnGround, id + " lands again");
                Assert.AreEqual(w.JumpHeight, w.JumpApex, w.JumpHeight * 0.02, id + ": apex = v²/(2g)");
                double flight = steps * 0.001;
                Assert.AreEqual(2.0 * SurfaceWalker.JumpSpeed / w.Gravity, flight, 0.02 * flight + 0.01, id + ": time in the air = 2v/g");
            }
        }

        [Test]
        public void Walker_FollowsTerrain_StopsAtWater_AndTheTetherHolds()
        {
            SurfaceWalker hills = Walker(HomeSystemCatalog.HomeWorldId, new Waves());
            hills.PlaceAt(hills.Anchor);
            for (int i = 0; i < 2000; i++) hills.Step(0.01, 1.0, 0.3, 45.0, true, false);
            Vector3d d = hills.Position.Normalized;
            Assert.AreEqual(hills.Radius + new Waves().SurfaceHeight(d), hills.Position.Magnitude, 1e-6, "on the ground of the hills");

            SurfaceWalker shore = Walker(HomeSystemCatalog.HomeWorldId, null, dir => dir.z > 2e-5); // water north of a line ~127 m away
            shore.PlaceAt(shore.Anchor);
            for (int i = 0; i < 30000; i++) shore.Step(0.01, 1.0, 0.0, 0.0, true, false); // run north
            Assert.LessOrEqual(shore.Position.Normalized.z, 2e-5 + 1e-9, "stopped at the water");
            Assert.AreEqual("впереди вода", shore.Blocked);

            SurfaceWalker far = Walker(HomeSystemCatalog.HomeWorldId);
            far.PlaceAt(far.Anchor);
            for (int i = 0; i < 60000; i++) far.Step(0.01, 1.0, 0.0, 90.0, true, false);
            Assert.LessOrEqual(far.DistanceToAnchor, SurfaceWalker.TetherLength + 1e-6);
        }

        // ---- Samples ------------------------------------------------------------------------------------------------

        [Test]
        public void SampleSites_AreDeterministic_AndPlanetSpecific()
        {
            var dir = new Vector3d(0.3, 0.5, 0.8).Normalized;
            List<SampleSite> a = SampleSiteGenerator.Around(HomeSystemCatalog.HermeousMoraId, 6.0e6, dir);
            List<SampleSite> b = SampleSiteGenerator.Around(HomeSystemCatalog.HermeousMoraId, 6.0e6, dir);
            Assert.AreEqual(a.Count, b.Count);
            Assert.AreEqual(9 * SampleSiteGenerator.SitesPerCell, a.Count);
            var ids = new HashSet<string>();
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Id, b[i].Id);
                Assert.AreEqual(a[i].Name, b[i].Name);
                Assert.AreEqual(a[i].Mass, b[i].Mass);
                Assert.IsTrue(ids.Add(a[i].Id), "unique id");
                Assert.Greater(a[i].Mass, 0.0);
                Assert.Greater(a[i].BasePrice, 0);
                Assert.Less((a[i].FixedDirection - dir).Magnitude * 6.0e6, 1500.0, "within the 3×3 cells");
            }
            // Over many places Mora yields acids, home yields water only where the ground is water.
            int acids = 0, water = 0;
            var rnd = new SpaceSim.Procedural.DeterministicRandom(7);
            for (int k = 0; k < 40; k++)
            {
                Vector3d d = rnd.UnitVector();
                foreach (var s in SampleSiteGenerator.Around(HomeSystemCatalog.HermeousMoraId, 6.0e6, d))
                    if (s.Category == SampleCategory.Acid) acids++;
                foreach (var s in SampleSiteGenerator.Around(HomeSystemCatalog.HomeWorldId, 6.37e6, d, x => x.z < 0.0))
                {
                    if (s.Category == SampleCategory.Water)
                    {
                        water++;
                        Assert.Less(s.FixedDirection.z, 0.0, "water samples only on water");
                    }
                    Assert.AreNotEqual(SampleCategory.Acid, s.Category, "no acid at home");
                }
            }
            Assert.Greater(acids, 0);
            Assert.Greater(water, 0);
        }

        [Test]
        public void Collection_NeedsTheRightCollector_Capacity_AndOnlyOnce()
        {
            var acid = new SampleSite { Id = "x:acid", Category = SampleCategory.Acid, Mass = 0.5, Required = SampleKinds.Liquid | SampleKinds.Acid };
            var rock = new SampleSite { Id = "x:rock", Category = SampleCategory.Rock, Mass = 3.0, Required = SampleKinds.Rock };
            var none = new VehicleCapabilities();
            var basic = new VehicleCapabilities { SampleCapacity = 10, SampleKinds = SampleKinds.Rock | SampleKinds.Soil };
            var advanced = new VehicleCapabilities { SampleCapacity = 30, SampleKinds = SampleKinds.Rock | SampleKinds.Soil | SampleKinds.Liquid | SampleKinds.Acid | SampleKinds.Rare };
            StringAssert.Contains("ПБ-1", SampleCollection.Check(rock, none, 0, null).Reason);
            Assert.IsTrue(SampleCollection.Check(rock, basic, 0, null).Ok);
            StringAssert.Contains("ПБ-2", SampleCollection.Check(acid, basic, 0, null).Reason);
            Assert.IsTrue(SampleCollection.Check(acid, advanced, 0, null).Ok);
            StringAssert.Contains("Нет места", SampleCollection.Check(rock, basic, 8.0, null).Reason);
            Assert.IsFalse(SampleCollection.Check(rock, basic, 0, new List<string> { "x:rock" }).Ok);

            var withSpectrometer = new VehicleCapabilities { Flags = ModuleCapability.SampleAnalysis, ResearchMultiplier = 1.5, SampleCapacity = 10, SampleKinds = SampleKinds.Rock };
            SampleRecord r = SampleCollection.ToRecord(rock, withSpectrometer);
            Assert.IsTrue(r.analysed);
            Assert.AreEqual(1.5, r.analysisMultiplier);
            Assert.IsFalse(r.delivered);
        }

        [Test]
        public void Cargo_IsDeliveredHome_OrLostWithTheRocket()
        {
            var p = PlayerProfile.CreateNew(HomeSystemCatalog.HomeWorldId);
            var rock = new SampleSite { Id = "q:1", Name = "Гематит", Category = SampleCategory.Mineral, Rarity = SampleRarity.Uncommon, Mass = 1.2, BasePrice = 200, Required = SampleKinds.Rock };
            Assert.IsTrue(p.AddCargo(SampleCollection.ToRecord(rock, null)));
            Assert.IsFalse(p.AddCargo(SampleCollection.ToRecord(rock, null)), "the same site twice");
            Assert.AreEqual(1.2, p.CargoMass(), 1e-12);
            Assert.IsFalse(Market.Sell(p, "q:1", out _), "not sellable before delivery");
            Assert.AreEqual(1, p.DeliverCargo());
            Assert.AreEqual(0.0, p.CargoMass());
            Assert.IsTrue(Market.Sell(p, "q:1", out int credits));
            Assert.AreEqual(500, credits, "200 × 2.5 (uncommon)");

            Assert.IsTrue(p.AddCargo(SampleCollection.ToRecord(new SampleSite { Id = "q:2", Mass = 1.0, Required = SampleKinds.Rock }, null)));
            Assert.AreEqual(1, p.LoseCargo());
            Assert.AreEqual(0, p.samples.Count);
            Assert.Contains("q:2", p.collectedSites, "a lost sample's site stays taken");
        }

        [Test]
        public void Advisor_TellsWhenToStartTheLandingBurn()
        {
            // 100 m/s down, engine off; thrust 20 m/s², g = 8 → d = v²/(2(a−g)) = 417 m; the lesson fires inside 1.5·d + 300 m.
            var s = new SpaceSim.Academy.AcademySituation
            {
                Time = 1, HasRocket = true, CurrentStageHasFuel = true, AnyPropellantLeft = true, StageCount = 1, Mass = 10000, AvailableThrust = 200000,
                LocalGravity = 8.0, RadialVelocity = -100.0, AltitudeAboveSurface = 2000.0, Mu = 3.4e14, Radius = 6.37e6, SpaceBoundary = 1e5, Phase = FlightPhase.Landing
            };
            var advisor = new SpaceSim.Academy.AcademyAdvisor();
            Assert.IsFalse(advisor.Evaluate(s).Exists(h => h.Id == SpaceSim.Academy.AcademyAdvisor.LandingBurn), "high enough");
            s.AltitudeAboveSurface = 800.0;
            s.Time = 2;
            var hint = advisor.Evaluate(s).Find(h => h.Id == SpaceSim.Academy.AcademyAdvisor.LandingBurn);
            Assert.IsNotNull(hint);
            Assert.IsTrue(hint.SolutionSteps.Exists(x => x.Contains(SpaceSim.Academy.AcademyNumber.Format(LandingPhysics.StoppingDistance(100, 20, 8)))));
        }

        [Test]
        public void Eva_AllowedWhereASuitSurvives()
        {
            CelestialBodyPreset home = Preset(HomeSystemCatalog.HomeWorldId), mora = Preset(HomeSystemCatalog.HermeousMoraId);
            CelestialBodyPreset teonan = Preset(HomeSystemCatalog.TeonanId), quoris = Preset(HomeSystemCatalog.QuorisId);
            Assert.IsTrue(SampleCollection.CanExit(home.Physical, home.Hazards).Ok);
            Assert.IsTrue(SampleCollection.CanExit(quoris.Physical, quoris.Hazards).Ok);
            StringAssert.Contains("Давление", SampleCollection.CanExit(mora.Physical, mora.Hazards).Reason, "74 atm, 300 °C, acid");
            StringAssert.Contains("Радиация", SampleCollection.CanExit(teonan.Physical, teonan.Hazards).Reason);
        }
    }
}
