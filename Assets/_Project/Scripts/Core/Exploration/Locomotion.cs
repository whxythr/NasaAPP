using System;
using SpaceSim.Units;

namespace SpaceSim.Exploration
{
    /// <summary>How the astronaut is moving: inverted-pendulum walk, bouncing run (Fr ≥ 0.5), or a ballistic flight.</summary>
    public enum Gait { Standing, Walking, Running, Airborne }

    /// <summary>What sets the top running speed on this world.</summary>
    public enum SprintLimit
    {
        /// <summary>Low gravity: faster strides throw the suited astronaut into long, uncontrollable flights (Fr cap).</summary>
        Froude,
        /// <summary>Strong gravity: the legs cannot push hard enough in a short stance (Weyand ground-force limit).</summary>
        Strength,
        /// <summary>Too heavy: the legs cannot hold the body in the air between steps — running is impossible.</summary>
        Weight
    }

    /// <summary>
    /// Level A formulas for an astronaut on foot in any gravity (SI). One place for the walker, the HUD and the tests.
    ///
    /// Speed: dynamic similarity — walking and running look alike at equal Froude numbers Fr = v²/(g·L), so the walk is
    /// v = √(Fr·g·L) and the walk→run switch is at Fr ≈ 0.5 on every world (Kram, Domingo &amp; Ferris 1997). The top running
    /// speed is the lower of two limits: the Fr cap (low gravity) and the ground-force limit of Weyand et al. (2000):
    /// the leg must deliver the whole stride's vertical impulse during a stance that lasts L_c/v while the other leg swings
    /// in a fixed t_sw, so v = L_c·(2k − 1)/t_sw with k = F_legs/(m·g). On a heavy world k → 1 and running disappears.
    ///
    /// Effort: the metabolic cost of transport is nearly proportional to the weight carried (Farley &amp; McMahon 1992,
    /// Taylor et al. 1980): P = P_rest + C·(m·g/g₀)·v. Above the critical power CP the anaerobic reserve W′ drains
    /// (critical-power model, Monod &amp; Scherrer 1965); below it W′ refills (Skiba et al. 2015) — see <see cref="WorkCapacity"/>.
    /// g₀ is used only as a unit: muscle strength and the costs were measured on Earth.
    /// </summary>
    public static class Locomotion
    {
        const double G0 = PhysicalConstants.StandardGravity;

        /// <summary>Astronaut without the suit [kg].</summary>
        public const double BodyMass = 75.0;
        /// <summary>Suit with life support [kg] (an Apollo A7L with PLSS ≈ 90 kg; a lighter surface suit is assumed).</summary>
        public const double SuitMass = 45.0;
        public const double TotalMass = BodyMass + SuitMass;
        /// <summary>Leg length L [m] in Fr = v²/(g·L).</summary>
        public const double LegLength = 0.9;
        /// <summary>Comfortable walk.</summary>
        public const double WalkFroude = 0.25;
        /// <summary>Walk→run transition, the same at 0.1–1 g.</summary>
        public const double TransitionFroude = 0.5;
        /// <summary>Running cap in a suit. Calibrated on the Apollo lunar lope: ≈ 2 m/s at 1.62 m/s² (√(3·1.62·0.9) = 2.09).</summary>
        public const double SprintFroude = 3.0;
        /// <summary>Mean vertical force a leg can apply during the stance, in body weights on Earth (sprinters ≈ 2.5).</summary>
        public const double LegForce = 2.2;
        /// <summary>Distance the body travels during one stance [m].</summary>
        public const double ContactLength = 0.9;
        /// <summary>Time to swing a leg forward [s]: ≈ 0.37 s unsuited at any speed (Weyand 2000), slower in a suit.</summary>
        public const double SwingTime = 0.45;
        /// <summary>Metabolic cost of transport [J per kg of Earth weight per m]: walking, running.</summary>
        public const double WalkCost = 2.2, RunCost = 4.0;
        /// <summary>Metabolic power at rest, standing in the suit [W].</summary>
        public const double RestPower = 100.0;
        /// <summary>Critical power CP [W]: the highest metabolic power sustained without tiring (≈ 80 % of VO₂max of a trained astronaut).</summary>
        public const double CriticalPower = 900.0;
        /// <summary>Anaerobic work capacity W′ above CP [J].</summary>
        public const double AnaerobicCapacity = 40000.0;
        /// <summary>After exhaustion the astronaut sprints again only when W′ is back to this fraction.</summary>
        public const double RecoveredFraction = 0.3;
        /// <summary>Boot-ground friction coefficient: horizontal acceleration ≤ μ·g.</summary>
        public const double Friction = 0.6;

