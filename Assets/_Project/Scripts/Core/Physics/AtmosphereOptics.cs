using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Physics
{
    /// <summary>
    /// Optical (scattering) properties of an atmosphere. Rayleigh scattering is proportional to the physical
    /// density ρ(h)/ρ₀, so the sky automatically follows the planet's density profile. Mie (aerosol) uses its
    /// own, much smaller scale height. Defaults are Earth values (Bruneton 2017), per metre, for R/G/B ≈ 680/550/440 nm.
    /// </summary>
    [Serializable]
    public class AtmosphereOpticalProperties
    {
        /// <summary>Rayleigh scattering coefficient at ρ₀ [1/m] for R, G, B.</summary>
        public Vector3d rayleighScattering = new Vector3d(5.802e-6, 13.558e-6, 33.1e-6);
        /// <summary>Mie scattering coefficient at the surface [1/m].</summary>
        public double mieScattering = 3.996e-6;
        /// <summary>Mie extinction / scattering ratio (≥ 1, aerosols also absorb).</summary>
        public double mieExtinctionRatio = 1.11;
        /// <summary>Aerosol scale height [m].</summary>
        public double mieScaleHeight = 1200.0;
        /// <summary>Henyey–Greenstein anisotropy g (0 = isotropic, →1 = strongly forward).</summary>
        public double mieAnisotropy = 0.8;

        /// <summary>When true the visual shell height is derived from the density profile.</summary>
        public bool autoShellHeight = true;
        /// <summary>Density ratio ρ/ρ₀ at which the visual shell ends (auto mode). Beyond it scattering is invisible.</summary>
        public double shellDensityRatio = 1.0e-6;
        /// <summary>Manual visual shell height above the reference radius [m] (when auto is off).</summary>
        public double shellHeight = 120000.0;

        public AtmosphereOpticalProperties Clone() => (AtmosphereOpticalProperties)MemberwiseClone();
    }

    /// <summary>Light transport helpers shared by CPU lighting and (mirrored) by the atmosphere shader.</summary>
    public static class AtmosphereOptics
    {
        /// <summary>
        /// Height of the visual atmosphere shell [m]: the altitude where ρ/ρ₀ falls to the configured ratio
        /// (searched numerically so it works for tabulated profiles too). This is a rendering bound only —
        /// the physical density continues above it.
        /// </summary>
        public static double VisualShellHeight(IAtmosphereProfile profile, AtmosphereOpticalProperties optics)
        {
            if (profile == null || profile.ReferenceDensity <= 0.0) return 0.0;
            if (!optics.autoShellHeight) return Math.Max(0.0, optics.shellHeight);
            double target = profile.ReferenceDensity * optics.shellDensityRatio;
            double lo = 0.0, hi = 1000.0;
            while (profile.Density(hi) > target && hi < 1.0e8) hi *= 2.0;
            for (int i = 0; i < 60; i++)
            {
                double mid = 0.5 * (lo + hi);
                if (profile.Density(mid) > target) lo = mid; else hi = mid;
            }
            return hi;
        }

        /// <summary>
        /// Spectral transmittance T = exp(−τ) along a ray from <paramref name="origin"/> (relative to the planet
        /// centre) in direction <paramref name="direction"/> until it leaves the shell. Returns zero if the
        /// planet's reference sphere blocks the ray (geometric shadow).
        /// </summary>
        public static Vector3d Transmittance(IAtmosphereProfile profile, AtmosphereOpticalProperties optics,
            double planetRadius, double shellRadius, Vector3d origin, Vector3d direction, int steps = 48)
        {
            Vector3d d = direction.Normalized;
            if (RaySphere(origin, d, planetRadius, out double pNear, out _) && pNear > 0.0) return Vector3d.Zero;
            if (profile == null || profile.ReferenceDensity <= 0.0) return new Vector3d(1.0, 1.0, 1.0);
            if (!RaySphere(origin, d, shellRadius, out double t0, out double t1) || t1 <= 0.0) return new Vector3d(1.0, 1.0, 1.0);
            t0 = Math.Max(0.0, t0);

            double ds = (t1 - t0) / steps;
            double rayleighDepth = 0.0, mieDepth = 0.0;
            double invRho0 = 1.0 / profile.ReferenceDensity;
            for (int i = 0; i < steps; i++)
            {
                Vector3d p = origin + d * (t0 + (i + 0.5) * ds);
                double h = p.Magnitude - planetRadius;
                rayleighDepth += profile.Density(h) * invRho0 * ds;
                mieDepth += Math.Exp(-h / optics.mieScaleHeight) * ds;
            }

            Vector3d br = optics.rayleighScattering;
            double bm = optics.mieScattering * optics.mieExtinctionRatio;
            return new Vector3d(
                Math.Exp(-(br.x * rayleighDepth + bm * mieDepth)),
                Math.Exp(-(br.y * rayleighDepth + bm * mieDepth)),
                Math.Exp(-(br.z * rayleighDepth + bm * mieDepth)));
        }

        /// <summary>Ray–sphere intersection (sphere centred at the origin). Returns false if missed.</summary>
        public static bool RaySphere(Vector3d origin, Vector3d unitDirection, double radius, out double tNear, out double tFar)
        {
            double b = Vector3d.Dot(origin, unitDirection);
            double c = origin.SqrMagnitude - radius * radius;
            double disc = b * b - c;
            if (disc < 0.0)
            {
                tNear = tFar = double.NaN;
                return false;
            }
            double s = Math.Sqrt(disc);
            tNear = -b - s;
            tFar = -b + s;
            return true;
        }
    }
}
