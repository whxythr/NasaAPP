using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Physics
{
    /// <summary>
    /// Exact two-body motion (Kepler) for any conic — ellipse, parabola, hyperbola — with universal variables
    /// (Curtis, "Orbital Mechanics for Engineering Students", ch. 3.7; Vallado alg. 8). This is the same physics the
    /// numerical integrator solves when only one body pulls; used for coasting at high time warp ("on rails") and for
    /// trajectory prediction. SI units, states relative to the attracting body.
    /// </summary>
    public static class ConicPropagator
    {
        /// <summary>Stumpff C(z).</summary>
        public static double StumpffC(double z)
        {
            if (z > 1e-6) return (1.0 - Math.Cos(Math.Sqrt(z))) / z;
            if (z < -1e-6) return (Math.Cosh(Math.Sqrt(-z)) - 1.0) / -z;
            return 0.5 - z / 24.0 + z * z / 720.0;
        }

        /// <summary>Stumpff S(z).</summary>
        public static double StumpffS(double z)
        {
            if (z > 1e-6)
            {
                double s = Math.Sqrt(z);
                return (s - Math.Sin(s)) / (s * s * s);
            }
            if (z < -1e-6)
            {
                double s = Math.Sqrt(-z);
                return (Math.Sinh(s) - s) / (s * s * s);
            }
            return 1.0 / 6.0 - z / 120.0 + z * z / 5040.0;
        }

        /// <summary>Moves (r₀, v₀) forward (or backward) by <paramref name="dt"/> seconds around a body with μ.</summary>
        public static void Propagate(double mu, Vector3d r0, Vector3d v0, double dt, out Vector3d r, out Vector3d v)
        {
            if (!(mu > 0.0)) throw new ArgumentOutOfRangeException(nameof(mu));
            double r0m = r0.Magnitude;
            if (dt == 0.0 || !(r0m > 0.0))
            {
                r = r0;
                v = v0;
                return;
            }
            double sqrtMu = Math.Sqrt(mu);
            double rv = Vector3d.Dot(r0, v0);
            double alpha = 2.0 / r0m - v0.SqrMagnitude / mu; // 1/a
            // Ellipses repeat: reduce huge times modulo the period (keeps Newton well-conditioned).
            if (alpha > 1e-20)
            {
                double period = MathD.TwoPi / (sqrtMu * Math.Pow(alpha, 1.5));
                if (Math.Abs(dt) > period) dt %= period;
                if (dt == 0.0)
                {
                    r = r0;
                    v = v0;
                    return;
                }
            }

            double chi = SolveUniversalAnomaly(sqrtMu, r0m, rv, alpha, dt);
            double z = alpha * chi * chi;
            double c = StumpffC(z), s = StumpffS(z);
            double f = 1.0 - chi * chi / r0m * c;
            double g = dt - chi * chi * chi * s / sqrtMu;
            r = r0 * f + v0 * g;
            double rm = r.Magnitude;
            double fDot = sqrtMu / (rm * r0m) * (alpha * chi * chi * chi * s - chi);
            double gDot = 1.0 - chi * chi / rm * c;
            v = r0 * fDot + v0 * gDot;
        }

        /// <summary>Time of flight t(χ) — strictly increasing in χ (dt/dχ = r/√μ &gt; 0).</summary>
        static double TimeOfFlight(double sqrtMu, double r0, double rv, double alpha, double chi)
        {
            double z = alpha * chi * chi;
            if (z < -4.0e5) return chi > 0.0 ? double.PositiveInfinity : double.NegativeInfinity; // cosh overflow guard
            double c = StumpffC(z), s = StumpffS(z);
            return (rv / sqrtMu * chi * chi * c + (1.0 - alpha * r0) * chi * chi * chi * s + r0 * chi) / sqrtMu;
        }

        /// <summary>Safeguarded Newton on Kepler's universal equation: bracket, then Newton steps that never leave it.</summary>
        static double SolveUniversalAnomaly(double sqrtMu, double r0, double rv, double alpha, double dt)
        {
            double guess = alpha > 0.0 ? sqrtMu * alpha * dt : sqrtMu * dt / r0;
            if (guess == 0.0 || double.IsNaN(guess)) guess = dt > 0.0 ? 1.0 : -1.0;
            double lo, hi;
            if (dt > 0.0)
            {
                lo = 0.0;
                hi = Math.Abs(guess);
                int k = 0;
                while (TimeOfFlight(sqrtMu, r0, rv, alpha, hi) < dt && k++ < 200) hi *= 2.0;
            }
            else
            {
                hi = 0.0;
                lo = -Math.Abs(guess);
                int k = 0;
                while (TimeOfFlight(sqrtMu, r0, rv, alpha, lo) > dt && k++ < 200) lo *= 2.0;
            }
            double chi = Math.Max(lo, Math.Min(hi, guess));
            for (int i = 0; i < 200; i++)
            {
                double t = TimeOfFlight(sqrtMu, r0, rv, alpha, chi);
                double err = t - dt;
                if (err > 0.0) hi = chi;
                else lo = chi;
                if (Math.Abs(err) <= 1e-12 * Math.Max(1.0, Math.Abs(dt))) return chi;
                double z = alpha * chi * chi;
                double rNow = rv / sqrtMu * chi * (1.0 - z * StumpffS(z)) + (1.0 - alpha * r0) * chi * chi * StumpffC(z) + r0; // = r(χ)
                double next = chi - err * sqrtMu / rNow;
                if (!(next > lo && next < hi) || double.IsNaN(next)) next = 0.5 * (lo + hi); // bisection fallback
                if (next == chi) return chi;
                chi = next;
            }
            return chi;
        }

        /// <summary>Classical shape of the conic through (r, v): eccentricity e, semi-latus rectum p [m], periapsis radius [m].</summary>
        public static void Shape(double mu, Vector3d r, Vector3d v, out double eccentricity, out double semiLatusRectum, out double periapsis)
        {
            Vector3d h = Vector3d.Cross(r, v);
            semiLatusRectum = h.SqrMagnitude / mu;
            Vector3d eVec = EccentricityVector(mu, r, v);
            eccentricity = eVec.Magnitude;
            periapsis = semiLatusRectum / (1.0 + eccentricity);
        }

        public static Vector3d EccentricityVector(double mu, Vector3d r, Vector3d v)
        {
            double rm = r.Magnitude;
            return (r * (v.SqrMagnitude - mu / rm) - v * Vector3d.Dot(r, v)) / mu;
        }

        /// <summary>
        /// Time [s] until the trajectory first reaches distance <paramref name="radius"/> from the body (moving forward), or
        /// +∞ if it never does (e.g. a circle, or a radius outside the conic's range).
        /// </summary>
        public static double TimeToRadius(double mu, Vector3d r0, Vector3d v0, double radius)
        {
            double r0m = r0.Magnitude;
            if (!(radius > 0.0) || !(r0m > 0.0)) return double.PositiveInfinity;
            Shape(mu, r0, v0, out double e, out double p, out double rp);
            if (e < 1e-10) return double.PositiveInfinity; // circular: distance never changes
            double cosTarget = (p / radius - 1.0) / e;
            if (cosTarget > 1.0 || cosTarget < -1.0) return double.PositiveInfinity; // not on this conic
            double thetaTarget = Math.Acos(cosTarget); // the conic crosses the radius at ±θ
            double theta0 = TrueAnomaly(mu, r0, v0, e);
            double best = double.PositiveInfinity;
            foreach (double th in new[] { thetaTarget, -thetaTarget })
            {
                double t = TimeBetweenAnomalies(mu, e, p, theta0, th);
                if (t >= 0.0 && t < best) best = t;
            }
            return best;
        }

        /// <summary>True anomaly θ of the state (−π, π]: negative before periapsis.</summary>
        public static double TrueAnomaly(double mu, Vector3d r, Vector3d v, double eccentricity)
        {
            Vector3d eVec = EccentricityVector(mu, r, v);
            double rm = r.Magnitude;
            double c = MathD.Clamp(Vector3d.Dot(eVec, r) / (eccentricity * rm), -1.0, 1.0);
            double th = Math.Acos(c);
            return Vector3d.Dot(r, v) < 0.0 ? -th : th;
        }

        /// <summary>Forward time from true anomaly θ₁ to θ₂ on the conic (ellipses wrap; hyperbolas do not).</summary>
        static double TimeBetweenAnomalies(double mu, double e, double p, double theta1, double theta2)
        {
            if (Math.Abs(e - 1.0) < 1e-9) e = 1.0 + 1e-9;
            if (e < 1.0)
            {
                double a = p / (1.0 - e * e);
                double n = Math.Sqrt(mu / (a * a * a));
                double m1 = MeanAnomalyElliptic(e, theta1), m2 = MeanAnomalyElliptic(e, theta2);
                double dm = m2 - m1;
                while (dm < 0.0) dm += MathD.TwoPi;
                return dm / n;
            }
            double ah = p / (e * e - 1.0); // |a|
            double nh = Math.Sqrt(mu / (ah * ah * ah));
            double thetaInf = Math.Acos(-1.0 / e);
            if (Math.Abs(theta2) >= thetaInf) return double.PositiveInfinity;
            double dmh = MeanAnomalyHyperbolic(e, theta2) - MeanAnomalyHyperbolic(e, theta1);
            return dmh >= 0.0 ? dmh / nh : double.PositiveInfinity;
        }

        static double MeanAnomalyElliptic(double e, double theta)
        {
            double E = 2.0 * Math.Atan(Math.Sqrt((1.0 - e) / (1.0 + e)) * Math.Tan(theta / 2.0));
            return E - e * Math.Sin(E);
        }

        static double MeanAnomalyHyperbolic(double e, double theta)
        {
            double x = Math.Sqrt((e - 1.0) / (e + 1.0)) * Math.Tan(theta / 2.0);
            x = MathD.Clamp(x, -0.999999999999, 0.999999999999);
            double H = Math.Log((1.0 + x) / (1.0 - x)); // tanh(H/2) = x  ⇒  H = 2·atanh(x)
            return e * Math.Sinh(H) - H;
        }
    }
}
