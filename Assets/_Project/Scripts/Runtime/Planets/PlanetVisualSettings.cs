using System;
using UnityEngine;

namespace SpaceSim.Runtime
{
    public enum PlanetSurfaceSource
    {
        /// <summary>Deterministic procedural relief (cube-sphere quadtree LOD). Physics uses the same height function.</summary>
        Procedural,
        /// <summary>Visual mesh imported from Blender (FBX/OBJ/…); physics uses the reference sphere.</summary>
        ImportedModel,
        /// <summary>Smooth sphere at the physical radius.</summary>
        ReferenceSphere
    }

    [Serializable]
    public class ProceduralLodSettings
    {
        [Tooltip("Quads per chunk edge. Power of two keeps edge vertices bit-exact between chunks.")]
        public int chunkResolution = 32;
        [Tooltip("Deepest quadtree level. Level L chunk edge ≈ (π/2·R)/2^L; 14 ≈ 610 m (≈19 m per quad on Earth).")]
        [Range(4, 20)] public int maxLevel = 14;
        [Tooltip("A chunk splits when the camera is closer than edgeLength × this factor.")]
        [Range(0.5f, 6f)] public float splitDistanceFactor = 2.2f;
        [Tooltip("Generate chunk geometry on worker threads (disable on platforms without threads, e.g. WebGL).")]
        public bool useWorkerThreads = true;
        [Range(1, 32)] public int maxConcurrentJobs = 6;
        [Tooltip("Mesh uploads to the GPU per frame (spreads load to avoid hitches).")]
        [Range(1, 64)] public int maxMeshUploadsPerFrame = 8;
        [Tooltip("Upper bound for cached chunk geometry not currently in use [MB].")]
        [Range(16, 4096)] public int cacheBudgetMegabytes = 256;
        [Tooltip("Skirt depth as a fraction of chunk edge length (hides cracks between LOD levels).")]
        [Range(0.001f, 0.1f)] public float skirtFraction = 0.015f;
    }

    [Serializable]
    public class ImportedModelSettings
    {
        [Tooltip("Prefab of the imported model (e.g. an FBX from Blender). Purely visual.")]
        public GameObject modelPrefab;
        [Tooltip("Radius of the model in its own units. The model is scaled so this matches the PHYSICAL radius — the model never defines the radius.")]
        [Min(1e-6f)] public float modelRadius = 1f;
        [Tooltip("Extra rotation to align the model's pole with the planet's spin axis (+Y in Unity model space = north).")]
        public Vector3 rotationOffsetEuler = Vector3.zero;
    }

    [Serializable]
    public class AtmosphereRenderSettings
    {
        public bool enabled = true;
        [Tooltip("Solar irradiance scale used by the scattering shader (tone-mapped afterwards).")]
        [Min(0f)] public float sunIntensity = 22f;
        [Tooltip("Exposure applied to scattered light before tone mapping.")]
        [Min(0f)] public float exposure = 1.0f;
        [Range(4, 64)] public int viewSamples = 16;
        [Range(2, 32)] public int lightSamples = 8;
    }

    [Serializable]
    public class LaunchSiteSettings
    {
        public string name = "Стартовая площадка 1";
        public double latitudeDegrees = 45.965;
        public double longitudeDegrees = 63.305;
        [Tooltip("Radius of the concrete pad [m] (visual).")]
        [Min(1f)] public float padRadius = 40f;
        [Tooltip("Terrain is flattened within this distance [m] and blends back over the same distance.")]
        public double flattenRadius = 600.0;
        [Tooltip("The pad is kept at least this high above sea level [m] (builds an islet if the site is at sea).")]
        public double minHeightAboveSeaLevel = 15.0;
    }
}
