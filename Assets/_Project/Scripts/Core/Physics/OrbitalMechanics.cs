using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Physics
{
    /// <summary>
    /// Osculating two-body orbit derived from an instantaneous state (r⃗, v⃗) relative to one primary.
    /// Ideal two-body model: no atmosphere, no other bodies, spherical primary.
    /// </summary>
    public readonly struct OrbitalElements
    {
        public OrbitalElements(double specificEnergy, double specificAngularMomentum, double eccentricity,
            double semiMajorAxis, double periapsisRadius, double apoapsisRadius, double period,
            double inclination, double trueAnomaly)
        {
            SpecificEnergy = specificEnergy;
            SpecificAngularMomentum = specificAngularMomentum;
            Eccentricity = eccentricity;
            SemiMajorAxis = semiMajorAxis;
            PeriapsisRadius = periapsisRadius;
            ApoapsisRadius = apoapsisRadius;
            Period = period;
            Inclination = inclination;
            TrueAnomaly = trueAnomaly;
        }

        /// <summary>ε = v²/2 − μ/r [J/kg].</summary>
        public double SpecificEnergy { get; }
        /// <summary>h = |r⃗ × v⃗| [m²/s].</summary>
        public double SpecificAngularMomentum { get; }
        public double Eccentricity { get; }
        /// <summary>a = −μ/(2ε) [m]; negative for hyperbolic, ∞ for parabolic.</summary>
        public double SemiMajorAxis { get; }
        /// <summary>Closest distance to the centre [m] (not altitude!).</summary>
        public double PeriapsisRadius { get; }
        /// <summary>Farthest distance to the centre [m]; ∞ when unbound.</summary>
        public double ApoapsisRadius { get; }
        /// <summary>T = 2π√(a³/μ) [s]; ∞ when unbound.</summary>
        public double Period { get; }
        /// <summary>Angle between orbital plane and the reference (equatorial) plane [rad].</summary>
        public double Inclination { get; }
        /// <summary>Angle from periapsis to the current position [rad], 0..2π.</summary>
        public double TrueAnomaly { get; }

        public bool IsBound => SpecificEnergy < 0.0;
    }

    /// <summary>Closed-form two-body relations used by telemetry, the Flight Manual and tests.</summary>
    public static class OrbitalMechanics
    {
        /// <summary>v_circ = √(μ/r) [m/s].</summary>
        public static double CircularVelocity(double mu, double r) => Math.Sqrt(mu / r);

        /// <summary>v_esc = √(2μ/r) [m/s] (no atmosphere, no rotation).</summary>
        public static double EscapeVelocity(double mu, double r) => Math.Sqrt(2.0 * mu / r);

        /// <summary>ε = v²/2 − μ/r [J/kg].</summary>
        public static double SpecificOrbitalEnergy(double mu, double r, double speed) => 0.5 * speed * speed - mu / r;

        /// <summary>T = 2π√(r³/μ) [s] for a circular orbit of radius r.</summary>
        public static double CircularPeriod(double mu, double r) => MathD.TwoPi * Math.Sqrt(r * r * r / mu);

        /// <summary>Vis-viva: v = √(μ(2/r − 1/a)) [m/s].</summary>
        public static double VisVivaSpeed(double mu, double r, double semiMajorAxis) =>
            Math.Sqrt(mu * (2.0 / r - 1.0 / semiMajorAxis));

        /// <summary>
        /// Osculating elements. <paramref name="referenceAxis"/> (unit) defines the equatorial plane for inclination.
        /// Periapsis is computed as p/(1+e) with p = h²/μ, which stays well-conditioned for all conic types.
        /// </summary>
        public static OrbitalElements ComputeElements(double mu, Vector3d r, Vector3d v, Vector3d referenceAxis)
        {
            double rMag = r.Magnitude;
            double vMag = v.Magnitude;
            double energy = SpecificOrbitalEnergy(mu, rMag, vMag);

            Vector3d hVec = Vector3d.Cross(r, v);
            double h = hVec.Magnitude;

            // e⃗ = (v⃗ × h⃗)/μ − r̂
            Vector3d eVec = Vector3d.Cross(v, hVec) / mu - r / rMag;
            double e = eVec.Magnitude;

            double p = h * h / mu;
            double rp = p / (1.0 + e);
            double ra = e < 1.0 ? p / (1.0 - e) : double.PositiveInfinity;

            double a;
            if (Math.Abs(energy) < 1e-12 * mu / rMag) a = double.PositiveInfinity;
            else a = -mu / (2.0 * energy);

            double period = energy < 0.0 ? MathD.TwoPi * Math.Sqrt(a * a * a / mu) : double.PositiveInfinity;

            double inclination = h > 0.0 ? Vector3d.Angle(hVec, referenceAxis) : 0.0;

            double nu;
            if (e > 1e-10)
            {
                nu = Vector3d.Angle(eVec, r);
                if (Vector3d.Dot(r, v) < 0.0) nu = MathD.TwoPi - nu;
            }
            else nu = 0.0;

            return new OrbitalElements(energy, h, e, a, rp, ra, period, inclination, nu);
        }
    }
}
