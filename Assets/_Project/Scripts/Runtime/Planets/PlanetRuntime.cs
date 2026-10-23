using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Runtime owner of one planet: turns a <see cref="PlanetDefinition"/> into the physical
    /// <see cref="PlanetModel"/> (gravity, rotation, atmosphere, surface) and keeps it in sync with Inspector
    /// edits. Physics reads <see cref="Model"/> every step, so a changed mass/radius applies immediately.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    public sealed class PlanetRuntime : MonoBehaviour
    {
        [SerializeField] PlanetDefinition definition;

        GravityField gravityField;
        bool physicsDirty, surfaceDirty;
        string surfaceSignature;

        public PlanetDefinition Definition => definition;
        public PlanetModel Model { get; private set; }
        /// <summary>Procedural surface (null for imported/reference-sphere planets).</summary>
        public PlanetSurfaceSampler Sampler { get; private set; }
        /// <summary>Visual atmosphere shell height above the reference radius [m] (rendering bound only).</summary>
        public double VisualShellHeight { get; private set; }
        /// <summary>Fingerprint of the current procedural surface.</summary>
        public string SurfaceFingerprint { get; private set; } = "";
        public string LastError { get; private set; }

        public event Action<PlanetModel> ModelChanged;
        public event Action<PlanetSurfaceSampler> SurfaceChanged;

        public void Initialize(PlanetDefinition def, GravityField field)
        {
            if (definition != null) definition.Changed -= OnDefinitionChanged;
            definition = def != null ? def : PlanetDefinition.CreateEarthPrototype();
            gravityField = field;
            definition.Changed += OnDefinitionChanged;
            RebuildSurface();
            RebuildPhysics();
        }

        void OnDestroy()
        {
            if (definition != null) definition.Changed -= OnDefinitionChanged;
        }

        void OnDefinitionChanged()
        {
            physicsDirty = true;
            if (ComputeSurfaceSignature() != surfaceSignature) surfaceDirty = true;
        }

        void Update()
        {
            if (surfaceDirty)
            {
                RebuildSurface();
                physicsDirty = true;
            }
            if (physicsDirty) RebuildPhysics();
        }

        string ComputeSurfaceSignature()
        {
            if (definition == null) return "";
            return definition.surfaceSource + "|" + definition.physical.meanRadius.ToString("R") + "|" +
                   JsonUtility.ToJson(definition.BuildGenerationSettings());
        }

        /// <summary>Rebuilds the deterministic surface function (does not touch physics constants).</summary>
        public void RebuildSurface()
        {
            surfaceDirty = false;
            surfaceSignature = ComputeSurfaceSignature();
            Sampler = null;
            SurfaceFingerprint = "";
            if (definition.surfaceSource == PlanetSurfaceSource.Procedural)
            {
                try
                {
                    Sampler = PlanetSurfaceSampler.Create(definition.BuildGenerationSettings(), definition.physical.meanRadius);
                    SurfaceFingerprint = PlanetFingerprint.Compute(Sampler);
                }
                catch (Exception e)
                {
                    LastError = e.Message;
                    Debug.LogError("[SpaceSim] Surface generation refused: " + e.Message + " Falling back to the reference sphere.", this);
                }
            }
            SurfaceChanged?.Invoke(Sampler);
        }

        /// <summary>Rebuilds gravity/rotation/atmosphere from the current physical data.</summary>
        public void RebuildPhysics()
        {
            physicsDirty = false;
            var issues = definition.physical.Validate();
            if (issues.Count > 0)
            {
                LastError = string.Join(" ", issues);
                Debug.LogError("[SpaceSim] Invalid physical data for " + definition.displayName + ": " + LastError + " Keeping the previous model.", this);
                return;
            }

            ISurfaceHeightProvider surface = Sampler != null ? Sampler : (ISurfaceHeightProvider)ReferenceSphereSurface.Instance;
            var oldModel = Model;
            Model = new PlanetModel(definition.displayName, definition.physical, surface, Vector3d.Zero);
            VisualShellHeight = AtmosphereOptics.VisualShellHeight(Model.Atmosphere, definition.optics);
            LastError = null;

            if (gravityField != null)
            {
                if (oldModel != null) gravityField.Remove(oldModel.Gravity);
                gravityField.Add(Model.Gravity);
            }
            ModelChanged?.Invoke(Model);
        }

        /// <summary>Planet centre in Unity space for the current floating origin.</summary>
        public Vector3 UnityCenter(FloatingOrigin origin) => origin.ToUnityPosition(Model.Position);

        /// <summary>Planet-fixed → Unity world rotation at <paramref name="time"/>.</summary>
        public Quaternion UnityRotation(double time) => CoordinateSystem.ToUnityRotation(Model.Rotation.Orientation(time));

        /// <summary>Simulation-frame position of a planet-fixed point at <paramref name="time"/>.</summary>
        public Vector3d FixedToSimulation(Vector3d fixedPosition, double time) => Model.ToInertial(fixedPosition, time);

        public Vector3d LaunchSiteDirection() => PlanetRotation.DirectionFromLatLon(
            definition.launchSite.latitudeDegrees * MathD.DegToRad, definition.launchSite.longitudeDegrees * MathD.DegToRad);

        [ContextMenu("Save planet (seed + parameters)")]
        void SaveFromMenu()
        {
            string path = PlanetSaveService.Save(this);
            Debug.Log("[SpaceSim] Planet saved to " + path, this);
        }

        [ContextMenu("Load planet from save")]
        void LoadFromMenu()
        {
            if (PlanetSaveService.TryLoadInto(this, out string message)) Debug.Log("[SpaceSim] " + message, this);
            else Debug.LogWarning("[SpaceSim] " + message, this);
        }

        [ContextMenu("Regenerate surface")]
        void RegenerateFromMenu()
        {
            RebuildSurface();
            RebuildPhysics();
        }

        /// <summary>Used by the save service after replacing the definition's data.</summary>
        internal void ApplyDefinitionData()
        {
            RebuildSurface();
            RebuildPhysics();
        }
    }
}
