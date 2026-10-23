using System;

namespace SpaceSim.Flight
{
    /// <summary>
    /// Descriptive flight phase for HUD, hints and visual effects. It never changes the physics and never
    /// blocks motion — it is a classification of the current physical state, not a game mode.
    /// </summary>
    public enum FlightPhase
    {
        Ground,
        AtmosphericAscent,
        UpperAtmosphere,
        Spaceflight,
        OrbitalFlight,
        EscapeTrajectory,
        AtmosphericDescent,
        Landing
    }

    /// <summary>Why the classifier picked the phase (for explanatory HUD text).</summary>
    public enum FlightPhaseReason
    {
        RestingOnSurface,
        ClimbingInDenseAir,
        DescendingInDenseAir,
        ThinAirAboveThreshold,
        CloseToSurfaceDescending,
        AboveBoundarySuborbital,
        AboveBoundaryPeriapsisInAtmosphere,
        AboveBoundaryPeriapsisBelowSurface,
        BoundOrbitPeriapsisClear,
        UnboundEnergy
    }

    /// <summary>Thresholds of the classifier. Visual/informational only.</summary>
    [Serializable]
    public class FlightPhaseThresholds
    {
        /// <summary>ρ/ρ₀ below which the air is considered "upper atmosphere" (0.01 ≈ 39 km on Earth).</summary>
        public double upperAtmosphereDensityRatio = 0.01;
        /// <summary>Height above the surface under which a descent is called a landing approach [m].</summary>
        public double landingApproachAltitude = 2000.0;
        /// <summary>Lowest periapsis altitude above the reference radius for an orbit to count as sustained [m].
        /// Below this, drag removes energy quickly even if ε &lt; 0.</summary>
        public double minimumStablePeriapsisAltitude = 140000.0;
        /// <summary>Dead band for climbing/descending decisions [m/s], prevents flicker near apex.</summary>
        public double verticalSpeedDeadband = 1.0;
    }
}
