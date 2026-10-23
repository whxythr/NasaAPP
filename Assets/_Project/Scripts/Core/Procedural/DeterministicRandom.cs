using SpaceSim.Mathematics;

namespace SpaceSim.Procedural
{
    /// <summary>
    /// SplitMix64 pseudo-random generator. Fully specified integer arithmetic, so the same seed yields the same
    /// sequence on every platform and runtime (unlike System.Random / UnityEngine.Random, whose algorithms are
    /// not guaranteed stable across versions). Never seeded from the clock.
    /// </summary>
    public sealed class DeterministicRandom
    {
        ulong state;

        public DeterministicRandom(ulong seed)
        {
            state = seed;
        }

        /// <summary>Combines a seed with a salt into an independent stream seed.</summary>
        public static ulong Mix(ulong seed, ulong salt)
        {
            unchecked
            {
                ulong z = seed ^ (salt * 0xD1B54A32D192ED03UL) ^ (salt >> 17);
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public ulong NextULong()
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform double in [0, 1) with 53 random bits.</summary>
        public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

        public double Range(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>Uniform integer in [0, maxExclusive).</summary>
        public int NextInt(int maxExclusive) => maxExclusive <= 0 ? 0 : (int)(NextULong() % (ulong)maxExclusive);

        /// <summary>Uniformly distributed unit vector (rejection sampling; uses only sqrt).</summary>
        public Vector3d UnitVector()
        {
            while (true)
            {
                var v = new Vector3d(Range(-1.0, 1.0), Range(-1.0, 1.0), Range(-1.0, 1.0));
                double m2 = v.SqrMagnitude;
                if (m2 > 1e-6 && m2 <= 1.0) return v / System.Math.Sqrt(m2);
            }
        }
    }
}
