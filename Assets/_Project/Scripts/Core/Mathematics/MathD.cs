using System;

namespace SpaceSim.Mathematics
{
    /// <summary>Scalar helpers in double precision (deterministic: only +, -, *, /, min, max, sqrt).</summary>
    public static class MathD
    {
        public const double TwoPi = 2.0 * Math.PI;
        public const double DegToRad = Math.PI / 180.0;
        public const double RadToDeg = 180.0 / Math.PI;

        public static double Clamp(double v, double min, double max) => v < min ? min : (v > max ? max : v);
        public static double Clamp01(double v) => Clamp(v, 0.0, 1.0);
        public static double Lerp(double a, double b, double t) => a + (b - a) * t;

        public static double InverseLerp(double a, double b, double v) =>
            a == b ? 0.0 : Clamp01((v - a) / (b - a));

        /// <summary>Hermite smoothstep. Works for edge0 &gt; edge1 too (reversed ramp).</summary>
        public static double SmoothStep(double edge0, double edge1, double v)
        {
            double t = Clamp01((v - edge0) / (edge1 - edge0));
            return t * t * (3.0 - 2.0 * t);
        }

        /// <summary>Polynomial smooth minimum (k &gt; 0 controls blend width).</summary>
        public static double SmoothMin(double a, double b, double k)
        {
            double h = Clamp01(0.5 + 0.5 * (b - a) / k);
            return Lerp(b, a, h) - k * h * (1.0 - h);
        }

        public static double SmoothMax(double a, double b, double k) => -SmoothMin(-a, -b, k);

        /// <summary>Wraps an angle to (-π, π].</summary>
        public static double WrapPi(double angle)
        {
            double a = angle % TwoPi;
            if (a <= -Math.PI) a += TwoPi;
            else if (a > Math.PI) a -= TwoPi;
            return a;
        }
    }
}
