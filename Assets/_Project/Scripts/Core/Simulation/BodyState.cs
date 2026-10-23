using SpaceSim.Mathematics;

namespace SpaceSim.Simulation
{
    /// <summary>
    /// Kinematic state of a point body in an inertial frame centred on the primary body.
    /// Position [m], velocity [m/s], time [s].
    /// </summary>
    public struct BodyState
    {
        public Vector3d Position;
        public Vector3d Velocity;
        public double Time;

        public BodyState(Vector3d position, Vector3d velocity, double time)
        {
            Position = position;
            Velocity = velocity;
            Time = time;
        }

        /// <summary>Linear interpolation for rendering between two physics steps. Never fed back into physics.</summary>
        public static BodyState Interpolate(BodyState a, BodyState b, double alpha) => new BodyState(
            Vector3d.Lerp(a.Position, b.Position, alpha),
            Vector3d.Lerp(a.Velocity, b.Velocity, alpha),
            a.Time + (b.Time - a.Time) * alpha);
    }
}
