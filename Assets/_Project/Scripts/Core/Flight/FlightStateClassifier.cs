using SpaceSim.Planets;

namespace SpaceSim.Flight
{
    /// <summary>
    /// Classifies the flight phase from physical criteria — energy, periapsis, radial velocity, air density,
    /// surface contact — not from altitude alone. Being above 100 km does not mean being in orbit: an orbit
    /// additionally needs ε &lt; 0 and a periapsis that clears the atmosphere and the ground.
    /// </summary>
    public sealed class FlightStateClassifier
    {
        public FlightStateClassifier(FlightPhaseThresholds thresholds)
        {
            Thresholds = thresholds ?? new FlightPhaseThresholds();
        }

        public FlightPhaseThresholds Thresholds { get; set; }

        public FlightPhase Phase { get; private set; } = FlightPhase.Ground;
        public FlightPhaseReason Reason { get; private set; } = FlightPhaseReason.RestingOnSurface;

        bool wasClimbing = true;

        public FlightPhase Update(FlightTelemetry t, PlanetModel planet)
        {
            Evaluate(t, planet, out FlightPhase phase, out FlightPhaseReason reason);
            Phase = phase;
            Reason = reason;
            return phase;
        }

        void Evaluate(FlightTelemetry t, PlanetModel planet, out FlightPhase phase, out FlightPhaseReason reason)
        {
            var th = Thresholds;

            if (t.IsLanded)
            {
                wasClimbing = true;
                phase = FlightPhase.Ground;
                reason = FlightPhaseReason.RestingOnSurface;
                return;
            }

            // Hysteresis on the sign of vertical speed (surface-relative).
            if (t.VelocityUp > th.verticalSpeedDeadband) wasClimbing = true;
            else if (t.VelocityUp < -th.verticalSpeedDeadband) wasClimbing = false;

            double terrainTop = planet.Radius + planet.Surface.MaxSurfaceHeight;
            bool hasAir = planet.Atmosphere.ReferenceDensity > 0.0;
            bool aboveBoundary = t.AltitudeAboveReference >= planet.SpaceBoundaryAltitude;

            // Airless bodies have no atmospheric regime: the orbital criteria apply at any altitude.
            if (aboveBoundary || !hasAir)
            {
                if (t.SpecificEnergy >= 0.0)
                {
                    phase = FlightPhase.EscapeTrajectory;
                    reason = FlightPhaseReason.UnboundEnergy;
                    return;
                }
                double periapsisAltitude = t.Orbit.PeriapsisRadius - planet.Radius;
                if (t.Orbit.PeriapsisRadius <= terrainTop)
                {
                    bool approaching = !wasClimbing && t.AltitudeAboveSurface < th.landingApproachAltitude;
                    phase = approaching ? FlightPhase.Landing : FlightPhase.Spaceflight;
                    reason = approaching ? FlightPhaseReason.CloseToSurfaceDescending : FlightPhaseReason.AboveBoundaryPeriapsisBelowSurface;
                    return;
                }
                if (hasAir && periapsisAltitude < th.minimumStablePeriapsisAltitude)
                {
                    phase = FlightPhase.Spaceflight;
                    reason = FlightPhaseReason.AboveBoundaryPeriapsisInAtmosphere;
                    return;
                }
                phase = FlightPhase.OrbitalFlight;
                reason = FlightPhaseReason.BoundOrbitPeriapsisClear;
                return;
            }

            // Below the conventional boundary: inside the atmosphere (or low over an airless body).
            if (!wasClimbing && t.AltitudeAboveSurface < th.landingApproachAltitude)
            {
                phase = FlightPhase.Landing;
                reason = FlightPhaseReason.CloseToSurfaceDescending;
                return;
            }

            bool thinAir = t.DensityRatio < th.upperAtmosphereDensityRatio;
            if (thinAir)
            {
                phase = FlightPhase.UpperAtmosphere;
                reason = FlightPhaseReason.ThinAirAboveThreshold;
                return;
            }

            phase = wasClimbing ? FlightPhase.AtmosphericAscent : FlightPhase.AtmosphericDescent;
            reason = wasClimbing ? FlightPhaseReason.ClimbingInDenseAir : FlightPhaseReason.DescendingInDenseAir;
        }
    }
}
