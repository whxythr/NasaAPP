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
        /// <summary>The planet the game starts on: home of the launch pad, the market and sample delivery.</summary>
        string startBodyId = SpaceSim.Planets.HomeSystemCatalog.HomeWorldId;
        /// <summary>The player's current rocket design; survives jumps between bodies (persisted by the save system in M3).</summary>
        SpaceSim.Vehicles.RocketBlueprint design = SpaceSim.Vehicles.RocketBlueprint.CreateDefault();

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
        public PlayerSession Session { get; private set; }
        /// <summary>Flight Academy: lessons, calculators and the contextual advisor for the current flight.</summary>
        public AcademyService Academy { get; private set; }
        /// <summary>Flight plan, trajectory prediction and burn guidance (survives frame handoffs).</summary>
        public NavigationService Navigation { get; private set; }
        /// <summary>Surface exploration of the current flight stack (sample sites, arm, astronaut on foot).</summary>
        public SurfaceExplorer Explorer { get; private set; }
        /// <summary>The in-game Esc menu (settings, main menu, quit).</summary>
        public SpaceSim.Runtime.Menu.GameMenu EscMenu { get; private set; }
        /// <summary>Applies the player's brightness setting to the camera.</summary>
        public ScreenBrightness Brightness { get; private set; }
        /// <summary>HUD of the current flight stack.</summary>
        public FlightHUD Hud { get; private set; }

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
            startBodyId = def.startBodyId;
            System = Create<StarSystemRuntime>("StarSystem");
            System.Initialize(def, Simulation, Origin, planetSurfaceShader, atmosphereShader);
            int start = System.IndexOf(def.startBodyId);

            // Persistent player profile: currency, purchases, rocket design, samples, progress.
            Session = Create<PlayerSession>("PlayerSession");
            Session.Initialize(def.startBodyId);
            design = Session.Profile.blueprint.Clone();
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
            // Player brightness (Settings): post-exposure on this camera, off at 100 %.
            Brightness = viewCamera.GetComponent<ScreenBrightness>();
            if (Brightness == null) Brightness = viewCamera.gameObject.AddComponent<ScreenBrightness>();
            Brightness.Initialize(viewCamera);

            Navigation = Create<NavigationService>("Navigation");
            Navigation.Initialize(this);

            // The Esc menu lives outside the flight stack (it survives body switches); the HUD of each stack plugs into it.
            EscMenu = Create<SpaceSim.Runtime.Menu.GameMenu>("GameMenu");
            EscMenu.Initialize(Simulation);

            BuildFlightStack();
        }

        void Update()
        {
            // Sphere-of-influence handoff: the vehicle entered another body's SOI — continue the same flight in that body's frame.
            if (Body != null && Body.NeedsHandoff(out int index)) HandoffTo(index);
        }

        /// <summary>
        /// Moves the simulation frame to body <paramref name="index"/> without touching the flight: same rocket, propellant and
        /// trajectory, re-expressed relative to the new centre. A real arrival counts as a visit (not after the test tools).
        /// </summary>
        void HandoffTo(int index)
        {
            FlightBody.VehicleCarry carry = Body.CarryTo(index);
            string from = System.ActiveBody.Name;
            Simulation.Unregister(Body);
            if (flightStack != null) Destroy(flightStack);
            System.SetActive(index, Gravity);
            BuildFlightStack(carry);
            // An encounter can be over in a fraction of a second at ×10⁶: stop the warp at every sphere change, the player decides.
            bool warped = Simulation.TimeWarp > 1.0;
            Simulation.ResetWarp();
            if (warped) FlightState.Notify("Ускорение времени сброшено: вход в новую сферу влияния.");
            string name = System.ActiveBody.Name;
            FlightState.Notify("Сфера влияния: " + name + " (из " + from + "). Теперь притягивает " + name + "; система отсчёта — центр " + name + ".");
            if (!carry.UsedTestTool && Session != null)
            {
                bool first = !Session.Profile.visitedBodies.Contains(System.ActiveBody.Id);
                Session.MarkVisited(System.ActiveBody.Id);
                if (first) FlightState.Notify("Открытие: " + name + " — первое посещение.");
                if (!Session.Profile.HasMilestone(SpaceSim.Economy.ResearchMilestones.Interplanetary))
                {
                    var m = Session.Award(SpaceSim.Economy.ResearchMilestones.Interplanetary);
                    if (m != null) FlightState.Notify("ДОСТИЖЕНИЕ: «" + m.Name + "» — +" + m.CreditReward + " кр.");
                }
            }
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

        void BuildFlightStack(FlightBody.VehicleCarry carry = null)
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
            Body.Initialize(planet, Gravity, Simulation, Origin, litShader, design, Session.Owns, System.Navigation, System.ActiveIndex, carry);
            Body.BlueprintApplied += applied =>
            {
                design = applied;
                Session.SetBlueprint(applied);
            };

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
            var hangar = ui.AddComponent<HangarUI>();
            hangar.Initialize(Body, FlightState);
            hud.SetHangar(hangar);
            hud.SetSession(Session);
            var shop = ui.AddComponent<ShopUI>();
            shop.Initialize(Session, FlightState, System);
            hud.SetShop(shop);
            Academy = ui.AddComponent<AcademyService>();
            Academy.Initialize(Body, FlightState, System, Simulation);
            var academyUi = ui.AddComponent<AcademyUI>();
            academyUi.Initialize(Academy, Session, FlightState);
            hud.SetAcademy(academyUi, Academy);
            var navigationUi = ui.AddComponent<NavigationUI>();
            navigationUi.Initialize(Navigation, Session, FlightState, System);
            hud.SetNavigation(navigationUi, Navigation);
            hud.SetNewRocket(NewRocketAtHome);
            Hud = hud;
            if (EscMenu != null) EscMenu.SetWindowCloser(hud.CloseTopWindow);
            Explorer = ui.AddComponent<SurfaceExplorer>();
            Explorer.Initialize(Body, FlightState, planet, CameraController, controls, Session, Simulation, Origin, litShader);
            Body.Touchdown += OnTouchdown;
        }

        /// <summary>
        /// Consequences of a touchdown: the verdict, delivery of the cargo on the start planet, the cargo lost in a crash,
        /// and the research milestone for the first landing on another body (real flights only).
        /// </summary>
        void OnTouchdown(SpaceSim.Physics.LandingOutcome outcome)
        {
            bool home = System.ActiveBody.Id == startBodyId;
            if (Body.IsOnLaunchPad && home && outcome.Result == SpaceSim.Physics.LandingResult.Soft && outcome.VerticalSpeed < 0.5) return; // settling on the pad
            FlightState.Notify(outcome.Reason + (outcome.Result == SpaceSim.Physics.LandingResult.Crash ? " " + FlightInput.KeyLabel(InputKey.R) + " — новая ракета на старте." : ""));
            if (outcome.Result == SpaceSim.Physics.LandingResult.Crash)
            {
                int lost = Session.LoseCargo();
                if (lost > 0) FlightState.Notify("Потеряно образцов: " + lost + ".");
                return;
            }
            if (home)
            {
                int delivered = Session.DeliverCargo();
                if (delivered > 0) FlightState.Notify("Доставлено образцов: " + delivered + " — их можно продать на рынке (" + FlightInput.KeyLabel(InputKey.K) + ").");
            }
            else if (!Body.UsedTestTool && !Session.Profile.HasMilestone(SpaceSim.Economy.ResearchMilestones.OtherworldLanding))
            {
                var m = Session.Award(SpaceSim.Economy.ResearchMilestones.OtherworldLanding);
                if (m != null) FlightState.Notify("ДОСТИЖЕНИЕ: «" + m.Name + "» — +" + m.CreditReward + " кр.");
            }
        }

        /// <summary>R: a fresh rocket on the start planet's pad. Whatever was on board is lost.</summary>
        public void NewRocketAtHome()
        {
            int lost = Session != null ? Session.LoseCargo() : 0;
            int home = System.IndexOf(startBodyId);
            if (home >= 0 && home != System.ActiveIndex) SwitchToBody(home);
            else
            {
                Explorer?.EndEva();
                Body.ResetToLaunchSite();
                FlightState.Notify("Новая заправленная ракета на стартовой площадке.");
            }
            if (lost > 0) FlightState.Notify("Образцы на борту старой ракеты потеряны: " + lost + ".");
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
