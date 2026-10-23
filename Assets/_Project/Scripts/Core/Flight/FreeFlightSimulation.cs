using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Simulation;

namespace SpaceSim.Flight
{
    /// <summary>Record of the last contact with the surface.</summary>
    public struct ImpactRecord
    {
        public double Time;
        /// <summary>Speed relative to the ground at contact [m/s].</summary>
        public double SurfaceRelativeSpeed;
        /// <summary>Vertical (radial) component at contact [m/s], negative = downward.</summary>
        public double VerticalSpeed;
        public double Latitude;
        public double Longitude;
    }

    /// <summary>
    /// Unpowered point-mass motion under gravity (+ optional drag) with surface contact. No engine, no thrust,
    /// no automatic orbit insertion: the body only moves according to its initial conditions and the forces.
    /// </summary>
    public sealed class FreeFlightSimulation : IAccelerationModel
    {
        PlanetModel planet;

        public FreeFlightSimulation(PlanetModel planet, GravityField gravity)
        {
            this.planet = planet ?? throw new ArgumentNullException(nameof(planet));
            Gravity = gravity ?? throw new ArgumentNullException(nameof(gravity));
        }

        /// <summary>Primary body for contact, atmosphere and rotation. Can be swapped when planet data changes.</summary>
        public PlanetModel Planet
        {
            get => planet;
            set
            {
                planet = value ?? throw new ArgumentNullException(nameof(value));
                if (IsLanded)
                {
                    // Radius or terrain may have changed: keep the body resting on the new surface.
                    Vector3d dir = LandedFixedPosition.Normalized;
                    LandedFixedPosition = dir * (planet.SurfaceRadius(dir) + ContactRadius);
                    State = LandedState(State.Time);
                }
            }
        }

        public GravityField Gravity { get; set; }

        public BodyState State;
        public BodyState PreviousState;

        public bool IsLanded { get; private set; }
        /// <summary>Contact point in the planet-fixed frame while landed [m].</summary>
        public Vector3d LandedFixedPosition { get; private set; }

        public double Mass = 1000.0;
        public DragProperties Drag = new DragProperties(true, 1.0, 1.0);
        /// <summary>Distance from the body's reference point to the ground when resting [m].</summary>
        public double ContactRadius = 1.0;

        public IntegrationMethod Method = IntegrationMethod.VelocityVerlet;
        /// <summary>η in Δt ≤ η·√(r³/μ). 0.002 ≈ 3100 steps per orbit — far finer than needed in LEO with Δt = 0.02 s.</summary>
        public double AccuracyFactor = 0.002;
        /// <summary>Drag stiffness limit: Δt ≤ k·v/|a_drag|.</summary>
        public double DragStepFactor = 0.05;
        public int MaxSubsteps = 1000;

        public ImpactRecord LastImpact { get; private set; }
        public int ImpactCount { get; private set; }
        /// <summary>Sub-steps used during the last <see cref="Step"/> (diagnostics).</summary>
        public int LastSubstepCount { get; private set; }

        // ---- IAccelerationModel -------------------------------------------------------------

        public Vector3d Acceleration(double time, Vector3d position, Vector3d velocity) =>
            Gravity.AccelerationAt(position) + DragAcceleration(position, velocity);

        public Vector3d DragAcceleration(Vector3d position, Vector3d velocity)
        {
            if (!Drag.enabled || Mass <= 0.0) return Vector3d.Zero;
            double rho = planet.DensityAt(position);
            if (rho <= 0.0) return Vector3d.Zero;
            Vector3d vRel = velocity - planet.AirVelocityAt(position);
            return AtmosphericDrag.DragForce(rho, vRel, Drag.dragCoefficient, Drag.referenceArea) / Mass;
        }

        // ---- Initial conditions (test tools; nothing here chooses a trajectory for the player) ----

        /// <summary>Puts the body at rest on the surface along a planet-fixed direction.</summary>
        public void PlaceOnSurface(Vector3d fixedDirection, double time)
        {
            Vector3d dir = fixedDirection.Normalized;
            LandedFixedPosition = dir * (planet.SurfaceRadius(dir) + ContactRadius);
            IsLanded = true;
            State = LandedState(time);
            PreviousState = State;
        }

        /// <summary>
        /// Sets position by latitude/longitude [rad] and altitude above the reference sphere [m], with a velocity
        /// given in the local East-North-Up frame [m/s], either relative to the rotating ground or inertial.
        /// </summary>
        public void SetLocalState(double latitude, double longitude, double altitudeAboveReference,
            double vEast, double vNorth, double vUp, bool relativeToSurface, double time)
        {
            Vector3d up = PlanetRotation.DirectionFromLatLon(latitude, longitude);
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            Vector3d fixedPos = up * (planet.Radius + altitudeAboveReference);
            Vector3d fixedVel = east * vEast + north * vNorth + up * vUp;

            Vector3d r = planet.ToInertial(fixedPos, time);
            Vector3d v = planet.Rotation.FixedToInertial(fixedVel, time);
            if (relativeToSurface) v += planet.SurfaceVelocityAt(r);
            SetInertialState(r, v, time);
        }

