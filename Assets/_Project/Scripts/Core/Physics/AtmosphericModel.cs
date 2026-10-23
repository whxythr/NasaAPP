using System;
using SpaceSim.Units;

namespace SpaceSim.Physics
{
    public enum AtmosphereProfileType
    {
        /// <summary>Isothermal exponential atmosphere ρ(h) = ρ₀·exp(−h/H).</summary>
        Exponential,
        /// <summary>Piecewise log-linear interpolation of a measured/standard table.</summary>
        Tabulated
    }

    /// <summary>One row of a tabulated atmosphere. Altitude above the reference radius [m].</summary>
    [Serializable]
    public struct AtmosphereTablePoint
    {
        public double altitude;
        public double density;
        public double pressure;
        public double temperature;

        public AtmosphereTablePoint(double altitude, double density, double pressure, double temperature)
        {
            this.altitude = altitude;
            this.density = density;
            this.pressure = pressure;
            this.temperature = temperature;
        }
    }

    /// <summary>
    /// Physical atmosphere data of one planet. This is the physical model only — the visual shell height
    /// and the conventional "edge of space" altitude are separate parameters elsewhere.
    /// </summary>
    [Serializable]
    public class AtmosphereProperties
    {
        public bool hasAtmosphere = true;
        public AtmosphereProfileType profile = AtmosphereProfileType.Exponential;

        /// <summary>ρ₀ at the reference radius [kg/m³].</summary>
        public double surfaceDensity = 1.225;
        /// <summary>Density scale height H [m].</summary>
        public double scaleHeight = 8500.0;
        /// <summary>Pressure at the reference radius [Pa].</summary>
        public double surfacePressure = 101325.0;
        /// <summary>Temperature of the isothermal model [K].</summary>
        public double surfaceTemperature = 288.15;
        /// <summary>Mean molar mass of the gas mixture [kg/mol] (Earth air ≈ 0.0289644).</summary>
        public double meanMolarMass = 0.0289644;
        /// <summary>1 = atmosphere co-rotates with the planet (no wind); 0 = atmosphere at rest in inertial space.</summary>
        public double coRotationFactor = 1.0;
        /// <summary>Used when <see cref="profile"/> is Tabulated. Must be sorted by altitude.</summary>
        public AtmosphereTablePoint[] table = new AtmosphereTablePoint[0];

        public AtmosphereProperties Clone()
        {
            var c = (AtmosphereProperties)MemberwiseClone();
            c.table = table != null ? (AtmosphereTablePoint[])table.Clone() : new AtmosphereTablePoint[0];
            return c;
        }
    }

    /// <summary>Vertical profile of an atmosphere. Altitude h is measured above the planet's reference radius [m].</summary>
    public interface IAtmosphereProfile
    {
        /// <summary>Density ρ(h) [kg/m³]. Decreases smoothly; never becomes exactly zero at a "boundary".</summary>
        double Density(double altitude);
        /// <summary>Pressure p(h) [Pa].</summary>
        double Pressure(double altitude);
        /// <summary>Temperature T(h) [K].</summary>
        double Temperature(double altitude);
        /// <summary>ρ at h = 0 [kg/m³].</summary>
        double ReferenceDensity { get; }
    }

    /// <summary>No atmosphere (airless body).</summary>
    public sealed class VacuumAtmosphere : IAtmosphereProfile
    {
        public static readonly VacuumAtmosphere Instance = new VacuumAtmosphere();
        public double Density(double altitude) => 0.0;
        public double Pressure(double altitude) => 0.0;
        public double Temperature(double altitude) => 0.0;
        public double ReferenceDensity => 0.0;
    }

    /// <summary>
    /// Isothermal exponential atmosphere: ρ(h) = ρ₀·e^(−h/H), p(h) = p₀·e^(−h/H), T = T₀.
    /// Good first approximation in the troposphere/stratosphere; it underestimates thermospheric density
    /// (real H grows with temperature above ~90 km). Replace with a table for higher fidelity.
    /// </summary>
    public sealed class ExponentialAtmosphere : IAtmosphereProfile
    {
        public ExponentialAtmosphere(double surfaceDensity, double scaleHeight, double surfacePressure, double temperature)
        {
            if (surfaceDensity < 0.0) throw new ArgumentOutOfRangeException(nameof(surfaceDensity));
            if (!(scaleHeight > 0.0)) throw new ArgumentOutOfRangeException(nameof(scaleHeight));
            SurfaceDensity = surfaceDensity;
            ScaleHeight = scaleHeight;
            SurfacePressure = surfacePressure;
            SurfaceTemperature = temperature;
        }

