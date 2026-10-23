using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Procedural
{
    /// <summary>
    /// Seeded 3D gradient noise (Ken Perlin's "improved noise", 2002) in double precision. The permutation
    /// table is shuffled with <see cref="DeterministicRandom"/>, and evaluation uses only floor, +, −, ×,
    /// so results are reproducible. Sampling in 3D on the unit sphere avoids polar seams.
    /// Immutable after construction → safe to use from worker threads.
    /// </summary>
    public sealed class GradientNoise3D
    {
        const int MaxOctaves = 24;
        readonly int[] perm = new int[512];
        readonly Vector3d[] octaveOffsets = new Vector3d[MaxOctaves];

        public GradientNoise3D(ulong seed)
        {
            var rng = new DeterministicRandom(seed);
            var p = new int[256];
            for (int i = 0; i < 256; i++) p[i] = i;
            for (int i = 255; i > 0; i--)
            {
                int j = rng.NextInt(i + 1);
                int tmp = p[i];
                p[i] = p[j];
                p[j] = tmp;
            }
            for (int i = 0; i < 512; i++) perm[i] = p[i & 255];
            // Per-octave offsets decorrelate octaves (otherwise all octaves vanish together at lattice points).
            for (int i = 0; i < MaxOctaves; i++)
                octaveOffsets[i] = new Vector3d(rng.Range(-512.0, 512.0), rng.Range(-512.0, 512.0), rng.Range(-512.0, 512.0));
        }

        /// <summary>Single-octave noise, approximately in [−1, 1].</summary>
        public double Sample(double x, double y, double z)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
            int X = (int)(long)fx & 255;
            int Y = (int)(long)fy & 255;
            int Z = (int)(long)fz & 255;
            x -= fx;
            y -= fy;
            z -= fz;
            double u = Fade(x), v = Fade(y), w = Fade(z);

            int A = perm[X] + Y, AA = perm[A] + Z, AB = perm[A + 1] + Z;
            int B = perm[X + 1] + Y, BA = perm[B] + Z, BB = perm[B + 1] + Z;

            return Lerp(w,
                Lerp(v,
                    Lerp(u, Grad(perm[AA], x, y, z), Grad(perm[BA], x - 1, y, z)),
                    Lerp(u, Grad(perm[AB], x, y - 1, z), Grad(perm[BB], x - 1, y - 1, z))),
                Lerp(v,
                    Lerp(u, Grad(perm[AA + 1], x, y, z - 1), Grad(perm[BA + 1], x - 1, y, z - 1)),
                    Lerp(u, Grad(perm[AB + 1], x, y - 1, z - 1), Grad(perm[BB + 1], x - 1, y - 1, z - 1))));
        }

        public double Sample(Vector3d p) => Sample(p.x, p.y, p.z);

        /// <summary>Fractional Brownian motion, normalised by total amplitude (≈ [−1, 1], typical σ ≈ 0.2).</summary>
        public double Fbm(Vector3d p, int octaves, double lacunarity, double gain)
        {
            octaves = Math.Min(Math.Max(octaves, 1), MaxOctaves);
            double sum = 0.0, amp = 1.0, freq = 1.0, norm = 0.0;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Sample(p * freq + octaveOffsets[i]);
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }

        /// <summary>Ridged multifractal in [0, 1]: sharp crests (mountain ranges) where the noise crosses zero.</summary>
        public double Ridged(Vector3d p, int octaves, double lacunarity, double gain)
        {
            octaves = Math.Min(Math.Max(octaves, 1), MaxOctaves);
            double sum = 0.0, amp = 1.0, freq = 1.0, norm = 0.0, weight = 1.0;
            for (int i = 0; i < octaves; i++)
            {
                double n = 1.0 - Math.Abs(Sample(p * freq + octaveOffsets[i]));
                n *= n;
                n *= weight;
                weight = MathD.Clamp01(n * 2.0);
                sum += amp * n;
                norm += amp;
                amp *= gain;
                freq *= lacunarity;
            }
            return sum / norm;
        }

        static double Fade(double t) => t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
        static double Lerp(double t, double a, double b) => a + t * (b - a);

        static double Grad(int hash, double x, double y, double z)
        {
            int h = hash & 15;
            double u = h < 8 ? x : y;
            double v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
    }
}
