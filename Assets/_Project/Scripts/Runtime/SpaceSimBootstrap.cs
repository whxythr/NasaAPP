using SpaceSim.Physics;
using UnityEngine;
using UnityEngine.Rendering;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Builds the game world at runtime: simulation clock, floating origin, the whole star system (every planet and
    /// moon with physics, procedural surface and atmosphere, moving on its orbit), the star, the star field, and a
    /// "flight stack" on the active body (unpowered test probe on the launch pad, camera, HUD, Flight Manual).
    /// Jumping to another body (key J) rebuilds only the flight stack. Drop on an empty GameObject in any scene.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class SpaceSimBootstrap : MonoBehaviour
    {
        [Header("Star system")]
        [Tooltip("System asset (SpaceSim ▸ Create Home System Assets). If empty, the built-in home system is used: " +
                 "Teonan, Quoris, Hermeous Mora and its moon.")]
        [SerializeField] StarSystemDefinition starSystem;

        [Header("Shaders (assigned by SpaceSim ▸ Setup; looked up by name if empty)")]
        [SerializeField] Shader planetSurfaceShader;
        [SerializeField] Shader atmosphereShader;
        [SerializeField] Shader starfieldShader;
        [SerializeField] Shader sunDiskShader;
        [Tooltip("Lit shader for the probe and the launch pad (URP/Lit or Standard).")]
        [SerializeField] Shader litShader;

        Camera viewCamera;
        StarfieldController stars;
        GameObject flightStack;

        public SimulationController Simulation { get; private set; }
        public FloatingOrigin Origin { get; private set; }
        public StarSystemRuntime System { get; private set; }
        public PlanetRuntime Planet => System.Active;
        public PlanetVisualController PlanetVisual => System.ActiveBody.Visual;
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

            StarSystemDefinition def = starSystem != null ? starSystem : StarSystemDefinition.CreateHomeSystem();
            System = Create<StarSystemRuntime>("StarSystem");
            System.Initialize(def, Simulation, Origin, planetSurfaceShader, atmosphereShader);
            int start = System.IndexOf(def.startBodyId);
            System.SetActive(start >= 0 ? start : 0, Gravity);

            var environment = new GameObject("Environment").transform;
            environment.SetParent(transform, false);
            var sunGo = new GameObject("StarLight");
            sunGo.transform.SetParent(environment, false);
            Sun = sunGo.AddComponent<SolarLightingController>();
            Sun.Initialize(System, Origin, sunDiskShader);

            stars = new GameObject("Starfield").AddComponent<StarfieldController>();
            stars.transform.SetParent(environment, false);
            stars.Initialize(System, Origin, Sun, starfieldShader);

            viewCamera = Camera.main;
            if (viewCamera == null)
            {
                var go = new GameObject("SpaceCamera") { tag = "MainCamera" };
                viewCamera = go.AddComponent<Camera>();
            }
            CameraController = viewCamera.GetComponent<SpaceCameraController>();
            if (CameraController == null) CameraController = viewCamera.gameObject.AddComponent<SpaceCameraController>();
            System.SetCamera(viewCamera);

            BuildFlightStack();
        }

        /// <summary>Moves the probe to another body's launch pad (that body becomes the simulation frame's centre).</summary>
        public void SwitchToBody(int index)
        {
            if (Body != null) Simulation.Unregister(Body); // stop stepping the old probe immediately
            if (flightStack != null) Destroy(flightStack);
            System.SetActive(index, Gravity);
            BuildFlightStack();
            FlightState.Notify("Активное тело: " + System.ActiveBody.Name + ". Зонд на стартовой площадке.");
        }

        public void SwitchToNextBody() => SwitchToBody((System.ActiveIndex + 1) % System.Bodies.Count);

        void BuildFlightStack()
        {
            PlanetRuntime planet = System.Active;
            flightStack = new GameObject("FlightStack · " + planet.Definition.displayName);
            flightStack.transform.SetParent(transform, false);

            var site = new GameObject("LaunchSite").AddComponent<LaunchSite>();
            site.transform.SetParent(flightStack.transform, false);
            site.Initialize(planet, Origin, Simulation, litShader);

            var bodyGo = new GameObject("TestProbe");
            bodyGo.transform.SetParent(flightStack.transform, false);
            Body = bodyGo.AddComponent<FlightBody>();
            Body.Initialize(planet, Gravity, Simulation, Origin, litShader);

            var stateGo = new GameObject("FlightState");
            stateGo.transform.SetParent(flightStack.transform, false);
            FlightState = stateGo.AddComponent<FlightStateController>();
            FlightState.Initialize(Body, planet);

            CameraController.Initialize(viewCamera, planet, Body, Origin, Simulation);

            var ui = new GameObject("UI");
            ui.transform.SetParent(flightStack.transform, false);
            var manual = ui.AddComponent<FlightManualUI>();
            manual.Initialize(planet);
            var ic = ui.AddComponent<InitialConditionsPanel>();
            ic.Initialize(Body, planet, FlightState);
            var hud = ui.AddComponent<FlightHUD>();
            var controls = ui.AddComponent<RocketControls>();
            controls.Initialize(Body, FlightState);
            hud.Initialize(FlightState, Body, planet, Simulation, CameraController, stars, manual, ic, System, Sun, Origin, SwitchToNextBody, controls);
        }

        T Create<T>(string objectName) where T : Component
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            return go.AddComponent<T>();
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
