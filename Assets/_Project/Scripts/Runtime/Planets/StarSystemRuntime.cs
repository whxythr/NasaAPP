using System;
using System.Collections.Generic;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// The star system: every planet/moon with its physics, visuals and atmosphere, moving along Kepler orbits.
    ///
    /// Frame: the simulation runs in a non-rotating frame centred on the ACTIVE body; gravity follows patched conics
    /// (<see cref="PatchedConicSystem"/>): inside a body's sphere of influence only that body pulls, outside — the star. All other bodies and the
    /// star are placed every frame at their orbital positions relative to the active body. Bodies beyond the camera's
    /// far plane are drawn in scaled space (exact direction and angular size, closer and smaller).
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public sealed class StarSystemRuntime : MonoBehaviour
    {
        public sealed class Body
        {
            public PlanetRuntime Runtime;
            public PlanetVisualController Visual;
            public AtmosphereRenderer Atmosphere;
            public Material SurfaceMaterial;
            public KeplerOrbit Orbit;
            public int ParentIndex = -1;
            public string Id => Runtime.Definition.planetId;
            public string Name => Runtime.Definition.displayName;
        }

        [Tooltip("Brightness of starlight on surfaces (eye-adapted; not scaled with distance).")]
        [SerializeField] float starLightIntensity = 1.4f;
        [Tooltip("Sky light colour on the ground at full daylight (visual approximation).")]
        // sRGB; ≈ (0.11, 0.16, 0.25) linear — a clear sky gives a shaded ground ~15–20 % of the sunlit brightness, bluish.
        // (It was ~10× weaker: shadows and the shaded side of the suit and the rocket were pitch black under a blue sky.)
        // Airless bodies get none of it: their shadows stay black.
        [SerializeField] Color skyAmbient = new Color(0.36f, 0.43f, 0.54f);

        readonly List<Body> bodies = new List<Body>();
        SimulationController simulation;
        FloatingOrigin origin;
        Camera viewCamera;

        public IReadOnlyList<Body> Bodies => bodies;
        public StarProperties Star { get; private set; }
        public string SystemName { get; private set; }
        public int ActiveIndex { get; private set; }
        public Body ActiveBody => bodies[ActiveIndex];
        public PlanetRuntime Active => bodies[ActiveIndex].Runtime;
        /// <summary>Star centre in the simulation (active-body) frame at render time [m].</summary>
        public Vector3d StarPosition { get; private set; }
        /// <summary>Linear colour × intensity of starlight for surface shaders.</summary>
        public Color StarLightColor => new Color(Star.color.r, Star.color.g, Star.color.b).linear * starLightIntensity;
        public Color SkyAmbient => skyAmbient.linear;
        public float StarLightIntensity => starLightIntensity;

        /// <summary>
        /// Sky light on a body's ground at full daylight: <see cref="SkyAmbient"/> scaled by how much air there is to scatter
        /// sunlight — the air column p/g against Earth's 10 330 kg/m²: 1 − exp(−3·m/m⊕) (≈ 0.95 for Earth-like air, ≈ 0.02
        /// for a 100 Pa trace of oxygen). Thin air: a dark sky and nearly black shadows. Visual approximation.
        /// </summary>
        public Color SkyAmbientFor(PlanetRuntime body)
        {
            var physical = body != null && body.Model != null ? body.Model.Properties : null;
            if (physical == null || physical.atmosphere == null || !physical.atmosphere.hasAtmosphere || physical.SurfaceGravity <= 0.0) return Color.black;
            double column = physical.atmosphere.surfacePressure / physical.SurfaceGravity;
            const double earthColumn = 101325.0 / 9.80665;
            return SkyAmbient * (float)(1.0 - Math.Exp(-3.0 * column / earthColumn));
        }

        public event Action<Body> ActiveBodyChanged;

        /// <summary>Orbits, spheres of influence and patched-conic gravity of this system (indices = <see cref="Bodies"/>).</summary>
        public PatchedConicSystem Navigation { get; private set; }

        /// <summary>
        /// Game progression: the star system's tools (body labels, system map, jumping between bodies) open the first
        /// time the vehicle crosses the space boundary. The other bodies physically exist all along.
        /// </summary>
        public bool SpaceUnlocked { get; private set; }

        public void UnlockSpace() => SpaceUnlocked = true;

        public void Initialize(StarSystemDefinition definition, SimulationController sim, FloatingOrigin floatingOrigin,
            Shader surfaceShader, Shader atmosphereShader)
        {
            simulation = sim;
            origin = floatingOrigin;
            Star = definition.star.Clone();
            SystemName = definition.systemName;

            foreach (PlanetDefinition def in definition.bodies)
            {
                if (def == null) continue;
                var go = new GameObject(def.displayName);
                go.transform.SetParent(transform, false);
                var runtime = go.AddComponent<PlanetRuntime>();
                runtime.Initialize(def, null);

                var material = new Material(surfaceShader) { name = "PlanetSurface · " + def.displayName };
                var visual = go.AddComponent<PlanetVisualController>();
                visual.Initialize(runtime, origin, simulation, material);

                var atmo = new GameObject("Atmosphere").AddComponent<AtmosphereRenderer>();
                atmo.transform.SetParent(go.transform, false);
                atmo.Initialize(runtime, origin, this, atmosphereShader, material);

                bodies.Add(new Body { Runtime = runtime, Visual = visual, Atmosphere = atmo, SurfaceMaterial = material });
            }
            if (bodies.Count == 0) throw new InvalidOperationException("Star system has no bodies.");

            // One navigation model for the whole game (orbits, spheres of influence, patched-conic gravity, prediction).
            var specs = new List<NavBodySpec>();
            foreach (Body b in bodies)
            {
                PlanetDefinition d = b.Runtime.Definition;
                specs.Add(new NavBodySpec
                {
                    Id = d.planetId, Name = d.displayName, ParentId = string.IsNullOrEmpty(d.parentBodyId) ? null : d.parentBodyId,
                    Physical = b.Runtime.Model.Properties, Orbit = d.orbit, MaxTerrainHeight = b.Runtime.Model.Surface.MaxSurfaceHeight,
                    Hazards = d.hazards, Description = d.description
                });
            }
            Navigation = new PatchedConicSystem(Star, specs);
            for (int i = 0; i < bodies.Count; i++)
            {
                bodies[i].ParentIndex = Navigation[i].ParentIndex;
                bodies[i].Orbit = Navigation[i].Orbit;
            }
            UpdatePositions(simulation.SimulationTime);
        }

        public int IndexOf(string planetId)
        {
            for (int i = 0; i < bodies.Count; i++)
                if (bodies[i].Id == planetId) return i;
            return -1;
        }

        public void SetCamera(Camera cam) => viewCamera = cam;

        /// <summary>Makes another body the centre of the simulation frame and the only gravity source for the probe.</summary>
        public void SetActive(int index, GravityField probeGravity)
        {
            index = Mathf.Clamp(index, 0, bodies.Count - 1);
            foreach (var b in bodies) b.Runtime.SetGravityField(null);
            ActiveIndex = index;
            bodies[index].Runtime.Model.Gravity.Position = Vector3d.Zero;
            bodies[index].Runtime.SetGravityField(probeGravity);
            UpdatePositions(simulation.SimulationTime);
            ActiveBodyChanged?.Invoke(bodies[index]);
        }

        /// <summary>Position of body i relative to the star [m] at time t.</summary>
        public Vector3d StarCentricPosition(int i, double time) => Navigation.Position(i, time);

        /// <summary>Unit vector from a simulation-frame point towards the star.</summary>
        public Vector3d SunDirectionFrom(Vector3d simulationPosition) => (StarPosition - simulationPosition).Normalized;

        /// <summary>
        /// False after Unity reloads scripts during Play mode: the simulation's state lives in plain C# objects that
        /// Unity does not serialize, so the world cannot continue — restart Play mode.
        /// </summary>
        bool Alive => simulation != null && bodies.Count > 0;

        void Update()
        {
            if (!Alive) return;
            UpdatePositions(simulation.RenderTime);
        }

        void UpdatePositions(double time)
        {
            Vector3d activeAbs = StarCentricPosition(ActiveIndex, time);
            StarPosition = -activeAbs;
            for (int i = 0; i < bodies.Count; i++)
            {
                var model = bodies[i].Runtime.Model;
                if (model == null) continue;
                // The active body's position is the origin of the frame and is never moved (physics depends on it).
                model.Gravity.Position = i == ActiveIndex ? Vector3d.Zero : StarCentricPosition(i, time) - activeAbs;
            }
        }

        void LateUpdate()
        {
            // After the camera (order −50) has set the viewer and far plane; before visuals (order ≥ 90).
            if (!Alive || origin == null) return;
            double far = viewCamera != null ? viewCamera.farClipPlane * 0.95 : 1e7;
            Vector3d viewer = origin.ViewerPosition;
            for (int i = 0; i < bodies.Count; i++)
            {
                PlanetRuntime r = bodies[i].Runtime;
                if (r.Model == null) continue;
                double distance = (r.Model.Position - viewer).Magnitude;
                // The active body is drawn at true scale near it (the camera's far plane is sized to contain it); scaled space
                // only during an interplanetary cruise, when the frame stays centred on it while the vehicle is far away.
                // (Scaling it near the ground shrank the planet around the camera and lifted the terrain over the launch pad.)
                if (i == ActiveIndex && distance < 50.0 * r.Model.Radius)
                {
                    r.RenderScale = 1.0;
                    continue;
                }
                double extent = r.Model.Radius + Math.Max(r.VisualShellHeight, r.Model.Surface.MaxSurfaceHeight) + 1000.0;
                double reach = distance + extent;
                r.RenderScale = reach <= far ? 1.0 : far / reach;
            }
        }
    }
}
