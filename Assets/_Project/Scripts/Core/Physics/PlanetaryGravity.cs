using System;
using SpaceSim.Mathematics;
using SpaceSim.Units;

namespace SpaceSim.Physics
{
    /// <summary>A body that produces a gravitational field.</summary>
    public interface IGravitySource
    {
        /// <summary>Standard gravitational parameter μ = GM [m³/s²].</summary>
        double GravitationalParameter { get; }

        /// <summary>Centre of mass in the simulation's inertial frame [m].</summary>
        Vector3d Position { get; }

        /// <summary>Gravitational acceleration [m/s²] at <paramref name="point"/> (inertial frame).</summary>
        Vector3d AccelerationAt(Vector3d point);
    }

    /// <summary>
    /// Newtonian point-mass gravity of a spherically symmetric planet:
    /// a = −μ·r⃗/|r⃗|³, |a| = μ/r². Outside a spherically symmetric body this is exact (shell theorem).
    /// Not modelled: oblateness (J2), mass concentrations, tides, relativistic corrections. Planet rotation
    /// does not change this field; centrifugal effects appear naturally because motion is integrated in an
    /// inertial frame.
    /// </summary>
    public sealed class PlanetaryGravity : IGravitySource
    {
        /// <param name="gravitationalParameter">μ = GM [m³/s²]</param>
        /// <param name="meanRadius">Reference radius [m]; used for the inside-the-body guard.</param>
        public PlanetaryGravity(double gravitationalParameter, double meanRadius, Vector3d position)
        {
            if (!(gravitationalParameter > 0.0)) throw new ArgumentOutOfRangeException(nameof(gravitationalParameter));
            if (!(meanRadius > 0.0)) throw new ArgumentOutOfRangeException(nameof(meanRadius));
            GravitationalParameter = gravitationalParameter;
            MeanRadius = meanRadius;
            Position = position;
        }

        public double GravitationalParameter { get; }
        public double MeanRadius { get; }
        public Vector3d Position { get; set; }

        public static double GravitationalParameterFromMass(double massKg) => PhysicalConstants.GravitationalConstant * massKg;

        /// <summary>Magnitude of gravitational acceleration g(r) = μ/r² [m/s²] at distance r from the centre.</summary>
        public static double AccelerationMagnitude(double mu, double distanceFromCentre) =>
            mu / (distanceFromCentre * distanceFromCentre);

        /// <summary>Surface gravity of this body at its reference radius [m/s²].</summary>
        public double SurfaceGravity => AccelerationMagnitude(GravitationalParameter, MeanRadius);

        public Vector3d AccelerationAt(Vector3d point)
        {
            Vector3d r = point - Position;
            double r2 = r.SqrMagnitude;
            double minR = 1.0e-3 * MeanRadius;
            if (r2 < minR * minR)
            {
                // Inside a uniform sphere g grows linearly with r; guarding the singularity at the centre.
                // A body can only get here through an invalid state, but we must not return NaN/∞.
                return r * (-GravitationalParameter / (MeanRadius * MeanRadius * MeanRadius));
            }
            double invR = 1.0 / Math.Sqrt(r2);
            return r * (-GravitationalParameter * invR * invR * invR);
        }

        /// <summary>Gravitational force on mass m: F = G·M·m/r² = μ·m/r² [N] (vector).</summary>
        public Vector3d ForceOn(Vector3d point, double massKg) => AccelerationAt(point) * massKg;
    }
}
