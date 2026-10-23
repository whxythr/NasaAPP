using System;
using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Units;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>A short message for the HUD (crossing the conventional boundary, contact, phase change).</summary>
    public struct FlightNotification
    {
        public string Text;
        public float RealTime;
    }

    /// <summary>
    /// Computes telemetry from the latest physics state each frame and classifies the flight phase. Read-only
    /// with respect to physics: phases inform the HUD and visuals, they never alter or block motion.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public sealed class FlightStateController : MonoBehaviour
    {
        [SerializeField] FlightPhaseThresholds thresholds = new FlightPhaseThresholds();

        FlightBody body;
        PlanetRuntime planet;
        FlightStateClassifier classifier;
        bool wasAboveBoundary;
        int lastImpactCount;
        bool initialized;

        public FlightTelemetry Telemetry { get; } = new FlightTelemetry();
        public FlightPhase Phase => classifier != null ? classifier.Phase : FlightPhase.Ground;
        public FlightPhaseReason Reason => classifier != null ? classifier.Reason : FlightPhaseReason.RestingOnSurface;
        public readonly List<FlightNotification> Notifications = new List<FlightNotification>();

        public event Action<FlightPhase, FlightPhase> PhaseChanged;

        public void Initialize(FlightBody flightBody, PlanetRuntime planetRuntime)
        {
            body = flightBody;
            planet = planetRuntime;
            classifier = new FlightStateClassifier(thresholds);
            Refresh();
            wasAboveBoundary = Telemetry.AboveSpaceBoundary;
            lastImpactCount = body.Simulation.ImpactCount;
            initialized = true;
        }

        void OnValidate()
        {
            if (classifier != null) classifier.Thresholds = thresholds;
        }

        void Update()
        {
            if (!initialized) return;
            FlightPhase before = classifier.Phase;
            Refresh();
            if (classifier.Phase != before) PhaseChanged?.Invoke(before, classifier.Phase);

            if (Telemetry.AboveSpaceBoundary != wasAboveBoundary)
            {
                string km = UnitFormatter.Number(planet.Model.SpaceBoundaryAltitude / 1000.0, 0);
                Notify(Telemetry.AboveSpaceBoundary
                    ? "Пересечена условная граница космоса (" + km + " км). Атмосфера здесь не заканчивается: ρ = " + UnitFormatter.Density(Telemetry.Density)
                    : "Ниже условной границы космоса (" + km + " км).");
                wasAboveBoundary = Telemetry.AboveSpaceBoundary;
            }

            var sim = body.Simulation;
            if (sim.ImpactCount != lastImpactCount)
            {
                lastImpactCount = sim.ImpactCount;
                Notify("Контакт с поверхностью: скорость относительно грунта " +
                       UnitFormatter.Number(sim.LastImpact.SurfaceRelativeSpeed, 1) + " м/с (вертикальная " +
                       UnitFormatter.Number(sim.LastImpact.VerticalSpeed, 1) + " м/с).");
            }
            Notifications.RemoveAll(n => Time.unscaledTime - n.RealTime > 12f);
        }

        void Refresh()
        {
            FlightTelemetryCalculator.Compute(body.Simulation, Telemetry);
            classifier.Update(Telemetry, planet.Model);
        }

        public void Notify(string text)
        {
            Notifications.Add(new FlightNotification { Text = text, RealTime = Time.unscaledTime });
            if (Notifications.Count > 6) Notifications.RemoveAt(0);
        }
    }
}
