using System;
using SpaceSim.Mathematics;
using SpaceSim.Simulation;

namespace SpaceSim.Physics
{
    /// <summary>Provides total acceleration acting on a body (gravity + other specific forces).</summary>
    public interface IAccelerationModel
    {
        /// <summary>Acceleration [m/s²] at time t for inertial position r [m] and velocity v [m/s].</summary>
        Vector3d Acceleration(double time, Vector3d position, Vector3d velocity);
    }

    public enum IntegrationMethod
    {
        /// <summary>Second order, symplectic for position-only forces. Default for orbital motion.</summary>
        VelocityVerlet,
        /// <summary>Fourth order, not symplectic: very accurate short-term, slow secular energy drift long-term.</summary>
        RungeKutta4,
        /// <summary>First order, symplectic. Kept for comparison/teaching.</summary>
        SemiImplicitEuler,
        /// <summary>First order, not symplectic: energy grows every orbit. Only for demonstrating why it is unsuitable.</summary>
        ExplicitEuler
    }

    /// <summary>
    /// Explicit one-step integrators for d²r/dt² = a(t, r, v).
    ///
    /// Why Velocity Verlet: for conservative gravity it is symplectic and time-reversible, so the energy error
    /// stays bounded (oscillates with amplitude ~ (Δt/T)²) instead of drifting, which is what keeps orbits
    /// stable over thousands of revolutions. Limitations: it is only second-order accurate (phase error grows
    /// linearly with time), and with velocity-dependent forces (drag) it is no longer symplectic — drag is
    /// evaluated with a predicted velocity, which is first-order in the drag term but dissipative anyway.
    /// </summary>
    public static class NumericalIntegrator
    {
        public static BodyState Step(BodyState s, double dt, IAccelerationModel model, IntegrationMethod method)
        {
            switch (method)
            {
                case IntegrationMethod.VelocityVerlet: return VelocityVerlet(s, dt, model);
                case IntegrationMethod.RungeKutta4: return RungeKutta4(s, dt, model);
                case IntegrationMethod.SemiImplicitEuler: return SemiImplicitEuler(s, dt, model);
                case IntegrationMethod.ExplicitEuler: return ExplicitEuler(s, dt, model);
                default: throw new ArgumentOutOfRangeException(nameof(method));
            }
        }

        /// <summary>
        /// x₁ = x₀ + v₀Δt + ½a₀Δt²;  a₁ = a(x₁, v*);  v₁ = v₀ + ½(a₀ + a₁)Δt,
        /// where v* = v₀ + a₀Δt is a predictor used only by velocity-dependent forces.
        /// </summary>
        public static BodyState VelocityVerlet(BodyState s, double dt, IAccelerationModel model)
        {
            Vector3d a0 = model.Acceleration(s.Time, s.Position, s.Velocity);
            Vector3d x1 = s.Position + s.Velocity * dt + a0 * (0.5 * dt * dt);
            Vector3d vPredicted = s.Velocity + a0 * dt;
            Vector3d a1 = model.Acceleration(s.Time + dt, x1, vPredicted);
            Vector3d v1 = s.Velocity + (a0 + a1) * (0.5 * dt);
            return new BodyState(x1, v1, s.Time + dt);
        }

        public static BodyState RungeKutta4(BodyState s, double dt, IAccelerationModel model)
        {
            double t = s.Time;
            double h = 0.5 * dt;
            Vector3d x = s.Position, v = s.Velocity;

            Vector3d k1x = v;
            Vector3d k1v = model.Acceleration(t, x, v);
            Vector3d k2x = v + k1v * h;
            Vector3d k2v = model.Acceleration(t + h, x + k1x * h, k2x);
            Vector3d k3x = v + k2v * h;
            Vector3d k3v = model.Acceleration(t + h, x + k2x * h, k3x);
            Vector3d k4x = v + k3v * dt;
            Vector3d k4v = model.Acceleration(t + dt, x + k3x * dt, k4x);

            double w = dt / 6.0;
            return new BodyState(
                x + (k1x + 2.0 * k2x + 2.0 * k3x + k4x) * w,
                v + (k1v + 2.0 * k2v + 2.0 * k3v + k4v) * w,
                t + dt);
        }

        public static BodyState SemiImplicitEuler(BodyState s, double dt, IAccelerationModel model)
        {
            Vector3d v1 = s.Velocity + model.Acceleration(s.Time, s.Position, s.Velocity) * dt;
            return new BodyState(s.Position + v1 * dt, v1, s.Time + dt);
        }

        public static BodyState ExplicitEuler(BodyState s, double dt, IAccelerationModel model)
        {
            Vector3d a = model.Acceleration(s.Time, s.Position, s.Velocity);
            return new BodyState(s.Position + s.Velocity * dt, s.Velocity + a * dt, s.Time + dt);
        }

        /// <summary>
        /// Largest step that keeps orbital integration accurate near a central body:
        /// Δt_max = η·√(r³/μ) (η × local dynamical time; one circular orbit is 2π·√(r³/μ)).
        /// </summary>
        public static double AccuracyLimitedStep(double mu, double distance, double accuracyFactor)
        {
            if (mu <= 0.0 || distance <= 0.0) return double.PositiveInfinity;
            return accuracyFactor * Math.Sqrt(distance * distance * distance / mu);
        }

        /// <summary>Number of equal sub-steps so that each is ≤ <paramref name="maxStep"/>.</summary>
        public static int SubstepCount(double dt, double maxStep, int maxSubsteps)
        {
            if (!(maxStep > 0.0) || double.IsInfinity(maxStep) || dt <= maxStep) return 1;
            double n = Math.Ceiling(dt / maxStep);
            return n > maxSubsteps ? maxSubsteps : (int)n;
        }
    }
}
