using System;
using SpaceSim.Mathematics;
using SpaceSim.Planets;

namespace SpaceSim.Physics
{
    public enum LandingResult
    {
        /// <summary>Within the landing gear's limits.</summary>
        Soft,
        /// <summary>Survived, but the structure and equipment are damaged.</summary>
        Hard,
        /// <summary>Destroyed.</summary>
        Crash
    }

    public readonly struct LandingOutcome
    {
        public LandingOutcome(LandingResult result, string reason, double verticalSpeed, double horizontalSpeed, double tilt, double slope)
        {
            Result = result;
            Reason = reason;
            VerticalSpeed = verticalSpeed;
            HorizontalSpeed = horizontalSpeed;
            TiltDegrees = tilt;
            SlopeDegrees = slope;
        }

        public LandingResult Result { get; }
        /// <summary>What decided the result, with the numbers.</summary>
        public string Reason { get; }
        /// <summary>Downward speed at contact [m/s] (positive = down).</summary>
        public double VerticalSpeed { get; }
        public double HorizontalSpeed { get; }
        /// <summary>Vehicle axis from the local vertical [deg].</summary>
        public double TiltDegrees { get; }
        public double SlopeDegrees { get; }
    }

    /// <summary>
    /// Landing (central physics, Level A for the planning numbers, rules for touchdown). Gravity is the local g, thrust the
    /// rocket's real thrust; the descent itself is always the integrated simulation.
    /// </summary>
    public static class LandingPhysics
    {
        /// <summary>Landing-gear limits: vertical / horizontal speed [m/s], tilt and slope [deg].</summary>
        public const double SoftVertical = 6.0, SoftHorizontal = 3.0, SoftTilt = 15.0, SoftSlope = 15.0;
        /// <summary>Beyond these the vehicle is destroyed.</summary>
        public const double CrashVertical = 15.0, CrashHorizontal = 8.0, CrashTilt = 35.0, CrashSlope = 30.0;
        /// <summary>Equipment integrity lost in a hard landing.</summary>
        public const double HardLandingDamage = 0.4;

        /// <summary>Distance [m] to stop a descent at speed v with thrust acceleration a against gravity g: v²/(2(a − g)); ∞ if a ≤ g.</summary>
        public static double StoppingDistance(double speed, double thrustAcceleration, double gravity)
        {
            double net = thrustAcceleration - gravity;
            return net > 0.0 ? speed * speed / (2.0 * net) : double.PositiveInfinity;
        }

        /// <summary>Time [s] to fall height h starting with downward speed v under g: h = v·t + ½·g·t².</summary>
        public static double TimeToImpact(double height, double downwardSpeed, double gravity)
        {
            if (height <= 0.0) return 0.0;
            if (gravity <= 0.0) return downwardSpeed > 0.0 ? height / downwardSpeed : double.PositiveInfinity;
            return (-downwardSpeed + Math.Sqrt(downwardSpeed * downwardSpeed + 2.0 * gravity * height)) / gravity;
        }

        /// <summary>Speed at the ground after free fall from h with downward speed v: √(v² + 2gh) (no drag).</summary>
        public static double ImpactSpeed(double height, double downwardSpeed, double gravity) =>
            Math.Sqrt(Math.Max(0.0, downwardSpeed * downwardSpeed + 2.0 * gravity * Math.Max(0.0, height)));

        /// <summary>Slope of the terrain [deg] around a planet-fixed direction, from heights at ±<paramref name="step"/> metres.</summary>
        public static double TerrainSlope(ISurfaceHeightProvider surface, double radius, Vector3d fixedDirection, double step = 5.0)
        {
            Vector3d up = fixedDirection.Normalized;
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            double angle = step / radius;
            double hE = surface.SurfaceHeight((up + east * angle).Normalized), hW = surface.SurfaceHeight((up - east * angle).Normalized);
            double hN = surface.SurfaceHeight((up + north * angle).Normalized), hS = surface.SurfaceHeight((up - north * angle).Normalized);
            double gx = (hE - hW) / (2.0 * step), gy = (hN - hS) / (2.0 * step);
            return Math.Atan(Math.Sqrt(gx * gx + gy * gy)) * MathD.RadToDeg;
        }

        /// <summary>Touchdown verdict from the contact velocity relative to the ground, the vehicle's tilt and the slope.</summary>
        public static LandingOutcome Assess(double verticalSpeed, double horizontalSpeed, double tiltDegrees, double slopeDegrees)
        {
            double down = Math.Max(0.0, -verticalSpeed);
            string numbers = "вертикально " + Fmt(down) + " м/с, вбок " + Fmt(horizontalSpeed) + " м/с, наклон " + Fmt(tiltDegrees) + "°, склон " + Fmt(slopeDegrees) + "°";
            if (down > CrashVertical || horizontalSpeed > CrashHorizontal || tiltDegrees > CrashTilt || slopeDegrees > CrashSlope)
                return new LandingOutcome(LandingResult.Crash, "Авария: " + numbers + ". Предел опор — " + CrashVertical + " м/с вниз, " + CrashHorizontal +
                                                              " м/с вбок, наклон " + CrashTilt + "°, склон " + CrashSlope + "°.", down, horizontalSpeed, tiltDegrees, slopeDegrees);
            if (down > SoftVertical || horizontalSpeed > SoftHorizontal || tiltDegrees > SoftTilt || slopeDegrees > SoftSlope)
                return new LandingOutcome(LandingResult.Hard, "Жёсткая посадка: " + numbers + ". Мягкая — не более " + SoftVertical + " м/с вниз, " + SoftHorizontal +
                                                             " м/с вбок, наклон и склон до " + SoftTilt + "°.", down, horizontalSpeed, tiltDegrees, slopeDegrees);
            return new LandingOutcome(LandingResult.Soft, "Мягкая посадка: " + numbers + ".", down, horizontalSpeed, tiltDegrees, slopeDegrees);
        }

        static string Fmt(double v) => Academy.AcademyNumber.Format(v);
    }
}
