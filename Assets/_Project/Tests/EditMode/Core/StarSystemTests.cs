using System;
using NUnit.Framework;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;

namespace SpaceSim.Tests
{
    public class StarSystemTests
    {
        static CelestialBodyPreset Body(string id) => HomeSystemCatalog.Bodies().Find(b => b.Id == id);

        [TestCase(HomeSystemCatalog.TeonanId, 1.24e23, 1896325.0, 4350.0, 2.31)]
        [TestCase(HomeSystemCatalog.QuorisId, 2.82e22, 1175730.0, 4140.0, 1.36)]
        [TestCase(HomeSystemCatalog.HermeousMoraId, 1.46e25, 8209166.0, 6310.0, 14.5)]
        public void Planets_MatchDesignData(string id, double mass, double radius, double density, double g)
        {
            var p = Body(id).Physical;
            Assert.AreEqual(mass, p.mass, mass * 1e-9);
            Assert.AreEqual(radius, p.meanRadius, 1e-6);
            Assert.AreEqual(density, p.MeanDensity, density * 0.01, "ρ = M / (4/3·π·R³)");
            Assert.AreEqual(g, p.SurfaceGravity, g * 0.01, "g = GM/R²");
            Assert.IsEmpty(p.Validate());
        }

        [Test]
        public void StarMass_FromKepler_IsConsistentForAllPlanets()
        {
            double mu = HomeSystemCatalog.Star().GravitationalParameter;
            foreach (var b in HomeSystemCatalog.Bodies())
            {
                if (b.ParentId != null) continue;
                var orbit = new KeplerOrbit(b.Orbit, mu);
                double impliedMass = orbit.ImpliedParentGravitationalParameter / Units.PhysicalConstants.GravitationalConstant;
                Assert.AreEqual(HomeSystemCatalog.Star().mass, impliedMass, HomeSystemCatalog.Star().mass * 0.01, b.Id);
            }
            Assert.AreEqual(2.6, HomeSystemCatalog.Star().mass / 1.98847e30, 0.05, "≈ 2.6 solar masses");
        }

        [Test]
        public void Quoris_IsTidallyLocked()
        {
            var b = Body(HomeSystemCatalog.QuorisId);
            var orbit = new KeplerOrbit(b.Orbit, 1.0);
            var rot = new PlanetRotation(b.Physical.siderealRotationPeriod, 0.0, b.Physical.rotationPhaseAtEpochDegrees * MathD.DegToRad);
            foreach (double t in new[] { 0.0, 2.0e5, 7.0e5, 3.0e6 })
            {
                Vector3d toStar = (-orbit.Position(t)).Normalized;
                Vector3d primeMeridian = rot.FixedToInertial(Vector3d.UnitX, t);
                Assert.Greater(Vector3d.Dot(toStar, primeMeridian), 0.9999, "the same face always looks at the star");
            }
        }

        [Test]
        public void Moon_OrbitsAt30000km_AndIsNotSpherical()
        {
            var moon = Body(HomeSystemCatalog.MoraMoonId);
            var parent = Body(HomeSystemCatalog.HermeousMoraId);
            var orbit = new KeplerOrbit(moon.Orbit, parent.Physical.GravitationalParameter);
            Assert.AreEqual(3.0e7, orbit.Position(1234.0).Magnitude, 1.0);
            Assert.AreEqual(2.0 * Math.PI * Math.Sqrt(Math.Pow(3.0e7, 3) / parent.Physical.GravitationalParameter), orbit.Period, 1e-6);

            var sampler = PlanetSurfaceSampler.Create(moon.Generation, moon.Physical.meanRadius);
            double min = double.MaxValue, max = double.MinValue;
            var rng = new DeterministicRandom(3);
            for (int i = 0; i < 3000; i++)
            {
                double h = sampler.SurfaceHeight(rng.UnitVector());
                min = Math.Min(min, h);
                max = Math.Max(max, h);
            }
            Assert.Greater((max - min) / moon.Physical.meanRadius, 0.15, "relief spans >15 % of the radius: irregular shape");
            Assert.Greater(moon.Physical.meanRadius + sampler.MinSurfaceHeight, 0.0);
        }

        [Test]
        public void KeplerOrbit_EllipseHasCorrectApsidesAndPeriodicity()
        {
            var o = new KeplerOrbit(new OrbitProperties { semiMajorAxis = 1e10, eccentricity = 0.3, period = 1000.0 }, 1.0);
            Assert.AreEqual(0.7e10, o.Position(0.0).Magnitude, 1.0, "starts at periapsis a(1−e)");
            Assert.AreEqual(1.3e10, o.Position(500.0).Magnitude, 1.0, "apoapsis half a period later");
            Assert.Less((o.Position(1000.0) - o.Position(0.0)).Magnitude, 1e-3);
        }

        [Test]
        public void HermeousMora_HazeAndThickAtmosphere()
        {
            var b = Body(HomeSystemCatalog.HermeousMoraId);
            var profile = AtmosphericModel.Create(b.Physical.atmosphere);
            Assert.Greater(profile.ReferenceDensity, 50.0, "very dense (≈ 80 kg/m³)");
            double shell = AtmosphereOptics.VisualShellHeight(profile, b.Optics);
            Assert.GreaterOrEqual(shell, b.Optics.hazeAltitude + 3.0 * b.Optics.hazeThickness, "shell contains the haze layer");
            // Sunlight through a long grazing path ends up bluer than it started: bluish-white sunsets.
            double R = b.Physical.meanRadius;
            Vector3d t = AtmosphereOptics.Transmittance(profile, b.Optics, R, R + shell, new Vector3d(R + 2.0, 0, 0),
                new Vector3d(0.1, 1.0, 0).Normalized);
            Assert.Greater(t.z, t.x);
        }

        [Test]
        public void RockyStyle_IsDeterministic_AndFeatureCratersDig()
        {
            var q = Body(HomeSystemCatalog.QuorisId);
            var a = PlanetSurfaceSampler.Create(q.Generation, q.Physical.meanRadius);
            var b = PlanetSurfaceSampler.Create(q.Generation, q.Physical.meanRadius);
            Assert.AreEqual(PlanetFingerprint.Compute(a), PlanetFingerprint.Compute(b));

            var f = q.Generation.featureCraters[0];
            Vector3d center = PlanetRotation.DirectionFromLatLon(f.latitudeDegrees * MathD.DegToRad, f.longitudeDegrees * MathD.DegToRad);
            var noHeart = q.Generation.Clone();
            noHeart.featureCraters = new FeatureCrater[0];
            var c = PlanetSurfaceSampler.Create(noHeart, q.Physical.meanRadius);
            Assert.Less(a.TerrainHeight(center), c.TerrainHeight(center) - 5000.0, "the heart craters are deep");
            Biome biome = a.Sample(center).Biome;
            Assert.IsTrue(biome == Biome.Desert || biome == Biome.Rock, "rocky style uses rock/desert biomes only");
        }

        [Test]
        public void TerrestrialSurface_UnchangedByNewFields()
        {
            // Defaults of the new rocky-style fields must not alter existing (terrestrial) planets.
            var s = new PlanetGenerationSettings { seed = 42 };
            var withDefaults = PlanetFingerprint.Compute(PlanetSurfaceSampler.Create(s, 6371000.0));
            var tweakedRocky = s.Clone();
            tweakedRocky.lowland = new ColorRGB(1, 0, 0);
            tweakedRocky.stainCoverage = 0.9;
            Assert.AreEqual(withDefaults, PlanetFingerprint.Compute(PlanetSurfaceSampler.Create(tweakedRocky, 6371000.0)));
        }
    }
}
