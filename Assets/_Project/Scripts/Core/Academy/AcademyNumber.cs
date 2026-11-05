using System;
using System.Globalization;
using SpaceSim.Units;

namespace SpaceSim.Academy
{
    /// <summary>Number entry and display for the Academy (calculators and exercises). Display only — no physics.</summary>
    public static class AcademyNumber
    {
        /// <summary>
        /// Parses what a player types: "12.5", "12,5", "5.03e24", "−3", "1 000". Spaces and the Unicode minus are accepted.
        /// </summary>
        public static bool TryParse(string text, out double value)
        {
            value = 0.0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text.Trim()
                .Replace(" ", "").Replace(" ", "").Replace(" ", "").Replace(" ", "")
                .Replace('−', '-').Replace(',', '.');
            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) return false;
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>Readable value with sensible precision (thousands grouped, scientific for very large/small).</summary>
        public static string Format(double v)
        {
            if (double.IsNaN(v)) return "—";
            if (double.IsPositiveInfinity(v)) return "∞";
            if (double.IsNegativeInfinity(v)) return "−∞";
            double a = Math.Abs(v);
            if (a == 0.0) return "0";
            if (a >= 1e7 || a < 1e-3) return UnitFormatter.Scientific(v, 4);
            if (a >= 1000.0) return UnitFormatter.Number(v, 0);
            if (a >= 100.0) return UnitFormatter.Number(v, 1);
            if (a >= 10.0) return UnitFormatter.Number(v, 2);
            if (a >= 1.0) return UnitFormatter.Number(v, 3);
            return UnitFormatter.Number(v, 4);
        }

        /// <summary>Value as an editable string that <see cref="TryParse"/> reads back (no grouping).</summary>
        public static string FormatEditable(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "";
            double a = Math.Abs(v);
            if (a != 0.0 && (a >= 1e7 || a < 1e-3)) return v.ToString("0.#####E+0", CultureInfo.InvariantCulture);
            return v.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>Rounds to <paramref name="significant"/> significant digits (exercise numbers stay readable).</summary>
        public static double RoundSignificant(double v, int significant)
        {
            if (v == 0.0 || double.IsNaN(v) || double.IsInfinity(v)) return v;
            int e = (int)Math.Floor(Math.Log10(Math.Abs(v))) - (significant - 1);
            if (e >= 0)
            {
                double p = Math.Pow(10.0, e);
                return Math.Round(v / p) * p;
            }
            double q = Math.Pow(10.0, -e);
            return Math.Round(v * q) / q;
        }
    }
}
