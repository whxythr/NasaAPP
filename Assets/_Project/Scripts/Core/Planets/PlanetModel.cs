using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;

namespace SpaceSim.Planets
{
    /// <summary>
    /// Immutable physical snapshot of one planet used by the simulation: gravity, rotation, atmosphere and
    /// physical surface. Rebuilt whenever the planet data changes, so edits apply to the very next step.
    /// </summary>
    public sealed class PlanetModel
    {
        public PlanetModel(string name, PlanetPhysicalProperties properties, ISurfaceHeightProvider surface, Vector3d position)
        {
            if (properties == null) throw new ArgumentNullException(nameof(properties));
            var issues = properties.Validate();
            if (issues.Count > 0) throw new ArgumentException("Invalid planet data: " + string.Join(" ", issues));

            Name = name ?? "Planet";
            Properties = properties.Clone();
            Radius = Properties.meanRadius;
            GravitationalParameter = Properties.GravitationalParameter;
            Gravity = new PlanetaryGravity(GravitationalParameter, Radius, position);
            Rotation = new PlanetRotation(Properties.siderealRotationPeriod,
                Properties.axialTiltDegrees * MathD.DegToRad,
                Properties.rotationPhaseAtEpochDegrees * MathD.DegToRad);
            Atmosphere = AtmosphericModel.Create(Properties.atmosphere);
            AtmosphereCoRotation = Properties.atmosphere != null ? Properties.atmosphere.coRotationFactor : 1.0;
            Surface = surface ?? ReferenceSphereSurface.Instance;
        }

        public string Name { get; }
        public PlanetPhysicalProperties Properties { get; }
        public double Radius { get; }
        public double GravitationalParameter { get; }
        public PlanetaryGravity Gravity { get; }
        public PlanetRotation Rotation { get; }
        public IAtmosphereProfile Atmosphere { get; }
        public double AtmosphereCoRotation { get; }
        public ISurfaceHeightProvider Surface { get; }
        public double SpaceBoundaryAltitude => Properties.spaceBoundaryAltitude;

        /// <summary>Centre of the planet in the simulation's inertial frame [m]. Static in Phase 1.</summary>
        public Vector3d Position => Gravity.Position;

        public Vector3d ToFixed(Vector3d inertialPosition, double time) =>
            Rotation.InertialToFixed(inertialPosition - Position, time);

        public Vector3d ToInertial(Vector3d fixedPosition, double time) =>
            Position + Rotation.FixedToInertial(fixedPosition, time);

        /// <summary>Distance from the centre to the physical surface along a planet-fixed direction [m].</summary>
        public double SurfaceRadius(Vector3d fixedDirection) => Radius + Surface.SurfaceHeight(fixedDirection.Normalized);

        /// <summary>Height above the physical (terrain/ocean) surface [m].</summary>
        public double AltitudeAboveSurface(Vector3d inertialPosition, double time)
        {
            Vector3d f = ToFixed(inertialPosition, time);
            double r = f.Magnitude;
            return r - SurfaceRadius(f / r);
        }

        /// <summary>Height above the reference sphere [m].</summary>
        public double AltitudeAboveReference(Vector3d inertialPosition) => (inertialPosition - Position).Magnitude - Radius;

        public double DensityAt(Vector3d inertialPosition) => Atmosphere.Density(AltitudeAboveReference(inertialPosition));

        /// <summary>Local air velocity in the inertial frame [m/s].</summary>
        public Vector3d AirVelocityAt(Vector3d inertialPosition) =>
            AtmosphericDrag.AirVelocity(Rotation.AngularVelocity, inertialPosition - Position, AtmosphereCoRotation);

        /// <summary>Velocity of the (rigid) ground under a point, inertial frame [m/s].</summary>
        public Vector3d SurfaceVelocityAt(Vector3d inertialPosition) => Rotation.SurfaceVelocity(inertialPosition - Position);
    }
}
