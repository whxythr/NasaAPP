using NUnit.Framework;
using SpaceSim.Persistence;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;

namespace SpaceSim.Tests
{
    public class PersistenceTests
    {
        static PlanetSaveData Capture(int seed) => PlanetSaveValidator.Capture("test-planet", "Test", new PlanetGenerationSettings { seed = seed },
            PlanetPhysicalProperties.CreateEarth(), new AtmosphereOpticalProperties(), 0, 0, "2026-01-01T00:00:00Z");

        [Test]
        public void CapturedPlanet_ValidatesAndReproduces()
        {
            var data = Capture(321);
            var result = PlanetSaveValidator.Validate(data);
            Assert.IsTrue(result.IsValid, result.Message);
            Assert.AreEqual(data.surfaceFingerprint, result.ActualFingerprint);
            Assert.AreEqual(PlanetSurfaceSampler.CurrentAlgorithmVersion, data.surfaceAlgorithmVersion);
        }

        [Test]
        public void TamperedSurface_IsDetected()
        {
            var data = Capture(321);
            data.generation.mountainAmplitude += 10.0; // what a silently changed generator would look like
            Assert.AreEqual(PlanetSaveStatus.FingerprintMismatch, PlanetSaveValidator.Validate(data).Status);
        }

        [Test]
        public void FutureAlgorithmVersion_IsRejectedNotRegenerated()
        {
            var data = Capture(1);
            data.surfaceAlgorithmVersion = 2;
            data.generation.algorithmVersion = 2;
            Assert.AreEqual(PlanetSaveStatus.UnsupportedAlgorithmVersion, PlanetSaveValidator.Validate(data).Status);
        }

        [Test]
        public void PhysicalGenerator_IsDeterministicAndSelfConsistent()
        {
            var a = PlanetPhysicalGenerator.Generate(2024, null);
            var b = PlanetPhysicalGenerator.Generate(2024, null);
            var c = PlanetPhysicalGenerator.Generate(2025, null);
            Assert.AreEqual(a.meanRadius, b.meanRadius);
            Assert.AreEqual(a.mass, b.mass);
            Assert.AreEqual(a.atmosphere.surfaceDensity, b.atmosphere.surfaceDensity);
            Assert.AreNotEqual(a.meanRadius, c.meanRadius);
            Assert.IsEmpty(a.Validate());
            if (a.atmosphere.hasAtmosphere)
            {
                double rho = AtmosphericModel.IdealGasDensity(a.atmosphere.surfacePressure, a.atmosphere.meanMolarMass, a.atmosphere.surfaceTemperature);
                Assert.AreEqual(rho, a.atmosphere.surfaceDensity, rho * 1e-12);
            }
        }
    }
}
