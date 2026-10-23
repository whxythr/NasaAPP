using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Simulation;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Unpowered test body (no engine, no thrust — Phase 1). Wraps <see cref="FreeFlightSimulation"/>; its state
    /// is changed only by physics or by the explicit initial-conditions tool. The visual model is interpolated
    /// between physics steps and never feeds back into the simulation.
    /// </summary>
    [DefaultExecutionOrder(105)]
    public sealed class FlightBody : MonoBehaviour, ISimulatedBody
    {
        [Header("Body (SI)")]
        [Tooltip("Mass [kg].")]
        [SerializeField] double mass = 500.0;
        [Tooltip("Height of the reference point above the ground when resting [m] (≈ probe radius).")]
        [SerializeField] double contactRadius = 1.0;
        [SerializeField] DragProperties drag = new DragProperties(true, 1.05, 3.14);

        [Header("Integration")]
        [SerializeField] IntegrationMethod integrator = IntegrationMethod.VelocityVerlet;
        [Tooltip("η in Δt ≤ η·√(r³/μ). Sub-steps are added automatically if the fixed step is too coarse.")]
        [SerializeField] double accuracyFactor = 0.002;

        FreeFlightSimulation sim;
        PlanetRuntime planet;
        SimulationController controller;
        FloatingOrigin origin;
        Transform visual;

        public FreeFlightSimulation Simulation => sim;
        public double Mass => mass;
        public DragProperties Drag => drag;

        public void Initialize(PlanetRuntime planetRuntime, GravityField gravity, SimulationController simulation,
            FloatingOrigin floatingOrigin, Shader litShader)
        {
            planet = planetRuntime;
            controller = simulation;
            origin = floatingOrigin;
            sim = new FreeFlightSimulation(planet.Model, gravity);
            ApplyParameters();
            planet.ModelChanged += m => sim.Planet = m;
            controller.Register(this);
            BuildVisual(litShader);
            ResetToLaunchSite();
        }

        void OnDestroy()
        {
            if (controller != null) controller.Unregister(this);
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

        /// <summary>Puts the body at rest on the launch pad.</summary>
        public void ResetToLaunchSite() => sim.PlaceOnSurface(planet.LaunchSiteDirection(), controller.SimulationTime);

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

        void BuildVisual(Shader litShader)
        {
            var root = new GameObject("ProbeVisual").transform;
            root.SetParent(transform, false);
            visual = root;
            var body = MakeMaterial(litShader, new Color(0.82f, 0.82f, 0.85f));
            var gold = MakeMaterial(litShader, new Color(0.85f, 0.65f, 0.25f));
            var panel = MakeMaterial(litShader, new Color(0.08f, 0.12f, 0.30f));

            float r = (float)contactRadius;
            var sphere = ProceduralMeshes.Primitive(PrimitiveType.Sphere, "Hull", root, body);
            sphere.transform.localScale = Vector3.one * 2f * r;
            var band = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Band", root, gold);
            band.transform.localScale = new Vector3(2.05f * r, 0.12f * r, 2.05f * r);
            var antenna = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Antenna", root, body);
            antenna.transform.localScale = new Vector3(0.05f * r, 0.9f * r, 0.05f * r);
            antenna.transform.localPosition = new Vector3(0f, 1.6f * r, 0f);
            for (int s = -1; s <= 1; s += 2)
            {
                var p = ProceduralMeshes.Primitive(PrimitiveType.Cube, "Panel", root, panel);
                p.transform.localScale = new Vector3(2.2f * r, 0.04f * r, 0.9f * r);
                p.transform.localPosition = new Vector3(s * 2.1f * r, 0f, 0f);
            }
        }

        static Material MakeMaterial(Shader shader, Color c)
        {
            var m = new Material(shader);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        void LateUpdate()
        {
            if (sim == null || origin == null) return;
            double t = controller.RenderTime;
            Vector3d p = RenderPosition(controller.InterpolationAlpha, t);
            Vector3d up = (p - planet.Model.Position).Normalized;
            Vector3 upUnity = CoordinateSystem.ToUnityVector(up);
            Vector3 fwd = Vector3.ProjectOnPlane(CoordinateSystem.ToUnityVector(planet.Model.Rotation.SpinAxis), upUnity);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.Cross(upUnity, Vector3.right);
            transform.SetPositionAndRotation(origin.ToUnityPosition(p), Quaternion.LookRotation(fwd.normalized, upUnity));
        }
    }
}
