using System;
using System.Collections.Generic;

namespace SpaceSim.Flight
{
    public enum DesignWarningCode
    {
        CannotLiftOff,
        LowUpperStageThrust,
        InsufficientDeltaV,
        StageWithoutEngine,
        StageWithoutPropellant
    }

    /// <summary>A consequence of a design choice, explained (never a hard block).</summary>
    public readonly struct DesignWarning
    {
        public DesignWarning(DesignWarningCode code, string message)
        {
            Code = code;
            Message = message;
        }

        public DesignWarningCode Code { get; }
        public string Message { get; }
    }

    /// <summary>Ideal (Level A) figures of one stage, computed with everything above it as payload.</summary>
    public sealed class StageReport
    {
        public string Name;
        public double DryMass, PropellantMass;
        public double MassAtIgnition, MassAtBurnout;
        public double VacuumThrust, SurfaceThrust;
        public double VacuumIsp, SurfaceIsp;
        public double MassFlow, BurnTime;
        /// <summary>Ideal Δv in vacuum [m/s].</summary>
        public double DeltaVVacuum;
        /// <summary>Ideal Δv with the surface-pressure Isp [m/s] (lower bound for a first stage).</summary>
        public double DeltaVSurface;
        /// <summary>TWR at ignition with the given gravity (surface thrust for the first stage, vacuum otherwise).</summary>
        public double IgnitionTwr;
    }

    public sealed class RocketDesignReport
    {
        public readonly List<StageReport> Stages = new List<StageReport>();
        public readonly List<DesignWarning> Warnings = new List<DesignWarning>();
        public double PayloadMass;
        public double DryMass;          // all stages' dry mass + payload
        public double PropellantMass;   // all stages
        public double LaunchMass;
        public double MaxSurfaceThrust; // first stage at full throttle, surface pressure
        public double MaxVacuumThrust;  // largest stage thrust in vacuum
        public double LiftoffTwr;
        /// <summary>Ideal total Δv in vacuum [m/s] — an upper bound, not a flight prediction.</summary>
        public double TotalDeltaVVacuum;
        public double RequiredDeltaV;
        public double SurfaceGravity;
        public bool CanLiftOff => LiftoffTwr > 1.0;
        public bool MeetsRequirement => RequiredDeltaV <= 0.0 || TotalDeltaVVacuum >= RequiredDeltaV;
    }

    /// <summary>
    /// Level A analysis of a rocket configuration (hangar, academy, planning). Uses <see cref="RocketEquations"/> only —
    /// the same relations the live <see cref="RocketState"/> uses. Produces warnings that explain consequences instead of
    /// forbidding unusual designs.
    /// </summary>
    public static class RocketDesignAnalysis
    {
        /// <param name="surfaceGravity">Local gravity at the launch site [m/s²].</param>
        /// <param name="requiredDeltaV">Mission requirement [m/s]; 0 = none.</param>
        public static RocketDesignReport Analyze(RocketConfiguration config, double surfaceGravity, double requiredDeltaV = 0.0)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var r = new RocketDesignReport { PayloadMass = config.payloadMass, SurfaceGravity = surfaceGravity, RequiredDeltaV = requiredDeltaV };
            RocketStage[] stages = config.stages ?? new RocketStage[0];

            r.DryMass = config.payloadMass;
            foreach (var s in stages)
            {
                r.DryMass += s.dryMass;
                r.PropellantMass += s.propellantMass;
            }
            r.LaunchMass = r.DryMass + r.PropellantMass;

            double above = r.LaunchMass;
            for (int i = 0; i < stages.Length; i++)
            {
                RocketStage s = stages[i];
                var sr = new StageReport
                {
                    Name = s.name,
                    DryMass = s.dryMass,
                    PropellantMass = s.propellantMass,
                    MassAtIgnition = above,
                    MassAtBurnout = above - s.propellantMass,
                    VacuumThrust = s.vacuumThrust,
                    VacuumIsp = s.vacuumIsp,
                    SurfaceIsp = s.seaLevelIsp
                };
                bool hasEngine = s.vacuumThrust > 0.0 && s.vacuumIsp > 0.0;
                if (hasEngine)
                {
                    sr.MassFlow = RocketEquations.MassFlow(s.vacuumThrust, s.vacuumIsp);
                    sr.SurfaceThrust = RocketEquations.ThrustFromMassFlow(sr.MassFlow, Math.Max(1.0, s.seaLevelIsp));
                    sr.BurnTime = RocketEquations.BurnTime(s.propellantMass, sr.MassFlow);
                    if (s.propellantMass > 0.0)
                    {
                        sr.DeltaVVacuum = RocketEquations.DeltaV(s.vacuumIsp, sr.MassAtIgnition, sr.MassAtBurnout);
                        sr.DeltaVSurface = RocketEquations.DeltaV(Math.Max(1.0, s.seaLevelIsp), sr.MassAtIgnition, sr.MassAtBurnout);
                    }
                    double thrustForTwr = i == 0 ? sr.SurfaceThrust : sr.VacuumThrust;
                    sr.IgnitionTwr = RocketEquations.ThrustToWeight(thrustForTwr, sr.MassAtIgnition, surfaceGravity);
                }
                else
                {
                    r.Warnings.Add(new DesignWarning(DesignWarningCode.StageWithoutEngine,
                        s.name + ": нет двигателя — ступень не даёт Δv, только добавляет массу."));
                }
                if (s.propellantMass <= 0.0)
                    r.Warnings.Add(new DesignWarning(DesignWarningCode.StageWithoutPropellant, s.name + ": нет топлива — ступень бесполезна."));

                r.TotalDeltaVVacuum += sr.DeltaVVacuum;
                r.MaxVacuumThrust = Math.Max(r.MaxVacuumThrust, sr.VacuumThrust);
                if (i == 0)
                {
                    r.MaxSurfaceThrust = sr.SurfaceThrust;
                    r.LiftoffTwr = sr.IgnitionTwr;
                }
                else if (hasEngine && sr.IgnitionTwr < 0.3)
                {
                    r.Warnings.Add(new DesignWarning(DesignWarningCode.LowUpperStageThrust,
                        s.name + ": тяга/вес " + sr.IgnitionTwr.ToString("0.00") + " — разгон будет долгим, гравитационные потери велики."));
                }
                r.Stages.Add(sr);
                above = sr.MassAtBurnout - s.dryMass;
            }

            if (stages.Length > 0 && !r.CanLiftOff)
                r.Warnings.Add(new DesignWarning(DesignWarningCode.CannotLiftOff,
                    "Тяга/вес на старте " + r.LiftoffTwr.ToString("0.00") + " < 1: тяга двигателей (" + (r.MaxSurfaceThrust / 1e6).ToString("0.00") +
                    " МН) меньше веса ракеты m·g = " + (r.LaunchMass * surfaceGravity / 1e6).ToString("0.00") + " МН. Ракета не оторвётся от площадки."));
            if (!r.MeetsRequirement)
                r.Warnings.Add(new DesignWarning(DesignWarningCode.InsufficientDeltaV,
                    "Идеальный запас Δv " + r.TotalDeltaVVacuum.ToString("0") + " м/с меньше требуемого " + requiredDeltaV.ToString("0") +
                    " м/с — и это ещё без потерь на гравитацию и сопротивление."));
            return r;
        }
    }
}
