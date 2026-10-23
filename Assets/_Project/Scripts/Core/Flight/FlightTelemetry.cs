using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Flight
{
    /// <summary>
    /// Instantaneous physical quantities of the flying body, all SI. Computed from the latest physics state;
    /// the HUD only formats them.
    /// </summary>
    public sealed class FlightTelemetry
    {
        public double Time;
        public bool IsLanded;

        // Geometry
        public double DistanceFromCenter;      // r [m]
        public double AltitudeAboveReference;  // r − R [m]
        public double TerrainHeight;           // physical surface height above R below the body [m]
        public double AltitudeAboveSurface;    // [m]
        public double Latitude, Longitude;     // [rad], planet-fixed

        // Velocities
        public Vector3d InertialVelocity;
        public double InertialSpeed;
        public Vector3d SurfaceRelativeVelocity; // v − ω×r
        public double SurfaceSpeed;
        public Vector3d AirRelativeVelocity;     // v − v_air
        public double AirSpeed;
        public double RadialVelocity;            // v·r̂ (inertial), + = climbing
        public double HorizontalInertialSpeed;
        public double VelocityEast, VelocityNorth, VelocityUp; // surface-relative, local ENU [m/s]

        // Accelerations
        public Vector3d GravityAcceleration;
        public double GravityMagnitude;          // g(r) = μ/r²
        public double DragAcceleration;          // |a_drag|
        public double TotalAcceleration;         // |a_total| (proper + gravitational)
        public double ProperAcceleration;        // |a_total − a_gravity| = what an accelerometer reads

        // Atmosphere
        public double Density, Pressure, Temperature, DensityRatio, DynamicPressure;
        public bool AboveSpaceBoundary;

        // Orbit (two-body, osculating)
        public double SpecificEnergy;
        public OrbitalElements Orbit;
        public double CircularVelocityHere;
        public double EscapeVelocityHere;

        /// <summary>
        /// Rough time until ground contact assuming constant g and no drag/curvature [s];
        /// NaN when not descending or when the orbit's periapsis clears the terrain.
        /// </summary>
        public double TimeToImpactEstimate;
    }

    public static class FlightTelemetryCalculator
    {
        public static void Compute(FreeFlightSimulation sim, FlightTelemetry t)
        {
            PlanetModel planet = sim.Planet;
            var s = sim.State;
            Vector3d rel = s.Position - planet.Position;
            double r = rel.Magnitude;
            Vector3d up = rel / r;
            Vector3d fixedPos = planet.ToFixed(s.Position, s.Time);
            Vector3d fixedDir = fixedPos / fixedPos.Magnitude;

            t.Time = s.Time;
            t.IsLanded = sim.IsLanded;
            t.DistanceFromCenter = r;
            t.AltitudeAboveReference = r - planet.Radius;
            t.TerrainHeight = planet.Surface.SurfaceHeight(fixedDir);
            t.AltitudeAboveSurface = r - planet.Radius - t.TerrainHeight - sim.ContactRadius;
            if (sim.IsLanded) t.AltitudeAboveSurface = 0.0;
            PlanetRotation.LatLonFromDirection(fixedDir, out t.Latitude, out t.Longitude);

            t.InertialVelocity = s.Velocity;
            t.InertialSpeed = s.Velocity.Magnitude;
            t.SurfaceRelativeVelocity = s.Velocity - planet.SurfaceVelocityAt(s.Position);
            t.SurfaceSpeed = t.SurfaceRelativeVelocity.Magnitude;
            t.AirRelativeVelocity = s.Velocity - planet.AirVelocityAt(s.Position);
            t.AirSpeed = t.AirRelativeVelocity.Magnitude;
            t.RadialVelocity = Vector3d.Dot(s.Velocity, up);
            t.HorizontalInertialSpeed = Vector3d.ProjectOnPlane(s.Velocity, up).Magnitude;

            Vector3d fixedVelRel = planet.Rotation.InertialToFixed(t.SurfaceRelativeVelocity, s.Time);
            PlanetRotation.LocalBasis(fixedDir, out Vector3d east, out Vector3d north);
            t.VelocityEast = Vector3d.Dot(fixedVelRel, east);
            t.VelocityNorth = Vector3d.Dot(fixedVelRel, north);
            t.VelocityUp = Vector3d.Dot(fixedVelRel, fixedDir);

            t.GravityAcceleration = sim.Gravity.AccelerationAt(s.Position);
            t.GravityMagnitude = t.GravityAcceleration.Magnitude;
            if (sim.IsLanded)
            {
                // Resting on the ground: the ground supplies the force that keeps the body co-rotating.
                Vector3d centripetal = -Vector3d.Cross(planet.Rotation.AngularVelocity,
                    Vector3d.Cross(planet.Rotation.AngularVelocity, rel));
                t.DragAcceleration = 0.0;
                t.TotalAcceleration = centripetal.Magnitude;
                t.ProperAcceleration = (centripetal - t.GravityAcceleration).Magnitude;
            }
            else
            {
                Vector3d aDrag = sim.DragAcceleration(s.Position, s.Velocity);
                t.DragAcceleration = aDrag.Magnitude;
                t.TotalAcceleration = (t.GravityAcceleration + aDrag).Magnitude;
                t.ProperAcceleration = t.DragAcceleration;
            }

            double h = t.AltitudeAboveReference;
            t.Density = planet.Atmosphere.Density(h);
            t.Pressure = planet.Atmosphere.Pressure(h);
            t.Temperature = planet.Atmosphere.Temperature(h);
            t.DensityRatio = planet.Atmosphere.ReferenceDensity > 0.0 ? t.Density / planet.Atmosphere.ReferenceDensity : 0.0;
            t.DynamicPressure = AtmosphericDrag.DynamicPressure(t.Density, t.AirSpeed);
            t.AboveSpaceBoundary = h >= planet.SpaceBoundaryAltitude;

            double mu = planet.GravitationalParameter;
            t.Orbit = OrbitalMechanics.ComputeElements(mu, rel, s.Velocity, planet.Rotation.SpinAxis);
            t.SpecificEnergy = t.Orbit.SpecificEnergy;
            t.CircularVelocityHere = OrbitalMechanics.CircularVelocity(mu, r);
            t.EscapeVelocityHere = OrbitalMechanics.EscapeVelocity(mu, r);

            t.TimeToImpactEstimate = EstimateTimeToImpact(t, planet);
        }

        static double EstimateTimeToImpact(FlightTelemetry t, PlanetModel planet)
        {
            if (t.IsLanded || t.RadialVelocity >= 0.0) return double.NaN;
            // Periapsis above the highest terrain: the conic never touches the ground (bound or not).
            if (t.Orbit.PeriapsisRadius > planet.Radius + planet.Surface.MaxSurfaceHeight) return double.NaN;
            // h + v_r·t − ½·g·t² = 0  →  t = (v_r + √(v_r² + 2gh)) / g   (v_r < 0)
            double g = t.GravityMagnitude;
            double hs = Math.Max(0.0, t.AltitudeAboveSurface);
            double vr = t.RadialVelocity;
            return (vr + Math.Sqrt(vr * vr + 2.0 * g * hs)) / g;
        }
    }
}
