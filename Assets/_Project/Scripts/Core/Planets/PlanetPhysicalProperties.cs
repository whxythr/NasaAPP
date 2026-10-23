using System;
using System.Collections.Generic;
using SpaceSim.Physics;
using SpaceSim.Units;

namespace SpaceSim.Planets
{
    public enum GravitationalParameterSource
    {
        /// <summary>μ = G·M from <see cref="PlanetPhysicalProperties.mass"/>. Editing mass changes gravity.</summary>
        FromMass,
        /// <summary>Use the measured μ directly (known far more precisely than G or M separately).</summary>
        Measured
    }

    /// <summary>
    /// Physical characteristics of a planet in SI units. Independent from any visual model: neither an imported
    /// mesh nor a procedural surface ever changes these values. Defaults are Earth.
    /// </summary>
    [Serializable]
    public class PlanetPhysicalProperties
    {
        /// <summary>Mean (reference) radius R [m]. Altitudes are measured above this sphere.</summary>
        public double meanRadius = 6371000.0;
        /// <summary>Mass M [kg].</summary>
        public double mass = 5.9722e24;
        public GravitationalParameterSource gravitySource = GravitationalParameterSource.FromMass;
        /// <summary>Measured μ = GM [m³/s²], used when <see cref="gravitySource"/> is Measured.</summary>
        public double measuredGravitationalParameter = 3.986004418e14;

        /// <summary>Sidereal rotation period [s]; negative = retrograde, 0 = no rotation.</summary>
        public double siderealRotationPeriod = 86164.0905;
        /// <summary>Axial tilt (obliquity) [deg].</summary>
        public double axialTiltDegrees = 23.44;
        /// <summary>Prime meridian angle at t = 0 [deg].</summary>
        public double rotationPhaseAtEpochDegrees = 0.0;

        public AtmosphereProperties atmosphere = new AtmosphereProperties();

        /// <summary>
        /// Conventional "edge of space" altitude [m] (Kármán line for Earth). Used ONLY for HUD notifications
        /// and flight-state hints — physics never changes here, density is not cut off at this altitude.
        /// </summary>
        public double spaceBoundaryAltitude = 100000.0;

        /// <summary>μ actually used by the simulation [m³/s²].</summary>
        public double GravitationalParameter => gravitySource == GravitationalParameterSource.Measured
            ? measuredGravitationalParameter
            : PhysicalConstants.GravitationalConstant * mass;

        /// <summary>g at the reference radius, g = μ/R² [m/s²] (pure gravitation, rotation not included).</summary>
        public double SurfaceGravity => GravitationalParameter / (meanRadius * meanRadius);

        /// <summary>Mean density M / (4/3·π·R³) [kg/m³].</summary>
        public double MeanDensity => mass / (4.0 / 3.0 * Math.PI * meanRadius * meanRadius * meanRadius);

        /// <summary>Relative difference between G·M and the measured μ (consistency check of the data).</summary>
        public double GravitationalParameterMismatch =>
            measuredGravitationalParameter > 0.0
                ? Math.Abs(PhysicalConstants.GravitationalConstant * mass - measuredGravitationalParameter) / measuredGravitationalParameter
                : double.NaN;

        public PlanetPhysicalProperties Clone()
        {
            var c = (PlanetPhysicalProperties)MemberwiseClone();
            c.atmosphere = atmosphere != null ? atmosphere.Clone() : new AtmosphereProperties();
            return c;
        }

        /// <summary>Returns human-readable problems; empty when the data is usable.</summary>
        public List<string> Validate()
        {
            var issues = new List<string>();
            if (!(meanRadius > 0.0)) issues.Add("Mean radius must be positive.");
            if (!(GravitationalParameter > 0.0)) issues.Add("Gravitational parameter (μ) must be positive (check mass).");
            if (atmosphere != null && atmosphere.hasAtmosphere)
            {
                if (atmosphere.surfaceDensity < 0.0) issues.Add("Atmosphere surface density must not be negative.");
                if (!(atmosphere.scaleHeight > 0.0)) issues.Add("Atmosphere scale height must be positive.");
            }
            if (spaceBoundaryAltitude < 0.0) issues.Add("Space boundary altitude must not be negative.");
            return issues;
        }

        public static PlanetPhysicalProperties CreateEarth() => new PlanetPhysicalProperties();
    }
}
