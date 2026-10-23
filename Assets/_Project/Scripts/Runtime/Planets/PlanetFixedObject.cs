using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Keeps a GameObject attached to a point on the rotating planet. The pose is recomputed every frame in
    /// double precision (planet-fixed → inertial → origin-relative), so objects far from the Unity origin
    /// never accumulate float error. Local frame: +Y = up (radial), +Z = north, +X = east.
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class PlanetFixedObject : MonoBehaviour
    {
        PlanetRuntime planet;
        FloatingOrigin origin;
        SimulationController simulation;

        public Vector3d FixedPosition { get; private set; }
        public QuaternionD LocalToFixed { get; private set; } = QuaternionD.Identity;

        public void Initialize(PlanetRuntime planetRuntime, FloatingOrigin floatingOrigin, SimulationController sim)
        {
            planet = planetRuntime;
            origin = floatingOrigin;
            simulation = sim;
        }

        /// <summary>Places the object on the physical surface at latitude/longitude [deg] with a height offset [m].</summary>
        public void PlaceOnSurface(double latitudeDeg, double longitudeDeg, double heightOffset)
        {
            Vector3d up = PlanetRotation.DirectionFromLatLon(latitudeDeg * MathD.DegToRad, longitudeDeg * MathD.DegToRad);
            FixedPosition = up * (planet.Model.SurfaceRadius(up) + heightOffset);
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            // Right-handed local frame (x = east, y = north, z = up). Through the Unity axis swap this becomes
            // Unity local X = east, Y = up, Z = north — the usual "Y up, Z forward" convention.
            LocalToFixed = BasisToQuaternion(east, north, up);
            UpdatePose();
        }

        void LateUpdate() => UpdatePose();

        void UpdatePose()
        {
            if (planet == null || planet.Model == null) return;
            double t = simulation.RenderTime;
            Vector3d sim = planet.Model.ToInertial(FixedPosition, t);
            QuaternionD q = planet.Model.Rotation.Orientation(t) * LocalToFixed;
            transform.SetPositionAndRotation(origin.ToUnityPosition(sim), CoordinateSystem.ToUnityRotation(q));
        }

        /// <summary>Rotation whose columns are the given right-handed orthonormal axes (physics frame).</summary>
        public static QuaternionD BasisToQuaternion(Vector3d x, Vector3d y, Vector3d z)
        {
            double m00 = x.x, m01 = y.x, m02 = z.x;
            double m10 = x.y, m11 = y.y, m12 = z.y;
            double m20 = x.z, m21 = y.z, m22 = z.z;
            double trace = m00 + m11 + m22;
            double qw, qx, qy, qz;
            if (trace > 0)
            {
                double s = System.Math.Sqrt(trace + 1.0) * 2.0;
                qw = 0.25 * s; qx = (m21 - m12) / s; qy = (m02 - m20) / s; qz = (m10 - m01) / s;
            }
            else if (m00 > m11 && m00 > m22)
            {
                double s = System.Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
                qw = (m21 - m12) / s; qx = 0.25 * s; qy = (m01 + m10) / s; qz = (m02 + m20) / s;
            }
            else if (m11 > m22)
            {
                double s = System.Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
                qw = (m02 - m20) / s; qx = (m01 + m10) / s; qy = 0.25 * s; qz = (m12 + m21) / s;
            }
            else
            {
                double s = System.Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
                qw = (m10 - m01) / s; qx = (m02 + m20) / s; qy = (m12 + m21) / s; qz = 0.25 * s;
            }
            return new QuaternionD(qx, qy, qz, qw).Normalized;
        }
    }
}
