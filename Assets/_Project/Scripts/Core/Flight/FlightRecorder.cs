using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Flight
{
    /// <summary>
    /// Level B (actual simulation): statistics accumulated by <see cref="FreeFlightSimulation"/> every sub-step, from
    /// the same accelerations the integrator uses. Lets the UI show what really happened next to the ideal (Level A)
    /// numbers:
    ///   d|v|/dt = a_T·v̂ + a_g·v̂ + a_D·v̂   (v relative to the ground; on a non-rotating body this is exact,
    ///   on a rotating one the small Coriolis/centrifugal terms are not booked separately)
    ///   ⇒ Δv expended (∫|a_T|dt) ≈ ground speed gained + gravity loss + drag loss + steering loss.
    /// Gravity loss is counted while the engine burns; drag loss whenever there is air.
    /// </summary>
    public sealed class FlightRecorder
    {
        /// <summary>Time since lift-off / release, excluding time resting on the ground [s].</summary>
        public double FlightTime { get; private set; }
        /// <summary>Path length relative to the rotating ground [m].</summary>
        public double DistanceTravelled { get; private set; }
        public double MaxAltitude { get; private set; }
        public double MaxSpeed { get; private set; }
        public double MaxDynamicPressure { get; private set; }
        /// <summary>Highest proper acceleration (what the crew feels) [m/s²].</summary>
        public double MaxProperAcceleration { get; private set; }
        /// <summary>∫|a_thrust| dt — what the engines actually delivered [m/s].</summary>
        public double DeltaVExpended { get; private set; }
        /// <summary>∫ −(a_g·v̂) dt while burning [m/s].</summary>
        public double GravityLoss { get; private set; }
        /// <summary>∫ −(a_D·v̂) dt [m/s].</summary>
        public double DragLoss { get; private set; }
        /// <summary>∫ |a_T|·(1 − cos∠(T, v)) dt — thrust not pointing along the velocity [m/s].</summary>
        public double SteeringLoss { get; private set; }
        public double PropellantUsed { get; private set; }

        public void Reset()
        {
            FlightTime = DistanceTravelled = MaxAltitude = MaxSpeed = MaxDynamicPressure = MaxProperAcceleration = 0.0;
            DeltaVExpended = GravityLoss = DragLoss = SteeringLoss = PropellantUsed = 0.0;
        }

        /// <summary>Analytic coast ("on rails", engine off): only time, distance and maxima change — no losses without thrust.</summary>
        public void RecordCoast(double dt, double speed, double altitude)
        {
            FlightTime += dt;
            DistanceTravelled += speed * dt;
            MaxAltitude = Math.Max(MaxAltitude, altitude);
            MaxSpeed = Math.Max(MaxSpeed, speed);
        }

        /// <summary>Called by the simulation for each integrated sub-step (state at the start of the sub-step).</summary>
        public void Record(double dt, Vector3d inertialVelocity, Vector3d surfaceVelocity, Vector3d aGravity, Vector3d aDrag,
            Vector3d aThrust, double altitude, double dynamicPressure, double propellantBurned)
        {
            FlightTime += dt;
            DistanceTravelled += surfaceVelocity.Magnitude * dt;
            MaxAltitude = Math.Max(MaxAltitude, altitude);
            double speed = inertialVelocity.Magnitude;
            MaxSpeed = Math.Max(MaxSpeed, speed);
            MaxDynamicPressure = Math.Max(MaxDynamicPressure, dynamicPressure);
            MaxProperAcceleration = Math.Max(MaxProperAcceleration, (aDrag + aThrust).Magnitude);
            PropellantUsed += propellantBurned;

            // Losses are measured along the velocity RELATIVE TO THE GROUND (textbook ascent convention: g·sin γ with γ the
            // flight-path angle over the local horizon). Using the inertial velocity would book a vertical climb as
            // "steering loss" because the pad already moves east with the planet's rotation.
            double groundSpeed = surfaceVelocity.Magnitude;
            Vector3d vHat = groundSpeed > 1e-9 ? surfaceVelocity / groundSpeed : Vector3d.Zero;
            double thrust = aThrust.Magnitude;
            DragLoss += -Vector3d.Dot(aDrag, vHat) * dt;
            if (thrust > 0.0)
            {
                DeltaVExpended += thrust * dt;
                GravityLoss += -Vector3d.Dot(aGravity, vHat) * dt;
                double cos = groundSpeed > 1e-9 ? Vector3d.Dot(aThrust, vHat) / thrust : 1.0;
                SteeringLoss += thrust * (1.0 - cos) * dt;
            }
        }
    }
}
