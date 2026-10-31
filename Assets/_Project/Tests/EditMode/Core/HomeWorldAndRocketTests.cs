using System;
using NUnit.Framework;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Units;

namespace SpaceSim.Tests
{
    public class HomeWorldAndRocketTests
    {
        static CelestialBodyPreset Home() => HomeSystemCatalog.Bodies().Find(b => b.Id == HomeSystemCatalog.HomeWorldId);

        static PlanetModel HomeModel()
        {
            var h = Home();
            return new PlanetModel("home", h.Physical, PlanetSurfaceSampler.Create(h.Generation, h.Physical.meanRadius), Vector3d.Zero);
        }

        [Test]
        public void HomeWorld_MatchesDesignData()
        {
            var p = Home().Physical;
            Assert.AreEqual(8.27, p.SurfaceGravity, 0.03);
            Assert.AreEqual(4640.0, p.MeanDensity, 30.0);
            Assert.AreEqual(22.5, p.axialTiltDegrees);
            Assert.AreEqual(0.844 * 86400.0, p.siderealRotationPeriod, 1e-6);
            var orbit = new KeplerOrbit(Home().Orbit, HomeSystemCatalog.Star().GravitationalParameter);
            double implied = orbit.ImpliedParentGravitationalParameter / PhysicalConstants.GravitationalConstant;
            Assert.AreEqual(HomeSystemCatalog.Star().mass, implied, HomeSystemCatalog.Star().mass * 0.01, "124 days at 0.67 AU fits the same star");
        }

        [Test]
        public void HomeWorld_LaunchPadIsOnGreenLand()
        {
            var h = Home();
            var sampler = PlanetSurfaceSampler.Create(h.Generation, h.Physical.meanRadius);
            Vector3d pad = PlanetRotation.DirectionFromLatLon(h.LaunchLatitude * MathD.DegToRad, h.LaunchLongitude * MathD.DegToRad);
            SurfaceSample s = sampler.Sample(pad);
            Assert.IsFalse(s.IsWater);
            Assert.That(s.Biome == Biome.Grassland || s.Biome == Biome.Forest, "pad biome: " + s.Biome);
            Assert.Greater(s.Albedo.g, s.Albedo.r, "green");
        }

        [Test]
        public void HomeWorld_HasVioletSeas_AndEquatorialDeserts()
        {
            var h = Home();
            var sampler = PlanetSurfaceSampler.Create(h.Generation, h.Physical.meanRadius);
            var rng = new DeterministicRandom(12);
            int water = 0, violet = 0, eqLand = 0, eqDesert = 0, midLand = 0, midDesert = 0;
            for (int i = 0; i < 6000; i++)
            {
                Vector3d d = rng.UnitVector();
                SurfaceSample s = sampler.Sample(d);
                if (s.IsWater)
                {
                    water++;
                    if (s.Albedo.b > s.Albedo.g && s.Albedo.r > s.Albedo.g) violet++;
                    continue;
                }
                double lat = Math.Abs(Math.Asin(d.z)) * MathD.RadToDeg;
                if (lat < 15) { eqLand++; if (s.Biome == Biome.Desert) eqDesert++; }
                else if (lat > 30 && lat < 50) { midLand++; if (s.Biome == Biome.Desert) midDesert++; }
            }
            Assert.Greater(water, 1000, "has oceans");
            Assert.Greater(violet, water * 0.9, "water is violet");
            Assert.Greater((double)eqDesert / eqLand, 2.0 * midDesert / Math.Max(1, midLand), "deserts concentrate at the equator");
        }

        static FreeFlightSimulation RocketOnPad(out PlanetModel planet, double throttle)
        {
            planet = HomeModel();
            var field = new GravityField();
            field.Add(planet.Gravity);
            var sim = new FreeFlightSimulation(planet, field) { ContactRadius = 1.0 };
            sim.Rocket = new RocketState(new RocketConfiguration()) { Throttle = throttle, EngineOn = true };
            var h = Home();
            sim.PlaceOnSurface(PlanetRotation.DirectionFromLatLon(h.LaunchLatitude * MathD.DegToRad, h.LaunchLongitude * MathD.DegToRad), 0.0);
            return sim;
        }

        [Test]
        public void Rocket_LiftsOffOnlyWhenThrustExceedsWeight()
        {
            var weak = RocketOnPad(out _, 0.5); // TWR ≈ 0.7
            double before = weak.Rocket.CurrentPropellant;
            for (int i = 0; i < 250; i++) weak.Step(0.02);
            Assert.IsTrue(weak.IsLanded, "TWR < 1 stays on the pad");
            Assert.Less(weak.Rocket.CurrentPropellant, before, "but burns propellant");

            var strong = RocketOnPad(out PlanetModel planet, 1.0); // TWR ≈ 1.45
            for (int i = 0; i < 500; i++) strong.Step(0.02);
            Assert.IsFalse(strong.IsLanded);
            double alt = planet.AltitudeAboveSurface(strong.State.Position, strong.State.Time);
            Assert.Greater(alt, 100.0, "climbs after 10 s");
        }

        [Test]
        public void Rocket_FollowsTsiolkovskyInVacuum()
        {
            // Airless planet, no gravity in the field, start far away: pure rocket equation.
            var props = TestPlanets.EarthProperties(rotating: false, atmosphere: false);
            var planet = new PlanetModel("vac", props, null, Vector3d.Zero);
            var sim = new FreeFlightSimulation(planet, new GravityField());
            sim.Rocket = new RocketState(new RocketConfiguration()) { EngineOn = true, PitchDegrees = 90.0, HeadingDegrees = 90.0 };
            double expected = sim.Rocket.DeltaVRemaining();
            sim.SetInertialState(new Vector3d(5.0e10, 0, 0), Vector3d.Zero, 0.0);
            for (int i = 0; i < 100000 && sim.Rocket.HasStages; i++)
            {
                if (!sim.Rocket.CanBurn)
                {
                    sim.Rocket.Separate();
                    sim.Rocket.EngineOn = true;
                    continue;
                }
                sim.Step(0.05);
            }
            Assert.AreEqual(expected, sim.State.Velocity.Magnitude, expected * 0.003, "Δv = Σ Isp·g₀·ln(m₀/m₁)");
        }

        [Test]
        public void Rocket_MassFlowAndIspFollowEngineModel()
        {
            var r = new RocketState(new RocketConfiguration()) { EngineOn = true };
            RocketStage s = r.CurrentStage;
            Assert.AreEqual(s.vacuumThrust / (s.vacuumIsp * PhysicalConstants.StandardGravity), r.MassFlow, 1e-9);
            Assert.AreEqual(s.vacuumThrust, r.Thrust(0.0), 1e-6, "vacuum thrust");
            Assert.Less(r.Thrust(1.0), r.Thrust(0.0), "less thrust at sea level (lower Isp)");
            r.Throttle = 0.0;
            Assert.AreEqual(0.0, r.MassFlow);
        }
    }
}
