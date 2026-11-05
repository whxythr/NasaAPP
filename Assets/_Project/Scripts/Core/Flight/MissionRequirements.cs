using System;
using SpaceSim.Physics;

namespace SpaceSim.Flight
{
    /// <summary>A Level A (ideal) Δv requirement with an honest note about what it leaves out.</summary>
    public readonly struct MissionTarget
    {
        public MissionTarget(string name, double idealDeltaV, string notes)
        {
            Name = name;
            IdealDeltaV = idealDeltaV;
            Notes = notes;
        }

        public string Name { get; }
        /// <summary>Ideal Δv [m/s]: impulsive burns, no atmosphere, no gravity losses.</summary>
        public double IdealDeltaV { get; }
        public string Notes { get; }
    }

    /// <summary>
    /// Ideal Δv requirements from a planet's surface, built only from central Level A functions
    /// (<see cref="OrbitalMechanics"/>, <see cref="TransferCalculator"/>). These are lower bounds — a real ascent through an
    /// atmosphere with finite thrust needs more (gravity and drag losses, see FlightRecorder).
    /// </summary>
    public static class MissionRequirements
    {
        /// <summary>Straight up to altitude h and fall back: v = √(2μ(1/R − 1/(R+h))).</summary>
        public static double SuborbitalHop(double mu, double radius, double altitude) =>
            Math.Sqrt(2.0 * mu * (1.0 / radius - 1.0 / (radius + altitude)));

        /// <summary>
        /// Circular orbit at altitude h, launched due east: ideal two-burn path from the surface (a Hohmann ellipse from R to R+h,
        /// first burn credited with the ground's rotational speed v_rot = ω·R·cos φ).
        /// </summary>
        public static double LowOrbit(double mu, double radius, double altitude, double surfaceRotationSpeed)
        {
            HohmannTransfer h = TransferCalculator.Hohmann(mu, radius, radius + altitude);
            return Math.Max(0.0, h.DepartureSpeed - surfaceRotationSpeed) + h.DeltaV2;
        }

        /// <summary>Escape from the surface, launched due east: v_esc(R) − v_rot.</summary>
        public static double Escape(double mu, double radius, double surfaceRotationSpeed) =>
            Math.Max(0.0, OrbitalMechanics.EscapeVelocity(mu, radius) - surfaceRotationSpeed);

        /// <summary>Standard targets for the hangar's "flight capability" estimate.</summary>
        public static MissionTarget[] StandardTargets(double mu, double radius, double surfaceRotationSpeed, double spaceBoundary, bool hasAtmosphere)
        {
            string losses = hasAtmosphere
                ? "Идеально. Реальный подъём через атмосферу требует больше: обычно +1.0…2.0 км/с на потери."
                : "Идеально. Без атмосферы потери — в основном гравитационные (зависят от тяги/веса).";
            double orbitAlt = Math.Max(spaceBoundary * 2.0, 50000.0);
            return new[]
            {
                new MissionTarget("Подъём на " + (spaceBoundary / 1000.0).ToString("0") + " км (суборбитально)", SuborbitalHop(mu, radius, spaceBoundary), losses),
                new MissionTarget("Круговая орбита " + (orbitAlt / 1000.0).ToString("0") + " км", LowOrbit(mu, radius, orbitAlt, surfaceRotationSpeed), losses),
                new MissionTarget("Уход с планеты", Escape(mu, radius, surfaceRotationSpeed), losses)
            };
        }
    }
}
