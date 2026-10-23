using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Physics
{
    /// <summary>Aerodynamic parameters of a body for the simple drag model.</summary>
    [Serializable]
    public struct DragProperties
    {
        public bool enabled;
        /// <summary>Drag coefficient Cd (dimensionless). Sphere ≈ 0.47, blunt capsule ≈ 1.2–1.5.</summary>
        public double dragCoefficient;
        /// <summary>Reference (cross-section) area A [m²].</summary>
        public double referenceArea;

        public DragProperties(bool enabled, double dragCoefficient, double referenceArea)
        {
            this.enabled = enabled;
            this.dragCoefficient = dragCoefficient;
            this.referenceArea = referenceArea;
        }
    }

    /// <summary>
    /// Quadratic drag: F⃗ = −½·ρ·|v⃗_rel|·v⃗_rel·Cd·A, where v⃗_rel is the velocity relative to the local air
    /// (not relative to the ground and not inertial). No lift, no Mach/Reynolds dependence of Cd, no heating.
    /// </summary>
    public static class AtmosphericDrag
    {
        /// <summary>Drag force [N] for velocity relative to the air [m/s].</summary>
        public static Vector3d DragForce(double density, Vector3d velocityRelativeToAir, double dragCoefficient, double area)
        {
            double speed = velocityRelativeToAir.Magnitude;
            if (speed <= 0.0 || density <= 0.0) return Vector3d.Zero;
            return velocityRelativeToAir * (-0.5 * density * speed * dragCoefficient * area);
        }

        /// <summary>Magnitude F = ½ρv²CdA [N].</summary>
        public static double DragForceMagnitude(double density, double airSpeed, double dragCoefficient, double area) =>
            0.5 * density * airSpeed * airSpeed * dragCoefficient * area;

        /// <summary>Dynamic pressure q = ½ρv² [Pa].</summary>
        public static double DynamicPressure(double density, double airSpeed) => 0.5 * density * airSpeed * airSpeed;

        /// <summary>Terminal speed v_t = √(2mg / (ρ·Cd·A)) [m/s] where drag balances gravity.</summary>
        public static double TerminalVelocity(double mass, double gravity, double density, double dragCoefficient, double area)
        {
            double denom = density * dragCoefficient * area;
            return denom > 0.0 ? Math.Sqrt(2.0 * mass * gravity / denom) : double.PositiveInfinity;
        }

        /// <summary>
        /// Velocity of the air at an inertial position relative to the planet centre:
        /// v⃗_air = k·(ω⃗ × r⃗), with k the co-rotation factor (1 = rigid co-rotation, no winds).
        /// </summary>
        public static Vector3d AirVelocity(Vector3d angularVelocity, Vector3d relativePosition, double coRotationFactor) =>
            Vector3d.Cross(angularVelocity, relativePosition) * coRotationFactor;
    }
}
