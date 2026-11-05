using System;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Units;
using SpaceSim.Vehicles;
using SpaceSim.Economy;
using SpaceSim.Planets;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Instrument panel. Shows real SI values from <see cref="FlightTelemetry"/> (rounded for display only).
    /// Derived orbital elements are hidden by default (training mode): the player is expected to compute them
    /// from r, v, v_r and ε using the Flight Manual; press O to reveal them for checking.
    /// </summary>
    [DefaultExecutionOrder(200)]
    public sealed class FlightHUD : MonoBehaviour
    {
        [SerializeField] DisplayUnitSystem units = DisplayUnitSystem.SI;
        [SerializeField] bool showOrbitAnalysis;

        FlightStateController state;
        FlightBody body;
        PlanetRuntime planet;
        SimulationController simulation;
        SpaceCameraController cameraController;
        StarfieldController stars;
        FlightManualUI manual;
        InitialConditionsPanel initialConditions;
        StarSystemRuntime system;
        SolarLightingController sun;
        FloatingOrigin origin;
        Action switchToNextBody;
        bool visible = true;
        bool showMarkers = true;
        bool showMap;
        RocketControls rocketControls;
        HangarUI hangar;
        PlayerSession session;
        ShopUI shop;
        AcademyUI academy;
        AcademyService academyService;
        NavigationUI navigationUi;
        NavigationService navigation;
        SpaceSim.Academy.AcademyHint banner;
        float bannerUntil;
        bool pausedByAcademy;
        Action newRocket;

        /// <summary>What R does (a new rocket at the start planet's pad; set by the bootstrap).</summary>
        public void SetNewRocket(Action action) => newRocket = action;

        public void Initialize(FlightStateController flightState, FlightBody flightBody, PlanetRuntime planetRuntime,
            SimulationController sim, SpaceCameraController cam, StarfieldController starfield,
            FlightManualUI manualUi, InitialConditionsPanel icPanel,
            StarSystemRuntime starSystem, SolarLightingController starLight, FloatingOrigin floatingOrigin, Action nextBody,
            RocketControls controls)
        {
            state = flightState;
            body = flightBody;
            planet = planetRuntime;
            simulation = sim;
            cameraController = cam;
            stars = starfield;
            manual = manualUi;
            initialConditions = icPanel;
            system = starSystem;
            sun = starLight;
            origin = floatingOrigin;
            switchToNextBody = nextBody;
            rocketControls = controls;
        }

        void Update()
        {
            if (state == null) return;
            if (!system.SpaceUnlocked && (state.Telemetry.AboveSpaceBoundary || (session != null && session.Profile.HasMilestone(ResearchMilestones.ReachSpace))))
            {
                system.UnlockSpace();
                if (state.Telemetry.AboveSpaceBoundary)
                    state.Notify("Вы в космосе! Открыта звёздная система: " + FlightInput.KeyLabel(InputKey.Tab) + " — карта системы, " +
                                 FlightInput.KeyLabel(InputKey.N) + " — метки тел, " + FlightInput.KeyLabel(InputKey.J) + " — перелёт к другому телу.");
            }
            CheckMilestones();
            // Hangar and shop are modal: while one is open, camera and flight hotkeys pause (FlightInput.IsTyping).
            FlightInput.ModalWindowOpen = AnyModalOpen;
            if (FlightInput.PauseMenuOpen) return; // the Esc menu owns the keyboard
            if (GUIUtility.keyboardControl == 0)
            {
                if (hangar != null && FlightInput.GetKeyDown(InputKey.B) && (hangar.IsOpen || body.IsOnLaunchPad) && !(shop != null && shop.IsOpen) &&
                    !(academy != null && academy.IsOpen))
                {
                    hangar.Toggle();
                    FlightInput.ModalWindowOpen = AnyModalOpen;
                    return;
                }
                if (shop != null && FlightInput.GetKeyDown(InputKey.K) && !(hangar != null && hangar.IsOpen) && !(academy != null && academy.IsOpen))
                {
                    shop.Toggle();
                    FlightInput.ModalWindowOpen = AnyModalOpen;
                    return;
                }
                if (navigationUi != null && FlightInput.GetKeyDown(InputKey.G) && !AnyModalOpen)
                {
                    navigationUi.Toggle();
                    return;
                }
                if (academy != null && FlightInput.GetKeyDown(InputKey.L) && !(hangar != null && hangar.IsOpen) && !(shop != null && shop.IsOpen))
                {
                    if (!academy.IsOpen && banner != null && Time.unscaledTime < bannerUntil) academy.OpenHint(banner);
                    else academy.Toggle();
                    banner = null;
                    FlightInput.ModalWindowOpen = AnyModalOpen;
                    return;
                }
            }
            if (FlightInput.IsTyping) return;
            if (FlightInput.GetKeyDown(InputKey.H)) visible = !visible;
            if (system.SpaceUnlocked)
            {
                if (FlightInput.GetKeyDown(InputKey.N)) showMarkers = !showMarkers;
                if (FlightInput.GetKeyDown(InputKey.Tab)) showMap = !showMap;
                if (FlightInput.GetKeyDown(InputKey.J))
                {
                    switchToNextBody?.Invoke();
                    return;
                }
            }
            if (FlightInput.GetKeyDown(InputKey.U))
            {
                units = units == DisplayUnitSystem.SI ? DisplayUnitSystem.Kilometric : DisplayUnitSystem.SI;
                session?.SetUnits(units);
            }
            if (FlightInput.GetKeyDown(InputKey.O)) showOrbitAnalysis = !showOrbitAnalysis;
            if (FlightInput.GetKeyDown(InputKey.M) || FlightInput.GetKeyDown(InputKey.F1)) manual.Toggle();
            if (FlightInput.GetKeyDown(InputKey.I)) initialConditions.Toggle();
            if (FlightInput.GetKeyDown(InputKey.R))
            {
                if (newRocket != null) newRocket();
                else
                {
                    body.ResetToLaunchSite();
                    state.Notify("Новая заправленная ракета на стартовой площадке.");
                }
            }
        }

        void OnGUI()
        {
            if (state == null || planet.Model == null || FlightInput.PauseMenuOpen) return; // the Esc menu covers the view
            HudStyle.Begin();
            if (!visible)
            {
                GUI.Label(new Rect(10, 6, 400, 20), HudStyle.Key(InputKey.H) + " — показать приборы", HudStyle.HintRichLeft);
                return;
            }
            Vector2 screen = HudStyle.VirtualScreen;
            FlightTelemetry t = state.Telemetry;

            if (system.SpaceUnlocked && showMarkers) DrawBodyMarkers();

            GUILayout.BeginArea(new Rect(12, 12, 360, screen.y - 80));
            DrawTelemetry(t);
            if (showOrbitAnalysis)
            {
                if (body.Capabilities.Has(ModuleCapability.OrbitPrediction)) DrawOrbit(t);
                else
                {
                    GUILayout.Space(6);
                    GUILayout.BeginVertical(HudStyle.Panel);
                    GUILayout.Label("АНАЛИЗ ОРБИТЫ НЕДОСТУПЕН", HudStyle.Title);
                    GUILayout.Label("Нужен навигационный компьютер НК-1 (добавьте в ангаре, бесплатно). Без него орбиту можно рассчитать самостоятельно по r, v и формулам Flight Manual.", HudStyle.Small);
                    GUILayout.EndVertical();
                }
            }
            GUILayout.EndArea();

            DrawStatus(t, screen);
            DrawProximity(t, screen);
            DrawRocket(t, screen);
            DrawNotifications(screen);
            DrawAcademyBanner(screen);
            DrawManeuver(screen);
            if (system.SpaceUnlocked && showMap) DrawSystemMap(screen);
            if (hintsFor != HintsKey) BuildHints();
            HudStyle.FooterBand(new Rect(0, screen.y - 46, screen.x, 46));
            GUI.Label(new Rect(0, screen.y - 40, screen.x, 20), hintRocket, HudStyle.HintRich);
            GUI.Label(new Rect(0, screen.y - 22, screen.x, 20), hintGeneral, HudStyle.HintRich);
        }

        // Key hint lines follow the player's bindings; rebuilt only when the bindings (or the unlocked features) change.
        string hintRocket, hintGeneral;
        int hintsFor = -1;
        static int bindingsVersion;
        static FlightHUD() => GameSettings.Changed += () => bindingsVersion++;
        int HintsKey => bindingsVersion * 2 + (system.SpaceUnlocked ? 1 : 0);

        void BuildHints()
        {
            hintsFor = HintsKey;
            string K(InputKey k) => HudStyle.Key(k);
            hintRocket = "РАКЕТА:  " + K(InputKey.Return) + " / кнопка — зажигание  ·  " + K(InputKey.W) + "/" + K(InputKey.S) + " — тангаж  ·  " +
                         K(InputKey.A) + "/" + K(InputKey.D) + " — курс  ·  " + K(InputKey.LeftShift) + "/" + K(InputKey.LeftCtrl) + " — тяга  ·  " +
                         K(InputKey.Z) + " — полная  ·  " + K(InputKey.X) + " — отсечка  ·  " + K(InputKey.Space) + " — отделить ступень";
            hintGeneral = HudStyle.KeyText("ПКМ") + " — обзор  ·  " + HudStyle.KeyText("колесо") + "/" + K(InputKey.Equals) + K(InputKey.Minus) + " — зум  ·  " +
                          K(InputKey.C) + " — система отсчёта  ·  " + K(InputKey.F) + " — фокус  ·  " + K(InputKey.Comma) + " " + K(InputKey.Period) +
                          " — время  ·  " + K(InputKey.P) + " — пауза  ·  " + K(InputKey.I) + " — нач. условия  ·  " + K(InputKey.R) + " — новая ракета  ·  " +
                          (system.SpaceUnlocked ? K(InputKey.Tab) + " — карта  ·  " + K(InputKey.N) + " — метки  ·  " + K(InputKey.J) + " — к другому телу  ·  " : "") +
                          K(InputKey.M) + " — Manual  ·  " + K(InputKey.L) + " — Академия  ·  " + K(InputKey.G) + " — навигация  ·  " + K(InputKey.O) +
                          " — орбита  ·  " + K(InputKey.U) + " — единицы  ·  " + K(InputKey.H) + " — скрыть  ·  " + HudStyle.KeyText("Esc") + " — настройки";
        }

        /// <summary>
        /// Esc: closes the window on top — Academy, shop, hangar, navigation, Flight Manual, initial conditions, then the system
        /// map. Returns false when nothing was open (the Esc menu opens then).
        /// </summary>
        public bool CloseTopWindow()
        {
            if (academy != null && academy.IsOpen) academy.Toggle();
            else if (shop != null && shop.IsOpen) shop.Toggle();
            else if (hangar != null && hangar.IsOpen) hangar.Toggle();
            else if (navigationUi != null && navigationUi.IsOpen) navigationUi.Toggle();
            else if (manual != null && manual.IsOpen) manual.Toggle();
            else if (initialConditions != null && initialConditions.IsOpen) initialConditions.Toggle();
            else if (showMap) showMap = false;
            else return false;
            FlightInput.ModalWindowOpen = AnyModalOpen;
            return true;
        }

        /// <summary>Opens or closes the Flight Manual (same as M).</summary>
        public void ToggleManual() => manual?.Toggle();

        /// <summary>Any window or overlay that Esc would close first.</summary>
        public bool AnyWindowOpen => AnyModalOpen || (navigationUi != null && navigationUi.IsOpen) || (manual != null && manual.IsOpen) ||
                                     (initialConditions != null && initialConditions.IsOpen) || showMap;

        void DrawRocket(FlightTelemetry t, Vector2 screen)
        {
            var r = body.Rocket;
            if (r == null) return;
            var area = new Rect(screen.x - 330, screen.y - 420, 310, 330);
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label("РАКЕТА · " + (r.HasStages ? r.CurrentStage.name + " (" + (r.StageIndex + 1) + "/" + r.StageCount + ")" : "только зонд"), HudStyle.Title);
            if (r.HasStages)
            {
                double fuel = r.CurrentStage.propellantMass > 0 ? r.CurrentPropellant / r.CurrentStage.propellantMass : 0.0;
                HudStyle.Row("Двигатель", r.IsBurning ? "РАБОТАЕТ" : (r.CanBurn ? "выключен" : "топливо кончилось"), 130);
                HudStyle.Row("Топливо ступени", UnitFormatter.Number(fuel * 100.0, 1) + " %  (" + UnitFormatter.Number(r.CurrentPropellant / 1000.0, 1) + " т)", 130);
                HudStyle.Row("Тяга (дроссель)", UnitFormatter.Number(r.Throttle * 100.0, 0) + " %", 130);
                double p0 = planet.Model.Atmosphere.Pressure(0.0);
                double pressureRatio = p0 > 0.0 ? t.Pressure / p0 : 0.0;
                HudStyle.Row("Тяга/вес (макс.)", UnitFormatter.Number(r.AvailableThrustToWeight(pressureRatio, t.GravityMagnitude), 2), 130);
                HudStyle.Row("Расход ṁ", UnitFormatter.Number(r.MassFlow, 0) + " кг/с", 130);
            }
            HudStyle.Row("Масса", UnitFormatter.Number(body.Simulation.CurrentMass / 1000.0, 2) + " т", 130);
            HudStyle.Row("Тангаж / курс", UnitFormatter.Number(r.PitchDegrees, 1) + "° / " + UnitFormatter.Number(r.HeadingDegrees, 0) + "°", 130);
            var rec = body.Simulation.Recorder;
            GUILayout.Space(3);
            HudStyle.Row("Δv идеальн. остаток", UnitFormatter.Speed(r.DeltaVRemaining(), DisplayUnitSystem.SI), 130);
            HudStyle.Row("Δv израсходовано", UnitFormatter.Speed(rec.DeltaVExpended, DisplayUnitSystem.SI), 130);
            HudStyle.Row("Потери: гравит.", UnitFormatter.Speed(rec.GravityLoss, DisplayUnitSystem.SI), 130);
            HudStyle.Row("Потери: сопрот.", UnitFormatter.Speed(rec.DragLoss, DisplayUnitSystem.SI), 130);
            HudStyle.Row("Потери: управл.", UnitFormatter.Speed(rec.SteeringLoss, DisplayUnitSystem.SI), 130);
            GUILayout.Label("Идеальный Δv (Циолковский) — без потерь; реальный разгон меньше на сумму потерь.", HudStyle.Small);
            GUILayout.EndVertical();
            GUILayout.EndArea();

            // Big action button in the bottom centre.
            var button = new Rect(screen.x * 0.5f - 170, screen.y - 112, 340, 56); // wide enough for «ВКЛЮЧИТЬ ДВИГАТЕЛЬ»
            bool outside = rocketControls != null && rocketControls.Suspended; // astronaut on foot
            if (body.Destroyed || outside) { }
            else if (r.HasStages && !r.IsBurning && r.CanBurn)
            {
                string label = body.Simulation.IsLanded ? "ЗАПУСК" : "ВКЛЮЧИТЬ ДВИГАТЕЛЬ";
                if (GUI.Button(button, label, HudStyle.Primary)) rocketControls.Ignite();
            }
            else if (r.HasStages && !r.CanBurn)
            {
                if (GUI.Button(button, "ОТДЕЛИТЬ СТУПЕНЬ", HudStyle.Primary)) rocketControls.Separate();
            }

            // Hangar: only on the launch pad, before ignition.
            if (hangar != null && body.IsOnLaunchPad && !r.IsBurning && !AnyModalOpen && !outside && !body.Destroyed)
            {
                if (GUI.Button(new Rect(button.x - 190, button.y + 8, 170, 40), "АНГАР (" + FlightInput.KeyLabel(InputKey.B) + ")", HudStyle.BigButton)) hangar.Toggle();
            }
            // Shop: available anywhere (purchases take effect in the hangar).
            if (shop != null && !AnyModalOpen)
            {
                if (GUI.Button(new Rect(button.xMax + 20, button.y + 8, 190, 40), "МАГАЗИН (" + FlightInput.KeyLabel(InputKey.K) + ")", HudStyle.BigButton)) shop.Toggle();
            }
            // Navigation: right of the shop if there is room before the rocket panel, otherwise above the shop button.
            if (navigationUi != null && !AnyModalOpen)
            {
                var nav = new Rect(button.xMax + 20 + 190 + 12, button.y + 8, 210, 40);
                if (nav.xMax > screen.x - 340) nav = new Rect(button.xMax + 20, button.y - 40, 190, 40);
                if (GUI.Button(nav, "НАВИГАЦИЯ (" + FlightInput.KeyLabel(InputKey.G) + ")", HudStyle.BigButton)) navigationUi.Toggle();
            }
            // Academy: anywhere; left of the hangar slot so it never covers the rocket panel.
            if (academy != null && !AnyModalOpen)
            {
                if (GUI.Button(new Rect(button.x - 190 - 12 - 210, button.y + 8, 210, 40), "АКАДЕМИЯ (" + FlightInput.KeyLabel(InputKey.L) + ")", HudStyle.BigButton)) academy.Toggle();
            }
        }

        /// <summary>
        /// A contextual Academy lesson just raised by the flight: one compact line under the status panel. It never covers the
        /// view centre, fades on its own, and disappears once the condition is gone. L or the button opens the full lesson.
        /// </summary>
        void DrawAcademyBanner(Vector2 screen)
        {
            if (banner == null || academy == null || AnyModalOpen) return;
            bool stillActive = academyService != null && academyService.Advisor.IsActive(banner.Id);
            if (Time.unscaledTime > bannerUntil || (!stillActive && Time.unscaledTime > bannerUntil - 17f))
            {
                banner = null;
                return;
            }
            const float w = 620f, h = 78f;
            var r = new Rect((screen.x - w) * 0.5f, 134f, w, h); // below the status box and its credits line
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            Color c = banner.Severity == SpaceSim.Academy.HintSeverity.Warning ? HudStyle.Warning
                : banner.Severity == SpaceSim.Academy.HintSeverity.Info ? HudStyle.Accent : Color.white;
            var old = GUI.color;
            GUI.color = c;
            GUI.Label(new Rect(r.x + 10, r.y + 6, w - 200, 18), "АКАДЕМИЯ · " + banner.Title, HudStyle.Title);
            GUI.color = old;
            GUI.Label(new Rect(r.x + 10, r.y + 24, w - 200, h - 28), banner.Message, HudStyle.Small);
            if (GUI.Button(new Rect(r.xMax - 180, r.y + 22, 140, 34), "Подробнее (" + FlightInput.KeyLabel(InputKey.L) + ")", HudStyle.Button))
            {
                academy.OpenHint(banner);
                banner = null;
            }
            if (banner != null && GUI.Button(new Rect(r.xMax - 34, r.y + 22, 26, 34), "×", HudStyle.Button)) banner = null;
        }

        bool AnyModalOpen => (hangar != null && hangar.IsOpen) || (shop != null && shop.IsOpen) || (academy != null && academy.IsOpen);

        /// <summary>Connects the navigation window and the flight-plan service (burn guidance, rails warp notices).</summary>
        public void SetNavigation(NavigationUI window, NavigationService service)
        {
            navigationUi = window;
            navigation = service;
        }

        /// <summary>
        /// Burn guidance for a planned manoeuvre and the predicted encounter: a compact panel above the rocket panel. Values come
        /// from <see cref="NavigationService"/> (Core predictor); the player flies the burn.
        /// </summary>
        void DrawManeuver(Vector2 screen)
        {
            if (navigation == null) return;
            NodeGuidance g = navigation.Guidance;
            SpaceSim.Navigation.TrajectoryPrediction p = navigation.Prediction;
            int target = navigation.Plan.Target;
            if (!g.Active && (p == null || target < 0)) return;
            var area = new Rect(screen.x - 330, screen.y - 560, 310, 134);
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(HudStyle.Panel);
            var sys = navigation.System;
            if (g.Active)
            {
                GUILayout.Label("МАНЁВР · Δv " + UnitFormatter.Number(navigation.Plan.Departure.Magnitude, 0) + " м/с", HudStyle.Title);
                if (!g.Started)
                    HudStyle.Row("Зажигание через", g.TimeToIgnition > 0 ? UnitFormatter.Duration(g.TimeToIgnition) : "СЕЙЧАС", 130);
                HudStyle.Row("Осталось Δv", UnitFormatter.Number(g.Remaining, 1) + " м/с", 130);
                HudStyle.Row("Тангаж / курс", UnitFormatter.Number(g.Pitch, 1) + "° / " + UnitFormatter.Number(g.Heading, 1) + "°", 130);
                HudStyle.Row("Работа двигателя", UnitFormatter.Number(g.BurnTime, 0) + " с", 130);
            }
            if (p != null && target >= 0 && sys != null)
            {
                string name = sys.NameOf(target);
                string text = p.Encounter ? "сфера влияния через " + UnitFormatter.Duration(p.EncounterTime - navigation.Now)
                    : "промах " + UnitFormatter.Number(p.ClosestDistance / 1e9, 2) + " млн км";
                HudStyle.Row(name, text, 130);
            }
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>Connects the Flight Academy window and the advisor that raises contextual lessons.</summary>
        public void SetAcademy(AcademyUI academyUi, AcademyService service)
        {
            if (academy != null) academy.OpenChanged -= OnAcademyOpenChanged;
            if (academyService != null) academyService.HintRaised -= OnHintRaised;
            academy = academyUi;
            academyService = service;
            if (academy != null) academy.OpenChanged += OnAcademyOpenChanged;
            if (academyService != null) academyService.HintRaised += OnHintRaised;
        }

        void OnHintRaised(SpaceSim.Academy.AcademyHint hint)
        {
            banner = hint;
            bannerUntil = Time.unscaledTime + 25f;
        }

        /// <summary>Reading a lesson mid-flight should not cost the flight: the simulation pauses while the Academy is open.</summary>
        void OnAcademyOpenChanged(bool isOpen)
        {
            FlightInput.ModalWindowOpen = AnyModalOpen;
            if (isOpen)
            {
                if (!simulation.Paused && !body.Simulation.IsLanded)
                {
                    simulation.SetPaused(true);
                    pausedByAcademy = true;
                    state.Notify("Пауза на время Академии — закройте её (" + FlightInput.KeyLabel(InputKey.L) + " или Esc), чтобы продолжить полёт.");
                }
            }
            else if (pausedByAcademy)
            {
                pausedByAcademy = false;
                simulation.SetPaused(false);
            }
        }

        /// <summary>Connects the hangar window (built with the flight stack).</summary>
        public void SetHangar(HangarUI hangarUi) => hangar = hangarUi;

        /// <summary>Connects the shop / market / research window.</summary>
        public void SetShop(ShopUI shopUi) => shop = shopUi;

        /// <summary>Connects the persistent player session (credits, rewards, saved settings).</summary>
        public void SetSession(PlayerSession playerSession)
        {
            session = playerSession;
            if (session != null) units = session.Profile.settings.units;
        }

        /// <summary>
        /// Research rewards for physical achievements of a real flight (not after the initial-conditions test tool).
        /// "Space" and "escape" count only from the start planet.
        /// </summary>
        void CheckMilestones()
        {
            if (session == null || body.UsedTestTool || body.Simulation.IsLanded) return;
            bool home = system.ActiveIndex == system.IndexOf(homeBodyId);
            FlightTelemetry t = state.Telemetry;
            if (home && t.AboveSpaceBoundary) Reward(ResearchMilestones.ReachSpace);
            if (state.Phase == FlightPhase.OrbitalFlight) Reward(ResearchMilestones.FirstOrbit);
            if (home && state.Phase == FlightPhase.EscapeTrajectory) Reward(ResearchMilestones.EscapeHome);
        }

        void Reward(string id)
        {
            if (session.Profile.HasMilestone(id)) return;
            MilestoneDefinition m = session.Award(id);
            if (m != null)
                state.Notify("ДОСТИЖЕНИЕ: «" + m.Name + "» — +" + m.CreditReward + " кр, +" + UnitFormatter.Number(m.ResearchReward, 0) +
                             " очк. исследований. Новые товары в магазине.");
        }

        string homeBodyId => session != null && session.Profile.visitedBodies.Count > 0 ? session.Profile.visitedBodies[0] : "";

        void OnDestroy()
        {
            FlightInput.ModalWindowOpen = false;
            SetAcademy(null, null);
            // The flight stack is rebuilt on a body switch; never leave the shared simulation paused by a window that is gone.
            if (pausedByAcademy && simulation != null) simulation.SetPaused(false);
            pausedByAcademy = false;
        }

        /// <summary>Top-down map of the system (true scale of orbit radii): the star, orbits and current positions.</summary>
        void DrawSystemMap(Vector2 screen)
        {
            float size = Mathf.Min(screen.x, screen.y) - 200f;
            var rect = new Rect((screen.x - size) * 0.5f, (screen.y - size) * 0.5f, size, size);
            GUI.Box(rect, GUIContent.none, HudStyle.Panel);
            GUI.Label(new Rect(rect.x + 10, rect.y + 6, size - 20, 20), "КАРТА СИСТЕМЫ · " + system.SystemName + " (вид сверху, " + FlightInput.KeyLabel(InputKey.Tab) + " или Esc — закрыть)", HudStyle.Title);
            Vector2 c = rect.center;
            double maxR = 1.0;
            var heliocentric = new Vector3d[system.Bodies.Count];
            for (int i = 0; i < system.Bodies.Count; i++)
            {
                heliocentric[i] = system.Bodies[i].Runtime.Model.Position - system.StarPosition;
                if (system.Bodies[i].ParentIndex < 0) maxR = System.Math.Max(maxR, heliocentric[i].Magnitude);
            }
            float scale = (size * 0.45f) / (float)maxR;
            HudStyle.Dot(c, 10f, HudStyle.Warning);
            GUI.Label(new Rect(c.x + 8, c.y - 9, 200, 18), system.Star.name, HudStyle.Label);
            for (int i = 0; i < system.Bodies.Count; i++)
            {
                var b = system.Bodies[i];
                Vector3d hp = heliocentric[i];
                if (b.ParentIndex < 0)
                {
                    float orbitR = (float)hp.Magnitude * scale;
                    for (int k = 0; k < 96; k++)
                    {
                        float a = k / 96f * Mathf.PI * 2f;
                        HudStyle.Dot(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * orbitR, 2f, new Color(1f, 1f, 1f, 0.25f));
                    }
                }
                var p = c + new Vector2((float)hp.x, -(float)hp.y) * scale;
                if (b.ParentIndex >= 0) p += new Vector2(10f, 10f); // moons: offset so they don't hide the parent
                bool active = i == system.ActiveIndex;
                HudStyle.Dot(p, active ? 9f : 7f, active ? HudStyle.Danger : HudStyle.Accent);
                double au = hp.Magnitude / PhysicalConstants.AstronomicalUnit;
                GUI.Label(new Rect(p.x + 8, p.y - 9, 280, 18), b.Name + (active ? "  ◄ вы здесь" : "") + (b.ParentIndex < 0 ? "  " + UnitFormatter.Number(au, 3) + " а.е." : ""), HudStyle.Label);
            }
        }

        /// <summary>Name and distance labels over the star and every other body (direction-only projection).</summary>
        void DrawBodyMarkers()
        {
            Camera cam = cameraController.Camera;
            if (cam == null) return;
            Vector3d viewer = origin.ViewerPosition;
            Marker(cam, system.StarPosition - viewer, system.Star.radius, system.Star.name, HudStyle.Warning);
            for (int i = 0; i < system.Bodies.Count; i++)
            {
                if (i == system.ActiveIndex) continue;
                var b = system.Bodies[i];
                if (b.Runtime.Model == null) continue;
                Marker(cam, b.Runtime.Model.Position - viewer, b.Runtime.Model.Radius, b.Name, HudStyle.Accent);
            }
        }

        static void Marker(Camera cam, Vector3d relative, double radius, string name, Color color)
        {
            double distance = relative.Magnitude;
            if (distance <= 0.0) return;
            Vector3 dir = CoordinateSystem.ToUnityVector(relative / distance);
            Vector3 screenPoint = cam.WorldToScreenPoint(cam.transform.position + dir * 1000f);
            if (screenPoint.z <= 0f) return; // behind the camera
            float s = HudStyle.Scale;
            var p = new Vector2(screenPoint.x / s, (Screen.height - screenPoint.y) / s);
            string dist = distance - radius > 1.0e9
                ? UnitFormatter.Number((distance - radius) / PhysicalConstants.AstronomicalUnit, 3) + " а.е."
                : UnitFormatter.Number((distance - radius) / 1000.0, 0) + " км";
            var c = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(p.x - 4, p.y - 9, 20, 18), "◇", HudStyle.Label);
            GUI.Label(new Rect(p.x + 10, p.y - 9, 320, 18), name + " · " + dist, HudStyle.Label);
            GUI.color = c;
        }

        void DrawTelemetry(FlightTelemetry t)
        {
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label("ТЕЛЕМЕТРИЯ · " + planet.Definition.displayName.ToUpperInvariant(), HudStyle.Title);
            HudStyle.Row("Время симуляции", UnitFormatter.Duration(t.Time));
            HudStyle.Row("Широта / долгота", UnitFormatter.Angle(t.Latitude) + "  " + UnitFormatter.Angle(t.Longitude));
            GUILayout.Space(4);
            HudStyle.Row("Высота над поверхн.", UnitFormatter.Distance(t.AltitudeAboveSurface, units));
            HudStyle.Row("Высота над R (h)", UnitFormatter.Distance(t.AltitudeAboveReference, units));
            HudStyle.Row("От центра (r)", UnitFormatter.Distance(t.DistanceFromCenter, units));
            GUILayout.Space(4);
            HudStyle.Row("|v| инерциальная", UnitFormatter.Speed(t.InertialSpeed, units));
            HudStyle.Row("|v| отн. поверхн.", UnitFormatter.Speed(t.SurfaceSpeed, units));
            HudStyle.Row("|v| отн. воздуха", UnitFormatter.Speed(t.AirSpeed, units));
            HudStyle.Row("v радиальная (v_r)", UnitFormatter.Speed(t.RadialVelocity, units));
            HudStyle.Row("v горизонт. (инерц.)", UnitFormatter.Speed(t.HorizontalInertialSpeed, units));
            HudStyle.Row("v⃗ отн. пов. E/N/Up",
                UnitFormatter.Number(Conv(t.VelocityEast), Dec) + " / " + UnitFormatter.Number(Conv(t.VelocityNorth), Dec) + " / " +
                UnitFormatter.Number(Conv(t.VelocityUp), Dec) + (units == DisplayUnitSystem.SI ? " m/s" : " km/s"));
            Vector3d v = t.InertialVelocity;
            HudStyle.Row("v⃗ инерц. x/y/z",
                UnitFormatter.Number(Conv(v.x), Dec) + " / " + UnitFormatter.Number(Conv(v.y), Dec) + " / " + UnitFormatter.Number(Conv(v.z), Dec));
            GUILayout.Space(4);
            HudStyle.Row("g(r) = μ/r²", UnitFormatter.Acceleration(t.GravityMagnitude));
            HudStyle.Row("|a| полное", UnitFormatter.Acceleration(t.TotalAcceleration));
            HudStyle.Row("Перегрузка (акселерометр)", UnitFormatter.Number(t.ProperAcceleration / PhysicalConstants.StandardGravity, 3) + " g₀");
            GUILayout.Space(4);
            HudStyle.Row("Плотность ρ", UnitFormatter.Density(t.Density));
            HudStyle.Row("ρ/ρ₀", UnitFormatter.Scientific(t.DensityRatio, 3));
            HudStyle.Row("Давление p", UnitFormatter.Pressure(t.Pressure));
            HudStyle.Row("Скоростной напор q", UnitFormatter.Pressure(t.DynamicPressure));
            HudStyle.Row("Сопротивление a_drag", UnitFormatter.Acceleration(t.DragAcceleration));
            if (body.HeatFlux > 1.0) HudStyle.Row("Нагрев носа (Саттон–Грейвс)", UnitFormatter.Number(body.HeatFlux / 1000.0, 1) + " кВт/м²");
            GUILayout.Space(4);
            HudStyle.Row("ε = v²/2 − μ/r", UnitFormatter.SpecificEnergy(t.SpecificEnergy, units));
            GUILayout.EndVertical();
        }

        void DrawOrbit(FlightTelemetry t)
        {
            var o = t.Orbit;
            double R = planet.Model.Radius;
            GUILayout.Space(6);
            GUILayout.BeginVertical(HudStyle.Panel);
            GUILayout.Label("АНАЛИЗ ОРБИТЫ (двухтельная модель, без атмосферы)", HudStyle.Title);
            HudStyle.Row("Эксцентриситет e", UnitFormatter.Number(o.Eccentricity, 5));
            HudStyle.Row("Большая полуось a", o.IsBound ? UnitFormatter.Distance(o.SemiMajorAxis, units) : "— (ε ≥ 0)");
            HudStyle.Row("Перицентр (высота)", UnitFormatter.Distance(o.PeriapsisRadius - R, units));
            HudStyle.Row("Апоцентр (высота)", o.IsBound ? UnitFormatter.Distance(o.ApoapsisRadius - R, units) : "∞");
            HudStyle.Row("Период T", UnitFormatter.Duration(o.Period));
            HudStyle.Row("Наклонение i", UnitFormatter.Angle(o.Inclination));
            HudStyle.Row("v_круг(r) = √(μ/r)", UnitFormatter.Speed(t.CircularVelocityHere, units));
            HudStyle.Row("v_уход(r) = √(2μ/r)", UnitFormatter.Speed(t.EscapeVelocityHere, units));
            GUILayout.Label("Это проверочные значения. Для обучения рассчитайте их сами по r, v и формулам из Flight Manual.", HudStyle.Small);
            GUILayout.EndVertical();
        }

        void DrawStatus(FlightTelemetry t, Vector2 screen)
        {
            float w = 560f;
            var r = new Rect((screen.x - w) * 0.5f, 10, w, 92);
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            GUI.Label(new Rect(r.x, r.y + 6, w, 26), PhaseName(state.Phase), HudStyle.Phase);
            GUI.Label(new Rect(r.x + 10, r.y + 34, w - 20, 32), ReasonText(state.Reason), HudStyle.Hint);

            string warp = simulation.Paused ? "ПАУЗА" : "×" + UnitFormatter.Number(simulation.TimeWarp, 0);
            if (simulation.OnRails) warp += " (на «рельсах»: точные кеплеровы дуги)";
            else if (simulation.RailsBlocked) warp = "×" + UnitFormatter.Number(simulation.Settings.maxNumericWarp, 0) + " — выше нельзя: " + body.RailsBlockedReason();
            if (simulation.WarpLimited) warp += " (ограничено производительностью)";
            string cam = (cameraController.Focus == SpaceCameraController.FocusTarget.Planet ? "камера: планета" : "камера: аппарат") +
                         (cameraController.Frame == SpaceCameraController.ReferenceFrame.Inertial ? ", инерциальная" : ", местный горизонт");
            GUI.Label(new Rect(r.x + 10, r.y + 64, w - 20, 22),
                "Время: " + warp + "  ·  шаг " + UnitFormatter.Number(simulation.FixedStep, 3) + " s  ·  " + cam +
                "  ·  звёзды " + UnitFormatter.Number(stars.Visibility * 100.0, 0) + "%", HudStyle.Hint);

            // Economy + environment line under the status box.
            var info = new Rect(r.x, r.yMax + 4, w, 22);
            string money = session != null ? "Кредиты: " + UnitFormatter.Number(session.Profile.credits, 0) + " кр  ·  исследования: " +
                                             UnitFormatter.Number(session.Profile.researchPoints, 0) + (session.ReadOnly ? "  ·  ТОЛЬКО ЧТЕНИЕ" : "") : "";
            HazardExposure hz = body.Condition.LastExposure;
            double integrity = body.Condition.Integrity;
            string hazard = "";
            if (!hz.IsSafe || integrity < 1.0)
            {
                hazard = "  ·  ";
                if (hz.RadiationExcess > 0) hazard += "РАДИАЦИЯ " + hz.Radiation + " (защита " + body.DesignCapabilities.RadiationShielding + ")  ";
                if (hz.AcidExcess > 0) hazard += "КИСЛОТА " + hz.Acid + " (защита " + body.DesignCapabilities.AcidResistance + ")  ";
                hazard += "оборудование " + UnitFormatter.Number(integrity * 100.0, 0) + " %" + (body.Condition.InstrumentsOnline ? "" : " — ПРИБОРЫ ОТКЛЮЧЕНЫ");
            }
            if (money.Length + hazard.Length > 0) GUI.Box(info, GUIContent.none, HudStyle.Panel); // readable over a bright sky
            var sim = body.Simulation;
            if (sim.Navigation != null && sim.Primary != sim.FrameBody)
                hazard += "  ·  притягивает: " + sim.Navigation.NameOf(sim.Primary) + " (вне сферы влияния " + sim.Navigation.NameOf(sim.FrameBody) + ")";
            var c = GUI.color;
            if (!hz.IsSafe || integrity < 1.0) GUI.color = body.Condition.InstrumentsOnline ? HudStyle.Warning : HudStyle.Danger;
            GUI.Label(info, money + hazard, HudStyle.Hint);
            GUI.color = c;
        }

        void DrawProximity(FlightTelemetry t, Vector2 screen)
        {
            var r = new Rect(screen.x - 120, 120, 100, 300);
            GUI.Box(r, GUIContent.none, HudStyle.Panel);
            GUI.Label(new Rect(r.x, r.y + 4, r.width, 18), "ДО ПОВЕРХН.", HudStyle.Hint);
            double alt = Math.Max(0.0, t.AltitudeAboveSurface);
            float frac = (float)(Math.Log10(alt + 1.0) / Math.Log10(100001.0)); // 0 m … 100 km, logarithmic
            bool danger = !double.IsNaN(t.TimeToImpactEstimate) && t.TimeToImpactEstimate < 30.0;
            bool caution = !double.IsNaN(t.TimeToImpactEstimate) && t.TimeToImpactEstimate < 120.0;
            Color c = danger ? HudStyle.Danger : (caution ? HudStyle.Warning : HudStyle.Accent);
            HudStyle.Bar(new Rect(r.x + 38, r.y + 26, 24, 200), frac, c);
            GUI.Label(new Rect(r.x, r.y + 230, r.width, 18), UnitFormatter.Distance(alt, DisplayUnitSystem.Kilometric), HudStyle.Hint);
            GUI.Label(new Rect(r.x, r.y + 250, r.width, 18), "до контакта≈", HudStyle.Hint);
            GUI.Label(new Rect(r.x, r.y + 268, r.width, 18), UnitFormatter.Duration(t.TimeToImpactEstimate), HudStyle.Hint);
        }

        void DrawNotifications(Vector2 screen)
        {
            float y = screen.y - 60;
            for (int i = state.Notifications.Count - 1; i >= 0; i--)
            {
                var n = state.Notifications[i];
                float age = Time.unscaledTime - n.RealTime;
                var c = GUI.color;
                GUI.color = new Color(1, 1, 1, Mathf.Clamp01(1.5f - age / 8f));
                GUI.Label(new Rect(screen.x * 0.5f - 400, y, 800, 22), n.Text, HudStyle.Hint);
                GUI.color = c;
                y -= 22;
            }
        }

        double Conv(double metersPerSecond) => units == DisplayUnitSystem.SI ? metersPerSecond : metersPerSecond / 1000.0;
        int Dec => units == DisplayUnitSystem.SI ? 1 : 4;

        public static string PhaseName(FlightPhase p)
        {
            switch (p)
            {
                case FlightPhase.Ground: return "НА ПОВЕРХНОСТИ";
                case FlightPhase.AtmosphericAscent: return "ПОДЪЁМ В АТМОСФЕРЕ";
                case FlightPhase.UpperAtmosphere: return "ВЕРХНИЕ СЛОИ АТМОСФЕРЫ";
                case FlightPhase.Spaceflight: return "КОСМИЧЕСКИЙ ПОЛЁТ (НЕ ОРБИТА)";
                case FlightPhase.OrbitalFlight: return "ОРБИТАЛЬНЫЙ ПОЛЁТ";
                case FlightPhase.EscapeTrajectory: return "ТРАЕКТОРИЯ УХОДА";
                case FlightPhase.AtmosphericDescent: return "СНИЖЕНИЕ В АТМОСФЕРЕ";
                case FlightPhase.Landing: return "ЗАХОД НА ПОСАДКУ";
                default: return p.ToString();
            }
        }

        public static string ReasonText(FlightPhaseReason r)
        {
            switch (r)
            {
                case FlightPhaseReason.RestingOnSurface: return "Аппарат покоится на грунте и вращается вместе с планетой.";
                case FlightPhaseReason.ClimbingInDenseAir: return "Вертикальная скорость > 0, плотный воздух.";
                case FlightPhaseReason.DescendingInDenseAir: return "Вертикальная скорость < 0, плотный воздух.";
                case FlightPhaseReason.ThinAirAboveThreshold: return "ρ/ρ₀ ниже порога верхней атмосферы; сопротивление ещё действует.";
                case FlightPhaseReason.CloseToSurfaceDescending: return "Снижение у поверхности: следите за вертикальной скоростью.";
                case FlightPhaseReason.AboveBoundarySuborbital: return "Суборбитальная траектория.";
                case FlightPhaseReason.AboveBoundaryPeriapsisBelowSurface: return "Выше условной границы, но перицентр под поверхностью — траектория пересекает планету.";
                case FlightPhaseReason.AboveBoundaryPeriapsisInAtmosphere: return "ε < 0, но перицентр в атмосфере — сопротивление разрушит орбиту.";
                case FlightPhaseReason.BoundOrbitPeriapsisClear: return "ε < 0 и перицентр выше плотной атмосферы.";
                case FlightPhaseReason.UnboundEnergy: return "ε ≥ 0 — траектория не замкнута, аппарат уходит от планеты.";
                default: return r.ToString();
            }
        }
    }
}
