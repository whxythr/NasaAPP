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
    /// Frame: the simulation runs in an inertial frame centred on the ACTIVE body (the one the probe is at); the
    /// probe feels only that body's gravity (patched-conic simplification, see PHYSICS.md). All other bodies and the
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
        [SerializeField] Color skyAmbient = new Color(0.10f, 0.15f, 0.24f);

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

        public event Action<Body> ActiveBodyChanged;

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

            for (int i = 0; i < bodies.Count; i++)
            {
                string parentId = bodies[i].Runtime.Definition.parentBodyId;
                bodies[i].ParentIndex = string.IsNullOrEmpty(parentId) ? -1 : IndexOf(parentId);
                double parentMu = bodies[i].ParentIndex >= 0
                    ? bodies[bodies[i].ParentIndex].Runtime.Model.GravitationalParameter
                    : Star.GravitationalParameter;
                bodies[i].Orbit = new KeplerOrbit(bodies[i].Runtime.Definition.orbit, parentMu);
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
        public Vector3d StarCentricPosition(int i, double time)
        {
            Vector3d p = bodies[i].Orbit.Position(time);
            int parent = bodies[i].ParentIndex;
            int guard = 0;
            while (parent >= 0 && guard++ < 16)
            {
                p += bodies[parent].Orbit.Position(time);
                parent = bodies[parent].ParentIndex;
            }
            return p;
        }

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
                if (i == ActiveIndex)
                {
                    r.RenderScale = 1.0;
                    continue;
                }
                double extent = r.Model.Radius + Math.Max(r.VisualShellHeight, r.Model.Surface.MaxSurfaceHeight) + 1000.0;
                double reach = (r.Model.Position - viewer).Magnitude + extent;
                r.RenderScale = reach <= far ? 1.0 : far / reach;
            }
        }
    }
}