        public double SurfaceDensity { get; }
        public double ScaleHeight { get; }
        public double SurfacePressure { get; }
        public double SurfaceTemperature { get; }
        public double ReferenceDensity => SurfaceDensity;

        public double Density(double altitude) => SurfaceDensity * Math.Exp(-altitude / ScaleHeight);
        public double Pressure(double altitude) => SurfacePressure * Math.Exp(-altitude / ScaleHeight);
        public double Temperature(double altitude) => SurfaceTemperature;
    }

    /// <summary>
    /// Tabulated atmosphere with log-linear interpolation of ρ and p (exact for piecewise-exponential layers)
    /// and linear interpolation of T. Outside the table, extrapolates exponentially with the scale height of
    /// the nearest segment, so density approaches zero smoothly rather than being cut off.
    /// </summary>
    public sealed class TabulatedAtmosphere : IAtmosphereProfile
    {
        readonly AtmosphereTablePoint[] points;

        public TabulatedAtmosphere(AtmosphereTablePoint[] table)
        {
            if (table == null || table.Length < 2) throw new ArgumentException("Table needs at least two points.", nameof(table));
            points = (AtmosphereTablePoint[])table.Clone();
            Array.Sort(points, (a, b) => a.altitude.CompareTo(b.altitude));
            for (int i = 0; i < points.Length; i++)
            {
                if (!(points[i].density > 0.0) || !(points[i].pressure > 0.0))
                    throw new ArgumentException("Tabulated density and pressure must be positive.", nameof(table));
            }
            ReferenceDensity = Density(0.0);
        }

        public double ReferenceDensity { get; }

        public double Density(double altitude) => LogInterpolate(altitude, p => p.density);
        public double Pressure(double altitude) => LogInterpolate(altitude, p => p.pressure);

        public double Temperature(double altitude)
        {
            Segment(altitude, out int i0, out int i1);
            var a = points[i0];
            var b = points[i1];
            double t = (altitude - a.altitude) / (b.altitude - a.altitude);
            if (t < 0.0) t = 0.0;
            if (t > 1.0) t = 1.0;
            return a.temperature + (b.temperature - a.temperature) * t;
        }

        double LogInterpolate(double altitude, Func<AtmosphereTablePoint, double> value)
        {
            Segment(altitude, out int i0, out int i1);
            var a = points[i0];
            var b = points[i1];
            double la = Math.Log(value(a));
            double lb = Math.Log(value(b));
            double t = (altitude - a.altitude) / (b.altitude - a.altitude); // unclamped → exponential extrapolation
            return Math.Exp(la + (lb - la) * t);
        }

        void Segment(double altitude, out int i0, out int i1)
        {
            int n = points.Length;
            if (altitude <= points[0].altitude) { i0 = 0; i1 = 1; return; }
            if (altitude >= points[n - 1].altitude) { i0 = n - 2; i1 = n - 1; return; }
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (points[mid].altitude <= altitude) lo = mid; else hi = mid;
            }
            i0 = lo;
            i1 = hi;
        }
    }

    public static class AtmosphericModel
    {
        /// <summary>Builds the runtime profile from planet data.</summary>
        public static IAtmosphereProfile Create(AtmosphereProperties p)
        {
            if (p == null || !p.hasAtmosphere) return VacuumAtmosphere.Instance;
            if (p.profile == AtmosphereProfileType.Tabulated && p.table != null && p.table.Length >= 2)
                return new TabulatedAtmosphere(p.table);
            if (p.surfaceDensity <= 0.0) return VacuumAtmosphere.Instance;
            return new ExponentialAtmosphere(p.surfaceDensity, Math.Max(1.0, p.scaleHeight), p.surfacePressure, p.surfaceTemperature);
        }

        /// <summary>Isothermal scale height H = R·T / (M·g) [m].</summary>
        public static double IsothermalScaleHeight(double temperature, double molarMass, double gravity) =>
            PhysicalConstants.MolarGasConstant * temperature / (molarMass * gravity);

        /// <summary>Ideal gas density ρ = p·M / (R·T) [kg/m³].</summary>
        public static double IdealGasDensity(double pressure, double molarMass, double temperature) =>
            pressure * molarMass / (PhysicalConstants.MolarGasConstant * temperature);
    }
}
