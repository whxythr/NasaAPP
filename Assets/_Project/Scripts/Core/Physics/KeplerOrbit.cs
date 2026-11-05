using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Physics
{
    /// <summary>Keplerian orbit of a body around its parent (star or planet). Angles in degrees, SI otherwise.</summary>
    [Serializable]
    public class OrbitProperties
    {
        /// <summary>Semi-major axis a [m].</summary>
        public double semiMajorAxis = 1.495978707e11;
        public double eccentricity;
        public double inclinationDegrees;
        public double longitudeOfAscendingNodeDegrees;
        public double argumentOfPeriapsisDegrees;
        /// <summary>Mean anomaly at t = 0 [deg] — where the body starts on its orbit.</summary>
        public double meanAnomalyAtEpochDegrees;
        /// <summary>Orbital period [s]. 0 = derive from the parent's μ: T = 2π√(a³/μ).</summary>
        public double period;
        /// <summary>Informational: rotation was set equal to the orbital period with one face towards the parent.</summary>
        public bool tidallyLocked;

        public OrbitProperties Clone() => (OrbitProperties)MemberwiseClone();
    }

    /// <summary>
    /// Body "on rails" along a fixed Kepler ellipse (two-body solution, no perturbations).
    /// Position(t) is relative to the parent's centre in the parent-centred inertial frame (same axes everywhere:
    /// Z = ecliptic north). Used for planets around the star and moons around planets.
    /// </summary>
    public sealed class KeplerOrbit
    {
        readonly double a, e, meanMotion, m0;
        readonly QuaternionD orientation;

        public KeplerOrbit(OrbitProperties p, double parentGravitationalParameter)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            if (!(p.semiMajorAxis > 0.0)) throw new ArgumentOutOfRangeException(nameof(p), "Semi-major axis must be positive.");
            a = p.semiMajorAxis;
            e = MathD.Clamp(p.eccentricity, 0.0, 0.99);
            Period = p.period > 0.0 ? p.period : OrbitalMechanics.CircularPeriod(parentGravitationalParameter, a);
            meanMotion = MathD.TwoPi / Period;
            m0 = p.meanAnomalyAtEpochDegrees * MathD.DegToRad;
            orientation = QuaternionD.AngleAxis(p.longitudeOfAscendingNodeDegrees * MathD.DegToRad, Vector3d.UnitZ)
                          * QuaternionD.AngleAxis(p.inclinationDegrees * MathD.DegToRad, Vector3d.UnitX)
                          * QuaternionD.AngleAxis(p.argumentOfPeriapsisDegrees * MathD.DegToRad, Vector3d.UnitZ);
        }

        /// <summary>Orbital period [s].</summary>
        public double Period { get; }
        public double SemiMajorAxis => a;
        public double Eccentricity => e;

        /// <summary>Gravitational parameter of the parent implied by a and T: μ = 4π²a³/T².</summary>
        public double ImpliedParentGravitationalParameter => 4.0 * Math.PI * Math.PI * a * a * a / (Period * Period);

        /// <summary>Position relative to the parent [m].</summary>
        public Vector3d Position(double time)
        {
            double E = EccentricAnomaly(time);
            var inPlane = new Vector3d(a * (Math.Cos(E) - e), a * Math.Sqrt(1.0 - e * e) * Math.Sin(E), 0.0);
            return orientation.Rotate(inPlane);
        }

        /// <summary>Velocity relative to the parent [m/s] (time derivative of <see cref="Position"/>).</summary>
        public Vector3d Velocity(double time)
        {
            double E = EccentricAnomaly(time);
            double dE = meanMotion / (1.0 - e * Math.Cos(E));
            var inPlane = new Vector3d(-a * Math.Sin(E) * dE, a * Math.Sqrt(1.0 - e * e) * Math.Cos(E) * dE, 0.0);
            return orientation.Rotate(inPlane);
        }

        /// <summary>
        /// Acceleration of the body along these rails [m/s²]: −μ′·r/|r|³ with μ′ = 4π²a³/T² (exact for the rails motion,
        /// even if the given period does not match the parent's μ precisely).
        /// </summary>
        public Vector3d Acceleration(double time)
        {
            Vector3d r = Position(time);
            double d = r.Magnitude;
            return r * (-ImpliedParentGravitationalParameter / (d * d * d));
        }

        double EccentricAnomaly(double time)
        {
            double M = MathD.WrapPi(m0 + meanMotion * time);
            double E = M;
            for (int i = 0; i < 12; i++) // Newton on Kepler's equation E − e·sin E = M
            {
                double f = E - e * Math.Sin(E) - M;
                E -= f / (1.0 - e * Math.Cos(E));
                if (Math.Abs(f) < 1e-14) break;
            }
            return E;
        }
    }

    /// <summary>The central star of a system.</summary>
    [Serializable]
    public class StarProperties
    {
        public string name = "Star";
        /// <summary>Mass [kg].</summary>
        public double mass = 1.98847e30;
        /// <summary>Photospheric radius [m] (sets the disk size in the sky).</summary>
        public double radius = 6.957e8;
        /// <summary>Effective temperature [K] (informational).</summary>
        public double surfaceTemperature = 5772.0;
        /// <summary>Visible colour of the light.</summary>
        public ColorRGB color = new ColorRGB(1f, 0.97f, 0.93f);

        public double GravitationalParameter => Units.PhysicalConstants.GravitationalConstant * mass;

        public StarProperties Clone() => (StarProperties)MemberwiseClone();
    }
}
