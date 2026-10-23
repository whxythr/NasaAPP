namespace SpaceSim.Units
{
    /// <summary>
    /// Physical constants in SI units. Planet-specific values (radius, mass, μ, atmosphere)
    /// deliberately do NOT live here — they belong to each planet's data.
    /// </summary>
    public static class PhysicalConstants
    {
        /// <summary>Newtonian constant of gravitation G [m³·kg⁻¹·s⁻²] (CODATA 2018).</summary>
        public const double GravitationalConstant = 6.67430e-11;

        /// <summary>Standard gravity g₀ [m/s²]. Used only as a unit (e.g. for specific impulse), never as planet gravity.</summary>
        public const double StandardGravity = 9.80665;

        /// <summary>Molar gas constant R [J·mol⁻¹·K⁻¹].</summary>
        public const double MolarGasConstant = 8.314462618;

        /// <summary>Astronomical unit [m].</summary>
        public const double AstronomicalUnit = 1.495978707e11;

        /// <summary>Stefan–Boltzmann constant σ [W·m⁻²·K⁻⁴].</summary>
        public const double StefanBoltzmann = 5.670374419e-8;
    }
}