        /// <summary>v = √(Fr·g·L) [m/s].</summary>
        public static double FroudeSpeed(double froude, double g) => Math.Sqrt(Math.Max(0.0, froude * g * LegLength));

        /// <summary>Fr = v²/(g·L).</summary>
        public static double FroudeNumber(double speed, double g) => g > 0.0 ? speed * speed / (g * LegLength) : double.PositiveInfinity;

        public static double WalkSpeed(double g) => FroudeSpeed(WalkFroude, g);

        /// <summary>Speed where walking turns into running, √(0.5·g·L).</summary>
        public static double TransitionSpeed(double g) => FroudeSpeed(TransitionFroude, g);

        /// <summary>k = F_legs/(m·g): how many times the legs can out-push the suited astronaut's weight here.</summary>
        public static double ForceRatio(double g) => g > 0.0 ? LegForce * BodyMass * G0 / (TotalMass * g) : double.PositiveInfinity;

        /// <summary>Running needs k &gt; 1: the stance leg must lift the body into a flight phase.</summary>
        public static bool CanRun(double g) => ForceRatio(g) > 1.0;

        /// <summary>Weyand ground-force limit v = L_c·(2k − 1)/t_sw [m/s]; 0 when running is impossible.</summary>
        public static double StrengthLimitedSpeed(double g)
        {
            double k = ForceRatio(g);
            if (double.IsPositiveInfinity(k)) return double.PositiveInfinity;
            return k <= 1.0 ? 0.0 : ContactLength * (2.0 * k - 1.0) / SwingTime;
        }

        /// <summary>Top running speed: min(Fr cap, ground-force limit). Where running is impossible — the walking speed.</summary>
        public static double SprintSpeed(double g) =>
            CanRun(g) ? Math.Max(WalkSpeed(g), Math.Min(FroudeSpeed(SprintFroude, g), StrengthLimitedSpeed(g))) : WalkSpeed(g);

        public static SprintLimit SprintLimitedBy(double g)
        {
            if (!CanRun(g)) return SprintLimit.Weight;
            return FroudeSpeed(SprintFroude, g) <= StrengthLimitedSpeed(g) ? SprintLimit.Froude : SprintLimit.Strength;
        }

        /// <summary>Weight of the suited astronaut in "Earth kilograms", m·g/g₀ [kg].</summary>
        public static double EffectiveLoad(double g) => TotalMass * g / G0;

        /// <summary>Cost of transport for the gait at this speed (running from Fr ≥ 0.5) [J/(kg·m)].</summary>
        public static double CostOfTransport(double speed, double g) => FroudeNumber(speed, g) >= TransitionFroude ? RunCost : WalkCost;

        /// <summary>Metabolic power on the ground P = P_rest + C·(m·g/g₀)·v [W].</summary>
        public static double MetabolicPower(double speed, double g) => RestPower + CostOfTransport(speed, g) * EffectiveLoad(g) * Math.Max(0.0, speed);

        /// <summary>The fastest pace of a gait that does not touch the reserve: P(v) = CP → v = (CP − P_rest)/(C·m·g/g₀) [m/s].</summary>
        public static double SustainableSpeed(double g, bool running)
        {
            double load = EffectiveLoad(g);
            return load > 0.0 ? (CriticalPower - RestPower) / ((running ? RunCost : WalkCost) * load) : double.PositiveInfinity;
        }

