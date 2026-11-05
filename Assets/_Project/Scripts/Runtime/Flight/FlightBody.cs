using System.Collections.Generic;
using SpaceSim.Flight;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Simulation;
using SpaceSim.Vehicles;
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
    public sealed class FlightBody : MonoBehaviour, ISimulatedBody, IRailsBody
    {
        /// <summary>A flight in progress moved into another body's frame (sphere-of-influence handoff).</summary>
        public sealed class VehicleCarry
        {
            public RocketState Rocket;
            /// <summary>State in the NEW frame [m, m/s] at <see cref="Time"/>.</summary>
            public Vector3d Position, Velocity;
            public double Time;
            public bool UsedTestTool;
        }

        [Header("Payload / probe (SI)")]
        [Tooltip("Probe mass [kg] when flown without a rocket.")]
        [SerializeField] double mass = 1000.0;
        [Tooltip("Height of the reference point above the ground when resting [m].")]
        [SerializeField] double contactRadius = 1.0;
        [SerializeField] DragProperties drag = new DragProperties(true, 1.05, 3.14);

        [Header("Rocket")]
        [SerializeField] bool startWithRocket = true;
        [Tooltip("The rocket design built in the hangar (compiled into the physics configuration).")]
        [SerializeField] RocketBlueprint blueprint = RocketBlueprint.CreateDefault();
        RocketConfiguration rocket = new RocketConfiguration();

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

        /// <summary>Current design (a copy is kept; edit a clone and pass it to <see cref="ApplyBlueprint"/>).</summary>
        public RocketBlueprint Blueprint => blueprint;
        /// <summary>The design compiled into physics data, with capabilities and construction issues.</summary>
        public CompiledRocket Compiled { get; private set; }
        /// <summary>Capabilities as designed (independent of damage).</summary>
        public VehicleCapabilities DesignCapabilities => Compiled != null ? Compiled.Capabilities : new VehicleCapabilities();
        /// <summary>Effective capabilities: instruments go offline when hazards have degraded the equipment below 50 %.</summary>
        public VehicleCapabilities Capabilities
        {
            get
            {
                VehicleCapabilities design = DesignCapabilities;
                if (Condition.InstrumentsOnline) return design;
                return new VehicleCapabilities { RadiationShielding = design.RadiationShielding, AcidResistance = design.AcidResistance };
            }
        }
        /// <summary>Payload equipment integrity under environmental hazards (Level B, reset with every new rocket).</summary>
        public EquipmentCondition Condition { get; } = new EquipmentCondition();
        /// <summary>True once the initial-conditions test tool moved the vehicle (until a new rocket is placed on the pad).</summary>
        public bool UsedTestTool { get; private set; }

        System.Func<string, bool> ownsModule = id => true;
        /// <summary>Whether the player may install a module (profile ownership; free modules always).</summary>
        public bool OwnsModule(string moduleId) => ownsModule(moduleId);
        /// <summary>Raised after a new design was applied (new rocket on the pad).</summary>
        public event System.Action<RocketBlueprint> BlueprintApplied;

        /// <summary>True while resting on the launch pad — the only place the rocket can be rebuilt.</summary>
        public bool IsOnLaunchPad
        {
            get
            {
                if (sim == null || !sim.IsLanded) return false;
                Vector3d padDir = planet.LaunchSiteDirection();
                double chord = (sim.LandedFixedPosition.Normalized - padDir).Magnitude * planet.Model.Radius;
                return chord < 250.0;
            }
        }

        public void Initialize(PlanetRuntime planetRuntime, GravityField gravity, SimulationController simulation,
            FloatingOrigin floatingOrigin, Shader litShader, RocketBlueprint design = null, System.Func<string, bool> owns = null,
            PatchedConicSystem navigation = null, int frameIndex = -1, VehicleCarry carry = null)
        {
            planet = planetRuntime;
            controller = simulation;
            origin = floatingOrigin;
            lit = litShader;
            if (owns != null) ownsModule = owns;
            if (design != null) blueprint = design.Clone();
            CompileBlueprint();
            sim = new FreeFlightSimulation(planet.Model, gravity);
            if (navigation != null && frameIndex >= 0) sim.SetNavigation(navigation, frameIndex);
            ApplyParameters();
            planet.ModelChanged += OnModelChanged;
            controller.Register(this);
            visual = new GameObject("VehicleVisual").transform;
            visual.SetParent(transform, false);
            if (carry == null) ResetToLaunchSite();
            else
            {
                // Continue the same flight in this body's frame: same rocket, same propellant, same trajectory.
                sim.Rocket = carry.Rocket;
                Condition.Reset();
                sim.SetInertialState(carry.Position, carry.Velocity, carry.Time);
                UsedTestTool = carry.UsedTestTool;
                RebuildVisual();
            }
        }

        /// <summary>Everything needed to continue this flight in another body's frame.</summary>
        public VehicleCarry CarryTo(int newFrameIndex)
        {
            Vector3d r = sim.State.Position, v = sim.State.Velocity;
            sim.Navigation.Transform(sim.FrameBody, newFrameIndex, sim.State.Time, ref r, ref v);
            return new VehicleCarry { Rocket = sim.Rocket, Position = r, Velocity = v, Time = sim.State.Time, UsedTestTool = UsedTestTool };
        }

        /// <summary>The vehicle is inside another body's sphere of influence: the frame should move to that body.</summary>
        public bool NeedsHandoff(out int bodyIndex)
        {
            bodyIndex = sim != null ? sim.Primary : -1;
            return sim != null && sim.Navigation != null && !sim.IsLanded && bodyIndex >= 0 && bodyIndex != sim.FrameBody;
        }

        // ---- IRailsBody: coasting at high time warp -----------------------------------------------------

        public double RailsHorizon(double maxDt) => sim.RailsHorizon(maxDt);

        public void RailsStep(double time, double dt, double renderLag)
        {
            sim.RailsStep(dt, renderLag);
            ApplyHazards(dt);
            CheckTouchdown();
        }

        /// <summary>Why the vehicle cannot coast on rails right now (empty when it can).</summary>
        public string RailsBlockedReason()
        {
            if (sim == null || sim.Navigation == null) return "нет модели навигации";
            if (sim.Rocket != null && sim.Rocket.IsBurning) return "работает двигатель";
            if (sim.IsLanded) return "";
            if (sim.RailsHorizon(1.0) <= 0.0)
            {
                int p = sim.Primary >= 0 ? sim.Primary : sim.FrameBody;
                NavBody b = sim.Navigation[p];
                return "ниже " + SpaceSim.Units.UnitFormatter.Number((b.ZoneRadius - b.Radius) / 1000.0, 0) + " км над " + b.Name + " (атмосфера/рельеф — только точное интегрирование)";
            }
            return "";
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

        public void SimulationStep(double time, double dt)
        {
            sim.Step(dt);
            ApplyHazards(dt);
            CheckTouchdown();
        }

        // ---- Touchdown ---------------------------------------------------------------------------------

        /// <summary>The vehicle was destroyed in a crash: no engine, no controls (R — a new rocket at the start planet).</summary>
        public bool Destroyed { get; private set; }
        /// <summary>Verdict of the last touchdown (null before the first one of this rocket).</summary>
        public LandingOutcome? LastLanding { get; private set; }
        /// <summary>Raised on every touchdown with its verdict.</summary>
        public event System.Action<LandingOutcome> Touchdown;
        int seenImpacts;

        /// <summary>Judges a new contact with the ground: speeds from the simulation's impact record, tilt, terrain slope.</summary>
        void CheckTouchdown()
        {
            if (sim.ImpactCount == seenImpacts) return;
            seenImpacts = sim.ImpactCount;
            ImpactRecord hit = sim.LastImpact;
            double horizontal = System.Math.Sqrt(System.Math.Max(0.0, hit.SurfaceRelativeSpeed * hit.SurfaceRelativeSpeed - hit.VerticalSpeed * hit.VerticalSpeed));
            double tilt = sim.Rocket != null && sim.Rocket.HasStages ? sim.Rocket.PitchDegrees : 0.0;
            double slope = LandingPhysics.TerrainSlope(planet.Model.Surface, planet.Model.Radius, sim.LandedFixedPosition);
            LandingOutcome outcome = LandingPhysics.Assess(hit.VerticalSpeed, horizontal, tilt, slope);
            LastLanding = outcome;
            if (outcome.Result == LandingResult.Hard) Condition.Damage(LandingPhysics.HardLandingDamage);
            if (outcome.Result == LandingResult.Crash)
            {
                Destroyed = true;
                if (sim.Rocket != null) sim.Rocket.EngineOn = false;
                Condition.Damage(1.0);
            }
            Touchdown?.Invoke(outcome);
        }

        void ApplyHazards(double dt)
        {
            // Environmental hazards act on the equipment where the vehicle actually is (Level B).
            var model = sim.Planet;
            double rho0 = model.Atmosphere.ReferenceDensity;
            double ratio = rho0 > 0.0 ? model.DensityAt(sim.State.Position) / rho0 : 0.0;
            VehicleCapabilities caps = DesignCapabilities;
            // A body's radiation and acid act only inside its own sphere of influence, not across interplanetary space.
            EnvironmentHazards hazards = sim.Navigation != null && sim.Primary != sim.FrameBody ? new EnvironmentHazards() : planet.Definition.hazards;
            Condition.Apply(HazardModel.Evaluate(hazards, ratio, caps.RadiationShielding, caps.AcidResistance), dt);
        }

        void CompileBlueprint()
        {
            Compiled = BlueprintCompiler.Compile(blueprint ?? RocketBlueprint.CreateDefault(), ownsModule);
            rocket = Compiled.Configuration;
            mass = Compiled.PayloadMass;
        }

        /// <summary>
        /// Builds a new rocket from <paramref name="design"/> and puts it, fully loaded as designed, on the launch pad.
        /// Only allowed while on the pad (returns false otherwise).
        /// </summary>
        public bool ApplyBlueprint(RocketBlueprint design)
        {
            if (design == null || !IsOnLaunchPad) return false;
            blueprint = design.Clone();
            CompileBlueprint();
            ApplyParameters();
            ResetToLaunchSite();
            BlueprintApplied?.Invoke(blueprint.Clone());
            return true;
        }

        /// <summary>Puts a fresh, fully fuelled rocket (or the bare probe) on the launch pad, engine off, vertical.</summary>
        public void ResetToLaunchSite()
        {
            sim.Rocket = startWithRocket ? new RocketState(rocket) : null;
            Condition.Reset();
            UsedTestTool = false;
            Destroyed = false;
            LastLanding = null;
            sim.PlaceOnSurface(planet.LaunchSiteDirection(), controller.SimulationTime);
            RebuildVisual();
        }

        /// <summary>Ignites the current stage at full throttle. Attitude is whatever the player has set (default vertical).</summary>
        public bool Launch()
        {
            var r = sim.Rocket;
            if (r == null || !r.CanBurn || Destroyed) return false;
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
            UsedTestTool = true; // research rewards only count for real flights
            sim.SetLocalState(latitudeDeg * MathD.DegToRad, longitudeDeg * MathD.DegToRad, altitude,
                vEast, vNorth, vUp, relativeToSurface, controller.SimulationTime);
        }

        /// <summary>
        /// Test tool: adds an instantaneous Δv [m/s] (simulation frame). Like the initial-conditions tool it is not a flight —
        /// research rewards and visits do not count afterwards.
        /// </summary>
        public void ApplyTestImpulse(Vector3d deltaV)
        {
            UsedTestTool = true;
            sim.SetInertialState(sim.State.Position, sim.State.Velocity + deltaV, sim.State.Time);
        }

        /// <summary>Position for rendering: interpolated between the last two physics steps.</summary>
        public Vector3d RenderPosition(double alpha, double renderTime)
        {
            if (sim.IsLanded) return sim.LandedPositionAt(renderTime);
            return Vector3d.Lerp(sim.PreviousState.Position, sim.State.Position, alpha);
        }

        // ---- Visuals -------------------------------------------------------------------------

        const float BellHeight = 1.8f;

        void RebuildVisual()
        {
            foreach (Transform child in visual) Destroy(child.gameObject);
            stageVisuals.Clear();
            laidOutStage = -1;
            fairing = heatGlow = flame = null;
            float radius = (float)rocket.radius;
            var white = MakeMaterial(new Color(0.90f, 0.90f, 0.91f), 0.35f);
            var dark = MakeMaterial(new Color(0.13f, 0.13f, 0.14f), 0.3f);
            var metal = MakeMaterial(new Color(0.38f, 0.36f, 0.34f), 0.5f, 0.6f);
            var gold = MakeMaterial(new Color(0.85f, 0.65f, 0.25f), 0.5f, 0.8f);
            var panel = MakeMaterial(new Color(0.08f, 0.12f, 0.30f), 0.6f);
            Mesh bell = ProceduralMeshes.Frustum(24, 0.55f, 0.28f, false, true, "EngineBell");
            Mesh cone = ProceduralMeshes.Frustum(32, 1f, 0f, false, false, "NoseCone");

            if (sim.Rocket != null)
            {
                for (int i = 0; i < rocket.stages.Length; i++)
                {
                    var stage = new GameObject("Stage" + (i + 1)).transform;
                    stage.SetParent(visual, false);
                    float len = (float)rocket.stages[i].length;
                    var tank = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Tank", stage, white);
                    tank.transform.localScale = new Vector3(2f * radius, len * 0.5f, 2f * radius);
                    tank.transform.localPosition = new Vector3(0f, len * 0.5f, 0f);
                    var ring = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Interstage", stage, dark);
                    ring.transform.localScale = new Vector3(2.02f * radius, 0.6f, 2.02f * radius);
                    ring.transform.localPosition = new Vector3(0f, len - 0.6f, 0f);
                    var band = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "RollBand", stage, dark);
                    band.transform.localScale = new Vector3(2.01f * radius, 0.5f, 2.01f * radius);
                    band.transform.localPosition = new Vector3(0f, len * 0.72f, 0f);
                    // Engine bell below the tank (open at the bottom).
                    var nozzle = ProceduralMeshes.MeshObject(bell, "EngineBell", stage, metal);
                    nozzle.transform.localScale = new Vector3(2f * radius, BellHeight, 2f * radius);
                    nozzle.transform.localPosition = new Vector3(0f, -BellHeight, 0f);
                    if (i == 0)
                        for (int f = 0; f < 4; f++)
                        {
                            var fin = ProceduralMeshes.Primitive(PrimitiveType.Cube, "Fin" + f, stage, dark);
                            Quaternion around = Quaternion.Euler(0f, 45f + 90f * f, 0f);
                            fin.transform.localRotation = around;
                            fin.transform.localScale = new Vector3(0.12f, 3.0f, 1.4f * radius);
                            fin.transform.localPosition = around * new Vector3(0f, 1.5f, radius * 1.3f);
                        }
                    stageVisuals.Add(stage.gameObject);
                }
                fairing = new GameObject("Fairing").transform;
                fairing.SetParent(visual, false);
                var ogive = ProceduralMeshes.MeshObject(cone, "Ogive", fairing, white);
                ogive.transform.localScale = new Vector3(radius, 2.6f * radius, radius);

                flameMaterial = ProceduralMeshes.EffectMaterial("SpaceSim/EnginePlume");
                if (flameMaterial != null)
                    flame = ProceduralMeshes.MeshObject(ProceduralMeshes.Frustum(24, 1f, 1.6f, false, false, "Plume"), "Plume", visual, flameMaterial).transform;
                heatMaterial = ProceduralMeshes.EffectMaterial("SpaceSim/HeatGlow");
                if (heatMaterial != null)
                {
                    // A plasma sheath around the whole stack, pushed towards the oncoming air (placed every frame).
                    heatGlow = ProceduralMeshes.Primitive(PrimitiveType.Sphere, "HeatGlow", visual, heatMaterial).transform;
                }
            }

            probeVisual = new GameObject("Probe").transform;
            probeVisual.SetParent(visual, false);
            float r = (float)System.Math.Min(contactRadius, rocket.radius);
            var hull = ProceduralMeshes.Primitive(PrimitiveType.Sphere, "Hull", probeVisual, white);
            hull.transform.localScale = Vector3.one * 2f * r;
            var goldBand = ProceduralMeshes.Primitive(PrimitiveType.Cylinder, "Band", probeVisual, gold);
            goldBand.transform.localScale = new Vector3(2.05f * r, 0.12f * r, 2.05f * r);
            for (int s = -1; s <= 1; s += 2)
            {
                var p = ProceduralMeshes.Primitive(PrimitiveType.Cube, "Panel", probeVisual, panel);
                p.transform.localScale = new Vector3(2.2f * r, 0.04f * r, 0.9f * r);
                p.transform.localPosition = new Vector3(s * 2.1f * r, 0f, 0f);
            }
        }

        Transform fairing, heatGlow;
        Material flameMaterial, heatMaterial;
        float nozzleY, stackTop;

        /// <summary>
        /// Stacks the remaining stages: the lowest engine bell ends at the ground under the reference point (on the pad — on its
        /// raised deck); the fairing covers the probe while stages remain.
        /// </summary>
        void LayoutStack()
        {
            var r = sim.Rocket;
            int first = r != null ? r.StageIndex : 1000;
            bool onPad = IsOnLaunchPad;
            int key = first * 2 + (onPad ? 1 : 0);
            if (key == laidOutStage) return;
            laidOutStage = key;
            float y = -(float)contactRadius + (onPad ? LaunchSite.DeckHeight : 0f) + BellHeight;
            nozzleY = y - BellHeight;
            for (int i = 0; i < stageVisuals.Count; i++)
            {
                bool attached = i >= first;
                stageVisuals[i].SetActive(attached);
                if (!attached) continue;
                stageVisuals[i].transform.localPosition = new Vector3(0f, y, 0f);
                y += (float)rocket.stages[i].length;
            }
            bool stack = first < stageVisuals.Count;
            stackTop = stack ? y + 2.6f * (float)rocket.radius : 2f * (float)contactRadius;
            if (fairing != null)
            {
                fairing.gameObject.SetActive(stack);
                fairing.localPosition = new Vector3(0f, y, 0f);
            }
            probeVisual.gameObject.SetActive(!stack);
            probeVisual.localPosition = Vector3.zero;
        }

        /// <summary>Stagnation heat flux at the nose now [W/m²] (Sutton–Graves, <see cref="AtmosphericHeating"/>).</summary>
        public double HeatFlux { get; private set; }

        // ---- Launch smoke (visual only): puffs in the planet-fixed frame, so they stay where they were made ----

        struct Puff
        {
            public Transform Transform;
            public Material Material;
            public Vector3d Position, Velocity; // planet-fixed
            public float Age, Life, Size;
        }

        readonly List<Puff> puffs = new List<Puff>();
        float puffBudget;
        uint puffSeed = 12345u;

        float Random01()
        {
            puffSeed = puffSeed * 1664525u + 1013904223u;
            return (puffSeed >> 8) / 16777216f;
        }

        void UpdateSmoke(float dt)
        {
            var rk = sim.Rocket;
            var model = planet.Model;
            bool air = model.Atmosphere.ReferenceDensity > 0.0;
            Vector3d fixedPos = sim.IsLanded ? sim.LandedFixedPosition : model.ToFixed(sim.State.Position, sim.State.Time);
            Vector3d dir = fixedPos.Normalized;
            double ground = model.Radius + model.Surface.SurfaceHeight(dir);
            double height = fixedPos.Magnitude - ground;
            if (air && rk != null && rk.IsBurning && height < 150.0 && puffs.Count < 90)
            {
                puffBudget += dt * 12f * (float)rk.Throttle;
                PlanetRotation.LocalBasis(dir, out Vector3d east, out Vector3d north);
                while (puffBudget >= 1f && puffs.Count < 90)
                {
                    puffBudget -= 1f;
                    Material m = ProceduralMeshes.EffectMaterial("SpaceSim/Smoke");
                    if (m == null) break;
                    float a = Random01() * 6.2831853f;
                    Vector3d outward = east * System.Math.Cos(a) + north * System.Math.Sin(a);
                    var go = ProceduralMeshes.Primitive(PrimitiveType.Sphere, "Smoke", null, m);
                    puffs.Add(new Puff
                    {
                        Transform = go.transform, Material = m, Position = dir * (ground + 2.0),
                        Velocity = outward * (18.0 + 18.0 * Random01()) + dir * (2.0 + 4.0 * Random01()),
                        Life = 9f + 5f * Random01(), Size = 3f + 3f * Random01()
                    });
                }
            }
            double t = controller.RenderTime;
            for (int i = puffs.Count - 1; i >= 0; i--)
            {
                Puff p = puffs[i];
                p.Age += dt;
                if (p.Age >= p.Life || p.Transform == null)
                {
                    if (p.Transform != null) Destroy(p.Transform.gameObject);
                    if (p.Material != null) Destroy(p.Material);
                    puffs.RemoveAt(i);
                    continue;
                }
                p.Velocity *= System.Math.Exp(-dt / 2.5); // the air slows the cloud
                p.Position += p.Velocity * dt;
                float k = p.Age / p.Life;
                float size = p.Size * (1.5f + 6f * k);
                Vector3d world = model.ToInertial(p.Position, t);
                p.Transform.position = origin.ToUnityPosition(world);
                p.Transform.localScale = Vector3.one * size;
                p.Material.SetFloat("_Alpha", 0.7f * Mathf.Pow(1f - k, 1.5f));
                p.Material.SetVector("_LightDir", CoordinateSystem.ToUnityVector((world - model.Position).Normalized));
                puffs[i] = p;
            }
        }

        void OnDisable()
        {
            foreach (Puff p in puffs)
            {
                if (p.Transform != null) Destroy(p.Transform.gameObject);
                if (p.Material != null) Destroy(p.Material);
            }
            puffs.Clear();
        }

        Material MakeMaterial(Color c, float smoothness = 0.3f, float metallic = 0f) => ProceduralMeshes.LitMaterial(lit, c, smoothness, metallic);

        void LateUpdate()
        {
            if (sim == null || origin == null || planet == null || planet.Model == null) return;
            double t = controller.RenderTime;
            Vector3d p = RenderPosition(controller.InterpolationAlpha, t);
            Vector3d up = (p - planet.Model.Position).Normalized;
            // Vehicle axis: vertical on the ground, the commanded attitude in flight.
            Vector3d axis = sim.IsLanded || sim.Rocket == null ? up : sim.ThrustDirection(p);
            if (Destroyed) axis = Vector3d.Cross(planet.Model.Rotation.SpinAxis, up).Normalized; // the wreck lies on its side
            Vector3 axisUnity = CoordinateSystem.ToUnityVector(axis);
            Vector3 fwd = Vector3.ProjectOnPlane(CoordinateSystem.ToUnityVector(planet.Model.Rotation.SpinAxis), axisUnity);
            if (fwd.sqrMagnitude < 1e-8f) fwd = Vector3.Cross(axisUnity, Vector3.right);
            transform.SetPositionAndRotation(origin.ToUnityPosition(p), Quaternion.LookRotation(fwd.normalized, axisUnity));

            LayoutStack();
            var model = planet.Model;
            var rk = sim.Rocket;
            double h = model.AltitudeAboveReference(p);
            double p0 = model.Atmosphere.Pressure(0.0);
            double vacuum = p0 > 0.0 ? 1.0 - System.Math.Min(1.0, model.Atmosphere.Pressure(h) / p0) : 1.0;
            if (flame != null)
            {
                bool burning = rk != null && rk.IsBurning && !Destroyed;
                flame.gameObject.SetActive(burning);
                if (burning)
                {
                    float scale = (float)rocket.radius / 1.85f;
                    // An under-expanded plume grows wide and long as the outside pressure drops.
                    float len = (8f + 22f * (float)rk.Throttle) * scale * (1f + 1.5f * (float)vacuum);
                    float w = 0.55f * (float)rocket.radius * (1f + 2.5f * (float)vacuum);
                    flame.localRotation = Quaternion.Euler(180f, 0f, 0f);
                    flame.localScale = new Vector3(w, len, w);
                    flame.localPosition = new Vector3(0f, nozzleY, 0f);
                    flameMaterial.SetFloat("_Intensity", (float)rk.Throttle);
                    flameMaterial.SetFloat("_Vacuum", (float)vacuum);
                }
            }
            // Aerodynamic heating at the nose (Level A estimate) → glow colour and brightness.
            Vector3d airVelocity = sim.State.Velocity - model.AirVelocityAt(sim.State.Position);
            HeatFlux = sim.IsLanded ? 0.0 : AtmosphericHeating.StagnationHeatFlux(model.DensityAt(sim.State.Position), airVelocity.Magnitude, rocket.radius);
            if (heatGlow != null)
            {
                float glow = HeatFlux > 2e4 ? Mathf.Clamp01((float)System.Math.Log10(HeatFlux / 2e4) / 2f) : 0f;
                heatGlow.gameObject.SetActive(glow > 0.01f);
                if (glow > 0.01f)
                {
                    float length = Mathf.Max(1f, stackTop - nozzleY), width = 2.6f * (float)rocket.radius;
                    heatGlow.localScale = new Vector3(width, length * 1.08f, width);
                    heatGlow.localPosition = new Vector3(0f, nozzleY + 0.5f * length, 0f);
                    Vector3 flow = CoordinateSystem.ToUnityVector(airVelocity.Normalized);
                    heatGlow.position += flow * (0.35f * width); // the windward side (facing the motion) glows
                    double temperature = AtmosphericHeating.EquilibriumTemperature(HeatFlux);
                    float warm = Mathf.Clamp01((float)(temperature - 800.0) / 1600f);
                    heatMaterial.SetColor("_Color", Color.Lerp(new Color(0.7f, 0.12f, 0.05f), new Color(1f, 0.85f, 0.55f), warm));
                    heatMaterial.SetFloat("_Intensity", glow * 1.5f);
                }
            }
            UpdateSmoke(Mathf.Min(Time.deltaTime, 0.1f));
        }
    }
}
