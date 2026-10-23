using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Builds the Phase 1 vertical slice at runtime: simulation clock, floating origin, planet (physics + visual),
    /// atmosphere, Sun, stars, unpowered test probe on the launch pad, camera, HUD and Flight Manual.
    /// Drop it on an empty GameObject in any scene. Explicit initialisation order — no hidden singletons.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SpaceSimBootstrap : MonoBehaviour
    {
        [Header("Planet")]
        [Tooltip("Planet data asset. If empty, a default Earth-like prototype is created at runtime.")]
        [SerializeField] PlanetDefinition planetDefinition;

        [Header("Start conditions (environment only — planet physics are not altered)")]
        [Tooltip("Places the Sun so the launch site starts at this local solar time [h].")]
        [SerializeField] double startLocalSolarHour = 9.5;

        [Header("Shaders (assigned by SpaceSim ▸ Setup; looked up by name if empty)")]
        [SerializeField] Shader planetSurfaceShader;
        [SerializeField] Shader atmosphereShader;
        [SerializeField] Shader starfieldShader;
        [SerializeField] Shader sunDiskShader;
        [Tooltip("Lit shader for the probe and the launch pad (URP/Lit or Standard).")]
        [SerializeField] Shader litShader;

        public SimulationController Simulation { get; private set; }
        public FloatingOrigin Origin { get; private set; }
        public PlanetRuntime Planet { get; private set; }
        public PlanetVisualController PlanetVisual { get; private set; }
        public FlightBody Body { get; private set; }
        public FlightStateController FlightState { get; private set; }
        public SpaceCameraController CameraController { get; private set; }
        public SolarLightingController Sun { get; private set; }
        public GravityField Gravity { get; private set; }

        void Awake()
        {
            // Every SpaceSim object sets world poses from double precision; the root must be an identity transform.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            ResolveShaders();
            Gravity = new GravityField();

            Simulation = Create<SimulationController>("Simulation");
            Origin = Create<FloatingOrigin>("FloatingOrigin");

            Planet = Create<PlanetRuntime>("Planet");
            Planet.Initialize(planetDefinition, Gravity);

            var surfaceMaterial = new Material(planetSurfaceShader) { name = "PlanetSurface (runtime)" };
            PlanetVisual = Planet.gameObject.AddComponent<PlanetVisualController>();
            PlanetVisual.Initialize(Planet, Origin, Simulation, surfaceMaterial);

            var environment = new GameObject("Environment").transform;
            environment.SetParent(transform, false);
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(environment, false);
            Sun = sunGo.AddComponent<SolarLightingController>();
            Sun.Initialize(Planet, Origin, sunDiskShader);
            Sun.EclipticLongitudeDegrees = SunLongitudeForLocalTime(startLocalSolarHour);

            var atmosphere = new GameObject("Atmosphere").AddComponent<AtmosphereRenderer>();
            atmosphere.transform.SetParent(environment, false);
            atmosphere.Initialize(Planet, Origin, Sun, atmosphereShader);

            var stars = new GameObject("Starfield").AddComponent<StarfieldController>();
            stars.transform.SetParent(environment, false);
            stars.Initialize(Planet, Origin, Sun, starfieldShader);

            var site = new GameObject("LaunchSite").AddComponent<LaunchSite>();
            site.transform.SetParent(transform, false);
            site.Initialize(Planet, Origin, Simulation, litShader);

            Body = Create<FlightBody>("TestProbe");
            Body.Initialize(Planet, Gravity, Simulation, Origin, litShader);

            FlightState = Create<FlightStateController>("FlightState");
            FlightState.Initialize(Body, Planet);

            CameraController = CreateCamera();
            CameraController.Initialize(CameraController.GetComponent<Camera>(), Planet, Body, Origin, Simulation);

            var ui = new GameObject("UI");
            ui.transform.SetParent(transform, false);
            var manual = ui.AddComponent<FlightManualUI>();
            manual.Initialize(Planet);
            var ic = ui.AddComponent<InitialConditionsPanel>();
            ic.Initialize(Body, Planet, FlightState);
            var hud = ui.AddComponent<FlightHUD>();
            hud.Initialize(FlightState, Body, Planet, Simulation, CameraController, stars, manual, ic);
        }

        T Create<T>(string objectName) where T : Component
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
        }

        SpaceCameraController CreateCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("SpaceCamera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            var controller = cam.GetComponent<SpaceCameraController>();
            return controller != null ? controller : cam.gameObject.AddComponent<SpaceCameraController>();
        }

        /// <summary>
        /// Chooses the Sun's ecliptic longitude so the launch site sees the given local solar time at t = 0:
        /// the Sun is placed (12 − hour)·15° east of the site's meridian, then projected into the ecliptic.
        /// </summary>
        double SunLongitudeForLocalTime(double hour)
        {
            var model = Planet.Model;
            double t = Simulation.SimulationTime;
            Vector3d site = model.Rotation.FixedToInertial(Planet.LaunchSiteDirection(), t);
            Vector3d axis = model.Rotation.SpinAxis;
            Vector3d meridian = Vector3d.ProjectOnPlane(site, axis).Normalized;
            if (meridian.SqrMagnitude < 1e-12) return 0.0;
            Vector3d sun = QuaternionD.AngleAxis((12.0 - hour) * 15.0 * MathD.DegToRad, axis).Rotate(meridian);
            return System.Math.Atan2(sun.y, sun.x) * MathD.RadToDeg;
        }

        void ResolveShaders()
        {
            bool urp = GraphicsSettings.currentRenderPipeline != null;
            if (planetSurfaceShader == null) planetSurfaceShader = Shader.Find("SpaceSim/PlanetSurface");
            if (atmosphereShader == null) atmosphereShader = Shader.Find("SpaceSim/Atmosphere");
            if (starfieldShader == null) starfieldShader = Shader.Find("SpaceSim/Starfield");
            if (sunDiskShader == null) sunDiskShader = Shader.Find("SpaceSim/SunDisk");
            if (litShader == null) litShader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            if (litShader == null) litShader = Shader.Find("Standard");
            if (planetSurfaceShader == null || atmosphereShader == null || starfieldShader == null)
                Debug.LogError("[SpaceSim] SpaceSim shaders not found. Run 'SpaceSim ▸ Setup Project' or assign them on the bootstrap.", this);
        }
    }
}
