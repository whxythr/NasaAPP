using System;
using SpaceSim.Mathematics;
using SpaceSim.Physics;
using SpaceSim.Planets;

namespace SpaceSim.Procedural
{
    public enum Biome : byte
    {
        DeepOcean,
        ShallowOcean,
        SeaIce,
        Beach,
        Desert,
        Grassland,
        Forest,
        Tundra,
        Rock,
        Snow
    }

    public struct SurfaceSample
    {
        /// <summary>Solid ground height above the reference radius [m] (may be below sea level).</summary>
        public double TerrainHeight;
        /// <summary>Physical surface height: max(terrain, sea level) when an ocean exists [m].</summary>
        public double SurfaceHeight;
        public bool IsWater;
        public ColorRGB Albedo;
        public Biome Biome;
    }

    /// <summary>
    /// Deterministic surface function: unit direction (planet-fixed) → height, biome, colour.
    /// Same settings + same seed + same algorithm version ⇒ same surface, independent of query order or
    /// thread. The whole planet is never generated at once; chunks sample this function on demand.
    ///
    /// Algorithm version 1: domain-warped fBm continents, ridged multifractal mountains masked to land,
    /// fBm detail, seeded impact craters, latitude/elevation/moisture biomes, flatten zones.
    /// </summary>
    public sealed class PlanetSurfaceSampler : ISurfaceHeightProvider
    {
        public const int CurrentAlgorithmVersion = 1;

        public static bool IsAlgorithmVersionSupported(int version) => version == 1;

        // Salts for independent noise streams. Changing any of them changes every planet → new algorithm version.
        const ulong SaltWarpX = 0x1001, SaltWarpY = 0x1002, SaltWarpZ = 0x1003, SaltContinent = 0x2001,
            SaltMountain = 0x3001, SaltMountainMask = 0x3002, SaltDetail = 0x4001, SaltMoisture = 0x5001,
            SaltClimateJitter = 0x5002, SaltCraters = 0x6001;

        struct Crater
        {
            public Vector3d Direction;
            public double AngularRadius; // chord length on the unit sphere ≈ angle for small craters
            public double Depth;          // [m]
        }

        struct Flat
        {
            public Vector3d Direction;
            public double ChordRadius;
            public double Height;
        }

        readonly PlanetGenerationSettings s;
        readonly double planetRadius;
        readonly GradientNoise3D warpX, warpY, warpZ, continent, mountain, mountainMask, detail, moisture, climateJitter;
        readonly Crater[] craters;
        readonly Flat[] flats;

        public static PlanetSurfaceSampler Create(PlanetGenerationSettings settings, double planetRadius)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (!IsAlgorithmVersionSupported(settings.algorithmVersion))
                throw new NotSupportedException(
                    "Surface algorithm version " + settings.algorithmVersion + " is not supported by this build " +
                    "(supported: " + CurrentAlgorithmVersion + "). Refusing to generate a different planet.");
            return new PlanetSurfaceSampler(settings, planetRadius);
        }

