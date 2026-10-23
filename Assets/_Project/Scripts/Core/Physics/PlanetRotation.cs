using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Physics
{
    /// <summary>
    /// Rigid rotation of a planet about a fixed (tilted) spin axis.
    ///
    /// Frames (both right-handed, origin at the planet centre):
    /// • inertial: Z = ecliptic north, X = direction of the Sun at the epoch (vernal-equinox-like);
    /// • planet-fixed: Z = spin axis (north pole), X = prime meridian on the equator, Y = 90°E.
    /// fixed→inertial: R(t) = R_x(tilt) · R_z(φ₀ + ω·t).
    /// Precession, nutation and polar motion are not modelled.
    /// </summary>
    public sealed class PlanetRotation
    {
        readonly QuaternionD tilt;

        /// <param name="siderealPeriod">Sidereal rotation period [s]. Negative = retrograde. 0 or ∞ = no rotation.</param>
        /// <param name="axialTilt">Obliquity [rad].</param>
        /// <param name="phaseAtEpoch">Rotation angle of the prime meridian at t = 0 [rad].</param>
        public PlanetRotation(double siderealPeriod, double axialTilt, double phaseAtEpoch)
        {
            SiderealPeriod = siderealPeriod;
            AxialTilt = axialTilt;
            PhaseAtEpoch = phaseAtEpoch;
            AngularSpeed = (siderealPeriod == 0.0 || double.IsInfinity(siderealPeriod) || double.IsNaN(siderealPeriod))
                ? 0.0
                : MathD.TwoPi / siderealPeriod;
            tilt = QuaternionD.AngleAxis(axialTilt, Vector3d.UnitX);
            SpinAxis = tilt.Rotate(Vector3d.UnitZ);
        }

        public double SiderealPeriod { get; }
        public double AxialTilt { get; }
        public double PhaseAtEpoch { get; }

        /// <summary>Signed angular speed ω [rad/s].</summary>
        public double AngularSpeed { get; }

        /// <summary>Unit north-pole axis in the inertial frame.</summary>
        public Vector3d SpinAxis { get; }

        /// <summary>ω⃗ in the inertial frame [rad/s].</summary>
        public Vector3d AngularVelocity => SpinAxis * AngularSpeed;

        public double RotationAngle(double time) => PhaseAtEpoch + AngularSpeed * time;

        /// <summary>Orientation of the planet-fixed frame relative to the inertial frame at time t.</summary>
        public QuaternionD Orientation(double time)
        {
            double angle = RotationAngle(time) % MathD.TwoPi;
            return tilt * QuaternionD.AngleAxis(angle, Vector3d.UnitZ);
        }

        public Vector3d FixedToInertial(Vector3d fixedVector, double time) => Orientation(time).Rotate(fixedVector);
        public Vector3d InertialToFixed(Vector3d inertialVector, double time) => Orientation(time).Conjugate.Rotate(inertialVector);

        /// <summary>Velocity of a point rigidly attached to the planet: v⃗ = ω⃗ × r⃗ [m/s].</summary>
        public Vector3d SurfaceVelocity(Vector3d inertialRelativePosition) => Vector3d.Cross(AngularVelocity, inertialRelativePosition);

        /// <summary>Unit direction in the planet-fixed frame for geodetic-free spherical latitude/longitude [rad].</summary>
        public static Vector3d DirectionFromLatLon(double latitude, double longitude)
        {
            double cl = Math.Cos(latitude);
            return new Vector3d(cl * Math.Cos(longitude), cl * Math.Sin(longitude), Math.Sin(latitude));
        }

        /// <summary>Spherical latitude/longitude [rad] of a planet-fixed vector.</summary>
        public static void LatLonFromDirection(Vector3d fixedVector, out double latitude, out double longitude)
        {
            Vector3d n = fixedVector.Normalized;
            latitude = Math.Asin(MathD.Clamp(n.z, -1.0, 1.0));
            longitude = Math.Atan2(n.y, n.x);
        }

        /// <summary>Local East-North-Up basis (planet-fixed) at a unit surface direction.</summary>
        public static void LocalBasis(Vector3d fixedUp, out Vector3d east, out Vector3d north)
        {
            Vector3d up = fixedUp.Normalized;
            Vector3d e = Vector3d.Cross(Vector3d.UnitZ, up);
            if (e.SqrMagnitude < 1e-20) e = Vector3d.UnitY; // at the poles: pick a stable east
            east = e.Normalized;
            north = Vector3d.Cross(up, east);
        }
    }
}
