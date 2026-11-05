using System;
using SpaceSim.Mathematics;
using SpaceSim.Units;

namespace SpaceSim.Flight
{
    /// <summary>One rocket stage (SI units).</summary>
    [Serializable]
    public class RocketStage
    {
        public string name = "Stage";
        /// <summary>Structure + engines without propellant [kg].</summary>
        public double dryMass = 20000.0;
        /// <summary>Usable propellant [kg].</summary>
        public double propellantMass = 200000.0;
        /// <summary>Thrust in vacuum at full throttle [N].</summary>
        public double vacuumThrust = 4.0e6;
        /// <summary>Specific impulse in vacuum [s].</summary>
        public double vacuumIsp = 330.0;
        /// <summary>Specific impulse at the planet's surface pressure [s] (nozzle back-pressure loss).</summary>
        public double seaLevelIsp = 295.0;
        /// <summary>Visual length of the stage [m].</summary>
        public double length = 30.0;

        public RocketStage Clone() => (RocketStage)MemberwiseClone();
    }

    /// <summary>A stacked multi-stage rocket. Stage 0 burns first.</summary>
    [Serializable]
    public class RocketConfiguration
    {
        public RocketStage[] stages =
        {
            new RocketStage { name = "Ступень 1", dryMass = 22000, propellantMass = 300000, vacuumThrust = 5.2e6, vacuumIsp = 330, seaLevelIsp = 295, length = 36 },
            new RocketStage { name = "Ступень 2", dryMass = 4000, propellantMass = 40000, vacuumThrust = 7.0e5, vacuumIsp = 350, seaLevelIsp = 240, length = 12 }
        };
        /// <summary>Payload on top (the probe) [kg].</summary>
        public double payloadMass = 1000.0;
        /// <summary>Radius of the stack [m] (visual and reference area).</summary>
        public double radius = 1.85;
        public double dragCoefficient = 0.3;

        public double ReferenceArea => Math.PI * radius * radius;

        public RocketConfiguration Clone()
        {
            var c = (RocketConfiguration)MemberwiseClone();
            c.stages = new RocketStage[stages.Length];
            for (int i = 0; i < stages.Length; i++) c.stages[i] = stages[i].Clone();
            return c;
        }
    }

    /// <summary>
    /// Live state of a rocket: remaining propellant per stage, engine, throttle and attitude. The player sets
    /// throttle and attitude; nothing here steers towards an orbit.
    ///
    /// Engine model: constant mass flow ṁ = throttle·F_vac/(I_sp,vac·g₀); thrust F = ṁ·g₀·I_sp(p) with
    /// I_sp(p) = I_sp,vac + (I_sp,SL − I_sp,vac)·p/p₀ — so thrust grows as the air thins.
    /// Attitude: pitch from the local vertical (0° = straight up, 90° = horizontal) and heading from north
    /// (90° = east), relative to the local horizon of the current position.
    /// </summary>
    public sealed class RocketState
    {
        readonly RocketConfiguration config;

        public RocketState(RocketConfiguration configuration)
        {
            config = (configuration ?? new RocketConfiguration()).Clone();
            Propellant = new double[config.stages.Length];
            for (int i = 0; i < Propellant.Length; i++) Propellant[i] = Math.Max(0.0, config.stages[i].propellantMass);
            HeadingDegrees = 90.0;
            Throttle = 1.0;
        }

        public RocketConfiguration Configuration => config;
        public double[] Propellant { get; }
        /// <summary>Index of the stage currently attached at the bottom; = stage count when only the payload is left.</summary>
        public int StageIndex { get; private set; }
        public bool EngineOn { get; set; }

        double throttle;
        public double Throttle
        {
            get => throttle;
            set => throttle = MathD.Clamp01(value);
        }

        double pitch;
        public double PitchDegrees
        {
            get => pitch;
            set => pitch = MathD.Clamp(value, 0.0, 180.0);
        }

        double heading;
        public double HeadingDegrees
        {
            get => heading;
            set => heading = ((value % 360.0) + 360.0) % 360.0;
        }

