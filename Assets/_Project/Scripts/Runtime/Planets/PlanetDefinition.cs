using System;
using System.Collections.Generic;
using SpaceSim.Physics;
using SpaceSim.Planets;
using SpaceSim.Procedural;
using UnityEngine;

namespace SpaceSim.Runtime
{
    /// <summary>
    /// Data asset describing one planet. Physical properties, visual generation and rendering parameters are
    /// separate sections: changing the visual seed or the imported model never changes mass, radius or gravity.
    /// Create via Assets → Create → SpaceSim → Planet Definition.
    /// </summary>
    [CreateAssetMenu(menuName = "SpaceSim/Planet Definition", fileName = "PlanetDefinition")]
    public sealed class PlanetDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string planetId = "earth-proto";
        public string displayName = "Земля (прототип)";

        [Header("Physics (SI units)")]
        public PlanetPhysicalProperties physical = new PlanetPhysicalProperties();
        [Tooltip("0 = physical data authored by hand; otherwise version of PlanetPhysicalGenerator that produced it.")]
        public int physicalGeneratorVersion;
        public int physicalSeed;

        [Header("Surface")]
        public PlanetSurfaceSource surfaceSource = PlanetSurfaceSource.Procedural;
        public PlanetGenerationSettings generation = new PlanetGenerationSettings();
        public ProceduralLodSettings lod = new ProceduralLodSettings();
        public ImportedModelSettings importedModel = new ImportedModelSettings();

        [Header("Atmosphere optics (visual + sunlight transmittance)")]
        public AtmosphereOpticalProperties optics = new AtmosphereOpticalProperties();
        public AtmosphereRenderSettings atmosphereRendering = new AtmosphereRenderSettings();

        [Header("Launch site")]
        public LaunchSiteSettings launchSite = new LaunchSiteSettings();

        /// <summary>Raised when the asset is edited in the Inspector (also during Play mode).</summary>
        public event Action Changed;

        [Tooltip("Keep generation.flattenZones[0] in sync with the launch site, so the pad is part of the deterministic (saved) surface.")]
        public bool flattenLaunchSite = true;

        void OnEnable() => SyncLaunchSiteZone();

        void OnValidate()
        {
            if (generation == null) generation = new PlanetGenerationSettings();
            generation.Sanitize();
            SyncLaunchSiteZone();
            Changed?.Invoke();
        }

        /// <summary>Notifies listeners after a change made from code.</summary>
        public void NotifyChanged() => Changed?.Invoke();

        void SyncLaunchSiteZone()
        {
            if (!flattenLaunchSite || generation == null || launchSite == null) return;
            var zone = new FlattenZone
            {
                latitudeDegrees = launchSite.latitudeDegrees,
                longitudeDegrees = launchSite.longitudeDegrees,
                radius = launchSite.flattenRadius,
                minHeightAboveSeaLevel = launchSite.minHeightAboveSeaLevel
            };
            var zones = new List<FlattenZone>(generation.flattenZones ?? new FlattenZone[0]);
            if (zones.Count == 0) zones.Add(zone);
            else zones[0] = zone;
            generation.flattenZones = zones.ToArray();
        }

        /// <summary>Snapshot of the generation settings (includes the launch-site flatten zone).</summary>
        public PlanetGenerationSettings BuildGenerationSettings()
        {
            SyncLaunchSiteZone();
            return generation.Clone();
        }

        /// <summary>Replaces the physical properties with a seeded, self-consistent generated set.</summary>
        public void GeneratePhysicalProperties(int seed, PlanetPhysicalGenerationRanges ranges = null)
        {
            physical = PlanetPhysicalGenerator.Generate(seed, ranges);
            physicalSeed = seed;
            physicalGeneratorVersion = PlanetPhysicalGenerator.AlgorithmVersion;
            NotifyChanged();
        }

        /// <summary>Default Earth-like prototype planet (used when no asset is assigned).</summary>
        public static PlanetDefinition CreateEarthPrototype()
        {
            var d = CreateInstance<PlanetDefinition>();
            d.name = "Earth (runtime default)";
            return d;
        }
    }
}
