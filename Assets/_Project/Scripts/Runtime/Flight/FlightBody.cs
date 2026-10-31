using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Simulation;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The player's vehicle: a multi-stage rocket carrying the probe. Wraps <see cref="FreeFlightSimulation"/>; its
    /// state changes only through physics — gravity, drag and the thrust of the engine the player controls — or the
    /// explicit initial-conditions test tool. No autopilot. The visual model is interpolated between physics steps
    /// and never feeds back into the simulation.
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class FlightBody : MonoBehaviour, ISimulatedBody
    {
        [Header("Payload / probe (SI)")]
        [Tooltip("Probe mass [kg] when flown without a rocket.")]
        [SerializeField] double mass = 1000.0;
        [Tooltip("Height of the reference point above the ground when resting [m].")]
        [SerializeField] double contactRadius = 1.0;
        [SerializeField] DragProperties drag = new DragProperties(true, 1.05, 3.14);

        [Header("Rocket")]
        [SerializeField] bool startWithRocket = true;
        [SerializeField] RocketConfiguration rocket = new RocketConfiguration();

        [Header("Integration")]
        [SerializeField] IntegrationMethod integrator = IntegrationMethod.VelocityVerlet;
        [Tooltip("η in Δt ≤ η·√(r³/μ). Sub-steps are added automatically if the fixed step is too coarse.")]
        [SerializeField] double accuracyFactor = 0.002;

        FreeFlightSimulation sim;
        PlanetRuntime planet;
        SimulationController controller;
        FloatingOrigin origin;
        Transform visual;
        readonly List<GameObject> stageVisuals = new List<GameObject>();
        Transform probeVisual;
        Transform flame;
        int laidOutStage = -1;
        Shader lit;

        public FreeFlightSimulation Simulation => sim;
        public RocketState Rocket => sim?.Rocket;
        public double Mass => mass;
        public DragProperties Drag => drag;
        public PlanetRuntime Planet => planet;

        public void Initialize(PlanetRuntime planetRuntime, GravityField gravity, SimulationController simulation,
            FloatingOrigin floatingOrigin, Shader litShader)
        {
            planet = planetRuntime;
            controller = simulation;
            origin = floatingOrigin;
            lit = litShader;
            sim = new FreeFlightSimulation(planet.Model, gravity);
            ApplyParameters();
            planet.ModelChanged += OnModelChanged;
            controller.Register(this);
            visual = new GameObject("VehicleVisual").transform;
            visual.SetParent(transform, false);
            ResetToLaunchSite();
        }

        void OnModelChanged(SpaceSim.Planets.PlanetModel model) => sim.Planet = model;

        void OnDestroy()
        {
            if (controller != null) controller.Unregister(this);
            if (planet != null) planet.ModelChanged -= OnModelChanged;
        }

        void OnValidate()
        {
            if (sim != null) ApplyParameters();
        }

        void ApplyParameters()
        {
            sim.Mass = System.Math.Max(1e-6, mass);
            sim.ContactRadius = System.Math.Max(0.0, contactRadius);
            sim.Drag = drag;
            sim.Method = integrator;
            sim.AccuracyFactor = System.Math.Max(1e-5, accuracyFactor);
        }

        public void SimulationStep(double time, double dt) => sim.Step(dt);

        /// <summary>Puts a fresh, fully fuelled rocket (or the bare probe) on the launch pad, engine off, vertical.</summary>
        public void ResetToLaunchSite()
        {
            rocket.payloadMass = mass;
            sim.Rocket = startWithRocket ? new RocketState(rocket) : null;
            sim.PlaceOnSurface(planet.LaunchSiteDirection(), controller.SimulationTime);
            RebuildVisual();
        }

        /// <summary>Ignites the current stage at full throttle. Attitude is whatever the player has set (default vertical).</summary>
        public bool Launch()
        {
            var r = sim.Rocket;
            if (r == null || !r.CanBurn) return false;
            r.Throttle = r.Throttle > 0.0 ? r.Throttle : 1.0;
            r.EngineOn = true;
            return true;
        }

        /// <summary>Drops the current (usually empty) stage; the next one must be ignited again.</summary>
        public bool SeparateStage()
        {
            var r = sim.Rocket;
            if (r == null || !r.HasStages) return false;
            bool wasOn = r.EngineOn;
            r.Separate();
            r.EngineOn = wasOn && r.CanBurn;
            return true;
        }

        /// <summary>
        /// Test tool: sets position (lat/lon [deg], altitude above the reference radius [m]) and velocity in the
        /// local East-North-Up frame [m/s]. Nothing is computed for the player — the numbers are used as given.
        /// </summary>
        public void ApplyInitialConditions(double latitudeDeg, double longitudeDeg, double altitude,
            double vEast, double vNorth, double vUp, bool relativeToSurface)
        {
            sim.SetLocalState(latitudeDeg * MathD.DegToRad, longitudeDeg * MathD.DegToRad, altitude,
                vEast, vNorth, vUp, relativeToSurface, controller.SimulationTime);
        }

        /// <summary>Position for rendering: interpolated between the last two physics steps.</summary>
        public Vector3d RenderPosition(double alpha, double renderTime)
        {
            if (sim.IsLanded) return sim.LandedPositionAt(renderTime);
            return Vector3d.Lerp(sim.PreviousState.Position, sim.State.Position, alpha);
        }

        // ---- Visuals -------------------------------------------------------------------------

        void RebuildVisual()
        {
            foreach (Transform child in visual) Destroy(child.gameObject);
            stageVisuals.Clear();
            laidOutStage = -1;
            float radius = (float)rocket.radius;
            var white = MakeMaterial(new Color(0.88f, 0.88f, 0.90f));
            var dark = MakeMaterial(new Color(0.15f, 0.15f, 0.17f));
            var gold = MakeMaterial(new Color(0.85f, 0.65f, 0.25f));
            var panel = MakeMaterial(new Color(0.08f, 0.12f, 0.30f));
            var fire = MakeMaterial(new Color(1.0f, 0.62f, 0.18f));

            if (sim.Rocket != null)
            {
                for (int i = 0; i < rocket.stages.Length; i++)
                {
                    var stage = new GameObject("Stage" + (i + 1)).transform;
                    stage.SetParent(visual, false);
                    float len = (float)rocket.stages[i].length;
                    var body = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Tank", stage, i % 2 == 0 ? white : white);
                    body.transform.localScale = new Vector3(2f * radius, len * 0.5f, 2f * radius);
                    body.transform.localPosition = new Vector3(0f, len * 0.5f, 0f);
                    var ring = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Interstage", stage, dark);
                    ring.transform.localScale = new Vector3(2.02f * radius, 0.6f, 2.02f * radius);
                    ring.transform.localPosition = new Vector3(0f, len - 0.6f, 0f);
                    var nozzle = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Nozzle", stage, dark);
                    nozzle.transform.localScale = new Vector3(1.2f * radius, 1.0f, 1.2f * radius);
                    nozzle.transform.localPosition = new Vector3(0f, -1.0f, 0f);
                    stageVisuals.Add(stage.gameObject);
                }
                flame = ProceduralMeshes.Primitive(PrimitiveType.Sphere, "Flame", visual, fire).transform;
            }

            probeVisual = new GameObject("Probe").transform;
            probeVisual.SetParent(visual, false);
            float r = (float)System.Math.Min(contactRadius, rocket.radius);
            var hull = ProceduralMeshes.Primitive(PrimitiveType.Sphere, "Hull", probeVisual, white);
            hull.transform.localScale = Vector3.one * 2f * r;
            var band = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Band", probeVisual, gold);
            band.transform.localScale = new Vector3(2.05f * r, 0.12f * r, 2.05f * r);
            for (int s = -1; s <= 1; s += 2)
            {
                var p = ProceduralMeshes.Primitive(PrimitiveType.Cube, "Panel", probeVisual, panel);
                p.transform.localScale = new Vector3(2.2f * r, 0.04f * r, 0.9f * r);
                p.transform.localPosition = new Vector3(s * 2.1f * r, 0f, 0f);
            }
        }

        /// <summary>Stacks the remaining stages with the bottom one resting at the reference point's ground level.</summary>
        void LayoutStack()
        {
            var r = sim.Rocket;
            int first = r != null ? r.StageIndex : int.MaxValue;
            if (first == laidOutStage) return;
            laidOutStage = first;
            float y = -(float)contactRadius;
            for (int i = 0; i < stageVisuals.Count; i++)
            {
                bool attached = i >= first;
                stageVisuals[i].SetActive(attached);
                if (!attached) continue;
                stageVisuals[i].transform.localPosition = new Vector3(0f, y, 0f);
                y += (float)rocket.stages[i].length;
            }
            float probeY = first < stageVisuals.Count ? y + (float)contactRadius : 0f;
            probeVisual.localPosition = new Vector3(0f, probeY, 0f);
        }

        Material MakeMaterial(Color c)
        {
            var m = new Material(lit);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        void LateUpdate()
        {
            if (sim == null || origin == null || planet == null || planet.Model == null) return;
            double t = controller.RenderTime;
            Vector3d p = RenderPosition(controller.InterpolationAlpha, t);
            Vector3d up = (p - planet.Model.Position).Normalized;
            // Vehicle axis: vertical on the ground, the commanded attitude in flight.
            Vector3d axis = sim.IsLanded || sim.Rocket == null ? up : sim.ThrustDirection(p);
            Vector3 axisUnity = CoordinateSystem.ToUnityVector(axis);
            Vector3 fwd = Vector3.ProjectOnPlane(CoordinateSystem.ToUnityVector(planet.Model.Rotation.SpinAxis), axisUnity);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.Cross(axisUnity, Vector3.right);
            transform.SetPositionAndRotation(origin.ToUnityPosition(p), Quaternion.LookRotation(fwd.normalized, axisUnity));

            LayoutStack();
            if (flame != null)
            {
                var r = sim.Rocket;
                bool burning = r != null && r.IsBurning;
                flame.gameObject.SetActive(burning);
                if (burning)
                {
                    float len = 6f + 14f * (float)r.Throttle;
                    float w = 1.2f * (float)rocket.radius;
                    flame.localScale = new Vector3(w, len, w);
                    flame.localPosition = new Vector3(0f, -(float)contactRadius - 2f - len * 0.5f, 0f);
                }
            }
        }
    }
}