        public int StageCount => config.stages.Length;
        public bool HasStages => StageIndex < config.stages.Length;
        public RocketStage CurrentStage => HasStages ? config.stages[StageIndex] : null;
        public double CurrentPropellant => HasStages ? Propellant[StageIndex] : 0.0;
        public bool CanBurn => HasStages && Propellant[StageIndex] > 0.0 && config.stages[StageIndex].vacuumThrust > 0.0;
        public bool IsBurning => EngineOn && CanBurn && throttle > 0.0;

        /// <summary>Current total mass [kg].</summary>
        public double Mass
        {
            get
            {
                double m = config.payloadMass;
                for (int i = StageIndex; i < config.stages.Length; i++) m += config.stages[i].dryMass + Propellant[i];
                return m;
            }
        }

        /// <summary>Propellant mass flow of the current stage at the current throttle [kg/s].</summary>
        public double MassFlow
        {
            get
            {
                if (!IsBurning) return 0.0;
                RocketStage s = CurrentStage;
                return throttle * RocketEquations.MassFlow(s.vacuumThrust, s.vacuumIsp);
            }
        }

        /// <summary>Mass flow at full throttle of the current stage, whether or not the engine is running [kg/s].</summary>
        public double FullThrottleMassFlow => HasStages && CurrentStage.vacuumIsp > 0.0
            ? RocketEquations.MassFlow(CurrentStage.vacuumThrust, CurrentStage.vacuumIsp)
            : 0.0;

        /// <summary>
        /// Thrust-to-weight ratio of the current stage at full throttle, at ambient pressure ratio p/p₀ and local gravity g —
        /// meaningful also while the engine is off (what the rocket COULD do).
        /// </summary>
        public double AvailableThrustToWeight(double pressureRatio, double localGravity) =>
            RocketEquations.ThrustToWeight(RocketEquations.ThrustFromMassFlow(FullThrottleMassFlow, Isp(pressureRatio)), Mass, localGravity);

        /// <summary>Specific impulse at ambient pressure ratio p/p₀ (0 = vacuum) [s].</summary>
        public double Isp(double pressureRatio)
        {
            RocketStage s = CurrentStage;
            if (s == null) return 0.0;
            return Math.Max(1.0, s.vacuumIsp + (s.seaLevelIsp - s.vacuumIsp) * MathD.Clamp01(pressureRatio));
        }

        /// <summary>Thrust [N] at ambient pressure ratio p/p₀.</summary>
        public double Thrust(double pressureRatio) => RocketEquations.ThrustFromMassFlow(MassFlow, Isp(pressureRatio));

        /// <summary>Burns propellant for dt seconds. Flames out (engine off) when the stage is empty.</summary>
        public void Burn(double dt)
        {
            double mdot = MassFlow;
            if (mdot <= 0.0) return;
            double left = Propellant[StageIndex] - mdot * dt;
            // Treat a rounding residue (< 1 mg) as empty, otherwise the engine would stay "on" for one more tick.
            if (left < 1e-6) left = 0.0;
            Propellant[StageIndex] = left;
            if (left <= 0.0) EngineOn = false;
        }

        /// <summary>Drops the current stage. Returns false if only the payload is left.</summary>
        public bool Separate()
        {
            if (!HasStages) return false;
            StageIndex++;
            return true;
        }

        /// <summary>Remaining ideal (vacuum) Δv over all remaining stages, Tsiolkovsky: Σ I_sp·g₀·ln(m₀/m₁) [m/s].</summary>
        public double DeltaVRemaining()
        {
            double dv = 0.0;
            for (int i = StageIndex; i < config.stages.Length; i++)
            {
                double above = config.payloadMass;
                for (int j = i + 1; j < config.stages.Length; j++) above += config.stages[j].dryMass + Propellant[j];
                double m0 = above + config.stages[i].dryMass + Propellant[i];
                double m1 = above + config.stages[i].dryMass;
                if (config.stages[i].vacuumIsp > 0.0 && m1 > 0.0) dv += RocketEquations.DeltaV(config.stages[i].vacuumIsp, m0, m1);
            }
            return dv;
        }

        /// <summary>Thrust direction in the local frame (up, north, east components).</summary>
        public void LocalDirection(out double up, out double north, out double east)
        {
            double p = pitch * MathD.DegToRad, h = heading * MathD.DegToRad;
            up = Math.Cos(p);
            north = Math.Sin(p) * Math.Cos(h);
            east = Math.Sin(p) * Math.Sin(h);
        }
    }
}
