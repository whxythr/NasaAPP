using System;
using SpaceSim.Units;

namespace SpaceSim.Flight
{
    /// <summary>
    /// Level A (theory): ideal rocket relations, SI units, no state. The single implementation used by the live
    /// rocket (<see cref="RocketState"/>), the hangar analysis, the Flight Academy calculators and flight planning.
    /// g₀ = 9.80665 m/s² is the unit that converts Isp [s] to effective exhaust velocity — never local gravity.
    /// </summary>
    public static class RocketEquations
    {
        public const double G0 = PhysicalConstants.StandardGravity;

        /// <summary>Effective exhaust velocity vₑ = Isp·g₀ [m/s].</summary>
        public static double ExhaustVelocity(double isp) => isp * G0;

        /// <summary>Tsiolkovsky: Δv = Isp·g₀·ln(m₀/m_f) [m/s]. Ideal: no gravity, drag or steering losses.</summary>
        public static double DeltaV(double isp, double initialMass, double finalMass)
        {
            if (!(initialMass > 0.0) || !(finalMass > 0.0)) throw new ArgumentOutOfRangeException(nameof(finalMass), "Masses must be positive.");
            if (finalMass > initialMass) throw new ArgumentException("Final mass cannot exceed initial mass.");
            return isp * G0 * Math.Log(initialMass / finalMass);
        }

        /// <summary>Mass ratio m₀/m_f needed for Δv: exp(Δv/(Isp·g₀)).</summary>
        public static double MassRatio(double deltaV, double isp) => Math.Exp(deltaV / (isp * G0));

        /// <summary>Propellant [kg] needed to give Δv to a vehicle that ends with <paramref name="finalMass"/> [kg].</summary>
        public static double PropellantForDeltaV(double deltaV, double isp, double finalMass) =>
            finalMass * (MassRatio(deltaV, isp) - 1.0);

        /// <summary>Mass flow ṁ = F/(Isp·g₀) [kg/s].</summary>
        public static double MassFlow(double thrust, double isp) => thrust / (isp * G0);

        /// <summary>Thrust F = ṁ·Isp·g₀ [N].</summary>
        public static double ThrustFromMassFlow(double massFlow, double isp) => massFlow * isp * G0;

        /// <summary>Burn time t = m_prop/ṁ [s] at constant mass flow.</summary>
        public static double BurnTime(double propellant, double massFlow) =>
            massFlow > 0.0 ? propellant / massFlow : double.PositiveInfinity;

        /// <summary>Thrust-to-weight ratio TWR = F/(m·g) with the LOCAL gravity g [m/s²]. &lt; 1 ⇒ cannot lift off vertically.</summary>
        public static double ThrustToWeight(double thrust, double mass, double localGravity) =>
            mass > 0.0 && localGravity > 0.0 ? thrust / (mass * localGravity) : double.PositiveInfinity;

        /// <summary>Newton's second law a = F/m [m/s²].</summary>
        public static double Acceleration(double force, double mass) => force / mass;

        /// <summary>Burn duration [s] to change velocity by Δv with constant thrust (mass decreases during the burn).</summary>
        public static double BurnTimeForDeltaV(double deltaV, double isp, double initialMass, double thrust)
        {
            double mf = initialMass / MassRatio(deltaV, isp);
            return BurnTime(initialMass - mf, MassFlow(thrust, isp));
        }
    }
}
