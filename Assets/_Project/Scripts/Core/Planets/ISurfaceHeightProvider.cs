using SpaceSim.Mathematics;

namespace SpaceSim.Planets
{
    /// <summary>
    /// Physical surface used for contact/altitude-above-ground: height [m] above the reference radius
    /// along a unit direction in the planet-fixed frame. Must be deterministic and thread-safe.
    /// </summary>
    public interface ISurfaceHeightProvider
    {
        double SurfaceHeight(Vector3d unitFixedDirection);
        /// <summary>Conservative bounds of <see cref="SurfaceHeight"/> over the whole planet [m].</summary>
        double MinSurfaceHeight { get; }
        double MaxSurfaceHeight { get; }
    }

    /// <summary>Smooth sphere at the reference radius (e.g. for imported visual models without height data).</summary>
    public sealed class ReferenceSphereSurface : ISurfaceHeightProvider
    {
        public static readonly ReferenceSphereSurface Instance = new ReferenceSphereSurface();
        public double SurfaceHeight(Vector3d unitFixedDirection) => 0.0;
        public double MinSurfaceHeight => 0.0;
        public double MaxSurfaceHeight => 0.0;
    }
}
