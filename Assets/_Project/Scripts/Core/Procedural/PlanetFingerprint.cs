using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Procedural
{
    /// <summary>
    /// Compact hash of a generated surface: samples a fixed set of directions and hashes quantised heights and
    /// colours (FNV-1a 64). Stored in saves; on load the surface is regenerated and the fingerprints compared,
    /// so a changed generator can never silently replace a saved planet with a different one.
    /// </summary>
    public static class PlanetFingerprint
    {
        const ulong FnvOffset = 14695981039346656037UL;
        const ulong FnvPrime = 1099511628211UL;

        public static string Compute(PlanetSurfaceSampler sampler)
        {
            ulong h = FnvOffset;
            h = Add(h, sampler.AlgorithmVersion);
            h = Add(h, (long)Math.Round(sampler.PlanetRadius));
            const int grid = 5;
            for (int face = 0; face < CubeSphere.FaceCount; face++)
            {
                for (int j = 0; j < grid; j++)
                {
                    for (int i = 0; i < grid; i++)
                    {
                        double u = -0.8 + 0.4 * i;
                        double v = -0.8 + 0.4 * j;
                        Vector3d dir = CubeSphere.Direction(face, u, v);
                        SurfaceSample s = sampler.Sample(dir);
                        h = Add(h, (long)Math.Round(s.TerrainHeight * 100.0)); // centimetres
                        h = Add(h, (long)Math.Round(s.Albedo.r * 255.0));
                        h = Add(h, (long)Math.Round(s.Albedo.g * 255.0));
                        h = Add(h, (long)Math.Round(s.Albedo.b * 255.0));
                        h = Add(h, (long)s.Biome);
                    }
                }
            }
            return h.ToString("x16");
        }

        static ulong Add(ulong hash, long value)
        {
            unchecked
            {
                ulong v = (ulong)value;
                for (int b = 0; b < 8; b++)
                {
                    hash ^= (v >> (b * 8)) & 0xFF;
                    hash *= FnvPrime;
                }
                return hash;
            }
        }
    }
}
