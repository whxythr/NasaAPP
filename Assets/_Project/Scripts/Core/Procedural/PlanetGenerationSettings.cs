using System;
using SpaceSim.Mathematics;

namespace SpaceSim.Procedural
{
    /// <summary>A region flattened to a constant height (launch site, future bases).</summary>
    [Serializable]
    public struct FlattenZone
    {
        public double latitudeDegrees;
        public double longitudeDegrees;
        /// <summary>Fully flat inside this ground distance [m]; blends back to terrain over the same distance.</summary>
        public double radius;
        /// <summary>The flat area is kept at least this high above sea level [m].</summary>
        public double minHeightAboveSeaLevel;
    }

    /// <summary>
    /// Parameters of the deterministic surface generator. Together with <see cref="seed"/> and
    /// <see cref="algorithmVersion"/> they fully determine the surface. They describe appearance and relief
    /// only — mass, radius and gravity live in PlanetPhysicalProperties and are never touched by the seed.
    /// Frequencies are in cycles per planet radius (noise is sampled on the unit sphere).
    /// </summary>
    [Serializable]
    public class PlanetGenerationSettings
    {
        public int seed = 20251;
        /// <summary>Version of the surface algorithm these settings were created with. Never silently upgraded.</summary>
        public int algorithmVersion = PlanetSurfaceSampler.CurrentAlgorithmVersion;

        // ---- Continents ----
        public double continentFrequency = 1.1;
        public int continentOctaves = 6;
        public double lacunarity = 2.0;
        public double persistence = 0.5;
        /// <summary>Shifts the land/ocean balance; negative → more ocean.</summary>
        public double continentBias = -0.04;
        /// <summary>Land height per unit of continental noise [m].</summary>
        public double continentAmplitude = 5000.0;
        /// <summary>Ocean depth per unit of continental noise [m].</summary>
        public double oceanDepthAmplitude = 9000.0;
        public double warpFrequency = 1.6;
        public double warpStrength = 0.12;

        // ---- Mountains ----
        public double mountainFrequency = 3.2;
        public int mountainOctaves = 7;
        public double mountainAmplitude = 5200.0;

        // ---- Detail ----
        public double detailFrequency = 180.0;
        public int detailOctaves = 9;
        public double detailAmplitude = 160.0;

        // ---- Ocean ----
        public bool hasOcean = true;
        /// <summary>Sea level relative to the reference radius [m].</summary>
        public double seaLevel = 0.0;

        // ---- Craters ----
        public int craterCount = 24;
        public double craterMinRadius = 3000.0;
        public double craterMaxRadius = 45000.0;
        /// <summary>Floor depth as a fraction of crater radius.</summary>
        public double craterDepthRatio = 0.06;
        public double craterRimWidth = 0.45;

        // ---- Climate / biomes ----
        /// <summary>Temperature drop per metre of elevation, in units of the 0..1 climate scale.</summary>
        public double elevationCooling = 0.00008;
        public double moistureFrequency = 2.4;
        public ColorRGB deepOcean = new ColorRGB(0.015f, 0.05f, 0.16f);
        public ColorRGB shallowOcean = new ColorRGB(0.04f, 0.17f, 0.30f);
        public ColorRGB seaIce = new ColorRGB(0.80f, 0.85f, 0.90f);
        public ColorRGB beach = new ColorRGB(0.72f, 0.66f, 0.48f);
        public ColorRGB desert = new ColorRGB(0.76f, 0.63f, 0.42f);
        public ColorRGB grassland = new ColorRGB(0.28f, 0.40f, 0.16f);
        public ColorRGB forest = new ColorRGB(0.11f, 0.24f, 0.09f);
        public ColorRGB tundra = new ColorRGB(0.42f, 0.41f, 0.35f);
        public ColorRGB rock = new ColorRGB(0.36f, 0.33f, 0.30f);
        public ColorRGB snow = new ColorRGB(0.90f, 0.92f, 0.95f);

        // ---- Atmosphere & clouds (visual) ----
        /// <summary>Multiplies the Rayleigh colour of the sky (1,1,1 = physical coefficients unchanged).</summary>
        public ColorRGB atmosphereTint = new ColorRGB(1f, 1f, 1f);
        /// <summary>0..1 cloud cover parameter, stored for the future cloud layer.</summary>
        public double cloudCoverage = 0.45;

        public FlattenZone[] flattenZones = new FlattenZone[0];

        public PlanetGenerationSettings Clone()
        {
            var c = (PlanetGenerationSettings)MemberwiseClone();
            c.flattenZones = flattenZones != null ? (FlattenZone[])flattenZones.Clone() : new FlattenZone[0];
            return c;
        }

        /// <summary>Clamps values to ranges the generator supports (protects against inspector typos).</summary>
        public void Sanitize()
        {
            continentOctaves = Clamp(continentOctaves, 1, 16);
            mountainOctaves = Clamp(mountainOctaves, 1, 16);
            detailOctaves = Clamp(detailOctaves, 0, 16);
            craterCount = Clamp(craterCount, 0, 2000);
            lacunarity = MathD.Clamp(lacunarity, 1.1, 4.0);
            persistence = MathD.Clamp(persistence, 0.05, 0.95);
            craterMinRadius = Math.Max(1.0, craterMinRadius);
            craterMaxRadius = Math.Max(craterMinRadius, craterMaxRadius);
            if (flattenZones == null) flattenZones = new FlattenZone[0];
        }

        static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
    }
}
