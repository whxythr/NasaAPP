using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Physics
{
    /// <summary>Result of an ideal Hohmann transfer between coplanar circular orbits.</summary>
    public readonly struct HohmannTransfer
    {
        public HohmannTransfer(double r1, double r2, double semiMajorAxis, double deltaV1, double deltaV2, double transferTime,
            double departureSpeed, double arrivalSpeed)
        {
            R1 = r1;
            R2 = r2;
            SemiMajorAxis = semiMajorAxis;
            DeltaV1 = deltaV1;
            DeltaV2 = deltaV2;
            TransferTime = transferTime;
            DepartureSpeed = departureSpeed;
            ArrivalSpeed = arrivalSpeed;
        }

        public double R1 { get; }
        public double R2 { get; }
        /// <summary>a = (r₁ + r₂)/2 [m].</summary>
        public double SemiMajorAxis { get; }
        /// <summary>|Δv| of the first burn (leaving r₁) [m/s].</summary>
        public double DeltaV1 { get; }
        /// <summary>|Δv| of the second burn (circularising at r₂) [m/s].</summary>
        public double DeltaV2 { get; }
        public double TotalDeltaV => DeltaV1 + DeltaV2;
        /// <summary>Half the transfer-ellipse period [s].</summary>
        public double TransferTime { get; }
        /// <summary>Speed on the transfer ellipse at r₁ [m/s].</summary>
        public double DepartureSpeed { get; }
        /// <summary>Speed on the transfer ellipse at r₂ [m/s].</summary>
        public double ArrivalSpeed { get; }
    }

    /// <summary>
    /// Level A (theory): ideal two-body transfer relations used by flight planning and the Flight Academy.
    /// Assumptions: coplanar circular orbits, impulsive burns, no other bodies. Patched conics for leaving/entering a planet.
    /// </summary>
    public static class TransferCalculator
    {
        /// <summary>Hohmann transfer between circular orbits of radii r₁, r₂ around a body with μ.</summary>
        public static HohmannTransfer Hohmann(double mu, double r1, double r2)
        {
            if (!(mu > 0.0) || !(r1 > 0.0) || !(r2 > 0.0)) throw new ArgumentOutOfRangeException(nameof(mu));
            double a = 0.5 * (r1 + r2);
            double v1 = OrbitalMechanics.CircularVelocity(mu, r1);
            double v2 = OrbitalMechanics.CircularVelocity(mu, r2);
            double vp = OrbitalMechanics.VisVivaSpeed(mu, r1, a);
            double va = OrbitalMechanics.VisVivaSpeed(mu, r2, a);
            double t = Math.PI * Math.Sqrt(a * a * a / mu);
            return new HohmannTransfer(r1, r2, a, Math.Abs(vp - v1), Math.Abs(v2 - va), t, vp, va);
        }

        /// <summary>
        /// Phase angle [rad] the target must lead the departure body by at departure (outward transfer):
        /// θ = π − n₂·t_transfer, wrapped to (−π, π]. Negative ⇒ the target must trail (inward transfers).
        /// </summary>
        public static double HohmannPhaseAngle(double mu, double r1, double r2)
        {
            double t = Hohmann(mu, r1, r2).TransferTime;
            double n2 = Math.Sqrt(mu / (r2 * r2 * r2));
            return MathD.WrapPi(Math.PI - n2 * t);
        }

        /// <summary>Synodic period 1/|1/T₁ − 1/T₂| [s]: how often the same relative geometry (launch window) repeats.</summary>
        public static double SynodicPeriod(double period1, double period2)
        {
            double d = Math.Abs(1.0 / period1 - 1.0 / period2);
            return d > 0.0 ? 1.0 / d : double.PositiveInfinity;
        }

        /// <summary>
        /// Time [s] until the phase angle (target angle − origin angle) reaches <paramref name="requiredPhase"/>,
        /// given mean motions n₁ (origin) and n₂ (target) [rad/s]. Always ≥ 0.
        /// </summary>
        public static double TimeToPhaseAngle(double currentPhase, double requiredPhase, double n1, double n2)
        {
            double rate = n2 - n1; // d(phase)/dt
            if (rate == 0.0) return double.PositiveInfinity;
            double diff = requiredPhase - currentPhase;
            double period = MathD.TwoPi / Math.Abs(rate);
            double t = diff / rate;
            t %= period;
            if (t < 0.0) t += period;
            return t;
        }

        /// <summary>
        /// Patched conics: Δv from a circular parking orbit of radius r_park to leave the planet with hyperbolic excess
        /// speed v∞: √(v∞² + 2μ/r) − √(μ/r).
        /// </summary>
        public static double DepartureDeltaV(double muPlanet, double parkingRadius, double vInfinity) =>
            Math.Sqrt(vInfinity * vInfinity + 2.0 * muPlanet / parkingRadius) - OrbitalMechanics.CircularVelocity(muPlanet, parkingRadius);

        /// <summary>Patched conics: Δv to capture from v∞ into a circular orbit of radius r_park (same expression).</summary>
        public static double CaptureDeltaV(double muPlanet, double orbitRadius, double vInfinity) =>
            DepartureDeltaV(muPlanet, orbitRadius, vInfinity);

        /// <summary>Hyperbolic excess speeds [m/s] at departure and arrival for a heliocentric Hohmann transfer.</summary>
        public static void HohmannExcessSpeeds(double muStar, double rOrigin, double rTarget, out double vInfDeparture, out double vInfArrival)
        {
            HohmannTransfer h = Hohmann(muStar, rOrigin, rTarget);
            vInfDeparture = h.DeltaV1;
            vInfArrival = h.DeltaV2;
        }
    }
}