        public void SetInertialState(Vector3d position, Vector3d velocity, double time)
        {
            IsLanded = false;
            State = new BodyState(position, velocity, time);
            PreviousState = State;
            if (planet.AltitudeAboveSurface(position, time) < ContactRadius)
            {
                // Started below ground: lift to the surface, keep the velocity (it may be pointing upward).
                Vector3d f = planet.ToFixed(position, time).Normalized;
                Vector3d lifted = planet.ToInertial(f * (planet.SurfaceRadius(f) + ContactRadius), time);
                State.Position = lifted;
                PreviousState = State;
                double vUp = Vector3d.Dot(velocity - planet.SurfaceVelocityAt(lifted), (lifted - planet.Position).Normalized);
                if (vUp <= 0.0) Land(time, lifted, velocity);
            }
        }

        // ---- Stepping -------------------------------------------------------------------------

        /// <summary>Advances by exactly <paramref name="dt"/> seconds (fixed step from the simulation clock).</summary>
        public void Step(double dt)
        {
            PreviousState = State;
            if (IsLanded)
            {
                State = LandedState(State.Time + dt);
                LastSubstepCount = 0;
                return;
            }

            double maxStep = MaxStableStep(State);
            int n = NumericalIntegrator.SubstepCount(dt, maxStep, MaxSubsteps);
            double h = dt / n;
            LastSubstepCount = n;

            for (int i = 0; i < n; i++)
            {
                BodyState before = State;
                BodyState after = NumericalIntegrator.Step(before, h, this, Method);
                if (planet.AltitudeAboveSurface(after.Position, after.Time) < ContactRadius)
                {
                    ResolveImpact(before, h);
                    // Remaining time is spent resting on the surface.
                    double endTime = PreviousState.Time + dt;
                    State = LandedState(endTime);
                    return;
                }
                State = after;
            }
        }

        double MaxStableStep(BodyState s)
        {
            Vector3d rel = s.Position - planet.Position;
            double limit = NumericalIntegrator.AccuracyLimitedStep(planet.GravitationalParameter, rel.Magnitude, AccuracyFactor);
            Vector3d aDrag = DragAcceleration(s.Position, s.Velocity);
            double ad = aDrag.Magnitude;
            if (ad > 0.0)
            {
                double vRel = (s.Velocity - planet.AirVelocityAt(s.Position)).Magnitude;
                limit = Math.Min(limit, DragStepFactor * vRel / ad);
            }
            return limit;
        }

        /// <summary>Bisection on the sub-step duration to find the contact time, then lands there.</summary>
        void ResolveImpact(BodyState before, double h)
        {
            double lo = 0.0, hi = h;
            BodyState contact = before;
            for (int k = 0; k < 24; k++)
            {
                double mid = 0.5 * (lo + hi);
                BodyState probe = NumericalIntegrator.Step(before, mid, this, Method);
                if (planet.AltitudeAboveSurface(probe.Position, probe.Time) < ContactRadius) hi = mid;
                else
                {
                    lo = mid;
                    contact = probe;
                }
            }
            Land(contact.Time, contact.Position, contact.Velocity);
        }

        void Land(double time, Vector3d position, Vector3d velocity)
        {
            Vector3d surfaceVel = planet.SurfaceVelocityAt(position);
            Vector3d rel = velocity - surfaceVel;
            Vector3d up = (position - planet.Position).Normalized;
            Vector3d fixedDir = planet.ToFixed(position, time).Normalized;
            PlanetRotation.LatLonFromDirection(fixedDir, out double lat, out double lon);

            LastImpact = new ImpactRecord
            {
                Time = time,
                SurfaceRelativeSpeed = rel.Magnitude,
                VerticalSpeed = Vector3d.Dot(rel, up),
                Latitude = lat,
                Longitude = lon
            };
            ImpactCount++;

            LandedFixedPosition = fixedDir * (planet.SurfaceRadius(fixedDir) + ContactRadius);
            IsLanded = true;
        }

        BodyState LandedState(double time)
        {
            Vector3d r = planet.ToInertial(LandedFixedPosition, time);
            return new BodyState(r, planet.SurfaceVelocityAt(r), time);
        }

        /// <summary>Exact position for rendering at an arbitrary time while landed (avoids chord interpolation).</summary>
        public Vector3d LandedPositionAt(double time) => planet.ToInertial(LandedFixedPosition, time);
    }
}
