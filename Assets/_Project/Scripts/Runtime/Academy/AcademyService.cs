using System;
using System.Collections.Generic;
using SpaceSim.Academy;
using SpaceSim.Flight;
using SpaceSim.Planets;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Connects the Flight Academy (Core, <see cref="SpaceSim.Academy"/>) to the running game: builds the lesson context from the
    /// active planet and the player's rocket, and feeds the contextual <see cref="AcademyAdvisor"/> with snapshots of the
    /// authoritative simulation. Computes nothing itself — every number comes from the simulation or the Core functions.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class AcademyService : MonoBehaviour
    {
        const float EvaluateInterval = 0.25f;

        FlightBody body;
        FlightStateController state;
        StarSystemRuntime system;
        SimulationController simulation;
        AcademyContext context;
        List<AcademyTopic> topics;
        List<AcademyCalculator> calculators;
        bool dirty = true;
        float nextEvaluate;
        RocketState trackedRocket;
        readonly AcademySituation situation = new AcademySituation();

        public AcademyAdvisor Advisor { get; } = new AcademyAdvisor();
        /// <summary>Increments whenever lessons and calculators are rebuilt (new planet or new rocket design).</summary>
        public int ContentVersion { get; private set; }
        /// <summary>The last snapshot given to the advisor.</summary>
        public AcademySituation Situation => situation;
        /// <summary>Raised when the advisor reacts to the flight (at most a few times per flight).</summary>
        public event Action<AcademyHint> HintRaised;

        public AcademyContext Context
        {
            get
            {
                EnsureContent();
                return context;
            }
        }

        public IReadOnlyList<AcademyTopic> Topics
        {
            get
            {
                EnsureContent();
                return topics;
            }
        }

        public IReadOnlyList<AcademyCalculator> Calculators
        {
            get
            {
                EnsureContent();
                return calculators;
            }
        }

        public AcademyTopic FindTopic(string id) => AcademyTopics.Find(Topics, id);
        public AcademyCalculator FindCalculator(string id) => AcademyCalculators.Find(Calculators, id);

        public void Initialize(FlightBody flightBody, FlightStateController flightState, StarSystemRuntime starSystem, SimulationController sim)
        {
            Unsubscribe();
            body = flightBody;
            state = flightState;
            system = starSystem;
            simulation = sim;
            if (body != null) body.BlueprintApplied += OnBlueprintApplied;
            if (system != null) system.ActiveBodyChanged += OnActiveBodyChanged;
            dirty = true;
        }

        void OnDestroy() => Unsubscribe();

        void Unsubscribe()
        {
            if (body != null) body.BlueprintApplied -= OnBlueprintApplied;
            if (system != null) system.ActiveBodyChanged -= OnActiveBodyChanged;
        }

        void OnBlueprintApplied(SpaceSim.Vehicles.RocketBlueprint design) => dirty = true;

        void OnActiveBodyChanged(StarSystemRuntime.Body active)
        {
            dirty = true;
            Advisor.ResetFlight();
        }

        void EnsureContent()
        {
            if (!dirty && context != null) return;
            dirty = false;
            context = BuildContext();
            topics = AcademyTopics.Build(context);
            calculators = AcademyCalculators.Build(context);
            ContentVersion++;
        }

        AcademyContext BuildContext()
        {
            AcademyContext c;
            if (system == null || system.Bodies.Count == 0)
                c = AcademyContext.FromHomeSystem(HomeSystemCatalog.HomeWorldId);
            else
            {
                var bodies = new List<AcademyBody>();
                foreach (var b in system.Bodies)
                {
                    PlanetDefinition d = b.Runtime.Definition;
                    PlanetPhysicalProperties physical = b.Runtime.Model != null ? b.Runtime.Model.Properties : d.physical;
                    bodies.Add(AcademyBody.From(d.planetId, d.displayName, string.IsNullOrEmpty(d.parentBodyId) ? null : d.parentBodyId, physical, d.orbit));
                }
                PlanetRuntime active = system.Active;
                PlanetPhysicalProperties activePhysical = active.Model != null ? active.Model.Properties : active.Definition.physical;
                c = AcademyContext.Create(active.Definition.planetId, active.Definition.displayName, activePhysical,
                    active.Definition.launchSite.latitudeDegrees, system.Star, bodies, null);
            }
            // The player's rocket, analysed exactly like the hangar does (same Level A code).
            RocketConfiguration config = body != null && body.Compiled != null ? body.Compiled.Configuration : null;
            if (config != null)
            {
                c.Rocket = config;
                double required = MissionRequirements.LowOrbit(c.Mu, c.Radius, c.LowOrbitAltitude, c.SiteRotationSpeed);
                c.Design = RocketDesignAnalysis.Analyze(config, c.SurfaceGravity, required);
            }
            return c;
        }

        void Update()
        {
            if (body == null || state == null || body.Simulation == null) return;
            RocketState rocket = body.Rocket;
            if (!ReferenceEquals(rocket, trackedRocket))
            {
                // New rocket on the pad (reset or new design): once-per-flight lessons may appear again.
                trackedRocket = rocket;
                Advisor.ResetFlight();
            }
            if (Time.unscaledTime < nextEvaluate) return;
            nextEvaluate = Time.unscaledTime + EvaluateInterval;
            if (simulation != null && simulation.Paused) return;
            EnsureContent();
            situation.Capture(body.Simulation, state.Telemetry, state.Phase, body.IsOnLaunchPad, context);
            foreach (AcademyHint hint in Advisor.Evaluate(situation)) HintRaised?.Invoke(hint);
        }
    }
}
