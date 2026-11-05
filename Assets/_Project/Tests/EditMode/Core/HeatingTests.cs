using NUnit.Framework;
using SpaceSim.Physics;

namespace SpaceSim.Tests
{
    /// <summary>Milestone 7: the heating estimate that drives the re-entry glow.</summary>
    public class HeatingTests
    {
        [Test]
        public void SuttonGraves_KnownValue_AndScaling()
        {
            // ρ = 10⁻⁴ kg/m³, v = 7 km/s, r_n = 1 m: q̇ = 1.7415e-4 · 0.01 · 3.43e11 ≈ 5.97e5 W/m².
            Assert.AreEqual(5.973e5, AtmosphericHeating.StagnationHeatFlux(1e-4, 7000.0, 1.0), 1e3);
            double q = AtmosphericHeating.StagnationHeatFlux(1e-4, 3500.0, 1.0);
            Assert.AreEqual(1.0 / 8.0, q / AtmosphericHeating.StagnationHeatFlux(1e-4, 7000.0, 1.0), 1e-12, "∝ v³");
            Assert.AreEqual(0.5, AtmosphericHeating.StagnationHeatFlux(1e-4, 7000.0, 4.0) / AtmosphericHeating.StagnationHeatFlux(1e-4, 7000.0, 1.0), 1e-12, "∝ 1/√r_n");
            Assert.AreEqual(0.0, AtmosphericHeating.StagnationHeatFlux(0.0, 7000.0, 1.0), "no air, no heating");
            Assert.AreEqual(1900.0, AtmosphericHeating.EquilibriumTemperature(6e5), 60.0, "≈ 1900 K glows orange");
        }
    }
}
