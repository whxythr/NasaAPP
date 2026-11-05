using System;
using System.Collections.Generic;
using SpaceSim.Economy;
using SpaceSim.Exploration;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using SpaceSim.Units;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Surface exploration after landing: the sample sites around the lander, the lander's collector arm, and the astronaut
    /// on foot (first-person, <see cref="SurfaceWalker"/> physics with the planet's own gravity). E — go out / take the
    /// nearest sample / come back in; Shift — sprint (speed and endurance from <see cref="Locomotion"/>); V — look from
    /// behind. The suited body (<see cref="AstronautModel"/>) is drawn and casts shadows; in first person the camera sits in
    /// the helmet and moves with the body's lean and stride. All rules come from Core (<see cref="SampleCollection"/>,
    /// <see cref="SampleSiteGenerator"/>, <see cref="Locomotion"/>).
    /// </summary>
    [DefaultExecutionOrder(110)]
    public sealed class SurfaceExplorer : MonoBehaviour, SpaceCameraController.IFirstPersonView
    {
        const float MouseSensitivity = 0.15f;
        const double BoardingDistance = 8.0;
        /// <summary>Distance of the camera behind the astronaut in the third-person view [m].</summary>
        const double ThirdPersonDistance = 3.4;
        /// <summary>Extra field of view at full sprint [degrees].</summary>
        const float SprintFovKick = 5f;

        FlightBody body;
        FlightStateController state;
        PlanetRuntime planet;
        SpaceCameraController cameraController;
        RocketControls controls;
        PlayerSession session;
        SimulationController simulation;
        FloatingOrigin origin;
        Shader lit;

        SurfaceWalker walker;
        List<SampleSite> sites;
        readonly Dictionary<string, Transform> siteVisuals = new Dictionary<string, Transform>();
        Transform siteRoot;
        Vector3d sitesAnchor;
        float yaw, lookPitch;
        GUIStyle note;

        AstronautModel model;
        HelmetVisorOverlay visor;
        bool thirdPerson;
        Vector3 eyeLocal = new Vector3(0f, (float)SurfaceWalker.EyeHeight, 0f);
        float baseFov = -1f, fovKick;

        public bool Eva => walker != null;
        public float Yaw => yaw;
        public float LookPitch => lookPitch;
        /// <summary>The suited body while outside (null inside the rocket).</summary>
        public AstronautModel Model => model;
        public bool ThirdPerson => thirdPerson;

        // Scripted input for tests and the development bridge (the player uses the keyboard).
        double driveForward, driveRight;
        float driveUntil;
        bool driveRun, driveJump;

        public void Drive(double forward, double right, float seconds, bool run)
        {
            driveForward = forward;
            driveRight = right;
            driveRun = run;
            driveUntil = Time.unscaledTime + seconds;
        }

        public void Jump() => driveJump = true;
        public void Face(float yawDegrees) => yaw = Mathf.Repeat(yawDegrees, 360f);
        /// <summary>Head pitch [degrees, + up], as the right mouse button would set it.</summary>
        public void Look(float pitchDegrees) => lookPitch = Mathf.Clamp(pitchDegrees, -85f, 85f);

        /// <summary>V: first person ↔ a view from behind the astronaut.</summary>
        public void ToggleThirdPerson()
        {
            thirdPerson = !thirdPerson;
            if (model != null) model.SetFirstPerson(!thirdPerson);
        }
        public SurfaceWalker Walker => walker;
        public IReadOnlyList<SampleSite> Sites => sites;
        /// <summary>Nearest uncollected site and its distance [m] from the astronaut (EVA) or the lander.</summary>
        public SampleSite Nearest { get; private set; }
        public double NearestDistance { get; private set; } = double.PositiveInfinity;

        public void Initialize(FlightBody flightBody, FlightStateController flightState, PlanetRuntime planetRuntime, SpaceCameraController cam,
            RocketControls rocketControls, PlayerSession playerSession, SimulationController sim, FloatingOrigin floatingOrigin, Shader litShader)
        {
            body = flightBody;
            state = flightState;
            planet = planetRuntime;
            cameraController = cam;
            controls = rocketControls;
            session = playerSession;
            simulation = sim;
            origin = floatingOrigin;
            lit = litShader;
            visor = gameObject.AddComponent<HelmetVisorOverlay>();
        }

        void OnDestroy()
        {
            if (cameraController != null && ReferenceEquals(cameraController.FirstPerson, this)) cameraController.FirstPerson = null;
            if (controls != null) controls.Suspended = false;
            RemoveBody();
        }

        void RemoveBody()
        {
            if (model != null) model.Destroy();
            model = null;
            if (visor != null) visor.Visible = false;
            if (baseFov > 0f && cameraController != null && cameraController.Camera != null) cameraController.Camera.fieldOfView = baseFov;
            baseFov = -1f;
            fovKick = 0f;
        }

        /// <summary>The ground the astronaut walks on: the planet surface, raised to the launch pad's deck over the pad (ramp on the apron).</summary>
        sealed class PadSurface : ISurfaceHeightProvider
        {
            readonly ISurfaceHeightProvider ground;
            readonly Vector3d pad;
            readonly double radius, padRadius, deck;

            public PadSurface(ISurfaceHeightProvider inner, Vector3d padDirection, double planetRadius, double deckRadius)
            {
                ground = inner;
                pad = padDirection.Normalized;
                radius = planetRadius;
                padRadius = deckRadius;
                deck = inner.SurfaceHeight(pad) + LaunchSite.DeckHeight;
            }

            public double SurfaceHeight(Vector3d d)
            {
                double h = ground.SurfaceHeight(d);
                double distance = (d - pad).Magnitude * radius;
                if (distance <= padRadius) return Math.Max(h, deck);
                if (distance <= padRadius + 6.0) return Math.Max(h, deck - (distance - padRadius) / 6.0 * (LaunchSite.DeckHeight + 1.5));
                return h;
            }

            public double MinSurfaceHeight => ground.MinSurfaceHeight;
            public double MaxSurfaceHeight => ground.MaxSurfaceHeight + LaunchSite.DeckHeight;
        }

        ISurfaceHeightProvider WalkSurface() =>
            new PadSurface(planet.Model.Surface, planet.LaunchSiteDirection(), planet.Model.Radius, planet.Definition.launchSite.padRadius);

        Func<Vector3d, bool> WaterTest()
        {
            if (planet.Model.Surface is PlanetSurfaceSampler sampler) return d => sampler.Sample(d).IsWater;
            return null;
        }

        // ---- Sites -------------------------------------------------------------------------------------------

        void RefreshSites()
        {
            var sim = body.Simulation;
            bool landed = sim != null && sim.IsLanded && !body.Destroyed;
            if (!landed && !Eva)
            {
                if (sites != null) ClearSites();
                return;
            }
            Vector3d dir = sim.LandedFixedPosition.Normalized;
            if (sites != null && (dir - sitesAnchor).Magnitude * planet.Model.Radius < 50.0) return;
            ClearSites();
            sitesAnchor = dir;
            sites = SampleSiteGenerator.Around(planet.Definition.planetId, planet.Model.Radius, dir, WaterTest());
            siteRoot = new GameObject("SampleSites").transform;
            siteRoot.SetParent(transform, false);
            foreach (SampleSite s in sites)
            {
                if (session != null && session.Profile.collectedSites.Contains(s.Id)) continue;
                var go = ProceduralMeshes.Primitive(s.IsLiquid ? PrimitiveType.Cylinder : PrimitiveType.Sphere, s.Name, siteRoot, Material(ColorOf(s)));
                float size = (float)(0.35 + 0.15 * Math.Min(3.0, s.Mass));
                go.transform.localScale = s.IsLiquid ? new Vector3(size * 2f, 0.04f, size * 2f) : Vector3.one * size;
                siteVisuals[s.Id] = go.transform;
            }
        }

        void ClearSites()
        {
            if (siteRoot != null) Destroy(siteRoot.gameObject);
            siteRoot = null;
            siteVisuals.Clear();
            sites = null;
            Nearest = null;
        }

        static Color ColorOf(SampleSite s)
        {
            switch (s.Category)
            {
                case SampleCategory.Rock: return new Color(0.45f, 0.40f, 0.36f);
                case SampleCategory.Mineral: return new Color(0.75f, 0.72f, 0.65f);
                case SampleCategory.RareGeology: return new Color(0.55f, 0.85f, 1.0f);
                case SampleCategory.Soil: return new Color(0.70f, 0.58f, 0.40f);
                case SampleCategory.MineralSoil: return new Color(0.62f, 0.32f, 0.20f);
                case SampleCategory.UnusualCompound: return new Color(0.30f, 0.75f, 0.45f);
                case SampleCategory.Water: return new Color(0.55f, 0.35f, 0.85f);
                case SampleCategory.OtherLiquid: return new Color(0.95f, 0.75f, 0.20f);
                default: return new Color(0.80f, 0.95f, 0.25f);
            }
        }

        Material Material(Color c) => ProceduralMeshes.LitMaterial(lit, c, 0.2f);

        Vector3d SitePoint(SampleSite s) => s.FixedDirection * (planet.Model.Radius + planet.Model.Surface.SurfaceHeight(s.FixedDirection));

        void FindNearest(Vector3d from)
        {
            Nearest = null;
            NearestDistance = double.PositiveInfinity;
            if (sites == null) return;
            foreach (SampleSite s in sites)
            {
                if (session != null && session.Profile.collectedSites.Contains(s.Id)) continue;
                double d = (SitePoint(s) - from).Magnitude;
                if (d < NearestDistance)
                {
                    NearestDistance = d;
                    Nearest = s;
                }
            }
        }

        // ---- EVA ---------------------------------------------------------------------------------------------

        /// <summary>Goes outside if a suit survives here; returns the reason when not.</summary>
        public string TryStartEva()
        {
            var sim = body.Simulation;
            if (sim == null || !sim.IsLanded || body.Destroyed) return "Выйти можно только из стоящей на грунте ракеты.";
            CollectionCheck exit = SampleCollection.CanExit(planet.Model.Properties, planet.Definition.hazards);
            if (!exit.Ok) return exit.Reason;
            Vector3d landed = sim.LandedFixedPosition;
            walker = new SurfaceWalker(planet.Model.GravitationalParameter, planet.Model.Radius, WalkSurface(), landed, WaterTest());
            PlanetRotation.LocalBasis(landed.Normalized, out Vector3d east, out _);
            walker.PlaceAt(landed + east * 5.0);
            yaw = 270f; // face the rocket (west)
            lookPitch = 0f;
            controls.Suspended = true;
            cameraController.FirstPerson = this;
            simulation.ResetWarp();
            RemoveBody();
            if (lit != null)
            {
                model = new AstronautModel(null, lit);
                model.SetFirstPerson(!thirdPerson);
            }
            eyeLocal = model != null ? model.EyeLocal : new Vector3(0f, (float)SurfaceWalker.EyeHeight, 0f);
            if (cameraController.Camera != null) baseFov = cameraController.Camera.fieldOfView;
            double g = walker.Gravity;
            string sprint = walker.CanSprint
                ? "спринт " + UnitFormatter.Number(walker.SprintSpeed, 2) + " м/с" +
                  (double.IsPositiveInfinity(Locomotion.SprintEndurance(g)) ? " без усталости" : " на ≈ " + UnitFormatter.Number(Locomotion.SprintEndurance(g), 0) + " с")
                : "бег невозможен — слишком тяжело";
            state.Notify("Выход на поверхность: g = " + UnitFormatter.Number(g, 2) + " м/с², шаг " + UnitFormatter.Number(walker.WalkSpeed, 2) +
                         " м/с, " + sprint + ", прыжок ≈ " + UnitFormatter.Number(walker.JumpHeight, 2) + " м. E — образец/вернуться, V — вид со стороны.");
            return "";
        }

        public void EndEva()
        {
            if (!Eva) return;
            walker = null;
            RemoveBody();
            controls.Suspended = false;
            cameraController.FirstPerson = null;
            state.Notify("Астронавт в ракете.");
        }

        /// <summary>Takes a sample through the core rules; returns the message shown to the player.</summary>
        public string TryCollect(SampleSite site)
        {
            if (site == null) return "Рядом нет образцов.";
            CollectionCheck check = SampleCollection.Check(site, body.Capabilities, session.Profile.CargoMass(), session.Profile.collectedSites);
            if (!check.Ok) return check.Reason;
            SampleRecord record = SampleCollection.ToRecord(site, body.Capabilities);
            if (!session.AddCargo(record)) return "Профиль только для чтения — образец не сохранится.";
            if (siteVisuals.TryGetValue(site.Id, out Transform t) && t != null) Destroy(t.gameObject);
            siteVisuals.Remove(site.Id);
            string message = "Взят образец: «" + site.Name + "» — " + site.Description + (record.analysed ? ", проанализирован спектрометром" : "") +
                             ". Груз " + UnitFormatter.Number(session.Profile.CargoMass(), 2) + " из " + UnitFormatter.Number(body.Capabilities.SampleCapacity, 0) + " кг.";
            if (planet.Definition.planetId == HomeSystemCatalog.HomeWorldId)
            {
                session.DeliverCargo();
                message += " Вы дома — образец сразу сдан (можно продать).";
            }
            else message += " Продать — после возвращения на Четвёртую планету.";
            if (!session.Profile.HasMilestone(ResearchMilestones.FirstSample) && !body.UsedTestTool)
            {
                MilestoneDefinition m = session.Award(ResearchMilestones.FirstSample);
                if (m != null) message += " ДОСТИЖЕНИЕ «" + m.Name + "» +" + m.CreditReward + " кр.";
            }
            state.Notify(message);
            return message;
        }

        /// <summary>E: outside — take the nearest sample in reach or board the rocket; inside — go out, or use the arm.</summary>
        public string Interact()
        {
            var sim = body.Simulation;
            if (Eva)
            {
                if (Nearest != null && NearestDistance <= SampleCollection.HandReach) return TryCollect(Nearest);
                double toRocket = (walker.Position - sim.LandedFixedPosition).Magnitude;
                if (toRocket <= BoardingDistance)
                {
                    EndEva();
                    return "Астронавт в ракете.";
                }
                string far = "Ничего в пределах " + SampleCollection.HandReach + " м; до ракеты " + UnitFormatter.Number(toRocket, 0) + " м.";
                state.Notify(far);
                return far;
            }
            if (sim == null || !sim.IsLanded || body.Destroyed) return "Нужна посадка.";
            string blocked = TryStartEva();
            if (blocked.Length == 0) return "";
            // A suit would not survive: the lander's arm still reaches the closest samples.
            FindNearest(sim.LandedFixedPosition);
            string reply = blocked + " " + (Nearest != null && NearestDistance <= SampleCollection.ArmReach
                ? TryCollect(Nearest)
                : "Манипулятор достаёт только до " + SampleCollection.ArmReach + " м — образцов в зоне нет.");
            state.Notify(reply);
            return reply;
        }

        // ---- Frame loop --------------------------------------------------------------------------------------

        void Update()
        {
            if (body == null || body.Simulation == null || planet == null || planet.Model == null) return;
            if (Eva && (!body.Simulation.IsLanded || body.Destroyed)) EndEva();
            RefreshSites();
            if (FlightInput.IsTyping) return;
            if (FlightInput.GetKeyDown(InputKey.E)) Interact();
            if (!Eva) return;

            if (FlightInput.RightMouseHeld)
            {
                Vector2 d = FlightInput.LookDelta; // x player sensitivity, optional inverted Y
                yaw = Mathf.Repeat(yaw + d.x * MouseSensitivity, 360f);
                lookPitch = Mathf.Clamp(lookPitch - d.y * MouseSensitivity, -85f, 85f);
            }
            if (FlightInput.GetKeyDown(InputKey.V)) ToggleThirdPerson();
            double forward = (FlightInput.GetKey(InputKey.W) ? 1 : 0) - (FlightInput.GetKey(InputKey.S) ? 1 : 0);
            double right = (FlightInput.GetKey(InputKey.D) ? 1 : 0) - (FlightInput.GetKey(InputKey.A) ? 1 : 0);
            bool sprint = FlightInput.GetKey(InputKey.LeftShift), jump = FlightInput.GetKeyDown(InputKey.Space) || driveJump;
            driveJump = false;
            if (Time.unscaledTime < driveUntil)
            {
                forward = driveForward;
                right = driveRight;
                sprint |= driveRun;
            }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            walker.Step(dt, forward, right, yaw, sprint, jump);
            FindNearest(walker.Position);
            AnimateBody(dt);
        }

        /// <summary>The body's horizontal axes in the planet-fixed frame: the way the astronaut (and the camera) faces.</summary>
        void BodyAxes(out Vector3d up, out Vector3d forward, out Vector3d right)
        {
            up = walker.Up;
            PlanetRotation.LocalBasis(up, out Vector3d east, out Vector3d north);
            double y = yaw * MathD.DegToRad;
            forward = north * Math.Cos(y) + east * Math.Sin(y);
            right = east * Math.Cos(y) - north * Math.Sin(y);
        }

        void AnimateBody(float dt)
        {
            BodyAxes(out _, out Vector3d forward, out Vector3d right);
            if (model != null)
            {
                model.Animate(walker, forward, right, dt);
                // The camera rides with the body, low-passed so the stride reads as a sway, not a jolt.
                float k = 1f - Mathf.Exp(-14f * dt);
                Vector3 target = model.EyeLocal;
                eyeLocal = new Vector3(Mathf.Lerp(eyeLocal.x, target.x, k), Mathf.Lerp(eyeLocal.y, target.y, k), Mathf.Lerp(eyeLocal.z, target.z, k));
            }
            // A little wider view at full sprint (the sense of speed), none in a slow lope on a small world.
            float speedShare = walker.Sprinting ? Mathf.Clamp01((float)((walker.HorizontalSpeed - walker.WalkSpeed) / Math.Max(0.5, 4.5 - walker.WalkSpeed))) : 0f;
            fovKick = Mathf.Lerp(fovKick, SprintFovKick * speedShare, 1f - Mathf.Exp(-4f * dt));
            if (baseFov > 0f && cameraController.Camera != null) cameraController.Camera.fieldOfView = baseFov + fovKick;
            if (visor != null)
            {
                visor.Visible = !thirdPerson;
                visor.Strain = (float)MathD.Clamp(1.0 - walker.Stamina.Fraction / 0.5, 0.0, 1.0) * 0.8f;
            }
        }

        void LateUpdate()
        {
            if (Eva && model != null && planet != null && planet.Model != null && origin != null)
            {
                // Feet on the walker's ground point, facing the camera's heading (after the camera moved the floating origin).
                double tr = simulation.RenderTime;
                BodyAxes(out Vector3d up, out Vector3d forward, out _);
                Vector3d feet = planet.Model.ToInertial(walker.Position, tr);
                Vector3 f = CoordinateSystem.ToUnityVector(planet.Model.Rotation.FixedToInertial(forward, tr));
                Vector3 u = CoordinateSystem.ToUnityVector(planet.Model.Rotation.FixedToInertial(up, tr));
                model.Root.SetPositionAndRotation(origin.ToUnityPosition(feet), Quaternion.LookRotation(f, u));
            }
            if (siteRoot == null || planet == null || planet.Model == null || origin == null) return;
            double t = simulation.RenderTime;
            foreach (SampleSite s in sites)
            {
                if (!siteVisuals.TryGetValue(s.Id, out Transform tr) || tr == null) continue;
                Vector3d p = planet.Model.ToInertial(SitePoint(s), t);
                Vector3d up = (p - planet.Model.Position).Normalized;
                tr.SetPositionAndRotation(origin.ToUnityPosition(p + up * 0.15), Quaternion.FromToRotation(Vector3.up, CoordinateSystem.ToUnityVector(up)));
            }
        }

        public void GetView(double renderTime, out Vector3d eye, out Vector3d forward, out Vector3d up)
        {
            BodyAxes(out Vector3d fixedUp, out Vector3d heading, out Vector3d side);
            double p = lookPitch * MathD.DegToRad;
            Vector3d look = heading * Math.Cos(p) + fixedUp * Math.Sin(p);
            Vector3d fixedEye, fixedForward;
            if (!thirdPerson)
            {
                // Inside the helmet: the eye follows the hips, the lean and the stride of the body below it.
                fixedEye = walker.Position + fixedUp * eyeLocal.y + heading * eyeLocal.z + side * eyeLocal.x;
                fixedForward = look;
            }
            else
            {
                // Behind and above the astronaut, never below the ground.
                Vector3d target = walker.Position + fixedUp * 1.25;
                fixedEye = target - look * ThirdPersonDistance;
                Vector3d dir = fixedEye.Normalized;
                double floor = walker.PointOnGround(dir).Magnitude + 0.4;
                if (fixedEye.Magnitude < floor) fixedEye = dir * floor;
                fixedForward = (target - fixedEye).Normalized;
            }
            eye = planet.Model.ToInertial(fixedEye, renderTime);
            forward = planet.Model.Rotation.FixedToInertial(fixedForward, renderTime);
            up = planet.Model.Rotation.FixedToInertial(fixedUp, renderTime);
        }

        // ---- HUD ---------------------------------------------------------------------------------------------

        void OnGUI()
        {
            var sim = body != null ? body.Simulation : null;
            if (sim == null || !sim.IsLanded || body.Destroyed || session == null || FlightInput.PauseMenuOpen) return;
            HudStyle.Begin();
            if (note == null) note = new GUIStyle(HudStyle.Small) { wordWrap = true };
            Vector2 screen = HudStyle.VirtualScreen;
            var r = Eva ? new Rect(screen.x * 0.5f - 320f, screen.y - 300f, 640f, 170f) : new Rect(screen.x * 0.5f - 290f, screen.y - 250f, 580f, 120f);
            GUILayout.BeginArea(r);
            GUILayout.BeginVertical(HudStyle.Panel);
            double cargo = session.Profile.CargoMass(), capacity = body.Capabilities.SampleCapacity;
            if (Eva)
            {
                double g = walker.Gravity;
                GUILayout.Label("НА ПОВЕРХНОСТИ · " + planet.Definition.displayName, HudStyle.Title);
                GUILayout.Label("g = " + UnitFormatter.Number(g, 2) + " м/с² · шаг " + UnitFormatter.Number(walker.WalkSpeed, 2) + " м/с (√(Fr·g·L)) · " + SprintLine(g) +
                                " · разгон ≤ μg = " + UnitFormatter.Number(Locomotion.MaxAcceleration(g), 2) + " м/с² · прыжок v²/2g ≈ " + UnitFormatter.Number(walker.JumpHeight, 2) +
                                " м · до ракеты " + UnitFormatter.Number(walker.DistanceToAnchor, 0) + " м (трос " + SurfaceWalker.TetherLength + " м)", note);
                StaminaRow(g);
                GUILayout.Label("Сейчас " + UnitFormatter.Number(walker.HorizontalSpeed, 2) + " м/с · " + GaitName(walker.Gait) +
                                (walker.Gait == Gait.Walking || walker.Gait == Gait.Running
                                    ? " (Fr = " + UnitFormatter.Number(Locomotion.FroudeNumber(walker.HorizontalSpeed, g), 2) + ", " +
                                      UnitFormatter.Number(Locomotion.StepFrequency(walker.HorizontalSpeed, g), 1) + " шаг/с)"
                                    : "") +
                                (model != null && Mathf.Abs(model.LeanDegrees) > 3f ? " · наклон " + UnitFormatter.Number(model.LeanDegrees, 0) + "° (tg θ = a/g)" : "") +
                                " · " + FlightInput.KeyLabel(InputKey.LeftShift) + " — спринт, " + FlightInput.KeyLabel(InputKey.V) + " — " + (thirdPerson ? "от первого лица" : "вид со стороны"), note);
                if (walker.Blocked.Length > 0) GUILayout.Label("Стоп: " + walker.Blocked, note);
            }
            else
            {
                CollectionCheck exit = SampleCollection.CanExit(planet.Model.Properties, planet.Definition.hazards);
                GUILayout.Label("ПОСАДКА · " + planet.Definition.displayName, HudStyle.Title);
                GUILayout.Label(exit.Ok ? FlightInput.KeyLabel(InputKey.E) + " — выйти на поверхность (" + MoveKeys() + " — идти, " + FlightInput.KeyLabel(InputKey.LeftShift) + " — спринт, " + FlightInput.KeyLabel(InputKey.Space) + " — прыжок, ПКМ — осмотреться, " + FlightInput.KeyLabel(InputKey.V) + " — вид со стороны)."
                    : "Выход невозможен: " + exit.Reason + " " + FlightInput.KeyLabel(InputKey.E) + " — манипулятор (до " + SampleCollection.ArmReach + " м).", note);
                FindNearest(sim.LandedFixedPosition);
            }
            string nearest = Nearest == null ? "образцов рядом нет"
                : "ближайший: «" + Nearest.Name + "» (" + Nearest.Description + ") — " + UnitFormatter.Number(NearestDistance, 0) + " м" +
                  (Eva && NearestDistance <= SampleCollection.HandReach ? " · " + FlightInput.KeyLabel(InputKey.E) + " — взять" : "");
            GUILayout.Label("Груз " + UnitFormatter.Number(cargo, 2) + " / " + UnitFormatter.Number(capacity, 0) + " кг · " + nearest, note);
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }

        /// <summary>"WASD" with the default keys, "Вверх/Влево/Вниз/Вправо" style when rebound to longer names.</summary>
        static string MoveKeys()
        {
            string[] k = { FlightInput.KeyLabel(InputKey.W), FlightInput.KeyLabel(InputKey.A), FlightInput.KeyLabel(InputKey.S), FlightInput.KeyLabel(InputKey.D) };
            bool letters = k[0].Length == 1 && k[1].Length == 1 && k[2].Length == 1 && k[3].Length == 1;
            return string.Join(letters ? "" : "/", k);
        }

        /// <summary>Top running speed here and what limits it.</summary>
        static string SprintLine(double g)
        {
            switch (Locomotion.SprintLimitedBy(g))
            {
                case SprintLimit.Weight:
                    return "бег невозможен: ноги давят " + UnitFormatter.Number(Locomotion.LegForce, 1) + " веса тела, а вес с костюмом здесь " +
                           UnitFormatter.Number(Locomotion.EffectiveLoad(g), 0) + " кг (k = " + UnitFormatter.Number(Locomotion.ForceRatio(g), 2) + " < 1)";
                case SprintLimit.Strength:
                    return "спринт " + UnitFormatter.Number(Locomotion.SprintSpeed(g), 2) + " м/с — предел силы ног L(2k−1)/t, k = " +
                           UnitFormatter.Number(Locomotion.ForceRatio(g), 2);
                default:
                    return "спринт " + UnitFormatter.Number(Locomotion.SprintSpeed(g), 2) + " м/с — предел Fr = " + UnitFormatter.Number(Locomotion.SprintFroude, 0) +
                           " (сильнее толчок — долгий неуправляемый полёт)";
            }
        }

        /// <summary>The anaerobic reserve W′: a bar and what the current effort costs.</summary>
        void StaminaRow(double g)
        {
            WorkCapacity stamina = walker.Stamina;
            GUILayout.BeginHorizontal();
            GUILayout.Label("Запас сил", note, GUILayout.Width(72f));
            Rect bar = GUILayoutUtility.GetRect(150f, 12f, GUILayout.Width(150f), GUILayout.Height(12f));
            bar = new Rect(bar.x, bar.y + 4f, bar.width, 9f);
            Color back = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = stamina.Exhausted ? HudStyle.Danger : stamina.Fraction < 0.35 ? HudStyle.Warning : HudStyle.Accent;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * (float)stamina.Fraction, bar.height), Texture2D.whiteTexture);
            GUI.color = back;

            double p = walker.MetabolicPower, cp = stamina.CriticalPower;
            string effort = UnitFormatter.Number(stamina.Fraction * 100.0, 0) + " % · " + UnitFormatter.Number(p, 0) + " Вт (порог " + UnitFormatter.Number(cp, 0) + " Вт)";
            if (!walker.CanSprint) effort += " · только шаг";
            else if (walker.SprintBlocked.Length > 0) effort += " · " + walker.SprintBlocked;
            else if (p > cp) effort += " · хватит на " + UnitFormatter.Number(stamina.Balance / (p - cp), 0) + " с";
            else if (double.IsPositiveInfinity(Locomotion.SprintEndurance(g))) effort += " · спринт здесь не утомляет: " +
                                                                                    UnitFormatter.Number(Locomotion.MetabolicPower(Locomotion.SprintSpeed(g), g), 0) + " Вт < порога";
            else if (stamina.Fraction < 0.999) effort += " · восстановление";
            else effort += " · спринт ≈ " + UnitFormatter.Number(Locomotion.SprintEndurance(g), 0) + " с";
            GUILayout.Label(effort, note);
            GUILayout.EndHorizontal();
        }

        static string GaitName(Gait gait)
        {
            switch (gait)
            {
                case Gait.Walking: return "шаг";
                case Gait.Running: return "бег";
                case Gait.Airborne: return "в прыжке";
                default: return "стоит";
            }
        }
    }
}
