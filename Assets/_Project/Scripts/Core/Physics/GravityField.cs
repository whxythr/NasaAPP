using System.Collections.Generic;
using SpaceSim.Mathematics;

namespace SpaceSim.Physics
{
    /// <summary>
    /// Superposition of all gravity sources in the simulation. Phase 1 has one planet; adding moons,
    /// a star or other planets means adding sources here — integrators and flight code stay unchanged.
    /// </summary>
    public sealed class GravityField
    {
        readonly List<IGravitySource> sources = new List<IGravitySource>();

        public IReadOnlyList<IGravitySource> Sources => sources;

        public void Add(IGravitySource source)
        {
            if (source != null && !sources.Contains(source)) sources.Add(source);
        }

        public bool Remove(IGravitySource source) => sources.Remove(source);

        public void Clear() => sources.Clear();

        public Vector3d AccelerationAt(Vector3d point)
        {
            Vector3d a = Vector3d.Zero;
            for (int i = 0; i < sources.Count; i++) a += sources[i].AccelerationAt(point);
            return a;
        }

        /// <summary>Source with the strongest local acceleration (a simple dominant-body criterion).</summary>
        public IGravitySource DominantSourceAt(Vector3d point)
        {
            IGravitySource best = null;
            double bestA = -1.0;
            for (int i = 0; i < sources.Count; i++)
            {
                double a = sources[i].AccelerationAt(point).SqrMagnitude;
                if (a > bestA)
                {
                    bestA = a;
                    best = sources[i];
                }
            }
            return best;
        }
    }
}