        PlanetSurfaceSampler(PlanetGenerationSettings settings, double radius)
        {
            s = settings.Clone(); // snapshot: later inspector edits cannot race with worker threads
            s.Sanitize();
            planetRadius = radius;
            ulong seed = (ulong)(uint)s.seed;

            warpX = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltWarpX));
            warpY = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltWarpY));
            warpZ = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltWarpZ));
            continent = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltContinent));
            mountain = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltMountain));
            mountainMask = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltMountainMask));
            detail = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltDetail));
            moisture = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltMoisture));
            climateJitter = new GradientNoise3D(DeterministicRandom.Mix(seed, SaltClimateJitter));

            var rng = new DeterministicRandom(DeterministicRandom.Mix(seed, SaltCraters));
            craters = new Crater[s.craterCount];
            double maxRim = 0.0;
            for (int i = 0; i < craters.Length; i++)
            {
                double u = rng.NextDouble();
                double r = s.craterMinRadius + (s.craterMaxRadius - s.craterMinRadius) * u * u * u; // many small, few large
                craters[i] = new Crater
                {
                    Direction = rng.UnitVector(),
                    AngularRadius = r / planetRadius,
                    Depth = 2.0 * s.craterDepthRatio * r
                };
                // Rim peak of CraterShape is 0.6·rimWidth² (+ smoothing margin), in units of Depth.
                maxRim = Math.Max(maxRim, craters[i].Depth * (0.6 * s.craterRimWidth * s.craterRimWidth + 0.1));
            }

            double maxCraterDepth = 0.0;
            for (int i = 0; i < craters.Length; i++) maxCraterDepth = Math.Max(maxCraterDepth, craters[i].Depth * 0.5);

            double contMax = (1.0 + Math.Abs(s.continentBias)) * Math.Max(s.continentAmplitude, 0.0);
            double contMin = (1.0 + Math.Abs(s.continentBias)) * Math.Max(s.oceanDepthAmplitude, 0.0);
            MaxTerrainHeight = contMax + Math.Abs(s.mountainAmplitude) + Math.Abs(s.detailAmplitude) + maxRim;
            MinTerrainHeight = -(contMin + Math.Abs(s.detailAmplitude) + maxCraterDepth);

            flats = new Flat[s.flattenZones.Length];
            for (int i = 0; i < flats.Length; i++)
            {
                var z = s.flattenZones[i];
                Vector3d dir = PlanetRotation.DirectionFromLatLon(z.latitudeDegrees * MathD.DegToRad, z.longitudeDegrees * MathD.DegToRad);
                double h = RawTerrainHeight(dir, out _);
                double floor = (s.hasOcean ? s.seaLevel : MinTerrainHeight) + Math.Max(0.0, z.minHeightAboveSeaLevel);
                flats[i] = new Flat { Direction = dir, ChordRadius = Math.Max(1.0, z.radius) / planetRadius, Height = Math.Max(h, floor) };
                MaxTerrainHeight = Math.Max(MaxTerrainHeight, flats[i].Height);
            }
        }

        public PlanetGenerationSettings Settings => s.Clone();
        public int Seed => s.seed;
        public int AlgorithmVersion => s.algorithmVersion;
        public double PlanetRadius => planetRadius;
        public double MinTerrainHeight { get; }
        public double MaxTerrainHeight { get; }

        public double MinSurfaceHeight => s.hasOcean ? Math.Max(MinTerrainHeight, s.seaLevel) : MinTerrainHeight;
        public double MaxSurfaceHeight => s.hasOcean ? Math.Max(MaxTerrainHeight, s.seaLevel) : MaxTerrainHeight;

        public double TerrainHeight(Vector3d unitDirection) => ApplyFlatten(unitDirection, RawTerrainHeight(unitDirection, out _));

        public double SurfaceHeight(Vector3d unitDirection)
        {
            double h = TerrainHeight(unitDirection);
            return s.hasOcean && h < s.seaLevel ? s.seaLevel : h;
        }

        public SurfaceSample Sample(Vector3d unitDirection)
        {
            double h = ApplyFlatten(unitDirection, RawTerrainHeight(unitDirection, out Vector3d warped));
            bool water = s.hasOcean && h < s.seaLevel;
            var sample = new SurfaceSample
            {
                TerrainHeight = h,
                SurfaceHeight = water ? s.seaLevel : h,
                IsWater = water
            };
            Shade(unitDirection, warped, h, water, ref sample);
            return sample;
        }

        double RawTerrainHeight(Vector3d p, out Vector3d q)
        {
            Vector3d wp = p * s.warpFrequency;
            q = p + new Vector3d(warpX.Fbm(wp, 3, 2.0, 0.5), warpY.Fbm(wp, 3, 2.0, 0.5), warpZ.Fbm(wp, 3, 2.0, 0.5)) * s.warpStrength;

            double c = continent.Fbm(q * s.continentFrequency, s.continentOctaves, s.lacunarity, s.persistence) + s.continentBias;
            double h = c >= 0.0 ? c * s.continentAmplitude : c * s.oceanDepthAmplitude;

            double land = MathD.SmoothStep(-0.02, 0.12, c);
            double mask = MathD.SmoothStep(0.02, 0.30, c) *
                          MathD.SmoothStep(-0.15, 0.35, mountainMask.Fbm(q * 1.3, 3, 2.0, 0.5));
            if (mask > 0.0)
            {
                double ridge = mountain.Ridged(q * s.mountainFrequency, s.mountainOctaves, s.lacunarity, 0.5);
                h += ridge * ridge * s.mountainAmplitude * mask;
            }

            if (s.detailOctaves > 0)
                h += detail.Fbm(q * s.detailFrequency, s.detailOctaves, s.lacunarity, 0.5) * s.detailAmplitude * (0.35 + 0.65 * land);

            for (int i = 0; i < craters.Length; i++)
            {
                ref readonly Crater cr = ref craters[i];
                double reach = cr.AngularRadius * (1.0 + s.craterRimWidth);
                // Cheap reject using the dot product before computing the chord length.
                double dot = Vector3d.Dot(p, cr.Direction);
                if (dot < 1.0 - 0.5 * reach * reach) continue;
                double x = (p - cr.Direction).Magnitude / cr.AngularRadius;
                h += CraterShape(x, s.craterRimWidth) * cr.Depth;
            }
            return h;
        }

        /// <summary>Bowl + raised rim, built from smooth min/max so it is continuous and exactly 0 outside the rim.</summary>
        static double CraterShape(double x, double rimWidth)
        {
            double cavity = x * x - 1.0;
            double rimX = Math.Min(x - 1.0 - rimWidth, 0.0);
            double rim = 0.6 * rimX * rimX;
            return MathD.SmoothMax(MathD.SmoothMin(cavity, rim, 0.25), -0.5, 0.25);
        }

        double ApplyFlatten(Vector3d p, double h)
        {
            for (int i = 0; i < flats.Length; i++)
            {
                ref readonly Flat f = ref flats[i];
                double d = (p - f.Direction).Magnitude;
                if (d >= 2.0 * f.ChordRadius) continue;
                double w = 1.0 - MathD.SmoothStep(f.ChordRadius, 2.0 * f.ChordRadius, d);
                h = MathD.Lerp(h, f.Height, w);
            }
            return h;
        }

        void Shade(Vector3d p, Vector3d q, double h, bool water, ref SurfaceSample sample)
        {
            // Climate scale 0 (polar) .. 1 (equatorial); p.z = sin(latitude) in the planet-fixed frame.
            double temp = 1.0 - p.z * p.z + 0.08 * climateJitter.Fbm(q * 3.0, 3, 2.0, 0.5)
                          - Math.Max(0.0, h - s.seaLevel) * s.elevationCooling;
            double wet = MathD.Clamp01(0.5 + 0.9 * moisture.Fbm(q * s.moistureFrequency, 4, 2.0, 0.5));

            if (water)
            {
                double depth = s.seaLevel - h;
                sample.Albedo = ColorRGB.Lerp(s.shallowOcean, s.deepOcean, MathD.SmoothStep(0.0, 2500.0, depth));
                sample.Biome = depth > 200.0 ? Biome.DeepOcean : Biome.ShallowOcean;
                double ice = MathD.SmoothStep(0.07, 0.02, temp);
                if (ice > 0.0)
                {
                    sample.Albedo = ColorRGB.Lerp(sample.Albedo, s.seaIce, ice);
                    if (ice > 0.5) sample.Biome = Biome.SeaIce;
                }
                return;
            }

            double elevation = h - s.seaLevel;
            ColorRGB c = s.grassland;
            Biome b = Biome.Grassland;

            double forestW = MathD.SmoothStep(0.52, 0.72, wet) * MathD.SmoothStep(0.25, 0.4, temp);
            c = ColorRGB.Lerp(c, s.forest, forestW);
            if (forestW > 0.5) b = Biome.Forest;

            double desertW = MathD.SmoothStep(0.38, 0.22, wet) * MathD.SmoothStep(0.45, 0.65, temp);
            c = ColorRGB.Lerp(c, s.desert, desertW);
            if (desertW > 0.5) b = Biome.Desert;

            double tundraW = MathD.SmoothStep(0.33, 0.2, temp);
            c = ColorRGB.Lerp(c, s.tundra, tundraW);
            if (tundraW > 0.5) b = Biome.Tundra;

            double rockW = MathD.SmoothStep(1800.0, 3200.0, elevation);
            c = ColorRGB.Lerp(c, s.rock, rockW);
            if (rockW > 0.5) b = Biome.Rock;

            double snowW = Math.Max(MathD.SmoothStep(0.12, 0.05, temp), MathD.SmoothStep(3900.0, 4800.0, elevation));
            c = ColorRGB.Lerp(c, s.snow, snowW);
            if (snowW > 0.5) b = Biome.Snow;

            if (s.hasOcean)
            {
                double beachW = 1.0 - MathD.SmoothStep(5.0, 45.0, elevation);
                if (beachW > 0.0 && temp > 0.15)
                {
                    c = ColorRGB.Lerp(c, s.beach, beachW);
                    if (beachW > 0.5) b = Biome.Beach;
                }
            }

            sample.Albedo = c;
            sample.Biome = b;
        }
    }
}
