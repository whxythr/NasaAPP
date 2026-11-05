using System;

namespace SpaceSim.Planets
{
    /// <summary>
    /// Environmental danger levels of a body (0 = harmless, 1 = moderate, 2 = severe). Compared with the vehicle's
    /// protection levels by <see cref="HazardModel"/>.
    /// </summary>
    [Serializable]
    public class EnvironmentHazards
    {
        /// <summary>Radiation in the body's vicinity above any dense air (star proximity, no magnetosphere).</summary>
        public int radiationLevel;
        /// <summary>Corrosive (acidic) atmosphere/surface chemistry, acting inside the air.</summary>
        public int acidLevel;

        public EnvironmentHazards Clone() => (EnvironmentHazards)MemberwiseClone();
    }

    /// <summary>Hazard exposure at one moment (Level B: depends on where the vehicle actually is).</summary>
    public readonly struct HazardExposure
    {
        public HazardExposure(int radiation, int acid, int radiationExcess, int acidExcess, double degradationPerSecond)
        {
            Radiation = radiation;
            Acid = acid;
            RadiationExcess = radiationExcess;
            AcidExcess = acidExcess;
            DegradationPerSecond = degradationPerSecond;
        }

        public int Radiation { get; }
        public int Acid { get; }
        /// <summary>Radiation level not covered by shielding.</summary>
        public int RadiationExcess { get; }
        public int AcidExcess { get; }
        /// <summary>Loss of equipment integrity per simulated second (fraction of 1).</summary>
        public double DegradationPerSecond { get; }
        public bool IsSafe => RadiationExcess == 0 && AcidExcess == 0;
    }

    /// <summary>
    /// Simple, explicit hazard rules (gameplay model, not a dosimetry simulation):
    /// • radiation acts where the air is thin (ρ/ρ₀ &lt; 1 %) — a thick atmosphere shields;
    /// • acid acts inside the atmosphere where ρ/ρ₀ &gt; 10⁻⁴;
    /// • each unprotected level degrades equipment: 1 level ≈ 1 %/min, 2 levels ≈ 6 %/min (simulated time).
    /// </summary>
    public static class HazardModel
    {
        public const double ThinAirRatio = 0.01;
        public const double AcidAirRatio = 1e-4;
        /// <summary>Integrity below this takes payload instruments offline.</summary>
        public const double InstrumentThreshold = 0.5;

        static readonly double[] RatePerLevel = { 0.0, 0.01 / 60.0, 0.06 / 60.0, 0.15 / 60.0 };

        public static HazardExposure Evaluate(EnvironmentHazards hazards, double densityRatio, int radiationShielding, int acidResistance)
        {
            if (hazards == null) return new HazardExposure(0, 0, 0, 0, 0.0);
            int radiation = densityRatio < ThinAirRatio ? hazards.radiationLevel : 0;
            int acid = densityRatio > AcidAirRatio ? hazards.acidLevel : 0;
            int rx = Math.Max(0, radiation - radiationShielding);
            int ax = Math.Max(0, acid - acidResistance);
            double rate = Rate(rx) + Rate(ax);
            return new HazardExposure(radiation, acid, rx, ax, rate);
        }

        static double Rate(int excess) => RatePerLevel[Math.Min(Math.Max(excess, 0), RatePerLevel.Length - 1)];
    }

    /// <summary>Integrity of the payload equipment (1 = intact). Reset with every new rocket.</summary>
    public sealed class EquipmentCondition
    {
        public double Integrity { get; private set; } = 1.0;
        public HazardExposure LastExposure { get; private set; }
        public bool InstrumentsOnline => Integrity >= HazardModel.InstrumentThreshold;

        public void Reset()
        {
            Integrity = 1.0;
            LastExposure = default;
        }

        /// <summary>Mechanical damage (e.g. a hard landing): integrity drops by <paramref name="fraction"/>.</summary>
        public void Damage(double fraction) => Integrity = Math.Max(0.0, Integrity - Math.Max(0.0, fraction));

        public void Apply(HazardExposure exposure, double dt)
        {
            LastExposure = exposure;
            if (exposure.DegradationPerSecond > 0.0) Integrity = Math.Max(0.0, Integrity - exposure.DegradationPerSecond * dt);
        }
    }
}