        /// <summary>How long a sprint from a full reserve lasts, W′/(P − CP) [s]; ∞ where sprinting stays below CP.</summary>
        public static double SprintEndurance(double g)
        {
            double p = MetabolicPower(SprintSpeed(g), g);
            return p > CriticalPower ? AnaerobicCapacity / (p - CriticalPower) : double.PositiveInfinity;
        }

        /// <summary>Time to refill W′ from empty to <paramref name="fraction"/> at power P &lt; CP: t = −W′/(CP − P)·ln(1 − f) [s].</summary>
        public static double RecoveryTime(double fraction, double power)
        {
            if (power >= CriticalPower) return double.PositiveInfinity;
            return -AnaerobicCapacity / (CriticalPower - power) * Math.Log(1.0 - Math.Min(fraction, 1.0 - 1e-12));
        }

        /// <summary>Largest horizontal acceleration the boots can give, μ·g [m/s²]: on a small world starting and stopping take long.</summary>
        public static double MaxAcceleration(double g) => Friction * g;

        /// <summary>
        /// Body lean needed to accelerate: the ground force must point through the centre of mass, tan θ = a/g [rad].
        /// The same acceleration needs a far deeper lean on a small world.
        /// </summary>
        public static double LeanAngle(double acceleration, double g) => Math.Atan2(acceleration, Math.Max(g, 1e-9));

        /// <summary>
        /// Steps per second. Dynamic similarity: f·√(L/g) depends only on Fr; fitted to human gait on Earth
        /// (≈ 1.8 Hz walking at 1.5 m/s, ≈ 2.6 Hz running at 3 m/s): f = 0.77·Fr^¼·√(g/L). Visual only.
        /// </summary>
        public static double StepFrequency(double speed, double g)
        {
            if (g <= 0.0 || speed <= 0.0) return 0.0;
            return 0.77 * Math.Pow(FroudeNumber(speed, g), 0.25) * Math.Sqrt(g / LegLength);
        }

        /// <summary>Height of the flight phase of a running step: the stance lasts L_c/v, the rest of the step is a ballistic hop, h = g·t²/8 [m].</summary>
        public static double StrideFlightHeight(double speed, double g)
        {
            double f = StepFrequency(speed, g);
            if (f <= 0.0) return 0.0;
            double flight = Math.Max(0.0, 1.0 / f - ContactLength / speed);
            return g * flight * flight / 8.0;
        }
    }

    /// <summary>
    /// The anaerobic reserve W′ (critical-power model). Above the critical power CP it drains at P − CP; below CP it
    /// refills as dW′/dt = (CP − P)·(W′₀ − W′)/W′₀ (Skiba et al. 2015), integrated exactly over each step.
    /// Empty → exhausted until the reserve is back to <see cref="Locomotion.RecoveredFraction"/>.
    /// </summary>
    public sealed class WorkCapacity
    {
        public WorkCapacity(double criticalPower = Locomotion.CriticalPower, double capacity = Locomotion.AnaerobicCapacity)
        {
            CriticalPower = criticalPower;
            Capacity = capacity;
            Balance = capacity;
        }

        public double CriticalPower { get; }
        public double Capacity { get; }
        /// <summary>Reserve left [J].</summary>
        public double Balance { get; private set; }
        public double Fraction => Capacity > 0.0 ? Balance / Capacity : 0.0;
        public bool Exhausted { get; private set; }

        public void Step(double power, double dt)
        {
            if (dt <= 0.0) return;
            if (power > CriticalPower)
            {
                Balance -= (power - CriticalPower) * dt;
                if (Balance <= 0.0)
                {
                    Balance = 0.0;
                    Exhausted = true;
                }
            }
            else Balance = Capacity - (Capacity - Balance) * Math.Exp(-(CriticalPower - power) * dt / Capacity);
            if (Exhausted && Fraction >= Locomotion.RecoveredFraction) Exhausted = false;
        }

        public void Refill()
        {
            Balance = Capacity;
            Exhausted = false;
        }
    }
}
