using System;

namespace SpaceSim.Physics
{
    /// <summary>
    /// Aerodynamic heating at the nose (Level A estimate used for the HUD and the glow effect; no thermal damage model).
    /// Sutton–Graves stagnation-point convective heat flux: q̇ = k·√(ρ/r_n)·v³ [W/m²], k = 1.7415·10⁻⁴ kg^½/m (air).
    /// For CO₂/SO₂ atmospheres k is ~10 % higher; the air value is used for every planet (documented approximation).
    /// </summary>
    public static class AtmosphericHeating
    {
        public const double SuttonGravesAir = 1.7415e-4;

        /// <summary>Stagnation-point heat flux [W/m²] at density ρ [kg/m³], air-relative speed v [m/s], nose radius r_n [m].</summary>
        public static double StagnationHeatFlux(double density, double airSpeed, double noseRadius)
        {
            if (density <= 0.0 || airSpeed <= 0.0 || noseRadius <= 0.0) return 0.0;
            return SuttonGravesAir * Math.Sqrt(density / noseRadius) * airSpeed * airSpeed * airSpeed;
        }

        /// <summary>Radiative equilibrium temperature of the nose for that flux: T = (q̇/(ε·σ))^¼ [K] (ε = 0.85).</summary>
        public static double EquilibriumTemperature(double heatFlux, double emissivity = 0.85) =>
            heatFlux > 0.0 ? Math.Pow(heatFlux / (emissivity * Units.PhysicalConstants.StefanBoltzmann), 0.25) : 0.0;
    }
}
