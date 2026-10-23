using System;
using System.Globalization;

namespace SpaceSim.Units
{
    /// <summary>Display unit system. Internal computations are always SI; this only affects text.</summary>
    public enum DisplayUnitSystem
    {
        /// <summary>Metres, m/s.</summary>
        SI,
        /// <summary>Kilometres, km/s (still SI-derived, more readable at orbital scales).</summary>
        Kilometric
    }

    /// <summary>Formats physical values for the HUD. Rounds for readability only; never alters stored values.</summary>
    public static class UnitFormatter
    {
        static readonly NumberFormatInfo Format = CreateFormat();

        static NumberFormatInfo CreateFormat()
        {
            var f = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            f.NumberGroupSeparator = " ";
            f.NumberDecimalSeparator = ".";
            return f;
        }

        public static string Number(double value, int decimals)
        {
            if (double.IsNaN(value)) return "—";
            if (double.IsPositiveInfinity(value)) return "∞";
            if (double.IsNegativeInfinity(value)) return "−∞";
            return value.ToString("N" + decimals, Format);
        }

        public static string Scientific(double value, int significant = 3)
        {
            if (double.IsNaN(value)) return "—";
            if (double.IsInfinity(value)) return value > 0 ? "∞" : "−∞";
            // "3.986E+14" (no zero-padded exponent like the standard "E" format's "E+014").
            string mantissa = significant > 1 ? "0." + new string('0', significant - 1) : "0";
            return value.ToString(mantissa + "E+0", Format);
        }

        public static string Distance(double metres, DisplayUnitSystem units)
        {
            if (units == DisplayUnitSystem.Kilometric) return Number(metres / 1000.0, 3) + " km";
            return Number(metres, 1) + " m";
        }

        public static string Speed(double metresPerSecond, DisplayUnitSystem units)
        {
            if (units == DisplayUnitSystem.Kilometric) return Number(metresPerSecond / 1000.0, 4) + " km/s";
            return Number(metresPerSecond, 2) + " m/s";
        }

        public static string Acceleration(double metresPerSecond2) => Number(metresPerSecond2, 4) + " m/s²";

        public static string Density(double kgPerM3) => Scientific(kgPerM3, 4) + " kg/m³";

        public static string Pressure(double pascals) => Scientific(pascals, 4) + " Pa";

        public static string Temperature(double kelvin) => Number(kelvin, 1) + " K";

        public static string SpecificEnergy(double joulesPerKg, DisplayUnitSystem units)
        {
            if (units == DisplayUnitSystem.Kilometric) return Number(joulesPerKg / 1.0e6, 4) + " MJ/kg";
            return Number(joulesPerKg, 0) + " J/kg";
        }

        public static string Duration(double seconds)
        {
            if (double.IsNaN(seconds)) return "—";
            if (double.IsInfinity(seconds)) return "∞";
            double s = Math.Abs(seconds);
            string sign = seconds < 0 ? "−" : "";
            if (s < 60.0) return sign + Number(s, 1) + " s";
            long total = (long)Math.Floor(s);
            long d = total / 86400;
            long h = (total % 86400) / 3600;
            long m = (total % 3600) / 60;
            long sec = total % 60;
            if (d > 0) return string.Format(CultureInfo.InvariantCulture, "{0}{1}d {2:00}:{3:00}:{4:00}", sign, d, h, m, sec);
            return string.Format(CultureInfo.InvariantCulture, "{0}{1:00}:{2:00}:{3:00}", sign, h, m, sec);
        }

        public static string Angle(double radians) => Number(radians * 180.0 / Math.PI, 3) + "°";
    }
}
